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
internal static class Program {
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
static bool Transact(IntPtr h, byte classId, byte cmd, byte[] payload, int forcedLen, int waitMs, out byte[] reply) {
  byte[] wire = new byte[WIRE];
  int plen = payload == null ? 0 : payload.Length;
  int len = (forcedLen > 0) ? forcedLen : 3 + plen;
  byte tid = (byte)NextTid();
  wire[0] = REPORT_ID;
  wire[2] = tid;
  wire[6] = (byte)len;
  wire[7] = classId;
  wire[8] = cmd;
  if (payload != null) for (int i = 0; i < plen; i++) wire[9 + i] = payload[i];
  byte cs = 0;
  for (int n = 3; n < 89; n++) cs ^= wire[n];
  wire[89] = cs;
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
}// Fast fire-and-forget write: a single SetFeature + one read flush, NO retry loop and NO
// sleep. Measured at ~1ms/frame, this is what unlocks smooth ~25-30fps animation. Returns
// success but callers (animation) may ignore it - a dropped frame is harmless.
static bool FastWrite(IntPtr h, byte classId, byte cmd, byte[] payload, int forcedLen) {
  byte[] reply;
  try { return Transact(h, classId, cmd, payload, forcedLen, 0, out reply); }
  catch { reply = null; return false; }
}
static void MatrixCustomFast(IntPtr h, byte[] rgb18) {
  byte[] p = new byte[1 + NUM_LED * 3]; p[0] = UE_CUSTOMFRAME;
  for (int i = 0; i < NUM_LED * 3; i++) p[1 + i] = rgb18[i];
  lock (hidLock) { FastWrite(h, CLASS_LED, LED_SET_MATRIX, p, 3 + p.Length); }
}
static void MatrixOffFast(IntPtr h) { lock (hidLock) { FastWrite(h, CLASS_LED, LED_SET_MATRIX, new byte[] { UE_OFF }, 3 + 1); } }
static void MatrixStaticFast(IntPtr h, int r, int g, int b) { lock (hidLock) { FastWrite(h, CLASS_LED, LED_SET_MATRIX, new byte[] { UE_STATIC, (byte)r, (byte)g, (byte)b }, 3 + 4); } }
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
class CP { public int T; public int R; public CP(int t, int r) { T = t; R = r; } }
static int RoundTo10(int v) { if (v < 10) return 10; if (v > 64) return 64; return v; }
static int ComputeRpm(List<CP> pts, int temp, bool gpu) {
  if (pts.Count == 0) return 500;
  CP first = pts[0];
  if (temp < first.T) {
    int baseT = gpu ? 30 : 20;
    if (first.T <= baseT) return Math.Max(500, first.R);
    double f = (double)(temp - baseT) / (first.T - baseT);
    double rpm = 500.0 + f * (first.R - 500.0);
    if (rpm < 500.0) rpm = 500.0;
    return (int)Math.Round(rpm);
  }
  CP last = pts[pts.Count - 1];
  if (temp >= last.T) return last.R;
  for (int i = 0; i < pts.Count - 1; i++) {
    CP a = pts[i], b = pts[i + 1];
    if (temp >= a.T && temp < b.T) {
      if (b.T == a.T) return a.R;
      double f = (double)(temp - a.T) / (b.T - a.T);
      return (int)Math.Round(a.R + f * (b.R - a.R));
    }
  }
  return last.R;
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
          if (maxT > 0) gpuCache = maxT;
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
    LogCrash("hot-reload: led=" + led + " profile=" + prof + " bri=" + bri);
  } catch { }
}
static volatile bool paused = false;
static volatile int curCpu = -1, curGpu = -1, curUnit = -1, curRpm = -1;
static volatile string curStatus = "starting...";
static volatile bool ledDirty = true;
static volatile int lastBriApplied = -1;
static string prof = "balanced";
static string led = "heatmap";
static string ledColor = "green";
static int bri = 80;
static int interval = 2000;
static int critical = 85;
static readonly object cfgLock = new object();
static bool keepOnExit = false;
// ---------- v3 additions ----------
static int maxStep = 6;            // fan-unit change limit per loop (0 = instant, no smoothing)
static bool autoTurbo = true;      // sustained very-high temp forces a timed max-fan cooling run
static bool idleDim = true;        // dim the light bar while the laptop is cool/idle
static bool nightDim = false;      // dim the light bar between 22:00 and 07:00
const int IDLE_BRI = 25;
const int NIGHT_BRI = 25;
const int TURBO_MS = 120000;       // duration of a manual "Turbo burst" (2 minutes)
static int manualTurboUntil;      // Environment.TickCount deadline
static volatile string fanOverride = "";
static double gpuCache = -1;       // latest GPU temp from the background sampler
static double gpuLoad = -1;        // latest GPU utilisation % from nvidia-smi
static double cpuLoad = -1;        // latest CPU utilisation % from PDH
static Thread gpuThread;                    // background sensor sampler
static Thread ledThread;                    // smooth lighting driver
static readonly object hidLock = new object();  // serialise every HID feature write
static bool loadBoost = true;               // pre-raise the fan on heavy load, before temp spikes
static int rpmCap = 2300;            // fan-wear cap: hold fan at/below this rpm while cool (0 = off)
static int capTemp = 60;             // at/above this temp the wear cap lifts (fan may exceed rpmCap)
static int deadband = 3;             // ignore target changes smaller than this (~150rpm) => steady speed
static int downHold = 6;             // loops a LOWER target must persist (6 x 2s = 12s) before the fan actually slows
static bool coolTail = true;         // gradually slow the fan to a stop on exit once hardware is cool
static int coolTemp = 45;            // target temperature for the exit cool-down tail
static bool hyperBoost = false;      // v7: lift OS power-saver caps (PCIe ASPM / USB suspend) for max GPU throughput
// ---------- v8 additions ----------
static bool manual = false;            // manual fan mode: a fixed user-set speed, bypassing the temp curve
static int manualUnit = 30;            // manual fan speed in raw units (10-64 -> ~500-3200rpm)
// ---------- v8 thermal model (auto mode: hold a CONSTANT speed, move only when it is truly needed) ----------
static double pidI = 0;              // integral of the temperature error: the term that FINDS the holding speed
static double emaT = -1;             // low-pass filter over the raw sensor so noise can never twitch the fan
static int lastMeas = -1;            // previous smoothed temp -> gives the heating/cooling rate (derivative)
static double lastDeriv = 0;
const double emaAlphaFor = 0.12;   // filter coefficient used in Steady mode: ~17s time constant, absorbs sensor jitter
static double emaAlpha = 0.35;          // current filter coefficient (ApplySmooth lowers it in Steady mode)
// Integral-dominant control is deliberate. A pure P/PI controller over-responds to the sensor noise on this
// pad, which is exactly the "sped up and slowed down every few seconds" symptom. The integral accumulates
// only while the temperature is inside the safe band, so the fan settles onto the LOWEST speed that holds
// the temperature and then sits there instead of chasing every wiggle.
const double Kp = 0.3;                    // fan units per degree of error
const double Ki = 0.02;                   // fan units per degree-second (slow, steady authority)
const double Kd = 1.4;                    // only active while WARMING (damps overshoot); ignored while cooling
const double IBAND_LO = 40, IBAND_HI = 80; // integrate only inside this band (no windup when cold / critical)
const double IMAX = 14;                   // anti-windup clamp on the integral correction (+/-14 units = +/-700rpm)
static int coast = 15;                  // anti-hunt: seconds the fan waits before easing DOWN one unit (slow coast-down, prevents pump up/down oscillation)
// ---------- Compact Graphite dashboard ----------
static Form dash;                      // the dashboard window (null = not open)
static bool exiting = false;           // set when the user truly exits (so closing the window quits)
static System.Windows.Forms.Timer dashTimer;   // ~500ms live-refresh for the open window
static Label lblCpu, lblGpu, lblHead;   // live read-outs
static TrackBar tbFan, tbBri;          // manual fan speed + LED brightness
static Label lblFanVal, lblBriVal;     // slider value captions
static ComboBox cbLed;                 // lighting effect selector
static CheckBox ckAutoTurbo, ckLoadBoost, ckWearCap, ckSteady, ckCoolTail, ckIdleDim, ckNightDim, ckHyper; // option toggles
static Button btnTurbo, btnPause;  // action buttons
static bool dashSyncing = false;       // guard: suppress slider/toggle events while refreshing the UI
static List<Button> profBtns = new List<Button>();    // profile quick-select buttons
static Dictionary<string, Button> colorSwatches = new Dictionary<string, Button>();
static TrackBar tbSmooth;              // fan steadiness: how hard the controller resists speed changes
static Label lblSmoothVal;
static CheckBox ckAutoStart;           // start the controller when Windows starts
static int smooth = 45;                // 0 = jumps at once ... 100 = very lazy / rock steady
static bool openOnStart = true;      // show the dashboard when launched by hand; the logon autostart passes --minimized
static bool NotExpired(int now, int until) {
  if (until == 0) return false;
  return unchecked(until - now) > 0;  // elapsed arithmetic also works across TickCount rollover
}
// device handle shared between the worker and menu actions (only the worker touches it)
static IntPtr devH = new IntPtr(-1);
static int fanId = 5;
// Maps a single 0-100 "steadiness" slider onto the three real anti-hunt knobs.
//   deadband  = how big a change has to be before we act (rpm jitter gate)
//   downHold  = how long a LOWER target must persist before we slow down (seconds / interval)
//   coast     = seconds between each single-step reduction (how lazily we ease down)
static void ApplySmooth(int v) {
  if (v < 0) v = 0; if (v > 100) v = 100;
  smooth = v;
  deadband = 1 + v / 20;                    // 1 .. 6 units (50..300 rpm)
  if (v <= 2) emaAlpha = 0.35; else emaAlpha = 0.30 - v * 0.18 / 100;   // instant=reactive, steady=heavily damped
  downHold = (2 + v / 10) * 1000 / (interval < 500 ? 500 : interval);   // ~2s .. 12s of hold
  if (downHold < 1) downHold = 1;
  coast = 5 + v * 35 / 100;                 // 5 .. 40 seconds per 1-unit reduction
  emaAlpha = emaAlphaFor;                       // steady default -> slow, noise-immune filter; instant -> fast
}
static List<CP> ProfileCurve(string p, out int fixedU, out int rMin, out int rMax) {
  fixedU = -1; rMin = 500; rMax = 3200;
  List<CP> pts = new List<CP>();
  switch (p) {
    case "quiet":
      rMax = 2100;
      pts.Add(new CP(45, 900)); pts.Add(new CP(55, 1200)); pts.Add(new CP(65, 1600));
      pts.Add(new CP(75, 1900)); pts.Add(new CP(85, 2000)); pts.Add(new CP(95, 2100));
      break;
    case "performance":
      pts.Add(new CP(40, 1500)); pts.Add(new CP(50, 2000)); pts.Add(new CP(60, 2400));
      pts.Add(new CP(70, 2800)); pts.Add(new CP(80, 3100)); pts.Add(new CP(90, 3200));
      break;
    case "turbo":
      fixedU = 64;
      pts.Add(new CP(45, 3200));
      break;
    case "balanced":
    default:
      pts.Add(new CP(45, 1000)); pts.Add(new CP(55, 1500)); pts.Add(new CP(65, 2000));
      pts.Add(new CP(75, 2500)); pts.Add(new CP(85, 2900)); pts.Add(new CP(95, 3200));
      break;
  }
  return pts;
}

