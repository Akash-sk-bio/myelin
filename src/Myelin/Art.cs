using System.Drawing;
using System.Drawing.Drawing2D;

namespace Myelin;

/// <summary>One animation frame: layer factory + display time in jiffies (1/60 s). One frame â static .cur.</summary>
public sealed record Frame(Func<List<Layer>> Make, int Jiffies);

public sealed record CursorDef(string Role, string File, string Label, float Hx, float Hy, Func<Motion, List<Frame>> Frames);

/// <summary>
/// Myelin glyph library on a 32Ã32 design grid, in two shapes:
///  â¢ Neon  â dark glass body inside a glowing neon tube (HUD look).
///  â¢ Fluid â smooth, curvy, tail-less agent-style pointer: solid colour body, white rim, soft shadow.
/// Bio-AI motifs everywhere: ECG trace + heartbeat LED, DNA-spindle loaders, benzene nuclei, neural nodes.
/// </summary>
public sealed class Art
{
    readonly Rgb neon, accent, core, alert;
    readonly float glow;
    readonly bool fluid, glass, tinted, solid;
    static readonly Rgb White = new(1, 1, 1);

    public Art(Palette p)
    {
        neon = Rgb.Of(p.NeonC); accent = Rgb.Of(p.AccentC); core = Rgb.Of(p.CoreC); alert = Rgb.Of(p.AlertC);
        glow = p.Glow;
        glass = p.Shape is CursorShape.Clear or CursorShape.Tinted;
        tinted = p.Shape == CursorShape.Tinted;
        solid = p.Shape == CursorShape.Solid;
        fluid = p.Shape != CursorShape.Neon; // glass shares the smooth Fluid geometry
    }

    Rgb GlassTint(Rgb c) => tinted ? c.Mix(White, .08f) : White.Mix(c, .14f);

    Style GlassS(Rgb c, float g = 1, float round = 1.1f) => new()
    {
        Glass = true, Fill = GlassTint(c), GlassAlpha = tinted ? .52f : .15f,
        Glow = glow * (tinted ? .35f : .18f) * g, GlowR = 1.4f,
        Shadow = .42f, ShadowX = .45f, ShadowY = 1f, ShadowR = 1.5f, Round = round
    };

    static readonly Rgb Black = new(0, 0, 0);

    /// <summary>Solid (light theme): opaque tinted body, darker outline that reads on white, soft shadow. No glass.</summary>
    Style SolidS(Rgb c, float g = 1, float round = 1.1f) => new()
    {
        Fill = c.Mix(White, .12f), Fill2 = c.Mix(Black, .1f), Edge = c.Mix(Black, .5f), EdgeW = 1f,
        Glow = 0, Inner = 0, Hot = 0, Grad = 0, Shadow = .38f, ShadowX = .35f, ShadowY = .8f, ShadowR = 1.2f, Round = round
    };

    static Brush W => Neon.W;
    string K(string id) => (fluid ? "f:" : "n:") + id; // geometry cache id (shape-specific)

    // âââââââââ styles âââââââââ
    Style Body(float g = 1) => solid ? SolidS(neon, g) : glass ? GlassS(neon, g) : fluid
        ? new() { Edge = White.Mix(neon, .1f), Fill = neon, Glow = glow * .45f * g, GlowR = 1.5f, EdgeW = 1.3f, Inner = .15f, Hot = .15f, Grad = .3f, Round = 1.1f, Shadow = .5f }
        : new() { Edge = neon, Fill = core, Glow = glow * g, EdgeW = 1.15f, Round = .3f };

    Style Line(float g = 1, float w = 1.15f) => fluid
        ? new() { Edge = neon, Fill = neon, Glow = glow * .6f * g, GlowR = 1.4f, EdgeW = w, Hot = .6f, Grad = .2f, Shadow = .35f }
        : new() { Edge = neon, Fill = core, Glow = glow * g, EdgeW = w };

    Style A(float g = 1, float w = 1f) => new() { Edge = accent, Fill = accent.Mix(core, .45f), Glow = glow * g, EdgeW = w, GlowR = 1.3f, Inner = .6f };
    Style Dim(float k) => new() { Edge = neon.Mix(core, k), Fill = core, Glow = glow * .3f, EdgeW = 1f, Hot = .15f };
    Style Ink(float w = .8f) => new() { Edge = core, Fill = core, Glow = 0, EdgeW = w, Hot = 0, Inner = 0, Grad = 0 };

