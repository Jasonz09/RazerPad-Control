using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
namespace RazerPadFan {
internal static partial class Program {
const int VID = 0x1532, PID = 0x0F43;
const int WIRE = 91;
const byte REPORT_ID = 0x00;
const byte CLASS_THERMAL = 13;
const byte CMD_SET_SPEED = 0x01, CMD_GET_SPEED = 0x81, CMD_GET_MODE = 0x82,
      CMD_GET_IDLIST = 0x80, CMD_GET_FANINFO = 0x83, CMD_SET_MODE = 0x04, CMD_GET_READING = 0x85;
const byte CLASS_LED = 3;
const byte LED_GET_ID = 0x89, LED_SET_STATE = 0x00, LED_SET_RGB = 0x01, LED_SET_EFFECT = 0x02,
      LED_SET_BRIGHT = 0x03, LED_SET_MATRIX = 0x0B, LED_SET_MATRIX_PAT = 0x0A, LED_GET_BRIGHT = 0x83;
const byte UE_OFF = 0, UE_WAVE = 1, UE_AFTERGLOW = 2, UE_BREATH = 3, UE_SPECTRUM = 4, UE_CUSTOMFRAME = 5, UE_STATIC = 6;
const int NUM_LED = 18;
static byte[] HexTo3(string s) {
  s = (s ?? "").Trim().TrimStart('#');
  if (s.Length == 3) { byte h = (byte)Convert.ToInt32(s.Substring(0, 1), 16); byte q = (byte)Convert.ToInt32(s.Substring(1, 1), 16); byte z = (byte)Convert.ToInt32(s.Substring(2, 1), 16); return new byte[] { (byte)(h * 17), (byte)(q * 17), (byte)(z * 17) }; }
  if (s.Length == 6) return new byte[] { (byte)Convert.ToInt32(s.Substring(0, 2), 16), (byte)Convert.ToInt32(s.Substring(2, 2), 16), (byte)Convert.ToInt32(s.Substring(4, 2), 16) };
  return null;
}
static string ColorName(string n) {
  n = (n ?? "").ToLowerInvariant();
  switch (n) {
    case "red": return "FF0000"; case "green": case "lime": return "00FF00"; case "blue": return "0000FF";
    case "yellow": return "FFFF00"; case "cyan": return "00FFFF"; case "magenta": case "pink": return "FF00FF";
    case "white": return "FFFFFF"; case "orange": return "FF8000"; case "purple": return "8000FF";
    case "teal": return "008080"; case "rainbow": return "rainbow";
    default: return n;
  }
}
static void HSV(int h, int s, int v, out int r, out int g, out int b) {
  h = ((h % 360) + 360) % 360; double hh = h / 60.0; double ss = s / 100.0, vv = v / 100.0;
  double c = vv * ss, x = c * (1 - Math.Abs(hh % 2 - 1)), m = vv - c;
  double rr = 0, gg = 0, bb = 0;
  if (hh < 1) { rr = c; gg = x; } else if (hh < 2) { rr = x; gg = c; } else if (hh < 3) { gg = c; bb = x; }
  else if (hh < 4) { gg = x; bb = c; } else if (hh < 5) { rr = x; bb = c; } else { rr = c; bb = x; }
  r = (int)Math.Round((rr + m) * 255); g = (int)Math.Round((gg + m) * 255); b = (int)Math.Round((bb + m) * 255);
}
static bool SetLedBrightness(IntPtr h, int ledId, int bri) { byte[] r; return TxLenC(h, CLASS_LED, LED_SET_BRIGHT, new byte[] { CLASS_LED, (byte)ledId, (byte)bri }, 3 + 3, out r); }
static bool SetLedAllBrightness(IntPtr h, int bri) { bool ok = true; for (int i = 0; i < NUM_LED; i++) if (!SetLedBrightness(h, i, bri)) ok = false; return ok; }
static bool SetLedStateOn(IntPtr h, int ledId, bool on) { byte[] r; return TxC(h, CLASS_LED, LED_SET_STATE, new byte[] { CLASS_LED, (byte)ledId, (byte)(on ? 1 : 0) }, out r); }
static bool SetLedRGB(IntPtr h, int ledId, int r, int g, int b) { byte[] rr; return TxC(h, CLASS_LED, LED_SET_RGB, new byte[] { CLASS_LED, (byte)ledId, (byte)r, (byte)g, (byte)b }, out rr); }
static bool MatrixOff(IntPtr h) { byte[] r; return TxLenC(h, CLASS_LED, LED_SET_MATRIX, new byte[] { UE_OFF }, 3 + 1, out r); }
static bool MatrixStatic(IntPtr h, int r, int g, int b) { byte[] rr; return TxLenC(h, CLASS_LED, LED_SET_MATRIX, new byte[] { UE_STATIC, (byte)r, (byte)g, (byte)b }, 3 + 4, out rr); }
static bool MatrixSpectrum(IntPtr h) { byte[] r; return TxLenC(h, CLASS_LED, LED_SET_MATRIX, new byte[] { UE_SPECTRUM }, 3 + 1, out r); }
static bool MatrixBreathing(IntPtr h, int r, int g, int b) { byte[] rr; return TxLenC(h, CLASS_LED, LED_SET_MATRIX, new byte[] { UE_BREATH, 1, (byte)r, (byte)g, (byte)b }, 3 + 5, out rr); }
static bool MatrixCustom(IntPtr h, byte[] rgb18) { byte[] p = new byte[1 + NUM_LED * 3]; p[0] = UE_CUSTOMFRAME; for (int i = 0; i < NUM_LED * 3; i++) p[1 + i] = rgb18[i]; byte[] r; return TxLenC(h, CLASS_LED, LED_SET_MATRIX, p, 3 + p.Length, out r); }
const int FACTOR = 50;
static int _tid = 0;
static int NextTid() { int t = _tid; _tid = (t + 1) & 31; return t; }
[DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
[DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr d);
[DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr d, IntPtr caps);
[DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr d);
[DllImport("hid.dll")] static extern bool HidD_SetFeature(IntPtr h, byte[] b, int l);
[DllImport("hid.dll")] static extern bool HidD_GetFeature(IntPtr h, byte[] b, int l);
[DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)]
static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
[DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)]
static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr c, ref Guid g, int i, IntPtr pdi);
[DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)]
static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, IntPtr pdi, IntPtr dd, int ds, out int need, IntPtr di);
[DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
[DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
static extern IntPtr CreateFile(string n, uint a, uint sh, IntPtr sa, uint d, uint fl, IntPtr at);
[DllImport("kernel32.dll", SetLastError=true)] static extern bool CloseHandle(IntPtr h);
[StructLayout(LayoutKind.Sequential)]
struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int flags; public IntPtr devicePath; }
static int DetailSize { get { return (IntPtr.Size == 4) ? (4 + 2) : 8; } }
static ushort FeatureLenOf(IntPtr caps) { return (ushort)(Marshal.ReadInt16(caps, 8) & 0xFFFF); }
static string FindDevicePath() {
Guid g; HidD_GetHidGuid(out g);
IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x00000012);
if (set == new IntPtr(-1)) return null;
int pdiSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
string result = null;
try {
for (int i = 0; i < 400; i++) {
IntPtr pdi = Marshal.AllocHGlobal(pdiSize);
try {
var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = pdiSize;
Marshal.StructureToPtr(d, pdi, false);
if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, pdi)) break;
int need;
SetupDiGetDeviceInterfaceDetail(set, pdi, IntPtr.Zero, 0, out need, IntPtr.Zero);
if (need <= 0) continue;
IntPtr det = Marshal.AllocHGlobal(need);
try {
Marshal.WriteInt32(det, DetailSize);
if (!SetupDiGetDeviceInterfaceDetail(set, pdi, det, need, out need, IntPtr.Zero)) continue;
string path = Marshal.PtrToStringUni(new IntPtr(det.ToInt64() + 4));
if (path == null) continue;
string lp = path.ToLowerInvariant();
if (!lp.Contains("vid_" + VID.ToString("x4")) || !lp.Contains("pid_" + PID.ToString("x4"))) continue;
IntPtr h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
if (h == new IntPtr(-1)) continue;
try {
IntPtr pp;
if (HidD_GetPreparsedData(h, out pp)) {
try {
IntPtr caps = Marshal.AllocHGlobal(256); // HIDP_CAPS is ~64 bytes; must not underallocate (was 16 -> native heap overflow / 0xC0000374)
int rc = HidP_GetCaps(pp, caps);
ushort flen = (rc == 0x110000) ? FeatureLenOf(caps) : (ushort)0;
Marshal.FreeHGlobal(caps);
if (flen == WIRE) result = path;
} finally { HidD_FreePreparsedData(pp); }
}
} finally { CloseHandle(h); }
if (result != null) break;
} finally { Marshal.FreeHGlobal(det); }
} finally { Marshal.FreeHGlobal(pdi); }
}
} finally { SetupDiDestroyDeviceInfoList(set); }
return result;
}
static byte[] FeatureReport(byte classId, byte cmd, byte[] payload, int forcedLen, byte transactionId) {
  byte[] wire = new byte[WIRE];
  int plen = payload == null ? 0 : payload.Length;
  int len = (forcedLen > 0) ? forcedLen : 3 + plen;
  wire[0] = REPORT_ID;
  wire[2] = transactionId;
  wire[6] = (byte)len;
  wire[7] = classId;
  wire[8] = cmd;
  if (payload != null) for (int i = 0; i < plen; i++) wire[9 + i] = payload[i];
  wire[89] = ReportChecksum(wire);
  return wire;
}
static byte ReportChecksum(byte[] wire) {
  byte checksum = 0;
  for (int n = 3; n < 89; n++) checksum ^= wire[n];
  return checksum;
}
static bool Transact(IntPtr h, byte classId, byte cmd, byte[] payload, int forcedLen, int waitMs, out byte[] reply) {
  byte[] wire = FeatureReport(classId, cmd, payload, forcedLen, (byte)NextTid());
  if (!HidD_SetFeature(h, wire, WIRE)) { reply = null; return false; }
  if (waitMs > 0) Thread.Sleep(waitMs);
  for (int i = 0; i < WIRE; i++) wire[i] = 0;
  if (!HidD_GetFeature(h, wire, WIRE)) { reply = null; return false; }
  int rlen = wire[6];
  if (rlen < 0) rlen = 0;
  if (8 + rlen > WIRE) rlen = WIRE - 8;
  reply = new byte[rlen];
  Array.Copy(wire, 9, reply, 0, rlen);
  return rlen > 0 && wire[7] == classId && wire[8] == cmd;
}
static bool Tx(IntPtr h, byte cmd, byte[] payload, out byte[] reply) {
  int[] waits = new int[] { 120, 300, 600 };
  for (int a = 0; a < waits.Length; a++)
    if (Transact(h, CLASS_THERMAL, cmd, payload, -1, waits[a], out reply)) return true;
  reply = null; return false;
}
static bool TxLen(IntPtr h, byte cmd, byte[] payload, int forcedLen, out byte[] reply) {
  int[] waits = new int[] { 150, 400, 700 };
  for (int a = 0; a < waits.Length; a++)
    if (Transact(h, CLASS_THERMAL, cmd, payload, forcedLen, waits[a], out reply)) return true;
  reply = null; return false;
}
static bool TxC(IntPtr h, byte classId, byte cmd, byte[] payload, out byte[] reply) {
  int[] waits = new int[] { 120, 300, 600 };
  for (int a = 0; a < waits.Length; a++)
    if (Transact(h, classId, cmd, payload, -1, waits[a], out reply)) return true;
  reply = null; return false;
}
static bool TxLenC(IntPtr h, byte classId, byte cmd, byte[] payload, int forcedLen, out byte[] reply) {
  int[] waits = new int[] { 150, 400, 700 };
  for (int a = 0; a < waits.Length; a++)
    if (Transact(h, classId, cmd, payload, forcedLen, waits[a], out reply)) return true;
  reply = null; return false;
}

