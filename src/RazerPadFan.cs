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
using System.Drawing.Drawing2D;
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
// ---------- Compact Graphite dashboard ----------
static Form dash;                      // the dashboard window (null = not open)
static bool exiting = false;           // set when the user truly exits (so closing the window quits)
static System.Windows.Forms.Timer dashTimer;   // ~500ms live-refresh for the open window
static Label lblCpu, lblGpu, lblHead, lblRpm;   // live read-outs
static TrackBar tbFan, tbBri;          // manual fan speed + LED brightness
static Label lblFanVal, lblBriVal;     // slider value captions
static Panel lightingPreview;
static CheckBox ckCoolTail, ckIdleDim, ckNightDim, ckHyper;
static RoundedButton btnPause;
static bool dashSyncing = false;       // guard: suppress slider/toggle events while refreshing the UI
static List<RoundedButton> profBtns = new List<RoundedButton>();
static CheckBox ckAutoStart;           // start the controller when Windows starts
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

// ===================== v8 dashboard: a Synapse-style control window =====================
// Shares the exact engine/threads/globals of the tray (tray stays the source of truth).
// Every control writes the same globals + SaveConfig(), so window and tray menu stay in sync.
static readonly Color GraphiteBackground = Color.FromArgb(15, 16, 27);
static readonly Color GraphitePanel = Color.FromArgb(23, 23, 37);
static readonly Color GraphiteBorder = Color.FromArgb(64, 60, 85);
static readonly Color GraphiteAccent = Color.FromArgb(175, 129, 255);
static readonly Color SurfaceEdge = Color.FromArgb(54, 51, 73);
static Panel manualControls, lightingOptions;
static Label fanModeSummary, overrideStatus, deviceConnection, lightingStatus;
static FanHistory coolingHistory;
static ToolStripMenuItem pauseItem;

[DllImport("dwmapi.dll")]
static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

static void ApplyDashboardCaption(Form window) {
  if (SystemInformation.HighContrast) return;
  const int immersiveDarkModeAttribute = 20;
  const int captionColorAttribute = 35;
  const int textColorAttribute = 36;
  int dark = 1;
  int caption = ColorTranslator.ToWin32(GraphiteBackground);
  int text = ColorTranslator.ToWin32(window.ForeColor);
  DwmSetWindowAttribute(window.Handle, immersiveDarkModeAttribute, ref dark, sizeof(int));
  if (DwmSetWindowAttribute(window.Handle, captionColorAttribute, ref caption, sizeof(int)) == 0)
    DwmSetWindowAttribute(window.Handle, textColorAttribute, ref text, sizeof(int));
}

