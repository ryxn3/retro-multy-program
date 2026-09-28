using System.Drawing.Drawing2D;

namespace RetroDash;

/// <summary>Modern digital cluster: glowing ring gauges and a centre screen with music and PC details.</summary>
sealed partial class Cluster
{
    static readonly PointF MTach = new(230, 200), MSpeed = new(770, 200);
    const float RingR = 148, RingW = 14;
    static readonly RectangleF MScreen = new(392, 58, 216, 286);

    Color ModernAccent => AccentOverride ?? Color.FromArgb(56, 189, 248);
    static readonly Color MText = Color.FromArgb(236, 240, 246), MDim = Color.FromArgb(128, 138, 156);

    void ModernBack(Graphics g)
    {
        using (var b = new LinearGradientBrush(new RectangleF(0, 0, W, H), Color.FromArgb(14, 18, 26), Color.FromArgb(4, 5, 8), 90f))
            g.FillRectangle(b, 0, 0, W, H);
        // Soft pools of light behind each ring.
        foreach (var c in new[] { MTach, MSpeed })
        {
            using var pool = new GraphicsPath();
            pool.AddEllipse(Circle(c, RingR + 60));
            using var pb = new PathGradientBrush(pool) { CenterColor = Color.FromArgb(Night ? 30 : 45, ModernAccent), SurroundColors = [Color.FromArgb(0, ModernAccent)] };
            g.FillPath(pb, pool);
        }
        Grain(g, new RectangleF(0, 0, W, H), 5000, 5, 5);

        ModernRing(g, MTach, 8, 1, v => ((int)v).ToString());
        ModernRing(g, MSpeed, MaxSpeed, Mph ? 20 : 20, v => ((int)v).ToString());
        Text(g, "x1000 rpm", F(12), MDim, MTach.X, MTach.Y + 64);
        Text(g, Unit, F(16), MDim, MSpeed.X, MSpeed.Y + 52);

        // Centre screen.
        using (var scr = RoundRect(MScreen, 18))
        {
            using (var b = new LinearGradientBrush(MScreen, Color.FromArgb(22, 28, 40), Color.FromArgb(10, 13, 19), 90f)) g.FillPath(b, scr);
            using (var p = new LinearGradientBrush(MScreen, Color.FromArgb(120, ModernAccent), Color.FromArgb(20, ModernAccent), 90f))
            using (var pen = new Pen(p, 1.5f))
                g.DrawPath(pen, scr);
        }
        using (var p = new Pen(Color.FromArgb(40, 255, 255, 255), 1))
        {
            g.DrawLine(p, MScreen.X + 16, 128, MScreen.Right - 16, 128);
            g.DrawLine(p, MScreen.X + 16, 196, MScreen.Right - 16, 196);
            g.DrawLine(p, MScreen.X + 16, 306, MScreen.Right - 16, 306);
        }

        // Fuel and temperature tracks.
        foreach (var r in new[] { ModernBar(MTach), ModernBar(MSpeed) })
            using (var path = RoundRect(r, 3))
            using (var b = new SolidBrush(Color.FromArgb(34, 40, 54)))
                g.FillPath(b, path);
        Text(g, "FUEL", F(11, "Bahnschrift SemiBold"), MDim, ModernBar(MTach).X - 8, ModernBar(MTach).Y + 3, StringAlignment.Far);
        Text(g, "TEMP", F(11, "Bahnschrift SemiBold"), MDim, ModernBar(MSpeed).X - 8, ModernBar(MSpeed).Y + 3, StringAlignment.Far);

        foreach (var (l, r) in ModernLamps()) DrawLamp(g, l, r, false, Color.FromArgb(34, 40, 52));
    }

    static RectangleF ModernBar(PointF c) => new(c.X - 55, c.Y + 138, 110, 6);

    static IEnumerable<(Lamp, RectangleF)> ModernLamps()
    {
        yield return (Lamp.Left, new RectangleF(30, 26, 32, 26));
        yield return (Lamp.Right, new RectangleF(938, 26, 32, 26));
        Lamp[] mid = [Lamp.HighBeam, Lamp.Engine, Lamp.Battery, Lamp.Fuel, Lamp.Temp];
        for (int i = 0; i < mid.Length; i++) yield return (mid[i], new RectangleF(420 + i * 34, 20, 26, 24));
    }

    void ModernRing(Graphics g, PointF c, float max, float labelStep, Func<float, string> label)
    {
        using (var track = new Pen(Color.FromArgb(30, 36, 50), RingW)) g.DrawArc(track, Circle(c, RingR), 135, 270);
        using var dot = new SolidBrush(Color.FromArgb(90, 100, 120));
        for (float v = 0; v <= max + 0.01f; v += labelStep)
        {
            float a = DialAngle(v, max);
            g.FillEllipse(dot, Circle(Polar(c, RingR + 17, a), 1.8f));
            var lp = Polar(c, RingR - 32, a);
            Text(g, label(v), F(16, "Bahnschrift Light"), MDim, lp.X, lp.Y);
        }
    }

