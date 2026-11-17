using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace RazerPadFan {
internal static partial class Program {
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
}
}