static void ShowDashboard() {
  if (dash != null && !dash.IsDisposed) {
    SyncDash();
    DashTick(null, EventArgs.Empty);
    dash.Show();
    if (dash.WindowState == FormWindowState.Minimized) dash.WindowState = FormWindowState.Normal;
    dash.Activate();
    dashTimer.Start();
    return;
  }

  Form window = new Form();
  window.Text = "RazerPad Control";
  Rectangle screen = Screen.FromPoint(Cursor.Position).WorkingArea;
  window.ClientSize = new Size(Math.Min(1152, Math.Max(680, screen.Width - 48)),
                               Math.Min(715, Math.Max(460, screen.Height - 84)));
  window.StartPosition = FormStartPosition.CenterScreen;
  window.BackColor = GraphiteBackground;
  window.ForeColor = Color.FromArgb(232, 240, 250);
  window.Font = new Font("Segoe UI", 10f);
  window.MinimumSize = new Size(730, 460);
  window.AutoScaleMode = AutoScaleMode.Dpi;
  window.FormClosing += DashClosing;
  window.ShowIcon = false;
  window.HandleCreated += delegate { ApplyDashboardCaption(window); };

  var shell = new TableLayoutPanel {
    Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(18, 7, 18, 16)
  };
  shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
  shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
  shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
  window.Controls.Add(shell);

  var header = GraphiteGrid(3);
  header.Dock = DockStyle.Fill;
  header.Margin = Padding.Empty;
  header.Padding = new Padding(4, 2, 4, 2);
  header.ColumnStyles[0].Width = 27;
  header.ColumnStyles[1].Width = 46;
  header.ColumnStyles[2].Width = 27;
  shell.Controls.Add(header, 0, 0);
  Label heading = GraphiteText("RazerPad Control", 14, Color.White);
  heading.Dock = DockStyle.Fill;
  heading.AutoEllipsis = true;
  heading.TextAlign = ContentAlignment.MiddleLeft;
  header.Controls.Add(heading, 0, 0);
  var navigation = GraphiteGrid(3, 25);
  navigation.Dock = DockStyle.Fill;
  navigation.Padding = new Padding(4);
  header.Controls.Add(navigation, 1, 0);
  lblHead = GraphiteText("Connecting", 9, WorkspaceMuted);
  lblHead.TextAlign = ContentAlignment.MiddleRight;
  lblHead.Dock = DockStyle.Fill;
  header.Controls.Add(lblHead, 2, 0);

  var pagesHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 13, 0, 0) };
  shell.Controls.Add(pagesHost, 0, 1);
  Panel coolingPage = BuildCoolingPage();
  Panel devicePage = BuildDevicePage();
  Panel lightingPage = WorkspacePage();
  Panel customPage = WorkspacePage();
  var lightingLayout = StudioLayout(lightingPage);
  GraphiteAdd(lightingLayout, GraphiteText("Lighting", 24, Color.White));
  var lightingColumns = StudioColumns(41, 59);
  var visual = GraphiteGrid(1);
  visual.Margin = new Padding(0, 8, 20, 0);
  GraphiteAdd(visual, GraphiteText("Lighting theme", 18, Color.White));
  GraphiteAdd(visual, GraphiteText("Choose one theme", 10, WorkspaceMuted));
  lightingPreview = new PadPreview { Height = 292, AccessibleName = "Active lighting preview" };
  GraphiteAdd(visual, lightingPreview);
  lightingStatus = GraphiteText("Connecting to pad lighting…", 9, WorkspaceMuted);
  lightingStatus.TextAlign = ContentAlignment.MiddleCenter;
  GraphiteAdd(visual, lightingStatus);
  GraphiteAdd(lightingColumns, visual, 0, 0);
  GraphiteAdd(lightingLayout, lightingColumns);

  var editorLayout = StudioLayout(customPage);
  var editorColumns = StudioColumns(37, 63);
  var draftVisual = GraphiteGrid(1);
  draftVisual.Margin = new Padding(0, 32, 22, 0);
  var editorPreview = new PadPreview { Height = 343, AccessibleName = "Custom effect draft preview" };
  GraphiteAdd(draftVisual, editorPreview);
  var draftCaption = GraphiteText("Draft preview", 9, WorkspaceMuted);
  draftCaption.TextAlign = ContentAlignment.MiddleCenter;
  GraphiteAdd(draftVisual, draftCaption);
  GraphiteAdd(editorColumns, draftVisual, 0, 0);
  GraphiteAdd(editorLayout, editorColumns);

  Control[] pages = { coolingPage, lightingPage, devicePage, customPage };
  foreach (Control page in pages) pagesHost.Controls.Add(page);
  var navigationButtons = new List<RoundedButton>();
  CustomEffectEditor editor = null;
  Action<int> showPage = delegate(int selectedPage) {
    lightingGallery.CloseColorPicker();
    if (selectedPage != 3) editor.End();
    for (int index = 0; index < pages.Length; index++) pages[index].Visible = index == selectedPage;
    for (int index = 0; index < navigationButtons.Count; index++) {
      bool selected = index == (selectedPage == 3 ? 1 : selectedPage);
      navigationButtons[index].Quiet = !selected;
      navigationButtons[index].FillColor = PrimaryButtonFill;
      navigationButtons[index].ForeColor = selected ? Color.White : WorkspaceText;
      navigationButtons[index].AccessibleDescription = selected ? "Selected page" : "";
    }
    pages[selectedPage].BringToFront();
  };
  editor = new CustomEffectEditor(delegate { showPage(1); lightingGallery.FocusSelection(); });
  editorPreview.DraftFrame = editor.PreviewFrame;
  GraphiteAdd(editorColumns, editor, 1, 0);
  lightingGallery = new LightingGallery(delegate(SavedEffect entry) {
    editor.Begin(entry);
    showPage(3);
    editor.FocusName();
  });
  GraphiteAdd(lightingColumns, lightingGallery, 1, 0);

  string[] pageNames = { "Cooling", "Lighting", "Device" };
  for (int i = 0; i < pageNames.Length; i++) {
    int index = i;
    RoundedButton tab = GraphiteButton(pageNames[i]);
    tab.Height = 43;
    tab.Dock = DockStyle.Fill;
    tab.Image = InstrumentIcon(pageNames[i]);
    tab.TextImageRelation = TextImageRelation.ImageBeforeText;
    tab.FlatAppearance.BorderSize = 0;
    tab.Margin = new Padding(2, 0, 2, 0);
    tab.Click += delegate { showPage(index); };
    navigationButtons.Add(tab);
    navigation.Controls.Add(tab, i, 0);
  }
  showPage(0);
  dash = window;
  WireToggles();
  lightingOptions.Hide();

  window.Shown += delegate {
    SyncDash();
    DashTick(null, EventArgs.Empty);
  };
  dashTimer = new System.Windows.Forms.Timer { Interval = 500 };
  dashTimer.Tick += DashTick;
  window.Show();
  dashTimer.Start();
}

static Bitmap InstrumentIcon(string kind) {
  Bitmap image = new Bitmap(28, 28);
  using (Graphics g = Graphics.FromImage(image))
  using (Pen pen = new Pen(Color.Silver, 1.7f)) {
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    switch (kind) {
      case "HyperBoost":
        g.DrawPolygon(pen, new[] { new Point(16, 3), new Point(6, 16), new Point(13, 16),
                                   new Point(12, 25), new Point(23, 11), new Point(16, 11) });
        break;
      case "Windows":
        g.DrawPolygon(pen, new[] { new Point(3, 7), new Point(25, 4), new Point(25, 24), new Point(3, 21) });
        g.DrawLine(pen, 13, 6, 13, 22);
        g.DrawLine(pen, 3, 14, 25, 14);
        break;
      case "Lighting":
        g.DrawArc(pen, 6, 3, 16, 17, 145, 250);
        g.DrawLines(pen, new[] { new Point(8, 16), new Point(11, 21), new Point(17, 21), new Point(20, 16) });
        g.DrawLine(pen, 11, 24, 17, 24);
        break;
      case "Device":
        g.DrawPolygon(pen, new[] { new Point(7, 6), new Point(21, 6), new Point(26, 21), new Point(2, 21) });
        g.DrawLine(pen, 4, 24, 24, 24);
        break;
      case "Manual":
        for (int i = 0; i < 3; i++) {
          int y = 7 + i * 7, x = i == 1 ? 18 : 9;
          g.DrawLine(pen, 3, y, x - 3, y);
          g.DrawEllipse(pen, x - 3, y - 3, 6, 6);
          g.DrawLine(pen, x + 3, y, 25, y);
        }
        break;
      case "Pause":
        g.FillRectangle(Brushes.Silver, 8, 5, 4, 18);
        g.FillRectangle(Brushes.Silver, 16, 5, 4, 18);
        break;
      default:
        g.TranslateTransform(14, 14);
        PaintFan(g, 1);
        break;
    }
  }
  return image;
}

