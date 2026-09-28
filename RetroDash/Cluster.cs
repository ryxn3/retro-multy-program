using System.Drawing.Drawing2D;
using System.Drawing.Text;
using RetroRadio;

namespace RetroDash;

enum ClusterStyle { Digital, Analog, Modern }

enum Lamp { Left, HighBeam, Engine, Battery, Fuel, Temp, Right }

/// <summary>
/// Draws the instrument cluster in a 1000 x 400 design space. The printed parts (faces, ticks, glass)
/// are cached in a bitmap; needles, segments and text are drawn live on top.
/// </summary>
sealed partial class Cluster : IDisposable
{
    public const float W = 1000, H = 400;

    public ClusterStyle Style = ClusterStyle.Analog;
    public bool Night, Mph, Demo;
    public Color? AccentOverride;
    public string Hint = "";

    Bitmap? bg;
    string bgKey = "";
    double t; // seconds since the ignition was turned on
    float rpm, rpmVel, spd, spdVel, fuel, temp = 40, peakRpm;
    double peakAt;

    public float MaxSpeed => Mph ? 160 : 260;
    string Unit => Mph ? "mph" : "km/h";
    float ToUnits(float kmh) => Mph ? kmh * 0.621371f : kmh;
    double ToUnits(double km) => Mph ? km * 0.621371 : km;

    bool BulbCheck => t < 1.4;
    bool BlinkOn => t % 0.7 < 0.38;

    /// <summary>Turns the key again: needle sweep and bulb check.</summary>
    public void Ignition() => t = 0;

    public void Dispose() => bg?.Dispose();

    public void Step(DashState s, double dt)
    {
        t += dt;
        float targetRpm = s.Rpm, targetSpd = ToUnits(s.Speed);
        const double sweep = 1.9;
        if (t < sweep)
        {
            double p = t / sweep;
            float f = (float)(p < 0.45 ? Ease(p / 0.45) : 1 - Ease((p - 0.45) / 0.55));
            targetRpm = 8000 * f;
            targetSpd = MaxSpeed * f;
        }
        for (double left = dt; left > 1e-6; left -= 0.005)
        {
            float h = (float)Math.Min(0.005, left);
            Spring(ref rpm, ref rpmVel, targetRpm, h);
            Spring(ref spd, ref spdVel, targetSpd, h);
        }
        rpm = Math.Clamp(rpm, 0, 8400);
        spd = Math.Clamp(spd, 0, MaxSpeed * 1.03f);
        float a = 1 - MathF.Exp(-(float)dt * 1.2f);
        fuel += (s.Fuel - fuel) * a;
        temp += (s.Temp - temp) * a;
        if (rpm >= peakRpm || t - peakAt > 1.2)
        {
            peakRpm = rpm;
            peakAt = t;
        }
    }

    static double Ease(double x) => x * x * (3 - 2 * x);

    static void Spring(ref float v, ref float vel, float target, float h)
    {
        const float k = 140, damping = 19; // a touch of overshoot, like a real needle
        vel += ((target - v) * k - vel * damping) * h;
        v += vel * h;
    }

    public void Draw(Graphics g, Size px, DashState s)
    {
        if (px.Width < 10 || px.Height < 10) return;
        string key = $"{Style}|{Night}|{Mph}|{AccentOverride?.ToArgb()}|{px.Width}x{px.Height}";
        if (bg == null || key != bgKey)
        {
            bg?.Dispose();
            bg = new Bitmap(px.Width, px.Height);
            using var bgG = Graphics.FromImage(bg);
            Setup(bgG, px);
            switch (Style)
            {
                case ClusterStyle.Digital: DigitalBack(bgG); break;
                case ClusterStyle.Analog: AnalogBack(bgG); break;
                default: ModernBack(bgG); break;
            }
            bgKey = key;
        }
        g.DrawImageUnscaled(bg, 0, 0);
        var saved = g.Save();
        Setup(g, px);
        switch (Style)
        {
            case ClusterStyle.Digital: DigitalLive(g, s); break;
            case ClusterStyle.Analog: AnalogLive(g, s); break;
            default: ModernLive(g, s); break;
        }
        g.Restore(saved);
    }