    Style Badge(float g)
    {
        if (solid) return SolidS(accent, g, .4f);
        if (glass) { var gs = GlassS(accent, g, .4f); gs.GlassAlpha = tinted ? .55f : .35f; gs.Fill = accent.Mix(White, .1f); return gs; }
        if (fluid) return new() { Edge = White, Fill = accent, Glow = glow * .4f * g, GlowR = 1.3f, EdgeW = 1.1f, Shadow = .45f, Grad = .25f, Hot = .1f, Inner = 0 };
        var s = A(g, 1.05f); s.Fill = core; return s;
    }

    Style Glyph(float g)
    {
        if (glass || solid) return new() { Edge = core, Fill = White, EdgeW = .7f, Glow = 0, Hot = 0, Inner = 0, Grad = 0 };
        if (fluid) return Ink(.9f);
        var s = A(g, .9f); s.Fill = accent; s.GlowR = .8f; return s;
    }

    Style Led(float led)
    {
        var s = A(.6f + led, .9f); s.Fill = accent; s.Hot = .4f + .5f * led;
        if (fluid) { s.Edge = White; s.EdgeW = .7f; }
        return s;
    }

    // âââââââââ helpers âââââââââ
    static float Beat(float t) // lub-dub
    {
        float a = MathF.Exp(-MathF.Pow((t - .12f) / .06f, 2));
        float b = .7f * MathF.Exp(-MathF.Pow((t - .32f) / .07f, 2));
        return MathF.Min(1, a + b);
    }

    static PointF Lerp(PointF a, PointF b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    static PointF[] Poly(float cx, float cy, float r, int n, float rotDeg)
    {
        var p = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            float a = (rotDeg + 360f / n * i) * MathF.PI / 180f;
            p[i] = new(cx + r * MathF.Cos(a), cy + r * MathF.Sin(a));
        }
        return p;
    }

    static void FillPoly(Graphics g, PointF[] p, float round)
    {
        g.FillPolygon(W, p);
        using var pen = new Pen(Color.White, round) { LineJoin = LineJoin.Round };
        g.DrawPolygon(pen, p);
    }

    static void Rr(Graphics g, float x, float y, float w, float h, float r)
    {
        using var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        g.FillPath(W, p);
    }