// RC21-0230 uses Chroma extended matrix with the zero LED and a one-row, 18-pixel strip.
const byte ChromaClass = 0x0F, ChromaTransaction = 0x1F, ChromaOk = 0x02;
const byte ChromaEffect = 0x02, ChromaFrame = 0x03, ChromaBrightness = 0x04;
static readonly byte[] ChromaOff = { 1, 0, 0, 0, 0, 0 };
static readonly byte[] ChromaCustom = { 0, 0, 0x08, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
static bool lightBarBrightnessReady;
static int lightBarModeAt;

// Called with hidLock held, so a thermal transaction cannot replace the Chroma reply.
static bool WriteChroma(IntPtr h, byte command, byte[] payload) {
  byte[] wire = FeatureReport(ChromaClass, command, payload, payload.Length, ChromaTransaction);
  if (!HidD_SetFeature(h, wire, WIRE)) return false;
  for (int attempt = 0; attempt < 3; attempt++) {
    if (attempt > 0) Thread.Sleep(6);
    Array.Clear(wire, 0, wire.Length);
    if (!HidD_GetFeature(h, wire, WIRE) || !IsChromaReply(wire, command, payload)) continue;
    if (wire[1] == ChromaOk) return true;
    if (wire[1] != 0x01) return false;
  }
  return false;
}

static bool IsChromaReply(byte[] wire, byte command, byte[] payload) {
  return wire[0] == REPORT_ID && wire[2] == ChromaTransaction && wire[6] == payload.Length &&
    wire[7] == ChromaClass && wire[8] == command && ReportChecksum(wire) == wire[89] &&
    payload.SequenceEqual(wire.Skip(9).Take(payload.Length));
}

static byte[] ChromaRow(byte[] frame) {
  var row = new byte[5 + NUM_LED * 3];
  row[4] = NUM_LED - 1;
  frame.CopyTo(row, 5);
  return row;
}

static bool TrySendLightBar(IntPtr h, byte[] frame, bool off, bool refreshMode) {
  if (off) return WriteChroma(h, ChromaEffect, ChromaOff);
  // Brightness is already baked into each pixel, so hold the device's master multiplier at full.
  if (!lightBarBrightnessReady) {
    lightBarBrightnessReady = WriteChroma(h, ChromaBrightness, new byte[] { 1, 0, 255 });
    if (!lightBarBrightnessReady) return false;
  }
  if (!WriteChroma(h, ChromaFrame, ChromaRow(frame))) return false;
  return !refreshMode || WriteChroma(h, ChromaEffect, ChromaCustom);
}
static byte Bake(int v, int bri) { if (v < 0) v = 0; if (v > 255) v = 255; if (bri < 0) bri = 0; if (bri > 100) bri = 100; return (byte)(v * bri / 100); }static int[] GetIdList(IntPtr h) {
  byte[] r;
  if (!Tx(h, CMD_GET_IDLIST, new byte[0], out r) || r.Length < 2) return null;
  int total = r[0];
  var ids = new List<int>();
  for (int i = 0; i < total && 1 + i < r.Length; i++) ids.Add(r[1 + i]);
  return ids.ToArray();
}
static int GetFanSpeed(IntPtr h, int thermalId) {
  byte[] r;
  if (!Tx(h, CMD_GET_SPEED, new byte[] { 0, (byte)thermalId }, out r) || r.Length < 3) return -1;
  return r[2];
}
static bool SetFanSpeed(IntPtr h, int thermalId, int unit) {
  if (unit < 10) unit = 10;
  if (unit > 64) unit = 64;
  byte[] r;
  return Tx(h, CMD_SET_SPEED, new byte[] { 0, (byte)thermalId, (byte)unit }, out r);
}
static int GetFanMode(IntPtr h, int thermalId) {
  byte[] r;
  if (!Tx(h, CMD_GET_MODE, new byte[] { 0, (byte)thermalId }, out r) || r.Length < 3) return -1;
  return r[2];
}
static bool SetFanMode(IntPtr h, int thermalId, int mode) {
  byte[] r;
  return TxLen(h, CMD_SET_MODE, new byte[] { 0, (byte)thermalId, (byte)mode, 0 }, 80, out r);
}
static PerformanceCounter _cpuPc = null;
static string _cpuInst = null;
static double GetCpuRaw() {
  try {
    if (_cpuPc == null) {
      List<string> cands = new List<string>();
      try {
        PerformanceCounterCategory cat = new PerformanceCounterCategory("Thermal Zone Information");
        cands.AddRange(cat.GetInstanceNames());
        cands.Sort();
      } catch { }
      cands.Add("\\_TZ.TSOC");
      cands.Add("\\_TZ.TZ01");
      foreach (string pick in cands) {
        try {
          PerformanceCounter pc = new PerformanceCounter();
          pc.CategoryName = "Thermal Zone Information";
          pc.CounterName = "Temperature";
          pc.InstanceName = pick;
          pc.ReadOnly = true;
          double v = pc.NextValue();
          if (v > 0) { _cpuPc = pc; _cpuInst = pick; break; }
        } catch { }
      }
      if (_cpuPc == null) return -1;
    }
    double v2 = _cpuPc.NextValue();
    double c = v2 / 10.0;
    if (c < 5 || c > 120) return -1;
    return c;
  } catch { return -1; }
}
static string FindNvidiaSmi() {
  string p = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot"), "System32\\nvidia-smi.exe");
  if (File.Exists(p)) return p;
  string[] more = new string[] {
    Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles"), "NVIDIA Corporation\\NVSMI\\nvidia-smi.exe"),
    Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles"), "NVIDIA\\GPU Computing Toolkit\\CUDA\\v12.9\\bin\\nvidia-smi.exe") };
  foreach (string m in more) if (File.Exists(m)) return m;
  return "nvidia-smi";
}
static double GetGpuRaw() {
  try {
    ProcessStartInfo psi = new ProcessStartInfo(FindNvidiaSmi(), "--query-gpu=temperature.gpu --format=csv,noheader");
    psi.UseShellExecute = false;
    psi.CreateNoWindow = true;
    psi.RedirectStandardOutput = true;
    using (Process pr = Process.Start(psi)) {
      string outp = pr.StandardOutput.ReadToEnd();
      if (!pr.WaitForExit(5000)) { try { pr.Kill(); } catch {} return -1; }
      string[] lines = outp.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
      foreach (string ln in lines) {
        int t;
        if (int.TryParse(ln.Trim(), out t) && t > 0 && t < 120) return t;
      }
      return -1;
    }
  } catch { return -1; }
}
// ---- CPU load via PDH (cheap, ~sub-ms once the counter exists; first read returns 0) ----
static PerformanceCounter _cpuLoadPc = null;
static int _cpuLoadCalls = 0;
static double GetCpuLoad() {
  try {
    if (_cpuLoadPc == null) {
      try { _cpuLoadPc = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true); }
      catch { _cpuLoadPc = new PerformanceCounter("Processor", "% Processor Time", "_Total", true); }
      try { _cpuLoadPc.NextValue(); } catch { }
      return -1;
    }
    double v = _cpuLoadPc.NextValue();
    if (v <= 0 && _cpuLoadCalls < 2) return cpuLoad;   // warm-up reads report 0; keep prior value
    _cpuLoadCalls++;
    if (v < 0) v = 0; if (v > 100) v = 100;
    return v;
  } catch { return -1; }
}
// ---- Background GPU sampler: runs nvidia-smi every ~3s off the main loop so the worker and
// the LED thread never block on the ~70ms process spawn. Also derives GPU load from GPU Engine
// utilisation via nvidia-smi's util query (single combined call). ----
static void GpuLoop() {
  while (_run) {
    try {
      string smi = FindNvidiaSmi();
      ProcessStartInfo psi = new ProcessStartInfo(smi, "--query-gpu=temperature.gpu,utilization.gpu --format=csv,noheader,nounits");
      psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
      using (Process pr = Process.Start(psi)) {
        string outp = pr.StandardOutput.ReadToEnd();
        if (!pr.WaitForExit(5000)) { try { pr.Kill(); } catch { } }
        else {
          string[] lines = outp.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
          int maxT = -1, maxU = -1;
          foreach (string ln in lines) {
            string[] f = ln.Split(',');
            if (f.Length >= 1) { int tt; if (int.TryParse(f[0].Trim(), out tt) && tt > 0 && tt < 120 && tt > maxT) maxT = tt; }
            if (f.Length >= 2) { int uu; if (int.TryParse(f[1].Trim(), out uu) && uu >= 0 && uu <= 100 && uu > maxU) maxU = uu; }
          }
          if (maxT > 0) {
            Volatile.Write(ref gpuCache, maxT);
            Interlocked.Exchange(ref gpuSampleTimestamp, Stopwatch.GetTimestamp());
          }
          if (maxU >= 0) gpuLoad = maxU;
        }
      }
    } catch { }
    for (int s = 0; s < 30 && _run; s++) Thread.Sleep(100);
  }
}
const int MIN_UNIT = 10, MAX_UNIT = 64;   // raw unit*50 = rpm; physical fan ~500-3200 rpm (raw 10-64)
static volatile bool _running = true;
static string _exitFile = null;
static string _logFile = null;
static void Log(string s) {
  Console.WriteLine(s);
  if (_logFile != null) { try { File.AppendAllText(_logFile, s + Environment.NewLine); } catch { } }
}
static double MaxTemp() { double c = GetCpuRaw(); double g = GetGpuRaw(); return Math.Max(c, g); }
static int TempHue(double t) { double f = (t - 40.0) / 45.0; if (f < 0) f = 0; if (f > 1) f = 1; return (int)Math.Round(120.0 - 120.0 * f); }
static void LedGradient(IntPtr h, double temp) { int baseH = TempHue(temp); byte[] rgb = new byte[NUM_LED * 3]; for (int i = 0; i < NUM_LED; i++) { int hu = baseH - 15 + (i * 30) / NUM_LED; int r, g, b; HSV(hu, 100, 100, out r, out g, out b); rgb[i * 3] = (byte)r; rgb[i * 3 + 1] = (byte)g; rgb[i * 3 + 2] = (byte)b; } MatrixCustom(h, rgb); }
static void LedRainbow(IntPtr h, int phase) { byte[] rgb = new byte[NUM_LED * 3]; for (int i = 0; i < NUM_LED; i++) { int hu = (phase + (i * 360) / NUM_LED) % 360; int r, g, b; HSV(hu, 100, 100, out r, out g, out b); rgb[i * 3] = (byte)r; rgb[i * 3 + 1] = (byte)g; rgb[i * 3 + 2] = (byte)b; } MatrixCustom(h, rgb); }
static void SetupLed(IntPtr h, string mode, string color) { byte[] c = null; string cn = ColorName(color); if (cn != "rainbow") c = HexTo3(cn); switch (mode) { case "off": MatrixOff(h); break; case "static": if (c != null) MatrixStatic(h, c[0], c[1], c[2]); break; case "spectrum": MatrixSpectrum(h); break; case "breath": case "breathing": if (c != null) MatrixBreathing(h, c[0], c[1], c[2]); else MatrixBreathing(h, 255, 0, 0); break; case "rainbow": LedRainbow(h, 0); break; case "heatmap": case "temp": LedGradient(h, MaxTemp()); break; case "none": default: break; } }
// ============ TRAY APP STATE ============
static NotifyIcon tray;
static System.Windows.Forms.Timer uiTimer;
static Thread worker;
static volatile bool _run = true;
// ---------- v5: crash logging, config hot-reload, GDI hygiene ----------
static string crashLogPath = null;
static FileSystemWatcher cfgWatcher = null;
static int lastSelfSaveMs = 0;
static int lastCfgReloadMs = 0;
static string lastIconSig = "";
static IntPtr lastIconH = IntPtr.Zero;
static IntPtr curIconH = IntPtr.Zero;
[DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
static void LogCrash(string msg) {
  try {
    if (crashLogPath == null) return;
    try { if (File.Exists(crashLogPath) && new FileInfo(crashLogPath).Length > 65536) File.Delete(crashLogPath); } catch { }
    File.AppendAllText(crashLogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
  } catch { }
}
static void CfgChanged(object s, FileSystemEventArgs e) {
  try {
    for (int w = 0; w < 3; w++) Thread.Sleep(100);
    int now = Environment.TickCount;
    if (now - lastSelfSaveMs < 1000) return;
    if (now - lastCfgReloadMs < 400) return;
    lastCfgReloadMs = now;
    LoadConfig();
    Form current = dash;
    if (current != null && current.IsHandleCreated && !current.IsDisposed)
      current.BeginInvoke((Action)SyncDash);
    LogCrash("hot-reload: led=" + led + " profile=" + fanMode + " bri=" + bri);
  } catch { }
}
static volatile bool paused = false;
static volatile int curCpu = -1, curGpu = -1, curUnit = -1, curRpm = -1;
static volatile string curStatus = "starting...";
static volatile bool ledDirty = true;
static volatile int lastBriApplied = -1;
static FanMode fanMode = FanMode.Automatic;
static string led = "heatmap";
static string ledColor = "green";
static int bri = 80;
static int interval = 2000;
static int critical = 85;
static readonly object cfgLock = new object();
static bool keepOnExit = false;
// ---------- v3 additions ----------
static bool idleDim = true;        // dim the light bar while the laptop is cool/idle
static bool nightDim = false;      // dim the light bar between 22:00 and 07:00
static volatile string fanOverride = "";
static double gpuCache = -1;       // latest GPU temp from the background sampler
static long gpuSampleTimestamp;
static double gpuLoad = -1;        // latest GPU utilisation % from nvidia-smi
static double cpuLoad = -1;        // latest CPU utilisation % from PDH
static Thread gpuThread;                    // background sensor sampler
static Thread ledThread;                    // smooth lighting driver
static readonly object hidLock = new object();  // serialise every HID feature write
static bool coolTail = true;         // gradually slow the fan to a stop on exit once hardware is cool
static int coolTemp = 45;            // target temperature for the exit cool-down tail
static bool hyperBoost = false;      // v7: lift OS power-saver caps (PCIe ASPM / USB suspend) for max GPU throughput
// ---------- v8 additions ----------
static int manualUnit = 30;            // manual fan speed in raw units (10-64 -> ~500-3200rpm)
static bool openOnStart = true;      // show the dashboard when launched by hand; the logon autostart passes --minimized
// device handle shared between the worker and menu actions (only the worker touches it)
static IntPtr devH = new IntPtr(-1);
static int fanId = 5;
static bool Reopen() {
  lock (hidLock) {
    lightingOutput = null;
    lightingConnection++;
    lightBarBrightnessReady = false;
    try { if (devH != new IntPtr(-1)) { CloseHandle(devH); devH = new IntPtr(-1); } } catch { }
  }
  string path = FindDevicePath();
  if (path == null) return false;
  IntPtr h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
  if (h == new IntPtr(-1)) return false;
  lock (hidLock) {
    devH = h;
    try { int[] ids = GetIdList(h); if (ids != null && ids.Length > 0) fanId = ids[0]; } catch { }
    try { SetFanMode(h, fanId, 1); } catch { }
  }
  ledDirty = true;
  return true;
}
// ---------- v3 lighting helpers ----------
// Flowing colour band that sweeps across the bar. Base hue follows temperature (subtle
// when cool, fiery when hot) so it is pretty AND still gives a rough thermal read.
static void LedWave(IntPtr h, int phase, double temp) {
  int baseH = TempHue(temp);
  byte[] rgb = new byte[NUM_LED * 3];
  for (int i = 0; i < NUM_LED; i++) {
    int hu = (int)(baseH + 45.0 * Math.Sin((i * 60.0 + phase * 4.0) * Math.PI / 180.0));
    int r, g, b; HSV(hu, 100, 100, out r, out g, out b);
    rgb[i * 3] = (byte)r; rgb[i * 3 + 1] = (byte)g; rgb[i * 3 + 2] = (byte)b;
  }
  MatrixCustom(h, rgb);
}
// LED bar-graph of temperature: lit segments = how hot, colour = how hot (green -> red).
static void LedLevel(IntPtr h, double temp) {
  double f = (temp - 30.0) / 70.0;
  if (f < 0) f = 0; if (f > 1) f = 1;
  int lit = (int)Math.Round(f * NUM_LED);
  if (lit < 1) lit = 1; if (lit > NUM_LED) lit = NUM_LED;
  int hue = TempHue(temp);
  byte[] rgb = new byte[NUM_LED * 3];
  for (int i = 0; i < NUM_LED; i++) {
    int r, g, b;
    if (i < lit) HSV(hue, 100, 100, out r, out g, out b);
    else HSV(hue, 100, 15, out r, out g, out b);
    rgb[i * 3] = (byte)r; rgb[i * 3 + 1] = (byte)g; rgb[i * 3 + 2] = (byte)b;
  }
  MatrixCustom(h, rgb);
}
static byte[] RainbowFrame(double ph, int bri, bool uniform) {
  byte[] rgb = new byte[NUM_LED * 3];
  for (int i = 0; i < NUM_LED; i++) {
    int hu = (int)(ph + (uniform ? 0 : i * (360.0 / NUM_LED))) % 360;
    int r, g, b; HSV(hu, 100, 100, out r, out g, out b);
    rgb[i * 3] = Bake(r, bri); rgb[i * 3 + 1] = Bake(g, bri); rgb[i * 3 + 2] = Bake(b, bri);
  }
  return rgb;
}
static byte[] WaveFrame(double ph, double temp, int bri) {
  int baseH = TempHue(temp);
  byte[] rgb = new byte[NUM_LED * 3];
  for (int i = 0; i < NUM_LED; i++) {
    double s = Math.Sin((i * 20.0 + ph * 6.0) * Math.PI / 180.0);
    int hu = (int)(baseH + 40.0 * s);
    double vv = 78.0 + 22.0 * (0.5 + 0.5 * Math.Sin((i * 24.0 - ph * 8.0) * Math.PI / 180.0));
    int r, g, b; HSV(hu, 100, (int)vv, out r, out g, out b);
    rgb[i * 3] = Bake(r, bri); rgb[i * 3 + 1] = Bake(g, bri); rgb[i * 3 + 2] = Bake(b, bri);
  }
  return rgb;
}
static byte[] LevelFrame(double temp, int bri) {
  double f = (temp - 30.0) / 70.0; if (f < 0) f = 0; if (f > 1) f = 1;
  int lit = (int)Math.Round(f * NUM_LED); if (lit < 1) lit = 1; if (lit > NUM_LED) lit = NUM_LED;
  int hue = TempHue(temp);
  byte[] rgb = new byte[NUM_LED * 3];
  for (int i = 0; i < NUM_LED; i++) {
    int r, g, b;
    if (i < lit) HSV(hue, 100, 100, out r, out g, out b); else HSV(hue, 100, 12, out r, out g, out b);
    rgb[i * 3] = Bake(r, bri); rgb[i * 3 + 1] = Bake(g, bri); rgb[i * 3 + 2] = Bake(b, bri);
  }
  return rgb;
}
static byte[] HeatmapFrame(double temp, int bri) {
  int baseH = TempHue(temp);
  byte[] rgb = new byte[NUM_LED * 3];
  for (int i = 0; i < NUM_LED; i++) {
    int hu = baseH - 15 + (i * 30) / NUM_LED;
    int r, g, b; HSV(hu, 100, 100, out r, out g, out b);
    rgb[i * 3] = Bake(r, bri); rgb[i * 3 + 1] = Bake(g, bri); rgb[i * 3 + 2] = Bake(b, bri);
  }
  return rgb;
}
static byte[] BreathFrame(double ph, double temp, byte cr, byte cg, byte cb, int bri) {
  double period = 3200.0 - 20.0 * temp; if (period < 900) period = 900; if (period > 3200) period = 3200;
  double cyc = (ph / period) * (2 * Math.PI);
  double env = 0.15 + 0.85 * (0.5 - 0.5 * Math.Cos(cyc));
  byte[] rgb = new byte[NUM_LED * 3];
  for (int i = 0; i < NUM_LED; i++) {
    rgb[i * 3] = Bake((int)(cr * env), bri); rgb[i * 3 + 1] = Bake((int)(cg * env), bri); rgb[i * 3 + 2] = Bake((int)(cb * env), bri);
  }
  return rgb;
}
static byte[] LoadMeterFrame(double cload, double gload, double temp, int bri) {
  if (cload < 0) cload = 0; if (cload > 100) cload = 100;
  if (gload < 0) gload = 0; if (gload > 100) gload = 100;
  int hue = TempHue(temp);
  int half = NUM_LED / 2;
  byte[] rgb = new byte[NUM_LED * 3];
  for (int i = 0; i < NUM_LED; i++) {
    int seg = (i < half) ? i : (i - half);
    double loadPct = (i < half) ? cload : gload;
    int litSeg = (int)Math.Round((loadPct / 100.0) * half);
    int r, g, b;
    if (seg < litSeg) HSV(hue, 100, 100, out r, out g, out b); else HSV(hue, 100, 12, out r, out g, out b);
    rgb[i * 3] = Bake(r, bri); rgb[i * 3 + 1] = Bake(g, bri); rgb[i * 3 + 2] = Bake(b, bri);
  }
  return rgb;
}
sealed class LightingOutput {
  public readonly byte[] Frame;
  public readonly int Brightness;
  public readonly int ConfirmedAt;
  public readonly string Note;
  public readonly bool Off;

  public LightingOutput(byte[] frame, int brightness, int confirmedAt, string note, bool off) {
    Frame = frame;
    Brightness = brightness;
    ConfirmedAt = confirmedAt;
    Note = note;
    Off = off;
  }
}

static volatile LightingOutput lightingOutput;
static volatile int lightingConnection;
static Color? singleColorDraft;
static readonly Dictionary<string, string> effectColors = new Dictionary<string, string>();
static readonly string[] lightingModes = { "heatmap", "level", "wave", "spectrum", "loadmeter", "breath", "rainbow", "static", "off", "custom", "singlecustom" };

static string CanonicalEffect(string mode) {
  return mode == "breathing" ? "breath" : mode == "temp" ? "heatmap" : mode == "none" ? "off" : mode;
}

static string EffectColor(string mode) {
  lock (cfgLock) {
    string color;
    return effectColors.TryGetValue(CanonicalEffect(mode), out color) ? color : null;
  }
}

static byte[] SolidFrame(byte red, byte green, byte blue) {
  var frame = new byte[NUM_LED * 3];
  for (int i = 0; i < frame.Length; i += 3) {
    frame[i] = red; frame[i + 1] = green; frame[i + 2] = blue;
  }
  return frame;
}

static void TintFrame(byte[] frame, byte[] color, string mode, double phase) {
  for (int i = 0; i < frame.Length; i += 3) {
    int intensity = Math.Max(frame[i], Math.Max(frame[i + 1], frame[i + 2]));
    if (mode == "rainbow" || mode == "spectrum") {
      double offset = mode == "rainbow" ? (i / 3) * 2 * Math.PI / NUM_LED : 0;
      intensity = (int)(intensity * (.62 + .38 * Math.Cos(phase / 600.0 - offset)));
    }
    for (int component = 0; component < 3; component++)
      frame[i + component] = (byte)(intensity * color[component] / 255);
  }
}

static int EffectiveLightingBrightness(int requested, double temperature, int hour,
                                       bool dimIdle, bool dimNight, bool alert) {
  bool cool = dimIdle && temperature >= 0 && temperature < 45;
  bool night = dimNight && (hour >= 22 || hour < 7);
  return !alert && (cool || night) ? (requested + 1) / 2 : requested;
}

static void LedLoop() {
  var clock = Stopwatch.StartNew();
  string previousMode = "";
  CustomEffect previousEffect = null;
  long modeStarted = 0;
  int previousConnection = -1;
  while (_run) {
    int connection = lightingConnection;
    IntPtr device = devH;
    if (connection != previousConnection) {
      previousMode = "";
      previousConnection = connection;
    }
    if (device == new IntPtr(-1)) { Thread.Sleep(100); continue; }

    long now = clock.ElapsedMilliseconds;
    string mode, baseColor, customColor;
    int brightness;
    CustomEffect customEffect;
    Color? colorDraft;
    bool isPaused, dimIdle, dimNight;
    lock (cfgLock) {
      mode = CanonicalEffect(led);
      baseColor = ledColor;
      effectColors.TryGetValue(mode, out customColor);
      SavedEffect selected = mode == "custom" ? FindSavedEffect(selectedCustomId) : null;
      customEffect = selected == null ? null : selected.Effect;
      colorDraft = mode == "static" ? singleColorDraft : null;
      brightness = bri; isPaused = paused; dimIdle = idleDim; dimNight = nightDim;
    }
    if (mode != previousMode || !ReferenceEquals(customEffect, previousEffect)) {
      previousMode = mode;
      previousEffect = customEffect;
      modeStarted = now;
    }
    double phase = now - modeStarted;
    double temp = Math.Max(curCpu, curGpu);
    bool criticalBlink = critical > 0 && temp >= critical && !isPaused;
    int requestedBrightness = brightness;
    brightness = EffectiveLightingBrightness(brightness, temp, DateTime.Now.Hour, dimIdle, dimNight, criticalBlink);

    byte[] custom = customColor == null ? null : HexTo3(customColor);
    byte[] baseRgb = colorDraft.HasValue
      ? new[] { colorDraft.Value.R, colorDraft.Value.G, colorDraft.Value.B }
      : custom ?? HexTo3(ColorName(baseColor)) ?? new byte[] { 255, 255, 255 };
    bool dark = isPaused || mode == "off" || (criticalBlink && (now / 350) % 2 != 0);
    bool solid = dark || criticalBlink || mode == "static" || mode == "singlecustom";
    try {
      byte[] frame;
      if (solid) {
        byte r = dark ? (byte)0 : Bake(criticalBlink ? 255 : baseRgb[0], brightness);
        byte g = dark || criticalBlink ? (byte)0 : Bake(baseRgb[1], brightness);
        byte b = dark || criticalBlink ? (byte)0 : Bake(baseRgb[2], brightness);
        frame = SolidFrame(r, g, b);
      } else {
        switch (mode) {
          case "rainbow": case "spectrum":
            frame = RainbowFrame(phase / 16.0 % 360.0, brightness, mode == "spectrum");
            break;
          case "custom":
            frame = customEffect != null ? RenderCustomEffect(customEffect, phase, brightness) :
              WaveFrame(phase / 16.0, temp, brightness);
            break;
          case "wave": frame = WaveFrame(phase / 16.0, temp, brightness); break;
          case "level": frame = LevelFrame(temp, brightness); break;
          case "loadmeter": frame = LoadMeterFrame(cpuLoad, gpuLoad, temp, brightness); break;
          case "breath": frame = BreathFrame(phase, temp, baseRgb[0], baseRgb[1], baseRgb[2], brightness); break;
          default: frame = HeatmapFrame(temp, brightness); break;
        }
        if (custom != null && mode != "breath" && customEffect == null) TintFrame(frame, custom, mode, phase);
      }
      string note = isPaused ? "Paused" : criticalBlink ? "Temperature alert" :
        brightness < requestedBrightness ? "auto dim" : "";
      ApplyLightBarFrame(device, connection, frame, dark ? 0 : brightness, dark, note);
    } catch (Exception error) {
      LogCrash("lighting update: " + error.Message);
      ApplyLightBarFailure(device, connection);
    }
    int sleep = Math.Max(1, 40 - (int)(clock.ElapsedMilliseconds - now));
    while (sleep > 0 && _run) { int step = Math.Min(sleep, 40); Thread.Sleep(step); sleep -= step; }
  }
}

static void ApplyLightBarFrame(IntPtr device, int connection, byte[] frame, int brightness, bool off, string note) {
  lock (hidLock) {
    if (device != devH || connection != lightingConnection) return;
    LightingOutput output = lightingOutput;
    int now = Environment.TickCount;
    if (output != null && output.Frame != null && output.Off == off &&
        unchecked(now - output.ConfirmedAt) < 1500 && frame.SequenceEqual(output.Frame)) {
      if (output.Brightness != brightness || output.Note != note)
        lightingOutput = new LightingOutput(output.Frame, brightness, output.ConfirmedAt, note, off);
      return;
    }
    // Reassert the effect mode as well as still frames in case another controller changed it.
    bool refreshMode = output == null || output.Frame == null || output.Off != off ||
      unchecked(now - lightBarModeAt) >= 1500;
    bool accepted = TrySendLightBar(device, frame, off, refreshMode);
    if (accepted && refreshMode) lightBarModeAt = now;
    lightingOutput = accepted ? new LightingOutput(frame, brightness, now, note, off)
      : new LightingOutput(null, 0, 0, "", false);
  }
}

static void ApplyLightBarFailure(IntPtr device, int connection) {
  lock (hidLock) {
    if (device == devH && connection == lightingConnection)
      lightingOutput = new LightingOutput(null, 0, 0, "", false);
  }
}
static void SetBrightnessIfChanged(IntPtr h, int briPct) {
  if (briPct == lastBriApplied) return;
  try { SetLedAllBrightness(h, briPct * 255 / 100); lastBriApplied = briPct; } catch { }
}
static void ApplyLed(IntPtr h, string mode, string color, double temp, int effBri) {
  try { SetBrightnessIfChanged(h, effBri); } catch { }
  byte[] c = null; string cn = ColorName(color);
  if (cn != "rainbow") c = HexTo3(cn);
  switch (mode) {
    case "off": case "none": MatrixOff(h); break;
    case "static": if (c != null) MatrixStatic(h, c[0], c[1], c[2]); break;
    case "spectrum": MatrixSpectrum(h); break;
    case "breath": case "breathing": if (c != null) MatrixBreathing(h, c[0], c[1], c[2]); else MatrixBreathing(h, 255, 0, 0); break;
    case "rainbow": LedRainbow(h, 0); break;
    case "wave": LedWave(h, 0, temp); break;
    case "level": LedLevel(h, temp); break;
    case "heatmap": case "temp": LedGradient(h, temp); break;
    default: break;
  }
}
// ---------- settings persistence ----------
static string ConfigPath() {
  try {
    string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
    if (string.IsNullOrEmpty(dir)) dir = Path.GetDirectoryName(Application.ExecutablePath);
    return Path.Combine(dir, "tray.cfg");
  } catch { return "tray.cfg"; }
}
static int GetInt(Dictionary<string, string> d, string k, int def) {
  string v; if (d.TryGetValue(k, out v)) { int r; if (int.TryParse(v.Trim(), out r)) return r; }
  return def;
}
static bool GetBool(Dictionary<string, string> d, string k, bool def) {
  string v; if (!d.TryGetValue(k, out v)) return def;
  v = v.Trim().ToLowerInvariant();
  if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
  if (v == "0" || v == "false" || v == "no" || v == "off") return false;
  return def;
}
// ---- v7 HyperBoost: lift Windows power-saver caps so the GPU/CPU keep full throughput.
// Reversible, AC-only, no admin needed. Disables PCIe ASPM link power management (the main
// GPU-link throttle) and USB selective suspend while active; restores both when turned off
// or when the app exits, so the machine is never left in a modified state unexpectedly. ----
const string ASPM_SETTING = "ee12f906-d277-404b-b6da-e5fa1a576df5";
const string USBSS_SETTING = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
static void RunPowerCfg(string args) {
  try {
    ProcessStartInfo psi = new ProcessStartInfo("powercfg", args);
    psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
    using (Process pr = Process.Start(psi)) {
      try { pr.StandardOutput.ReadToEnd(); } catch { }
      if (!pr.WaitForExit(4000)) { try { pr.Kill(); } catch { } }
    }
  } catch { }
}
static void ApplyHyperBoost(bool on) {
  if (on) {
    RunPowerCfg("/setacvalueindex SCHEME_CURRENT SUB_PCIEXPRESS " + ASPM_SETTING + " 0");
    RunPowerCfg("/setacvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 " + USBSS_SETTING + " 0");
    RunPowerCfg("/setactive SCHEME_CURRENT");
  } else {
    RunPowerCfg("/setacvalueindex SCHEME_CURRENT SUB_PCIEXPRESS " + ASPM_SETTING + " 2");
    RunPowerCfg("/setacvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 " + USBSS_SETTING + " 1");
    RunPowerCfg("/setactive SCHEME_CURRENT");
  }
}static void LoadConfig() {
  lock (cfgLock) {
    try {
      string p = ConfigPath();
      if (!File.Exists(p)) return;
      Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      foreach (string line in File.ReadAllLines(p)) {
        int eq = line.IndexOf('=');
        if (eq <= 0) continue;
        d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
      }
      string sv;
      if (d.TryGetValue("led", out sv) && !string.IsNullOrEmpty(sv)) led = sv.ToLowerInvariant();
      if (d.TryGetValue("ledcolor", out sv) && !string.IsNullOrEmpty(sv)) ledColor = sv;
      // accept the combined "static:red" / "static:FF0000" syntax in the cfg file too
      if (led.StartsWith("static:")) { string cc2 = led.Substring(7); if (cc2.Length > 0) ledColor = cc2; led = "static"; }
      effectColors.Clear();
      foreach (string mode in lightingModes)
        if (d.TryGetValue("color." + mode, out sv) && sv.Length == 6 && HexTo3(sv) != null)
          effectColors[mode] = sv;
      bri = Limit(GetInt(d, "bri", bri), 0, 100);
      ReadEffectLibrary(d);
      interval = GetInt(d, "interval", interval);
      critical = GetInt(d, "critical", critical);
      idleDim = GetBool(d, "idledim", idleDim);
      nightDim = GetBool(d, "nightdim", nightDim);
      keepOnExit = GetBool(d, "keeponexit", keepOnExit);
      coolTail = GetBool(d, "cooltail", coolTail);
      coolTemp = GetInt(d, "cooltemp", coolTemp);
      hyperBoost = GetBool(d, "hyperboost", hyperBoost);
      bool hasProfile = d.TryGetValue("profile", out sv);
      FanMode savedMode = hasProfile ? ParseFanMode(sv) : fanMode;
      fanMode = GetBool(d, "manual", savedMode == FanMode.Manual) ? FanMode.Manual : savedMode;
      manualUnit = Limit(GetInt(d, "manualunit", manualUnit), MIN_UNIT, MAX_UNIT);
    } catch { }
  }
}
static bool SaveConfig() {
  lock (cfgLock) {
    string path = ConfigPath();
    string temporary = path + ".pending";
    try {
      lastSelfSaveMs = Environment.TickCount;
      var lines = new List<string> {
        "; RazerPadTray settings - edited automatically when you use the tray menu.",
        "; The old CLI keeps its own autostart.cfg; this file only configures the tray app.",
        "profile=" + fanMode.ToString().ToLowerInvariant(),
        "led=" + led,
        "ledcolor=" + ledColor,
        "bri=" + bri,
        "interval=" + interval,
        "critical=" + critical,
        "idledim=" + (idleDim ? "true" : "false"),
        "nightdim=" + (nightDim ? "true" : "false"),
        "keeponexit=" + (keepOnExit ? "true" : "false"),
        "cooltail=" + (coolTail ? "true" : "false"),
        "cooltemp=" + coolTemp,
        "hyperboost=" + (hyperBoost ? "true" : "false"),
        "manual=" + (fanMode == FanMode.Manual ? "true" : "false"),
        "manualunit=" + manualUnit,
      };
      foreach (var entry in effectColors) lines.Add("color." + entry.Key + "=" + entry.Value);
      WriteEffectLibrary(lines);
      File.WriteAllLines(temporary, lines);
      if (File.Exists(path)) File.Replace(temporary, path, null);
      else File.Move(temporary, path);
      return true;
    } catch (Exception error) {
      try { File.Delete(temporary); } catch { }
      LogCrash("settings save: " + error.Message);
      return false;
    }
  }
}
static void WorkerLoop() {
  var controller = new FanController();
  var clock = Stopwatch.StartNew();
  int failCount = 0, lastSentUnit = -1;
  long lastSentAt = 0;
  while (_run) {
    try {
      long loopStart = clock.ElapsedMilliseconds;
      if (devH == new IntPtr(-1)) {
        curStatus = "pad not found (retrying...)";
        curUnit = curRpm = -1;
        fanOverride = "";
        WriteStatus();
        lastSentUnit = -1;
        if (!Reopen()) { WaitForFanLoop(clock, loopStart, 3000); continue; }
      }
      IntPtr device = devH;
      double cpu = GetCpuRaw();
      long gpuAt = Interlocked.Read(ref gpuSampleTimestamp);
      double gpu = gpuAt != 0 && Stopwatch.GetTimestamp() - gpuAt <= 12 * Stopwatch.Frequency
        ? Volatile.Read(ref gpuCache) : -1;
      cpuLoad = GetCpuLoad();
      curCpu = FanController.ValidTemperature(cpu) ? (int)Math.Round(cpu) : -1;
      curGpu = FanController.ValidTemperature(gpu) ? (int)Math.Round(gpu) : -1;

      FanMode mode;
      int target, sampleInterval;
      lock (cfgLock) {
        mode = fanMode;
        target = manualUnit;
        sampleInterval = Limit(interval, 500, 2000);
      }
      if (paused) {
        curStatus = "paused";
        WriteStatus();
        WaitForFanLoop(clock, loopStart, 1000);
        continue;
      }

      long now = clock.ElapsedMilliseconds;
      int command = controller.Update(now, cpu, gpu, mode, target);
      bool heartbeat = lastSentUnit < 0 || now - lastSentAt >= 15000;
      bool fanOk = true;
      if (command != lastSentUnit || heartbeat) {
        lock (hidLock) {
          if (heartbeat && GetFanMode(device, fanId) != 1) SetFanMode(device, fanId, 1);
          fanOk = SetFanSpeed(device, fanId, command);
        }
        if (fanOk) { lastSentUnit = command; lastSentAt = now; }
      }
      if (fanOk) {
        failCount = 0;
        curUnit = command;
        curRpm = command * FACTOR;
      } else {
        curUnit = curRpm = -1;
        lastSentUnit = -1;
        if (++failCount >= 3) { failCount = 0; Reopen(); }
      }

      fanOverride = !controller.HasTemperature ? "Temperature unavailable · holding speed" :
                    controller.Emergency ? "Temperature override active" : "";
      curStatus = "CPU " + (curCpu < 0 ? "n/a" : curCpu + "C") +
                  "  GPU " + (curGpu < 0 ? "n/a" : curGpu + "C") +
                  "  Fan " + (curRpm < 0 ? "n/a" : curRpm.ToString()) + "rpm" +
                  (fanOverride.Length == 0 ? "" : "  [" + fanOverride + "]");
      WriteStatus();
      WaitForFanLoop(clock, loopStart, sampleInterval);
    } catch (Exception error) {
      LogCrash("worker loop: " + error);
      WaitForFanLoop(clock, clock.ElapsedMilliseconds, 1000);
    }
  }
}

static void WaitForFanLoop(Stopwatch clock, long start, int intervalMs) {
  long remaining = intervalMs - (clock.ElapsedMilliseconds - start);
  while (remaining > 0 && _run) {
    Thread.Sleep((int)Math.Min(remaining, 150));
    remaining = intervalMs - (clock.ElapsedMilliseconds - start);
  }
}

static System.Drawing.Icon MakeIcon(int temp) {
  using (Bitmap bmp = new Bitmap(32, 32)) {
    using (Graphics g = Graphics.FromImage(bmp)) {
      g.Clear(Color.Transparent);
      g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
      double fraction = Math.Max(0, Math.Min(1, (temp - 45.0) / 45.0));
      int hue = temp < 0 ? 210 : (int)Math.Round(120.0 * (1 - fraction));
      int r, green, b;
      HSV(hue, 90, 100, out r, out green, out b);
      using (var brush = new SolidBrush(Color.FromArgb(r, green, b))) g.FillEllipse(brush, 2, 2, 28, 28);
      using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2)) g.DrawEllipse(pen, 2, 2, 28, 28);
      using (var brush = new SolidBrush(Color.White))
      using (var font = new Font("Segoe UI", 11, FontStyle.Bold)) {
        string label = temp < 0 ? "?" : temp.ToString();
        SizeF size = g.MeasureString(label, font);
        g.DrawString(label, font, brush, (32 - size.Width) / 2f, (32 - size.Height) / 2f);
      }
    }
    curIconH = bmp.GetHicon();
    return System.Drawing.Icon.FromHandle(curIconH);
  }
}

static ToolStripMenuItem Radio(ref List<ToolStripMenuItem> group, string text, string tag, EventHandler onClick) {
  ToolStripMenuItem mi = new ToolStripMenuItem(text);
  mi.Tag = tag;
  mi.Click += onClick;
  group.Add(mi);
  return mi;
}
static List<ToolStripMenuItem> fanItems = new List<ToolStripMenuItem>();
static List<ToolStripMenuItem> ledItems = new List<ToolStripMenuItem>();
static List<ToolStripMenuItem> briItems = new List<ToolStripMenuItem>();

static void CheckGroup(List<ToolStripMenuItem> g, string tag) {
  foreach (var it in g) it.Checked = (string.Equals((string)it.Tag, tag, StringComparison.OrdinalIgnoreCase));
}
static void SelectFanMode(FanMode mode) {
  lock (cfgLock) fanMode = mode;
  CheckGroup(fanItems, mode.ToString());
  SaveConfig();
  SyncDash();
}
static void FanClick(object s, EventArgs e) {
  SelectFanMode(ParseFanMode((string)((ToolStripMenuItem)s).Tag));
}
static void LedClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  string tag = (string)mi.Tag;
  lock (cfgLock) {
    if (tag.StartsWith("static:")) { led = "static"; ledColor = tag.Substring(7); effectColors.Remove("static"); }
    else led = tag;
  }
  string baseTag = tag.StartsWith("static:") ? "static" : tag;
  foreach (var it in ledItems) {
    string t = (string)it.Tag;
    string bt = t.StartsWith("static:") ? "static" : t;
    it.Checked = (bt == "static") ? (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) : (string.Equals(bt, baseTag, StringComparison.OrdinalIgnoreCase));
  }
  ledDirty = true;
  SaveConfig();
  SyncDash();
}
static void BriClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  int v = Convert.ToInt32(mi.Tag);
  SetLightingBrightness(v);
  CheckGroup(briItems, mi.Tag.ToString());
  ledDirty = true;
  SaveConfig();
  SyncDash();
}
static void PauseClick(object s, EventArgs e) {
  paused = !paused;
  if (pauseItem != null) {
    pauseItem.Checked = paused;
    pauseItem.Text = paused ? "Resume automation" : "Pause automation";
  }
  SyncDash();
  DashTick(null, EventArgs.Empty);
  ledDirty = true;
}
static void IdleDimClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  idleDim = !idleDim;
  mi.Checked = idleDim;
  ledDirty = true;
  SaveConfig();
  SyncDash();
}
static void NightDimClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  nightDim = !nightDim;
  mi.Checked = nightDim;
  ledDirty = true;
  SaveConfig();
  SyncDash();
}
static void CoolTailClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  coolTail = !coolTail; mi.Checked = coolTail; SaveConfig();
}
static void HyperBoostClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  hyperBoost = !hyperBoost;
  mi.Checked = hyperBoost;
  ApplyHyperBoost(hyperBoost);
  SaveConfig();
}static void ExitClick(object s, EventArgs e) {
  exiting = true;
  _run = false;
  if (tray != null) { tray.Visible = false; }
  try { Application.Exit(); } catch { try { Application.ExitThread(); } catch { } }
}