    void ModernArc(Graphics g, PointF c, float value, float max, Color hot, float hotFrom)
    {
        float u = Math.Clamp(value / max, 0, 1);
        if (u < 0.002f) return;
        var rect = Circle(c, RingR);
        Color acc = ModernAccent;
        using (var halo = new Pen(Color.FromArgb(Night ? 50 : 34, acc), RingW + 16) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(halo, rect, 135, 270 * u);
        const int n = 72;
        int count = (int)Math.Ceiling(u * n);
        for (int i = 0; i < count; i++)
        {
            float s0 = i / (float)n, s1 = Math.Min(u, (i + 1) / (float)n);
            float mid = (s0 + s1) / 2;
            Color col = Lerp(acc, hot, (mid - hotFrom) / (1 - hotFrom));
            using var pen = new Pen(col, RingW);
            g.DrawArc(pen, rect, 135 + 270 * s0, 270 * (s1 - s0) + 0.6f);
        }
        var tip = Polar(c, RingR, 135 + 270 * u);
        using (var pg = new GraphicsPath())
        {
            pg.AddEllipse(Circle(tip, 22));
            using var pb = new PathGradientBrush(pg) { CenterColor = Color.FromArgb(150, 255, 255, 255), SurroundColors = [Color.FromArgb(0, acc)] };
            g.FillPath(pb, pg);
        }
        using (var w = new SolidBrush(Color.White)) g.FillEllipse(w, Circle(tip, 6));
    }

    void ModernLive(Graphics g, DashState s)
    {
        Color acc = ModernAccent;
        ModernArc(g, MTach, rpm / 1000, 8, Color.FromArgb(255, 60, 70), 0.72f);
        ModernArc(g, MSpeed, spd, MaxSpeed, Color.FromArgb(240, 250, 255), 0.4f);

        // Gear in the tach, speed in the speedometer.
        Text(g, BulbCheck ? "8" : s.Gear, F(104, "Bahnschrift SemiBold"), MText, MTach.X, MTach.Y - 6);
        Text(g, $"{rpm:0} rpm", F(15), MDim, MTach.X, MTach.Y + 88);
        Text(g, ((int)Math.Round(spd)).ToString(), F(96, "Bahnschrift Light"), MText, MSpeed.X, MSpeed.Y - 12);

        // Fuel and temperature.
        FillBar(g, ModernBar(MTach), Math.Clamp(fuel, 0, 1), fuel < 0.15f ? Color.FromArgb(255, 176, 32) : acc);
        FillBar(g, ModernBar(MSpeed), Math.Clamp((temp - 40) / 90, 0, 1), temp > 115 ? Color.FromArgb(255, 60, 50) : acc);
        Text(g, $"{fuel * 100:0}%", F(11), MDim, ModernBar(MTach).Right + 8, ModernBar(MTach).Y + 3, StringAlignment.Near);
        Text(g, $"{temp:0}°", F(11), MDim, ModernBar(MSpeed).Right + 8, ModernBar(MSpeed).Y + 3, StringAlignment.Near);

        // Centre screen: clock, music, details, odometer.
        float cx = MScreen.X + MScreen.Width / 2, left = MScreen.X + 18, right = MScreen.Right - 18;
        Text(g, Clock, F(34, "Bahnschrift SemiBold"), MText, cx, 88);
        Text(g, DateTime.Now.ToString("ddd d MMM"), F(13), MDim, cx, 114);

        Text(g, "", F(18, "Segoe MDL2 Assets"), s.NowPlaying.Length > 0 ? acc : MDim, left + 8, 150);
        using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord, LineAlignment = StringAlignment.Center })
        using (var b = new SolidBrush(s.NowPlaying.Length > 0 ? MText : MDim))
            g.DrawString(s.NowPlaying.Length > 0 ? s.NowPlaying : "Nothing playing", F(14), b, new RectangleF(left + 26, 134, right - left - 26, 52), sf);

        (string, string)[] rows = Demo
            ? [("MODE", "Demo drive"), ("ENGINE", $"{rpm:0} rpm"), ("GEAR", s.Gear), ("SPEED", $"{spd:0} {Unit}")]
            : [("CPU", $"{s.CpuPercent:0}%"), ("MEMORY", $"{s.RamPercent:0}%"), ("DOWN", Rate(s.DownBps)), ("UP", Rate(s.UpBps))];
        if (Hint.Length > 0 && t < 9) rows = []; // first start: the tip takes this space
        for (int i = 0; i < rows.Length; i++)
        {
            float y = 214 + i * 24;
            Text(g, rows[i].Item1, F(12, "Bahnschrift SemiBold"), MDim, left, y, StringAlignment.Near);
            Text(g, rows[i].Item2, F(15), MText, right, y, StringAlignment.Far);
        }
        if (Hint.Length > 0 && t < 9)
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var b = new SolidBrush(MText))
                g.DrawString(Hint, F(15), b, new RectangleF(left, 222, right - left, 70), sf);

        Text(g, $"TRIP {ToUnits(s.TripKm):0.0}", F(12), MDim, left, 324, StringAlignment.Near);
        Text(g, $"{ToUnits(s.OdoKm):0} {(Mph ? "mi" : "km")}", F(12), MDim, right, 324, StringAlignment.Far);

        LampRow(g, s, ModernLamps(), Color.Transparent);
    }

    static void FillBar(Graphics g, RectangleF r, float u, Color c)
    {
        if (u <= 0.01f) return;
        var fill = new RectangleF(r.X, r.Y, Math.Max(r.Height, r.Width * u), r.Height);
        using var path = RoundRect(fill, 3);
        using (var glow = new Pen(Color.FromArgb(50, c), 6)) g.DrawPath(glow, path);
        using var b = new SolidBrush(c);
        g.FillPath(b, path);
    }
}
