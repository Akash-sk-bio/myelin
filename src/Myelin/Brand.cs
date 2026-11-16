using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Myelin;

/// <summary>
/// Myelin mark: the segmented M. Rounded segments (the sheath) spell an M, the gaps are the nodes of Ranvier,
/// and the centre chevron is a pointer tip landing on a node. White on a cyan→indigo squircle, with a dark variant.
/// </summary>
public static class Brand
{
    public static readonly int[] IconSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    public static Bitmap LogoBitmap(int size, bool dark = false) => Neon.ToBitmap(Neon.Render(Art.LogoLayers(dark), size), size);

    public static System.Windows.Media.Imaging.BitmapSource WpfLogo(int size) => Img.From(Neon.Render(Art.LogoLayers(), size), size);

    /// <summary>Multi-resolution .ico with PNG-compressed entries (Vista+).</summary>
    public static byte[] Ico()
    {
        var pngs = IconSizes.Select(s =>
        {
            using var b = LogoBitmap(s);
            using var ms = new MemoryStream();
            b.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToList();
        using var o = new MemoryStream();
        using var w = new BinaryWriter(o);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)pngs.Count);
        int off = 6 + 16 * pngs.Count;
        for (int i = 0; i < pngs.Count; i++)
        {
            int s = IconSizes[i];
            w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32);
            w.Write(pngs[i].Length); w.Write(off);
            off += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
        return o.ToArray();
    }

