using System.Drawing.Drawing2D;
using RetroRadio;

namespace RetroDash;

/// <summary>80s digital dash: a vacuum-fluorescent panel with a sweeping bar-graph tach and seven-segment speed.</summary>
sealed partial class Cluster
{
    const int TachSegs = 48;
    static readonly RectangleF VfdGlass = new(24, 24, 952, 352);
    const float DotPitch = 2.6f;
    static readonly PointF DotOrigin = new(782, 206);
    const int DotCols = 11, DotRows = 4;
    const float DotLineH = 28;

    Color Vfd => AccentOverride ?? Color.FromArgb(93, 242, 220);
    static readonly Color VfdAmber = Color.FromArgb(255, 180, 60), VfdRed = Color.FromArgb(255, 64, 64);
    Color Ghost => Color.FromArgb(Night ? 20 : 26, Vfd);
    float VfdGlow => Night ? 1.3f : 0.7f;

    static float BandBottom(float u) => 180 - 26 * u;
    static float BandTop(float u) => 152 - 106 * MathF.Pow(u, 1.35f);
    static float BandX(float u) => 70 + u * 860;

    static PointF[] TachQuad(int i)
    {
        float u0 = i / (float)TachSegs, u1 = (i + 0.74f) / TachSegs;
        return [new(BandX(u0), BandTop(u0)), new(BandX(u1), BandTop(u1)), new(BandX(u1), BandBottom(u1)), new(BandX(u0), BandBottom(u0))];
    }

    Color TachColor(int i)
    {
        float rpmAt = (i + 1) * 8000f / TachSegs;
        return rpmAt > 6500 ? VfdRed : rpmAt > 5000 ? VfdAmber : Vfd;
    }