    static void Setup(Graphics g, Size px)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.ScaleTransform(px.Width / W, px.Height / H);
    }

    // ───────────────────────────── shared helpers ─────────────────────────────

    static readonly Dictionary<string, FontFamily> families = [];

    /// <summary>Bahnschrift (Windows' DIN-style dashboard font) with a fallback.</summary>
    static FontFamily Fam(string name = "Bahnschrift")
    {
        if (families.TryGetValue(name, out var f)) return f;
        var found = FontFamily.Families.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? FontFamily.Families.FirstOrDefault(x => x.Name == "Bahnschrift")
            ?? new FontFamily("Segoe UI");
        families[name] = found;
        return found;
    }

    static readonly Dictionary<(string, float, FontStyle), Font> fonts = [];

    static Font F(float size, string family = "Bahnschrift", FontStyle style = FontStyle.Regular)
    {
        if (!fonts.TryGetValue((family, size, style), out var f))
            fonts[(family, size, style)] = f = new Font(Fam(family), size, style, GraphicsUnit.Pixel);
        return f;
    }

    static void Text(Graphics g, string s, Font f, Color c, float x, float y,
        StringAlignment h = StringAlignment.Center, StringAlignment v = StringAlignment.Center)
    {
        using var sf = new StringFormat { Alignment = h, LineAlignment = v, FormatFlags = StringFormatFlags.NoWrap };
        using var b = new SolidBrush(c);
        g.DrawString(s, f, b, x, y, sf);
    }

    /// <summary>Text as a filled outline, optionally with a soft glow around it (backlit printing, VFD phosphor).</summary>
    static void GlowText(Graphics g, string s, string family, float size, Color c, float x, float y, float glow,
        StringAlignment h = StringAlignment.Center, FontStyle style = FontStyle.Regular)
    {
        using var path = new GraphicsPath(FillMode.Winding); // overlapping glyph contours must not cancel out
        using var sf = new StringFormat { Alignment = h, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        path.AddString(s, Fam(family), (int)style, size, new PointF(x, y), sf);
        Glow(g, path, c, glow);
        using var b = new SolidBrush(c);
        g.FillPath(b, path);
    }

    static void Glow(Graphics g, GraphicsPath path, Color c, float glow)
    {
        if (glow <= 0) return;
        for (int i = 3; i >= 1; i--)
        {
            using var p = new Pen(Color.FromArgb(Math.Clamp((int)(glow * 22), 0, 255), c), i * 3f) { LineJoin = LineJoin.Round };
            g.DrawPath(p, path);
        }
    }

    static RectangleF Circle(float cx, float cy, float r) => new(cx - r, cy - r, r * 2, r * 2);
    static RectangleF Circle(PointF c, float r) => Circle(c.X, c.Y, r);

    static PointF Polar(PointF c, float r, float deg)
    {
        float a = deg * MathF.PI / 180;
        return new(c.X + r * MathF.Cos(a), c.Y + r * MathF.Sin(a));
    }

    static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    static Color Dim(Color c, float k) => Color.FromArgb(c.A, (int)(c.R * k), (int)(c.G * k), (int)(c.B * k));

    /// <summary>Fine speckle so plastic and glass don't look like flat fills.</summary>
    static void Grain(Graphics g, RectangleF area, int count, int alpha, int seed)
    {
        var rng = new Random(seed);
        var light = new List<RectangleF>(count / 2);
        var dark = new List<RectangleF>(count / 2);
        for (int i = 0; i < count; i++)
        {
            var r = new RectangleF(area.X + (float)rng.NextDouble() * area.Width, area.Y + (float)rng.NextDouble() * area.Height, 1.1f, 1.1f);
            (i % 2 == 0 ? light : dark).Add(r);
        }
        using var lb = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
        using var db = new SolidBrush(Color.FromArgb(alpha * 2, 0, 0, 0));
        if (light.Count > 0) g.FillRectangles(lb, light.ToArray());
        if (dark.Count > 0) g.FillRectangles(db, dark.ToArray());
    }

    static string Clock => DateTime.Now.ToString("H:mm");

    static string Rate(double bps) => bps >= 1_000_000 ? $"{bps / 1_000_000:0.0} MB/s" : $"{bps / 1000:0} KB/s";

    /// <summary>The rotating line on each cluster's info display.</summary>
    string InfoLine(DashState s, bool dotMatrix)
    {
        var lines = new List<string>();
        if (Hint.Length > 0 && t < 9) return Hint;
        if (s.NowPlaying.Length > 0) lines.Add((dotMatrix ? "" : "♪ ") + s.NowPlaying);
        if (Demo) lines.Add("DEMO DRIVE");
        else
        {
            lines.Add($"CPU {s.CpuPercent:0}%  RAM {s.RamPercent:0}%");
            lines.Add($"DL {Rate(s.DownBps)}  UL {Rate(s.UpBps)}");
        }
        if (!Demo) lines.Add($"UPTIME {(int)s.Uptime.TotalHours}:{s.Uptime.Minutes:00}");
        return lines[(int)(t / 4) % lines.Count];
    }

    // ───────────────────────────── warning lamps ─────────────────────────────

    static Color LampColor(Lamp l) => l switch
    {
        Lamp.Left or Lamp.Right => Color.FromArgb(60, 255, 110),
        Lamp.HighBeam => Color.FromArgb(70, 140, 255),
        Lamp.Battery or Lamp.Temp => Color.FromArgb(255, 60, 50),
        _ => Color.FromArgb(255, 176, 32),
    };

    bool LampLit(Lamp l, DashState s) => BulbCheck || l switch
    {
        Lamp.Left => s.Left && BlinkOn,
        Lamp.Right => s.Right && BlinkOn,
        Lamp.HighBeam => s.HighBeam,
        Lamp.Engine => s.CheckEngine,
        Lamp.Battery => s.Battery,
        Lamp.Fuel => s.LowFuel,
        _ => s.Hot,
    };

    /// <summary>Tell-tale symbol: arrows for the indicators, a beam for high beam, badges for the rest.</summary>
    static void DrawLamp(Graphics g, Lamp l, RectangleF r, bool lit, Color unlit)
    {
        if (!lit && unlit.A == 0) return;
        Color c = lit ? LampColor(l) : unlit;
        if (lit)
        {
            using var halo = new GraphicsPath();
            halo.AddEllipse(RectangleF.Inflate(r, r.Width * 0.35f, r.Height * 0.35f));
            using var pb = new PathGradientBrush(halo) { CenterColor = Color.FromArgb(90, c), SurroundColors = [Color.FromArgb(0, c)] };
            g.FillPath(pb, halo);
        }
        using var b = new SolidBrush(c);
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        switch (l)
        {
            case Lamp.Left:
            case Lamp.Right:
            {
                float dir = l == Lamp.Left ? 1 : -1, x0 = l == Lamp.Left ? r.X : r.Right;
                PointF P(float fx, float fy) => new(x0 + dir * fx * r.Width, r.Y + fy * r.Height);
                g.FillPolygon(b, [P(0, 0.5f), P(0.48f, 0.08f), P(0.48f, 0.32f), P(1, 0.32f), P(1, 0.68f), P(0.48f, 0.68f), P(0.48f, 0.92f)]);
                break;
            }
            case Lamp.HighBeam:
            {
                using var pen = new Pen(c, r.Height * 0.09f);
                g.FillPie(b, cx - r.Width * 0.12f, r.Y + r.Height * 0.14f, r.Width * 0.5f, r.Height * 0.72f, -90, 180);
                for (int i = 0; i < 4; i++)
                {
                    float y = r.Y + r.Height * (0.24f + i * 0.17f);
                    g.DrawLine(pen, r.X + r.Width * 0.05f, y, cx + r.Width * 0.02f, y);
                }
                break;
            }
            default:
            {
                string label = l switch { Lamp.Engine => "ENG", Lamp.Battery => "BAT", Lamp.Fuel => "FUEL", _ => "TEMP" };
                using var box = RoundRect(RectangleF.Inflate(r, 0, -r.Height * 0.14f), r.Height * 0.14f);
                using var pen = new Pen(c, 1.6f);
                g.DrawPath(pen, box);
                Text(g, label, F(r.Height * 0.36f, "Bahnschrift SemiBold Condensed"), c, cx, cy + 0.5f);
                break;
            }
        }
    }

    void LampRow(Graphics g, DashState s, IEnumerable<(Lamp, RectangleF)> lamps, Color unlit)
    {
        foreach (var (l, r) in lamps) DrawLamp(g, l, r, LampLit(l, s), unlit);
    }

    // ───────────────────────────── seven-segment digits ─────────────────────────────

    static readonly Dictionary<char, int> SegMap = new()
    {
        ['0'] = 0x3F, ['1'] = 0x06, ['2'] = 0x5B, ['3'] = 0x4F, ['4'] = 0x66, ['5'] = 0x6D, ['6'] = 0x7D, ['7'] = 0x07,
        ['8'] = 0x7F, ['9'] = 0x6F, ['-'] = 0x40, ['P'] = 0x73, ['N'] = 0x54, ['R'] = 0x50, ['D'] = 0x5E, ['E'] = 0x79,
        ['H'] = 0x76, ['L'] = 0x38, ['C'] = 0x39, [' '] = 0,
    };

    /// <summary>Hexagonal segments (a..g) of one digit, slightly italic like 80s dashboards.</summary>
    static PointF[][] SegPolys(float x, float y, float w, float h, float skew)
    {
        float th = w * 0.17f, half = th / 2, gap = th * 0.18f;
        float L = x + half, R = x + w - half, T = y + half, M = y + h / 2, B = y + h - half;
        PointF S(float px, float py) => new(px + (y + h - py) * skew, py);
        PointF[] Hz(float x0, float x1, float yy) =>
            [S(x0, yy), S(x0 + half, yy - half), S(x1 - half, yy - half), S(x1, yy), S(x1 - half, yy + half), S(x0 + half, yy + half)];
        PointF[] Vt(float xx, float y0, float y1) =>
            [S(xx, y0), S(xx + half, y0 + half), S(xx + half, y1 - half), S(xx, y1), S(xx - half, y1 - half), S(xx - half, y0 + half)];
        return
        [
            Hz(L + gap, R - gap, T),       // a
            Vt(R, T + gap, M - gap),       // b
            Vt(R, M + gap, B - gap),       // c
            Hz(L + gap, R - gap, B),       // d
            Vt(L, M + gap, B - gap),       // e
            Vt(L, T + gap, M - gap),       // f
            Hz(L + gap, R - gap, M),       // g
        ];
    }

    /// <summary>Draws a string of seven-segment digits; unlit segments are shown as faint ghosts.</summary>
    static void Seg7(Graphics g, string text, float x, float y, float w, float h, float pitch, Color on, Color ghost, float glow, float skew = 0.1f)
    {
        using var onB = new SolidBrush(on);
        using var ghostB = new SolidBrush(ghost);
        for (int i = 0; i < text.Length; i++)
        {
            int bits = SegMap.GetValueOrDefault(char.ToUpperInvariant(text[i]), 0);
            var polys = SegPolys(x + i * pitch, y, w, h, skew);
            for (int k = 0; k < 7; k++)
            {
                bool lit = (bits & (1 << k)) != 0;
                if (!lit)
                {
                    if (ghost.A > 0) g.FillPolygon(ghostB, polys[k]);
                    continue;
                }
                if (glow > 0)
                {
                    using var gp = new Pen(Color.FromArgb(Math.Clamp((int)(glow * 40), 0, 255), on), w * 0.14f) { LineJoin = LineJoin.Round };
                    g.DrawPolygon(gp, polys[k]);
                }
                g.FillPolygon(onB, polys[k]);
            }
        }
    }
}