// ---------- run-at-logon (HKCU Run key: no administrator rights needed) ----------
const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
const string RunValue = "RazerPadFan";
static string ExePath() {
  try {
    string d = System.Reflection.Assembly.GetExecutingAssembly().Location;
    if (!string.IsNullOrEmpty(d)) return d;
  } catch { }
  try { return Application.ExecutablePath; } catch { }
  return "RazerPadTray.exe";
}
static bool IsAutostartEnabled() {
  try {
    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false)) {
      if (k != null && k.GetValue(RunValue) != null) return true;
    }
  } catch { }
  return false;
}
static void SetAutostart(bool on) {
  try {
    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) {
      if (k == null) return;
      if (on) k.SetValue(RunValue, "\"" + ExePath() + "\" --minimized --status-file \"" + statusFilePath + "\"");
      else if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue, false);
    }
  } catch (Exception ax) { try { LogCrash("autostart: " + ax.Message); } catch { } }
  // retire the older Startup-folder .vbs so we never launch twice
  try {
    string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    string vbs = Path.Combine(startup, "RazerPadFan.vbs");
    if (File.Exists(vbs)) File.Delete(vbs);
  } catch { }
}

static ContextMenuStrip BuildMenu() {
  ContextMenuStrip m = new ContextMenuStrip();
  ToolStripMenuItem openItem = new ToolStripMenuItem("Open dashboard");
  openItem.Font = new Font(openItem.Font, FontStyle.Bold);
  openItem.Click += new EventHandler(delegate (object s2, EventArgs e2) { ShowDashboard(); });
  m.Items.Add(openItem);
  m.Items.Add(new ToolStripSeparator());
  var fan = new ToolStripMenuItem("Fan mode");
  foreach (FanMode mode in Enum.GetValues(typeof(FanMode))) {
    string name = mode.ToString();
    fan.DropDownItems.Add(Radio(ref fanItems, name, name, FanClick));
  }
  m.Items.Add(fan);

  var device = new ToolStripMenuItem("Device settings");
  var coolDown = new ToolStripMenuItem("Wind fan down gently on exit") { Checked = coolTail };
  coolDown.Click += CoolTailClick;
  device.DropDownItems.Add(coolDown);
  var boost = new ToolStripMenuItem("HyperBoost") { Checked = hyperBoost };
  boost.Click += HyperBoostClick;
  device.DropDownItems.Add(boost);
  m.Items.Add(device);

  ToolStripMenuItem lm = new ToolStripMenuItem("Lighting");
  lm.DropDownItems.Add(Radio(ref ledItems, "Heatmap (temp)", "heatmap", new EventHandler(LedClick)));
  lm.DropDownItems.Add(Radio(ref ledItems, "Temperature meter (bar graph)", "level", new EventHandler(LedClick)));
  lm.DropDownItems.Add(Radio(ref ledItems, "Wave (flowing, temp-tinted)", "wave", new EventHandler(LedClick)));
  lm.DropDownItems.Add(Radio(ref ledItems, "Spectrum cycle", "spectrum", new EventHandler(LedClick)));
  lm.DropDownItems.Add(Radio(ref ledItems, "Load meter (CPU | GPU)", "loadmeter", new EventHandler(LedClick)));
  lm.DropDownItems.Add(Radio(ref ledItems, "Breathing", "breath", new EventHandler(LedClick)));
  lm.DropDownItems.Add(Radio(ref ledItems, "Rainbow", "rainbow", new EventHandler(LedClick)));
  ToolStripMenuItem colors = new ToolStripMenuItem("Static color");
  colors.DropDownItems.Add(Radio(ref ledItems, "White", "static:FFFFFF", new EventHandler(LedClick)));
  colors.DropDownItems.Add(Radio(ref ledItems, "Green", "static:green", new EventHandler(LedClick)));
  colors.DropDownItems.Add(Radio(ref ledItems, "Cyan", "static:cyan", new EventHandler(LedClick)));
  colors.DropDownItems.Add(Radio(ref ledItems, "Blue", "static:blue", new EventHandler(LedClick)));
  colors.DropDownItems.Add(Radio(ref ledItems, "Red", "static:red", new EventHandler(LedClick)));
  colors.DropDownItems.Add(Radio(ref ledItems, "Purple", "static:purple", new EventHandler(LedClick)));
  colors.DropDownItems.Add(Radio(ref ledItems, "Orange", "static:orange", new EventHandler(LedClick)));
  colors.DropDownItems.Add(Radio(ref ledItems, "Pink", "static:pink", new EventHandler(LedClick)));
  lm.DropDownItems.Add(colors);
  lm.DropDownItems.Add(Radio(ref ledItems, "Off", "off", new EventHandler(LedClick)));
  m.Items.Add(lm);

  ToolStripMenuItem bm = new ToolStripMenuItem("Brightness");
  bm.DropDownItems.Add(Radio(ref briItems, "25%", "25", new EventHandler(BriClick)));
  bm.DropDownItems.Add(Radio(ref briItems, "50%", "50", new EventHandler(BriClick)));
  bm.DropDownItems.Add(Radio(ref briItems, "75%", "75", new EventHandler(BriClick)));
  bm.DropDownItems.Add(Radio(ref briItems, "100%", "100", new EventHandler(BriClick)));
  bm.DropDownItems.Add(new ToolStripSeparator());
  ToolStripMenuItem idm = new ToolStripMenuItem("Dim automatically when idle/cool");
  idm.Checked = idleDim;
  idm.Click += new EventHandler(IdleDimClick);
  bm.DropDownItems.Add(idm);
  ToolStripMenuItem ndm = new ToolStripMenuItem("Dim automatically at night (22:00-07:00)");
  ndm.Checked = nightDim;
  ndm.Click += new EventHandler(NightDimClick);
  bm.DropDownItems.Add(ndm);
  m.Items.Add(bm);

  m.Items.Add(new ToolStripSeparator());
  ToolStripMenuItem asu = new ToolStripMenuItem("Start with Windows");
  asu.Checked = IsAutostartEnabled();
  asu.Click += delegate { SetAutostart(!IsAutostartEnabled()); SyncDash(); };
  m.Opening += delegate {
    idm.Checked = idleDim;
    ndm.Checked = nightDim;
    asu.Checked = IsAutostartEnabled();
    CheckGroup(fanItems, fanMode.ToString());
    CheckGroup(ledItems, led == "static" ? "static:" + ledColor : CanonicalEffect(led));
    CheckGroup(briItems, bri.ToString());
  };
  m.Items.Add(asu);
  ToolStripMenuItem pause = new ToolStripMenuItem("Pause automation");
  pause.Click += new EventHandler(PauseClick);
  pauseItem = pause;
  m.Items.Add(pause);
  m.Items.Add(new ToolStripSeparator());
  ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
  exit.Click += new EventHandler(ExitClick);
  m.Items.Add(exit);
  return m;
}
static void UiTick(object s, EventArgs e) {
  FanHistory.Record(devH != new IntPtr(-1) && !paused ? curRpm : -1);
  if (coolingHistory != null && !coolingHistory.IsDisposed && coolingHistory.Visible) coolingHistory.Invalidate();
  if (tray == null) return;
  int t = Math.Max(curCpu, curGpu);
  string head = paused ? "RazerPad (paused)" : "RazerPad Fan Control";
  string bodyTxt = "CPU: " + (curCpu < 0 ? "n/a" : curCpu + " C") + "\n" +
                   "GPU: " + (curGpu < 0 ? "n/a" : curGpu + " C") + "\n" +
                   "Fan: " + (curRpm < 0 ? "n/a" : curRpm + " rpm");
  string tip = head + "\n" + bodyTxt;
  if (tip.Length > 63) tip = tip.Substring(0, 63);
  tray.Text = tip;
  string iconSig = (paused ? "P" : "") + ":" + (paused ? -1 : t);
  if (iconSig != lastIconSig) {
    lastIconSig = iconSig;
    int ic = t;
    if (paused) ic = -1;
    try {
      System.Drawing.Icon old = tray.Icon;
      tray.Icon = MakeIcon(ic);
      if (old != null) { try { old.Dispose(); } catch { } }
      if (lastIconH != IntPtr.Zero) { try { DestroyIcon(lastIconH); } catch { } }
      lastIconH = curIconH;
    } catch { }
  }
}

