using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace Myelin;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        WF.Application.SetHighDpiMode(WF.HighDpiMode.PerMonitorV2);
        WF.Application.EnableVisualStyles();
        WF.Application.SetCompatibleTextRenderingDefault(false);

        string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        string arg = args.Length > 1 ? args[1] : null;
        switch (cmd)
        {
            case "--selftest": return SelfTest.Run(arg ?? "selftest");
            case "--brand": Brand.WriteAssets(arg ?? "assets"); return 0;
            case "--export": { var es = Settings.Load(); Console.WriteLine(Scheme.Export(es.Effective(Look.IsLight(es)), arg ?? "export")); } return 0;
            case "--apply":
            {
                var s = Settings.Load();
                var ep = s.Effective(Look.IsLight(s));
                Scheme.Apply(Pack.Ensure(ep), ep.Name);
                s.AppliedKey = ep.CacheKey; s.Save();
                return 0;
            }
            case "--restore": Scheme.Restore(); return 0;
            case "--snap":
            {
                var app = Host.CreateWpfApp();
                var host = new Host(showWindow: false, new Settings()); // stock look for docs; never touches user settings
                app.Dispatcher.InvokeAsync(async () => { await host.SnapAsync(arg ?? "snaps"); host.Quit(); });
                app.Run();
                return 0;
            }
        }

        if (Mutex.TryOpenExisting("BioPulse.CursorStudio.v1", out var legacy))
        {
            legacy.Dispose();
            WF.MessageBox.Show("The previous version (BioPulse) is still running.\n\nRight-click its icon in the tray, choose Exit, then open Myelin again.",
                "Myelin", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Information);
            return 0;
        }

        using var mutex = new Mutex(true, "Myelin.App", out bool first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting("Myelin.Show").Set(); } catch { }
            return 0;
        }
        var wpf = Host.CreateWpfApp();
        _ = new Host(showWindow: !args.Contains("--tray"));
        wpf.Run();
        return 0;
    }
}

/// <summary>Owns settings, the effects overlay, live pointer updates, tray, hotkeys and the window.</summary>
public sealed class Host
{
    public Settings S { get; }
    public readonly Effects Fx;
    public bool Quitting { get; private set; }
    /// <summary>Status line for the toolbar: text, and progress 0..1 (or -1 when idle).</summary>
    public event Action<string, double> Status;

    readonly bool readOnly;
    readonly Overlay overlay;
    readonly WF.NotifyIcon tray;
    readonly Hotkeys hotkeys;
    readonly EventWaitHandle showEvent = new(false, EventResetMode.AutoReset, "Myelin.Show");
    readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
    MainWindow window;
    Icon trayIcon;
    bool busy, hinted, liveRunning;
    int liveVersion;

