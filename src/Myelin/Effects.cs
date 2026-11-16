using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Myelin;

/// <summary>
/// Bio-AI pointer effects (screen pixels):
///  trails  — comet star shedding ferrocene molecules · DNA helix ribbon · neural constellation · ECG pulse · agent glide halo
///  clicks  — Synapse (neural fire) · Molecule (benzene) · Atom (orbitals) · Mitosis (cell division) · Ripple
///  extras  — double-click mitosis · drag tether · idle orbit ("thinking") halo
/// </summary>
public sealed class Effects
{
    struct Pt { public float X, Y, T; }
    sealed class Fc { public float X, Y, Vx, Vy, Age, Life, Spin, Rot, Tilt, Size; }
    sealed class Spark { public float X, Y, Vx, Vy, Age, Life, Size; public Color C; }
    sealed class Node { public float X, Y, Age, Life; }
    sealed class Burst
    {
        public float X, Y, Age, Life, Axis; public ClickStyle Style; public Color C, C2;
        public float[] Ang = Array.Empty<float>(), Len = Array.Empty<float>();
    }

    readonly List<Pt> trail = new();
    readonly List<Fc> fcs = new();
    readonly List<Spark> sparks = new();
    readonly List<Node> nodes = new();
    readonly List<Burst> bursts = new();
    readonly Random rng = new();
    readonly ImageAttributes[] ia = new ImageAttributes[17];
    readonly Bitmap gWhite;
    Bitmap gNeon, gAccent;
    Color neon, accent;
    float energy, time, lastX = float.NaN, lastY, acc, nodeAcc, speed;
    float gx, gy, gvx, gvy, gAlpha; bool gInit;          // glide spring
    bool lHeld, dragging; float downX, downY;            // drag tether
    float idleT, idleFade;                               // idle orbit
    float lastDownT = -9, lastDownX, lastDownY;          // double-click
    static readonly float DoubleTime = SystemInformation.DoubleClickTime / 1000f;

    public Settings S { get; private set; }
    public float K = 1f; // DPI scale

    public Effects(Settings s)
    {
        for (int i = 0; i < ia.Length; i++)
        {
            ia[i] = new ImageAttributes();
            ia[i].SetColorMatrix(new ColorMatrix { Matrix33 = i / 16f });
        }
        gWhite = Sprite(Color.White);
        Apply(s);
    }

    public void Apply(Settings s)
    {
        S = s;
        neon = s.Palette.NeonC; accent = s.Palette.AccentC;
        energy = Math.Clamp(s.Palette.Energy, .2f, 1.6f);
        gNeon?.Dispose(); gAccent?.Dispose();
        gNeon = Sprite(neon); gAccent = Sprite(accent);
    }

    public bool Idle => trail.Count == 0 && fcs.Count == 0 && sparks.Count == 0 && bursts.Count == 0 && nodes.Count == 0
                        && !dragging && idleFade <= 0 && gAlpha <= .01f;

    public void Reset()
    {
        trail.Clear(); fcs.Clear(); sparks.Clear(); bursts.Clear(); nodes.Clear();
        lastX = float.NaN; speed = 0; acc = 0; nodeAcc = 0; gAlpha = 0; gInit = false;
        lHeld = dragging = false; idleT = 0; idleFade = 0;
    }

    float R() => (float)rng.NextDouble();
    static float Dist(float ax, float ay, float bx, float by) => MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    static float Approach(float a, float b, float step) => a < b ? MathF.Min(b, a + step) : MathF.Max(b, a - step);

    // ───────── input ─────────
    public void Press(float x, float y, int button)
    {
        if (button == 0)
        {
            bool dbl = time - lastDownT < DoubleTime && Dist(x, y, lastDownX, lastDownY) < 8 * K;
            lastDownT = dbl ? -9 : time; lastDownX = x; lastDownY = y;
            lHeld = true; downX = x; downY = y; dragging = false;
            if (dbl && S.DoubleClickMitosis) { AddBurst(x, y, ClickStyle.Mitosis, neon, accent); return; }
        }
        Click(x, y, button);
    }

    public void Release(int button)
    {
        if (button == 0) { lHeld = false; dragging = false; }
    }

    public void Click(float x, float y, int button)
    {
        var st = button == 0 ? S.LeftClick : button == 1 ? S.RightClick : ClickStyle.Ripple;
        if (st == ClickStyle.Off) return;
        Color c = button == 1 ? accent : neon, c2 = button == 1 ? neon : accent;
        AddBurst(x, y, st, c, c2);
        int ns = (int)(8 * energy) + (st == ClickStyle.Ripple ? 0 : 4);
        for (int i = 0; i < ns; i++)
        {
            float a = R() * MathF.Tau, v = (140 + R() * 240) * K;
            sparks.Add(new Spark
            {
                X = x, Y = y, Vx = MathF.Cos(a) * v, Vy = MathF.Sin(a) * v,
                Life = .3f + R() * .35f, Size = 1.2f + R() * 1.4f, C = i % 3 == 0 ? c2 : c
            });
        }
    }