static void PaintFan(Graphics g, float scale) {
  var state = g.Save();
  g.ScaleTransform(scale, scale);
  for (int i = 0; i < 4; i++) {
    using (var blade = new System.Drawing.Drawing2D.GraphicsPath()) {
      blade.AddBezier(1, -2, -4, -13, 9, -15, 9, -9);
      blade.AddBezier(9, -9, 8, -5, 5, -3, 1, -2);
      g.FillPath(Brushes.Silver, blade);
    }
    g.RotateTransform(90);
  }
  g.FillEllipse(Brushes.Silver, -2, -2, 4, 4);
  g.Restore(state);
}

sealed class FanHistory : Panel {
  const int MaxHistoryMs = 600000;
  static readonly Queue<KeyValuePair<int, int>> samples = new Queue<KeyValuePair<int, int>>();
  readonly RoundedButton range;
  readonly ContextMenuStrip ranges = new ContextMenuStrip();
  int minutes = 5;

  public FanHistory() {
    DoubleBuffered = true;
    ResizeRedraw = true;
    range = GraphiteButton("Last 5 min  ▾");
    range.AccessibleName = "Fan history range";
    range.Width = 108;
    range.Height = 28;
    range.CornerRadius = 6;
    range.Font = new Font("Segoe UI", 8.5f);
    range.Click += delegate {
      foreach (ToolStripMenuItem item in ranges.Items) item.Checked = (int)item.Tag == minutes;
      ranges.Show(range, new Point(0, range.Height));
    };
    Controls.Add(range);
    ranges.BackColor = GraphitePanel;
    ranges.ForeColor = WorkspaceText;
    foreach (int span in new[] { 1, 5, 10 }) {
      var choice = new ToolStripMenuItem("Last " + span + " min") { Tag = span };
      choice.Click += delegate {
        minutes = span;
        range.Text = choice.Text + "  ▾";
        Invalidate();
      };
      ranges.Items.Add(choice);
    }
  }

  public static void Record(int rpm) {
    int now = Environment.TickCount;
    while (samples.Count != 0 && unchecked(now - samples.Peek().Key) > MaxHistoryMs) samples.Dequeue();
    samples.Enqueue(new KeyValuePair<int, int>(now, rpm));
  }

  protected override void OnResize(EventArgs e) {
    base.OnResize(e);
    if (range != null) range.Location = new Point(Width - range.Width - 12, 7);
  }

  protected override void Dispose(bool disposing) {
    if (disposing) ranges.Dispose();
    base.Dispose(disposing);
  }

  protected override void OnPaint(PaintEventArgs e) {
    base.OnPaint(e);
    if (Width < 140 || Height < 95) return;
    Graphics g = e.Graphics;
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    PaintSurface(g, new Rectangle(1, 1, Width - 3, Height - 3), 13, GraphitePanel, SurfaceEdge, 0);
    using (var grid = new Pen(SurfaceEdge))
    using (var trace = new Pen(GraphiteAccent, 2f)) {
      TextRenderer.DrawText(g, "Fan command history", Font, new Point(16, 11), Color.White);
      Rectangle plot = Rectangle.FromLTRB(67, 45, Width - 24, Height - 35);
      int maxRpm = MAX_UNIT * FACTOR;
      for (int i = 0; i <= 4; i++) {
        int y = plot.Top + i * plot.Height / 4;
        g.DrawLine(grid, plot.Left, y, plot.Right, y);
        TextRenderer.DrawText(g, (maxRpm - i * maxRpm / 4).ToString("N0"), Font,
          new Rectangle(8, y - 9, 49, 18), WorkspaceMuted, TextFormatFlags.Right);
      }
      int segments = plot.Width > 395 && minutes > 1 ? 5 : 2;
      for (int i = 0; i <= segments; i++) {
        int x = plot.Left + i * plot.Width / segments;
        int seconds = minutes * 60 * (segments - i) / segments;
        g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
        string label = seconds == 0 ? "Now" : "−" + (seconds / 60) + ":" + (seconds % 60).ToString("00");
        TextRenderer.DrawText(g, label, Font, new Rectangle(x - 26, plot.Bottom + 7, 52, 18), WorkspaceMuted,
                              TextFormatFlags.HorizontalCenter);
      }
      var points = new List<PointF>();
      int now = Environment.TickCount;
      int window = minutes * 60000;
      int last = 0;
      using (var fill = new LinearGradientBrush(plot, Color.FromArgb(106, 137, 68, 255),
                                                 Color.FromArgb(8, 137, 68, 255), 90f)) {
        foreach (var sample in samples) {
          int age = unchecked(now - sample.Key);
          if (age > window) continue;
          if (sample.Value < 0 || points.Count > 0 && unchecked(sample.Key - last) > 3200) {
            PaintRun(g, trace, fill, points, plot.Bottom);
            points.Clear();
          }
          if (sample.Value < 0) continue;
          points.Add(new PointF(plot.Right - plot.Width * age / (float)window,
            plot.Bottom - plot.Height * Math.Min(maxRpm, sample.Value) / (float)maxRpm));
          last = sample.Key;
        }
        PaintRun(g, trace, fill, points, plot.Bottom);
        if (points.Count > 0) {
          PointF latest = points[points.Count - 1];
          g.FillEllipse(Brushes.White, latest.X - 3, latest.Y - 3, 6, 6);
          g.DrawEllipse(trace, latest.X - 3, latest.Y - 3, 6, 6);
        }
      }
    }
  }