    static Pen RoundPen(float w) => new(Color.White, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    static void Lines(Graphics g, List<(PointF A, PointF B)> segs, float w)
    {
        using var p = RoundPen(w);
        foreach (var (a, b) in segs) g.DrawLine(p, a, b);
    }

    static List<Frame> Loop(int n, int rate, Func<float, List<Layer>> make)
    {
        var f = new List<Frame>();
        for (int k = 0; k < n; k++) { float t = k / (float)n; f.Add(new(() => make(t), rate)); }
        return f;
    }

    static List<Frame> Pulse(Motion m, Func<float, List<Layer>> make, int n = 12, int rateFull = 5, int rateSub = 8)
    {
        if (m == Motion.Off) return new() { new(() => make(.5f), 0) };
        var f = new List<Frame>();
        int r = m == Motion.Full ? rateFull : rateSub;
        float amp = m == Motion.Full ? 1f : .5f;
        for (int k = 0; k < n; k++)
        {
            float p = .5f + .5f * amp * MathF.Sin(k / (float)n * MathF.Tau);
            f.Add(new(() => make(p), r));
        }
        return f;
    }

    // âââââââââ arrow (ECG + heartbeat LED) âââââââââ
    static readonly PointF[] NeonArrow =
    {
        new(3f, 2.6f), new(3f, 21.6f), new(7.4f, 17.4f), new(10.6f, 25f), new(13.2f, 23.9f), new(10.1f, 16.4f), new(16.1f, 16.4f)
    };
    // Tail-less, concave-based agent pointer; rounding turns it into smooth curves.
    static readonly PointF[] FluidArrow = { new(2.2f, 1.4f), new(4.9f, 23.6f), new(10.4f, 17.9f), new(20.4f, 16.6f) };

    static readonly PointF[] NeonEcg =
    {
        new(4.4f, 12.6f), new(5.8f, 12.6f), new(6.6f, 10.6f), new(7.6f, 14.6f), new(8.5f, 11.8f), new(9.2f, 12.6f), new(10.8f, 12.6f)
    };
    static readonly PointF[] FluidEcg =
    {
        new(5.6f, 13.2f), new(6.8f, 13.2f), new(7.5f, 11.2f), new(8.5f, 15.0f), new(9.4f, 12.4f), new(10f, 13.2f), new(12.4f, 13.2f)
    };

    List<Layer> Arrow(float led, float ecg, float sweep = 0, float sweepAmt = 0, float glowMul = 1)
    {
        var body = Body(glowMul);
        body.SweepPos = sweep; body.SweepAmt = sweepAmt;
        var L = new List<Layer> { new(body, g => FillPoly(g, fluid ? FluidArrow : NeonArrow, fluid ? .6f : 1.2f), K("arrow")) };

        if (!glass && !solid) // glass and solid keep the body clean; the heartbeat LED carries the pulse
        {
            var src = fluid ? FluidEcg : NeonEcg;
            int n = Math.Clamp((int)MathF.Round(ecg * src.Length), 2, src.Length);
            var pts = src.Take(n).ToArray();
            Style es;
            if (fluid) es = Ink(.85f);
            else { es = A(.5f, .8f); es.GlowR = .9f; es.Alpha = .92f; }
            L.Add(new(es, g => { using var p = RoundPen(.7f); g.DrawLines(p, pts); }));
        }

        var c = fluid ? new PointF(6.6f, 8.8f) : new PointF(5f, 7.4f);
        L.Add(new(Led(led), g => g.FillEllipse(W, c.X - .95f, c.Y - .95f, 1.9f, 1.9f)));
        return L;
    }

    List<Frame> ArrowFrames(Motion m)
    {
        var f = new List<Frame>();
        if (m == Motion.Off) { f.Add(new(() => Arrow(.5f, 1f), 0)); return f; }
        if (m == Motion.Subtle)
        {
            for (int k = 0; k < 8; k++) { float b = Beat(k / 8f * .6f); f.Add(new(() => Arrow(b, 1f), 4)); }
            f.Add(new(() => Arrow(0f, 1f), 200));
            return f;
        }
        for (int k = 0; k < 14; k++)
        {
            float t = k / 13f, b = Beat(t * .7f), sw = -2 + 34 * t, e = MathF.Min(1, .15f + t * 1.1f);
            f.Add(new(() => Arrow(b, e, sw, .55f, 1 + .25f * b), 3));
        }
        f.Add(new(() => Arrow(.1f, 1f), 140));
        return f;
    }

    // âââââââââ DNA spindle (loading) âââââââââ
    void Helix(List<Layer> L, PointF a, PointF b, float amp, float turns, float phase, int rungs, float bind, float w)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, len = MathF.Sqrt(dx * dx + dy * dy);
        float nx = -dy / len, ny = dx / len;

        PointF P(float u, int s, out float depth)
        {
            float th = MathF.Tau * turns * u + phase + s * MathF.PI;
            float o = MathF.Pow(MathF.Sin(MathF.PI * u), .7f) * amp * MathF.Sin(th); // spindle envelope
            depth = MathF.Cos(th);
            return new(a.X + dx * u + nx * o, a.Y + dy * u + ny * o);
        }

        var back = new List<(PointF, PointF)>();
        var front = new List<(PointF, PointF)>();
        var bound = new List<(PointF, PointF)>();
        const int seg = 36;
        for (int s = 0; s < 2; s++)
            for (int i = 0; i < seg; i++)
            {
                float u0 = i / (float)seg, u1 = (i + 1) / (float)seg;
                var p0 = P(u0, s, out _); var p1 = P(u1, s, out _);
                P((u0 + u1) / 2, s, out float dep);
                (dep > 0 ? front : back).Add((p0, p1));
            }
        for (int r = 0; r < rungs; r++)
        {
            float u = (r + .5f) / rungs;
            var pa = P(u, 0, out _); var pb = P(u, 1, out _);
            float ph = bind - u; ph -= MathF.Floor(ph);
            if (ph < .55f) bound.Add((pa, pb));                                              // base pair bound
            else { back.Add((pa, Lerp(pa, pb, .3f))); back.Add((pb, Lerp(pb, pa, .3f))); } // open stubs
        }

        L.Add(new(Dim(.55f), g => Lines(g, back, w * .85f)));
        if (bound.Count > 0)
            L.Add(new(A(.8f, .9f), g =>
            {
                Lines(g, bound, w * .7f);
                foreach (var (p, q) in bound) { var m = Lerp(p, q, .5f); g.FillEllipse(W, m.X - .75f, m.Y - .75f, 1.5f, 1.5f); }
            }));
        L.Add(new(Line(1f, 1.1f), g => Lines(g, front, w)));
    }

    List<Layer> Busy(float t)
    {
        var L = new List<Layer>();
        float a0 = t * 360f, r = 12.6f;
        L.Add(new(Dim(.75f), g => { using var p = new Pen(Color.White, .5f); g.DrawEllipse(p, 16 - r, 16 - r, 2 * r, 2 * r); }));
        L.Add(new(Line(.9f, 1.05f), g =>
        {
            using var p = RoundPen(fluid ? 1.5f : 1f);
            g.DrawArc(p, 16 - r, 16 - r, 2 * r, 2 * r, a0, 80);
            g.DrawArc(p, 16 - r, 16 - r, 2 * r, 2 * r, a0 + 180, 80);
        }));
        Helix(L, new(10.75f, 6.9f), new(21.25f, 25.1f), 4.6f, 1.25f, -t * MathF.Tau, 9, t, 1.15f);
        return L;
    }