    public static void WriteAssets(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "Myelin.ico"), Ico());
        using (var b = LogoBitmap(512)) b.Save(Path.Combine(dir, "logo-512.png"), ImageFormat.Png);
        using (var b = LogoBitmap(128)) b.Save(Path.Combine(dir, "logo-128.png"), ImageFormat.Png);
        using (var b = LogoBitmap(512, dark: true)) b.Save(Path.Combine(dir, "logo-dark-512.png"), ImageFormat.Png);
        File.WriteAllText(Path.Combine(dir, "logo.svg"), Svg);
        File.WriteAllText(Path.Combine(dir, "logo-dark.svg"), SvgDark);
        File.WriteAllText(Path.Combine(dir, "mark.svg"), SvgMark);
        LogoSheet(Path.Combine(dir, "logo-sheet.png"));
        SocialPreview(Path.Combine(dir, "social-preview.png"));
    }

    static Font F(string family, float px, FontStyle st, string fallback)
    {
        var f = new Font(family, px, st, GraphicsUnit.Pixel);
        if (f.Name == family) return f;
        f.Dispose();
        return new Font(fallback, px, st, GraphicsUnit.Pixel);
    }

    static Bitmap Mark(int size, Rgb color) => Neon.ToBitmap(Neon.Render(Art.MarkOnly(color), size), size);

    /// <summary>Proof sheet: icon, dark icon, single-colour mark, a 16→128 px size ladder and wordmark lockups.</summary>
    public static void LogoSheet(string path)
    {
        const int W = 1600, H = 960, half = W / 2;
        using var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Black);
        using (var white = new SolidBrush(Color.White)) g.FillRectangle(white, half, 0, half, H);
        using var cap = F("Segoe UI", 15f, FontStyle.Regular, "Segoe UI");
        using var word = F("Segoe UI Variable Display Semibold", 76f, FontStyle.Regular, "Segoe UI Semibold");
        using var grayD = new SolidBrush(Color.FromArgb(142, 142, 147));
        using var grayL = new SolidBrush(Color.FromArgb(110, 110, 115));

        void Draw(Bitmap b, float x, float y, float s) { using (b) g.DrawImage(b, x, y, s, s); }

        Draw(LogoBitmap(220), 90, 80, 220);
        Draw(LogoBitmap(220, dark: true), 400, 80, 220);
        g.DrawString("App icon", cap, grayD, 90, 314);
        g.DrawString("Dark", cap, grayD, 400, 314);
        Draw(LogoBitmap(220), half + 90, 80, 220);
        Draw(Mark(220, new Rgb(.11f, .11f, .12f)), half + 400, 80, 220);
        g.DrawString("App icon", cap, grayL, half + 90, 314);
        g.DrawString("Single colour", cap, grayL, half + 400, 314);

        int[] ladder = { 16, 24, 32, 48, 64, 128 };
        float lx = 90;
        foreach (var s in ladder)
        {
            Draw(LogoBitmap(s), lx, 520 - s, s);
            Draw(LogoBitmap(s), half + lx, 520 - s, s);
            lx += s + 34;
        }
        g.DrawString("16 · 24 · 32 · 48 · 64 · 128 px", cap, grayD, 90, 540);
        g.DrawString("16 · 24 · 32 · 48 · 64 · 128 px", cap, grayL, half + 90, 540);

        Draw(LogoBitmap(104), 90, 700, 104);
        using (var t = new SolidBrush(Color.FromArgb(245, 245, 247))) g.DrawString("Myelin", word, t, 214, 700, StringFormat.GenericTypographic);
        Draw(LogoBitmap(104), half + 90, 700, 104);
        using (var t = new SolidBrush(Color.FromArgb(29, 29, 31))) g.DrawString("Myelin", word, t, half + 214, 700, StringFormat.GenericTypographic);

        bmp.Save(path, ImageFormat.Png);
    }

    /// <summary>1280×640 GitHub social preview.</summary>
    public static void SocialPreview(string path)
    {
        const int W = 1280, H = 640;
        using var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Black);

        using (var logo = LogoBitmap(220)) g.DrawImage(logo, 104, 120, 220, 220);

        using var title = F("Segoe UI Variable Display Semibold", 120f, FontStyle.Regular, "Segoe UI Semibold");
        using var tag = F("Segoe UI", 34f, FontStyle.Regular, "Segoe UI");
        using var sub = F("Segoe UI", 21f, FontStyle.Regular, "Segoe UI");
        var typo = StringFormat.GenericTypographic;
        using (var w = new SolidBrush(Color.FromArgb(245, 245, 247))) g.DrawString("Myelin", title, w, 370, 118, typo);
        using (var m = new SolidBrush(Color.FromArgb(161, 161, 166))) g.DrawString("Liquid-glass pointers that glide.", tag, m, 376, 262, typo);
        using (var m = new SolidBrush(Color.FromArgb(110, 110, 115)))
            g.DrawString("17 animated Windows pointers  ·  live Bio-AI effects  ·  clear or tinted glass", sub, m, 378, 318, typo);

        var sig = new Art(Palette.Presets[0]).All().ToDictionary(d => d.Role);
        var cry = new Art(Palette.Presets[1]).All().ToDictionary(d => d.Role);
        var emb = new Art(Palette.Presets[2]).All().ToDictionary(d => d.Role);
        var row = new (CursorDef D, int F)[]
        {
            (sig["Arrow"], 0), (sig["Wait"], 6), (sig["Hand"], 0), (cry["Arrow"], 0), (cry["IBeam"], 0), (emb["Arrow"], 0), (emb["Pin"], 0), (emb["Wait"], 14)
        };
        using var card = new SolidBrush(Color.FromArgb(28, 28, 30));
        for (int i = 0; i < row.Length; i++)
        {
            float x = 104 + i * 136, y = 418;
            using (var rp = Round(new RectangleF(x, y, 116, 116), 24)) g.FillPath(card, rp);
            var fr = row[i].D.Frames(Motion.Full);
            using var c = Neon.ToBitmap(Neon.Render(fr[Math.Min(row[i].F, fr.Count - 1)].Make(), 80), 80);
            g.DrawImage(c, x + 18, y + 18, 80, 80);
        }
        bmp.Save(path, ImageFormat.Png);
    }

    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    // Geometry = the 32-unit design grid × 8: one M path in three rounded segments (32 px), two signal nodes in the gaps.
    const string MarkPaths = """
      <line x1="67.2" y1="187.2" x2="67.2" y2="158.4"/>
      <polyline points="67.2,120 67.2,72 128,145.6 188.8,72 188.8,120"/>
      <line x1="188.8" y1="158.4" x2="188.8" y2="187.2"/>
""";

    public const string Svg = """
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256" role="img" aria-label="Myelin">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#2ED9F7"/>
      <stop offset="1" stop-color="#3B45F5"/>
    </linearGradient>
    <linearGradient id="mark" gradientUnits="userSpaceOnUse" x1="0" y1="56" x2="0" y2="200">
      <stop offset="0" stop-color="#FFFFFF"/>
      <stop offset="1" stop-color="#DBE8FF"/>
    </linearGradient>
    <filter id="lift" x="-20%" y="-20%" width="140%" height="140%">
      <feDropShadow dx="0" dy="6" stdDeviation="6" flood-color="#0B1A66" flood-opacity=".35"/>
    </filter>
  </defs>
  <rect x="10" y="10" width="236" height="236" rx="58" fill="url(#bg)"/>
  <g filter="url(#lift)">
    <g fill="none" stroke="url(#mark)" stroke-width="32" stroke-linecap="round" stroke-linejoin="round">
""" + MarkPaths + """
    </g>
    <circle cx="128" cy="182.4" r="16" fill="url(#mark)"/>
    <circle cx="67.2" cy="139.2" r="7.2" fill="#FFD60A"/>
    <circle cx="188.8" cy="139.2" r="7.2" fill="#FFD60A"/>
  </g>
</svg>
""";

    public const string SvgDark = """
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256" role="img" aria-label="Myelin">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#1C1C1E"/>
      <stop offset="1" stop-color="#000000"/>
    </linearGradient>
    <linearGradient id="mark" gradientUnits="userSpaceOnUse" x1="0" y1="56" x2="0" y2="200">
      <stop offset="0" stop-color="#2ED9F7"/>
      <stop offset="1" stop-color="#5A62F7"/>
    </linearGradient>
  </defs>
  <rect x="10" y="10" width="236" height="236" rx="58" fill="url(#bg)" stroke="#3D3D42" stroke-width="4"/>
  <g fill="none" stroke="url(#mark)" stroke-width="32" stroke-linecap="round" stroke-linejoin="round">
""" + MarkPaths + """
  </g>
  <circle cx="128" cy="182.4" r="16" fill="url(#mark)"/>
  <circle cx="67.2" cy="139.2" r="7.2" fill="#FFD60A"/>
  <circle cx="188.8" cy="139.2" r="7.2" fill="#FFD60A"/>
</svg>
""";

    /// <summary>The bare mark in currentColor, for single-colour use (docs, stickers, monochrome icons).</summary>
    public const string SvgMark = """
<svg xmlns="http://www.w3.org/2000/svg" viewBox="40 48 176 168" width="176" height="168" role="img" aria-label="Myelin">
  <g fill="none" stroke="currentColor" stroke-width="32" stroke-linecap="round" stroke-linejoin="round">
""" + MarkPaths + """
  </g>
  <circle cx="128" cy="182.4" r="16" fill="currentColor"/>
  <circle cx="67.2" cy="139.2" r="7.2" fill="currentColor"/>
  <circle cx="188.8" cy="139.2" r="7.2" fill="currentColor"/>
</svg>
""";
}