  static void PaintRun(Graphics graphics, Pen trace, Brush fill, List<PointF> points, float bottom) {
    if (points.Count < 2) return;
    PointF[] line = points.ToArray();
    var area = new PointF[line.Length + 2];
    line.CopyTo(area, 0);
    area[line.Length] = new PointF(line[line.Length - 1].X, bottom);
    area[line.Length + 1] = new PointF(line[0].X, bottom);
    graphics.FillPolygon(fill, area);
    graphics.DrawLines(trace, line);
  }
}

sealed class PadPreview : Panel {
  readonly Bitmap pad;
  readonly System.Windows.Forms.Timer refresh;
  byte[] renderedFrame;
  public Func<byte[]> DraftFrame;
  public bool Compact;

  public PadPreview() {
    DoubleBuffered = true;
    ResizeRedraw = true;
    using (Stream asset = typeof(PadPreview).Assembly.GetManifestResourceStream("CoolingPadArtwork"))
    using (var image = Image.FromStream(asset))
      pad = new Bitmap(image);
    refresh = new System.Windows.Forms.Timer { Interval = 33 };
    refresh.Tick += delegate {
      LightingOutput output = lightingOutput;
      byte[] latest = DraftFrame != null ? DraftFrame() : output == null ? null : output.Frame;
      if (!ReferenceEquals(latest, renderedFrame)) {
        renderedFrame = latest;
        Invalidate();
      }
    };
    VisibleChanged += delegate { refresh.Enabled = Visible; };
  }

  protected override void Dispose(bool disposing) {
    if (disposing) {
      if (refresh != null) refresh.Dispose();
      if (pad != null) pad.Dispose();
    }
    base.Dispose(disposing);
  }

  protected override void OnPaint(PaintEventArgs e) {
    base.OnPaint(e);
    if (Width < 65 || Height < 48) return;
    Graphics g = e.Graphics;
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
    float scale = Math.Min((Width - 10) / (float)pad.Width, (Height - (Compact ? 12 : 24)) / (float)pad.Height);
    int w = (int)(pad.Width * scale), h = (int)(pad.Height * scale);
    var device = new Rectangle((Width - w) / 2, (Height - h) / 2 - 2, w, h);
    var ambient = Rectangle.Inflate(device, Math.Max(12, w / 5), Math.Max(24, h / 3));
    using (var halo = new GraphicsPath()) {
      halo.AddEllipse(ambient);
      using (var light = new PathGradientBrush(halo)) {
        light.CenterColor = Color.FromArgb(146, 83, 44, 157);
        light.SurroundColors = new[] { Color.FromArgb(0, 83, 44, 157) };
        g.FillPath(light, halo);
      }
    }
    g.DrawImage(pad, device);
    byte[] frame = renderedFrame;
    if (frame == null) return;
    int lightWidth = (int)(device.Width * .865f);
    int x = device.Left + (device.Width - lightWidth) / 2;
    int y = device.Top + (int)(device.Height * .942f);
    DrawPadLight(g, new Rectangle(x, y, lightWidth, Math.Max(2, device.Height / 115)), frame);
  }

  static void DrawPadLight(Graphics graphics, Rectangle band, byte[] frame) {
    if (frame.All(component => component == 0)) return;
    var palette = new Color[NUM_LED + 2];
    var positions = new float[NUM_LED + 2];
    for (int i = 0; i < NUM_LED; i++) {
      palette[i + 1] = Color.FromArgb(frame[i * 3], frame[i * 3 + 1], frame[i * 3 + 2]);
      positions[i + 1] = (i + .5f) / NUM_LED;
    }
    palette[0] = palette[1];
    palette[palette.Length - 1] = palette[palette.Length - 2];
    positions[positions.Length - 1] = 1;
    PaintPadBand(graphics, band, palette, positions);
  }

  static void PaintPadBand(Graphics graphics, Rectangle band, Color[] colors, float[] positions) {
    using (var shape = RoundedPath(band, Math.Max(1, band.Height / 2)))
    using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(band, Color.Black, Color.Black, 0f)) {
      brush.InterpolationColors = new System.Drawing.Drawing2D.ColorBlend { Colors = colors, Positions = positions };
      graphics.FillPath(brush, shape);
    }
  }
}

static TableLayoutPanel GraphiteGrid(int columns, int railRadius = 0) {
  TableLayoutPanel panel = railRadius == 0 ? new TableLayoutPanel() : new RoundedRail(railRadius);
  panel.AutoSize = true;
  panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
  panel.ColumnCount = columns;
  panel.Margin = Padding.Empty;
  panel.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
  for (int i = 0; i < columns; i++)
    panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
  return panel;
}

sealed class RoundedRail : TableLayoutPanel {
  readonly int radius;

  public RoundedRail(int radius) {
    this.radius = radius;
    BackColor = GraphiteBackground;
    DoubleBuffered = true;
    ResizeRedraw = true;
  }

  public void PaintCapsule(Graphics graphics) {
    PaintSurface(graphics, new Rectangle(1, 1, Width - 3, Height - 3), radius, GraphitePanel, SurfaceEdge);
  }

  protected override void OnPaint(PaintEventArgs e) {
    base.OnPaint(e);
    PaintCapsule(e.Graphics);
  }
}