    public static System.Windows.Application CreateWpfApp()
    {
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Myelin;component/Theme.xaml", UriKind.Absolute)
        });
        return app;
    }

    public Host(bool showWindow, Settings snapshot = null)
    {
        S = snapshot ?? Settings.Load();
        readOnly = snapshot != null;
        Look.Apply(S);
        Fx = new Effects(S);
        overlay = new Overlay(Fx);
        overlay.Show();
        overlay.Running = S.EffectsOn;

        tray = new WF.NotifyIcon { Text = "Myelin", Visible = true, ContextMenuStrip = new WF.ContextMenuStrip { ShowImageMargin = false } };
        UpdateTrayIcon();
        tray.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        tray.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) ShowWindow(); };
        BuildMenu();

        hotkeys = new Hotkeys(this);
        new Thread(() => { while (showEvent.WaitOne()) ui.InvokeAsync(ShowWindow); }) { IsBackground = true }.Start();
        SystemEvents.UserPreferenceChanged += OnSystemPreference;
        if (!readOnly) MigrateStartup();

        if (showWindow) ShowWindow();
        // Pointers from an older build (or a pending change) get refreshed in the background.
        if (!readOnly && S.LiveApply && (Scheme.LegacyActive() || (Scheme.IsActive() && S.AppliedKey != S.Effective(Look.Light).CacheKey))) RequestLive();
    }

    void OnSystemPreference(object sender, UserPreferenceChangedEventArgs e)
    {
        if (S.Theme != AppTheme.System || e.Category != UserPreferenceCategory.General) return;
        ui.InvokeAsync(() => { Look.Apply(S); BuildMenu(); UpdateTrayIcon(); if (window != null) window.OnThemeChanged(); else if (S.LiveApply && Scheme.IsActive()) RequestLive(); });
    }

    void UpdateTrayIcon()
    {
        using var bmp = Neon.ToBitmap(Neon.Render(new Art(S.Effective(Look.Light)).IconLayers(), 32), 32);
        var old = trayIcon;
        trayIcon = Icon.FromHandle(bmp.GetHicon());
        tray.Icon = trayIcon;
        if (old != null) { DestroyIcon(old.Handle); old.Dispose(); }
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    void BuildMenu()
    {
        var m = tray.ContextMenuStrip;
        bool light = Look.Light;
        m.Renderer = new WF.ToolStripProfessionalRenderer(new MenuColors(light));
        m.BackColor = light ? Color.FromArgb(246, 246, 248) : Color.FromArgb(28, 28, 30);
        m.ForeColor = light ? Color.FromArgb(29, 29, 31) : Color.FromArgb(245, 245, 247);
        m.Items.Clear();
        var open = new WF.ToolStripMenuItem("Open Myelin", null, (_, _) => ShowWindow());
        open.Font = new Font(m.Font, FontStyle.Bold);
        m.Items.Add(open);
        m.Items.Add(new WF.ToolStripSeparator());
        for (int i = 0; i < Palette.Presets.Length; i++)
        {
            int idx = i;
            m.Items.Add(new WF.ToolStripMenuItem((S.PresetIndex == i ? "✓  " : "     ") + Palette.Presets[i].Name, null, (_, _) => SetLook(idx, applyNow: true)));
        }
        m.Items.Add(new WF.ToolStripSeparator());
        m.Items.Add(new WF.ToolStripMenuItem((S.EffectsOn ? "✓  " : "     ") + "Effects", null, (_, _) => SetEffects(!S.EffectsOn)));
        m.Items.Add("     Restore Windows pointers", null, (_, _) => RestoreCursors());
        m.Items.Add(new WF.ToolStripSeparator());
        m.Items.Add("     Quit Myelin", null, (_, _) => Quit());
    }

    public void ShowWindow()
    {
        window ??= new MainWindow(this);
        window.Show();
        if (window.WindowState == System.Windows.WindowState.Minimized) window.WindowState = System.Windows.WindowState.Normal;
        window.Activate();
    }

    public void HintTray()
    {
        if (hinted) return;
        hinted = true;
        tray.ShowBalloonTip(2500, "Myelin is still running", "Your pointer and effects stay live. Click the tray icon to reopen.", WF.ToolTipIcon.None);
    }

    public void SettingsChanged()
    {
        if (!readOnly) S.Save();
        Fx.Apply(S);
        UpdateTrayIcon();
    }

    public void SetEffects(bool on)
    {
        S.EffectsOn = on;
        overlay.Running = on;
        if (!readOnly) S.Save();
        window?.SyncFromSettings();
    }

    public void SetLook(int index, bool applyNow = false)
    {
        S.ApplyPreset(index);
        SettingsChanged();
        if (window != null) window.OnLookChanged();
        if (applyNow || window == null) RequestLive();
    }

    public void NextLook()
    {
        int next = (Math.Max(-1, S.PresetIndex) + 1) % Palette.Presets.Length;
        SetLook(next, applyNow: true);
        tray.ShowBalloonTip(1200, "Myelin", Palette.Presets[next].Name + " · " + Palette.Presets[next].Tag, WF.ToolTipIcon.None);
    }

    public void ResetSettings()
    {
        S.CopyFrom(new Settings());
        SettingsChanged();
        overlay.Running = S.EffectsOn;
        Look.Apply(S);
        window?.SyncFromSettings(reload: true);
        if (S.LiveApply) RequestLive();
    }

    /// <summary>
    /// Live updates: a single-size pack (the size Windows uses right now) goes in within about a second,
    /// then the full multi-size pack replaces it. Newer requests supersede older ones.
    /// </summary>
    public void RequestLive()
    {
        if (readOnly) return;
        liveVersion++;
        if (!liveRunning) _ = LiveLoop();
    }

    async Task LiveLoop()
    {
        liveRunning = true;
        try
        {
            while (true)
            {
                int v = liveVersion;
                var p = S.Effective(Look.Light);
                Status?.Invoke("Updating your pointer…", .05);
                var quick = await Task.Run(() => Pack.Ensure(p, null, Pack.QuickSize()));
                if (v != liveVersion) continue;
                Scheme.Apply(quick, p.Name);
                S.AppliedKey = p.CacheKey;
                S.Save();
                Status?.Invoke("Your pointer is live", -1);
                window?.UpdateApplyState();

                var full = await Task.Run(() => Pack.Ensure(p, f => Status?.Invoke("Sharpening every size…", f)));
                if (v != liveVersion) continue;
                Scheme.Apply(full, p.Name);
                _ = Task.Run(() => Pack.Prune(Pack.DirFor(p)));
                Status?.Invoke("Your pointer is live", -1);
                window?.UpdateApplyState();
                if (v == liveVersion) break;
            }
        }
        catch (Exception ex) { Status?.Invoke("Couldn't update the pointer: " + ex.Message, -1); }
        finally { liveRunning = false; }
    }

    public async Task<string> ApplyCursorsAsync(Action<double> progress)
    {
        if (busy) return "Already applying…";
        busy = true;
        try
        {
            var p = S.Effective(Look.Light);
            var files = await Task.Run(() => Pack.Ensure(p, f => progress?.Invoke(f)));
            Scheme.Apply(files, p.Name);
            S.AppliedKey = p.CacheKey;
            if (!readOnly) S.Save();
            _ = Task.Run(() => Pack.Prune(Pack.DirFor(p)));
            return "Your pointer is live";
        }
        catch (Exception ex) { return "Couldn't apply: " + ex.Message; }
        finally { busy = false; }
    }

    public string RestoreCursors()
    {
        try
        {
            liveVersion++;
            Scheme.Restore();
            S.AppliedKey = "";
            if (!readOnly) S.Save();
            window?.SyncFromSettings();
            return "Windows pointers restored";
        }
        catch (Exception ex) { return "Couldn't restore: " + ex.Message; }
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsStartup()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Settings.AppName) != null;
    }

    public static void SetStartup(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue(Settings.AppName, $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue(Settings.AppName, false);
            k.DeleteValue("BioPulse", false);
        }
        catch { /* policy may block */ }
    }

    /// <summary>A BioPulse start-at-sign-in entry becomes a Myelin one pointing at this executable.</summary>
    static void MigrateStartup()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            if (k?.GetValue("BioPulse") != null) SetStartup(true);
        }
        catch { }
    }

    public async Task SnapAsync(string dir)
    {
        window = new MainWindow(this)
        {
            WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
            Left = -12000, Top = 0, Height = 860, ShowActivated = false, ShowInTaskbar = false
        };
        window.Show();
        await Task.Delay(2500);
        await window.SnapAsync(dir);
    }

    public void Quit()
    {
        Quitting = true;
        SystemEvents.UserPreferenceChanged -= OnSystemPreference;
        hotkeys.DestroyHandle();
        overlay.Running = false;
        overlay.Close();
        tray.Visible = false;
        tray.Dispose();
        window?.Close();
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>Ctrl+Alt+M = next look, Ctrl+Alt+E = effects on/off.</summary>
    sealed class Hotkeys : WF.NativeWindow
    {
        readonly Host host;
        public Hotkeys(Host host)
        {
            this.host = host;
            CreateHandle(new WF.CreateParams());
            RegisterHotKey(Handle, 1, 0x1 | 0x2 | 0x4000, (uint)WF.Keys.M);
            RegisterHotKey(Handle, 2, 0x1 | 0x2 | 0x4000, (uint)WF.Keys.E);
        }

        protected override void WndProc(ref WF.Message m)
        {
            if (m.Msg == 0x0312)
            {
                if ((int)m.WParam == 1) host.NextLook();
                else if ((int)m.WParam == 2) host.SetEffects(!host.S.EffectsOn);
            }
            base.WndProc(ref m);
        }

        public override void DestroyHandle()
        {
            if (Handle != IntPtr.Zero) { UnregisterHotKey(Handle, 1); UnregisterHotKey(Handle, 2); }
            base.DestroyHandle();
        }

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    }

    sealed class MenuColors : WF.ProfessionalColorTable
    {
        readonly Color bg, hi, line;
        public MenuColors(bool light)
        {
            bg = light ? Color.FromArgb(246, 246, 248) : Color.FromArgb(28, 28, 30);
            hi = light ? Color.FromArgb(228, 228, 233) : Color.FromArgb(58, 58, 60);
            line = light ? Color.FromArgb(224, 224, 229) : Color.FromArgb(46, 46, 49);
        }
        public override Color ToolStripDropDownBackground => bg;
        public override Color MenuBorder => line;
        public override Color MenuItemBorder => hi;
        public override Color MenuItemSelected => hi;
        public override Color MenuItemSelectedGradientBegin => hi;
        public override Color MenuItemSelectedGradientEnd => hi;
        public override Color MenuItemPressedGradientBegin => hi;
        public override Color MenuItemPressedGradientEnd => hi;
        public override Color ImageMarginGradientBegin => bg;
        public override Color ImageMarginGradientMiddle => bg;
        public override Color ImageMarginGradientEnd => bg;
        public override Color SeparatorDark => line;
        public override Color SeparatorLight => line;
    }
}

