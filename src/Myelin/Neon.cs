using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Myelin;

public readonly struct Rgb
{
    public readonly float R, G, B;
    public Rgb(float r, float g, float b) { R = r; G = g; B = b; }
    public static Rgb Of(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f);
    public Rgb Mix(Rgb o, float t) => new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t);
}

/// <summary>Neon-tube shading recipe for one vector layer. Units are the 32×32 design grid.</summary>
public sealed class Style
{
    public Rgb Edge, Fill;
    public float EdgeW = 1.15f, Glow = .8f, GlowR = 1.7f, Inner = .35f, Hot = .5f, Alpha = 1f, FillAlpha = 1f;
    public float SweepPos, SweepAmt, SweepX = .45f, SweepY = .89f;
    /// <summary>Top-to-bottom body gradient strength (dark fills).</summary>
    public float Grad = .7f;
    /// <summary>Morphological rounding radius (closing + opening) → smooth, curvy corners.</summary>
    public float Round;
    /// <summary>Soft drop shadow (Fluid shape).</summary>
    public float Shadow, ShadowX = .55f, ShadowY = .95f, ShadowR = 1.3f;
    /// <summary>Optional bottom colour: Fill blends to Fill2 top→bottom.</summary>
    public Rgb? Fill2;
    /// <summary>Liquid Glass material: translucent tinted body (Fill), specular rim, refraction glow, hairline.</summary>
    public bool Glass;
    public float GlassAlpha = .28f;
}

public sealed class Layer
{
    public readonly Style S;
    public readonly Action<Graphics> Draw;
    /// <summary>Optional geometry id. Same key ⇒ same shape, so its distance field is cached across frames/palettes.</summary>
    public readonly string Key;
    public Layer(Style s, Action<Graphics> draw, string key = null) { S = s; Draw = draw; Key = key; }
}

/// <summary>
/// Vector mask → signed distance field → neon shading (glass core, rim light, white-hot tube, exp glow),
/// supersampled then box-filtered. Thread-safe: every call owns its GDI+ objects.
/// </summary>
public static class Neon
{
    [ThreadStatic] static SolidBrush white;
    public static Brush W => white ??= new SolidBrush(Color.White);

    static readonly ConcurrentDictionary<(string, int, float), float[]> Cache = new();