static bool Reopen() {
  lock (hidLock) {
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
static void LedRainbowFast(IntPtr h, double ph, int bri) {
  byte[] rgb = new byte[NUM_LED * 3];
  for (int i = 0; i < NUM_LED; i++) {
    int hu = (int)(ph + i * (360.0 / NUM_LED)) % 360;
    int r, g, b; HSV(hu, 100, 100, out r, out g, out b);
    rgb[i * 3] = Bake(r, bri); rgb[i * 3 + 1] = Bake(g, bri); rgb[i * 3 + 2] = Bake(b, bri);
  }
  MatrixCustomFast(h, rgb);
}
static void LedWaveFast(IntPtr h, double ph, double temp, int bri) {
  int baseH = TempHue(temp);
  byte[] rgb = new byte[NUM_LED * 3];
  for (int i = 0; i < NUM_LED; i++) {
    double s = Math.Sin((i * 20.0 + ph * 6.0) * Math.PI / 180.0);
    int hu = (int)(baseH + 40.0 * s);
    double vv = 78.0 + 22.0 * (0.5 + 0.5 * Math.Sin((i * 24.0 - ph * 8.0) * Math.PI / 180.0));
    int r, g, b; HSV(hu, 100, (int)vv, out r, out g, out b);
    rgb[i * 3] = Bake(r, bri); rgb[i * 3 + 1] = Bake(g, bri); rgb[i * 3 + 2] = Bake(b, bri);
  }
  MatrixCustomFast(h, rgb);
}
static void LedLevelFast(IntPtr h, double temp, int bri) {
  double f = (temp - 30.0) / 70.0; if (f < 0) f = 0; if (f > 1) f = 1;
  int lit = (int)Math.Round(f * NUM_LED); if (lit < 1) lit = 1; if (lit > NUM_LED) lit = NUM_LED;
  int hue = TempHue(temp);
  byte[] rgb = new byte[NUM_LED * 3];
  for (int i = 0; i < NUM_LED; i++) {
    int r, g, b;
    if (i < lit) HSV(hue, 100, 100, out r, out g, out b); else HSV(hue, 100, 12, out r, out g, out b);
    rgb[i * 3] = Bake(r, bri); rgb[i * 3 + 1] = Bake(g, bri); rgb[i * 3 + 2] = Bake(b, bri);
  }
  MatrixCustomFast(h, rgb);
}
static void LedHeatmapFast(IntPtr h, double temp, int bri) {
  int baseH = TempHue(temp);
  byte[] rgb = new byte[NUM_LED * 3];
  for (int i = 0; i < NUM_LED; i++) {
    int hu = baseH - 15 + (i * 30) / NUM_LED;
    int r, g, b; HSV(hu, 100, 100, out r, out g, out b);
    rgb[i * 3] = Bake(r, bri); rgb[i * 3 + 1] = Bake(g, bri); rgb[i * 3 + 2] = Bake(b, bri);
  }
  MatrixCustomFast(h, rgb);
}
static void LedBreathFast(IntPtr h, double ph, double temp, byte cr, byte cg, byte cb, int bri) {
  double period = 3200.0 - 20.0 * temp; if (period < 900) period = 900; if (period > 3200) period = 3200;
  double cyc = (ph / period) * (2 * Math.PI);
  double env = 0.15 + 0.85 * (0.5 - 0.5 * Math.Cos(cyc));
  byte[] rgb = new byte[NUM_LED * 3];
  for (int i = 0; i < NUM_LED; i++) {
    rgb[i * 3] = Bake((int)(cr * env), bri); rgb[i * 3 + 1] = Bake((int)(cg * env), bri); rgb[i * 3 + 2] = Bake((int)(cb * env), bri);
  }
  MatrixCustomFast(h, rgb);
}
static void LedLoadMeter(IntPtr h, double cload, double gload, double temp, int bri) {
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
  MatrixCustomFast(h, rgb);
}
static void LedLoop() {
  int frameMs = 40;
  int lastStaticMs = -999999;
  int ledPhase = 0; double ledPhaseF = 0;
  string drawnSig = "";
  Stopwatch sw = Stopwatch.StartNew();
  int lastMs = 0;
  while (_run) {
    IntPtr h = devH;
    if (h == new IntPtr(-1)) { lastStaticMs = -999999; drawnSig = ""; Thread.Sleep(200); continue; }
    int now = (int)sw.ElapsedMilliseconds;
    string ml, mc; int mb; bool pz; bool idl, nd;
    lock (cfgLock) { ml = led; mc = ledColor; mb = bri; pz = paused; idl = idleDim; nd = nightDim; }
    double use = Math.Max(curCpu, curGpu);
    bool crit = (critical > 0 && use >= critical && !paused);
    int effBri = mb;
    if (idl && use >= 0 && use < 45 && !crit) { if (effBri > IDLE_BRI) effBri = IDLE_BRI; }
    if (nd) { int hr = DateTime.Now.Hour; if (hr >= 22 || hr < 7) { if (effBri > NIGHT_BRI) effBri = NIGHT_BRI; } }
    bool animate = (ml == "rainbow" || ml == "wave" || ml == "level" || ml == "heatmap" || ml == "temp" || ml == "loadmeter" || ml == "breath" || ml == "breathing");
    try {
      if (paused || ml == "off" || ml == "none") {
        if (drawnSig != "off" && now - lastStaticMs >= 200) { MatrixOffFast(h); drawnSig = "off"; lastStaticMs = now; }
      } else if (crit) {
        bool on = ((now / 350) % 2) == 0;
        string cs = "crit" + (on ? "1" : "0");
        if (drawnSig != cs && now - lastStaticMs >= 300) {
          if (on) MatrixStaticFast(h, Bake(255, effBri), 0, 0); else MatrixOffFast(h);
          drawnSig = cs; lastStaticMs = now;
        }
      } else if (ml == "static") {
        byte[] cc = HexTo3(ColorName(mc)); if (cc == null) cc = new byte[] { 255, 255, 255 };
        string sig = "static|" + effBri;
        if (drawnSig != sig) { MatrixStaticFast(h, Bake(cc[0], effBri), Bake(cc[1], effBri), Bake(cc[2], effBri)); drawnSig = sig; lastStaticMs = now; }
      } else if (ml == "spectrum") {
        string sig = "spectrum|" + effBri;
        if (drawnSig != sig) { lock (hidLock) { SetLedAllBrightness(h, 255); MatrixSpectrum(h); } drawnSig = sig; lastStaticMs = now; }
      } else {
        string sig = ml + "|" + mc + "|" + effBri;
        if (sig != drawnSig) { drawnSig = sig; ledPhase = 0; ledPhaseF = 0; }
        double dt = now - lastMs; lastMs = now; ledPhaseF += dt;
        if (ml == "rainbow") LedRainbowFast(h, (ledPhaseF / 16.0) % 360.0, effBri);
        else if (ml == "wave") LedWaveFast(h, (ledPhaseF / 16.0) % 360.0, use, effBri);
        else if (ml == "level") LedLevelFast(h, use, effBri);
        else if (ml == "loadmeter") LedLoadMeter(h, cpuLoad, gpuLoad, use, effBri);
        else if (ml == "breath" || ml == "breathing") {
          byte[] cc = HexTo3(ColorName(mc)); if (cc == null) { string cn2 = ColorName(ledColor); cc = HexTo3(cn2); }
          if (cc == null) cc = new byte[] { 255, 0, 0 };
          LedBreathFast(h, ledPhaseF, use, cc[0], cc[1], cc[2], effBri);
        } else LedHeatmapFast(h, use, effBri);
        lastStaticMs = now;
      }
    } catch { }
    bool anim2 = animate && !paused;
    int nap = anim2 ? frameMs : 250;
    int sleepMs = nap - ((int)sw.ElapsedMilliseconds - now);
    if (sleepMs < 1) sleepMs = 1;
    while (sleepMs > 0 && _run) { int step = sleepMs > 50 ? 50 : sleepMs; Thread.Sleep(step); sleepMs -= step; }
  }
}static void SetBrightnessIfChanged(IntPtr h, int briPct) {
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
    if (d.TryGetValue("profile", out sv) && !string.IsNullOrEmpty(sv)) prof = sv.ToLowerInvariant();
    if (d.TryGetValue("led", out sv) && !string.IsNullOrEmpty(sv)) led = sv.ToLowerInvariant();
    if (d.TryGetValue("ledcolor", out sv) && !string.IsNullOrEmpty(sv)) ledColor = sv;
    // accept the combined "static:red" / "static:FF0000" syntax in the cfg file too
    if (led.StartsWith("static:")) { string cc2 = led.Substring(7); if (cc2.Length > 0) ledColor = cc2; led = "static"; }
    bri = GetInt(d, "bri", bri);
    interval = GetInt(d, "interval", interval);
    critical = GetInt(d, "critical", critical);
    maxStep = GetInt(d, "maxstep", maxStep);
    autoTurbo = GetBool(d, "autoturbo", autoTurbo);
    idleDim = GetBool(d, "idledim", idleDim);
    nightDim = GetBool(d, "nightdim", nightDim);
    loadBoost = GetBool(d, "loadboost", loadBoost);
    keepOnExit = GetBool(d, "keeponexit", keepOnExit);
    rpmCap = GetInt(d, "rpmcap", rpmCap);
    capTemp = GetInt(d, "captemp", capTemp);
    deadband = GetInt(d, "deadband", deadband);
    downHold = GetInt(d, "downhold", downHold);
    coolTail = GetBool(d, "cooltail", coolTail);
    coolTemp = GetInt(d, "cooltemp", coolTemp);
    hyperBoost = GetBool(d, "hyperboost", hyperBoost);
    manual = GetBool(d, "manual", manual);
    manualUnit = GetInt(d, "manualunit", manualUnit);
    coast = GetInt(d, "coast", coast);
    // The steadiness slider is the single source of truth for the three anti-hunt knobs.
    int sm = GetInt(d, "smooth", -1);
    if (sm >= 0) ApplySmooth(sm);
  } catch { }
}
static void SaveConfig() {
  try {
    lastSelfSaveMs = Environment.TickCount;
    string[] lines = new string[] {
      "; RazerPadTray settings - edited automatically when you use the tray menu.",
      "; The old CLI keeps its own autostart.cfg; this file only configures the tray app.",
      "profile=" + prof,
      "led=" + led,
      "ledcolor=" + ledColor,
      "bri=" + bri,
      "interval=" + interval,
      "critical=" + critical,
      "maxstep=" + maxStep,
      "autoturbo=" + (autoTurbo ? "true" : "false"),
      "idledim=" + (idleDim ? "true" : "false"),
      "nightdim=" + (nightDim ? "true" : "false"),
      "loadboost=" + (loadBoost ? "true" : "false"),
      "keeponexit=" + (keepOnExit ? "true" : "false"),
      "rpmcap=" + rpmCap,
      "captemp=" + capTemp,
      "deadband=" + deadband,
      "downhold=" + downHold,
      "cooltail=" + (coolTail ? "true" : "false"),
      "cooltemp=" + coolTemp,
      "hyperboost=" + (hyperBoost ? "true" : "false"),
      "manual=" + (manual ? "true" : "false"),
      "manualunit=" + manualUnit,
      "coast=" + coast,
      "smooth=" + smooth
    };
    File.WriteAllLines(ConfigPath(), lines);
  } catch { }
}
static void WorkerLoop() {
  int loopN = 0, ledPhase = 0, lastLedMs = 0, lastBlinkMs = 0;
  bool critOn = false;
  int lastApplied = -1, failCount = 0, hotStreak = 0, pendingUnit = -1, pendingCnt = 0, holdUnit = -1, decayCnt = 0, loadStreak = 0; int lastSentUnit = -1; int lastSentMs = 0; string profPrev = prof + (manual ? "|manual" : "");
  bool autoTurboOn = false, burstWasActive = false;
  string appliedSig = "";
  while (_run) {
   try {
    int loopStart = Environment.TickCount;
    if (devH == new IntPtr(-1)) {
      curStatus = "pad not found (retrying...)";
      curUnit = curRpm = -1;
      fanOverride = "";
      WriteStatus();
      lastApplied = -1; appliedSig = ""; lastBriApplied = -1; pidI = 0; emaT = -1; lastMeas = -1; lastDeriv = 0; lastSentUnit = -1; lastSentMs = 0;
      if (!Reopen()) { for (int s = 0; s < 30 && _run; s++) Thread.Sleep(100); continue; }
    }
    IntPtr h = devH;
    double cpuT = GetCpuRaw();
    double gpuT = gpuCache;   // background sampler; never blocks the worker on nvidia-smi
    cpuLoad = GetCpuLoad();   // cheap PDH read
    curCpu = cpuT < 0 ? -1 : (int)Math.Round(cpuT);
    curGpu = gpuT < 0 ? -1 : (int)Math.Round(gpuT);

    string mp, ml, mc; int mb, mi, mcr, ms, mcap, mcapT, mdb, mdh; bool pz, at, lb, man; int mu; string profWas = profPrev;
    lock (cfgLock) { mp = prof; ml = led; mc = ledColor; mb = bri; mi = interval; mcr = critical; pz = paused; at = autoTurbo; ms = maxStep; lb = loadBoost; mcap = rpmCap; mcapT = capTemp; mdb = deadband; mdh = downHold; man = manual; mu = manualUnit; }

    if (pz) {
      curStatus = "paused"; WriteStatus();
      for (int s = 0; s < 10 && _run; s++) Thread.Sleep(100);
      continue;
    }

    double useRaw = Math.Max(cpuT, gpuT);
    // Low-pass the sensor. Without this, +-1C jitter is enough to push the target across the deadband and
    // the fan twitches constantly. EMA with alpha=0.35 over a 2s loop -> ~6s time constant.
    if (useRaw < 0) {
      // no reading this loop: keep the last estimate, drift it slowly toward the curve so we never freeze forever
      if (emaT < 0) emaT = useRaw; else emaT = emaT - 0.05;
    } else if (emaT < 0) emaT = useRaw;
    else { double prev = emaT; emaT = emaT + emaAlpha * (useRaw - emaT); lastDeriv = emaT - prev; }
    double use = emaT;
    int now = Environment.TickCount;
    bool crit = (use >= mcr);

    // ---- auto-turbo: sustained very-hot temps force a max-fan cooling run, then release ----
    int trigT = mcr - 5; if (trigT < 55) trigT = 55;
    int relT = trigT - 10; if (relT < 40) relT = 40;
    if (use >= trigT) { hotStreak++; if (hotStreak >= 4) autoTurboOn = true; }
    else if (use >= 0 && use <= relT) { hotStreak = 0; autoTurboOn = false; }
    int burstDeadline = Volatile.Read(ref manualTurboUntil);
    bool manualTurbo = NotExpired(now, burstDeadline);
    if (burstDeadline != 0 && !manualTurbo)
      Interlocked.CompareExchange(ref manualTurboUntil, 0, burstDeadline);
    bool burstEnded = burstWasActive && !manualTurbo;
    burstWasActive = manualTurbo;

    int fixedU, rmin, rmax;
    List<CP> pts = ProfileCurve(mp, out fixedU, out rmin, out rmax);
    if (man) fixedU = mu;
    int unit; bool forceMax = false;
    string overrideReason = crit ? "Critical temperature" : manualTurbo ? "Turbo Burst" :
                            at && autoTurboOn ? "Auto-Turbo" : "";
    if (overrideReason.Length != 0) { unit = MAX_UNIT; forceMax = true; }
    else if (fixedU >= 0) unit = fixedU;
    else if (use < 0) unit = lastApplied < 0 ? 30 : lastApplied;
    else {
      int rpm = ComputeRpm(pts, (int)Math.Round(use), true);
      if (rpm < rmin) rpm = rmin;
      if (rpm > rmax) rpm = rmax;
      unit = RoundTo10(rpm / FACTOR);

      // ---- closed-loop correction on top of the curve (this is what kills the hunting) ----
      // The curve alone is open-loop: it re-reads a lagging sensor every 2s, so the fan chases the
      // rebound and pumps up and down. Here we aim at a temperature SETPOINT instead. The feed-forward
      // curve already gets us close; the integral term then trims up/down until the temperature holds
      // steady, and once it is holding, the integral stops moving -> the fan sits at ONE constant speed.
      double sp = 75;                       // default setpoint (balanced)
      if (mp == "quiet") sp = 80;
      else if (mp == "performance") sp = 68;
      else if (mp == "turbo") sp = 60;
      double err = use - sp;
      double dtS = (mi < 500 ? 500 : mi) / 1000.0;
      if (use >= IBAND_LO && use <= IBAND_HI) {
        pidI = pidI + Ki * err * dtS;
        if (pidI > IMAX) pidI = IMAX; if (pidI < -IMAX) pidI = -IMAX;
      }
      double corr = Kp * err + pidI;
      // D term: only when the hardware is still WARMING, so we react early and damp overshoot.
      if (lastDeriv > 0.1) corr += Kd * lastDeriv;
      unit = RoundTo10((int)Math.Round(unit + corr));
    }
    // Switching profile (or entering/leaving manual mode) changes the whole target: forget the old
    // closed-loop trim so the new mode starts from its own curve instead of inheriting a stale correction.
    string modeSig = mp + (man ? "|manual" : "");
    if (modeSig != profWas) { pidI = 0; emaT = -1; decayCnt = 0; pendingCnt = 0; profPrev = modeSig; }

    // ---- load-aware pre-emptive boost: if CPU/GPU is working hard, raise the fan floor
    // BEFORE the temperature has had time to climb, so the pad reacts ahead of the spike ----
    if (lb && !forceMax && fixedU < 0 && use >= 0) {
      double cl = cpuLoad; double gl = gpuLoad;
      int hot = Math.Max(cl < 0 ? 0 : (int)cl, gl < 0 ? 0 : (int)gl);
      // Require the load to PERSIST before lifting the floor. A single busy 2s sample (indexer,
      // antivirus, browser blink) used to slam the fan up and then down again - visible flapping.
      if (hot >= 55) loadStreak++; else if (hot < 30) loadStreak = 0;
      if (loadStreak >= 3) {
        if (hot >= 90) { int floor = MAX_UNIT; if (floor > unit) unit = floor; }
        else if (hot >= 65) { int floor = 46; if (floor > unit) unit = floor; }
        else if (hot >= 40) { int floor = 38; if (floor > unit) unit = floor; }
      }
    }

    // ---- fan-wear cap: while the hardware is COOL, hold the fan at or below a gentle ceiling
    // (default 2300 rpm) to protect the bearing. As soon as temp reaches capTemp (or critical /
    // turbo), the cap lifts and the curve/critical decide the real speed, so cooling never suffers. ----
    if (!forceMax && fixedU < 0 && mcap > 0 && use >= 0 && use < mcapT) {
      int capU = mcap / FACTOR; if (capU < MIN_UNIT) capU = MIN_UNIT; if (capU > MAX_UNIT) capU = MAX_UNIT;
      if (unit > capU) unit = capU;
    }

    // ---- smooth ramp: cap how many fan-units we change per loop (anti-flap, quieter) ----
    // ---- stability filter: hold a CONSTANT speed and only move when it is genuinely needed. ----
    //  * a deadband ignores tiny target jitter (sensor noise) so the fan does not flutter every second;
    //  * upward moves happen promptly (cool first);
    //  * downward moves only take effect after the lower target has HELD for downHold loops, so the
    //    fan stays up after a load ends and eases back down gradually instead of pumping up and down.
    // ---- high-water-mark coast controller (anti-hunt).
    // The thermal system lags: if the fan drops speed the instant the temp dips, the temp
    // rebounds a second later and the fan pumps back up -> the up/down oscillation we want to kill.
    // Fix: remember a high-water mark of recent cooling demand. Climb PROMPTLY when it gets hot,
    // but only ease DOWN slowly (1 unit per coast seconds), and freeze entirely inside the deadband.
    int delta = unit - lastApplied;
    int decayLoops = coast * 1000 / (mi < 500 ? 500 : mi); if (decayLoops < 1) decayLoops = 1;
    if (forceMax || burstEnded || lastApplied < 0 || fixedU >= 0) {
      lastApplied = unit; holdUnit = unit; decayCnt = 0;
    } else if (ms <= 0) {
      lastApplied = unit; holdUnit = unit; decayCnt = 0;
    } else if (delta >= mdb) {
      // clearly hotter: ramp up promptly (respect the per-loop step limit) and lift the high-water mark
      lastApplied = Math.Min(unit, lastApplied + ms);
      if (holdUnit < lastApplied) holdUnit = lastApplied;
      decayCnt = 0;
    } else if (delta <= -mdb) {
      // clearly cooler: hold the high-water mark, creep down 1 unit per coast interval
      if (lastDeriv > 0.12) { decayCnt = 0; }   // still warming -> do not start easing down yet
      else pendingCnt++;
      if (pendingCnt >= (mdh < 1 ? 1 : mdh)) {
        decayCnt++;
        if (decayCnt >= decayLoops) {
          decayCnt = 0;
          if (holdUnit > unit) holdUnit--;
          if (holdUnit < unit) holdUnit = unit;
        }
        lastApplied = holdUnit;
      }
    } else {
      // inside deadband: freeze speed and coast timer (ignore sensor noise)
      decayCnt = 0;
    }
    int cmd = lastApplied;
    bool fanOk;
    bool speedChanged = (cmd != lastSentUnit) || (now - lastSentMs > 15000);   // change OR 15s heartbeat
    if (speedChanged) { lock (hidLock) { fanOk = SetFanSpeed(h, fanId, cmd); } if (fanOk) { lastSentUnit = cmd; lastSentMs = now; } }
    else fanOk = true;
    if (fanOk) { failCount = 0; curUnit = cmd; curRpm = cmd * FACTOR; }
    else {
      failCount++; curUnit = -1; curRpm = -1;
      if (failCount >= 3) { lastApplied = -1; appliedSig = ""; lastBriApplied = -1; lastSentUnit = -1; lastSentMs = 0; pidI = 0; Reopen(); }
    }

    // Synapse steals manual mode; re-assert it periodically
    if (++loopN % 10 == 0) { lock (hidLock) { try { int m = GetFanMode(h, fanId); if (m != 1) SetFanMode(h, fanId, 1); } catch { } } }

    // ---------- lighting is driven by the dedicated LedLoop thread (smooth ~25fps) ----------

    fanOverride = overrideReason;
    bool turboShow = forceMax && !crit;
    curStatus = "CPU " + (cpuT < 0 ? "n/a" : cpuT.ToString("0") + "C") +
                "  GPU " + (gpuT < 0 ? "n/a" : gpuT.ToString("0") + "C") +
                "  Fan " + (curRpm < 0 ? "n/a" : curRpm.ToString()) + "rpm" +
                (crit ? "  [HOT]" : (turboShow ? "  [TURBO]" : ""));
    WriteStatus();
    int wait = mi < 500 ? 500 : mi;
    int sleep = wait - (Environment.TickCount - loopStart);
    while (sleep > 0 && _run) { int step = sleep > 150 ? 150 : sleep; Thread.Sleep(step); sleep -= step; }
   } catch (Exception wex) { try { LogCrash("worker loop: " + wex.Message + " | " + wex.StackTrace); } catch { } for (int s9 = 0; s9 < 10 && _run; s9++) Thread.Sleep(100); }
  }
}
static System.Drawing.Icon MakeIcon(int temp) {
  Bitmap bmp = new Bitmap(32, 32);
  using (Graphics g = Graphics.FromImage(bmp)) {
    g.Clear(Color.Transparent);
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    int hue;
    if (temp < 0) hue = 210; else { double f = (temp - 45.0) / 45.0; if (f < 0) f = 0; if (f > 1) f = 1; hue = (int)Math.Round(120.0 - 120.0 * f); }
    int r, gg, b; HSV(hue, 90, 100, out r, out gg, out b);
    Color c = Color.FromArgb(r, gg, b);
    using (SolidBrush br = new SolidBrush(c)) g.FillEllipse(br, 2, 2, 28, 28);
    using (Pen pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2)) g.DrawEllipse(pen, 2, 2, 28, 28);
    using (SolidBrush w = new SolidBrush(Color.White))
    using (Font f2 = new Font("Segoe UI", 11, FontStyle.Bold)) {
      string t = temp < 0 ? "?" : temp.ToString();
      SizeF sz = g.MeasureString(t, f2);
      g.DrawString(t, f2, w, (32 - sz.Width) / 2f, (32 - sz.Height) / 2f);
    }
  }
  IntPtr ico = bmp.GetHicon();
  curIconH = ico;
  return System.Drawing.Icon.FromHandle(ico);
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
static void FanClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  lock (cfgLock) { prof = (string)mi.Tag; manual = false; }
  CheckGroup(fanItems, (string)mi.Tag);
  ledDirty = true;
  SaveConfig();
  SyncDash();
}
static void LedClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  string tag = (string)mi.Tag;
  lock (cfgLock) {
    if (tag.StartsWith("static:")) { led = "static"; ledColor = tag.Substring(7); }
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
}
static void BriClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  int v = Convert.ToInt32(mi.Tag);
  lock (cfgLock) { bri = v; }
  CheckGroup(briItems, mi.Tag.ToString());
  ledDirty = true;
  SaveConfig();
}
static void PauseClick(object s, EventArgs e) {
  paused = !paused;
  if (paused) Volatile.Write(ref manualTurboUntil, 0);
  if (pauseItem != null) {
    pauseItem.Checked = paused;
    pauseItem.Text = paused ? "Resume automation" : "Pause automation";
  }
  SyncDash();
  DashTick(null, EventArgs.Empty);
  ledDirty = true;
}
static void TurboBurstClick(object s, EventArgs e) {
  if (paused) return;
  int now = Environment.TickCount;
  int deadline = unchecked(now + TURBO_MS);
  int currentDeadline = Volatile.Read(ref manualTurboUntil);
  Volatile.Write(ref manualTurboUntil, NotExpired(now, currentDeadline) ? 0 : (deadline == 0 ? 1 : deadline));
  DashTick(null, EventArgs.Empty);
}
static void AutoTurboClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  autoTurbo = !autoTurbo;
  mi.Checked = autoTurbo;
  if (!autoTurbo) { lock (cfgLock) { } }
  SaveConfig();
}
static void LoadBoostClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  loadBoost = !loadBoost;
  mi.Checked = loadBoost;
  SaveConfig();
}
static void IdleDimClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  idleDim = !idleDim;
  mi.Checked = idleDim;
  ledDirty = true;
  SaveConfig();
}
static void NightDimClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  nightDim = !nightDim;
  mi.Checked = nightDim;
  ledDirty = true;
  SaveConfig();
}
static int lastRpmCap = 2300;
static void WearCapClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  if (rpmCap > 0) { lastRpmCap = rpmCap; rpmCap = 0; mi.Checked = false; }
  else { rpmCap = lastRpmCap > 0 ? lastRpmCap : 2300; mi.Checked = true; }
  SaveConfig();
}
static void SteadyClick(object s, EventArgs e) {
  ToolStripMenuItem mi = (ToolStripMenuItem)s;
  ApplySmooth(smooth > 0 ? 0 : 45);   // 0 = instant/reactive, 45 = the recommended steady default
  mi.Checked = (smooth > 0);
  SaveConfig();
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
      if (on) k.SetValue(RunValue, "\"" + ExePath() + "\" --minimized --status-file \"" + Path.Combine(Path.GetDirectoryName(ExePath()), "status.txt") + "\"");
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
  ToolStripMenuItem fan = new ToolStripMenuItem("Fan profile");
  fan.DropDownItems.Add(Radio(ref fanItems, "Quiet", "quiet", new EventHandler(FanClick)));
  fan.DropDownItems.Add(Radio(ref fanItems, "Balanced (default)", "balanced", new EventHandler(FanClick)));
  fan.DropDownItems.Add(Radio(ref fanItems, "Performance", "performance", new EventHandler(FanClick)));
  fan.DropDownItems.Add(Radio(ref fanItems, "Turbo (max)", "turbo", new EventHandler(FanClick)));
  fan.DropDownItems.Add(new ToolStripSeparator());
  ToolStripMenuItem burst = new ToolStripMenuItem("Turbo burst for 2 minutes");
  burst.Click += new EventHandler(TurboBurstClick);
  fan.DropDownItems.Add(burst);
  ToolStripMenuItem at = new ToolStripMenuItem("Auto-turbo when very hot");
  at.Checked = autoTurbo;
  at.Click += new EventHandler(AutoTurboClick);
  fan.DropDownItems.Add(at);
  ToolStripMenuItem lbt = new ToolStripMenuItem("Boost fan ahead of heavy load");
  lbt.Checked = loadBoost;
  lbt.Click += new EventHandler(LoadBoostClick);
  fan.DropDownItems.Add(lbt);
  ToolStripMenuItem wct = new ToolStripMenuItem("Protect fan: cap at 2300rpm until hot");
  wct.Checked = (rpmCap > 0);
  wct.Click += new EventHandler(WearCapClick);
  fan.DropDownItems.Add(wct);
  ToolStripMenuItem sdy = new ToolStripMenuItem("Steady speed (anti-flap)");
  sdy.Checked = (deadband > 0 && downHold > 1);
  sdy.Click += new EventHandler(SteadyClick);
  fan.DropDownItems.Add(sdy);
  ToolStripMenuItem ctl = new ToolStripMenuItem("Wind fan down gently on exit");
  ctl.Checked = coolTail;
  ctl.Click += new EventHandler(CoolTailClick);
  fan.DropDownItems.Add(ctl);  ToolStripMenuItem hbt = new ToolStripMenuItem("Max performance (HyperBoost)");
  hbt.Checked = hyperBoost;
  hbt.Click += new EventHandler(HyperBoostClick);
  fan.DropDownItems.Add(hbt);
  m.Items.Add(fan);

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
  asu.Click += new EventHandler(delegate (object s2, EventArgs e2) { asu.Checked = !asu.Checked; SetAutostart(asu.Checked); });
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
  if (tray == null) return;
  int t = Math.Max(curCpu, curGpu);
  string head = paused ? "RazerPad (paused)" : "RazerPad Fan Control";
  string bodyTxt = "CPU: " + (curCpu < 0 ? "n/a" : curCpu + " C") + "\n" +
                   "GPU: " + (curGpu < 0 ? "n/a" : curGpu + " C") + "\n" +
                   "Fan: " + (curRpm < 0 ? "n/a" : curRpm + " rpm");
  string tip = head + "\n" + bodyTxt;
  if (tip.Length > 63) tip = tip.Substring(0, 63);
  tray.Text = tip;
  string iconSig = (paused ? "P" : "") + ((!string.IsNullOrEmpty(curStatus) && curStatus.Contains("[TURBO]")) ? "T" : "") + ":" + (paused ? -1 : t);
  if (iconSig != lastIconSig) {
    lastIconSig = iconSig;
    int ic = t;
    if (paused) ic = -1;
    else if (!string.IsNullOrEmpty(curStatus) && curStatus.Contains("[TURBO]")) ic = 95;
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
static readonly Color GraphiteBackground = Color.FromArgb(15, 16, 17);
static readonly Color GraphitePanel = Color.FromArgb(24, 25, 26);
static readonly Color GraphiteBorder = Color.FromArgb(60, 62, 65);
static readonly Color GraphiteAccent = Color.FromArgb(155, 248, 0);
static Panel automaticControls, manualControls, turboControls, automationOptions, lightingOptions, deviceOptions, speedMeter;
static Label overrideStatus;
static ToolStripMenuItem pauseItem;

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
  window.MinimumSize = new Size(690, 475);
  window.AutoScaleMode = AutoScaleMode.Dpi;
  window.FormClosing += DashClosing;
  try { window.Icon = MakeIcon(Math.Max(0, Math.Max(curCpu, curGpu))); } catch { }

  TableLayoutPanel shell = new TableLayoutPanel {
    Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
  };
  shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
  shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
  shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
  shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
  window.Controls.Add(shell);

  FlowLayoutPanel navigation = new FlowLayoutPanel {
    Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
    Padding = new Padding(0, 20, 0, 0), Margin = Padding.Empty, BackColor = GraphiteBackground
  };
  shell.Controls.Add(navigation, 0, 0);

  TableLayoutPanel telemetry = new TableLayoutPanel {
    Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(18), Margin = Padding.Empty
  };
  telemetry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
  telemetry.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
  telemetry.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
  telemetry.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
  shell.Controls.Add(telemetry, 1, 0);

  speedMeter = new FanDial { Dock = DockStyle.Fill, AccessibleName = "Commanded fan speed" };
  telemetry.Controls.Add(speedMeter, 0, 0);
  TableLayoutPanel temperatures = GraphiteGrid(2);
  temperatures.Dock = DockStyle.Fill;
  telemetry.Controls.Add(temperatures, 0, 1);
  lblCpu = AddTemperature(temperatures, "CPU", 0);
  lblGpu = AddTemperature(temperatures, "GPU", 1);
  lblHead = GraphiteText("Connecting to cooling pad", 10, Color.Silver);
  lblHead.TextAlign = ContentAlignment.MiddleCenter;
  lblHead.Dock = DockStyle.Fill;
  telemetry.Controls.Add(lblHead, 0, 2);

  TableLayoutPanel right = new TableLayoutPanel {
    Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(16, 18, 16, 20), Margin = Padding.Empty
  };
  right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
  right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
  right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
  shell.Controls.Add(right, 2, 0);
  Panel scroll = new Panel { AutoScroll = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
  right.Controls.Add(scroll, 0, 0);
  TableLayoutPanel cooling = GraphiteGrid(1);
  cooling.Dock = DockStyle.Top;
  scroll.Controls.Add(cooling);

  TableLayoutPanel profiles = GraphiteGrid(1);
  profiles.Margin = new Padding(0, 4, 0, 0);
  GraphiteAdd(profiles, GraphiteText("Fan Profile", 14, Color.Silver));
  TableLayoutPanel choices = GraphiteGrid(5);
  profBtns.Clear();
  foreach (string name in new[] { "Quiet", "Balanced", "Performance", "Turbo", "Manual" }) {
    Button button = GraphiteButton(name);
    button.Height = 68;
    button.Font = new Font("Segoe UI", 8.5f);
    button.AutoEllipsis = true;
    button.Image = InstrumentIcon(name == "Manual" ? "Manual" : "Cooling");
    button.TextImageRelation = TextImageRelation.ImageAboveText;
    button.Margin = new Padding(0, 0, 2, 0);
    button.Click += delegate {
      lock (cfgLock) {
        manual = name == "Manual";
        if (!manual) prof = name.ToLowerInvariant();
      }
      CheckGroup(fanItems, manual ? "" : prof);
      SaveConfig();
      ledDirty = true;
      SyncDash();
    };
    GraphiteAdd(choices, button, profBtns.Count, 0);
    profBtns.Add(button);
  }
  GraphiteAdd(profiles, choices);
  GraphiteAdd(cooling, profiles);
  AddInstrumentDivider(cooling);

  TableLayoutPanel automatic = GraphiteGrid(1);
  automaticControls = automatic;
  tbSmooth = AddSlider(automatic, "Fan Steadiness", 0, 100, out lblSmoothVal, SmoothChanged);
  TableLayoutPanel sliderEnds = GraphiteGrid(2);
  GraphiteAdd(sliderEnds, GraphiteText("More responsive", 8, Color.Silver), 0, 0);
  Label steadyEnd = GraphiteText("More steady", 8, Color.Silver);
  steadyEnd.TextAlign = ContentAlignment.MiddleRight;
  GraphiteAdd(sliderEnds, steadyEnd, 1, 0);
  GraphiteAdd(automatic, sliderEnds);
  GraphiteAdd(cooling, automatic);

  TableLayoutPanel fixedSpeed = GraphiteGrid(1);
  manualControls = fixedSpeed;
  tbFan = AddSlider(fixedSpeed, "Target speed", MIN_UNIT, MAX_UNIT, out lblFanVal, FanSliderChanged);
  GraphiteAdd(fixedSpeed, GraphiteText("500–3,200 RPM in 50 RPM steps", 8, Color.Silver));
  GraphiteAdd(cooling, fixedSpeed);

  TableLayoutPanel maxSpeed = GraphiteGrid(1);
  turboControls = maxSpeed;
  GraphiteAdd(maxSpeed, GraphiteText("3,200 RPM fixed speed", 12, GraphiteAccent));
  GraphiteAdd(cooling, maxSpeed);
  TableLayoutPanel automation = GraphiteGrid(1);
  automationOptions = automation;
  ckAutoTurbo = AddCheck(automation, "Auto-Turbo at high temperatures");
  ckLoadBoost = AddCheck(automation, "Boost fan ahead of load");
  ckWearCap = AddCheck(automation, "Limit fan speed while cool");
  ckSteady = AddCheck(automation, "Steady speed control");
  AddInstrumentDivider(cooling);
  AddDisclosure(cooling, "Fan Automation", automation);

  TableLayoutPanel lighting = GraphiteCard("Lighting");
  lighting.Margin = new Padding(6, 0, 0, 0);
  cbLed = new ComboBox {
    DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
    DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 23, FormattingEnabled = true,
    BackColor = GraphiteBackground, ForeColor = window.ForeColor, AccessibleName = "Lighting effect"
  };
  cbLed.Items.AddRange(new object[] { "heatmap", "level", "wave", "spectrum", "loadmeter", "breath", "rainbow", "static", "off" });
  cbLed.Format += delegate (object sender, ListControlConvertEventArgs e) {
    string effect = (string)e.ListItem;
    e.Value = effect == "loadmeter" ? "Load meter" :
              effect.Length > 0 ? char.ToUpperInvariant(effect[0]) + effect.Substring(1) : effect;
  };
  cbLed.DrawItem += delegate (object sender, DrawItemEventArgs e) {
    bool selected = (e.State & DrawItemState.Selected) != 0;
    using (Brush background = new SolidBrush(selected ? Color.FromArgb(47, 67, 19) : GraphiteBackground))
      e.Graphics.FillRectangle(background, e.Bounds);
    if (e.Index >= 0) {
      Rectangle textBounds = Rectangle.Inflate(e.Bounds, -6, 0);
      TextRenderer.DrawText(e.Graphics, cbLed.GetItemText(cbLed.Items[e.Index]), cbLed.Font,
                            textBounds, cbLed.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
    e.DrawFocusRectangle();
  };
  cbLed.SelectedIndexChanged += LedComboChanged;
  GraphiteAdd(lighting, cbLed);
  TableLayoutPanel swatches = GraphiteGrid(8);
  colorSwatches.Clear();
  foreach (string name in new[] { "cyan", "blue", "purple", "pink", "red", "orange", "green", "white" }) {
    Button swatch = GraphiteButton("");
    swatch.Height = 24;
    swatch.Margin = new Padding(0, 2, 5, 2);
    swatch.AccessibleName = name + " static lighting";
    swatch.BackColor = ParseHex(ColorName(name));
    swatch.Click += delegate {
      lock (cfgLock) { led = "static"; ledColor = name; }
      CheckGroup(ledItems, "static:" + name);
      SaveConfig();
      ledDirty = true;
      SyncDash();
    };
    GraphiteAdd(swatches, swatch, colorSwatches.Count, 0);
    colorSwatches.Add(name, swatch);
  }
  GraphiteAdd(lighting, swatches);
  TableLayoutPanel lightSettings = GraphiteGrid(1);
  lightingOptions = lightSettings;
  tbBri = AddSlider(lightSettings, "Brightness", 0, 100, out lblBriVal, BriSliderChanged);
  ckIdleDim = AddCheck(lightSettings, "Dim when idle or cool");
  ckNightDim = AddCheck(lightSettings, "Dim at night (22:00–07:00)");
  AddDisclosure(lighting, "Lighting options", lightSettings);
  TableLayoutPanel device = GraphiteCard("Device");
  device.Margin = new Padding(0, 0, 6, 0);
  ckHyper = AddCheck(device, "HyperBoost (plugged in)");
  TableLayoutPanel deviceSettings = GraphiteGrid(1);
  deviceOptions = deviceSettings;
  ckAutoStart = AddCheck(deviceSettings, "Start with Windows");
  ckCoolTail = AddCheck(deviceSettings, "Wind fan down on exit");
  Button exit = GraphiteButton("Exit app");
  exit.Click += ExitClick;
  GraphiteAdd(deviceSettings, exit);
  AddDisclosure(device, "Device options", deviceSettings);
  lighting.Dock = device.Dock = DockStyle.Top;
  lighting.Hide();
  device.Hide();
  scroll.Controls.Add(lighting);
  scroll.Controls.Add(device);
  TableLayoutPanel[] pages = { cooling, lighting, device };
  List<Button> navigationButtons = new List<Button>();
  string[] pageNames = { "Cooling", "Lighting", "Device" };
  for (int i = 0; i < pages.Length; i++) {
    int selectedPage = i;
    Button tab = GraphiteButton(pageNames[i]);
    tab.Size = new Size(110, 88);
    tab.Image = InstrumentIcon(pageNames[i]);
    tab.TextImageRelation = TextImageRelation.ImageAboveText;
    tab.FlatAppearance.BorderSize = 0;
    tab.Margin = Padding.Empty;
    tab.BackColor = i == 0 ? Color.FromArgb(33, 44, 18) : GraphiteBackground;
    tab.ForeColor = i == 0 ? GraphiteAccent : Color.Silver;
    tab.Click += delegate {
      for (int index = 0; index < pages.Length; index++) {
        bool active = index == selectedPage;
        pages[index].Visible = active;
        navigationButtons[index].BackColor = active ? Color.FromArgb(33, 44, 18) : GraphiteBackground;
        navigationButtons[index].ForeColor = active ? GraphiteAccent : Color.Silver;
      }
      pages[selectedPage].BringToFront();
    };
    navigationButtons.Add(tab);
    navigation.Controls.Add(tab);
  }

  TableLayoutPanel actions = GraphiteGrid(1);
  actions.Dock = DockStyle.Top;
  AddInstrumentDivider(actions);
  overrideStatus = GraphiteText("", 9, Color.Silver);
  GraphiteAdd(actions, overrideStatus);
  TableLayoutPanel buttons = GraphiteGrid(2);
  buttons.ColumnStyles.Clear();
  buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
  buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
  btnPause = GraphiteButton("Pause");
  btnPause.Height = 58;
  btnPause.Image = InstrumentIcon("Pause");
  btnPause.TextImageRelation = TextImageRelation.ImageBeforeText;
  btnPause.Margin = new Padding(0, 0, 5, 0);
  btnPause.Click += PauseClick;
  btnTurbo = GraphiteButton("Turbo Burst · 2 min");
  btnTurbo.Height = 58;
  btnTurbo.Font = new Font("Segoe UI Semibold", 11);
  btnTurbo.BackColor = GraphiteAccent;
  btnTurbo.ForeColor = GraphiteBackground;
  btnTurbo.FlatAppearance.MouseOverBackColor = Color.FromArgb(187, 255, 57);
  btnTurbo.Click += TurboBurstClick;
  GraphiteAdd(buttons, btnPause, 0, 0);
  GraphiteAdd(buttons, btnTurbo, 1, 0);
  GraphiteAdd(actions, buttons);
  right.Controls.Add(actions, 0, 1);

  dash = window;
  WireToggles();
  automationOptions.Hide();
  lightingOptions.Hide();
  deviceOptions.Hide();
  window.Shown += delegate {
    SyncDash();
    DashTick(null, EventArgs.Empty);
  };
  dashTimer = new System.Windows.Forms.Timer { Interval = 500 };
  dashTimer.Tick += DashTick;
  window.Show();
  dashTimer.Start();
}

static void AddInstrumentDivider(TableLayoutPanel grid) {
  Panel line = new Panel { Height = 1, BackColor = GraphiteBorder };
  GraphiteAdd(grid, new Panel { Height = 12 });
  GraphiteAdd(grid, line);
  GraphiteAdd(grid, new Panel { Height = 12 });
}

static Bitmap InstrumentIcon(string kind) {
  Bitmap image = new Bitmap(28, 28);
  using (Graphics g = Graphics.FromImage(image))
  using (Pen pen = new Pen(Color.Silver, 1.7f)) {
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    switch (kind) {
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

sealed class FanDial : Panel {
  public FanDial() {
    DoubleBuffered = true;
    ResizeRedraw = true;
  }

  protected override void OnPaint(PaintEventArgs e) {
    base.OnPaint(e);
    int size = Math.Min(ClientSize.Width, ClientSize.Height);
    if (size < 40) return;
    Graphics g = e.Graphics;
    var state = g.Save();
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    g.TranslateTransform(ClientSize.Width / 2f, ClientSize.Height / 2f);
    g.ScaleTransform(size / 440f, size / 440f);
    using (var surface = new System.Drawing.Drawing2D.HatchBrush(
      System.Drawing.Drawing2D.HatchStyle.LightDownwardDiagonal, Color.FromArgb(22, 24, 22), GraphiteBackground))
      g.FillEllipse(surface, -190, -190, 380, 380);
    using (Pen rim = new Pen(GraphiteBorder, 6))
      g.DrawArc(rim, -208, -208, 416, 416, 135, 270);
    const int segments = 56;
    int rpm = curRpm;
    int active = Math.Min(segments, Math.Max(0, rpm) * segments / (MAX_UNIT * FACTOR));
    using (Pen lit = new Pen(GraphiteAccent, 19))
    using (Pen unlit = new Pen(GraphiteBorder, 19))
    using (Pen tick = new Pen(Color.Silver, 1.4f)) {
      for (int i = 0; i < segments; i++)
        g.DrawArc(i < active ? lit : unlit, -192, -192, 384, 384, 135 + 270f * i / segments, 3.9f);
      for (int i = 0; i <= 18; i++) {
        double angle = (135 + i * 15) * Math.PI / 180;
        int inner = i % 3 == 0 ? 150 : 158;
        g.DrawLine(tick, (float)Math.Cos(angle) * inner, (float)Math.Sin(angle) * inner,
                   (float)Math.Cos(angle) * 168, (float)Math.Sin(angle) * 168);
      }
    }
    var fan = g.Save();
    g.TranslateTransform(0, -64);
    PaintFan(g, 1.9f);
    g.Restore(fan);
    using (var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
    using (var valueFont = new Font("Bahnschrift", 74, GraphicsUnit.Pixel))
    using (var unitFont = new Font("Segoe UI Semibold", 20, GraphicsUnit.Pixel)) {
      g.DrawString(rpm < 0 ? "—" : rpm.ToString("N0"), valueFont, Brushes.White, new PointF(0, 12), centered);
      g.DrawString("R P M", unitFont, Brushes.Silver, new PointF(0, 66), centered);
    }
    g.Restore(state);
  }
}

static TableLayoutPanel GraphiteGrid(int columns) {
  TableLayoutPanel panel = new TableLayoutPanel {
    AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = columns,
    Margin = Padding.Empty, GrowStyle = TableLayoutPanelGrowStyle.AddRows
  };
  for (int i = 0; i < columns; i++)
    panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
  return panel;
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

static TableLayoutPanel GraphiteCard(string title) {
  TableLayoutPanel card = GraphiteGrid(1);
  card.BackColor = GraphitePanel;
  card.Padding = new Padding(11, 8, 11, 3);
  GraphiteAdd(card, GraphiteText(title, 13, Color.White));
  return card;
}

static Label GraphiteText(string text, float size, Color color) {
  return new Label {
    AutoSize = true, Margin = Padding.Empty, Text = text,
    Font = new Font("Segoe UI Semibold", size), ForeColor = color
  };
}

static Label AddTemperature(TableLayoutPanel parent, string name, int column) {
  TableLayoutPanel group = GraphiteGrid(2);
  group.Margin = new Padding(5);
  group.Padding = new Padding(10, 6, 10, 5);
  group.BackColor = GraphitePanel;
  Label title = GraphiteText(name, 12, Color.Silver);
  title.TextAlign = ContentAlignment.MiddleLeft;
  GraphiteAdd(group, title, 0, 0);
  Label value = GraphiteText("--", 22, GraphiteAccent);
  value.AccessibleName = name + " temperature";
  value.TextAlign = ContentAlignment.MiddleRight;
  GraphiteAdd(group, value, 1, 0);
  GraphiteAdd(parent, group, column, 0);
  group.Dock = DockStyle.Fill;
  title.Dock = value.Dock = DockStyle.Fill;
  return value;
}

static Button GraphiteButton(string text) {
  Button button = new Button {
    Height = 34, Margin = Padding.Empty, Text = text, FlatStyle = FlatStyle.Flat,
    BackColor = GraphitePanel, ForeColor = Color.White, Cursor = Cursors.Hand,
    UseVisualStyleBackColor = false
  };
  button.FlatAppearance.BorderColor = GraphiteBorder;
  return button;
}

sealed class InstrumentSlider : TrackBar {
  public InstrumentSlider() {
    SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
  }

  protected override void OnValueChanged(EventArgs e) {
    base.OnValueChanged(e);
    Invalidate();
  }

  protected override void OnPaint(PaintEventArgs e) {
    base.OnPaint(e);
    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    const int left = 12;
    int width = Math.Max(0, ClientSize.Width - left * 2);
    int y = ClientSize.Height / 2;
    float thumb = left + width * (Value - Minimum) / (float)(Maximum - Minimum);
    using (Pen rail = new Pen(GraphiteBorder, 5))
    using (Pen progress = new Pen(GraphiteAccent, 5))
    using (Brush fill = new SolidBrush(GraphiteAccent)) {
      rail.StartCap = rail.EndCap = System.Drawing.Drawing2D.LineCap.Round;
      progress.StartCap = progress.EndCap = System.Drawing.Drawing2D.LineCap.Round;
      e.Graphics.DrawLine(rail, left, y, left + width, y);
      e.Graphics.DrawLine(progress, left, y, thumb, y);
      e.Graphics.FillEllipse(fill, thumb - 7, y - 7, 14, 14);
      e.Graphics.DrawEllipse(Pens.White, thumb - 7, y - 7, 14, 14);
    }
    if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
  }
}

static TrackBar AddSlider(TableLayoutPanel grid, string name, int min, int max, out Label value, EventHandler changed) {
  TableLayoutPanel heading = GraphiteGrid(2);
  heading.ColumnStyles.Clear();
  heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
  heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
  GraphiteAdd(heading, GraphiteText(name, 10, Color.White), 0, 0);
  value = GraphiteText("", 10, GraphiteAccent);
  value.TextAlign = ContentAlignment.MiddleRight;
  GraphiteAdd(heading, value, 1, 0);
  GraphiteAdd(grid, heading);
  TrackBar slider = new InstrumentSlider {
    Minimum = min, Maximum = max, Value = min, SmallChange = 1, LargeChange = 5,
    TickStyle = TickStyle.None, AutoSize = false, Height = 25,
    Margin = Padding.Empty, BackColor = GraphiteBackground, AccessibleName = name
  };
  slider.ValueChanged += changed;
  GraphiteAdd(grid, slider);
  return slider;
}

static CheckBox AddCheck(TableLayoutPanel grid, string text) {
  CheckBox check = new CheckBox { Text = text, AutoSize = true, Margin = Padding.Empty };
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
  lock (cfgLock) { manualUnit = v; manual = true; }
  SaveConfig();
  if (lblFanVal != null) lblFanVal.Text = (v * FACTOR).ToString("N0") + " RPM";
}
static void BriSliderChanged(object s, EventArgs e) {
  if (dashSyncing) return;
  int v = tbBri.Value;
  lock (cfgLock) { bri = v; }
  ledDirty = true; SaveConfig();
  if (lblBriVal != null) lblBriVal.Text = v.ToString() + "%";
}
static void LedComboChanged(object s, EventArgs e) {
  if (dashSyncing) return;
  string v = cbLed.SelectedItem == null ? "heatmap" : cbLed.SelectedItem.ToString();
  lock (cfgLock) {
    if (v == "static") { led = "static"; } else led = v;
  }
  ledDirty = true; SaveConfig();
  string tagSel = v == "static" ? "static:" + ledColor : v;
  foreach (var it in ledItems) {
    string t = (string)it.Tag;
    string bt = t.StartsWith("static:") ? "static" : t;
    string lt = led == "static" ? "static:" + ledColor : led;
    it.Checked = (bt == "static") ? string.Equals(t, lt, StringComparison.OrdinalIgnoreCase)
                                  : string.Equals(bt, led, StringComparison.OrdinalIgnoreCase);
  }
  LayoutColors();
}
static void SmoothChanged(object s, EventArgs e) {
  if (dashSyncing) return;
  try { ApplySmooth(tbSmooth.Value); } catch { }
  SyncDash();
  SaveConfig();
}
static void LayoutColors() {
  foreach (var swatch in colorSwatches) {
    bool selected = led == "static" && string.Equals(ledColor, swatch.Key, StringComparison.OrdinalIgnoreCase);
    swatch.Value.FlatAppearance.BorderColor = selected ? Color.White : GraphiteBorder;
    swatch.Value.FlatAppearance.BorderSize = selected ? 3 : 1;
  }
}
static void SyncDash() {
  if (dash == null || dash.IsDisposed) return;
  try {
    dashSyncing = true;
    foreach (Button button in profBtns) {
      bool selected = manual ? button.Text == "Manual" :
        string.Equals(prof, button.Text, StringComparison.OrdinalIgnoreCase);
      button.BackColor = selected ? Color.FromArgb(47, 67, 19) : GraphitePanel;
      button.FlatAppearance.BorderColor = selected ? GraphiteAccent : GraphiteBorder;
      button.ForeColor = selected ? GraphiteAccent : Color.White;
      button.AccessibleDescription = selected ? "Selected fan profile" : "";
    }
    bool automatic = !manual && prof != "turbo";
    automaticControls.Visible = automatic;
    manualControls.Visible = manual;
    turboControls.Visible = !manual && !automatic;
    ckLoadBoost.Visible = automatic;
    ckWearCap.Visible = automatic;
    ckSteady.Visible = automatic;
    tbFan.Value = Math.Max(MIN_UNIT, Math.Min(MAX_UNIT, manualUnit));
    lblFanVal.Text = (manualUnit * FACTOR).ToString("N0") + " RPM";
    int effect = cbLed.Items.IndexOf(led);
    cbLed.SelectedIndex = effect;
    LayoutColors();
    if (tbBri != null && tbBri.Value != bri) tbBri.Value = Math.Max(0, Math.Min(100, bri));
    if (lblBriVal != null) lblBriVal.Text = bri.ToString() + "%";
    if (ckAutoTurbo != null) ckAutoTurbo.Checked = autoTurbo;
    if (ckLoadBoost != null) ckLoadBoost.Checked = loadBoost;
    if (ckWearCap != null) ckWearCap.Checked = (rpmCap > 0);
    if (ckSteady != null) ckSteady.Checked = (deadband > 0 && downHold > 1);
    if (ckCoolTail != null) ckCoolTail.Checked = coolTail;
    if (ckIdleDim != null) ckIdleDim.Checked = idleDim;
    if (ckNightDim != null) ckNightDim.Checked = nightDim;
    if (ckHyper != null) ckHyper.Checked = hyperBoost;
    if (tbSmooth != null && tbSmooth.Value != smooth) tbSmooth.Value = Math.Max(0, Math.Min(100, smooth));
    if (lblSmoothVal != null) lblSmoothVal.Text = smooth + "%";
    if (ckAutoStart != null) ckAutoStart.Checked = IsAutostartEnabled();
    btnPause.Text = paused ? "Resume" : "Pause";
    dashSyncing = false;
  } catch (Exception ex) { dashSyncing = false; try { LogCrash("SyncDash: " + ex.Message); } catch { } }
}
// per-toggle handlers wired after Check() so each writes exactly one setting
static void WireToggles() {
  if (ckAutoTurbo != null) ckAutoTurbo.CheckedChanged += new EventHandler(delegate (object s, EventArgs e) {
    if (dashSyncing) return;
    autoTurbo = ckAutoTurbo.Checked; SaveConfig();
  });
  if (ckLoadBoost != null) ckLoadBoost.CheckedChanged += new EventHandler(delegate (object s, EventArgs e) {
    if (dashSyncing) return;
    loadBoost = ckLoadBoost.Checked; SaveConfig();
  });
  if (ckWearCap != null) ckWearCap.CheckedChanged += new EventHandler(delegate (object s, EventArgs e) {
    if (dashSyncing) return;
    if (ckWearCap.Checked) { if (rpmCap <= 0) rpmCap = 2300; lastRpmCap = rpmCap; } else { if (rpmCap > 0) lastRpmCap = rpmCap; rpmCap = 0; }
    SaveConfig();
  });
  if (ckSteady != null) ckSteady.CheckedChanged += new EventHandler(delegate (object s, EventArgs e) {
    if (dashSyncing) return;
    if (ckSteady.Checked) { if (deadband <= 0) deadband = 2; if (downHold <= 1) downHold = 3; }
    else { deadband = 0; downHold = 1; }
    SaveConfig();
  });
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
  });
}
static void DashTick(object sender, EventArgs e) {
  if (dash == null || dash.IsDisposed) return;
  int cpu = curCpu, gpu = curGpu, rpm = curRpm;
  lblCpu.Text = cpu < 0 ? "--" : cpu + " °C";
  lblGpu.Text = gpu < 0 ? "--" : gpu + " °C";
  speedMeter.AccessibleDescription = rpm < 0 ? "Speed unavailable" : rpm.ToString("N0") + " RPM";
  speedMeter.Invalidate();
  bool connected = devH != new IntPtr(-1);
  lblHead.Text = connected ? "USB connected" : "USB disconnected";
  int now = Environment.TickCount, deadline = Volatile.Read(ref manualTurboUntil);
  int seconds = NotExpired(now, deadline) ? (unchecked(deadline - now) + 999) / 1000 : 0;
  btnTurbo.Enabled = !paused && connected;
  btnTurbo.Text = seconds > 0 ? "End Burst · " + TimeSpan.FromSeconds(seconds).ToString(@"m\:ss") : "Turbo Burst · 2 min";
  string reason = fanOverride;
  overrideStatus.Text = paused ? "Fan control paused" :
    connected && reason == "Critical temperature" ? reason + " override" :
    connected && seconds > 0 ? "Turbo Burst override" :
    connected && reason == "Auto-Turbo" ? reason + " override" : " ";
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
    string txt = "ts=" + DateTime.Now.ToString("HH:mm:ss") + " paused=" + paused + " profile=" + prof + " led=" + led + " bri=" + bri +
      " cpu=" + curCpu + " gpu=" + curGpu + " unit=" + curUnit + " rpm=" + curRpm + " | " + curStatus;
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
    else if (a == "--maxstep" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) maxStep = v; }
    else if (a == "--led" && i + 1 < args.Length) led = args[++i].ToLowerInvariant();
    else if (a == "--ledcolor" && i + 1 < args.Length) ledColor = args[++i];
    else if (a == "--ledbri" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) bri = v; }
    else if (a == "--critical" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) critical = v; }
    else if (a == "--profile" && i + 1 < args.Length) prof = args[++i].ToLowerInvariant();
    else if (a == "--noautoturbo") autoTurbo = false;
    else if (a == "--idledim") idleDim = true;
    else if (a == "--nightdim") nightDim = true;
    else if (a == "--keep") cliKeep = true;
    else if (a == "--rpmcap" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) rpmCap = v; }
    else if (a == "--captemp" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) capTemp = v; }
    else if (a == "--deadband" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) deadband = v; }
    else if (a == "--downhold" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) downHold = v; }
    else if (a == "--cooltemp" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) coolTemp = v; }
    else if (a == "--nocooltail") coolTail = false;
    else if (a == "--hyperboost") hyperBoost = true;
    else if (a == "--manual") { manual = true; if (i + 1 < args.Length) { int v; if (int.TryParse(args[i + 1], out v)) { manualUnit = v; i++; } } }
    else if (a == "--manualunit" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) { manualUnit = v; manual = true; } }
    else if (a == "--coast" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) coast = v; }
    else if (a == "--smooth" && i + 1 < args.Length) { int v; if (int.TryParse(args[++i], out v)) ApplySmooth(v); }
    else if (a == "--open") openOnStart = true;
    else if (a == "--minimized") openOnStart = false;
    else if (a == "--nostabilize") { deadband = 0; downHold = 1; }
  }
  if (interval < 500) interval = 500;
  if (bri < 0) bri = 0; if (bri > 100) bri = 100;
  if (maxStep < 0) maxStep = 0; if (maxStep > 54) maxStep = 54;
  if (rpmCap < 0) rpmCap = 0; if (rpmCap > MAX_UNIT * FACTOR) rpmCap = MAX_UNIT * FACTOR;
  if (capTemp < 1) capTemp = 1; if (capTemp > 120) capTemp = 120;
  if (deadband < 0) deadband = 0; if (deadband > 20) deadband = 20;
  if (downHold < 1) downHold = 1; if (downHold > 60) downHold = 60;
  if (coolTemp < 20) coolTemp = 20; if (coolTemp > 95) coolTemp = 95;
  if (manualUnit < MIN_UNIT) manualUnit = MIN_UNIT; if (manualUnit > MAX_UNIT) manualUnit = MAX_UNIT;
  if (coast < 1) coast = 1; if (coast > 120) coast = 120;
  if (smooth < 0) smooth = 0; if (smooth > 100) smooth = 100;
  if (rpmCap > 0) lastRpmCap = rpmCap;
  if (cliKeep) keepOnExit = true;
  foreach (string a0 in args) { if (a0.ToLowerInvariant() == "--status") { StatusPrint(); return; } }
  for (int i = 0; i < args.Length; i++) { if (args[i].ToLowerInvariant() == "--status-file" && i + 1 < args.Length) statusFilePath = args[++i]; }

  // single-instance guard: if another tray is already running, this one quietly retires
  bool createdNew = false;
  Mutex single = null;
  try { single = new Mutex(true, "RazerPadTray_SingleInstance", out createdNew); } catch { }
  if (single != null && !createdNew) {
    try { tray = new NotifyIcon(); tray.Visible = false; tray.Dispose(); } catch { }
    if (single != null) { try { single.Close(); } catch { } }
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
  CheckGroup(fanItems, prof);
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
    cfgWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
    cfgWatcher.Changed += new FileSystemEventHandler(CfgChanged);
    cfgWatcher.Created += new FileSystemEventHandler(CfgChanged);
    cfgWatcher.EnableRaisingEvents = true;
  } catch { }

  try { if (hyperBoost) ApplyHyperBoost(true); } catch { }
  Application.Run();

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
      try { MatrixOff(devH); } catch { }
      CloseHandle(devH);
      devH = new IntPtr(-1);
    }
  } catch { }
  if (tray != null) { tray.Visible = false; tray.Dispose(); }
  if (single != null) { try { single.ReleaseMutex(); single.Close(); } catch { } }
}
}
}