/// <summary>Renders packs, loads every file through the Win32 cursor loader, and writes proof sheets.</summary>
static class SelfTest
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr h, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadCursorFromFile(string file);
    [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr h, int cx, int cy, int step, IntPtr brush, int flags);
    [DllImport("user32.dll")] static extern bool DestroyCursor(IntPtr h);
    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(int colorRef);
    [DllImport("user32.dll")] static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr brush);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    static void Fill(IntPtr hdc, int l, int t, int r, int b, int colorRef)
    {
        var rc = new RECT { L = l, T = t, R = r, B = b };
        IntPtr br = CreateSolidBrush(colorRef);
        FillRect(hdc, ref rc, br);
        DeleteObject(br);
    }

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var log = new StringBuilder();
        int fails = 0;
        foreach (var (preset, tag) in new[] { (0, "tinted"), (1, "clear"), (0, "solid") })
        {
            var p = Palette.Presets[preset].Clone();
            if (tag == "solid") p.Shape = CursorShape.Solid;
            var sw = Stopwatch.StartNew();
            var files = Pack.Build(p, Path.Combine(dir, "pack-" + tag));
            log.AppendLine($"[{tag}] {p.Name}: full pack {sw.ElapsedMilliseconds} ms");
            fails += Sheet(p, files, Path.Combine(dir, $"cursor-sheet-{tag}.png"), log);
            sw.Restart();
            var quick = Pack.Build(p, Path.Combine(dir, "quick-" + tag), null, 32);
            log.AppendLine($"[{tag}] quick 32 px pack {sw.ElapsedMilliseconds} ms, {quick.Values.Count(f => LoadCursorFromFile(f) != IntPtr.Zero)}/17 load");
        }
        LookMatrix(Path.Combine(dir, "looks.png"));
        FxGallery(Path.Combine(dir, "fx-gallery.png"));
        log.AppendLine(fails == 0 ? "RESULT: all cursor files load through Win32" : $"RESULT: {fails} file(s) failed to load");
        File.WriteAllText(Path.Combine(dir, "report.txt"), log.ToString());
        return fails;
    }

    static int Sheet(Palette p, Dictionary<string, string> files, string png, StringBuilder log)
    {
        var defs = new Art(p).All();
        int fails = 0;
        const int rowH = 76;
        using var sheet = new Bitmap(1000, 34 + defs.Count * rowH, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(11, 11, 12));
        using var font = new Font("Consolas", 9f);
        var labels = new List<(string, float, float)> { ($"{p.Name} ({p.Shape}) — 32 / 64 dark / 64 light / animation, via Win32 DrawIconEx", 10, 8) };
        for (int i = 0; i < defs.Count; i++)
        {
            var d = defs[i]; var path = files[d.Role];
            int y = 34 + i * rowH, frames = d.Frames(p.Motion).Count;
            IntPtr h32 = LoadImage(IntPtr.Zero, path, 2, 32, 32, 0x10);
            IntPtr h64 = LoadImage(IntPtr.Zero, path, 2, 64, 64, 0x10);
            IntPtr hc = LoadCursorFromFile(path);
            if (h32 == IntPtr.Zero || h64 == IntPtr.Zero || hc == IntPtr.Zero) fails++;
            log.AppendLine($"  {d.Role,-12} {Path.GetFileName(path),-18} {new FileInfo(path).Length / 1024,6} KB  frames={frames,2}  load32={h32 != IntPtr.Zero}  load64={h64 != IntPtr.Zero}  LoadCursorFromFile={hc != IntPtr.Zero}");
            IntPtr hdc = g.GetHdc();
            try
            {
                Fill(hdc, 140, y, 1000, y + 72, 0x0C0B0B);
                Fill(hdc, 262, y, 334, y + 72, 0xF7F5F5);
                if (h32 != IntPtr.Zero) DrawIconEx(hdc, 150, y + 20, h32, 32, 32, 0, IntPtr.Zero, 3);
                if (h64 != IntPtr.Zero)
                {
                    DrawIconEx(hdc, 192, y + 4, h64, 64, 64, 0, IntPtr.Zero, 3);
                    DrawIconEx(hdc, 266, y + 4, h64, 64, 64, 0, IntPtr.Zero, 3);
                    int steps = Math.Min(frames, 9);
                    for (int k = 0; k < steps && frames > 1; k++)
                        DrawIconEx(hdc, 350 + k * 70, y + 4, h64, 64, 64, k * frames / steps, IntPtr.Zero, 3);
                }
            }
            finally { g.ReleaseHdc(hdc); }
            labels.Add(($"{d.Label}\n{frames}f", 10, y + 22));
            foreach (var h in new[] { h32, h64, hc }) if (h != IntPtr.Zero) DestroyCursor(h);
        }
        foreach (var (t, x, y) in labels) g.DrawString(t, font, Brushes.Gainsboro, x, y);
        sheet.Save(png, ImageFormat.Png);
        return fails;
    }

    static void LookMatrix(string png)
    {
        string[] show = { "Arrow", "Hand", "IBeam", "Wait", "Help", "Pin", "SizeAll", "No" };
        using var bmp = new Bitmap(250 + show.Length * 2 * 80, 20 + Palette.Presets.Length * 90, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(11, 11, 12));
        using var font = new Font("Segoe UI", 11f);
        using var light = new SolidBrush(Color.FromArgb(242, 242, 244));
        for (int r = 0; r < Palette.Presets.Length; r++)
        {
            var pp = Palette.Presets[r]; var dd = new Art(pp).All();
            int y = 12 + r * 90;
            g.DrawString(pp.Name + "\n" + pp.Tag, font, Brushes.Gainsboro, 14, y + 22);
            g.FillRectangle(light, 250 + show.Length * 80, y, show.Length * 80, 84);
            for (int c = 0; c < show.Length; c++)
            {
                var fr = dd.First(x => x.Role == show[c]).Frames(pp.Motion);
                using var b = Neon.ToBitmap(Neon.Render(fr[Math.Min(fr.Count - 1, fr.Count / 3)].Make(), 64), 64);
                g.DrawImage(b, 258 + c * 80, y + 10, 64, 64);
                g.DrawImage(b, 258 + (show.Length + c) * 80, y + 10, 64, 64);
            }
        }
        bmp.Save(png, ImageFormat.Png);
    }

    static void FxGallery(string png)
    {
        var tiles = new (string Name, Action<Settings> Set, int A, int B, bool Drag, bool Idle)[]
        {
            ("Comet + ferrocenes · Synapse (L) · Molecule (R)", s => { s.Trail = TrailStyle.Comet; s.LeftClick = ClickStyle.Synapse; s.RightClick = ClickStyle.Molecule; }, 0, 1, false, false),
            ("DNA helix · Atom click", s => { s.Trail = TrailStyle.Helix; s.LeftClick = ClickStyle.Atom; }, 0, -1, false, false),
            ("Neural · Mitosis click", s => { s.Trail = TrailStyle.Neural; s.LeftClick = ClickStyle.Mitosis; }, 0, -1, false, false),
            ("Pulse · Ripple click", s => { s.Trail = TrailStyle.Pulse; s.LeftClick = ClickStyle.Ripple; }, 0, -1, false, false),
            ("Glide · drag tether", s => { s.Trail = TrailStyle.Glide; }, -1, -1, true, false),
            ("Idle orbit", s => { s.Trail = TrailStyle.Off; }, -1, -1, false, true),
        };
        const int tw = 640, th = 300, cap = 30;
        using var bmp = new Bitmap(tw * 2, (th + cap) * 3, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(11, 11, 12));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font("Segoe UI Semibold", 11f);
        using var line = new Pen(Color.FromArgb(46, 46, 49));
        for (int i = 0; i < tiles.Length; i++)
        {
            int x = i % 2 * tw, y = i / 2 * (th + cap);
            var s = new Settings { Palette = Palette.Presets[0].Clone(), Ferrocenes = true, IdleOrbit = tiles[i].Idle, DragTether = true, DoubleClickMitosis = false };
            tiles[i].Set(s);
            g.DrawString(tiles[i].Name, font, Brushes.Gainsboro, x + 14, y + 6);
            var st = g.Save();
            g.SetClip(new Rectangle(x, y + cap, tw, th));
            g.TranslateTransform(x, y + cap);
            Effects.Scene(s, g, tw, th, 1.25f, tiles[i].A, tiles[i].B, tiles[i].Drag, tiles[i].Idle);
            g.Restore(st);
            g.DrawRectangle(line, x, y + cap, tw - 1, th - 1);
        }
        bmp.Save(png, ImageFormat.Png);
    }
}