    void AddBurst(float x, float y, ClickStyle st, Color c, Color c2)
    {
        var b = new Burst
        {
            X = x, Y = y, Style = st, C = c, C2 = c2, Axis = R() * MathF.PI,
            Life = st switch { ClickStyle.Molecule => .8f, ClickStyle.Synapse => .7f, ClickStyle.Atom => .95f, ClickStyle.Mitosis => 1.05f, _ => .55f }
        };
        if (st == ClickStyle.Synapse)
        {
            const int n = 7;
            b.Ang = new float[n]; b.Len = new float[n];
            float o = R() * MathF.Tau;
            for (int i = 0; i < n; i++)
            {
                b.Ang[i] = o + i * MathF.Tau / n + (R() - .5f) * .5f;
                b.Len[i] = (26 + R() * 22) * (.75f + .25f * energy);
            }
        }
        bursts.Add(b);
    }

    public void Update(float x, float y, float dt)
    {
        time += dt;
        float d = 0;
        bool moved = true;
        if (!float.IsNaN(lastX))
        {
            float dx = x - lastX, dy = y - lastY;
            d = MathF.Sqrt(dx * dx + dy * dy);
            moved = d > .5f;
            speed = speed * .7f + (dt > 0 ? d / dt : 0) * .3f;
            if (S.Trail == TrailStyle.Comet && S.Ferrocenes && moved)
            {
                acc += d;
                float gap = 54f * K / energy;
                while (acc > gap) { acc -= gap; SpawnFc(x, y, dx / d, dy / d); }
            }
            if (S.Trail == TrailStyle.Neural && moved)
            {
                nodeAcc += d;
                float gap = 26f * K / MathF.Sqrt(energy);
                while (nodeAcc > gap)
                {
                    nodeAcc -= gap;
                    nodes.Add(new Node { X = x + (R() - .5f) * 14 * K, Y = y + (R() - .5f) * 14 * K, Life = .9f + .5f * energy });
                }
            }
        }
        lastX = x; lastY = y;

        // trail history
        if (S.Trail != TrailStyle.Off && (trail.Count == 0 || trail[^1].X != x || trail[^1].Y != y))
            trail.Add(new Pt { X = x, Y = y, T = time });
        float maxAge = .16f + .2f * energy;
        while (trail.Count > 0 && time - trail[0].T > maxAge) trail.RemoveAt(0);
        if (trail.Count > 64) trail.RemoveRange(0, trail.Count - 64);

        // idle "thinking" orbit
        idleT = moved || lHeld ? 0 : idleT + dt;
        float idleTarget = S.IdleOrbit && idleT > 1.2f && idleT < 7f ? 1 : 0;
        idleFade = Approach(idleFade, idleTarget, dt * 3f);

        // agent glide: critically damped spring halo
        if (S.Trail == TrailStyle.Glide)
        {
            if (!gInit) { gx = x; gy = y; gvx = gvy = 0; gInit = true; }
            const float k = 140f, c = 23.66f;
            gvx += ((x - gx) * k - gvx * c) * dt; gvy += ((y - gy) * k - gvy * c) * dt;
            gx += gvx * dt; gy += gvy * dt;
            float target = Math.Clamp(Dist(gx, gy, x, y) / (6 * K) + speed / (700 * K), 0, 1);
            gAlpha = Approach(gAlpha, target, dt * (target > gAlpha ? 8f : 2.2f));
        }
        else { gAlpha = 0; gInit = false; }

        // drag tether
        if (lHeld && !dragging && S.DragTether && Dist(x, y, downX, downY) > 10 * K) dragging = true;

        // particles
        float damp = MathF.Pow(.08f, dt);
        for (int i = fcs.Count - 1; i >= 0; i--)
        {
            var f = fcs[i]; f.Age += dt;
            if (f.Age > f.Life) { fcs.RemoveAt(i); continue; }
            f.Vx *= damp; f.Vy *= damp; f.Vy -= 6 * dt * K;
            f.X += f.Vx * dt; f.Y += f.Vy * dt; f.Rot += f.Spin * dt;
        }
        if (fcs.Count > 40) fcs.RemoveRange(0, fcs.Count - 40);

        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            nodes[i].Age += dt;
            if (nodes[i].Age > nodes[i].Life) nodes.RemoveAt(i);
        }
        if (nodes.Count > 60) nodes.RemoveRange(0, nodes.Count - 60);