static void GraphiteAdd(TableLayoutPanel grid, Control control, int column = 0, int row = -1) {
  if (row < 0) {
    row = grid.RowCount++;
    grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    control.Margin = new Padding(control.Margin.Left, 0, control.Margin.Right, 6);
  }
  control.Dock = DockStyle.Top;
  grid.Controls.Add(control, column, row);
}

static readonly Color PrimaryButtonFill = Color.FromArgb(120, 61, 218);

static void UsePrimaryButton(RoundedButton button) {
  button.FillColor = PrimaryButtonFill;
  button.ForeColor = Color.White;
  button.FlatAppearance.BorderSize = 0;
}

static Panel WorkspacePage() {
  return new Panel { AutoScroll = true, Dock = DockStyle.Fill, Padding = new Padding(0, 4, 8, 4) };
}

static void OutlineSurface(Control surface, int radius, int highlight = 18) {
  surface.BackColor = Color.Transparent;
  surface.Resize += delegate { surface.Invalidate(); };
  surface.Paint += delegate(object sender, PaintEventArgs e) {
    PaintSurface(e.Graphics, new Rectangle(1, 1, surface.Width - 3, surface.Height - 3),
                 radius, GraphitePanel, SurfaceEdge, highlight);
  };
}

static void PaintSurface(Graphics graphics, Rectangle bounds, int radius, Color fill, Color edge, int highlight = 18) {
  if (bounds.Width < 2 || bounds.Height < 2) return;
  graphics.SmoothingMode = SmoothingMode.AntiAlias;
  using (var shape = RoundedPath(bounds, radius))
  using (var baseFill = new SolidBrush(fill)) {
    graphics.FillPath(baseFill, shape);
    if (highlight > 0) {
      using (var light = new LinearGradientBrush(bounds, Color.FromArgb(highlight, 167, 119, 234), Color.Transparent, 90f))
        graphics.FillPath(light, shape);
    }
    if (!edge.IsEmpty) {
      using (var outline = new Pen(edge)) graphics.DrawPath(outline, shape);
    }
  }
}

static Label GraphiteText(string text, float size, Color color) {
  return new Label {
    AutoSize = true, Margin = Padding.Empty, Text = text,
    Font = new Font("Segoe UI Semibold", size), ForeColor = color
  };
}

static Label AddTemperature(TableLayoutPanel parent, string name, int column) {
  TableLayoutPanel group = GraphiteGrid(1);
  group.Margin = new Padding(2, 0, 2, 0);
  group.Padding = new Padding(3, 8, 3, 0);
  Label title = GraphiteText(name, 8.5f, WorkspaceMuted);
  title.AutoSize = false;
  title.Height = title.Font.Height + 4;
  title.AutoEllipsis = true;
  title.TextAlign = ContentAlignment.MiddleCenter;
  GraphiteAdd(group, title, 0, 0);
  Label value = GraphiteText("—", 13, GraphiteAccent);
  value.AutoSize = false;
  value.Height = value.Font.Height + 4;
  value.AutoEllipsis = true;
  value.AccessibleName = name == "Fan command" ? "Commanded fan speed" : name + " temperature";
  value.TextAlign = ContentAlignment.MiddleCenter;
  GraphiteAdd(group, value, 0, 1);
  GraphiteAdd(parent, group, column, 0);
  group.Dock = DockStyle.Fill;
  return value;
}

static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle bounds, int radius) {
  var path = new System.Drawing.Drawing2D.GraphicsPath();
  int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
  path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
  path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
  path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
  path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
  path.CloseFigure();
  return path;
}

class RoundedButton : Button {
  public bool Quiet;
  public bool Radio, RadioSelected;
  public int CornerRadius;
  Color fillColor = GraphitePanel;
  bool hovered;

  public Color FillColor {
    get { return fillColor; }
    set { fillColor = value; Invalidate(); }
  }

  public RoundedButton() {
    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
             ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    BackColor = GraphiteBackground;
  }

  protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
  protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }

  void PaintButtonBackground(Graphics graphics) {
    graphics.Clear(Parent == null ? GraphiteBackground : Parent.BackColor);
    var rail = Parent as RoundedRail;
    if (rail == null) return;
    var state = graphics.Save();
    graphics.TranslateTransform(-Left, -Top);
    rail.PaintCapsule(graphics);
    graphics.Restore(state);
  }

  protected override void OnPaint(PaintEventArgs e) {
    PaintButtonBackground(e.Graphics);
    var bounds = new Rectangle(2, 2, Width - 5, Height - 5);
    if (bounds.Width < 2 || bounds.Height < 2) return;
    bool highlight = hovered && Enabled;
    bool focus = Focused && ShowFocusCues;
    if (!Quiet || highlight || focus) {
      Color fill = Enabled ? FillColor : GraphitePanel;
      if (highlight) fill = ControlPaint.Light(fill, .12f);
      Color edge = focus ? WorkspaceText : FlatAppearance.BorderSize > 0 ? FlatAppearance.BorderColor : Color.Empty;
      PaintSurface(e.Graphics, bounds, CornerRadius > 0 ? CornerRadius : bounds.Height / 2, fill, edge);
    }
    Rectangle text = Rectangle.Inflate(bounds, -4, -1);
    TextFormatFlags align = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
    if (Image != null && TextImageRelation == TextImageRelation.ImageAboveText) {
      int top = Math.Max(2, (Height - Image.Height - Font.Height - 5) / 2);
      e.Graphics.DrawImage(Image, (Width - Image.Width) / 2, top);
      text.Y = top + Image.Height + 3;
      text.Height = Height - text.Y - 4;
    } else if (Image != null && TextImageRelation == TextImageRelation.ImageBeforeText) {
      int left = Math.Max(4, (Width - Image.Width - TextRenderer.MeasureText(Text, Font).Width) / 2);
      var icon = new Rectangle(left, (Height - Image.Height) / 2, Image.Width, Image.Height);
      if (ForeColor == GraphiteBackground) {
        using (var tint = new System.Drawing.Imaging.ImageAttributes()) {
          tint.SetRemapTable(new[] { new System.Drawing.Imaging.ColorMap { OldColor = Color.Silver, NewColor = GraphiteBackground } });
          e.Graphics.DrawImage(Image, icon, 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel, tint);
        }
      } else e.Graphics.DrawImage(Image, icon);
      text.X = left + Image.Width;
      text.Width = Width - text.X - 4;
      align = TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
    } else if (TextAlign == ContentAlignment.MiddleLeft) {
      text.X += 6;
      text.Width -= 6;
      align = TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
    }
    if (Radio) {
      int y = Height / 2;
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      using (var ring = new Pen(RadioSelected ? GraphiteAccent : WorkspaceMuted))
        e.Graphics.DrawEllipse(ring, 11, y - 5, 10, 10);
      if (RadioSelected)
        using (var dot = new SolidBrush(GraphiteAccent)) e.Graphics.FillEllipse(dot, 14, y - 2, 4, 4);
      text.X += 15;
      text.Width -= 15;
    }
    TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? ForeColor : Color.Gray,
                          align | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
                          TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
  }
}

