using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace RazerPadFan {
internal static partial class Program {
  static readonly Color WorkspaceText = Color.FromArgb(237, 232, 249);
  static readonly Color WorkspaceMuted = Color.FromArgb(175, 167, 199);
  static LightingGallery lightingGallery;

  static RoundedButton TextAction(string text, Color color) {
    var button = new RoundedButton {
      AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 28),
      Margin = Padding.Empty, Padding = new Padding(10, 0, 10, 0),
      Text = text, FlatStyle = FlatStyle.Flat, Quiet = true,
      ForeColor = color, Cursor = Cursors.Hand, UseVisualStyleBackColor = false
    };
    button.FlatAppearance.BorderSize = 0;
    return button;
  }

  static void WorkspaceGap(TableLayoutPanel layout, int height) {
    GraphiteAdd(layout, new Panel { Height = height });
  }

  static void WorkspaceRule(TableLayoutPanel layout) {
    GraphiteAdd(layout, new Panel { BackColor = GraphiteBorder, Height = 1 });
  }

  static FlowLayoutPanel Inline(FlowDirection direction) {
    return new FlowLayoutPanel {
      FlowDirection = direction, WrapContents = false, AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty
    };
  }

  sealed class LightingGallery : UserControl {
    readonly ThemeTile single = new ThemeTile { Text = "Single color" };
    readonly ThemeTile rgb = new ThemeTile { Text = "RGB", Rainbow = true };
    readonly TableLayoutPanel saved = GraphiteGrid(3);
    readonly Label count, limit, currentMode, feedback;
    readonly RoundedButton create;
    readonly Action<SavedEffect> openEditor;
    readonly Dictionary<Guid, ThemeTile> tiles = new Dictionary<Guid, ThemeTile>();
    readonly ToolTip tips = new ToolTip();
    SavedEffect[] rendered = new SavedEffect[0];
    ToolStripDropDown colorPopup;

    public LightingGallery(Action<SavedEffect> openEditor) {
      this.openEditor = openEditor;
      AutoSize = true;
      AutoSizeMode = AutoSizeMode.GrowAndShrink;
      var layout = GraphiteGrid(1);
      layout.Dock = DockStyle.Top;
      Controls.Add(layout);
      currentMode = GraphiteText("", 9, WorkspaceMuted);
      GraphiteAdd(layout, currentMode);

      var choices = GraphiteGrid(2);
      AddTile(choices, single, null, 0, 0);
      AddTile(choices, rgb, null, 1, 0);
      single.Click += delegate {
        if (colorPopup != null) return;
        Color color = SingleColor();
        if (ChangeColor(color)) ShowColorPicker(color);
      };
      rgb.Click += delegate { Commit(delegate { return SaveLightingChange(delegate { led = "rainbow"; }); }); };
      GraphiteAdd(layout, choices);

      WorkspaceGap(layout, 12);
      WorkspaceRule(layout);
      var heading = GraphiteGrid(3);
      heading.ColumnStyles[0].Width = 51;
      heading.ColumnStyles[1].Width = 22;
      heading.ColumnStyles[2].Width = 27;
      Label headingText = GraphiteText("Custom effects", 13, WorkspaceText);
      GraphiteAdd(heading, headingText, 0, 0);
      headingText.Dock = DockStyle.Fill;
      headingText.TextAlign = ContentAlignment.MiddleLeft;
      count = GraphiteText("", 9, WorkspaceMuted);
      GraphiteAdd(heading, count, 1, 0);
      count.Dock = DockStyle.Fill;
      count.TextAlign = ContentAlignment.MiddleRight;
      create = GraphiteButton("+  Custom");
      create.AccessibleName = "Custom";
      create.AccessibleDescription = "Create a custom effect";
      create.Click += delegate { CloseColorPicker(); openEditor(null); };
      create.Height = 34;
      UsePrimaryButton(create);
      create.Margin = new Padding(8, 0, 0, 0);
      GraphiteAdd(heading, create, 2, 0);
      GraphiteAdd(layout, heading);

      limit = GraphiteText("Delete a custom effect to make room.", 9, WorkspaceMuted);
      GraphiteAdd(layout, limit);
      GraphiteAdd(layout, saved);
      feedback = GraphiteText("", 9, Color.FromArgb(255, 165, 177));
      GraphiteAdd(layout, feedback);

      var options = GraphiteGrid(1);
      lightingOptions = options;
      tbBri = AddSlider(options, "Brightness", 0, 100, out lblBriVal, BriSliderChanged, persist: true);
      ckIdleDim = AddCheck(options, "Dim when idle or cool");
      ckNightDim = AddCheck(options, "Dim at night (22:00–07:00)");
      AddDisclosure(layout, "Lighting options", options);
    }

    static Color SingleColor() {
      string tint = EffectColor(led == "singlecustom" ? "singlecustom" : "static");
      return ParseHex(tint ?? ColorName(ledColor));
    }

    bool ChangeColor(Color color) {
      lock (cfgLock) singleColorDraft = null;
      return Commit(delegate {
        return SaveLightingChange(delegate {
          ledColor = HexColor(color);
          effectColors.Remove("static");
          led = "static";
        });
      }, false);
    }

    void ShowColorPicker(Color initial) {
      var content = GraphiteGrid(1);
      content.AutoSize = false;
      content.Size = new Size(248, 200);
      content.Padding = new Padding(9, 8, 9, 4);
      content.BackColor = GraphiteBackground;
      var picker = new ColorPicker { Height = 148 };
      var hex = new HexField("Single color hex");
      picker.SetColor(initial);
      hex.SetColor(initial);
      picker.ColorChanged += delegate {
        Color color = picker.Value;
        hex.SetColor(color);
        if (picker.Capture) {
          lock (cfgLock) singleColorDraft = color;
        }
      };
      picker.ColorCommitted += delegate { if (!ChangeColor(picker.Value)) CloseColorPicker(); };
      hex.ColorCommitted += delegate(Color color) {
        if (ChangeColor(color)) picker.SetColor(color);
        else CloseColorPicker();
      };
      GraphiteAdd(content, picker);
      GraphiteAdd(content, hex);
      var host = new ToolStripControlHost(content) { AutoSize = false, Size = content.Size, Margin = Padding.Empty };
      var popup = new ToolStripDropDown { Padding = new Padding(1), BackColor = SurfaceEdge };
      popup.Items.Add(host);
      popup.Closed += delegate {
        lock (cfgLock) singleColorDraft = null;
        colorPopup = null;
        if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)popup.Dispose);
        else popup.Dispose();
      };
      colorPopup = popup;
      popup.Show(single, new Point(0, single.Height + 3));
      picker.Focus();
    }

    public void CloseColorPicker() {
      if (colorPopup != null) colorPopup.Close();
    }

    public void FocusSelection() {
      ThemeTile active = tiles.Values.FirstOrDefault(tile => tile.Selected);
      if (active != null) active.Focus();
      else if (create.Enabled) create.Focus();
      else rgb.Focus();
    }

    bool Commit(Func<bool> change, bool closePicker = true) {
      if (closePicker) CloseColorPicker();
      bool savedChange = change();
      feedback.Text = savedChange ? "" : "Could not save the lighting change.";
      feedback.Visible = !savedChange;
      ledDirty = true;
      CheckGroup(ledItems, led == "static" ? "static:" + ledColor : led);
      CheckGroup(briItems, bri.ToString());
      SyncDash();
      return savedChange;
    }

    void OpenMenu(Control button, Guid id) {
      SavedEffect entry;
      lock (cfgLock) entry = FindSavedEffect(id);
      if (entry == null) return;
      var menu = new ContextMenuStrip();
      menu.BackColor = GraphitePanel;
      menu.ForeColor = WorkspaceText;
      menu.Items.Add("Edit", null, delegate { openEditor(entry); });
      menu.Items.Add("Delete", null, delegate {
        bool active;
        lock (cfgLock) active = led == "custom" && selectedCustomId == id;
        string question = "Delete “" + entry.Name + "”?";
        if (active) question += "\n\nRGB will become the active lighting theme.";
        if (MessageBox.Show(this, question, "Delete custom effect", MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        if (Commit(delegate { return DeleteSavedEffect(id); })) FocusSelection();
      });
      menu.Closed += delegate { BeginInvoke((Action)menu.Dispose); };
      menu.Show(button, new Point(button.Width, button.Height), ToolStripDropDownDirection.BelowLeft);
    }

    void AddTile(TableLayoutPanel grid, ThemeTile tile, Guid? id, int column, int row) {
      var cell = new Panel { Height = 106, Margin = new Padding(0, 1, 8, 8) };
      tile.Dock = DockStyle.Fill;
      cell.Controls.Add(tile);
      if (id.HasValue) {
        Guid effectId = id.Value;
        var menu = new ThemeOptionsButton(tile) { Size = new Size(29, 26) };
        menu.AccessibleName = "Options for " + tile.Text;
        tips.SetToolTip(menu, menu.AccessibleName);
        tile.HasMenu = true;
        cell.Controls.Add(menu);
        cell.Resize += delegate { menu.Location = new Point(cell.Width - 34, cell.Height - 29); };
        menu.Location = new Point(cell.Width - 34, cell.Height - 29);
        menu.Click += delegate { OpenMenu(menu, effectId); };
        menu.BringToFront();
      }
      tile.AccessibleName = tile.Text;
      tips.SetToolTip(tile, tile.Text);
      GraphiteAdd(grid, cell, column, row);
    }

    void ShowEffects(SavedEffect[] entries) {
      if (rendered.SequenceEqual(entries, SavedEffectDisplayComparer.Instance)) return;
      saved.SuspendLayout();
      try {
        foreach (Control control in saved.Controls.Cast<Control>().ToArray()) control.Dispose();
        tiles.Clear();
        for (int i = 0; i < entries.Length; i++) {
          SavedEffect entry = entries[i];
          Guid id = entry.Id;
          var tile = new ThemeTile { Text = entry.Name, Effect = entry.Effect };
          tiles.Add(id, tile);
          tile.Click += delegate { Commit(delegate { return ActivateSavedEffect(id); }); };
          AddTile(saved, tile, id, i % 3, i / 3);
        }
        rendered = entries;
      } finally { saved.ResumeLayout(); }
    }

    public void SyncSelection() {
      string mode;
      Guid currentId;
      SavedEffect[] entries;
      Color color;
      lock (cfgLock) {
        mode = CanonicalEffect(led);
        currentId = selectedCustomId;
        entries = savedEffects;
        color = SingleColor();
      }
      if (mode != "static" && mode != "singlecustom") CloseColorPicker();
      single.Solid = color;
      single.Selected = mode == "static" || mode == "singlecustom";
      rgb.Selected = mode == "rainbow";
      ShowEffects(entries);
      foreach (var entry in tiles) entry.Value.Selected = mode == "custom" && entry.Key == currentId;
      bool full = entries.Length >= MaxCustomEffects;
      count.Text = entries.Length + " / " + MaxCustomEffects + " saved";
      limit.Visible = full;
      create.Enabled = !full;
      bool legacy = !single.Selected && !rgb.Selected && !tiles.Values.Any(tile => tile.Selected);
      currentMode.Visible = legacy;
      if (legacy) currentMode.Text = "Active: " + LegacyModeTitle(mode);
    }

    static string LegacyModeTitle(string mode) {
      switch (mode) {
        case "wave": return "Temperature wave";
        case "heatmap": return "Heatmap";
        case "level": return "Temperature meter";
        case "loadmeter": return "Load meter";
        case "breath": return "Breathing";
        case "spectrum": return "Spectrum";
        case "off": return "Off";
        default: return mode;
      }
    }

    protected override void Dispose(bool disposing) {
      if (disposing) {
        CloseColorPicker();
        tips.Dispose();
      }
      base.Dispose(disposing);
    }
  }

  sealed class SavedEffectDisplayComparer : IEqualityComparer<SavedEffect> {
    public static readonly SavedEffectDisplayComparer Instance = new SavedEffectDisplayComparer();
    public bool Equals(SavedEffect a, SavedEffect b) {
      return a.Id == b.Id && a.Name == b.Name && ReferenceEquals(a.Effect, b.Effect);
    }
    public int GetHashCode(SavedEffect value) { return value.Id.GetHashCode(); }
  }

  sealed class ThemeTile : Button {
    public Color Solid;
    public CustomEffect Effect;
    public bool Rainbow, HasMenu;
    bool selected, hover;

    public ThemeTile() {
      FlatStyle = FlatStyle.Flat;
      AccessibleRole = AccessibleRole.RadioButton;
      Cursor = Cursors.Hand;
      SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
               ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public bool Selected {
      get { return selected; }
      set {
        selected = value;
        AccessibleDescription = value ? "Active lighting theme" : "Inactive lighting theme";
        Invalidate();
      }
    }

    public Color TileColor { get { return selected ? Color.FromArgb(33, 23, 54) : GraphitePanel; } }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e) {
      var g = e.Graphics;
      g.Clear(GraphiteBackground);
      if (Width < 38 || Height < 50) return;
      var surface = new Rectangle(1, 1, Width - 3, Height - 3);
      Color fill = TileColor;
      if (hover) fill = ControlPaint.Light(fill, .05f);
      Color edge = selected || Focused && ShowFocusCues ? GraphiteAccent : SurfaceEdge;
      PaintSurface(g, surface, 11, fill, edge, 0);
      var sample = new Rectangle(8, 8, Width - 17, Height - 43);
      using (var shape = RoundedPath(sample, 7)) {
        if (Rainbow || Effect != null && Effect.Kind == CustomEffectKind.Spectrum)
          GradientEditor.PaintStops(g, sample, rgbSwatch, 7);
        else if (Effect != null && Effect.Kind == CustomEffectKind.Wave)
          GradientEditor.PaintStops(g, sample, Effect.Stops, 7);
        else {
          using (var brush = new SolidBrush(Effect == null ? Solid : Effect.SolidColor)) g.FillPath(brush, shape);
        }
      }
      int y = Height - 18;
      using (var outline = new Pen(selected ? GraphiteAccent : WorkspaceMuted, 1.3f))
        g.DrawEllipse(outline, 10, y - 6, 12, 12);
      if (selected)
        using (var dot = new SolidBrush(GraphiteAccent)) g.FillEllipse(dot, 13, y - 3, 6, 6);
      const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
                                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
      int right = Width - (HasMenu ? 33 : 9);
      int badgeWidth = TextRenderer.MeasureText(g, "Active", Font, Size.Empty, TextFormatFlags.NoPadding).Width + 8;
      if (selected && right >= 26 + 74 + badgeWidth) {
        right -= badgeWidth;
        TextRenderer.DrawText(g, "Active", Font, new Rectangle(right, y - 12, badgeWidth, 23),
                              GraphiteAccent, flags | TextFormatFlags.Right);
      }
      TextRenderer.DrawText(g, Text, Font, new Rectangle(26, y - 12, Math.Max(1, right - 26), 23), WorkspaceText, flags);
      if (HasMenu) ThemeOptionsButton.PaintDots(g, Width - 20, y);
    }

    static readonly ColorStop[] rgbSwatch = {
      new ColorStop(0, Color.FromArgb(255, 44, 76)), new ColorStop(19, Color.Orange),
      new ColorStop(34, Color.Yellow), new ColorStop(51, Color.SpringGreen),
      new ColorStop(66, Color.DeepSkyBlue), new ColorStop(82, Color.Blue),
      new ColorStop(100, Color.Magenta)
    };
  }

  sealed class ThemeOptionsButton : Button {
    readonly ThemeTile tile;
    bool hovered;

    public ThemeOptionsButton(ThemeTile tile) {
      this.tile = tile;
      FlatStyle = FlatStyle.Flat;
      Cursor = Cursors.Hand;
      SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e) {
      Graphics g = e.Graphics;
      g.Clear(tile.TileColor);
      if (hovered || Focused)
        PaintSurface(g, Rectangle.Inflate(ClientRectangle, -2, -2), 5, GraphiteBorder, Color.Empty, 0);
      PaintDots(g, 14, 11);
    }

    public static void PaintDots(Graphics graphics, int x, int y) {
      graphics.SmoothingMode = SmoothingMode.AntiAlias;
      using (var dot = new SolidBrush(WorkspaceMuted))
        for (int offset = -5; offset <= 5; offset += 5)
          graphics.FillEllipse(dot, x + offset - 1.25f, y - 1.25f, 2.5f, 2.5f);
    }
  }

  sealed class SwatchButton : Button {
    Color color;
    public Color Swatch {
      get { return color; }
      set { color = value; Invalidate(); }
    }

    public SwatchButton(string name, Color swatch) {
      Swatch = swatch;
      AccessibleName = name;
      Size = new Size(27, 29);
      Margin = new Padding(0, 0, 2, 0);
      Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e) {
      e.Graphics.Clear(GraphiteBackground);
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      var swatch = new Rectangle(4, 5, 18, 18);
      using (var fill = new LinearGradientBrush(swatch, ControlPaint.Light(Swatch, .16f), Swatch, 90f))
      using (var edge = new Pen(Color.FromArgb(85, WorkspaceText))) {
        e.Graphics.FillEllipse(fill, swatch);
        e.Graphics.DrawEllipse(edge, swatch);
      }
      if (Focused && ShowFocusCues)
        e.Graphics.DrawEllipse(Pens.White, 3, 4, 20, 20);
    }
  }

  sealed class HexField : UserControl {
    readonly TextBox input;
    Color value;
    public event Action<Color> ColorCommitted;

    public HexField(string name, bool pickColor = false) {
      Height = 30;
      TabStop = false;
      input = new TextBox {
        Left = 31, Top = 6, Width = 77, ForeColor = WorkspaceText, BackColor = GraphiteBackground,
        BorderStyle = BorderStyle.None, MaxLength = 7, AccessibleName = name, CharacterCasing = CharacterCasing.Upper
      };
      Controls.Add(input);
      if (pickColor) {
        var swatch = new SwatchButton("Choose color", Color.Black) { Location = new Point(0, 0) };
        swatch.Click += delegate {
          Commit();
          using (var picker = new ColorDialog { Color = value, FullOpen = true }) {
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Color.ToArgb() == value.ToArgb()) return;
            SetColor(picker.Color);
            if (ColorCommitted != null) ColorCommitted(value);
          }
        };
        Controls.Add(swatch);
        colorButton = swatch;
      }
      input.KeyDown += delegate(object sender, KeyEventArgs e) {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        Commit();
      };
      input.Leave += delegate { Commit(); };
    }

    readonly SwatchButton colorButton;

    public void SetMarkerName(string name) {
      input.AccessibleName = name + " hex";
      colorButton.AccessibleName = "Choose color for " + name;
    }

    public void SetColor(Color color) {
      if (color.ToArgb() == value.ToArgb() && (input.Focused || input.Text == "#" + HexColor(color))) return;
      value = color;
      input.Text = "#" + HexColor(color);
      if (colorButton != null) colorButton.Swatch = value;
      Invalidate();
    }

    public void Commit() {
      Color parsed;
      bool changed = TryColor(input.Text, out parsed) && parsed.ToArgb() != value.ToArgb();
      if (changed) value = parsed;
      input.Text = "#" + HexColor(value);
      if (colorButton != null) colorButton.Swatch = value;
      Invalidate();
      if (changed && ColorCommitted != null) ColorCommitted(value);
    }

    protected override void OnPaint(PaintEventArgs e) {
      e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
      if (colorButton == null)
        using (var brush = new SolidBrush(value)) e.Graphics.FillEllipse(brush, 2, 5, 18, 18);
      using (var pen = new Pen(ContainsFocus ? GraphiteAccent : GraphiteBorder))
        e.Graphics.DrawLine(pen, 30, 26, Math.Min(Width - 1, 108), 26);
    }
  }

  sealed class ColorPicker : Control {
    double hue = 215, saturation = .91, value = 1;
    bool pickingHue, dragging;
    public event EventHandler ColorChanged, ColorCommitted;
    Rectangle Field { get { return new Rectangle(5, 5, Math.Max(1, Width - 11), Math.Max(1, Height - 43)); } }
    Rectangle HueBar { get { return new Rectangle(5, Height - 24, Math.Max(1, Width - 11), 12); } }
    public Color Value {
      get {
        int r, g, b;
        HSV((int)Math.Round(hue), (int)Math.Round(saturation * 100), (int)Math.Round(value * 100), out r, out g, out b);
        return Color.FromArgb(r, g, b);
      }
    }

    public ColorPicker() {
      Height = 164;
      AccessibleName = "Color picker";
      AccessibleDescription = "Drag in the field or hue bar. Arrow keys adjust saturation and value. Page Up and Page Down adjust hue.";
      TabStop = true;
      Cursor = Cursors.Cross;
      SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void SetColor(Color color) {
      if (color.ToArgb() == Value.ToArgb()) return;
      int max = Math.Max(color.R, Math.Max(color.G, color.B));
      int min = Math.Min(color.R, Math.Min(color.G, color.B));
      if (max != min) hue = color.GetHue();
      saturation = max == 0 ? 0 : (max - min) / (double)max;
      value = max / 255.0;
      Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e) {
      var g = e.Graphics;
      g.SmoothingMode = SmoothingMode.AntiAlias;
      Rectangle field = Field, bar = HueBar;
      int r, green, b;
      HSV((int)hue, 100, 100, out r, out green, out b);
      using (var shape = RoundedPath(field, 6))
      using (var paint = new LinearGradientBrush(field, Color.White, Color.FromArgb(r, green, b), 0f))
      using (var shade = new LinearGradientBrush(field, Color.Transparent, Color.Black, 90f)) {
        g.FillPath(paint, shape);
        g.FillPath(shade, shape);
      }
      var colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red };
      using (var shape = RoundedPath(bar, 6))
      using (var paint = new LinearGradientBrush(bar, Color.Red, Color.Red, 0f)) {
        paint.InterpolationColors = new ColorBlend { Colors = colors, Positions = new[] { 0f, 1f/6, 2f/6, .5f, 4f/6, 5f/6, 1f } };
        g.FillPath(paint, shape);
      }
      DrawPickerCursor(g, field.Left + (float)(field.Width * saturation), field.Top + (float)(field.Height * (1 - value)));
      DrawPickerCursor(g, bar.Left + (float)(bar.Width * hue / 360), bar.Top + bar.Height / 2f);
      if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, ClientRectangle);
    }

    static void DrawPickerCursor(Graphics g, float x, float y) {
      g.DrawEllipse(Pens.Black, x - 6, y - 6, 12, 12);
      using (var outline = new Pen(Color.White, 1.5f)) g.DrawEllipse(outline, x - 5, y - 5, 10, 10);
    }

    void Pick(Point point) {
      if (pickingHue) hue = Limit(point.X - HueBar.X, 0, HueBar.Width) * 359.99 / HueBar.Width;
      else {
        saturation = Limit(point.X - Field.X, 0, Field.Width) / (double)Field.Width;
        value = 1 - Limit(point.Y - Field.Y, 0, Field.Height) / (double)Field.Height;
      }
      Invalidate();
      if (ColorChanged != null) ColorChanged(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e) {
      base.OnMouseDown(e);
      if (e.Button != MouseButtons.Left) return;
      Focus();
      pickingHue = e.Y >= HueBar.Top - 6;
      dragging = Capture = true;
      Pick(e.Location);
    }
    protected override void OnMouseMove(MouseEventArgs e) {
      base.OnMouseMove(e);
      if (dragging && Capture) Pick(e.Location);
    }
    protected override void OnMouseCaptureChanged(EventArgs e) {
      base.OnMouseCaptureChanged(e);
      if (!dragging || Capture) return;
      dragging = false;
      if (ColorCommitted != null) ColorCommitted(this, EventArgs.Empty);
    }
    protected override void OnMouseUp(MouseEventArgs e) {
      base.OnMouseUp(e);
      if (dragging && e.Button == MouseButtons.Left) Capture = false;
    }
    protected override bool IsInputKey(Keys key) {
      switch (key & Keys.KeyCode) {
        case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: return true;
        default: return base.IsInputKey(key);
      }
    }
    protected override void OnKeyDown(KeyEventArgs e) {
      base.OnKeyDown(e);
      switch (e.KeyCode) {
        case Keys.Left: saturation = Math.Max(0, saturation - .025); break;
        case Keys.Right: saturation = Math.Min(1, saturation + .025); break;
        case Keys.Up: value = Math.Min(1, value + .025); break;
        case Keys.Down: value = Math.Max(0, value - .025); break;
        case Keys.PageUp: hue = (hue + 5) % 360; break;
        case Keys.PageDown: hue = (hue + 355) % 360; break;
        default: return;
      }
      e.Handled = true;
      Invalidate();
      if (ColorChanged != null) ColorChanged(this, EventArgs.Empty);
      if (ColorCommitted != null) ColorCommitted(this, EventArgs.Empty);
    }
  }

  sealed class CustomEffectEditor : UserControl {
    readonly GradientEditor gradient;
    readonly Control gradientHeading, speedGroup;
    readonly HexField solidHex;
    readonly Button add;
    readonly Label headingLabel, feedback, speedValue, intensityValue;
    readonly TextBox nameField;
    readonly TrackBar speed, intensity;
    readonly List<EffectTab> tabs = new List<EffectTab>();
    readonly Stopwatch clock = new Stopwatch();
    CustomEffectKind kind;
    Color solid;
    CustomEffect preview;
    Guid editing;
    bool loading, open;

    public CustomEffectEditor(Action back) {
      AutoSize = true;
      AutoSizeMode = AutoSizeMode.GrowAndShrink;
      Dock = DockStyle.Top;
      var layout = GraphiteGrid(1);
      layout.Dock = DockStyle.Top;
      Controls.Add(layout);
      var backButton = TextAction("‹  Lighting", WorkspaceMuted);
      backButton.AccessibleName = "Back to Lighting";
      GraphiteAdd(layout, backButton);
      backButton.Dock = DockStyle.Left;
      backButton.Click += delegate { back(); };
      headingLabel = GraphiteText("New custom effect", 22, Color.White);
      GraphiteAdd(layout, headingLabel);

      var nameRow = StudioColumns(22, 78);
      var nameLabel = GraphiteText("Name", 10, WorkspaceText);
      nameLabel.TextAlign = ContentAlignment.MiddleLeft;
      GraphiteAdd(nameRow, nameLabel, 0, 0);
      nameLabel.Dock = DockStyle.Fill;
      nameField = new TextBox {
        BackColor = GraphitePanel, ForeColor = WorkspaceText, BorderStyle = BorderStyle.FixedSingle,
        AccessibleName = "Effect name", MaxLength = MaxEffectNameLength
      };
      GraphiteAdd(nameRow, nameField, 1, 0);
      GraphiteAdd(layout, nameRow);

      var rail = GraphiteGrid(4, 22);
      rail.Padding = new Padding(4);
      foreach (CustomEffectKind option in Enum.GetValues(typeof(CustomEffectKind))) {
        var tab = new EffectTab(option.ToString());
        tab.Click += delegate { SetMode(option); };
        GraphiteAdd(rail, tab, tabs.Count, 0);
        tabs.Add(tab);
      }
      GraphiteAdd(layout, rail);
      WorkspaceGap(layout, 6);

      var heading = GraphiteGrid(2);
      gradientHeading = heading;
      var title = GraphiteText("Light bar", 12, WorkspaceText);
      title.TextAlign = ContentAlignment.MiddleLeft;
      GraphiteAdd(heading, title, 0, 0);
      title.Dock = DockStyle.Fill;
      add = GraphiteButton("+ Add marker");
      add.Width = 116;
      add.Height = 31;
      GraphiteAdd(heading, add, 1, 0);
      add.Dock = DockStyle.Right;
      GraphiteAdd(layout, heading);
      gradient = new GradientEditor();
      GraphiteAdd(layout, gradient);
      GraphiteAdd(layout, gradient.MarkerList);
      solidHex = new HexField("Effect color hex", true);
      GraphiteAdd(layout, solidHex);
      WorkspaceGap(layout, 4);

      var speedLayout = GraphiteGrid(1);
      speedGroup = speedLayout;
      speed = AddSlider(speedLayout, "Speed", 1, 100, out speedValue, delegate { RefreshPreview(); });
      GraphiteAdd(layout, speedLayout);
      intensity = AddSlider(layout, "Brightness", 0, 100, out intensityValue, delegate { RefreshPreview(); });
      WorkspaceGap(layout, 8);
      feedback = GraphiteText("", 9, Color.FromArgb(255, 165, 177));
      GraphiteAdd(layout, feedback);
      var actions = Inline(FlowDirection.RightToLeft);
      var apply = GraphiteButton("Save effect");
      apply.Size = new Size(122, 34);
      UsePrimaryButton(apply);
      var cancel = TextAction("Cancel", WorkspaceMuted);
      cancel.AutoSize = false;
      cancel.Width = 72;
      actions.Controls.Add(apply);
      actions.Controls.Add(cancel);
      GraphiteAdd(layout, actions);
      add.Click += delegate { gradient.AddStop(); };
      solidHex.ColorCommitted += delegate(Color color) { solid = color; RefreshPreview(); };
      gradient.Changed += delegate {
        add.Enabled = gradient.Count < NUM_LED;
        RefreshPreview();
      };
      cancel.Click += delegate { back(); };
      apply.Click += delegate {
        if (kind == CustomEffectKind.Wave) gradient.CommitColors();
        if (kind == CustomEffectKind.Breathing || kind == CustomEffectKind.Static) solidHex.Commit();
        string error = SaveCustomEffect(editing, nameField.Text, preview, intensity.Value);
        if (error != null) {
          feedback.Text = error;
          feedback.Visible = true;
          nameField.Focus();
          return;
        }
        CheckGroup(briItems, bri.ToString());
        SyncDash();
        back();
      };
    }

    public void Begin(SavedEffect entry) {
      CustomEffect source = entry == null ? CustomEffect.Default(null) : entry.Effect;
      int brightness = entry == null ? bri : entry.Brightness;
      loading = true;
      editing = entry == null ? Guid.Empty : entry.Id;
      headingLabel.Text = entry == null ? "New custom effect" : "Edit custom effect";
      nameField.Text = entry == null ? "" : entry.Name;
      feedback.Visible = false;
      gradient.SetStops(source.Stops);
      solid = source.SolidColor;
      solidHex.SetColor(solid);
      speed.Value = source.Speed;
      intensity.Value = Limit(brightness, 0, 100);
      loading = false;
      open = true;
      SetMode(source.Kind);
    }

    public void FocusName() { nameField.Focus(); nameField.SelectAll(); }

    public void End() { open = false; clock.Stop(); }

    public byte[] PreviewFrame() {
      return open && preview != null ? RenderCustomEffect(preview, clock.Elapsed.TotalMilliseconds, intensity.Value) : null;
    }

    void SetMode(CustomEffectKind option) {
      kind = option;
      foreach (EffectTab tab in tabs) tab.Selected = tab.Text == kind.ToString();
      gradientHeading.Visible = gradient.Visible = gradient.MarkerList.Visible = kind == CustomEffectKind.Wave;
      solidHex.Visible = kind == CustomEffectKind.Breathing || kind == CustomEffectKind.Static;
      speedGroup.Visible = kind != CustomEffectKind.Static;
      RefreshPreview();
    }

    void RefreshPreview() {
      if (loading || gradient.Count == 0) return;
      speedValue.Text = speed.Value == 50 ? "Normal" : speed.Value.ToString() + "%";
      intensityValue.Text = intensity.Value.ToString() + "%";
      preview = new CustomEffect(kind, speed.Value, gradient.Stops, solid);
      clock.Restart();
    }
  }

  sealed class EffectTab : RoundedButton {
    public bool Selected {
      set {
        Quiet = !value;
        FillColor = PrimaryButtonFill;
        ForeColor = value ? Color.White : WorkspaceMuted;
        AccessibleDescription = value ? "Selected effect" : null;
        Invalidate();
      }
    }

    public EffectTab(string name) {
      Text = name;
      Height = 34;
      Margin = new Padding(1, 0, 1, 0);
      Cursor = Cursors.Hand;
      FlatStyle = FlatStyle.Flat;
      FlatAppearance.BorderSize = 0;
    }
  }

  sealed class GradientEditor : Control {
    readonly List<MarkerButton> markers = new List<MarkerButton>();
    MarkerButton selected;
    public event EventHandler Changed;
    public readonly TableLayoutPanel MarkerList = GraphiteGrid(1);
    public int Count { get { return markers.Count; } }
    public ColorStop[] Stops { get { return markers.Select(marker => marker.Stop).OrderBy(stop => stop.Position).ToArray(); } }
    Rectangle Bar { get { return new Rectangle(17, 10, Math.Max(1, Width - 34), 12); } }

    public GradientEditor() {
      Height = 54;
      TabStop = false;
      AccessibleName = "Light bar gradient";
      MarkerList.AccessibleName = "Color markers";
      SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
      BackColor = GraphiteBackground;
    }

    public void SetStops(IEnumerable<ColorStop> stops) {
      MarkerList.SuspendLayout();
      try {
        var previous = markers.ToArray();
        markers.Clear();
        selected = null;
        foreach (var marker in previous) {
          marker.Row.Dispose();
          marker.Dispose();
        }
        foreach (var stop in stops) NewMarker(stop);
        SelectMarker(markers[markers.Count / 2]);
      } finally { MarkerList.ResumeLayout(); }
    }

    MarkerButton NewMarker(ColorStop stop) {
      var marker = new MarkerButton(this, stop);
      markers.Add(marker);
      Controls.Add(marker);
      MarkerList.Controls.Add(marker.Row, 0, markers.Count - 1);
      return marker;
    }

    public void CommitColors() {
      foreach (var marker in markers) marker.Row.Hex.Commit();
    }

    public void AddStop() {
      if (Count >= NUM_LED) return;
      ColorStop[] stops = Stops;
      int start = 0, size = 0, previous = 0;
      foreach (int position in stops.Select(stop => stop.Position).Concat(new[] { 100 })) {
        int gap = position - previous;
        if (gap > size) { start = previous; size = gap; }
        previous = position;
      }
      int next = start + size / 2;
      var marker = NewMarker(new ColorStop(next, SampleStops(stops, next)));
      SelectMarker(marker);
      marker.Focus();
    }

    void DeleteMarker(MarkerButton removed, bool fromList) {
      if (Count <= 1 || !markers.Contains(removed)) return;
      var next = markers.Where(marker => marker != removed)
        .OrderBy(marker => Math.Abs(marker.Stop.Position - removed.Stop.Position)).First();
      markers.Remove(removed);
      selected = null;
      removed.Row.Dispose();
      removed.Dispose();
      SelectMarker(next);
      if (fromList) next.Row.SelectNextControl(null, true, true, true, false);
      else next.Focus();
    }

    void SetMarkerColor(MarkerButton marker, Color color) {
      if (!markers.Contains(marker)) return;
      marker.Stop = new ColorStop(marker.Stop.Position, color);
      UpdateGradient();
    }

    void SelectMarker(MarkerButton marker) {
      if (selected == marker || !markers.Contains(marker)) return;
      selected = marker;
      marker.BringToFront();
      UpdateGradient();
    }

    void MoveMarker(MarkerButton marker, int x) {
      int position = Limit((int)Math.Round(100.0 * (x - Bar.Left) / Bar.Width), 0, 100);
      if (IsOccupied(position)) return;
      SetPosition(marker, position);
    }

    bool IsOccupied(int position) { return markers.Any(marker => marker.Stop.Position == position); }

    void NudgeMarker(MarkerButton marker, int direction) {
      int position = marker.Stop.Position + direction;
      while (position >= 0 && position <= 100 && IsOccupied(position)) position += direction;
      if (position >= 0 && position <= 100) SetPosition(marker, position);
    }

    void SetPosition(MarkerButton marker, int position) {
      marker.Stop = new ColorStop(position, marker.Stop.Color);
      UpdateGradient();
    }

    void UpdateGradient() {
      PlaceMarkers();
      MarkerList.SuspendLayout();
      try {
        int row = 0;
        foreach (var marker in markers.OrderBy(item => item.Stop.Position)) {
          if (MarkerList.GetRow(marker.Row) != row) MarkerList.SetRow(marker.Row, row);
          marker.Row.TabIndex = row++;
          marker.Row.UpdateMarker(marker.Stop, marker == selected, Count > 1);
        }
      } finally { MarkerList.ResumeLayout(); }
      Invalidate();
      if (Changed != null) Changed(this, EventArgs.Empty);
    }

    void PlaceMarkers() {
      int tab = 0;
      foreach (var marker in markers.OrderBy(item => item.Stop.Position)) {
        int center = Bar.Left + (int)Math.Round(Bar.Width * marker.Stop.Position / 100.0);
        marker.Location = new Point(center - marker.Width / 2, Bar.Bottom + 1);
        marker.TabIndex = tab++;
        marker.AccessibleName = "Color marker at " + marker.Stop.Position + "%";
        marker.AccessibleDescription = "#" + HexColor(marker.Stop.Color) + (marker == selected ? ", selected" : "") +
          ". Drag or use Left and Right to move.";
        marker.Invalidate();
      }
    }

    protected override void OnResize(EventArgs e) {
      base.OnResize(e);
      PlaceMarkers();
    }
    void PaintWell(Graphics graphics) {
      PaintSurface(graphics, Rectangle.Inflate(ClientRectangle, -1, -1), 21, GraphitePanel, SurfaceEdge);
    }

    protected override void OnPaint(PaintEventArgs e) {
      if (Count == 0) return;
      PaintWell(e.Graphics);
      PaintStops(e.Graphics, Bar, Stops, 5);
      using (var path = RoundedPath(Bar, 5))
      using (var sheen = new LinearGradientBrush(Bar, Color.FromArgb(40, Color.White), Color.Transparent, 90f))
        e.Graphics.FillPath(sheen, path);
    }

    public static void PaintStops(Graphics graphics, Rectangle bounds, ColorStop[] source, int radius) {
      var stops = source.ToList();
      if (stops[0].Position != 0) stops.Insert(0, new ColorStop(0, stops[0].Color));
      if (stops[stops.Count - 1].Position != 100) stops.Add(new ColorStop(100, stops[stops.Count - 1].Color));
      graphics.SmoothingMode = SmoothingMode.AntiAlias;
      using (var path = RoundedPath(bounds, radius))
      using (var brush = new LinearGradientBrush(bounds, Color.Black, Color.Black, 0f)) {
        brush.InterpolationColors = new ColorBlend {
          Colors = stops.Select(stop => stop.Color).ToArray(),
          Positions = stops.Select(stop => stop.Position / 100f).ToArray()
        };
        graphics.FillPath(brush, path);
      }
    }

    sealed class MarkerRow : TableLayoutPanel {
      readonly Label position;
      readonly RoundedButton remove;
      public readonly HexField Hex;

      public MarkerRow() {
        Height = 35;
        Margin = Padding.Empty;
        Dock = DockStyle.Top;
        ColumnCount = 3;
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        position = GraphiteText("", 10, WorkspaceMuted);
        position.Margin = new Padding(9, 0, 0, 1);
        position.Dock = DockStyle.Fill;
        position.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(position, 0, 0);

        Hex = new HexField("Marker color", true) { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty };
        Controls.Add(Hex, 1, 0);

        remove = TextAction("×", WorkspaceMuted);
        remove.AutoSize = false;
        remove.Padding = Padding.Empty;
        remove.Size = new Size(30, 30);
        remove.Anchor = AnchorStyles.None;
        remove.Font = new Font("Segoe UI Semibold", 13);
        remove.Click += delegate { if (Remove != null) Remove(); };
        Controls.Add(remove, 2, 0);
      }

      public event Action Remove;

      public void UpdateMarker(ColorStop stop, bool active, bool canRemove) {
        string percent = stop.Position + "%";
        position.Text = percent;
        position.ForeColor = active ? GraphiteAccent : WorkspaceMuted;
        Hex.SetMarkerName("marker at " + percent);
        Hex.SetColor(stop.Color);
        remove.AccessibleName = "Remove marker at " + percent;
        remove.Enabled = canRemove;
      }

      protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        using (var line = new Pen(SurfaceEdge))
          e.Graphics.DrawLine(line, 9, Height - 1, Width - 9, Height - 1);
      }
    }

    sealed class MarkerButton : Button {
      readonly GradientEditor owner;
      public readonly MarkerRow Row;
      public ColorStop Stop { get; set; }
      int dragOffset;
      bool hovered;

      public MarkerButton(GradientEditor owner, ColorStop stop) {
        this.owner = owner;
        Stop = stop;
        Row = new MarkerRow();
        Row.Hex.Enter += delegate { owner.SelectMarker(this); };
        Row.Hex.ColorCommitted += delegate(Color color) { owner.SetMarkerColor(this, color); };
        Row.Remove += delegate { owner.DeleteMarker(this, true); };
        Size = new Size(26, 28);
        Margin = Padding.Empty;
        Cursor = Cursors.SizeWE;
        FlatStyle = FlatStyle.Flat;
        BackColor = GraphiteBackground;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
      }

      protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
      protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }

      protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.Clear(GraphiteBackground);
        var background = g.Save();
        g.TranslateTransform(-Left, -Top);
        owner.PaintWell(g);
        g.Restore(background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int x = Width / 2;
        bool active = owner.selected == this || Focused;
        int radius = active || hovered ? 9 : 7;
        var stem = new Rectangle(x - 1, 0, 2, 8);
        var diamond = new[] { new Point(x, 12 - radius), new Point(x + radius, 12),
          new Point(x, 12 + radius), new Point(x - radius, 12) };
        using (var glow = new SolidBrush(Color.FromArgb(48, Stop.Color)))
        using (var connector = new SolidBrush(active ? WorkspaceText : GraphiteBorder))
        using (var edge = new Pen(active ? WorkspaceText : Color.FromArgb(121, 150, 183), active ? 1.7f : 1f))
        using (var fill = new SolidBrush(Stop.Color)) {
          if (active) g.FillEllipse(glow, x - radius - 3, 9 - radius, radius * 2 + 6, radius * 2 + 6);
          g.FillRectangle(connector, stem);
          g.FillPolygon(fill, diamond);
          g.DrawPolygon(edge, diamond);
        }
      }
      protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); owner.SelectMarker(this); }
      protected override void OnClick(EventArgs e) { base.OnClick(e); owner.SelectMarker(this); }
      protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        owner.SelectMarker(this);
        dragOffset = e.X - Width / 2;
        Capture = true;
      }
      protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (Capture && e.Button == MouseButtons.Left) owner.MoveMarker(this, Left + e.X - dragOffset);
      }
      protected override bool IsInputKey(Keys keyData) {
        var key = keyData & Keys.KeyCode;
        return key == Keys.Left || key == Keys.Right || base.IsInputKey(keyData);
      }
      protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        switch (e.KeyCode) {
          case Keys.Delete: owner.DeleteMarker(this, false); break;
          case Keys.Left: owner.NudgeMarker(this, -1); break;
          case Keys.Right: owner.NudgeMarker(this, 1); break;
          default: return;
        }
        e.Handled = true;
      }
    }
  }
}
}
