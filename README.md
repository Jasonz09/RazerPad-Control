# Razer Laptop Cooling Pad - open-source controller + dashboard (RTX Spark / ARM64)

Razer Synapse cannot drive the Razer Laptop Cooling Pad on this machine, because it does
not support the NVIDIA RTX Spark (GB10 / ARM64) host for the automatic fan page: it cannot
read the hardware temperature, so the pad just sits in its onboard default.

This project talks to the pad **directly over HID** (reverse engineered from Synapse) and
adds a **system-tray app** that controls the fan and the 18-LED light bar from host CPU/GPU
temperatures. It runs side by side with Synapse (no admin required).

* Device: USB `VID 1532 / PID 0F43`, control interface **MI_00**, 91-byte HID feature report
* Real device commands verified on hardware - see `PROTOCOL.md`
* Prior art that helped: `github.com/Benson5650/FanControl.RazerCoolingPadPlugin`, `github.com/morluto/rea`

---

## 1. The tray app (this is what runs)

`RazerPadTray.exe` lives in the notification area. Its icon is a coloured disc that shows the
current temperature (green when cool, red when hot) and the tooltip shows live CPU / GPU / fan RPM.

Everything is configured by clicking the tray icon:

| Menu | Options |
|---|---|
| **Fan profile** | Quiet (cap 2100 rpm) - Balanced (default) - Performance - Turbo (constant max) ; Turbo burst for 2 minutes ; Auto-turbo when very hot ; Protect fan cap ; Steady speed ; Wind down on exit ; Max performance (HyperBoost) |
| **Lighting** | Heatmap (temp) - Temperature meter (bar graph) - Wave (flowing, temp-tinted) - **Load meter (CPU bar left / GPU bar right)** - Spectrum cycle - Breathing (speed follows temperature) - Rainbow - Static color (White/Green/Cyan/Blue/Red/Purple/Orange/Pink) - Off |
| **Brightness** | 25 / 50 / 75 / 100 % ; Dim automatically when idle/cool ; Dim automatically at night (22:00-07:00) |
| **Pause / Resume automation** | hands control back to you, freezes the loop |
| **Exit** | stops the app, restores the fan to a quiet rest speed, puts the pad back into onboard Auto mode, turns the LEDs off |

**Every choice is saved** to `tray.cfg` next to the exe and reloaded on the next launch, so the
pad comes back exactly how you left it.

### Automation behaviour (what runs in the background every 2 seconds)

1. Read **CPU** (Windows `Thermal Zone Information` performance counter) and **GPU** (`nvidia-smi`).
2. Drive the fan from the selected profile curve against `max(CPU, GPU)`.
3. **Smooth ramp + steady-speed filter (anti-flap)** - the fan never jumps: each update is limited
   to `maxstep` fan-units, and on top of that a small **deadband** (`deadband`, default ~100 rpm) is
   ignored and downward moves only happen after the lower target has **held** for `downhold` loops
   (default 3). Net effect: the fan holds one constant speed and only eases down after a sustained
   drop in demand, instead of pumping up and down every second. Upward moves stay prompt (cool first).
4. **Auto-turbo** - if the temperature stays very hot (5 degC below the critical threshold) for
   ~8 seconds, the fan goes to full speed until it has cooled back down by 10 degC.
5. **Critical overheat** - at/above `critical` (default 85 degC) the fan is pinned to maximum and
   the light bar blinks red, whatever lighting mode is selected.
   5b. **Fan-wear cap** - while the hardware is **cool** (below `captemp`, default 60 degC) the fan is
   held at or below `rpmcap` (default **2300 rpm**) to protect the bearing. As soon as temperature
   reaches `captemp` (or critical / turbo), the cap lifts and the curve decides the real speed, so
   cooling under load is never throttled. Set `rpmcap=0` (or toggle it in the menu) to disable.
   5c. **Wind-down on exit** - on Exit the fan is NOT switched off abruptly. It steps down gradually
   and keeps running until the hardware has actually cooled below `cooltemp` (default 45 degC),
   then settles to a gentle idle and hands control back to the pad onboard Auto mode. Toggle:
   "Wind fan down gently on exit" (or `cooltail`).
6. **Synapse protection** - Synapse periodically steals manual fan control. The app re-checks the
   fan mode and re-asserts manual mode every 10 loops, so it keeps working even with Synapse open.
7. **Lighting** - drives the 18 LEDs, with automatic brightness (dim when the laptop is idle/cool,
   optionally dim at night).
8. **Hot-plug** - if the pad is unplugged it retries, and re-initialises automatically when plugged
   back in. Three consecutive write failures also trigger a clean device reopen.
