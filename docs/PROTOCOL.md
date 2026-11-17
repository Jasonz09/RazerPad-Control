# Razer Laptop Cooling Pad - Reverse-Engineered Protocol

Device: USB VID_1532 PID_0F43, composite. Control + LED on interface MI_00 (HID
feature-report collection, FeatureReportByteLength = 91). Internally Razer/Synapse calls
this protocol "rzDevice25". Source: extracted from Razer Synapse middleware JS
(mw_main.js, morluto/rea style) + verified live on the device.

## 91-byte feature report layout (wire; byte 0 = report ID 0x00)
  [0]     reportId 0x00
  [1]     status (0x00 in a request, 0x02 for a successful lighting reply)
  [2]     transactionId (reply echoes it)
  [6]     data length (depends on the command class)
  [7]     classId (0x0D thermal, 0x0F Chroma extended matrix)
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

## Chroma extended matrix (class 0x0F)

Use transaction ID `0x1F`. The data length is the number of argument bytes.

| Command | Arguments |
| --- | --- |
| `0x03` (frame) | `00 00 00 00 11` followed by 18 RGB triples |
| `0x02` (activate custom frame) | `00 00 08` followed by nine zero bytes |
| `0x02` (lighting off) | `01 00 00 00 00 00` |
| `0x04` (master brightness) | `01 00 <0–255>` |
| `0x84` (read master brightness) | `01 00 00`. The reply returns the value in its third argument. |

The pad echoed the arguments and reported status `0x02` for these commands.
The previous class 3 matrix requests returned `0x05` (unsupported) on this pad.
The app sets master brightness to 255 on connection and applies the effective
brightness to the RGB values it sends. Live previews use the acknowledged frame.
The custom editor draws its draft separately.

[OpenRazer cooling pad support and packet builders](https://github.com/openrazer/openrazer/pull/2798/files)

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
0x82/0x04/0x83) with the full XOR checksum. Lighting uses the Chroma protocol above.
---

## Note (v4/v5 client architecture)

The fan worker and LED animator serialize HID transactions behind a single
process-wide lock. Lighting verifies the status, transaction, command, payload
and checksum before publishing a frame to the preview. Still frames refresh
periodically. Fan speed and mode reads continue to check the reply class and
command.