static RoundedButton GraphiteButton(string text) {
  var button = new RoundedButton {
    Height = 38, Margin = Padding.Empty, Text = text, FlatStyle = FlatStyle.Flat,
    ForeColor = Color.White, Cursor = Cursors.Hand,
    UseVisualStyleBackColor = false
  };
  button.FlatAppearance.BorderColor = SurfaceEdge;
  return button;
}

sealed class InstrumentSlider : TrackBar {
  const int KeyDownMessage = 0x0100;
  const int MouseMoveMessage = 0x0200;
  const int LeftDownMessage = 0x0201;
  const int LeftUpMessage = 0x0202;
  const int LeftDoubleClickMessage = 0x0203;
  const int MouseWheelMessage = 0x020A;
  const int RailInset = 12;
  const int ThumbRadius = 7;
  int pendingWheelDelta;
  bool dragging;
  int startValue;
  float dragOffset;

  public event EventHandler ValueCommitted;

  int RailWidth { get { return Math.Max(1, ClientSize.Width - RailInset * 2); } }
  float ThumbX {
    get { return RailInset + RailWidth * (Value - Minimum) / (float)Math.Max(1, Maximum - Minimum); }
  }

  public InstrumentSlider() {
    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
             ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    Cursor = Cursors.Hand;
  }

  void SetFromPointer(int x) {
    float ratio = (x - dragOffset - RailInset) / RailWidth;
    Value = Limit(Minimum + (int)Math.Round(ratio * (Maximum - Minimum)), Minimum, Maximum);
  }

  bool MoveFromKey(Keys key) {
    int target;
    switch (key) {
      case Keys.Left: case Keys.Down: target = Value - SmallChange; break;
      case Keys.Right: case Keys.Up: target = Value + SmallChange; break;
      case Keys.PageDown: target = Value - LargeChange; break;
      case Keys.PageUp: target = Value + LargeChange; break;
      case Keys.Home: target = Minimum; break;
      case Keys.End: target = Maximum; break;
      default: return false;
    }
    Value = Limit(target, Minimum, Maximum);
    return true;
  }

  void CommitValue() {
    if (ValueCommitted != null) ValueCommitted(this, EventArgs.Empty);
  }

  protected override void OnMouseCaptureChanged(EventArgs e) {
    base.OnMouseCaptureChanged(e);
    if (!dragging || Capture) return;
    dragging = false;
    if (Value != startValue) CommitValue();
  }

  protected override void WndProc(ref Message message) {
    // Native navigation can move the thumb without notifying the managed control.
    if (message.Msg == KeyDownMessage && MoveFromKey((Keys)message.WParam.ToInt32())) {
      message.Result = IntPtr.Zero;
      return;
    }
    int x = unchecked((short)message.LParam.ToInt64());
    if (message.Msg == LeftDownMessage || message.Msg == LeftDoubleClickMessage) {
      Focus();
      startValue = Value;
      dragging = true;
      dragOffset = Math.Abs(x - ThumbX) <= ThumbRadius ? x - ThumbX : 0;
      Capture = true;
      SetFromPointer(x);
      message.Result = IntPtr.Zero;
      return;
    }
    if (dragging && (message.Msg == MouseMoveMessage || message.Msg == LeftUpMessage)) {
      SetFromPointer(x);
      if (message.Msg == LeftUpMessage) Capture = false;
      message.Result = IntPtr.Zero;
      return;
    }
    if (message.Msg == MouseWheelMessage) {
      // Consume wheel input here so the native trackbar cannot move or paint its own thumb.
      pendingWheelDelta += unchecked((short)(message.WParam.ToInt64() >> 16));
      int detent = SystemInformation.MouseWheelScrollDelta;
      int steps = pendingWheelDelta / detent;
      pendingWheelDelta %= detent;
      if (steps != 0) Value = Math.Max(Minimum, Math.Min(Maximum, Value + steps * SmallChange));
      message.Result = IntPtr.Zero;
      return;
    }
    base.WndProc(ref message);
  }

  protected override void OnValueChanged(EventArgs e) {
    base.OnValueChanged(e);
    Invalidate();
    if (!dragging) CommitValue();
  }