    List<Layer> Working(float t)
    {
        var L = Arrow(.3f, 1f);
        float a0 = t * 360f, r = 6.1f; var c = new PointF(24.2f, 23.4f);
        L.Add(new(Dim(.7f), g => { using var p = new Pen(Color.White, .45f); g.DrawEllipse(p, c.X - r, c.Y - r, 2 * r, 2 * r); }));
        L.Add(new(Line(.9f, 1f), g => { using var p = RoundPen(fluid ? 1.3f : .9f); g.DrawArc(p, c.X - r, c.Y - r, 2 * r, 2 * r, a0, 110); }));
        Helix(L, new(c.X - 3.6f, c.Y + 2.2f), new(c.X + 3.6f, c.Y - 2.2f), 2.1f, 1f, -t * MathF.Tau, 4, t, .9f);
        return L;
    }

    // âââââââââ badges âââââââââ
    List<Layer> Help(float p)
    {
        var L = Arrow(.3f, 1f);
        L.Add(new(Badge(.6f + .8f * p), g => FillPoly(g, Poly(23.4f, 22f, 5.6f, 6, 30), .8f)));
        L.Add(new(Glyph(.4f + .5f * p), g =>
        {
            using var path = new GraphicsPath();
            using var ff = new FontFamily("Segoe UI");
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            path.AddString("?", ff, (int)FontStyle.Bold, 8.4f, new RectangleF(17.8f, 16.6f, 11.2f, 11.2f), sf);
            g.FillPath(W, path);
        }));
        return L;
    }

    List<Layer> Person(float p)
    {
        var L = Arrow(.3f, 1f);
        L.Add(new(Badge(.6f + .8f * p), g => g.FillEllipse(W, 23.4f - 5.8f, 22.2f - 5.8f, 11.6f, 11.6f)));
        L.Add(new(Glyph(.4f + .4f * p), g =>
        {
            g.FillEllipse(W, 23.4f - 1.7f, 18.6f - 1.7f, 3.4f, 3.4f);
            g.FillPie(W, 23.4f - 3.6f, 21.4f, 7.2f, 7.2f, 180, 180);
        }));
        return L;
    }