        float sd = MathF.Pow(.02f, dt);
        for (int i = sparks.Count - 1; i >= 0; i--)
        {
            var s = sparks[i]; s.Age += dt;
            if (s.Age > s.Life) { sparks.RemoveAt(i); continue; }
            s.Vx *= sd; s.Vy *= sd; s.X += s.Vx * dt; s.Y += s.Vy * dt;
        }
        for (int i = bursts.Count - 1; i >= 0; i--)
        {
            bursts[i].Age += dt;
            if (bursts[i].Age > bursts[i].Life) bursts.RemoveAt(i);
        }
    }

    void SpawnFc(float x, float y, float ux, float uy)
    {
        float side = R() < .5f ? -1 : 1, px = -uy * side, py = ux * side;
        float spread = 25 + R() * 35;
        fcs.Add(new Fc
        {
            X = x - ux * 10 * K + px * 6 * K, Y = y - uy * 10 * K + py * 6 * K,
            Vx = (-ux * 30 + px * spread) * K, Vy = (-uy * 30 + py * spread) * K,
            Life = .9f + R() * .5f, Spin = (2 + R() * 2.5f) * side, Rot = R() * MathF.Tau,
            Tilt = (R() - .5f) * 70, Size = 6.5f + R() * 2.5f
        });
    }

    public Rectangle Bounds()
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        void Add(float x, float y, float r)
        {
            if (x - r < x0) x0 = x - r; if (y - r < y0) y0 = y - r;
            if (x + r > x1) x1 = x + r; if (y + r > y1) y1 = y + r;
        }
        foreach (var p in trail) Add(p.X, p.Y, 36 * K);
        foreach (var f in fcs) Add(f.X, f.Y, f.Size * K * 4f);
        foreach (var n in nodes) Add(n.X, n.Y, 20 * K);
        foreach (var s in sparks) Add(s.X, s.Y, 12 * K);
        foreach (var b in bursts) Add(b.X, b.Y, 84 * K);
        if (gAlpha > .01f) { Add(gx, gy, 40 * K); Add(lastX, lastY, 8 * K); }
        if (dragging) { Add(downX, downY, 18 * K); Add(lastX, lastY, 18 * K); }
        if (idleFade > 0 && !float.IsNaN(lastX)) Add(lastX, lastY, 34 * K);
        if (x0 > x1) return Rectangle.Empty;
        return Rectangle.FromLTRB((int)MathF.Floor(x0), (int)MathF.Floor(y0), (int)MathF.Ceiling(x1), (int)MathF.Ceiling(y1));
    }

    // ───────── drawing ─────────
    public void Draw(Graphics g)
    {
        switch (S.Trail)
        {
            case TrailStyle.Comet: DrawComet(g); break;
            case TrailStyle.Helix: DrawHelix(g); break;
            case TrailStyle.Neural: DrawNeural(g); break;
            case TrailStyle.Pulse: DrawPulse(g); break;
            case TrailStyle.Glide: DrawGlide(g); break;
        }
        if (dragging) DrawTether(g);
        foreach (var f in fcs) DrawFc(g, f);
        foreach (var b in bursts) DrawBurst(g, b);
        foreach (var s in sparks) DrawSpark(g, s);
        if (idleFade > 0) DrawIdle(g);
        if (S.Trail == TrailStyle.Comet) DrawHead(g);
    }

    static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    static Color A(float a, Color c) => Color.FromArgb(Math.Clamp((int)(a * 255), 0, 255), c);

    static unsafe Bitmap Sprite(Color c)
    {
        const int n = 64;
        var b = new Bitmap(n, n, PixelFormat.Format32bppPArgb);
        var d = b.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        for (int y = 0; y < n; y++)
        {
            byte* row = (byte*)d.Scan0 + y * d.Stride;
            for (int x = 0; x < n; x++)
            {
                float dx = (x + .5f) / n * 2 - 1, dy = (y + .5f) / n * 2 - 1, r2 = dx * dx + dy * dy;
                float a = r2 >= 1 ? 0 : MathF.Exp(-r2 * 4f) * (1 - r2);
                row[x * 4] = (byte)(c.B * a); row[x * 4 + 1] = (byte)(c.G * a);
                row[x * 4 + 2] = (byte)(c.R * a); row[x * 4 + 3] = (byte)(255 * a);
            }
        }
        b.UnlockBits(d);
        return b;
    }

    Bitmap Spr(Color c) => c.ToArgb() == accent.ToArgb() ? gAccent : gNeon;

    void Glow(Graphics g, Bitmap spr, float x, float y, float r, float a)
    {
        int i = (int)MathF.Round(Math.Clamp(a, 0, 1) * 16);
        if (i == 0 || r < 1) return;
        g.DrawImage(spr, new Rectangle((int)(x - r), (int)(y - r), (int)(2 * r), (int)(2 * r)), 0, 0, 64, 64, GraphicsUnit.Pixel, ia[i]);
    }

    static void Dot(Graphics g, float x, float y, float r, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillEllipse(b, x - r, y - r, 2 * r, 2 * r);
    }

    void Ring(Graphics g, Pen pen, float x, float y, float r, Color c, float a, float w)
    {
        pen.Width = w * 3.2f * K; pen.Color = A(.25f * a, c); g.DrawEllipse(pen, x - r, y - r, 2 * r, 2 * r);
        pen.Width = w * K; pen.Color = A(.9f * a, Mix(c, Color.White, .3f)); g.DrawEllipse(pen, x - r, y - r, 2 * r, 2 * r);
    }

    static Pen NewPen() => new(Color.White) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    /// <summary>Shooting star: tail fades gold → neon → white-hot head.</summary>
    void DrawComet(Graphics g)
    {
        int n = trail.Count;
        if (n < 2) return;
        using var pen = NewPen();
        for (int pass = 0; pass < 2; pass++)
            for (int i = 1; i < n; i++)
            {
                float f = i / (float)(n - 1);
                var a = trail[i - 1]; var b = trail[i];
                float w = (1.2f + 6.5f * f * f) * K * (.7f + .3f * energy);
                var c = Mix(accent, neon, f * 1.4f);
                if (pass == 0) { pen.Color = A(.28f * f, c); pen.Width = w * 2.8f; }
                else { pen.Color = A(.92f * f, Mix(c, Color.White, .5f * f * f)); pen.Width = w * .55f; }
                g.DrawLine(pen, a.X, a.Y, b.X, b.Y);
            }
    }

    void DrawHead(Graphics g)
    {
        if (trail.Count == 0) return;
        var h = trail[^1];
        float s = Math.Clamp((speed / K - 120) / 900f, 0, 1);
        if (s <= .02f) return;
        Glow(g, gNeon, h.X, h.Y, 30 * K * (.5f + .6f * s), .9f * s);
        Glow(g, gWhite, h.X, h.Y, 10 * K * (.6f + .5f * s), .9f * s);
        float r = 11 * K * (.5f + .6f * s), rot = time * 2.4f;
        var p = new PointF[8];
        for (int i = 0; i < 8; i++)
        {
            float a = rot + i * MathF.PI / 4, rr = i % 2 == 0 ? r : r * .18f;
            p[i] = new(h.X + rr * MathF.Cos(a), h.Y + rr * MathF.Sin(a));
        }
        using var br = new SolidBrush(A(s, Color.White));
        g.FillPolygon(br, p);
    }

    /// <summary>Ferrocene Fe(C5H5)2: two flattened, counter-rotating Cp pentagons around a glowing Fe atom.</summary>
    void DrawFc(Graphics g, Fc f)
    {
        float life = f.Age / f.Life;
        float a = Math.Clamp(life < .15f ? life / .15f : 1 - (life - .15f) / .85f, 0, 1);
        float s = f.Size * K * (1 + .25f * life), h = s * .62f;
        Glow(g, gNeon, f.X, f.Y, s * 3.2f, .45f * a);

        var st = g.Save();
        g.TranslateTransform(f.X, f.Y);
        g.RotateTransform(f.Tilt);
        var top = Pent(0, -h, s, f.Rot);
        var bot = Pent(0, h, s, -f.Rot + .63f); // staggered conformation
        using (var pg = new Pen(A(.35f * a, neon), 2.6f * K) { LineJoin = LineJoin.Round })
        { g.DrawPolygon(pg, top); g.DrawPolygon(pg, bot); }
        using (var pc = new Pen(A(.94f * a, Mix(neon, Color.White, .35f)), 1.1f * K) { LineJoin = LineJoin.Round })
        { g.DrawPolygon(pc, top); g.DrawPolygon(pc, bot); }
        using (var pb = new Pen(A(.5f * a, accent), .8f * K))
        { g.DrawLine(pb, 0, 0, 0, -h); g.DrawLine(pb, 0, 0, 0, h); }
        g.Restore(st);

        Glow(g, gAccent, f.X, f.Y, s * 1.6f, .9f * a);
        Dot(g, f.X, f.Y, s * .22f, A(a, Mix(accent, Color.White, .4f)));
    }

    static PointF[] Pent(float cx, float cy, float r, float rot)
    {
        var p = new PointF[5];
        for (int i = 0; i < 5; i++)
        {
            float a = rot + i * MathF.Tau / 5;
            p[i] = new(cx + r * MathF.Cos(a), cy + r * .38f * MathF.Sin(a));
        }
        return p;
    }

    List<PointF> Resample(float step)
    {
        var r = new List<PointF>();
        if (trail.Count == 0) return r;
        r.Add(new(trail[0].X, trail[0].Y));
        float carry = 0;
        for (int i = 1; i < trail.Count && r.Count < 200; i++)
        {
            float ax = trail[i - 1].X, ay = trail[i - 1].Y, bx = trail[i].X, by = trail[i].Y;
            float d = Dist(ax, ay, bx, by);
            float t = step - carry;
            while (t <= d) { r.Add(new(ax + (bx - ax) * t / d, ay + (by - ay) * t / d)); t += step; }
            carry = d - (t - step);
        }
        r.Add(new(trail[^1].X, trail[^1].Y));
        return r;
    }

    /// <summary>DNA double-helix ribbon with base-pair rungs, spindle-tapered at both ends.</summary>
    void DrawHelix(Graphics g)
    {
        float step = 4 * K;
        var pts = Resample(step);
        int n = pts.Count;
        if (n < 3) return;
        var A1 = new PointF[n]; var B1 = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            var q = pts[Math.Min(n - 1, i + 1)]; var o = pts[Math.Max(0, i - 1)];
            float tx = q.X - o.X, ty = q.Y - o.Y, l = MathF.Sqrt(tx * tx + ty * ty);
            if (l < .001f) { tx = 1; ty = 0; l = 1; }
            float nx = -ty / l, ny = tx / l, f = i / (float)(n - 1);
            float amp = 8 * K * MathF.Pow(MathF.Sin(MathF.PI * f), .7f) * (.7f + .3f * energy);
            float s = MathF.Sin(i * step / (13 * K) - time * 9f) * amp;
            A1[i] = new(pts[i].X + nx * s, pts[i].Y + ny * s);
            B1[i] = new(pts[i].X - nx * s, pts[i].Y - ny * s);
        }
        using var pen = NewPen();
        for (int i = 1; i < n; i++)
        {
            float f = i / (float)(n - 1);
            pen.Width = 4.5f * K; pen.Color = A(.22f * f, neon);
            g.DrawLine(pen, A1[i - 1], A1[i]); g.DrawLine(pen, B1[i - 1], B1[i]);
            pen.Width = 1.4f * K;
            pen.Color = A(.9f * f, Mix(neon, Color.White, .3f)); g.DrawLine(pen, A1[i - 1], A1[i]);
            pen.Color = A(.8f * f, Mix(neon, accent, .5f)); g.DrawLine(pen, B1[i - 1], B1[i]);
            if (i % 3 == 0) { pen.Width = 1.1f * K; pen.Color = A(.67f * f, accent); g.DrawLine(pen, A1[i], B1[i]); }
        }
    }

    static float Fade(Node n)
    {
        float t = n.Age / n.Life;
        return t < .12f ? t / .12f : 1 - (t - .12f) / .88f;
    }

    /// <summary>Neural constellation: nodes dropped along the path link to nearby nodes; signals travel the links.</summary>
    void DrawNeural(Graphics g)
    {
        int n = nodes.Count;
        if (n == 0) return;
        float link = 64 * K;
        using var pen = NewPen();
        for (int i = 0; i < n; i++)
        {
            var a = nodes[i]; float fa = Fade(a);
            for (int j = i + 1; j < n; j++)
            {
                var b = nodes[j];
                float dd = Dist(a.X, a.Y, b.X, b.Y);
                if (dd > link) continue;
                float al = (1 - dd / link) * MathF.Min(fa, Fade(b));
                pen.Width = 2.6f * K; pen.Color = A(.18f * al, neon); g.DrawLine(pen, a.X, a.Y, b.X, b.Y);
                pen.Width = .9f * K; pen.Color = A(.75f * al, Mix(neon, Color.White, .25f)); g.DrawLine(pen, a.X, a.Y, b.X, b.Y);
                float ph = (time * 1.6f + (i * 7 + j * 3) * .137f) % 1f;
                Dot(g, a.X + (b.X - a.X) * ph, a.Y + (b.Y - a.Y) * ph, 1.2f * K, A(.85f * al, accent));
            }
        }
        foreach (var a in nodes)
        {
            float fa = Fade(a);
            Glow(g, gNeon, a.X, a.Y, 9 * K, .7f * fa);
            Dot(g, a.X, a.Y, 1.9f * K, A(fa, Color.White));
            if (a.Age < .3f)
            {
                float r = (2 + 14 * a.Age / .3f) * K;
                pen.Width = 1f * K; pen.Color = A(.6f * (1 - a.Age / .3f), neon);
                g.DrawEllipse(pen, a.X - r, a.Y - r, 2 * r, 2 * r);
            }
        }
    }

    static float Ecg(float p) // one heartbeat, p ∈ [0,1)
    {
        if (p < .10f) return 0;
        if (p < .20f) return .16f * MathF.Sin((p - .10f) / .10f * MathF.PI);   // P wave
        if (p < .28f) return 0;
        if (p < .31f) return -.22f * (p - .28f) / .03f;                        // Q
        if (p < .35f) return -.22f + 1.22f * (p - .31f) / .04f;                // R
        if (p < .39f) return 1f - 1.4f * (p - .35f) / .04f;                    // S
        if (p < .42f) return -.4f + .4f * (p - .39f) / .03f;
        if (p < .58f) return 0;
        if (p < .76f) return .3f * MathF.Sin((p - .58f) / .18f * MathF.PI);   // T wave
        return 0;
    }

    /// <summary>ECG / oximeter pulse trace drawn along the pointer path.</summary>
    void DrawPulse(Graphics g)
    {
        var pts = Resample(2.5f * K);
        int n = pts.Count;
        if (n < 3) return;
        float period = 110 * K, amp = 15 * K * (.65f + .35f * energy), s = 0;
        var o = new PointF[n];
        for (int i = n - 1; i >= 0; i--)
        {
            if (i < n - 1) s += Dist(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y);
            var q = pts[Math.Min(n - 1, i + 1)]; var p0 = pts[Math.Max(0, i - 1)];
            float tx = q.X - p0.X, ty = q.Y - p0.Y, l = MathF.Sqrt(tx * tx + ty * ty);
            if (l < .001f) { tx = 1; ty = 0; l = 1; }
            float ph = s / period; ph -= MathF.Floor(ph);
            float env = MathF.Min(1, s / (18 * K));
            float v = -Ecg(ph) * amp * env;
            o[i] = new(pts[i].X - ty / l * v, pts[i].Y + tx / l * v);
        }
        using var pen = NewPen();
        for (int pass = 0; pass < 2; pass++)
            for (int i = 1; i < n; i++)
            {
                float f = i / (float)(n - 1);
                if (pass == 0) { pen.Width = 5f * K; pen.Color = A(.2f * f, neon); }
                else { pen.Width = 1.6f * K; pen.Color = A(.95f * f, Mix(neon, Color.White, .35f * f)); }
                g.DrawLine(pen, o[i - 1], o[i]);
            }
        Glow(g, gNeon, o[n - 1].X, o[n - 1].Y, 12 * K, .8f);
        Dot(g, o[n - 1].X, o[n - 1].Y, 2.2f * K, Color.White);
    }

    /// <summary>Agent glide: a smooth, spring-follow halo with an elastic tether to the pointer.</summary>
    void DrawGlide(Graphics g)
    {
        if (gAlpha <= .01f || float.IsNaN(lastX)) return;
        float a = gAlpha, r = 15 * K;
        using var pen = NewPen();
        pen.Width = 1.2f * K; pen.Color = A(.35f * a, neon); g.DrawLine(pen, gx, gy, lastX, lastY);
        Glow(g, gNeon, gx, gy, r * 2.2f, .55f * a);
        pen.Width = 4f * K; pen.Color = A(.22f * a, neon); g.DrawEllipse(pen, gx - r, gy - r, 2 * r, 2 * r);
        pen.Width = 1.6f * K; pen.Color = A(.9f * a, Mix(neon, Color.White, .3f)); g.DrawEllipse(pen, gx - r, gy - r, 2 * r, 2 * r);
        for (int i = 0; i < 3; i++)
        {
            float ang = time * 2.2f + i * MathF.Tau / 3;
            Dot(g, gx + MathF.Cos(ang) * r, gy + MathF.Sin(ang) * r, 2f * K, A(a, i == 0 ? accent : Color.White));
        }
    }

    /// <summary>Drag tether: animated dashed beam from the press point (benzene anchor) to the pointer.</summary>
    void DrawTether(Graphics g)
    {
        using var pen = NewPen();
        pen.Width = 4.5f * K; pen.Color = A(.18f, neon); g.DrawLine(pen, downX, downY, lastX, lastY);
        pen.Width = 1.5f * K; pen.Color = A(.85f, Mix(neon, Color.White, .3f));
        pen.DashPattern = new[] { 3f, 2.2f }; pen.DashOffset = -time * 12f;
        g.DrawLine(pen, downX, downY, lastX, lastY);
        pen.DashStyle = DashStyle.Solid; pen.Width = 1.4f * K; pen.Color = A(.9f, accent);
        g.DrawPolygon(pen, Hex(downX, downY, 6 * K, time * 40));
        Glow(g, gAccent, downX, downY, 12 * K, .7f);
        Dot(g, downX, downY, 1.8f * K, Color.White);
    }

    /// <summary>Idle "thinking" orbit: tilted electron orbits around a resting pointer.</summary>
    void DrawIdle(Graphics g)
    {
        if (float.IsNaN(lastX)) return;
        float a = idleFade, r = 22 * K, x = lastX, y = lastY;
        using var pen = NewPen();
        pen.Width = 1.2f * K; pen.Color = A(.5f * a, neon);
        pen.DashPattern = new[] { 2f, 3f }; pen.DashOffset = time * 4;
        g.DrawEllipse(pen, x - r, y - r, 2 * r, 2 * r);
        for (int i = 0; i < 3; i++)
        {
            float rr = r * (.62f + .19f * i), sp = 1.6f - .35f * i, ang = time * sp * MathF.PI + i * 2.1f;
            float ex = x + MathF.Cos(ang) * rr, ey = y + MathF.Sin(ang) * rr * .55f;
            Glow(g, i == 1 ? gAccent : gNeon, ex, ey, 6 * K, .8f * a);
            Dot(g, ex, ey, 1.6f * K, A(a, Color.White));
        }
    }

    void DrawBurst(Graphics g, Burst b)
    {
        float t = Math.Min(1, b.Age / b.Life), e = 1 - MathF.Pow(1 - t, 3), fade = 1 - t;
        using var pen = NewPen();
        switch (b.Style)
        {
            case ClickStyle.Synapse: // neural fire: dendrites + nodes
            {
                Ring(g, pen, b.X, b.Y, (5 + 30 * e) * K, b.C, fade * .8f, 1.3f);
                float grow = Math.Min(1, t / .3f); grow = 1 - (1 - grow) * (1 - grow);
                for (int i = 0; i < b.Ang.Length; i++)
                {
                    float L = b.Len[i] * K * grow, cx = MathF.Cos(b.Ang[i]), sy = MathF.Sin(b.Ang[i]);
                    float kink = (i & 1) == 0 ? .12f : -.12f;
                    var path = new[]
                    {
                        new PointF(b.X, b.Y),
                        new PointF(b.X + cx * L * .55f - sy * L * kink, b.Y + sy * L * .55f + cx * L * kink),
                        new PointF(b.X + cx * L, b.Y + sy * L)
                    };
                    pen.Width = 3.6f * K; pen.Color = A(.24f * fade, b.C); g.DrawLines(pen, path);
                    pen.Width = 1.1f * K; pen.Color = A(.9f * fade, Mix(b.C, Color.White, .35f)); g.DrawLines(pen, path);
                    float ip = (t * 2.4f + i * .13f) % 1f;
                    Dot(g, b.X + cx * L * ip, b.Y + sy * L * ip, 1.3f * K, A(.9f * fade, Color.White));
                    if (grow > .85f)
                    {
                        Glow(g, Spr(b.C2), path[2].X, path[2].Y, 9 * K, fade);
                        Dot(g, path[2].X, path[2].Y, 1.8f * K, A(fade, Color.White));
                    }
                }
                Glow(g, Spr(b.C), b.X, b.Y, 16 * K, fade);
                Dot(g, b.X, b.Y, 2.6f * K, A(fade, Color.White));
                break;
            }
            case ClickStyle.Molecule: // benzene ring with Kekulé double bonds
            {
                float r = (7 + 20 * e) * K, rot = e * 60;
                var hex = Hex(b.X, b.Y, r, rot);
                pen.Width = 4f * K; pen.Color = A(.24f * fade, b.C); g.DrawPolygon(pen, hex);
                pen.Width = 1.4f * K; pen.Color = A(.94f * fade, Mix(b.C, Color.White, .3f)); g.DrawPolygon(pen, hex);
                var inner = Hex(b.X, b.Y, r * .72f, rot);
                pen.Width = 1f * K; pen.Color = A(.67f * fade, b.C);
                for (int i = 0; i < 6; i += 2) g.DrawLine(pen, inner[i], inner[(i + 1) % 6]);
                var outer = Hex(b.X, b.Y, r * 1.5f, -rot * .6f);
                pen.Width = .9f * K; pen.Color = A(.35f * fade * fade, b.C2); g.DrawPolygon(pen, outer);
                foreach (var v in hex)
                {
                    Glow(g, Spr(b.C), v.X, v.Y, 7 * K, fade * .9f);
                    Dot(g, v.X, v.Y, 1.7f * K, A(fade, Color.White));
                }
                Glow(g, Spr(b.C), b.X, b.Y, 14 * K * (1 - e * .5f), fade * .8f);
                break;
            }
            case ClickStyle.Atom: // three electron orbitals around a nucleus
            {
                float R = (9 + 17 * e) * K;
                for (int i = 0; i < 3; i++)
                {
                    float rot = i * 60 + t * 90;
                    var st = g.Save();
                    g.TranslateTransform(b.X, b.Y); g.RotateTransform(rot);
                    pen.Width = 3.2f * K; pen.Color = A(.2f * fade, b.C); g.DrawEllipse(pen, -R, -R * .36f, 2 * R, R * .72f);
                    pen.Width = 1.1f * K; pen.Color = A(.85f * fade, Mix(b.C, Color.White, .3f)); g.DrawEllipse(pen, -R, -R * .36f, 2 * R, R * .72f);
                    g.Restore(st);
                    float th = t * MathF.Tau * 1.8f + i * 2.1f, ex = MathF.Cos(th) * R, ey = MathF.Sin(th) * R * .36f;
                    float rr = rot * MathF.PI / 180, wx = b.X + ex * MathF.Cos(rr) - ey * MathF.Sin(rr), wy = b.Y + ex * MathF.Sin(rr) + ey * MathF.Cos(rr);
                    Glow(g, Spr(b.C2), wx, wy, 7 * K, fade);
                    Dot(g, wx, wy, 1.8f * K, A(fade, Color.White));
                }
                Glow(g, Spr(b.C2), b.X, b.Y, 13 * K, fade);
                Dot(g, b.X, b.Y, 3f * K, A(fade, Mix(b.C2, Color.White, .4f)));
                break;
            }
            case ClickStyle.Mitosis: // a cell pinches into two daughter cells
            {
                float r0 = 11 * K, sep = 17 * K * e, ax = MathF.Cos(b.Axis), ay = MathF.Sin(b.Axis);
                float r = r0 * (1 - .18f * e);
                var c1 = new PointF(b.X - ax * sep, b.Y - ay * sep);
                var c2 = new PointF(b.X + ax * sep, b.Y + ay * sep);
                float bridge = r * 1.7f * MathF.Max(0, 1 - e * 1.25f);
                if (bridge > .5f) { pen.Width = bridge; pen.Color = A(.10f * fade, b.C); g.DrawLine(pen, c1, c2); }
                using var fill = new SolidBrush(A(.10f * fade, b.C));
                foreach (var c in new[] { c1, c2 })
                {
                    g.FillEllipse(fill, c.X - r, c.Y - r, 2 * r, 2 * r);
                    pen.Width = 3.4f * K; pen.Color = A(.2f * fade, b.C); g.DrawEllipse(pen, c.X - r, c.Y - r, 2 * r, 2 * r);
                    pen.Width = 1.2f * K; pen.Color = A(.9f * fade, Mix(b.C, Color.White, .3f)); g.DrawEllipse(pen, c.X - r, c.Y - r, 2 * r, 2 * r);
                    Glow(g, Spr(b.C2), c.X, c.Y, 8 * K, fade * .9f);
                    Dot(g, c.X, c.Y, 2.4f * K, A(fade, Mix(b.C2, Color.White, .3f)));
                }
                break;
            }
            default:
                Ring(g, pen, b.X, b.Y, (4 + 34 * e) * K, b.C, fade, 1.6f);
                Ring(g, pen, b.X, b.Y, (4 + 22 * e) * K, b.C2, fade * .6f, 1f);
                break;
        }
    }

    static PointF[] Hex(float cx, float cy, float r, float rotDeg)
    {
        var p = new PointF[6];
        for (int i = 0; i < 6; i++)
        {
            float a = (rotDeg + 30 + 60 * i) * MathF.PI / 180;
            p[i] = new(cx + r * MathF.Cos(a), cy + r * MathF.Sin(a));
        }
        return p;
    }

    void DrawSpark(Graphics g, Spark s)
    {
        float a = 1 - s.Age / s.Life;
        using var pen = new Pen(A(.9f * a, Mix(s.C, Color.White, .4f)), s.Size * K) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, s.X, s.Y, s.X - s.Vx * .035f, s.Y - s.Vy * .035f);
    }

    /// <summary>Headless scripted scene (self-test + docs). Clicks land at fixed spots so every burst is visible.</summary>
    public static void Scene(Settings s, Graphics g, int w, int h, float k, int clickA = -1, int clickB = -1, bool drag = false, bool idle = false)
    {
        var fx = new Effects(s) { K = k };
        const float dt = 1 / 60f;
        float X(float u) => w * .09f + w * .82f * u;
        float Y(float u) => h * .5f + h * .22f * MathF.Sin(u * MathF.PI * 2.2f);
        for (int i = 0; i <= 46; i++)
        {
            float u = i / 46f;
            if (drag && i == 8) fx.Press(X(u), Y(u), 0);
            if (clickA >= 0 && i == 30) { fx.Press(w * .3f, h * .76f, clickA); fx.Release(clickA); }
            if (clickB >= 0 && i == 36) { fx.Press(w * .6f, h * .24f, clickB); fx.Release(clickB); }
            fx.Update(X(u), Y(u), dt);
        }
        if (idle) for (int i = 0; i < 150; i++) fx.Update(X(1), Y(1), dt);
        fx.Draw(g);
    }
}

