using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;
using WF = System.Windows.Forms;

namespace Myelin;

/// <summary>Absolute black / absolute white token sets plus the accent, swapped live.</summary>
public static class Look
{
    public static bool Light { get; private set; }

    public static bool IsLight(Settings s) => s.Theme == AppTheme.Light || (s.Theme == AppTheme.System && SystemLight());

    public static bool SystemLight()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    public static void Apply(Settings s)
    {
        Light = IsLight(s);
        var r = System.Windows.Application.Current.Resources;
        void B(string key, uint argb) => r[key] = Br(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        if (Light)
        {
            B("BgBrush", 0xFFFFFFFF); B("SurfaceBrush", 0xFFF5F5F7); B("Surface2Brush", 0xFFE8E8ED); B("SeparatorBrush", 0xFFE0E0E5);
            B("TextBrush", 0xFF1D1D1F); B("SecondaryBrush", 0xFF6E6E73); B("TertiaryBrush", 0xFFAEAEB2); B("TrackBrush", 0xFFE5E5EA);
            B("SegHostBrush", 0xFFEDEDF0); B("SegOnBrush", 0xFFFFFFFF); B("LightOffBrush", 0xFFD1D1D6);
        }
        else
        {
            B("BgBrush", 0xFF000000); B("SurfaceBrush", 0xFF1C1C1E); B("Surface2Brush", 0xFF2C2C2E); B("SeparatorBrush", 0xFF2E2E31);
            B("TextBrush", 0xFFF5F5F7); B("SecondaryBrush", 0xFF98989D); B("TertiaryBrush", 0xFF636366); B("TrackBrush", 0xFF3A3A3C);
            B("SegHostBrush", 0xFF1C1C1E); B("SegOnBrush", 0xFF48484A); B("LightOffBrush", 0xFF3A3A3C);
        }
        var c = Img.Wpf(s.Palette.NeonC);
        r["AccentColor"] = c;
        r["AccentBrush"] = Br(c);
        r["AccentSoftBrush"] = Br(Color.FromArgb(Light ? (byte)0x24 : (byte)0x30, c.R, c.G, c.B));
        r["OnAccentBrush"] = Br(Lum(c) > .45 ? Colors.Black : Colors.White);
        r["AccentInkBrush"] = Br(Ink(c, Light ? Colors.White : Colors.Black));
    }

    static SolidColorBrush Br(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    static double Lin(byte v) { double x = v / 255.0; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
    static double Lum(Color c) => .2126 * Lin(c.R) + .7152 * Lin(c.G) + .0722 * Lin(c.B);
    static double Contrast(Color a, Color b) { double x = Lum(a), y = Lum(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }

    /// <summary>The accent, nudged until it reads as text on the page background (≥4.5:1).</summary>
    static Color Ink(Color c, Color bg)
    {
        var toward = Lum(bg) > .5 ? Colors.Black : Colors.White;
        for (double t = 0; t <= .8; t += .05)
        {
            var m = Color.FromRgb((byte)(c.R + (toward.R - c.R) * t), (byte)(c.G + (toward.G - c.G) * t), (byte)(c.B + (toward.B - c.B) * t));
            if (Contrast(m, bg) >= 4.5) return m;
        }
        return toward;
    }
}

/// <summary>One-page configurator: live stage on the left, the options column on the right.</summary>
public partial class MainWindow : Window
{
    /// <summary>Set to your repository URL before publishing (shows the GitHub link).</summary>
    public const string RepoUrl = "https://github.com/Akash-sk-bio/myelin";
    const string InstaUrl = "https://www.instagram.com/akashsk.ai/";

    static readonly (string Name, string Hex)[] TintSet =
    {
        ("Cyan", "#22D3EE"), ("Blue", "#0A84FF"), ("Indigo", "#5E5CE6"), ("Purple", "#BF5AF2"), ("Pink", "#FF375F"),
        ("Coral", "#FF7A45"), ("Gold", "#FFC23D"), ("Green", "#30D158"), ("Graphite", "#8E8E93"),
    };
    static readonly (string Name, string Hex)[] HighlightSet =
    {
        ("Amber", "#FFB020"), ("Yellow", "#FFD60A"), ("White", "#FFFFFF"), ("Mint", "#63E6BE"), ("Rose", "#FF6FAE"), ("Sky", "#7DD3FC"),
    };
    static readonly (TrailStyle T, string Name)[] TrailSet =
    {
        (TrailStyle.Comet, "Comet"), (TrailStyle.Glide, "Glide"), (TrailStyle.Helix, "DNA helix"),
        (TrailStyle.Neural, "Neural"), (TrailStyle.Pulse, "Pulse"), (TrailStyle.Off, "None"),
    };
    static readonly (ClickStyle C, string Name)[] ClickSet =
    {
        (ClickStyle.Synapse, "Synapse"), (ClickStyle.Molecule, "Molecule"), (ClickStyle.Atom, "Atom"),
        (ClickStyle.Mitosis, "Mitosis"), (ClickStyle.Ripple, "Ripple"), (ClickStyle.Off, "None"),
    };
    static readonly string[] ShelfRoles = { "Arrow", "Hand", "IBeam", "Wait", "AppStarting", "Help", "Pin", "SizeAll" };

    readonly Host host;
    Settings S => host.S;
    readonly List<RadioButton> lookTiles = new();
    readonly List<Image> lookThumbs = new();
    readonly List<(RadioButton R, string Hex)> tintSwatches = new(), hlSwatches = new();
    RadioButton customTint;
    readonly List<(RadioButton R, TrailStyle T, Image Thumb)> trailTiles = new();
    readonly List<(RadioButton R, ClickStyle C)> leftChips = new(), rightChips = new();
    readonly DispatcherTimer previewDebounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly DispatcherTimer liveDebounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    bool syncing, busy, showAll, stageLight;
    int renderVersion;
    double dpi = 1;

    public MainWindow(Host host)
    {
        this.host = host;
        InitializeComponent();
        // Fit the screen: comfortable default, never taller or wider than the work area at any scale.
        var wa = SystemParameters.WorkArea;
        Width = Math.Min(1160, Math.Floor(wa.Width * .9));
        Height = Math.Min(740, Math.Floor(wa.Height * .9));
        Icon = Brand.WpfLogo(64);
        LogoImg.Source = Brand.WpfLogo(40);
        FootLogo.Source = Brand.WpfLogo(72);
        VersionText.Text = "Version " + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "3.0.0");
        GithubBtn.Visibility = string.IsNullOrEmpty(RepoUrl) ? Visibility.Collapsed : Visibility.Visible;

        Wire();
        Build();
        previewDebounce.Tick += (_, _) => { previewDebounce.Stop(); RenderPreviews(); };
        liveDebounce.Tick += (_, _) => { liveDebounce.Stop(); host.RequestLive(); };
        host.Status += (text, progress) => Dispatcher.BeginInvoke(() => ShowStatus(text, progress));

        SourceInitialized += (_, _) => { HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc); ApplyChrome(); };
        Loaded += (_, _) =>
        {
            dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            RenderStaticThumbs();
            OnThemeChanged();
            SyncFromSettings(reload: true);
        };
        StateChanged += (_, _) =>
        {
            ZoomBtn.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Zoom";
            Dispatcher.BeginInvoke(FixMaximized, DispatcherPriority.Loaded);
        };
        SizeChanged += (_, _) => { if (WindowState == WindowState.Maximized) FixMaximized(); };
        Body.SizeChanged += (_, _) => FitStage();
        Shelf.SizeChanged += (_, _) => FitStage();
        Closing += (_, e) =>
        {
            if (host.Quitting) return;
            e.Cancel = true;
            Hide();
            host.HintTray();
        };
    }

    // ───────── window chrome ─────────
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

    /// <summary>A maximized borderless window overhangs the work area; inset the content by exactly that overhang.</summary>
    void FixMaximized()
    {
        if (WindowState != WindowState.Maximized) { Root.Margin = new Thickness(0); return; }
        var h = new WindowInteropHelper(this).Handle;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (h == IntPtr.Zero || !GetWindowRect(h, out var wr) || !GetMonitorInfo(MonitorFromWindow(h, 2), ref mi)) { Root.Margin = new Thickness(7); return; }
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Root.Margin = new Thickness(Math.Max(0, mi.rcWork.Left - wr.Left) / s, Math.Max(0, mi.rcWork.Top - wr.Top) / s,
                                    Math.Max(0, wr.Right - mi.rcWork.Right) / s, Math.Max(0, wr.Bottom - mi.rcWork.Bottom) / s);
    }

    /// <summary>The stage grows with the window (within limits) and the pointer set stays right under it.</summary>
    void FitStage()
    {
        if (Body.ActualHeight <= 0) return;
        Stage.Height = Math.Clamp(Body.ActualHeight - Shelf.ActualHeight - 14, 280, 560);
    }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
    [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MINMAXINFO { public PT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }

    public void ApplyChrome()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        int dark = Look.Light ? 0 : 1, round = 2, border = Look.Light ? 0x00E5E0E0 : 0x00312E2E;
        DwmSetWindowAttribute(h, 20, ref dark, 4);   // immersive dark mode
        DwmSetWindowAttribute(h, 33, ref round, 4);  // rounded corners (Windows 11)
        DwmSetWindowAttribute(h, 34, ref border, 4); // hairline border colour
    }

    /// <summary>Borderless windows otherwise maximize over the taskbar and past the screen edges.</summary>
    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0024) // WM_GETMINMAXINFO
        {
            var mon = MonitorFromWindow(hwnd, 2);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.ptMaxPosition.X = mi.rcWork.Left - mi.rcMonitor.Left;
                mmi.ptMaxPosition.Y = mi.rcWork.Top - mi.rcMonitor.Top;
                mmi.ptMaxSize.X = mi.rcWork.Right - mi.rcWork.Left;
                mmi.ptMaxSize.Y = mi.rcWork.Bottom - mi.rcWork.Top;
                Marshal.StructureToPtr(mmi, lParam, true);
            }
        }
        return IntPtr.Zero;
    }

    // ───────── wiring ─────────
    void Wire()
    {
        CloseBtn.Click += (_, _) => Close();
        MinBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        ZoomBtn.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        ApplyBtn.Click += async (_, _) => await ApplyAsync();
        StageToggle.Click += (_, _) => SetStage(!stageLight);
        TabLook.Checked += (_, _) => ShowPanel(PanelLook);
        TabStyle.Checked += (_, _) => ShowPanel(PanelStyle);
        TabEffects.Checked += (_, _) => ShowPanel(PanelEffects);
        TabSettings.Checked += (_, _) => ShowPanel(PanelSettings);
        ShelfToggle.Click += (_, _) => { showAll = !showAll; ShelfToggle.Content = showAll ? "Show fewer" : "Show all 17 pointers"; RenderPreviews(); };

        MatClear.Checked += (_, _) => SetMaterial(CursorShape.Clear);
        MatTinted.Checked += (_, _) => SetMaterial(CursorShape.Tinted);
        MotFull.Checked += (_, _) => SetMotion(Motion.Full);
        MotSubtle.Checked += (_, _) => SetMotion(Motion.Subtle);
        MotStill.Checked += (_, _) => SetMotion(Motion.Off);

        FerroSwitch.Click += (_, _) => { S.Ferrocenes = FerroSwitch.IsChecked == true; FxChanged(); };
        DblSwitch.Click += (_, _) => { S.DoubleClickMitosis = DblSwitch.IsChecked == true; FxChanged(); };
        DragSwitch.Click += (_, _) => { S.DragTether = DragSwitch.IsChecked == true; FxChanged(); };
        IdleSwitch.Click += (_, _) => { S.IdleOrbit = IdleSwitch.IsChecked == true; FxChanged(); };
        EnergySlider.ValueChanged += (_, e) => { if (syncing) return; S.Palette.Energy = (float)(e.NewValue / 100); FxChanged(); };

        FxSwitch.Click += (_, _) => host.SetEffects(FxSwitch.IsChecked == true);
        LiveSwitch.Click += (_, _) =>
        {
            S.LiveApply = LiveSwitch.IsChecked == true;
            host.SettingsChanged();
            if (S.LiveApply) host.RequestLive();
            UpdateApplyState();
        };
        ThemeSystem.Checked += (_, _) => SetTheme(AppTheme.System);
        ThemeLight.Checked += (_, _) => SetTheme(AppTheme.Light);
        ThemeDark.Checked += (_, _) => SetTheme(AppTheme.Dark);
        StartupSwitch.Click += (_, _) => Host.SetStartup(StartupSwitch.IsChecked == true);
        FsSwitch.Click += (_, _) => { S.PauseFullscreen = FsSwitch.IsChecked == true; host.SettingsChanged(); };

        ExportBtn.Click += async (_, _) => await ExportAsync();
        RestoreBtn.Click += (_, _) => { ShowStatus(host.RestoreCursors(), -1); UpdateApplyState(); };
        ResetBtn.Click += (_, _) => host.ResetSettings();
        InstaBtn.Click += (_, _) => Open(InstaUrl);
        GithubBtn.Click += (_, _) => Open(RepoUrl);
    }

    static SolidColorBrush Br(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    System.Windows.Style St(string key) => (System.Windows.Style)FindResource(key);

    // ───────── build the options column ─────────
    void Build()
    {
        for (int i = 0; i < Palette.Presets.Length; i++)
        {
            int idx = i;
            var p = Palette.Presets[i];
            var thumb = new Image { Width = 46, Height = 46 };
            lookThumbs.Add(thumb);
            var art = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(14), Background = Br("#0B0B0C"), Child = thumb };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            text.Children.Add(new TextBlock { Text = p.Name, FontSize = 15.5, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("DisplayFont") });
            text.Children.Add(new TextBlock { Text = p.Tag, Style = St("Secondary"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) });
            var row = new DockPanel();
            DockPanel.SetDock(art, Dock.Left);
            row.Children.Add(art);
            row.Children.Add(text);
            var tile = new RadioButton { Style = St("Tile"), GroupName = "look", Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10), Content = row };
            tile.Click += (_, _) => { host.SetLook(idx); };
            lookTiles.Add(tile);
            Looks.Children.Add(tile);
        }

        foreach (var (name, hex) in TintSet)
        {
            var h = hex;
            var r = new RadioButton { Style = St("Swatch"), GroupName = "tint", Background = Br(hex), ToolTip = name };
            r.Click += (_, _) => SetTint(h);
            tintSwatches.Add((r, hex));
            Tints.Children.Add(r);
        }
        customTint = new RadioButton
        {
            Style = St("Swatch"), GroupName = "tint", ToolTip = "Any colour…",
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(255, 59, 48), 0), new GradientStop(Color.FromRgb(255, 204, 0), .3),
                new GradientStop(Color.FromRgb(52, 199, 89), .55), new GradientStop(Color.FromRgb(10, 132, 255), .8),
                new GradientStop(Color.FromRgb(191, 90, 242), 1)
            }, 45)
        };
        customTint.Click += (_, _) => PickTint();
        Tints.Children.Add(customTint);

        foreach (var (name, hex) in HighlightSet)
        {
            var h = hex;
            var r = new RadioButton { Style = St("Swatch"), GroupName = "hl", Background = Br(hex), ToolTip = name, Width = 28, Height = 28 };
            r.Click += (_, _) => SetHighlight(h);
            hlSwatches.Add((r, hex));
            Highlights.Children.Add(r);
        }

        foreach (var (t, name) in TrailSet)
        {
            var trail = t;
            var thumb = new Image { Stretch = Stretch.Uniform };
            var inset = new Border { Height = 44, CornerRadius = new CornerRadius(9), Background = Br("#0B0B0C"), ClipToBounds = true, Child = thumb };
            var stack = new StackPanel();
            stack.Children.Add(inset);
            stack.Children.Add(new TextBlock { Text = name, FontSize = 12.5, Margin = new Thickness(2, 8, 0, 2) });
            var tile = new RadioButton { Style = St("Tile"), GroupName = "trail", Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 8), Content = stack };
            tile.Click += (_, _) => { S.Trail = trail; MarkCustomFx(); FxChanged(); };
            trailTiles.Add((tile, t, thumb));
            TrailGrid.Children.Add(tile);
        }

        foreach (var (c, name) in ClickSet)
        {
            var cs = c;
            var l = new RadioButton { Style = St("Chip"), GroupName = "left", Content = name };
            l.Click += (_, _) => { S.LeftClick = cs; MarkCustomFx(); FxChanged(); };
            leftChips.Add((l, c)); LeftChips.Children.Add(l);
            var r = new RadioButton { Style = St("Chip"), GroupName = "right", Content = name };
            r.Click += (_, _) => { S.RightClick = cs; MarkCustomFx(); FxChanged(); };
            rightChips.Add((r, c)); RightChips.Children.Add(r);
        }
    }

    // ───────── sync ─────────
    public void SyncFromSettings(bool reload = false)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => SyncFromSettings(reload)); return; }
        syncing = true;
        try
        {
            var p = S.Palette;
            Look.Apply(S);
            ApplyChrome();
            for (int i = 0; i < lookTiles.Count; i++) lookTiles[i].IsChecked = i == S.PresetIndex;
            bool curated = false;
            foreach (var (r, hex) in tintSwatches) { bool on = string.Equals(hex, p.Neon, StringComparison.OrdinalIgnoreCase); r.IsChecked = on; curated |= on; }
            customTint.IsChecked = !curated;
            foreach (var (r, hex) in hlSwatches) r.IsChecked = string.Equals(hex, p.Accent, StringComparison.OrdinalIgnoreCase);
            MatClear.IsChecked = p.Shape == CursorShape.Clear;
            MatTinted.IsChecked = p.Shape != CursorShape.Clear;
            MotFull.IsChecked = p.Motion == Motion.Full;
            MotSubtle.IsChecked = p.Motion == Motion.Subtle;
            MotStill.IsChecked = p.Motion == Motion.Off;
            foreach (var (r, t, _) in trailTiles) r.IsChecked = t == S.Trail;
            foreach (var (r, c) in leftChips) r.IsChecked = c == S.LeftClick;
            foreach (var (r, c) in rightChips) r.IsChecked = c == S.RightClick;
            FerroSwitch.IsChecked = S.Ferrocenes;
            FerroSwitch.IsEnabled = S.Trail == TrailStyle.Comet;
            DblSwitch.IsChecked = S.DoubleClickMitosis;
            DragSwitch.IsChecked = S.DragTether;
            IdleSwitch.IsChecked = S.IdleOrbit;
            EnergySlider.Value = p.Energy * 100;
            FxSwitch.IsChecked = S.EffectsOn;
            LiveSwitch.IsChecked = S.LiveApply;
            ThemeSystem.IsChecked = S.Theme == AppTheme.System;
            ThemeLight.IsChecked = S.Theme == AppTheme.Light;
            ThemeDark.IsChecked = S.Theme == AppTheme.Dark;
            StartupSwitch.IsChecked = Host.IsStartup();
            FsSwitch.IsChecked = S.PauseFullscreen;
        }
        finally { syncing = false; }
        UpdateStageText();
        ConfigureDemo();
        UpdateApplyState();
        if (reload) { previewDebounce.Stop(); previewDebounce.Start(); }
    }

    void UpdateStageText()
    {
        var p = S.Palette;
        StageTitle.Text = S.PresetIndex >= 0 ? p.Name : "Custom";
        var tint = TintSet.FirstOrDefault(t => string.Equals(t.Hex, p.Neon, StringComparison.OrdinalIgnoreCase)).Name ?? p.Neon.ToUpperInvariant();
        StageSub.Text = $"{(Look.Light ? "Solid" : p.Shape == CursorShape.Clear ? "Clear glass" : "Tinted glass")} · {tint} · {TrailSet.First(t => t.T == S.Trail).Name} trail";
    }

    void ConfigureDemo()
    {
        var c = S.Clone();
        c.IdleOrbit = false; c.DragTether = false; c.DoubleClickMitosis = false;
        StageDemo.Configure(c, 2, .8f);
    }

    public void UpdateApplyState()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(UpdateApplyState); return; }
        bool live = Scheme.IsActive() && S.AppliedKey == S.Effective(Look.Light).CacheKey;
        ApplyBtn.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        ApplyBtn.Content = Scheme.IsActive() ? "Apply changes" : "Use these pointers";
        if (busy) return;
        StatusDot.Fill = live ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("TertiaryBrush");
        StatusText.Text = live ? "Your pointer is live" : S.LiveApply && Scheme.IsActive() ? "Updating…" : "Preview";
    }

    void ShowStatus(string text, double progress)
    {
        StatusText.Text = text;
        busy = progress >= 0;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        if (busy) Progress.Value = progress * 100;
        if (!busy) UpdateApplyState();
    }

    // ───────── edits ─────────
    void MarkCustom()
    {
        if (S.PresetIndex < 0) return;
        S.PresetIndex = -1;
        S.Palette.Name = "Custom";
        S.Palette.Tag = "Your own look";
        foreach (var t in lookTiles) t.IsChecked = false;
    }

    void MarkCustomFx()
    {
        // effects are part of a look; changing them makes the look custom too
        if (S.PresetIndex >= 0 && Palette.PresetFx[S.PresetIndex] != (S.Trail, S.LeftClick, S.RightClick, S.Ferrocenes)) MarkCustom();
    }

    void SetTint(string hex)
    {
        var c = Palette.Hex(hex, SD.Color.Cyan);
        S.Palette.Neon = Palette.ToHex(c);
        S.Palette.Core = Palette.InkFor(c);
        MarkCustom();
        CursorsChanged();
    }

    void PickTint()
    {
        using var dlg = new WF.ColorDialog { FullOpen = true, AnyColor = true, Color = S.Palette.NeonC };
        if (dlg.ShowDialog() == WF.DialogResult.OK) SetTint(Palette.ToHex(dlg.Color));
        else SyncFromSettings();
    }

    void SetHighlight(string hex)
    {
        S.Palette.Accent = hex;
        MarkCustom();
        CursorsChanged();
    }

    void SetMaterial(CursorShape shape)
    {
        if (syncing || S.Palette.Shape == shape) return;
        S.Palette.Shape = shape;
        MarkCustom();
        CursorsChanged();
    }

    void SetMotion(Motion m)
    {
        if (syncing || S.Palette.Motion == m) return;
        S.Palette.Motion = m;
        MarkCustom();
        CursorsChanged();
    }

    void SetTheme(AppTheme m)
    {
        if (syncing || S.Theme == m) return;
        S.Theme = m;
        Look.Apply(S);
        host.SettingsChanged();
        OnThemeChanged();
    }

    /// <summary>Instant on the stage; the real pointer follows when Live updates is on.</summary>
    void CursorsChanged()
    {
        host.SettingsChanged();
        SyncFromSettings();
        previewDebounce.Stop(); previewDebounce.Start();
        if (S.LiveApply) { liveDebounce.Stop(); liveDebounce.Start(); }
    }

    void FxChanged()
    {
        host.SettingsChanged();
        FerroSwitch.IsEnabled = S.Trail == TrailStyle.Comet;
        UpdateStageText();
        ConfigureDemo();
    }

    public void OnLookChanged()
    {
        SyncFromSettings(reload: true);
        if (S.LiveApply) { liveDebounce.Stop(); liveDebounce.Start(); }
    }

    /// <summary>Light theme: flat UI and solid pointers. Dark theme: glass. The real pointer follows when Live is on.</summary>
    public void OnThemeChanged()
    {
        ApplyChrome();
        SetStage(Look.Light);
        MaterialNote.Visibility = Look.Light ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) RenderStaticThumbs();
        UpdateStageText();
        UpdateApplyState();
        previewDebounce.Stop(); previewDebounce.Start();
        if (S.LiveApply && Scheme.IsActive()) { liveDebounce.Stop(); liveDebounce.Start(); }
    }

    void ShowPanel(System.Windows.FrameworkElement panel)
    {
        foreach (var p in new System.Windows.FrameworkElement[] { PanelLook, PanelStyle, PanelEffects, PanelSettings })
            p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
        Options.ScrollToTop();
    }

    void SetStage(bool light)
    {
        stageLight = light;
        Stage.Background = Br(light ? "#F2F2F4" : "#0B0B0C");
        StageTitle.Foreground = Br(light ? "#1D1D1F" : "#F5F5F7");
        StageSub.Foreground = Br(light ? "#6E6E73" : "#8E8E93");

        StageToggle.Background = Br(light ? "#14000000" : "#26FFFFFF");
        StageToggle.Foreground = Br(light ? "#1D1D1F" : "#F5F5F7");

    }

    // ───────── rendering ─────────
    int Px(double logical) => (int)Math.Round(logical * dpi);

    void RenderStaticThumbs()
    {
        int look = Px(46), tw = Px(118), th = Px(54);
        bool light = Look.Light;
        var ui = TaskScheduler.FromCurrentSynchronizationContext();
        Task.Run(() =>
        {
            var looks = Palette.Presets.Select(p => { var q = p.Clone(); if (light) q.Shape = CursorShape.Solid; return Img.Still(new Art(q).All()[0], look); }).ToArray();
            var trails = TrailSet.Select(t => TrailThumb(t.T, tw, th)).ToArray();
            return (looks, trails);
        }).ContinueWith(t =>
        {
            if (t.IsFaulted) return;
            for (int i = 0; i < lookThumbs.Count; i++) lookThumbs[i].Source = t.Result.looks[i];
            for (int i = 0; i < trailTiles.Count; i++) trailTiles[i].Thumb.Source = t.Result.trails[i];
            var stage = Br(light ? "#FFFFFF" : "#0B0B0C");
            foreach (var img in lookThumbs.Concat(trailTiles.Select(x => x.Thumb))) if (img.Parent is Border bd) bd.Background = stage;
        }, ui);
    }

    /// <summary>A still frame of each trail, drawn by the real effects engine.</summary>
    static BitmapSource TrailThumb(TrailStyle trail, int w, int h)
    {
        using var bmp = new SD.Bitmap(w, h, SDI.PixelFormat.Format32bppPArgb);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            var s = new Settings { Palette = Palette.Presets[0].Clone(), Trail = trail, Ferrocenes = true, IdleOrbit = false };
            if (trail != TrailStyle.Off) Effects.Scene(s, g, w, h, h / 110f);
        }
        var d = bmp.LockBits(new SD.Rectangle(0, 0, w, h), SDI.ImageLockMode.ReadOnly, SDI.PixelFormat.Format32bppPArgb);
        try
        {
            var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, d.Scan0, d.Stride * h, d.Stride);
            src.Freeze();
            return src;
        }
        finally { bmp.UnlockBits(d); }
    }

    void RenderPreviews()
    {
        int v = ++renderVersion;
        var p = S.Effective(Look.Light);
        int px = Px(46), sp = Px(26);
        bool all = showAll;
        var ui = TaskScheduler.FromCurrentSynchronizationContext();
        Task.Run(() =>
        {
            var defs = new Art(p).All();
            var roles = all ? defs.Select(d => d.Role).ToArray() : ShelfRoles;
            var shelf = roles.Select(r => Img.Frames(defs.First(d => d.Role == r), p.Motion, px)).ToArray();
            var arrow = defs[0];
            var pointer = Neon.ToBitmap(Neon.Render(arrow.Frames(Motion.Off)[0].Make(), sp), sp);
            var names = roles.Select(r => defs.First(d => d.Role == r).Label).ToArray();
            return (shelf, names, pointer, hx: arrow.Hx * sp / 32f, hy: arrow.Hy * sp / 32f);
        }).ContinueWith(t =>
        {
            if (t.IsFaulted || v != renderVersion) return;
            var r = t.Result;
            ShelfGrid.Columns = all ? 9 : 8;
            ShelfGrid.Children.Clear();
            for (int i = 0; i < r.shelf.Length; i++)
            {
                var img = new AnimImage { Width = 46, Height = 46, Margin = new Thickness(4), ToolTip = r.names[i] };
                img.SetFrames(r.shelf[i].Frames, r.shelf[i].Jif);
                ShelfGrid.Children.Add(img);
            }
            var old = FxDemo.Pointer;
            FxDemo.Pointer = r.pointer; FxDemo.PointerHx = r.hx; FxDemo.PointerHy = r.hy;
            old?.Dispose();
        }, ui);
    }

    // ───────── actions ─────────
    async Task ApplyAsync()
    {
        if (busy) return;
        ShowStatus("Preparing your pointers…", 0);
        var msg = await host.ApplyCursorsAsync(f => Dispatcher.BeginInvoke(() => { if (busy) Progress.Value = f * 100; }));
        ShowStatus(msg, -1);
        UpdateApplyState();
    }

    async Task ExportAsync()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Export a portable Myelin pointer pack" };
        if (dlg.ShowDialog(this) != true) return;
        var p = S.Effective(Look.Light);
        ShowStatus("Exporting…", 0);
        try { ShowStatus("Exported to " + await Task.Run(() => Scheme.Export(p, dlg.FolderName)), -1); }
        catch (Exception ex) { ShowStatus("Export failed: " + ex.Message, -1); }
    }

    static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* nothing to open */ }
    }

    // ───────── docs screenshots (--snap) ─────────
    public async Task SnapAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
        {
            S.Theme = theme;
            SyncFromSettings();
            OnThemeChanged();
            Options.ScrollToTop();
            await Task.Delay(1600);
            Capture(Path.Combine(dir, $"app-{theme.ToString().ToLowerInvariant()}.png"));
            TabEffects.IsChecked = true;
            await Task.Delay(900);
            Capture(Path.Combine(dir, $"app-{theme.ToString().ToLowerInvariant()}-effects.png"));
            TabLook.IsChecked = true;
        }
    }

    void Capture(string path)
    {
        UpdateLayout();
        int w = (int)(Root.ActualWidth * dpi), h = (int)(Root.ActualHeight * dpi);
        var rtb = new RenderTargetBitmap(w, h, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        rtb.Render(Root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
