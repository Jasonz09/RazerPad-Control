using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerPadFan {
internal static partial class Program {
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
}
}