static string statusFilePath = DefaultStatusPath();
static string DefaultStatusPath() {
  try {
    string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
    if (string.IsNullOrEmpty(dir)) dir = Path.GetDirectoryName(Application.ExecutablePath);
    return Path.Combine(dir, "status.txt");
  } catch { return "status.txt"; }
}
static void WriteStatus() {
  if (statusFilePath == null) return;
  try {
    LightingOutput output = lightingOutput;
    string lighting = output == null ? "pending" : output.Frame == null ? "retrying" : "confirmed";
    string frame = output == null || output.Frame == null ? "" :
      " ledbri=" + output.Brightness + " ledframe=" + BitConverter.ToString(output.Frame).Replace("-", "");
    string txt = "ts=" + DateTime.Now.ToString("HH:mm:ss") + " paused=" + paused + " profile=" + fanMode.ToString().ToLowerInvariant() + " led=" + led + " bri=" + bri +
      " cpu=" + curCpu + " gpu=" + curGpu + " unit=" + curUnit + " rpm=" + curRpm +
      " ledsync=" + lighting + frame + " | " + curStatus;
    File.WriteAllText(statusFilePath, txt);
  } catch { }
}
static void StatusPrint() {
  string path = FindDevicePath();
  if (path == null) { Console.WriteLine("Pad not found."); return; }
  IntPtr h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
  if (h == new IntPtr(-1)) { Console.WriteLine("Cannot open pad."); return; }
  try {
    int[] ids = GetIdList(h); int fid = (ids != null && ids.Length > 0) ? ids[0] : 5;
    double c = GetCpuRaw(); double g = GetGpuRaw();
    int sp = GetFanSpeed(h, fid);
    Console.WriteLine("CPU=" + (c < 0 ? "n/a" : c.ToString("0.0") + "C") + " GPU=" + (g < 0 ? "n/a" : g.ToString("0") + "C") + " fanUnit=" + sp + " (~" + (sp * FACTOR) + "rpm)");
  } finally { CloseHandle(h); }
}
[STAThread]
static void Main(string[] args) {
  Application.EnableVisualStyles();
  LoadConfig();
  // command line always wins over tray.cfg
  bool cliKeep = false;
  for (int i = 0; i < args.Length; i++) {
    string a = args[i].ToLowerInvariant();
    if (a == "--interval" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) interval = v; }
    else if ((a == "--maxstep" || a == "--rpmcap" || a == "--captemp" || a == "--deadband" ||
              a == "--downhold" || a == "--coast" || a == "--smooth") &&
             i + 1 < args.Length && !args[i + 1].StartsWith("--")) i++; // legacy fan options
    else if (a == "--led" && i + 1 < args.Length) led = args[++i].ToLowerInvariant();
    else if (a == "--ledcolor" && i + 1 < args.Length) ledColor = args[++i];
    else if (a == "--ledbri" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) bri = v; }
    else if (a == "--critical" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) critical = v; }
    else if (a == "--profile" && i + 1 < args.Length) fanMode = ParseFanMode(args[++i]);
    else if (a == "--idledim") idleDim = true;
    else if (a == "--nightdim") nightDim = true;
    else if (a == "--keep") cliKeep = true;
    else if (a == "--cooltemp" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) coolTemp = v; }
    else if (a == "--nocooltail") coolTail = false;
    else if (a == "--hyperboost") hyperBoost = true;
    else if (a == "--manual") { fanMode = FanMode.Manual; if (i + 1 < args.Length) { int v; if (int.TryParse(args[i + 1], out v)) { manualUnit = v; i++; } } }
    else if (a == "--manualunit" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) { manualUnit = v; fanMode = FanMode.Manual; } }
    else if (a == "--open") openOnStart = true;
    else if (a == "--minimized") openOnStart = false;
  }
  if (interval < 500) interval = 500;
  if (bri < 0) bri = 0; if (bri > 100) bri = 100;
  if (coolTemp < 20) coolTemp = 20; if (coolTemp > 95) coolTemp = 95;
  if (manualUnit < MIN_UNIT) manualUnit = MIN_UNIT; if (manualUnit > MAX_UNIT) manualUnit = MAX_UNIT;
  if (cliKeep) keepOnExit = true;
  foreach (string a0 in args) { if (a0.ToLowerInvariant() == "--status") { StatusPrint(); return; } }
  for (int i = 0; i < args.Length; i++) { if (args[i].ToLowerInvariant() == "--status-file" && i + 1 < args.Length) statusFilePath = args[++i]; }

  const string dashboardEventName = "RazerPadTray_OpenDashboard";
  // A second launch can open the window without starting a second controller.
  var dashboardRequest = new EventWaitHandle(false, EventResetMode.AutoReset, dashboardEventName);
  bool createdNew = false;
  Mutex single = null;
  try { single = new Mutex(true, "RazerPadTray_SingleInstance", out createdNew); } catch { }
  if (single != null && !createdNew) {
    if (openOnStart) dashboardRequest.Set();
    dashboardRequest.Dispose();
    single.Dispose();
    return;
  }

  try {
    string cdir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
    if (string.IsNullOrEmpty(cdir)) cdir = Path.GetDirectoryName(Application.ExecutablePath);
    crashLogPath = Path.Combine(cdir, "crash.log");
  } catch { crashLogPath = "crash.log"; }
  AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(delegate (object s0, UnhandledExceptionEventArgs e0) {
    try { Exception ex = e0.ExceptionObject as Exception; LogCrash("UNHANDLED[" + (e0.IsTerminating ? "term" : "bg") + "] " + (ex == null ? "unknown" : ex.ToString())); } catch { }
  });
  AppDomain.CurrentDomain.ProcessExit += delegate { LogCrash("process exiting"); };
  Application.ThreadException += new ThreadExceptionEventHandler(delegate (object s0, ThreadExceptionEventArgs e0) {
    try { LogCrash("UI-THREAD kept alive: " + (e0.Exception == null ? "unknown" : e0.Exception.ToString())); } catch { }
  });

  tray = new NotifyIcon();
  tray.Icon = MakeIcon(-1);
  tray.Visible = true;
  tray.ContextMenuStrip = BuildMenu();
  tray.MouseDoubleClick += new MouseEventHandler(delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { try { ShowDashboard(); } catch (Exception dex) { try { LogCrash("dblclick: " + dex.Message); } catch { } } } });
  CheckGroup(fanItems, fanMode.ToString());
  CheckGroup(briItems, bri.ToString());
  foreach (var it in ledItems) {
    string t = (string)it.Tag;
    string bt = t.StartsWith("static:") ? "static" : t;
    string lt = led == "static" ? "static:" + ledColor : led;
    it.Checked = string.Equals(t, lt, StringComparison.OrdinalIgnoreCase) ||
                 ((bt != "static" && string.Equals(bt, led, StringComparison.OrdinalIgnoreCase)));
  }

  uiTimer = new System.Windows.Forms.Timer();
  uiTimer.Interval = 1200;
  uiTimer.Tick += new EventHandler(UiTick);
  uiTimer.Start();
  var dashboardRequestTimer = new System.Windows.Forms.Timer { Interval = 150 };
  dashboardRequestTimer.Tick += delegate {
    if (!dashboardRequest.WaitOne(0)) return;
    try { ShowDashboard(); } catch (Exception ex) { LogCrash("open-request: " + ex.Message); }
  };
  dashboardRequestTimer.Start();
  if (openOnStart) { try { ShowDashboard(); } catch (Exception oex) { try { LogCrash("open-on-start: " + oex.Message); } catch { } } }

  worker = new Thread(new ThreadStart(WorkerLoop));
  worker.IsBackground = true;
  worker.Priority = ThreadPriority.AboveNormal;
  worker.Start();

  gpuThread = new Thread(new ThreadStart(GpuLoop));
  gpuThread.IsBackground = true;
  gpuThread.Start();

  ledThread = new Thread(new ThreadStart(LedLoop));
  ledThread.IsBackground = true;
  ledThread.Priority = ThreadPriority.Normal;
  ledThread.Start();

  try {
    string cfgp = ConfigPath();
    cfgWatcher = new FileSystemWatcher();
    cfgWatcher.Path = Path.GetDirectoryName(cfgp);
    cfgWatcher.Filter = Path.GetFileName(cfgp);
    cfgWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
    cfgWatcher.Changed += new FileSystemEventHandler(CfgChanged);
    cfgWatcher.Created += new FileSystemEventHandler(CfgChanged);
    cfgWatcher.Renamed += new RenamedEventHandler(CfgChanged);
    cfgWatcher.EnableRaisingEvents = true;
  } catch { }

  try { if (hyperBoost) ApplyHyperBoost(true); } catch { }
  Application.Run();
  dashboardRequestTimer.Dispose();
  dashboardRequest.Dispose();

  // ---------- cleanup on exit ----------
  _run = false;
  try { if (cfgWatcher != null) { cfgWatcher.EnableRaisingEvents = false; cfgWatcher.Dispose(); } } catch { }
  try { worker.Join(3000); } catch { }
  try { if (ledThread != null) ledThread.Join(1000); } catch { }
  try { if (gpuThread != null) gpuThread.Join(1000); } catch { }
  try { if (hyperBoost) ApplyHyperBoost(false); } catch { }
  try {
    if (devH == new IntPtr(-1)) Reopen();
    if (devH != new IntPtr(-1)) {
      if (!keepOnExit) {
        // Cool-down tail: don't just slam the fan off. Step it down gradually and keep it
        // running until the hardware has actually cooled below coolTemp, then settle to a gentle
        // idle and hand control back to the pad's onboard Auto mode.
        int u = curUnit; if (u < MIN_UNIT || u > MAX_UNIT) u = 30;
        if (coolTail) {
          int safetyLoops = 150;            // ~75s ceiling so we never hang here forever
          while (safetyLoops-- > 0) {
            double ct = -1, gt = gpuCache;
            try { ct = GetCpuRaw(); } catch { }
            double use2 = Math.Max(ct, gt);
            if (use2 >= 0 && use2 < coolTemp) break;   // hardware is cool enough -> stop easing
            try { SetFanSpeed(devH, fanId, u); } catch { }
            if (u > MIN_UNIT) { u -= 2; if (u < MIN_UNIT) u = MIN_UNIT; }
            for (int sc = 0; sc < 5; sc++) Thread.Sleep(100);
          }
        }
        int rest = 30; if (rest < MIN_UNIT) rest = MIN_UNIT; if (rest > MAX_UNIT) rest = MAX_UNIT;
        try { SetFanSpeed(devH, fanId, rest); } catch { }
        try { SetFanMode(devH, fanId, 0); } catch { }   // restore onboard "Auto" mode
      }
      try { lock (hidLock) WriteChroma(devH, ChromaEffect, ChromaOff); } catch { }
      CloseHandle(devH);
      devH = new IntPtr(-1);
    }
  } catch { }
  if (tray != null) { tray.Visible = false; tray.Dispose(); }
  if (single != null) { try { single.ReleaseMutex(); single.Close(); } catch { } }
}
}
}