    public static byte[] Render(IList<Layer> layers, int S)
    {
        int K = S <= 48 ? 4 : S <= 64 ? 3 : 2, N = S * K;
        float u = 32f / N;
        var acc = new float[N * N * 4];
        var mask = new bool[N * N];
        using var bmp = new Bitmap(N, N, PixelFormat.Format32bppArgb);
        foreach (var L in layers)
        {
            float[] sd;
            if (L.Key != null)
            {
                if (Cache.Count > 800) Cache.Clear();
                sd = Cache.GetOrAdd((L.Key, N, L.S.Round), _ => Field(L, bmp, mask, N, u));
            }
            else sd = Field(L, bmp, mask, N, u);
            if (L.S.Glass) ShadeGlass(acc, sd, N, u, L.S);
            else Shade(acc, sd, N, u, L.S);
        }

        var o = new byte[S * S * 4];
        float inv = 1f / (K * K);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int j = 0; j < K; j++)
                {
                    int row = ((y * K + j) * N + x * K) * 4;
                    for (int i = 0; i < K; i++)
                    {
                        int p = row + i * 4;
                        r += acc[p]; g += acc[p + 1]; b += acc[p + 2]; a += acc[p + 3];
                    }
                }
                a *= inv;
                if (a < 1f / 255f) continue;
                float k = inv / a;
                int q = (y * S + x) * 4;
                o[q] = B8(b * k); o[q + 1] = B8(g * k); o[q + 2] = B8(r * k); o[q + 3] = B8(a);
            }
        return o;
    }

    static byte B8(float v) => (byte)Math.Clamp((int)(v * 255f + .5f), 0, 255);

    static float[] Field(Layer L, Bitmap bmp, bool[] mask, int N, float u)
    {
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            g.ScaleTransform(N / 32f, N / 32f);
            L.Draw(g);
        }
        ReadMask(bmp, mask, N);
        var sd = Sdf(mask, N);
        return L.S.Round > 0 ? RoundField(sd, N, L.S.Round / u) : sd;
    }

    /// <summary>Closing (rounds concave corners) then opening (rounds convex corners) on the distance field.</summary>
    static float[] RoundField(float[] sd, int n, float r)
    {
        var m = new bool[n * n];
        for (int i = 0; i < m.Length; i++) m[i] = sd[i] < r;   // dilate
        var s = Sdf(m, n);
        for (int i = 0; i < m.Length; i++) m[i] = s[i] < -r;   // erode  → closing
        s = Sdf(m, n);
        for (int i = 0; i < m.Length; i++) m[i] = s[i] < -r;   // erode
        s = Sdf(m, n);
        for (int i = 0; i < s.Length; i++) s[i] -= r;          // dilate → opening
        return s;
    }

    static unsafe void ReadMask(Bitmap bmp, bool[] m, int n)
    {
        var d = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < n; y++)
            {
                byte* row = (byte*)d.Scan0 + y * d.Stride;
                for (int x = 0; x < n; x++) m[y * n + x] = row[x * 4 + 3] > 127;
            }
        }
        finally { bmp.UnlockBits(d); }
    }

    static void Shade(float[] acc, float[] sd, int N, float u, Style s)
    {
        float hw = s.EdgeW * .5f, c = -hw * .55f, reach = hw + s.GlowR * 6f;
        int sox = 0, soy = 0;
        if (s.Shadow > 0)
        {
            reach = MathF.Max(reach, s.ShadowR * 6f + MathF.Abs(s.ShadowX) + MathF.Abs(s.ShadowY));
            sox = (int)MathF.Round(s.ShadowX / u); soy = (int)MathF.Round(s.ShadowY / u);
        }
        bool light = .3f * s.Fill.R + .59f * s.Fill.G + .11f * s.Fill.B > .5f;
        for (int y = 0; y < N; y++)
        {
            float py = (y + .5f) * u;
            float shade = s.Fill2.HasValue ? 1f : light ? 1f - .1f * py / 32f : 1f + s.Grad * (1f - py / 32f);
            var fill = s.Fill2.HasValue ? s.Fill.Mix(s.Fill2.Value, py / 32f) : s.Fill;
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                float d = sd[i] * u;
                if (d > reach) continue;
                float px = (x + .5f) * u;
                float r = 0, g = 0, b = 0, a = 0;

                if (d <= 0) // glass core with inner rim light
                {
                    float inner = MathF.Exp(d / 1.6f) * s.Inner;
                    a = s.FillAlpha;
                    r = (fill.R * shade + s.Edge.R * inner) * a;
                    g = (fill.G * shade + s.Edge.G * inner) * a;
                    b = (fill.B * shade + s.Edge.B * inner) * a;
                }

                float t = 1f - MathF.Abs(d - c) / hw;
                if (t > 0) // neon tube, white-hot at its centre
                {
                    float hot = s.Hot * t * t, ta = MathF.Min(1f, t * 1.6f);
                    float er = s.Edge.R + (1 - s.Edge.R) * hot, eg = s.Edge.G + (1 - s.Edge.G) * hot, eb = s.Edge.B + (1 - s.Edge.B) * hot;
                    r = er * ta + r * (1 - ta); g = eg * ta + g * (1 - ta); b = eb * ta + b * (1 - ta);
                    a = ta + a * (1 - ta);
                }

                if (s.SweepAmt > 0 && d < hw) // scan shimmer
                {
                    float pr = px * s.SweepX + py * s.SweepY - s.SweepPos;
                    float band = MathF.Exp(-pr * pr / 2.2f) * s.SweepAmt * a;
                    r += band; g += band; b += band;
                }

                float edgeFade = Math.Clamp(MathF.Min(MathF.Min(px, py), MathF.Min(32 - px, 32 - py)) / 1.8f, 0f, 1f);

                if (s.Shadow > 0 && a < 1 && edgeFade > 0) // soft drop shadow
                {
                    int sx = Math.Clamp(x - sox, 0, N - 1), sy = Math.Clamp(y - soy, 0, N - 1);
                    float so = sd[sy * N + sx] * u;
                    float sa = s.Shadow * MathF.Exp(-MathF.Max(so, 0) / s.ShadowR) * (1 - a) * edgeFade;
                    r += .01f * sa; g += .015f * sa; b += .03f * sa; a += sa;
                }

                if (d > c && s.Glow > 0 && edgeFade > 0) // outer bloom, faded at canvas border
                {
                    float ga = s.Glow * .7f * MathF.Exp(-(d - c) / s.GlowR) * edgeFade * (1 - MathF.Min(1, a));
                    r += s.Edge.R * ga; g += s.Edge.G * ga; b += s.Edge.B * ga; a += ga;
                }

                if (s.Alpha < 1) { r *= s.Alpha; g *= s.Alpha; b *= s.Alpha; a *= s.Alpha; }
                if (a <= 0) continue;
                if (a > 1) a = 1;
                int p = i * 4;
                float ia = 1 - a;
                acc[p] = r + acc[p] * ia;
                acc[p + 1] = g + acc[p + 1] * ia;
                acc[p + 2] = b + acc[p + 2] * ia;
                acc[p + 3] = a + acc[p + 3] * ia;
            }
        }
    }

    static void Over(ref float r, ref float g, ref float b, ref float a, float cr, float cg, float cb, float ca)
    {
        if (ca <= 0) return;
        if (ca > 1) ca = 1;
        r = cr * ca + r * (1 - ca); g = cg * ca + g * (1 - ca); b = cb * ca + b * (1 - ca); a = ca + a * (1 - ca);
    }

    /// <summary>
    /// Liquid Glass: a translucent tinted body that thickens toward its rim, a white specular highlight on the
    /// edge facing the light (top-left), a refraction glow inside the far edge, a dark hairline for legibility on
    /// light backgrounds, and a soft offset shadow. Lighting uses the distance field's gradient as the surface normal.
    /// </summary>
    static void ShadeGlass(float[] acc, float[] sd, int N, float u, Style s)
    {
        const float Lx = -.55f, Ly = -.835f;
        float reach = 1.5f;
        if (s.Glow > 0) reach = MathF.Max(reach, s.GlowR * 6f);
        if (s.Shadow > 0) reach = MathF.Max(reach, s.ShadowR * 6f + MathF.Abs(s.ShadowX) + MathF.Abs(s.ShadowY));
        int sox = (int)MathF.Round(s.ShadowX / u), soy = (int)MathF.Round(s.ShadowY / u);
        for (int y = 0; y < N; y++)
        {
            float py = (y + .5f) * u, vert = 1f - py / 32f;
            var tint = s.Fill2.HasValue ? s.Fill.Mix(s.Fill2.Value, py / 32f) : s.Fill;
            var rim = tint.Mix(new Rgb(1, 1, 1), .45f);
            int yt = Math.Max(0, y - 1), yb = Math.Min(N - 1, y + 1);
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                float d = sd[i] * u;
                if (d > reach) continue;
                float px = (x + .5f) * u;
                int xl = Math.Max(0, x - 1), xr = Math.Min(N - 1, x + 1);
                float gx = sd[y * N + xr] - sd[y * N + xl], gy = sd[yb * N + x] - sd[yt * N + x];
                float gl = MathF.Sqrt(gx * gx + gy * gy), facing = 0;
                if (gl > 1e-4f) facing = (gx * Lx + gy * Ly) / gl; // +1 = edge faces the light
                float r = 0, g = 0, b = 0, a = 0;

                if (d <= 0)
                {
                    float thick = MathF.Exp(d / 1.5f);
                    float ba = s.GlassAlpha + .2f * thick, br = .85f + .3f * vert;
                    r = tint.R * br * ba; g = tint.G * br * ba; b = tint.B * br * ba; a = ba;
                    Over(ref r, ref g, ref b, ref a, 1, 1, 1, .13f * vert * vert);                                    // top sheen
                    Over(ref r, ref g, ref b, ref a, rim.R, rim.G, rim.B, MathF.Exp(d / .9f) * MathF.Max(0, -facing) * .6f); // refraction
                    float band = MathF.Exp(-MathF.Pow((d + .45f) / .32f, 2));
                    Over(ref r, ref g, ref b, ref a, 1, 1, 1, band * MathF.Pow(MathF.Max(0, facing), 1.3f) * .95f);        // specular
                    Over(ref r, ref g, ref b, ref a, rim.R, rim.G, rim.B, MathF.Exp(-MathF.Pow((d + .16f) / .17f, 2)) * .4f); // glass edge
                    if (s.SweepAmt > 0)
                    {
                        float pr = px * s.SweepX + py * s.SweepY - s.SweepPos;
                        Over(ref r, ref g, ref b, ref a, 1, 1, 1, MathF.Exp(-pr * pr / 2.2f) * s.SweepAmt * .6f);
                    }
                }
                else
                {
                    float hl = MathF.Exp(-MathF.Pow(d / .38f, 2)) * .42f * (1 - .5f * MathF.Max(0, facing));        // hairline
                    Over(ref r, ref g, ref b, ref a, .04f, .05f, .07f, hl);
                }

                float edgeFade = Math.Clamp(MathF.Min(MathF.Min(px, py), MathF.Min(32 - px, 32 - py)) / 1.8f, 0f, 1f);
                if (a < 1 && edgeFade > 0)
                {
                    if (s.Shadow > 0)
                    {
                        int sx = Math.Clamp(x - sox, 0, N - 1), sy = Math.Clamp(y - soy, 0, N - 1);
                        float sa = s.Shadow * MathF.Exp(-MathF.Max(sd[sy * N + sx] * u, 0) / s.ShadowR) * (1 - a) * edgeFade;
                        r += .01f * sa; g += .015f * sa; b += .03f * sa; a += sa;
                    }
                    if (s.Glow > 0 && d > 0)
                    {
                        float ga = s.Glow * .5f * MathF.Exp(-d / s.GlowR) * edgeFade * (1 - MathF.Min(1, a));
                        r += tint.R * ga; g += tint.G * ga; b += tint.B * ga; a += ga;
                    }
                }

                if (s.Alpha < 1) { r *= s.Alpha; g *= s.Alpha; b *= s.Alpha; a *= s.Alpha; }
                if (a <= 0) continue;
                if (a > 1) a = 1;
                int p = i * 4; float ia = 1 - a;
                acc[p] = r + acc[p] * ia; acc[p + 1] = g + acc[p + 1] * ia; acc[p + 2] = b + acc[p + 2] * ia; acc[p + 3] = a + acc[p + 3] * ia;
            }
        }
    }

    static float[] Sdf(bool[] m, int n)
    {
        var outD = Edt(m, n, true);
        var inD = Edt(m, n, false);
        var r = new float[n * n];
        for (int i = 0; i < r.Length; i++)
            r[i] = m[i] ? -((float)Math.Sqrt(inD[i]) - .5f) : (float)Math.Sqrt(outD[i]) - .5f;
        return r;
    }

    /// <summary>Felzenszwalb squared Euclidean distance transform to the nearest feature pixel.</summary>
    static double[] Edt(bool[] m, int n, bool featureIsInside)
    {
        const double INF = 1e20;
        var f = new double[n * n];
        for (int i = 0; i < f.Length; i++) f[i] = m[i] == featureIsInside ? 0 : INF;
        var line = new double[n]; var d = new double[n]; var v = new int[n]; var z = new double[n + 1];
        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < n; y++) line[y] = f[y * n + x];
            Edt1(line, n, d, v, z);
            for (int y = 0; y < n; y++) f[y * n + x] = d[y];
        }
        for (int y = 0; y < n; y++)
        {
            Array.Copy(f, y * n, line, 0, n);
            Edt1(line, n, d, v, z);
            Array.Copy(d, 0, f, y * n, n);
        }
        return f;
    }

    static void Edt1(double[] f, int n, double[] d, int[] v, double[] z)
    {
        int k = 0;
        v[0] = 0; z[0] = double.NegativeInfinity; z[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s = (f[q] + (double)q * q - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
            while (s <= z[k])
            {
                k--;
                s = (f[q] + (double)q * q - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
            }
            k++; v[k] = q; z[k] = s; z[k + 1] = double.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            double dq = q - v[k];
            d[q] = dq * dq + f[v[k]];
        }
    }

    public static Bitmap ToBitmap(byte[] px, int S)
    {
        var b = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        var d = b.LockBits(new Rectangle(0, 0, S, S), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < S; y++) Marshal.Copy(px, y * S * 4, d.Scan0 + y * d.Stride, S * 4);
        b.UnlockBits(d);
        return b;
    }
}