    void DigitalBack(Graphics g)
    {
        g.Clear(Color.FromArgb(10, 10, 11));
        Grain(g, new RectangleF(0, 0, W, H), 7000, 8, 11);

        // Bezel and smoked glass.
        using (var bezel = RoundRect(RectangleF.Inflate(VfdGlass, 12, 12), 26))
        using (var b = new LinearGradientBrush(RectangleF.Inflate(VfdGlass, 12, 12), Color.FromArgb(58, 60, 64), Color.FromArgb(14, 14, 16), 90f))
            g.FillPath(b, bezel);
        using var glass = RoundRect(VfdGlass, 16);
        using (var b = new LinearGradientBrush(VfdGlass, Color.FromArgb(Night ? 4 : 12, Night ? 10 : 20, Night ? 10 : 20), Color.FromArgb(2, 4, 5), 90f))
            g.FillPath(b, glass);

        // Filament wires in front of the phosphor.
        using (var wire = new Pen(Color.FromArgb(16, 200, 220, 220), 0.8f))
            for (float y = VfdGlass.Y + 30; y < VfdGlass.Bottom - 10; y += 46) g.DrawLine(wire, VfdGlass.X + 14, y, VfdGlass.Right - 14, y);

        // Ghosts of every segment and dot.
        using (var gb = new SolidBrush(Ghost))
        {
            for (int i = 0; i < TachSegs; i++) g.FillPolygon(gb, TachQuad(i));
            for (int i = 0; i < 10; i++)
            {
                g.FillRectangle(gb, BarRect(58, i));
                g.FillRectangle(gb, BarRect(150, i));
            }
            var dots = new List<RectangleF>();
            for (int row = 0; row < DotRows; row++)
                for (int col = 0; col < DotCols; col++)
                    for (int r = 0; r < 7; r++)
                        for (int c = 0; c < 5; c++)
                            dots.Add(new RectangleF(DotOrigin.X + (col * 6 + c) * DotPitch, DotOrigin.Y + row * DotLineH + r * DotPitch, DotPitch * 0.8f, DotPitch * 0.8f));
            g.FillRectangles(gb, dots.ToArray());
        }
        Seg7(g, "888", 300, 205, 74, 130, 92, Color.Transparent, Ghost, 0);
        Seg7(g, "8", 690, 222, 54, 96, 0, Color.Transparent, Ghost, 0);

        // Printed legends (phosphor, always lit).
        Color legend = Dim(Vfd, 0.8f);
        float glow = VfdGlow * 0.5f;
        GlowText(g, "RPM x1000", "Bahnschrift SemiBold", 13, legend, 72, 118, glow, StringAlignment.Near);
        for (int k = 0; k <= 8; k++)
        {
            float u = k / 8f;
            GlowText(g, k.ToString(), "Bahnschrift SemiBold", 14, k >= 7 ? VfdRed : k >= 6 ? VfdAmber : legend, BandX(u) + 6, BandBottom(u) + 15, glow);
        }
        GlowText(g, Unit.ToUpperInvariant(), "Bahnschrift SemiBold", 18, legend, 580, 325, glow, StringAlignment.Near);
        GlowText(g, "GEAR", "Bahnschrift SemiBold", 12, Dim(VfdAmber, 0.8f), 717, 209, glow);
        GlowText(g, "FUEL", "Bahnschrift SemiBold", 12, legend, 83, 346, glow);
        GlowText(g, "TEMP", "Bahnschrift SemiBold", 12, legend, 175, 346, glow);
        GlowText(g, "F", "Bahnschrift", 11, legend, 118, 214, glow);
        GlowText(g, "E", "Bahnschrift", 11, legend, 118, 327, glow);
        GlowText(g, "H", "Bahnschrift", 11, legend, 210, 214, glow);
        GlowText(g, "C", "Bahnschrift", 11, legend, 210, 327, glow);
        using (var p = new Pen(Dim(Vfd, 0.35f), 1)) g.DrawRectangle(p, DotOrigin.X - 8, DotOrigin.Y - 8, DotCols * 6 * DotPitch + 12, DotRows * DotLineH + 6);

        foreach (var (l, r) in DigitalLamps()) DrawLamp(g, l, r, false, Ghost);

        // Glass reflection.
        using (var b = new LinearGradientBrush(VfdGlass, Color.FromArgb(22, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 80f))
        {
            var clip = g.Clip;
            g.SetClip(glass);
            g.FillPolygon(b, new PointF[] { new(VfdGlass.X, VfdGlass.Y), new(VfdGlass.X + 520, VfdGlass.Y), new(VfdGlass.X + 300, VfdGlass.Bottom), new(VfdGlass.X, VfdGlass.Bottom) });
            g.Clip = clip;
        }
    }

    static RectangleF BarRect(float x, int i) => new(x, 318 - i * 12, 50, 8.5f);

    static IEnumerable<(Lamp, RectangleF)> DigitalLamps()
    {
        Lamp[] order = [Lamp.Left, Lamp.HighBeam, Lamp.Engine, Lamp.Battery, Lamp.Fuel, Lamp.Temp, Lamp.Right];
        for (int i = 0; i < order.Length; i++)
            yield return (order[i], new RectangleF(340 + i * 44, 344, 26, 24));
    }

    void DigitalLive(Graphics g, DashState s)
    {
        float glow = VfdGlow;

        // Tach bar graph with a peak-hold segment.
        int lit = (int)Math.Round(rpm / 8000 * TachSegs);
        int peak = Math.Clamp((int)Math.Round(peakRpm / 8000 * TachSegs) - 1, 0, TachSegs - 1);
        for (int i = 0; i < TachSegs; i++)
        {
            if (i >= lit && i != peak) continue;
            var quad = TachQuad(i);
            Color c = TachColor(i);
            using var gp = new Pen(Color.FromArgb((int)(45 * glow), c), 5) { LineJoin = LineJoin.Round };
            g.DrawPolygon(gp, quad);
            using var b = new SolidBrush(c);
            g.FillPolygon(b, quad);
        }

        // Speed and gear.
        int speed = (int)Math.Round(spd);
        Seg7(g, speed.ToString().PadLeft(3), 300, 205, 74, 130, 92, Vfd, Color.Transparent, glow);
        Seg7(g, BulbCheck ? "8" : s.Gear, 690, 222, 54, 96, 0, VfdAmber, Color.Transparent, glow);

        // Fuel and temperature bar graphs.
        int fuelBars = (int)Math.Ceiling(Math.Clamp(fuel, 0, 1) * 10);
        int tempBars = (int)Math.Ceiling(Math.Clamp((temp - 40) / 90, 0, 1) * 10);
        for (int i = 0; i < 10; i++)
        {
            if (i < fuelBars || BulbCheck) Bar(g, BarRect(58, i), i < 2 ? VfdAmber : Vfd, glow);
            if (i < tempBars || BulbCheck) Bar(g, BarRect(150, i), i >= 8 ? VfdRed : Vfd, glow);
        }

        // Dot-matrix info panel.
        string info = DotFont.Normalize(InfoLine(s, true));
        if (info.Length > DotCols)
        {
            int shift = (int)(t * 5) % (info.Length + 4);
            info = (info + "    " + info)[shift..][..DotCols];
        }
        string[] lines =
        [
            DotFont.Normalize(Clock.PadLeft(5)),
            DotFont.Normalize($"ODO {ToUnits(s.OdoKm) % 1_000_000:000000}"),
            DotFont.Normalize($"TRP {ToUnits(s.TripKm) % 10000:0.0}"),
            info,
        ];
        var dots = new List<RectangleF>();
        for (int row = 0; row < DotRows; row++)
            DotFont.Emit(dots, lines[row].Length > DotCols ? lines[row][..DotCols] : lines[row], DotOrigin.X, DotOrigin.Y + row * DotLineH, DotPitch, DotPitch * 0.8f);
        if (dots.Count > 0)
        {
            var arr = dots.ToArray();
            using (var gb = new SolidBrush(Color.FromArgb((int)(40 * glow), Vfd)))
                g.FillRectangles(gb, arr.Select(d => RectangleF.Inflate(d, 1.1f, 1.1f)).ToArray());
            using var b = new SolidBrush(Vfd);
            g.FillRectangles(b, arr);
        }

        LampRow(g, s, DigitalLamps(), Color.Transparent);
    }

    static void Bar(Graphics g, RectangleF r, Color c, float glow)
    {
        using var gp = new Pen(Color.FromArgb((int)(45 * glow), c), 5);
        g.DrawRectangle(gp, r.X, r.Y, r.Width, r.Height);
        using var b = new SolidBrush(c);
        g.FillRectangle(b, r);
    }
}