  protected override void OnPaint(PaintEventArgs e) {
    e.Graphics.Clear(BackColor);
    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    int y = ClientSize.Height / 2;
    float thumb = ThumbX;
    var handle = new RectangleF(thumb - ThumbRadius, y - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);
    using (Pen rail = new Pen(SurfaceEdge, 6))
    using (Pen progress = new Pen(GraphiteAccent, 5))
    using (Brush shadow = new SolidBrush(Color.FromArgb(90, Color.Black)))
    using (Brush fill = new LinearGradientBrush(handle, Color.FromArgb(197, 228, 255), GraphiteAccent, 90f)) {
      rail.StartCap = rail.EndCap = System.Drawing.Drawing2D.LineCap.Round;
      progress.StartCap = progress.EndCap = System.Drawing.Drawing2D.LineCap.Round;
      e.Graphics.DrawLine(rail, RailInset, y, RailInset + RailWidth, y);
      if (Value > Minimum) e.Graphics.DrawLine(progress, RailInset, y, thumb, y);
      e.Graphics.FillEllipse(shadow, thumb - 9, y - 6, 18, 18);
      e.Graphics.FillEllipse(fill, handle);
    }
    if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
  }
}

static TrackBar AddSlider(TableLayoutPanel grid, string name, int min, int max, out Label value, EventHandler changed,
                          bool persist = false) {
  TableLayoutPanel heading = GraphiteGrid(2);
  heading.ColumnStyles.Clear();
  heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
  heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
  GraphiteAdd(heading, GraphiteText(name, 10, Color.White), 0, 0);
  value = GraphiteText("", 10, GraphiteAccent);
  value.TextAlign = ContentAlignment.MiddleRight;
  GraphiteAdd(heading, value, 1, 0);
  GraphiteAdd(grid, heading);
  var slider = new InstrumentSlider {
    Minimum = min, Maximum = max, Value = min, SmallChange = 1, LargeChange = 5,
    TickStyle = TickStyle.None, AutoSize = false, Height = 25,
    Margin = Padding.Empty, BackColor = GraphiteBackground, AccessibleName = name
  };
  slider.ValueChanged += changed;
  if (persist) slider.ValueCommitted += delegate { if (!dashSyncing) SaveConfig(); };
  GraphiteAdd(grid, slider);
  return slider;
}

sealed class GraphiteToggle : CheckBox {
  public Image Icon;

  protected override void Dispose(bool disposing) {
    if (disposing && Icon != null) Icon.Dispose();
    base.Dispose(disposing);
  }
  protected override void OnCheckedChanged(EventArgs e) {
    base.OnCheckedChanged(e);
    Invalidate();
  }

  protected override void OnPaint(PaintEventArgs e) {
    e.Graphics.Clear(Parent == null ? GraphiteBackground : Parent.BackColor);
    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    var track = new Rectangle(Width - 39, (Height - 20) / 2, 36, 20);
    if (track.Left < 0) return;
    PaintSurface(e.Graphics, track, 10, Checked ? PrimaryButtonFill : GraphitePanel, SurfaceEdge);
    int thumbX = Checked ? track.Right - 17 : track.Left + 3;
    var knob = new Rectangle(thumbX, track.Top + 3, 14, 14);
    using (var fill = new LinearGradientBrush(knob, Color.White, Checked ? WorkspaceText : WorkspaceMuted, 90f))
      e.Graphics.FillEllipse(fill, knob);
    int left = 3;
    if (Icon != null) {
      e.Graphics.DrawImage(Icon, new Rectangle(4, (Height - 22) / 2, 22, 22));
      left = 38;
    }
    TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(left, 0, Math.Max(0, track.Left - left), Height),
                          Enabled ? ForeColor : Color.Gray, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                          TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
  }
}

static CheckBox AddCheck(TableLayoutPanel grid, string text) {
  CheckBox check = new GraphiteToggle { Text = text, Height = 34, Margin = Padding.Empty, Cursor = Cursors.Hand };
  GraphiteAdd(grid, check);
  return check;
}

