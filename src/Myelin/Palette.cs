using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Myelin;

public enum Motion { Off, Subtle, Full }
/// <summary>Neon and Fluid are legacy (v1–v2) and migrate to Tinted; the app offers Liquid Glass: Clear or Tinted.</summary>
public enum CursorShape { Neon, Fluid, Clear, Tinted, Solid }
public enum AppTheme { System, Light, Dark }
public enum TrailStyle { Off, Comet, Helix, Neural, Pulse, Glide }
public enum ClickStyle { Off, Synapse, Molecule, Ripple, Atom, Mitosis }

/// <summary>Colour + material + motion recipe for one mood. Hex strings keep settings.json hand-editable.</summary>
public sealed class Palette
{
    public string Name { get; set; } = "Signature";
    public string Tag { get; set; } = "Everyday · creating";
    public string Neon { get; set; } = "#22D3EE";
    public string Accent { get; set; } = "#FFB020";
    public string Core { get; set; } = "#0B1222";
    public string Alert { get; set; } = "#FF4057";
    public float Glow { get; set; } = 0.6f;
    public Motion Motion { get; set; } = Motion.Subtle;
    public CursorShape Shape { get; set; } = CursorShape.Tinted;
    public float Energy { get; set; } = 1f;

    public Palette Clone() => (Palette)MemberwiseClone();

    [JsonIgnore] public Color NeonC => Hex(Neon, Color.Cyan);
    [JsonIgnore] public Color AccentC => Hex(Accent, Color.Orange);
    [JsonIgnore] public Color CoreC => Hex(Core, Color.Black);
    [JsonIgnore] public Color AlertC => Hex(Alert, Color.Red);

    /// <summary>Everything that changes the rendered cursor files (effects-only values excluded).</summary>
    [JsonIgnore]
    public string CacheKey => $"{Neon}|{Accent}|{Core}|{Alert}|{Glow:0.00}|{Motion}|{Shape}|v{Pack.Version}".ToUpperInvariant();

    public static Color Hex(string s, Color fallback)
    {
        try
        {
            s = (s ?? "").Trim();
            if (!s.StartsWith('#')) s = "#" + s;
            return s.Length == 7 ? ColorTranslator.FromHtml(s) : fallback;
        }
        catch { return fallback; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    static Palette P(string name, string tag, string tint, string accent, string core, string alert,
                     float glow, Motion motion, float energy, CursorShape shape = CursorShape.Tinted) => new()
    {
        Name = name, Tag = tag, Neon = tint, Accent = accent, Core = core, Alert = alert,
        Glow = glow, Motion = motion, Energy = energy, Shape = shape
    };

    // Three premium looks. Each bundles a glass, a colour and an effects set; everything else is customization.
    public static readonly Palette[] Presets =
    {
        P("Signature", "Cyan glass · comet trail",  "#22D3EE", "#FFB020", "#06131A", "#FF4057", .6f,  Motion.Subtle, 1.00f),
        P("Crystal",   "Clear glass · agent glide", "#5AC8FA", "#FFD60A", "#0A0F18", "#FF453A", .45f, Motion.Subtle, 0.80f, CursorShape.Clear),
        P("Ember",     "Warm glass · DNA helix",    "#FF7A45", "#FFD166", "#1A0D06", "#FF4057", .65f, Motion.Full,   1.15f),
    };

    public static readonly (TrailStyle Trail, ClickStyle Left, ClickStyle Right, bool Ferrocenes)[] PresetFx =
    {
        (TrailStyle.Comet, ClickStyle.Synapse, ClickStyle.Molecule, true),
        (TrailStyle.Glide, ClickStyle.Ripple,  ClickStyle.Atom,     false),
        (TrailStyle.Helix, ClickStyle.Mitosis, ClickStyle.Atom,     false),
    };

    /// <summary>Very dark ink derived from the tint (used for glyph details).</summary>
    public static string InkFor(Color tint) => ToHex(Color.FromArgb(tint.R / 12 + 4, tint.G / 12 + 6, tint.B / 12 + 10));
}

public sealed class Settings
{
    public Palette Palette { get; set; } = Palette.Presets[0].Clone();
    public int PresetIndex { get; set; }
    public bool EffectsOn { get; set; } = true;
    public TrailStyle Trail { get; set; } = TrailStyle.Comet;
    public bool Ferrocenes { get; set; } = true;
    public ClickStyle LeftClick { get; set; } = ClickStyle.Synapse;
    public ClickStyle RightClick { get; set; } = ClickStyle.Molecule;
    public bool DoubleClickMitosis { get; set; } = true;
    public bool DragTether { get; set; } = true;
    public bool IdleOrbit { get; set; } = true;
    public bool PauseFullscreen { get; set; } = true;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool LiveApply { get; set; } = true;
    public string AppliedKey { get; set; } = "";

    public const string AppName = "Myelin";
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
    static string LegacyDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BioPulse");
    static string FilePath => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static Settings Load()
    {
        Migrate();
        var s = new Settings();
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Opt) ?? new Settings();
        }
        catch { /* corrupt file → defaults */ }
        if (s.Palette.Shape is CursorShape.Neon or CursorShape.Fluid) s.Palette.Shape = CursorShape.Tinted; // v1/v2 → Liquid Glass
        if (s.PresetIndex >= Palette.Presets.Length) s.PresetIndex = -1;
        return s;
    }

    /// <summary>One-time move of settings + the original-cursor backup from the BioPulse folder.</summary>
    static void Migrate()
    {
        try
        {
            if (Directory.Exists(Dir) || !Directory.Exists(LegacyDir)) return;
            Directory.CreateDirectory(Dir);
            foreach (var f in new[] { "settings.json", "cursor-backup.json" })
            {
                var src = Path.Combine(LegacyDir, f);
                if (File.Exists(src)) File.Copy(src, Path.Combine(Dir, f), true);
            }
        }
        catch { /* best effort */ }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opt));
        }
        catch { /* non-fatal */ }
    }

    public Settings Clone()
    {
        var s = (Settings)MemberwiseClone();
        s.Palette = Palette.Clone();
        return s;
    }

    /// <summary>What actually gets rendered: glass in the dark theme, solid (opaque) pointers in the light theme.</summary>
    public Palette Effective(bool lightTheme)
    {
        var p = Palette.Clone();
        if (lightTheme) p.Shape = CursorShape.Solid;
        return p;
    }

    /// <summary>Switch to one of the three premium looks (glass, colour and effects together).</summary>
    public void ApplyPreset(int i)
    {
        PresetIndex = i;
        Palette = Palette.Presets[i].Clone();
        (Trail, LeftClick, RightClick, Ferrocenes) = Palette.PresetFx[i];
    }

    /// <summary>Overwrite every user preference from another instance (keeps AppliedKey).</summary>
    public void CopyFrom(Settings o)
    {
        Palette = o.Palette.Clone(); PresetIndex = o.PresetIndex; EffectsOn = o.EffectsOn; Trail = o.Trail;
        Ferrocenes = o.Ferrocenes; LeftClick = o.LeftClick; RightClick = o.RightClick;
        DoubleClickMitosis = o.DoubleClickMitosis; DragTether = o.DragTether; IdleOrbit = o.IdleOrbit;
        PauseFullscreen = o.PauseFullscreen; Theme = o.Theme; LiveApply = o.LiveApply;
    }
}
