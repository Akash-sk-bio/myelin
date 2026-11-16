using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;

namespace Myelin;

/// <summary>Bridges the GDI+ cursor renderer to WPF bitmaps (frozen, so they can be built off-thread).</summary>
public static class Img
{
    public static BitmapSource From(byte[] bgra, int s)
    {
        var b = BitmapSource.Create(s, s, 96, 96, PixelFormats.Bgra32, null, bgra, s * 4);
        b.Freeze();
        return b;
    }

    public static (BitmapSource[] Frames, int[] Jif) Frames(CursorDef d, Motion m, int px)
    {
        var fr = d.Frames(m);
        var bmps = fr.AsParallel().AsOrdered().Select(f => From(Neon.Render(f.Make(), px), px)).ToArray();
        return (bmps, fr.Select(f => Math.Max(1, f.Jiffies)).ToArray());
    }

    public static BitmapSource Still(CursorDef d, int px, Motion m = Motion.Off, int index = 0)
    {
        var fr = d.Frames(m);
        return From(Neon.Render(fr[Math.Clamp(index, 0, fr.Count - 1)].Make(), px), px);
    }

    public static Color Wpf(SD.Color c) => Color.FromRgb(c.R, c.G, c.B);
}

/// <summary>Plays a cursor's frames with their real .ani timings, sharing one UI timer across instances.</summary>
public sealed class AnimImage : Image
{
    static readonly List<AnimImage> Live = new();
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static DispatcherTimer timer;

    BitmapSource[] frames = Array.Empty<BitmapSource>();
    int[] jif = Array.Empty<int>();
    int total = 1, shown = -1;

    public AnimImage()
    {
        Stretch = Stretch.Uniform;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Loaded += (_, _) => { if (!Live.Contains(this)) Live.Add(this); EnsureTimer(); };
        Unloaded += (_, _) => Live.Remove(this);
    }

    public void SetFrames(BitmapSource[] f, int[] j)
    {
        frames = f; jif = j; total = Math.Max(1, j.Sum()); shown = -1;
        Tick();
    }

    static void EnsureTimer()
    {
        if (timer != null) return;
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => { foreach (var a in Live) if (a.IsVisible) a.Tick(); };
        timer.Start();
    }

    void Tick()
    {
        if (frames.Length == 0) { Source = null; return; }
        long j = Clock.ElapsedMilliseconds * 60 / 1000 % total;
        int f = 0;
        while (f < jif.Length - 1 && j >= jif[f]) { j -= jif[f]; f++; }
        if (f != shown) { shown = f; Source = frames[f]; }
    }
}

/// <summary>Live, scripted preview of the effects engine (runs only while visible).</summary>
public sealed class FxDemo : Image
{
    public static SD.Bitmap Pointer;
    public static float PointerHx, PointerHy;

    readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    readonly Stopwatch clock = Stopwatch.StartNew();
    Effects fx;
    int button = -1, clicks, pw, ph;
    float speed = 1f;
    double last, t, nextClick = .7;
    WriteableBitmap wb;
    SD.Bitmap gdi;

    public FxDemo()
    {
        Stretch = Stretch.Fill;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        timer.Tick += (_, _) => Step();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { last = clock.Elapsed.TotalSeconds; timer.Start(); }
            else timer.Stop();
        };
        Unloaded += (_, _) => timer.Stop();
    }

    /// <param name="clickButton">-1 none, 0 left, 1 right, 2 alternate left/right.</param>
    public void Configure(Settings s, int clickButton = -1, float speed = 1f)
    {
        fx = new Effects(s);
        button = clickButton;
        this.speed = speed;
        pw = 0;
    }

    void Step()
    {
        // An Image without a Source measures to 0×0, so take the size from the hosting container.
        var box = Parent as FrameworkElement;
        double lw = box?.ActualWidth ?? ActualWidth, lh = box?.ActualHeight ?? ActualHeight;
        if (fx == null || lw < 8 || lh < 8) return;
        var src = PresentationSource.FromVisual(this);
        double sc = src?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        int w = Math.Max(8, (int)(lw * sc)), h = Math.Max(8, (int)(lh * sc));
        if (w != pw || h != ph || wb == null)
        {
            pw = w; ph = h;
            gdi?.Dispose();
            gdi = new SD.Bitmap(w, h, SDI.PixelFormat.Format32bppPArgb);
            wb = new WriteableBitmap(w, h, 96 * sc, 96 * sc, PixelFormats.Pbgra32, null);
            Source = wb;
            fx.K = (float)(sc * .85);
            fx.Reset();
        }
        double now = clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(.05, now - last);
        last = now;
        t += dt * speed;
        float x = (float)(w * (.5 + .36 * Math.Sin(t * 1.25))), y = (float)(h * (.5 + .27 * Math.Sin(t * 2.1 + .6)));
        if (button >= 0 && t > nextClick)
        {
            int b = button == 2 ? clicks++ % 2 : button;
            fx.Press(x, y, b); fx.Release(b);
            nextClick = t + 1.15;
        }
        fx.Update(x, y, dt);
        using (var g = SD.Graphics.FromImage(gdi))
        {
            g.Clear(SD.Color.Transparent);
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            fx.Draw(g);
            if (Pointer != null) g.DrawImage(Pointer, x - PointerHx, y - PointerHy, Pointer.Width, Pointer.Height);
        }
        var d = gdi.LockBits(new SD.Rectangle(0, 0, w, h), SDI.ImageLockMode.ReadOnly, SDI.PixelFormat.Format32bppPArgb);
        try { wb.WritePixels(new Int32Rect(0, 0, w, h), d.Scan0, d.Stride * h, d.Stride); }
        finally { gdi.UnlockBits(d); }
    }
}
