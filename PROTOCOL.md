# Razer Laptop Cooling Pad - Reverse-Engineered Protocol

Device: USB VID_1532 PID_0F43, composite. Control + LED on interface MI_00 (HID
feature-report collection, FeatureReportByteLength = 91). Internally Razer/Synapse calls
this protocol "rzDevice25". Source: extracted from Razer Synapse middleware JS
(mw_main.js, morluto/rea style) + verified live on the device.

## 91-byte feature report layout (wire; byte 0 = report ID 0x00)
  [0]     reportId 0x00
  [2]     transactionId (0..31, auto-increment; reply echoes it)
  [6]     payload total length = 3 + payloadLen
  [7]     classId   (13 = THERMAL, 3 = LED)
  [8]     commandId
  [9..]   payload data
  [89]    checksum = XOR of bytes [3..88]
  total   91 bytes (HidD_SetFeature to send / HidD_GetFeature to read reply)
Reply: after SetFeature, wait 100-400 ms then HidD_GetFeature. The reply echoes classId
at [7] and commandId at [8] (the real success test); reply payload = bytes [9 .. 9+len).
Select the right HID collection by opening each 1532:0F43 path and keeping the one whose
HidP caps FeatureReportByteLength == 91.

## Thermal class (classId 13)
  0x80 Get Fan Id List    req: (len 80)              rep: total, ids...  (this pad: 1 fan, id 5)
  0x81 Get Fan Speed       req: 00 05                 rep: 00 05 <speed>   (<speed> = raw)
  0x01 Set Fan Speed       req: 00 05 <speed>         rep: echo
  0x82 Get Fan Mode        req: 00 05                 rep: 00 05 <mode>    (0=Auto 1=Manual)
  0x04 Set Fan Mode        req: 00 05 <mode> 00 (len 80)
  0x83 Get Fan Info        req: 00 05 (len 80)        rep: gear ladder (see below)
  0x85 Get Thermal Reading req: (len 80)              rep: total=0 (NO onboard sensor!)

GetFanInfo (0x83) returns the supported gear ladder 0a 20 2c 36 3c 40 = raw
{10,32,44,54,60,64}. Continuous raw values between gears are accepted and ramp smoothly.

## Speed units  ->  RPM = raw * 50
Real physical range: raw 10 (=500 RPM) .. raw 64 (=3200 RPM).
(v1 of this tool wrongly documented raw 10..60 as 1000..6000 RPM; factor 50 makes that
 impossible. The correct ceiling is 64 = 3200 RPM. Confirmed live: GetFanInfo ladder tops
 at 0x40=64, and a Set of 64 is echoed/accepted.)

## LED class (classId 3)  -  18-LED light bar
  0x01 SET_RGB       req: 03 <id> <r> <g> <b>
  0x03 SET_BRIGHT    req: 03 <id> <bri0..100>
  0x0B SET_MATRIX   req: [<effect>, params...]   (NO leading class byte; starts at payload[0])
  Effects (matrix byte 0): 0=OFF 1=WAVE 2=AFTERGLOW 3=BREATH 4=SPECTRUM 5=CUSTOMFRAME 6=STATIC
    Static     = [6, r, g, b]
    Breathing  = [3, 1, r, g, b]
    Spectrum   = [4]
    Off        = [0]
    CustomFrame= [5, R0,G0,B0 ... R17,G17,B17]  (len 55, per-LED RGB - used for heatmap)
  All 18 LEDs are addressed as one matrix for the effect modes.

## RazerPadFan v2 behaviour
- Fan curve = list of {temperature, RPM} points, linear interpolation; below the first
  point it interpolates from an implicit idle temp (20C CPU / 30C GPU). deviceSpeed=floor(RPM/50).
- Default curve is graduated across the REAL range: 45C=1000 55C=1500 65C=2000
  75C=2500 85C=2900 95C=3200 rpm. (v1 used 45..95 = 1000..6000, which maxed the fan
  out from ~65C and wasted the graduated headroom.)
- --hysteresis/--minhold smooth small steps; --critical forces raw 64 + LED blink;
  --boost forces raw 64 for N loops; Synapse is told nothing - the tool just re-asserts
  manual mode every ~10 loops because Synapse steals it.

## Why Synapse cannot auto-control it on this laptop
Synapse maps supported laptops via its device manifest DB; the NVIDIA RTX Spark (GB10,
ARM64) thermal zone is unknown to it, so its Thermal page has no valid host sensor source.
The pad itself works fine - only the temperature feed is missing. RazerPadFan supplies
that feed from nvidia-smi (GPU) + the "Thermal Zone Information\_TZ.TSOC\Temperature" perf
counter (CPU).

## Bug history (hard-won)
- FindDevicePath originally allocated 16 bytes for the HidP_CAPS struct but HidP_GetCaps
  writes the full ~64-byte struct. This overflowed the native heap on every run and caused
  intermittent 0xC0000374 (heap corruption) crashes, usually fatal when the corruption
  landed on live metadata (i.e. during longer HID-heavy runs). Fix: allocate 256 bytes.

## Cross-reference: github.com/Benson5650/FanControl.RazerCoolingPadPlugin
Also 91-byte reports, reportId 0, checksum, factor 50. He uses a simplified curve control
(class13 cmd 0x10, fixed payload, one-byte shortcut checksum RPM_lo^0x0B) and correctly
assumes the 500-3200 RPM range. This tool uses the actual Synapse commands (0x01/0x81/
0x82/0x04/0x83) with the full XOR checksum, plus the LED class (3) that his plugin does not
drive. Reference for the LED/matrix command shapes: Razer rzDevice25 middleware + morluto/rea.
---

## Note (v4/v5 client architecture)

The wire protocol above is unchanged, but the client now uses three threads
(fan worker / GPU sampler / LED animator). All HID feature-report calls
(SetFeature/GetFeature on the shared device handle) are serialised behind a
single process-wide lock. LED animation frames use fire-and-forget
SetFeature with no matching GetFeature read - measured ~1 ms per 91-byte
frame (~1000 fps possible), which is what makes smooth 25 fps effects safe.
Fan speed/mode reads still verify the reply (classId/cmd echo).