/// <summary>Click-through, topmost layered window that only spans the live effects' bounding box.</summary>
public sealed class Overlay : Form
{
    readonly Effects fx;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    double last;
    bool l0, r0, m0, paused, drawn;
    int frame;
    IntPtr memDC, dib, oldBmp, bits;
    int capW, capH;
    Bitmap wrap;

    public Overlay(Effects fx)
    {
        this.fx = fx;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(0, 0, 1, 1);
        timer.Tick += Tick;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8 | 0x08000000; // LAYERED|TRANSPARENT|TOOLWINDOW|TOPMOST|NOACTIVATE
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public bool Running
    {
        get => timer.Enabled;
        set
        {
            if (value) { fx.Reset(); timer.Start(); }
            else { timer.Stop(); fx.Reset(); Render(); }
        }
    }

    void Tick(object sender, EventArgs e)
    {
        double now = clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(.05, now - last);
        last = now;
        GetCursorPos(out var pt);
        if (++frame % 60 == 1)
        {
            paused = fx.S.PauseFullscreen && FullscreenBusy();
            if (MonitorFromPoint(pt, 2) is var mon && GetDpiForMonitor(mon, 0, out uint dx, out _) == 0) fx.K = dx / 96f;
            SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        }
        bool l = Down(0x01), r = Down(0x02), m = Down(0x04);
        if (!paused)
        {
            if (l && !l0) fx.Press(pt.X, pt.Y, 0);
            if (!l && l0) fx.Release(0);
            if (r && !r0) fx.Press(pt.X, pt.Y, 1);
            if (m && !m0) fx.Press(pt.X, pt.Y, 2);
            fx.Update(pt.X, pt.Y, dt);
        }
        else fx.Reset();
        l0 = l; r0 = r; m0 = m;
        Render();
    }

    void Render()
    {
        if (!IsHandleCreated) return;
        if (fx.Idle)
        {
            if (drawn) { Push(Rectangle.Empty); drawn = false; }
            return;
        }
        var b = fx.Bounds();
        b.Inflate(4, 4);
        b.Intersect(SystemInformation.VirtualScreen);
        if (b.Width < 1 || b.Height < 1) return;
        Ensure(b.Width, b.Height);
        using (var g = Graphics.FromImage(wrap))
        {
            g.SetClip(new Rectangle(0, 0, b.Width, b.Height));
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.TranslateTransform(-b.X, -b.Y);
            fx.Draw(g);
        }
        Push(b);
        drawn = true;
    }

    void Push(Rectangle b)
    {
        if (b.IsEmpty)
        {
            Ensure(1, 1);
            using (var g = Graphics.FromImage(wrap)) g.Clear(Color.Transparent);
            GetCursorPos(out var p);
            b = new Rectangle(p.X, p.Y, 1, 1);
        }
        var dst = new POINT { X = b.X, Y = b.Y };
        var size = new SIZE { cx = b.Width, cy = b.Height };
        var src = new POINT();
        var bf = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0, ref bf, 2);
    }

