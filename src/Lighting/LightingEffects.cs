using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RazerPadFan {
internal static partial class Program {
  enum CustomEffectKind { Wave, Breathing, Spectrum, Static }

  struct ColorStop {
    public readonly int Position;
    public readonly Color Color;

    public ColorStop(int position, Color color) {
      Position = position;
      Color = color;
    }
  }

  // Published under cfgLock. The editor keeps its own draft.
  sealed class CustomEffect {
    public readonly CustomEffectKind Kind;
    public readonly int Speed;
    public readonly ColorStop[] Stops;
    public readonly Color SolidColor;

    public CustomEffect(CustomEffectKind kind, int speed, IEnumerable<ColorStop> stops, Color solidColor) {
      Kind = kind;
      Speed = Limit(speed, 1, 100);
      Stops = stops.OrderBy(stop => stop.Position).ToArray();
      SolidColor = solidColor;
    }

    public static CustomEffect Default(Color? legacyColor) {
      Color color = legacyColor ?? Color.FromArgb(22, 119, 255);
      return new CustomEffect(CustomEffectKind.Wave, 50, new[] {
        new ColorStop(0, color),
        new ColorStop(50, legacyColor ?? Color.FromArgb(0, 214, 255)),
        new ColorStop(100, legacyColor ?? Color.FromArgb(123, 55, 255))
      }, color);
    }

    public string Serialize() {
      return Kind + ";" + Speed.ToString(CultureInfo.InvariantCulture) + ";" + HexColor(SolidColor) + ";" +
        string.Join(",", Stops.Select(stop => stop.Position.ToString(CultureInfo.InvariantCulture) + ":" + HexColor(stop.Color)));
    }

    public static CustomEffect Parse(string value) {
      if (string.IsNullOrEmpty(value)) return null;
      string[] parts = value.Split(';');
      CustomEffectKind kind;
      int speed;
      Color color;
      if (parts.Length != 4 || !Enum.TryParse(parts[0], true, out kind) ||
          !Enum.IsDefined(typeof(CustomEffectKind), kind) || !int.TryParse(parts[1], out speed) ||
          !TryColor(parts[2], out color)) return null;
      var stops = new List<ColorStop>();
      var positions = new HashSet<int>();
      foreach (string entry in parts[3].Split(',')) {
        string[] pair = entry.Split(':');
        int position;
        Color stopColor;
        if (pair.Length != 2 || !int.TryParse(pair[0], out position) || position < 0 || position > 100 ||
            !positions.Add(position) || !TryColor(pair[1], out stopColor)) return null;
        stops.Add(new ColorStop(position, stopColor));
      }
      if (stops.Count == 0 || stops.Count > NUM_LED) return null;
      return new CustomEffect(kind, speed, stops, color);
    }
  }

  const int MaxCustomEffects = 5;
  const int MaxEffectNameLength = 40;
  static SavedEffect[] savedEffects = new SavedEffect[0];
  static Guid selectedCustomId;

  sealed class SavedEffect {
    public readonly Guid Id;
    public readonly string Name;
    public readonly CustomEffect Effect;
    public readonly int Brightness;

    public SavedEffect(Guid id, string name, CustomEffect effect, int brightness) {
      Id = id;
      Name = name;
      Effect = effect;
      Brightness = Limit(brightness, 0, 100);
    }

    public string Serialize() {
      return Id.ToString("N") + "|" + Brightness.ToString(CultureInfo.InvariantCulture) + "|" +
        Convert.ToBase64String(Encoding.UTF8.GetBytes(Name)) + "|" + Effect.Serialize();
    }

    public static SavedEffect Parse(string value) {
      string[] parts = value.Split(new[] { '|' }, 4);
      Guid id;
      int brightness;
      if (parts.Length != 4 || !Guid.TryParseExact(parts[0], "N", out id) || id == Guid.Empty ||
          !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out brightness)) return null;
      string name;
      try { name = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])).Trim(); }
      catch (FormatException) { return null; }
      CustomEffect effect = CustomEffect.Parse(parts[3]);
      return ValidEffectName(name) && effect != null ? new SavedEffect(id, name, effect, brightness) : null;
    }
  }

  static bool ValidEffectName(string name) {
    return name.Length > 0 && name.Length <= MaxEffectNameLength && !name.Any(char.IsControl);
  }

  static SavedEffect FindSavedEffect(Guid id) {
    return Array.Find(savedEffects, effect => effect.Id == id);
  }

  // These snapshots are protected by cfgLock and published together with the active mode.
  static void ReadEffectLibrary(Dictionary<string, string> config) {
    var library = new List<SavedEffect>();
    string value;
    selectedCustomId = Guid.Empty;
    if (config.ContainsKey("selectedcustom")) {
      for (int i = 1; i <= MaxCustomEffects; i++) {
        if (!config.TryGetValue("customslot." + i, out value)) continue;
        SavedEffect entry = SavedEffect.Parse(value);
        if (entry != null && !library.Any(item => item.Id == entry.Id ||
            string.Equals(item.Name, entry.Name, StringComparison.OrdinalIgnoreCase))) library.Add(entry);
      }
      Guid.TryParse(config["selectedcustom"], out selectedCustomId);
    } else {
      CustomEffect legacy = config.TryGetValue("customeffect", out value) ? CustomEffect.Parse(value) : null;
      Color tint;
      if (legacy == null && led == "custom")
        legacy = CustomEffect.Default(TryColor(EffectColor("custom"), out tint) ? (Color?)tint : null);
      if (legacy != null) {
        // A stable migration ID lets an open editor survive an external config reload.
        selectedCustomId = new Guid("d1953df8-a8db-43ec-aec4-c998f3c55467");
        library.Add(new SavedEffect(selectedCustomId, "Custom effect", legacy, bri));
      }
    }
    savedEffects = library.ToArray();
    if (FindSavedEffect(selectedCustomId) == null) {
      selectedCustomId = Guid.Empty;
      if (led == "custom") led = "rainbow";
    }
  }

  static void WriteEffectLibrary(List<string> lines) {
    lines.Add("selectedcustom=" + selectedCustomId.ToString("N"));
    for (int i = 0; i < savedEffects.Length; i++) lines.Add("customslot." + (i + 1) + "=" + savedEffects[i].Serialize());
  }

  static bool SaveLightingChange(Action update) {
    lock (cfgLock) {
      string previousMode = led, previousColor = ledColor;
      int previousBrightness = bri;
      Guid previousSelection = selectedCustomId;
      SavedEffect[] previousEffects = savedEffects;
      string staticTint;
      bool hadStaticTint = effectColors.TryGetValue("static", out staticTint);
      update();
      if (SaveConfig()) return true;
      led = previousMode;
      ledColor = previousColor;
      bri = previousBrightness;
      selectedCustomId = previousSelection;
      savedEffects = previousEffects;
      if (hadStaticTint) effectColors["static"] = staticTint;
      else effectColors.Remove("static");
      return false;
    }
  }

  static string SaveCustomEffect(Guid id, string name, CustomEffect effect, int brightness) {
    name = name.Trim();
    if (!ValidEffectName(name)) return "Enter an effect name of up to " + MaxEffectNameLength + " characters.";
    lock (cfgLock) {
      int index = Array.FindIndex(savedEffects, item => item.Id == id);
      if (id == Guid.Empty && savedEffects.Length >= MaxCustomEffects) return "Delete a custom effect to make room.";
      if (id != Guid.Empty && index < 0) return "This effect was removed. Return to Lighting to create a new one.";
      if (savedEffects.Any(item => item.Id != id && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        return "An effect with this name already exists.";
      var saved = new SavedEffect(id == Guid.Empty ? Guid.NewGuid() : id, name, effect, brightness);
      if (!SaveLightingChange(delegate {
        if (index < 0) savedEffects = savedEffects.Concat(new[] { saved }).ToArray();
        else {
          savedEffects = (SavedEffect[])savedEffects.Clone();
          savedEffects[index] = saved;
          if (led == "custom" && selectedCustomId == id) bri = saved.Brightness;
        }
      })) return "Could not write the settings file. Try again.";
    }
    return null;
  }

  static bool ActivateSavedEffect(Guid id) {
    lock (cfgLock) {
      SavedEffect target = FindSavedEffect(id);
      return target != null && SaveLightingChange(delegate {
        selectedCustomId = id;
        led = "custom";
        bri = target.Brightness;
      });
    }
  }

  static bool DeleteSavedEffect(Guid id) {
    lock (cfgLock) {
      if (FindSavedEffect(id) == null) return false;
      return SaveLightingChange(delegate {
        savedEffects = savedEffects.Where(item => item.Id != id).ToArray();
        if (selectedCustomId != id) return;
        selectedCustomId = Guid.Empty;
        if (led == "custom") led = "rainbow";
      });
    }
  }

  static void SetLightingBrightness(int value) {
    lock (cfgLock) {
      bri = value;
      if (led != "custom") return;
      int index = Array.FindIndex(savedEffects, item => item.Id == selectedCustomId);
      if (index < 0 || savedEffects[index].Brightness == value) return;
      SavedEffect entry = savedEffects[index];
      savedEffects = (SavedEffect[])savedEffects.Clone();
      savedEffects[index] = new SavedEffect(entry.Id, entry.Name, entry.Effect, value);
    }
  }

  static int Limit(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }

  static bool TryColor(string text, out Color color) {
    color = Color.Empty;
    int rgb;
    string hex = (text ?? "").Trim().TrimStart('#');
    if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb))
      return false;
    color = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    return true;
  }

  static string HexColor(Color color) { return (color.ToArgb() & 0xffffff).ToString("X6", CultureInfo.InvariantCulture); }

  static Color SampleStops(ColorStop[] stops, double position) {
    for (int i = 1; i < stops.Length; i++) {
      if (position > stops[i].Position) continue;
      ColorStop left = stops[i - 1], right = stops[i];
      double mix = Math.Max(0, (position - left.Position) / (right.Position - left.Position));
      return Color.FromArgb(
        (int)Math.Round(left.Color.R + (right.Color.R - left.Color.R) * mix),
        (int)Math.Round(left.Color.G + (right.Color.G - left.Color.G) * mix),
        (int)Math.Round(left.Color.B + (right.Color.B - left.Color.B) * mix));
    }
    return stops[stops.Length - 1].Color;
  }

  static byte[] RenderCustomEffect(CustomEffect effect, double milliseconds, int brightness) {
    double rate = Math.Pow(4, (effect.Speed - 50) / 50.0);
    double phase = milliseconds * rate / 4500.0;
    switch (effect.Kind) {
      case CustomEffectKind.Spectrum:
        return RainbowFrame(phase * 360 % 360, brightness, true);
      case CustomEffectKind.Breathing:
        brightness = (int)Math.Round(brightness * (.12 + .88 * (.5 - .5 * Math.Cos(phase * Math.PI * 2))));
        goto case CustomEffectKind.Static;
      case CustomEffectKind.Static:
        return SolidFrame(Bake(effect.SolidColor.R, brightness), Bake(effect.SolidColor.G, brightness),
                          Bake(effect.SolidColor.B, brightness));
      default:
        var frame = new byte[NUM_LED * 3];
        for (int i = 0; i < NUM_LED; i++) {
          // Mirror the gradient at each end to avoid an abrupt color seam.
          double offset = (i / (NUM_LED - 1.0) - phase) % 2;
          if (offset < 0) offset += 2;
          Color color = SampleStops(effect.Stops, 100 * (offset > 1 ? 2 - offset : offset));
          frame[i * 3] = Bake(color.R, brightness);
          frame[i * 3 + 1] = Bake(color.G, brightness);
          frame[i * 3 + 2] = Bake(color.B, brightness);
        }
        return frame;
    }
  }
}
}