static void AddDisclosure(TableLayoutPanel parent, string title, Panel options) {
  Button button = GraphiteButton(title + "    ▾");
  button.Height = 48;
  button.TextAlign = ContentAlignment.MiddleLeft;
  button.Click += delegate {
    options.Visible = !options.Visible;
    button.Text = title + (options.Visible ? "    ▴" : "    ▾");
  };
  GraphiteAdd(parent, button);
  GraphiteAdd(parent, options);
}
static Color ParseHex(string hex) {
  try {
    if (string.IsNullOrEmpty(hex) || hex == "rainbow") return Color.Lime;
    hex = hex.Trim().TrimStart('#');
    if (hex.Length == 3) return Color.FromArgb(Convert.ToInt32(new string(hex[0], 2), 16), Convert.ToInt32(new string(hex[1], 2), 16), Convert.ToInt32(new string(hex[2], 2), 16));
    if (hex.Length == 6) return Color.FromArgb(Convert.ToInt32(hex.Substring(0, 2), 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16));
  } catch { }
  return Color.Lime;
}
static void FanSliderChanged(object s, EventArgs e) {
  if (dashSyncing) return;
  int v = tbFan.Value;
  lock (cfgLock) manualUnit = v;
  if (lblFanVal != null) lblFanVal.Text = (v * FACTOR).ToString("N0") + " RPM";
}
static void BriSliderChanged(object s, EventArgs e) {
  if (dashSyncing) return;
  int v = tbBri.Value;
  SetLightingBrightness(v);
  ledDirty = true;
  CheckGroup(briItems, v.ToString());
  if (lblBriVal != null) lblBriVal.Text = v.ToString() + "%";
  if (lightingPreview != null) lightingPreview.Invalidate();
}
static void SyncDash() {
  if (dash == null || dash.IsDisposed) return;
  try {
    dashSyncing = true;
    FanMode currentMode;
    lock (cfgLock) currentMode = fanMode;
    CheckGroup(fanItems, currentMode.ToString());
    foreach (RoundedButton button in profBtns) {
      string key = (string)button.Tag;
      bool selected = string.Equals(currentMode.ToString(), key, StringComparison.OrdinalIgnoreCase);
      button.Quiet = !selected;
      button.RadioSelected = selected;
      button.FillColor = selected ? Color.FromArgb(45, 32, 75) : GraphitePanel;
      button.FlatAppearance.BorderColor = selected ? GraphiteAccent : SurfaceEdge;
      button.ForeColor = selected ? GraphiteAccent : Color.White;
      button.AccessibleDescription = selected ? "Selected fan profile" : "";
    }
    manualControls.Visible = currentMode == FanMode.Manual;
    fanModeSummary.Visible = currentMode != FanMode.Manual;
    fanModeSummary.Text = currentMode == FanMode.Quiet ? "Up to 1,900 RPM normally" : "Up to 2,300 RPM normally";
    tbFan.Value = Math.Max(MIN_UNIT, Math.Min(MAX_UNIT, manualUnit));
    lblFanVal.Text = (manualUnit * FACTOR).ToString("N0") + " RPM";
    lightingGallery.SyncSelection();
    lightingPreview.Invalidate();
    if (tbBri != null && tbBri.Value != bri) tbBri.Value = Math.Max(0, Math.Min(100, bri));
    if (lblBriVal != null) lblBriVal.Text = bri.ToString() + "%";
    if (ckCoolTail != null) ckCoolTail.Checked = coolTail;
    if (ckIdleDim != null) ckIdleDim.Checked = idleDim;
    if (ckNightDim != null) ckNightDim.Checked = nightDim;
    if (ckHyper != null) ckHyper.Checked = hyperBoost;
    if (ckAutoStart != null) ckAutoStart.Checked = IsAutostartEnabled();
    btnPause.Text = paused ? "Resume" : "Pause";
    dashSyncing = false;
  } catch (Exception ex) { dashSyncing = false; try { LogCrash("SyncDash: " + ex.Message); } catch { } }
}
// per-toggle handlers wired after Check() so each writes exactly one setting
static void WireToggles() {
  if (ckCoolTail != null) ckCoolTail.CheckedChanged += new EventHandler(delegate (object s, EventArgs e) {
    if (dashSyncing) return;
    coolTail = ckCoolTail.Checked; SaveConfig();
  });
  if (ckIdleDim != null) ckIdleDim.CheckedChanged += new EventHandler(delegate (object s, EventArgs e) {
    if (dashSyncing) return;
    idleDim = ckIdleDim.Checked; ledDirty = true; SaveConfig();
  });
  if (ckNightDim != null) ckNightDim.CheckedChanged += new EventHandler(delegate (object s, EventArgs e) {
    if (dashSyncing) return;
    nightDim = ckNightDim.Checked; ledDirty = true; SaveConfig();
  });
  if (ckHyper != null) ckHyper.CheckedChanged += new EventHandler(delegate (object s, EventArgs e) {
    if (dashSyncing) return;
    hyperBoost = ckHyper.Checked; ApplyHyperBoost(hyperBoost); SaveConfig();
  });
  if (ckAutoStart != null) ckAutoStart.CheckedChanged += new EventHandler(delegate (object s, EventArgs e) {
    if (dashSyncing) return;
    SetAutostart(ckAutoStart.Checked);
    SyncDash();
  });
}
static void DashTick(object sender, EventArgs e) {
  if (dash == null || dash.IsDisposed) return;
  int cpu = curCpu, gpu = curGpu, rpm = curRpm;
  lblCpu.Text = cpu < 0 ? "--" : cpu + " °C";
  lblGpu.Text = gpu < 0 ? "--" : gpu + " °C";
  lblRpm.Text = rpm < 0 ? "— RPM" : rpm.ToString("N0") + " RPM";
  bool connected = devH != new IntPtr(-1);
  lblHead.Text = connected ? "●  USB connected" : "○  USB disconnected";
  lblHead.ForeColor = connected ? Color.FromArgb(126, 228, 168) : WorkspaceMuted;
  deviceConnection.Text = connected ? "●  USB connected" : "○  USB disconnected";
  deviceConnection.ForeColor = lblHead.ForeColor;
  LightingOutput output = lightingOutput;
  if (!connected) lightingStatus.Text = "Lighting preview unavailable · USB disconnected";
  else if (output == null) lightingStatus.Text = "Waiting for the pad’s lighting response…";
  else if (output.Frame == null) lightingStatus.Text = "Lighting unconfirmed · retrying";
  else if (output.Note == "Paused") lightingStatus.Text = "USB confirmed · lighting paused";
  else if (output.Off && output.Note.Length == 0) lightingStatus.Text = "USB confirmed · lights off";
  else lightingStatus.Text = "USB confirmed · " + output.Brightness + "%" +
      (output.Note.Length == 0 ? "" : " (" + output.Note + ")");
  overrideStatus.Text = paused ? "Fan control paused" : connected ? fanOverride : "";
}

static void DashClosing(object s, FormClosingEventArgs e) {
  // Close (X) hides the window to the tray unless the user chose "Exit" (exiting=true).
  if (!exiting) {
    e.Cancel = true;
    try { dash.Visible = false; } catch { }
    if (dashTimer != null) { try { dashTimer.Stop(); } catch { } }
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