    void Ensure(int w, int h)
    {
        if (wrap != null && w <= capW && h <= capH) return;
        int nw = Math.Max(w, Math.Max(256, (int)(capW * 1.25f))), nh = Math.Max(h, Math.Max(256, (int)(capH * 1.25f)));
        Free();
        capW = nw; capH = nh;
        var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = capW, biHeight = -capH, biPlanes = 1, biBitCount = 32 };
        memDC = CreateCompatibleDC(IntPtr.Zero);
        dib = CreateDIBSection(memDC, ref bi, 0, out bits, IntPtr.Zero, 0);
        oldBmp = SelectObject(memDC, dib);
        wrap = new Bitmap(capW, capH, capW * 4, PixelFormat.Format32bppPArgb, bits);
    }

    void Free()
    {
        wrap?.Dispose(); wrap = null;
        if (memDC != IntPtr.Zero)
        {
            SelectObject(memDC, oldBmp);
            DeleteObject(dib);
            DeleteDC(memDC);
            memDC = IntPtr.Zero;
        }
    }

    protected override void Dispose(bool disposing)
    {
        timer.Dispose();
        Free();
        base.Dispose(disposing);
    }

    static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    static bool FullscreenBusy() => SHQueryUserNotificationState(out int st) == 0 && st is 2 or 3 or 4;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dpiX, out uint dpiY);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
}