    // âââââââââ precision / text / pen / no âââââââââ
    List<Layer> Cross(float p)
    {
        float s = 1 + .18f * p;
        return new()
        {
            new(Line(.8f, 1f), g =>
            {
                using var pen = RoundPen(fluid ? 1.2f : .9f);
                g.DrawLine(pen, 16, 3.5f, 16, 12.5f); g.DrawLine(pen, 16, 19.5f, 16, 28.5f);
                g.DrawLine(pen, 3.5f, 16, 12.5f, 16); g.DrawLine(pen, 19.5f, 16, 28.5f, 16);
            }),
            new(A(.5f + .5f * p, .9f), g =>
            {
                using var pen = RoundPen(.8f);
                float d = 6.2f * s, k = 2.6f;
                foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    var c = new PointF(16 + sx * d, 16 + sy * d);
                    g.DrawLines(pen, new[] { new PointF(c.X, c.Y - sy * k), c, new PointF(c.X - sx * k, c.Y) });
                }
                g.FillEllipse(W, 16 - 1.1f, 16 - 1.1f, 2.2f, 2.2f);
            })
        };
    }

    List<Layer> IBeam(float p) => new()
    {
        new(solid ? SolidS(neon, 1, .5f) : glass ? GlassS(neon, .7f + .5f * p, .5f) : Line(.7f + .5f * p, 1.1f), g =>
        {
            using var pen = RoundPen(glass || solid ? 2.5f : fluid ? 1.7f : 1.3f);
            g.DrawLine(pen, 16, 5.6f, 16, 26.4f);
            g.DrawLine(pen, 12.6f, 5.2f, 19.4f, 5.2f);
            g.DrawLine(pen, 12.6f, 26.8f, 19.4f, 26.8f);
        }),
        new(A(.3f + .5f * p, .8f), g => g.FillEllipse(W, 16 - .9f, 16 - .9f, 1.8f, 1.8f))
    };

    List<Layer> PenL(float p) => new()
    {
        new(Body(.8f), g =>
        {
            var st = g.Save(); g.TranslateTransform(3f, 3f); g.RotateTransform(45);
            FillPoly(g, new PointF[] { new(0, 0), new(5.5f, -2.6f), new(25.5f, -2.6f), new(27f, 0), new(25.5f, 2.6f), new(5.5f, 2.6f) }, .9f);
            g.Restore(st);
        }, K("pen")),
        new(fluid ? Ink(.8f) : A(.5f + .8f * p, .9f), g =>
        {
            var st = g.Save(); g.TranslateTransform(3f, 3f); g.RotateTransform(45);
            g.FillRectangle(W, 7.2f, -2.1f, 1.6f, 4.2f);
            g.Restore(st);
        }),
        new(Led(p), g => { var st = g.Save(); g.TranslateTransform(3f, 3f); g.RotateTransform(45); g.FillEllipse(W, 10.6f, -.8f, 1.6f, 1.6f); g.Restore(st); })
    };

    List<Layer> No(float p)
    {
        var s = solid ? SolidS(alert, 1, .4f) : glass ? GlassS(alert, .7f + .4f * p, .4f) : fluid
            ? new Style { Edge = White, Fill = alert, Glow = glow * (.4f + .4f * p), GlowR = 1.4f, EdgeW = 1.1f, Shadow = .45f, Grad = .25f, Hot = .1f }
            : new Style { Edge = alert, Fill = core, Glow = glow * (.7f + .6f * p), EdgeW = 1.1f };
        return new()
        {
            new(s, g =>
            {
                using var pen = new Pen(Color.White, fluid ? 3f : 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawEllipse(pen, 16 - 8.6f, 16 - 8.6f, 17.2f, 17.2f);
                g.DrawLine(pen, 16 - 6.1f, 16 - 6.1f, 16 + 6.1f, 16 + 6.1f);
            })
        };
    }

    // âââââââââ resize / move / alternate âââââââââ
    static readonly PointF[] Dbl =
    {
        new(3.6f, 16), new(9.4f, 10.6f), new(9.4f, 14.3f), new(22.6f, 14.3f), new(22.6f, 10.6f),
        new(28.4f, 16), new(22.6f, 21.4f), new(22.6f, 17.7f), new(9.4f, 17.7f), new(9.4f, 21.4f)
    };

    static PointF[] Push(PointF[] src, float push) => src.Select(q => new PointF(q.X + (q.X < 16 ? -push : push), q.Y)).ToArray();

    List<Layer> Resize(float angle, float push)
    {
        var pts = Push(Dbl, push);
        return new()
        {
            new(Body(), g =>
            {
                var st = g.Save();
                g.TranslateTransform(16, 16); g.RotateTransform(angle); g.TranslateTransform(-16, -16);
                FillPoly(g, pts, 1f);
                g.Restore(st);
            }, K($"rs{angle}:{push:0.000}")),
            new(fluid ? Led(.6f) : A(.7f, .9f), g => g.FillEllipse(W, 16 - 1.15f, 16 - 1.15f, 2.3f, 2.3f))
        };
    }

    static readonly PointF[] Dbl4 =
    {
        new(3.2f, 16), new(8.2f, 11.4f), new(8.2f, 14.5f), new(23.8f, 14.5f), new(23.8f, 11.4f),
        new(28.8f, 16), new(23.8f, 20.6f), new(23.8f, 17.5f), new(8.2f, 17.5f), new(8.2f, 20.6f)
    };

    List<Layer> Move(float p)
    {
        var pts = Push(Dbl4, .8f * p);
        var hub = fluid ? Badge(.6f) : A(.7f + .4f * p, .9f);
        return new()
        {
            new(Body(), g =>
            {
                FillPoly(g, pts, 1f);
                var st = g.Save();
                g.TranslateTransform(16, 16); g.RotateTransform(90); g.TranslateTransform(-16, -16);
                FillPoly(g, pts, 1f);
                g.Restore(st);
            }, K($"mv{p:0.000}")),
            new(hub, g => FillPoly(g, Poly(16, 16, 2.4f, 6, 30), .4f))
        };
    }

    static readonly PointF[] Up =
    {
        new(16, 2.8f), new(23.4f, 11.2f), new(18.4f, 11.2f), new(18.4f, 28.6f), new(13.6f, 28.6f), new(13.6f, 11.2f), new(8.6f, 11.2f)
    };

    List<Layer> UpL(float sweep, float amt, float led)
    {
        var s = Body(); s.SweepPos = sweep; s.SweepAmt = amt; s.SweepX = 0; s.SweepY = -1;
        return new()
        {
            new(s, g => FillPoly(g, Up, 1f), K("up")),
            new(Led(led), g => g.FillEllipse(W, 16 - .95f, 20f - .95f, 1.9f, 1.9f))
        };
    }

    List<Frame> UpFrames(Motion m)
    {
        if (m == Motion.Off) return new() { new(() => UpL(0, 0, .5f), 0) };
        if (m == Motion.Subtle) return Pulse(m, p => UpL(0, 0, p), 12, 5, 8);
        var f = new List<Frame>();
        for (int k = 0; k < 12; k++) { float t = k / 11f, sw = -32 + 34 * t, b = Beat(t * .7f); f.Add(new(() => UpL(sw, .55f, b), 3)); }
        f.Add(new(() => UpL(0, 0, .1f), 120));
        return f;
    }

    // âââââââââ link hand âââââââââ
    static void HandShape(Graphics g)
    {
        Rr(g, 10.6f, 2.4f, 3.8f, 15f, 1.9f);    // index
        Rr(g, 14.2f, 10.8f, 3.4f, 8f, 1.6f);    // middle
        Rr(g, 17.4f, 11.8f, 3.4f, 7.6f, 1.6f);  // ring
        Rr(g, 20.6f, 13.4f, 3.2f, 6.6f, 1.5f);  // pinky
        Rr(g, 9.4f, 15.6f, 14.6f, 12.4f, 4.2f); // palm
        var st = g.Save();
        g.TranslateTransform(10.4f, 21f); g.RotateTransform(-38);
        Rr(g, -1.7f, -8.2f, 3.6f, 9f, 1.7f);    // thumb
        g.Restore(st);
    }

    List<Layer> Hand(float led, float tap)
    {
        var L = new List<Layer> { new(Body(), HandShape, K("hand")) };
        Style gs;
        if (glass || solid) gs = new() { Edge = White, Fill = White, EdgeW = .6f, Glow = 0, Hot = 0, Inner = 0, Alpha = .45f };
        else if (fluid) { gs = Ink(.65f); gs.Alpha = .55f; }
        else { gs = Dim(.25f); gs.Glow = 0; gs.EdgeW = .7f; }
        L.Add(new(gs, g =>
        {
            using var p = RoundPen(.45f);
            g.DrawLine(p, 14.3f, 13f, 14.3f, 17.8f); g.DrawLine(p, 17.5f, 14f, 17.5f, 17.8f); g.DrawLine(p, 20.7f, 15.4f, 20.7f, 17.8f);
        }));
        L.Add(new(Led(led), g => g.FillEllipse(W, 17.4f - 1.05f, 22.6f - 1.05f, 2.1f, 2.1f)));
        if (tap > 0 && tap < 1)
        {
            float r = 1.6f + 2.4f * tap;
            var ts = A(.8f, .8f); ts.Alpha = 1 - tap;
            L.Add(new(ts, g => { using var p = new Pen(Color.White, .6f); g.DrawEllipse(p, 12.5f - r, 4.4f - r, 2 * r, 2 * r); }));
        }
        return L;
    }

    List<Frame> HandFrames(Motion m)
    {
        var f = new List<Frame>();
        if (m == Motion.Off) { f.Add(new(() => Hand(.5f, 0), 0)); return f; }
        int n = m == Motion.Full ? 10 : 8;
        for (int k = 0; k < n; k++)
        {
            float t = k / (float)n, b = Beat(t * .7f), tap = m == Motion.Full ? t : 0;
            f.Add(new(() => Hand(b, tap), m == Motion.Full ? 3 : 4));
        }
        f.Add(new(() => Hand(.1f, 0), m == Motion.Full ? 100 : 200));
        return f;
    }

    // âââââââââ location pin (benzene nucleus + radar) âââââââââ
    List<Layer> Pin(float radar)
    {
        var L = new List<Layer>();
        float baseY = fluid ? 26.2f : 27.6f;
        if (radar > 0 && radar < 1)
        {
            float r = 1.5f + 5f * radar;
            var rs = A(.6f, .8f); rs.Alpha = 1 - radar;
            L.Add(new(rs, g => { using var p = new Pen(Color.White, .6f); g.DrawEllipse(p, 16 - r, baseY - r * .42f, 2 * r, r * .84f); }));
        }
        L.Add(new(Body(), g =>
        {
            g.FillEllipse(W, 16 - 6.8f, 11 - 6.8f, 13.6f, 13.6f);
            g.FillPolygon(W, new PointF[] { new(16 - 6.2f, 13.8f), new(16 + 6.2f, 13.8f), new(16, 27.6f) });
        }, K("pin")));
        var ns = fluid ? Badge(.8f) : A(.9f, 1f);
        if (!fluid) ns.Fill = core;
        L.Add(new(ns, g => FillPoly(g, Poly(16, 11, 2.9f, 6, 30), .5f)));
        return L;
    }

    List<Frame> PinFrames(Motion m)
    {
        if (m == Motion.Off) return new() { new(() => Pin(0), 0) };
        var f = new List<Frame>();
        for (int k = 1; k <= 14; k++) { float t = k / 15f; f.Add(new(() => Pin(t), m == Motion.Full ? 3 : 4)); }
        f.Add(new(() => Pin(0), m == Motion.Full ? 60 : 160));
        return f;
    }

    // âââââââââ registry âââââââââ
    public List<CursorDef> All()
    {
        float ax = 2.6f, ay = 2.4f;
        return new()
        {
            new("Arrow",       "arrow",       "Normal",   ax, ay, ArrowFrames),
            new("Help",        "help",        "Help",     ax, ay, m => Pulse(m, Help)),
            new("AppStarting", "working",     "Working",  ax, ay, _ => Loop(18, 3, Working)),
            new("Wait",        "busy",        "DNA busy", 16f, 16f, _ => Loop(24, 3, Busy)),
            new("Crosshair",   "precision",   "Precise",  16f, 16f, m => Pulse(m, Cross, 16, 4, 7)),
            new("IBeam",       "text",        "Text",     16f, 16f, m => Pulse(m, IBeam, 12, 6, 9)),
            new("NWPen",       "handwriting", "Pen",      fluid ? 3.8f : 3.2f, fluid ? 3.8f : 3.2f, m => Pulse(m, PenL, 12, 5, 8)),
            new("No",          "unavailable", "Blocked",  16f, 16f, m => Pulse(m, No)),
            new("SizeNS",      "resize-ns",   "Resize V", 16f, 16f, m => Pulse(m, p => Resize(90, .9f * p), 12, 4, 7)),
            new("SizeWE",      "resize-we",   "Resize H", 16f, 16f, m => Pulse(m, p => Resize(0, .9f * p), 12, 4, 7)),
            new("SizeNWSE",    "resize-nwse", "Diag 1",   16f, 16f, m => Pulse(m, p => Resize(45, .9f * p), 12, 4, 7)),
            new("SizeNESW",    "resize-nesw", "Diag 2",   16f, 16f, m => Pulse(m, p => Resize(-45, .9f * p), 12, 4, 7)),
            new("SizeAll",     "move",        "Move",     16f, 16f, m => Pulse(m, Move, 12, 4, 7)),
            new("UpArrow",     "alternate",   "Alt",      16f, fluid ? 3.4f : 3f, UpFrames),
            new("Hand",        "link",        "Link",     12.4f, 2.6f, HandFrames),
            new("Pin",         "location",    "Location", 16f, fluid ? 26.1f : 27.2f, PinFrames),
            new("Person",      "person",      "Person",   ax, ay, m => Pulse(m, Person)),
        };
    }

    public List<Layer> IconLayers() => Arrow(.8f, 1f);

    // âââââââââ brand mark: the segmented M âââââââââ
    // Bold rounded segments (the myelin sheath) spell an M; the gaps between them are the nodes of Ranvier
    // (negative space). The centre chevron is a pointer tip landing on one node: the signal, and your click.
    // One idea, no hairlines: it holds at 16 px and works as a single-colour glyph.
    static void MarkStrokes(Graphics g)
    {
        // One continuous M (8.4,23.4)→(8.4,9)→(16,18.2)→(23.6,9)→(23.6,23.4), split into three rounded
        // myelin segments by two gaps (nodes of Ranvier) in the legs; the centre chevron is the pointer.
        using var p = RoundPen(4f);
        g.DrawLine(p, 8.4f, 23.4f, 8.4f, 19.8f);                                   // left foot segment
        g.DrawLines(p, new PointF[] { new(8.4f, 15f), new(8.4f, 9f), new(16f, 18.2f), new(23.6f, 9f), new(23.6f, 15f) }); // the M and its pointer chevron
        g.DrawLine(p, 23.6f, 19.8f, 23.6f, 23.4f);                                 // right foot segment
        g.FillEllipse(W, 16f - 2f, 22.8f - 2f, 4f, 4f);                            // the point the pointer lands on: your click
    }

    /// <summary>The signal jumping node to node (saltatory conduction).</summary>
    static void MarkNodes(Graphics g)
    {
        g.FillEllipse(W, 8.4f - .9f, 17.4f - .9f, 1.8f, 1.8f);
        g.FillEllipse(W, 23.6f - .9f, 17.4f - .9f, 1.8f, 1.8f);
    }

    static readonly Rgb Cyan = new(.18f, .85f, .97f), Indigo = new(.23f, .27f, .96f);

    /// <summary>App icon. Colour = white mark on a cyanâindigo squircle; dark = gradient mark on black.</summary>
    public static List<Layer> LogoLayers(bool dark = false)
    {
        var bg = dark
            ? new Style { Fill = new Rgb(.11f, .11f, .12f), Fill2 = new Rgb(0, 0, 0), Edge = new Rgb(.24f, .24f, .26f), EdgeW = .6f, Glow = 0, Inner = 0, Hot = 0, Grad = 0 }
            : new Style { Fill = Cyan, Fill2 = Indigo, Edge = Cyan.Mix(White, .35f), EdgeW = .6f, Glow = 0, Inner = 0, Hot = .2f, Grad = 0 };
        var mark = dark
            ? new Style { Fill = Cyan, Fill2 = Indigo.Mix(White, .2f), Edge = Cyan.Mix(White, .3f), EdgeW = .5f, Glow = 0, Inner = 0, Hot = 0, Grad = 0, Shadow = .35f, ShadowX = 0, ShadowY = .7f, ShadowR = 1f }
            : new Style { Fill = White, Fill2 = new Rgb(.86f, .91f, 1f), Edge = White, EdgeW = .5f, Glow = 0, Inner = 0, Hot = 0, Grad = 0, Shadow = .4f, ShadowX = 0, ShadowY = .75f, ShadowR = 1.1f };
        return new()
        {
            new(bg, g => Rr(g, 1.2f, 1.2f, 29.6f, 29.6f, 7.2f), dark ? "logo-bg-dark" : "logo-bg"),
            new(mark, MarkStrokes, "logo-mark4"),
            new(new Style { Fill = new Rgb(1f, .84f, .04f), Edge = new Rgb(1f, .9f, .4f), EdgeW = .4f, Glow = 0, Inner = 0, Hot = 0, Grad = 0 }, MarkNodes, "logo-nodes"),
        };
    }

    /// <summary>The mark alone in one solid colour (for light/dark/tinted uses).</summary>
    public static List<Layer> MarkOnly(Rgb color) => new()
    {
        new(new Style { Fill = color, Edge = color, EdgeW = .4f, Glow = 0, Inner = 0, Hot = 0, Grad = 0 }, MarkStrokes, "logo-mark4"),
        new(new Style { Fill = color, Edge = color, EdgeW = .4f, Glow = 0, Inner = 0, Hot = 0, Grad = 0 }, MarkNodes, "logo-nodes")
    };

    // Retired v3.0 mark (glass pointer with node bands), kept for reference renders.
    static readonly PointF[] LogoArrow = { new(9f, 6f), new(11.2f, 25.6f), new(16f, 20.6f), new(25.6f, 19.2f) };

    public static List<Layer> LegacyLogoLayers()
    {
        var bg = new Style { Fill = new Rgb(.11f, .11f, .12f), Fill2 = new Rgb(0, 0, 0), Edge = new Rgb(.23f, .23f, .24f), EdgeW = .7f, Glow = 0, Inner = 0, Hot = 0, Grad = 0 };
        var body = new Style { Glass = true, Fill = new Rgb(.37f, .91f, 1f), Fill2 = new Rgb(.18f, .42f, 1f), GlassAlpha = .62f, Glow = 0, Shadow = .6f, ShadowX = .5f, ShadowY = 1.1f, ShadowR = 1.4f, Round = 1.3f };
        var node = new Style { Edge = new Rgb(.04f, .04f, .05f), Fill = new Rgb(.04f, .04f, .05f), EdgeW = 1f, Glow = 0, Hot = 0, Inner = 0, Grad = 0 };
        var spark = new Style { Edge = new Rgb(1f, .84f, .04f), Fill = new Rgb(1f, .84f, .04f), EdgeW = .6f, Glow = .25f, GlowR = .8f, Hot = .5f, Inner = 0 };
        // Nodes of Ranvier: two gaps across the pointer's axis; the signal jumps between them (saltatory conduction).
        PointF n1 = new(11.87f, 11.27f), n2 = new(13.88f, 14.96f);
        float nx = -.878f * 6, ny = .478f * 6;
        return new()
        {
            new(bg, g => Rr(g, 1.2f, 1.2f, 29.6f, 29.6f, 7.4f), "myelin-bg"),
            new(body, g => FillPoly(g, LogoArrow, .6f), "myelin-body"),
            new(node, g =>
            {
                using var clip = new GraphicsPath();
                clip.AddPolygon(LogoArrow);
                g.SetClip(clip);
                using var p = RoundPen(.95f);
                g.DrawLine(p, n1.X - nx, n1.Y - ny, n1.X + nx, n1.Y + ny);
                g.DrawLine(p, n2.X - nx, n2.Y - ny, n2.X + nx, n2.Y + ny);
                g.ResetClip();
            }, "myelin-nodes"),
            new(spark, g => { g.FillEllipse(W, n1.X - .7f, n1.Y - .7f, 1.4f, 1.4f); g.FillEllipse(W, n2.X - .7f, n2.Y - .7f, 1.4f, 1.4f); }),
        };
    }
}
