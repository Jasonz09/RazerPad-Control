using System;
using System.Drawing;
using System.Windows.Forms;

namespace RazerPadFan {
internal static partial class Program {
  static TableLayoutPanel StudioLayout(Panel page) {
    var layout = GraphiteGrid(1);
    layout.Dock = DockStyle.Top;
    page.Controls.Add(layout);
    return layout;
  }

  static TableLayoutPanel StudioColumns(float left, float right) {
    var layout = GraphiteGrid(2);
    layout.ColumnStyles[0].Width = left;
    layout.ColumnStyles[1].Width = right;
    return layout;
  }

  static Panel BuildCoolingPage() {
    var page = WorkspacePage();
    var layout = StudioLayout(page);
    GraphiteAdd(layout, GraphiteText("Cooling", 24, Color.White));
    WorkspaceGap(layout, 6);
    var instruments = StudioColumns(66, 34);
    coolingHistory = new FanHistory {
      Height = 302, Margin = new Padding(0, 0, 12, 0), AccessibleName = "Fan command history"
    };
    GraphiteAdd(instruments, coolingHistory, 0, 0);
    var command = GraphiteGrid(1);
    command.AutoSize = false;
    command.Height = coolingHistory.Height;
    command.Padding = new Padding(15, 12, 15, 9);
    OutlineSurface(command, 13, 0);
    command.BackColor = GraphitePanel;
    GraphiteAdd(command, GraphiteText("Current fan command", 10, WorkspaceMuted));
    lblRpm = GraphiteText("— RPM", 27, Color.FromArgb(220, 201, 255));
    GraphiteAdd(command, lblRpm);
    WorkspaceRule(command);
    GraphiteAdd(command, GraphiteText("Fan mode", 10, WorkspaceText));
    profBtns.Clear();
    foreach (FanMode mode in Enum.GetValues(typeof(FanMode))) {
      string name = mode.ToString();
      var button = GraphiteButton(name);
      button.Tag = name;
      button.Height = 23;
      button.Font = new Font("Segoe UI", 9f);
      button.TextAlign = ContentAlignment.MiddleLeft;
      button.Radio = true;
      button.AccessibleRole = AccessibleRole.RadioButton;
      button.CornerRadius = 6;
      button.Margin = Padding.Empty;
      button.Click += delegate { SelectFanMode(mode); };
      GraphiteAdd(command, button);
      profBtns.Add(button);
    }
    fanModeSummary = GraphiteText("", 9, WorkspaceMuted);
    GraphiteAdd(command, fanModeSummary);
    GraphiteAdd(instruments, command, 1, 0);
    GraphiteAdd(layout, instruments);

    var footer = StudioColumns(59, 41);
    var readouts = GraphiteGrid(3);
    readouts.ColumnStyles[0].Width = 53;
    readouts.ColumnStyles[1].Width = 23.5f;
    readouts.ColumnStyles[2].Width = 23.5f;
    readouts.Padding = new Padding(3);
    readouts.Margin = new Padding(0, 0, 13, 0);
    OutlineSurface(readouts, 12, 0);
    GraphiteAdd(readouts, new PadPreview { Height = 73, Compact = true,
                                         AccessibleName = "Cooling pad preview" }, 0, 0);
    lblCpu = AddTemperature(readouts, "CPU", 1);
    lblGpu = AddTemperature(readouts, "GPU", 2);
    GraphiteAdd(footer, readouts, 0, 0);
    btnPause = GraphiteButton("Pause");
    btnPause.Width = 150;
    btnPause.Margin = new Padding(0, 15, 0, 0);
    btnPause.CornerRadius = 10;
    btnPause.Image = InstrumentIcon("Pause");
    btnPause.TextImageRelation = TextImageRelation.ImageBeforeText;
    btnPause.Click += PauseClick;
    GraphiteAdd(footer, btnPause, 1, 0);
    btnPause.Dock = DockStyle.Right;
    GraphiteAdd(layout, footer);
    overrideStatus = GraphiteText("", 9, WorkspaceMuted);
    GraphiteAdd(layout, overrideStatus);

    var fixedSpeed = GraphiteGrid(1);
    manualControls = fixedSpeed;
    tbFan = AddSlider(fixedSpeed, "Target speed", MIN_UNIT, MAX_UNIT, out lblFanVal, FanSliderChanged);
    GraphiteAdd(fixedSpeed, GraphiteText("500–3,200 RPM in 50 RPM steps", 8, WorkspaceMuted));
    GraphiteAdd(layout, fixedSpeed);
    return page;
  }

  static Panel BuildDevicePage() {
    var page = WorkspacePage();
    var layout = StudioLayout(page);
    GraphiteAdd(layout, GraphiteText("Device", 24, Color.White));
    WorkspaceGap(layout, 8);
    var columns = StudioColumns(57, 43);
    var hardware = GraphiteGrid(1);
    hardware.Margin = new Padding(0, 0, 20, 0);
    GraphiteAdd(hardware, new PadPreview { Height = 375, AccessibleName = "Connected cooling pad" });
    deviceConnection = GraphiteText("Connecting", 10, WorkspaceMuted);
    GraphiteAdd(hardware, deviceConnection);
    deviceConnection.TextAlign = ContentAlignment.MiddleCenter;
    GraphiteAdd(columns, hardware, 0, 0);

    var system = GraphiteGrid(1);
    system.Padding = new Padding(12, 14, 4, 0);
    GraphiteAdd(system, GraphiteText("System settings", 14, Color.White));
    WorkspaceGap(system, 5);
    WorkspaceRule(system);
    ckHyper = AddDeviceCheck(system, "HyperBoost", "HyperBoost");
    ckHyper.AccessibleDescription = "Keeps PCIe and USB power saving off while plugged in.";
    WorkspaceRule(system);
    ckAutoStart = AddDeviceCheck(system, "Start with Windows", "Windows");
    WorkspaceRule(system);
    ckCoolTail = AddDeviceCheck(system, "Wind fan down on exit", "Cooling");
    WorkspaceRule(system);
    WorkspaceGap(system, 6);
    Button exit = TextAction("Exit app", WorkspaceMuted);
    exit.Click += ExitClick;
    GraphiteAdd(system, exit);
    exit.Dock = DockStyle.Left;
    GraphiteAdd(columns, system, 1, 0);
    GraphiteAdd(layout, columns);
    return page;
  }

  static CheckBox AddDeviceCheck(TableLayoutPanel layout, string label, string icon) {
    var toggle = (GraphiteToggle)AddCheck(layout, label);
    toggle.Icon = InstrumentIcon(icon);
    toggle.Height = 46;
    return toggle;
  }
}
}
