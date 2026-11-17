# Razer Laptop Cooling Pad - open-source controller + dashboard (RTX Spark / ARM64)

## Launch on Windows

Double-click [Start-RazerPad.cmd](Start-RazerPad.cmd). It builds the app on first launch.
If an older RazerPad tray app is running, exit it from its tray menu first. To reopen
the dashboard, right-click the RazerPad icon by the Windows clock and select **Open dashboard**.

Razer Synapse cannot drive the Razer Laptop Cooling Pad on this machine, because it does
not support the NVIDIA RTX Spark (GB10 / ARM64) host for the automatic fan page: it cannot
read the hardware temperature, so the pad just sits in its onboard default.

This project talks to the pad **directly over HID** (reverse engineered from Synapse) and
adds a **system-tray app** that controls the fan and the 18-LED light bar from host CPU/GPU
temperatures. It runs side by side with Synapse (no admin required).

* Device: USB `VID 1532 / PID 0F43`, control interface **MI_00**, 91-byte HID feature report
* Real device commands verified on hardware - see [the protocol notes](docs/PROTOCOL.md)
* Prior art that helped: `github.com/Benson5650/FanControl.RazerCoolingPadPlugin`, `github.com/morluto/rea`

---

## 1. The tray app (this is what runs)

`bin/RazerPadFan.exe` lives in the notification area. Its icon is a coloured disc that shows the
current temperature (green when cool, red when hot) and the tooltip shows live CPU / GPU / fan RPM.

Open the dashboard for the full interface, or use the tray icon for quick controls.

| Menu | Options |
|---|---|
| **Fan mode** | Automatic, Quiet, or Manual |
| **Device settings** | Fan wind-down on exit and HyperBoost |
| **Lighting** | Heatmap (temp) - Temperature meter (bar graph) - Wave (flowing, temp-tinted) - **Load meter (CPU bar left / GPU bar right)** - Spectrum cycle - Breathing (speed follows temperature) - Rainbow - Static color (White/Green/Cyan/Blue/Red/Purple/Orange/Pink) - Off |
| **Brightness** | 25 / 50 / 75 / 100 % ; Dim automatically when idle/cool ; Dim automatically at night (22:00-07:00) |
| **Pause / Resume automation** | hands control back to you, freezes the loop |
| **Exit** | stops the app, restores the fan to a quiet rest speed, puts the pad back into onboard Auto mode, turns the LEDs off |

The dashboard's **Lighting** page selects one active theme from Single color, RGB, and up to five
saved custom effects. **+ Custom** opens the gradient editor. Saving adds a choice to the gallery.
The live preview shows the last RGB frame acknowledged by the pad. Settings and saved effects
are stored in `tray.cfg` beside the executable.

### Fan control

The controller samples CPU and GPU temperatures every two seconds by default. It uses the warmer
valid reading and ignores stale GPU samples. The dashboard reports the commanded speed.

- **Automatic** estimates a steady speed up to 2,300 RPM during normal operation.
- **Quiet** follows the same approach with a 1,900 RPM cap.
- **Manual** holds the selected speed between 500 and 3,200 RPM.

Automatic and Quiet review filtered temperature and its trend every two minutes. Differences below
150 RPM leave the current command in place. The fan sends speed changes and a 15-second keep-alive.

All three modes use the temperature override. It starts after 10 seconds at 78°C or immediately at
82°C. It raises the command in 150 RPM steps while the temperature stays level or climbs. At 90°C
or higher it can step by 250 RPM every two seconds. The command eases back to the regular setting
after one minute at 69°C or cooler. If the temperature is unavailable, the controller holds its
current speed.

On exit, the default wind-down gradually returns control to the pad's onboard Auto mode.
The controller also reopens the USB device after a disconnect or repeated write failures.

### Single instance

A named mutex makes sure only one tray controller runs. Launching a second copy exits silently
instead of fighting the first one for the device.

## 2. Build from source

```
git clone https://github.com/Jasonz09/RazerPad-Control.git
cd RazerPad-Control
Start-RazerPad.cmd
```

Windows 10/11 and the .NET Framework C# compiler are required. Connect the pad over USB.

## 3. Project layout

| Location | Purpose |
|---|---|
| `Start-RazerPad.cmd` | Launcher |
| `build.cmd` | Rebuild from source |
| `src/` | Dashboard and controller source |
| `docs/` | USB protocol notes |
| `bin/` | Local executable, settings, status, and logs |

## 4. Autostart

Open **Device options** and enable **Start with Windows**. If it was enabled from
an older copy, toggle it off and back on here to update the launch path.

## 5. Command-line flags (all optional; they override `tray.cfg`)

```
bin/RazerPadFan.exe [flags]

  --profile NAME        automatic | quiet | manual
  --manual [N]          select Manual and optionally set its speed in 50 RPM units (10–64)
  --manualunit N        set the Manual speed and select Manual
  --led MODE            heatmap | level | wave | spectrum | breath | rainbow | static | custom | off
  --ledcolor NAME|HEX   color used by static / breathing
  --ledbri N            brightness 0–100
  --interval MS         fan sampling interval (500–2000, default 2000)
  --critical C          high-temperature lighting alert (default 85°C)
  --idledim / --nightdim   enable automatic brightness options
  --cooltemp C          wind-down exit target temperature (default 45°C)
  --nocooltail          skip the gradual exit wind-down
  --hyperboost          relax PCIe and USB power saving on AC until exit
  --open                show the dashboard on launch (default)
  --minimized           start in the tray
  --keep                on exit, leave the fan speed and mode as they are
  --status-file PATH    write live status to PATH
  --status              print device + sensor status and exit (no tray icon)
```

Example - read the pad without touching it:

```
bin/RazerPadFan.exe --status
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

Exit the tray app and run `build.cmd` to rebuild `bin/RazerPadFan.exe`.
Open it with `Start-RazerPad.cmd`.

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

* **Compact Slate** - a single-screen dashboard with CPU/GPU temperatures, commanded fan
  speed, Lighting, and HyperBoost. Quiet, Balanced, and Performance show Fan steadiness. Manual
  shows a 500–3,200 RPM target, and Turbo holds 3,200 RPM. The two-minute Turbo Burst runs independently
  of Auto-Turbo, and critical-temperature protection takes priority in every fan profile.

* **v8** - the **dashboard app** and the **anti-hunting fan controller**:
  - A real Synapse-style **dashboard window** (open from the tray - Open dashboard): live CPU/GPU
    temperature and fan-RPM tiles, profile picker, lighting picker, brightness, a **Fan steadiness**
    slider, every toggle, and Start-with-Windows - all in one place.
  - **Closed-loop PID + EMA + high-water-mark coast** controller so the fan holds ONE constant speed
    instead of pumping up and down every few seconds (in that release). This was the
    main thing you asked for.
  - **Fan steadiness** slider (smooth) that tunes deadband / down-hold / coast / sensor-filter as one knob.
  - **Write-on-change** fan control (only re-sends the HID command when the speed actually changes, plus a
    15s keep-alive), so Synapse and sensor noise stop re-triggering the pad.
  - Per-user **Start with Windows** via the `HKCU\...\Run` key (no admin, replaces the old Startup `.vbs`).
### crash.log

If something unexpected happens, look at `bin/crash.log`.
Every hot-reload is recorded there too, so you can confirm config edits landed.

---