9. Writes a live one-line status to `status.txt` (next to the exe by default).
10. **Live config file** - `tray.cfg` is watched; external edits are re-read automatically.
11. Lighting runs on its own ~25 fps thread; `nvidia-smi` sampling runs on its own thread;
    the 2-second fan loop reads their cached values, so all three stay responsive.

### Single instance

A named mutex makes sure only one tray controller runs. Launching a second copy exits silently
instead of fighting the first one for the device.

## 2. Build from source

```
git clone https://github.com/YOURNAME/RazerCoolingPad-Control.git
cd RazerCoolingPad-Control
build.cmd            ''-> produces RazerPadFan.exe (no Visual Studio needed; uses the Windows-bundled csc)
RazerPadFan.exe      ''starts the tray app and pops the dashboard
```

Runtime: Windows 10/11 (any architecture - it is pure .NET Framework 4.x), and the pad plugged
in over USB. No admin rights required. The C# source is a single file (`src\RazerPadFan.cs`).

## 3. Files on disk

```
C:\Users\zhaoF\RazerPadFan\
  RazerPadTray.exe     the tray app (this is what you run)
  RazerPadTray.cs      its source
  tray.cfg             your saved settings (written by the tray menu; live-edited OK)
  status.txt           live status, updated every loop
  crash.log            error / hot-reload log (auto-trimmed)
  Start-RazerPad.cmd   start the tray if it is not already running
  Stop-RazerPad.cmd    stop it
  autostart.cfg        settings for the OLD command-line controller (unused by the tray)
  RazerPadFan.exe/.cs  the original CLI controller, kept as a fallback / reference
  PROTOCOL.md          the reverse-engineered wire protocol
```

## 4. Autostart

`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\RazerPadFan.vbs` launches the tray
hidden at every logon:

```vbs
Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = "C:\Users\zhaoF\RazerPadFan"
sh.Run """C:\Users\zhaoF\RazerPadFan\RazerPadTray.exe"" --interval 2000 --status-file ""C:\Users\zhaoF\RazerPadFan\status.txt""", 0, False
```

To disable: delete that `.vbs` (or Shift-right-click the tray icon and Exit).

## 5. Command-line flags (all optional; they override `tray.cfg`)

```
RazerPadTray.exe [flags]

  --profile NAME        quiet | balanced | performance | turbo
  --led MODE            heatmap | level | wave | spectrum | breath | rainbow | static | off
  --ledcolor NAME|HEX   colour used by static / breathing
  --ledbri N            brightness 0-100
  --interval MS         control-loop period (default 2000, min 500)
  --maxstep N           max fan-units changed per loop (0 = instant, no smoothing)
  --critical C          overheat threshold in degC (default 85)
  --noautoturbo         disable the sustained-heat turbo behaviour
  --idledim / --nightdim   force the automatic brightness options on
  --rpmcap N            fan-wear cap in rpm while cool (default 2300; 0 disables)
  --captemp C           temp (degC) at/above which the wear cap lifts (default 60)
  --deadband N          ignore target changes smaller than N fan-units (~100rpm/each)
  --downhold N          loops a lower target must hold before the fan slows (default 3)
  --cooltemp C          wind-down exit target temp in degC (default 45)
  --nocooltail          stop the fan immediately on exit (no gradual wind-down)
  --hyperboost          enable Max-performance (HyperBoost): disable PCIe ASPM + USB selective suspend on AC (restored on exit)
  --nostabilize         disable the steady-speed anti-flap filter (deadband+downhold)
  --keep                on exit, leave the fan speed and mode as they are
  --status-file PATH    write live status to PATH
  --status              print device + sensor status and exit (no tray icon)
```

Example - read the pad without touching it:

```
C:\Users\zhaoF\RazerPadFan\RazerPadTray.exe --status
CPU=32.6C GPU=47C fanUnit=24 (~1200rpm)
```

## 6. Facts learned about the pad

* **There is no temperature sensor in the pad.** Auto mode must use host sensors.
* **Fan scale**: raw unit 10..64, `RPM = unit x 50`, so the physical fan is ~500-3200 RPM.
  An earlier build assumed a phantom 1000-6000 range and therefore sat at full speed from ~65 degC up.
* **The LEDs are one 18-LED matrix** driven with a single "user effect" command, so effects
  (including per-LED custom frames) are possible even though Synapse offers nothing here.
* The fan physically ramps; read-back lags the command by a few seconds. That is normal.
* A `HidP_GetCaps` capability buffer must be 256 bytes; a too-small buffer silently corrupts
  the native heap and crashes the process with `0xC0000374`.

## 7. Rebuild

Only the .NET Framework 4.x compiler is needed (ships with Windows, works on ARM64):

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe ^
  /out:RazerPadTray.exe /reference:System.dll ^
  /reference:System.Windows.Forms.dll /reference:System.Drawing.dll RazerPadTray.cs
```

Compile to a **new file name** and copy it over `RazerPadTray.exe` after stopping the running
instance - a live process locks its own exe.

## 8. Version history

* **v1** - CLI, correct device addressing, wrong RPM scale, fixed-speed steps.
* **v2** - heap-overflow fix, true 500-3200 RPM scale, graduated curves, 18-LED lighting
  (heatmap/static/spectrum/breath/rainbow), hysteresis, critical handling, config file, autostart.
* **v3** - system-tray app: live temp icon and tooltip, menu-driven profiles, three
  new lighting effects (temperature meter, temp-tinted wave, rainbow), brightness presets plus
  automatic idle/night dimming, auto-turbo on sustained heat, manual "turbo burst", smooth
  fan ramping, settings persistence, single-instance guard, hot-plug recovery.
* **v4** - performance + automation upgrades:
  - `nvidia-smi` moved to a dedicated background GPU thread (the 2-second fan loop never
    blocks on the ~70 ms process spawn anymore); GPU utilisation is read in the same call.
  - Dedicated LED thread at ~25 fps with fire-and-forget HID writes (~1 ms per frame, no
    read-wait), so wave/rainbow/breath animate smoothly instead of stepping.
  - Brightness is now baked into the RGB matrix values (one write) instead of 18 per-LED calls.
  - New effect: **Load meter** - left half of the bar shows CPU load, right half shows GPU
    load, colour still follows temperature.
  - Breathing speed now follows temperature (calm when cool, fast pulse when hot).
  - **Load-aware pre-emptive fan boost**: if CPU or GPU utilisation goes over 85/60/35 %,
    the fan floor is raised immediately (max / 2300 / 1900 rpm) *before* the temperature
    has time to climb. Toggle in the menu ("Boost fan ahead of heavy load").
  - All HID access serialised behind one lock - fixes a race where the LED thread and the
    fan worker collided on the shared device handle (status showed unit=-1).
* **v5** - stability + quality-of-life:
  - **Config hot-reload**: edit `tray.cfg` in place (any text editor) and the running app
    picks up profile / led / bri / critical / maxstep / toggles within ~1 second. No restart.
    The menu still writes the same file, so both ways work.
  - `led=static:red` (combined mode:colour syntax) is now accepted in `tray.cfg`.
  - Crash logging to `crash.log` next to the exe (AppDomain + UI-thread handlers); a fault
    on a background thread is now caught and logged instead of silently killing the app.
  - Tray icon only redraws when the temperature digit actually changes, and the GDI icon
    handle is destroyed properly (no more per-second GDI leak).
  - Fixed cleanup-on-exit flag bug (`_run`), so Exit shuts the threads down correctly.
* **v6** - fan behaviour refinements you asked for:
  - **Fan-wear cap** (`rpmcap` default 2300, lifts at `captemp` default 60 degC): hold the fan at a
    gentle 2300 rpm while the laptop is cool to spare the bearing, and let it rise freely when hot.
  - **Steady speed / anti-flap**: a `deadband` (~100 rpm) plus an asymmetric **down-hold** so the
    fan holds a constant speed and only slows after a sustained need, instead of pumping every second.
  - **Wind-down cool tail on exit** (`cooltail` + `cooltemp` 45): gradually ramp the fan to a stop and
    only release to onboard Auto once the hardware is genuinely cool, so it does not stop hot.
  - All three toggleable from the tray menu and configurable in `tray.cfg` / CLI flags.

* **v7** - HyperBoost-style max-performance toggle:
  - **Max performance (HyperBoost)** toggle (also `hyperboost=` in `tray.cfg` / `--hyperboost` flag).
    On the RTX Spark (GB10 / ARM64) the GPU's own TGP/clocks are firmware-managed and locked -
    `nvidia-smi` exposes no power limit you can raise (it reports N/A and rejects `-pl`/`-lgc`), and the
    CPU boost/PL controls are likewise absent. So Razer Synapse's "HyperBoost" button (which wraps
    NVIDIA Dynamic Boost / TGP) has nothing to switch here. The only real, OS-level performance lever
    left is **Windows power policy**, which is what this toggle flips:
      - disables **PCIe ASPM** (Active State Power Management) on AC - stops the GPU link from dropping
        into power-saving states under bursty load, the main GPU-throughput throttle on this platform;
      - disables **USB selective suspend** on AC - keeps the pad (and other USB) from suspending.
  - **Safe and fully reversible, no admin needed.** It only touches the AC (plugged-in) values of the
    current power scheme. Turn the toggle off, or close the app from its tray menu, and both settings
    are restored to your Windows defaults automatically. It never edits the DC/battery settings.
  - Defaults to **off**, so installing this build does not silently change your power plan; flip it on
    when you want maximum sustained GPU throughput and accept the slightly higher idle power draw.

* **Compact Slate (current)** - a single-screen dashboard with CPU/GPU temperatures, commanded fan
  speed, Lighting, and HyperBoost. Quiet, Balanced, and Performance show Fan steadiness. Manual
  shows a 500–3,200 RPM target, and Turbo holds 3,200 RPM. The two-minute Turbo Burst runs independently
  of Auto-Turbo, and critical-temperature protection takes priority in every fan profile.

* **v8** - the **dashboard app** and the **anti-hunting fan controller**:
  - A real Synapse-style **dashboard window** (open from the tray - Open dashboard): live CPU/GPU
    temperature and fan-RPM tiles, profile picker, lighting picker, brightness, a **Fan steadiness**
    slider, every toggle, and Start-with-Windows - all in one place.
  - **Closed-loop PID + EMA + high-water-mark coast** controller so the fan holds ONE constant speed
    instead of pumping up and down every few seconds (see the Stability section below). This was the
    main thing you asked for.
  - **Fan steadiness** slider (smooth) that tunes deadband / down-hold / coast / sensor-filter as one knob.
  - **Write-on-change** fan control (only re-sends the HID command when the speed actually changes, plus a
    15s keep-alive), so Synapse and sensor noise stop re-triggering the pad.
  - Per-user **Start with Windows** via the `HKCU\...\Run` key (no admin, replaces the old Startup `.vbs`).
### crash.log

If something unexpected happens, look at `C:\Users\zhaoF\RazerPadFan\crash.log`.
Every hot-reload is recorded there too, so you can confirm config edits landed.

---

## 9. Stability: how the fan avoids hunting (v8)

A naive temperature-curve controller oscillates: the fan speeds up, over-cools, the temperature
dips so the fan slows, the temperature rebounds so it speeds up again - visible every few seconds.
That up-and-down pumping is exactly what wears a fan bearing out, so this controller has four
layers to stop it:

1. **EMA sensor filter** - the raw CPU/GPU reading (which jitters by a degree or two constantly,
   especially the GPU) is low-pass filtered. In *Steady* mode the filter time constant is ~17s, so
   transient jitter never reaches the fan at all.
2. **Closed-loop PID on a temperature setpoint** - instead of blindly following an open-loop curve,
   the controller aims at a target temperature (Quiet 80C, Balanced 75C, Performance 68C, Turbo 60C).
   The curve is the feed-forward estimate; an integral term slowly trims the fan until the
   temperature *holds steady*. Once it holds, the integral stops moving and the fan sits at **one
   constant speed**. The derivative term is only active while the hardware is *warming*, so it
   reacts early and damps overshoot but never pumps on the way down.
3. **High-water-mark coast controller** - the fan climbs promptly when it gets hot but only eases
   *down* one unit every coast seconds (default 15s), and freezes entirely inside the deadband.
   A short-lived temperature dip can never pull the speed down prematurely.
4. **Write-on-change** - the HID command is only re-sent when the speed actually changes (plus a
   15s keep-alive heartbeat), so Synapse chatter and sensor noise don't re-trigger the pad.

Measured on hardware: with the GPU idling and bouncing 45-60C on its own, the fan holds a single
speed for 20-30s at a time and ramps down one 50-rpm step at a time, instead of pumping.

### Tuning steadiness

The dashboard has a **Fan steadiness** slider (or 	ray.cfg smooth=0..100):

| | Reactive (0) | Steady (default 45) | Locked (100) |
|---|---|---|---|
| deadband (ignore changes under) | 50 rpm | ~175 rpm | 300 rpm |
| hold before slowing | ~2s | ~6s | 12s |
| coast-down per step | 5s | ~21s | 40s |
| sensor filter | fast | slow | slowest |

If you still see slow oscillation, raise the slider (more steady). If the pad feels sluggish under
load, lower it (more reactive).

## 10. New flags added in v8

```
  --smooth N          steadiness 0 (reactive) .. 100 (locked); sets deadband/downhold/coast/filter together
  --coast N           seconds between each single-step fan reduction while cooling
  --open              show the dashboard on launch (default when started by hand)
  --minimized         start in the tray only, do not show the dashboard
```

"Start with Windows" in the tray/dashboard uses the per-user HKCU\...\Run key, so it needs no admin.