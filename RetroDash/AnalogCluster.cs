using System.Drawing.Drawing2D;

namespace RetroDash;

/// <summary>Classic cluster: chrome-ringed dials, printed scales that glow at night, orange needles under glass.</summary>
sealed partial class Cluster
{
    static readonly PointF TachC = new(215, 200), SpeedC = new(785, 200);
    const float DialR = 160;
    static readonly PointF FuelC = new(447, 330), TempC = new(553, 330);
    const float SmallR = 44;
    static readonly RectangleF GearBox = new(462, 84, 76, 90), InfoBox = new(404, 186, 192, 60);

    static readonly Color Needle = Color.FromArgb(255, 84, 28);
    static readonly Color RedZone = Color.FromArgb(215, 30, 30);

    /// <summary>Ink of the printed scales: white by day, the backlight colour by night.</summary>
    Color AnalogInk => Night ? AccentOverride ?? Color.FromArgb(255, 112, 40) : Color.FromArgb(236, 238, 240);
    Color LcdInk => Night ? AnalogInk : AccentOverride ?? Color.FromArgb(255, 170, 70);

    static float DialAngle(float v, float max) => 135 + 270 * Math.Clamp(v / max, 0, 1.03f);

    void AnalogBack(Graphics g)
    {
        // Dash top: soft-touch plastic.
        using (var b = new LinearGradientBrush(new RectangleF(0, 0, W, H), Color.FromArgb(38, 38, 42), Color.FromArgb(12, 12, 14), 90f))
            g.FillRectangle(b, 0, 0, W, H);
        Grain(g, new RectangleF(0, 0, W, H), 9000, 10, 7);

        // Binnacle: the recessed hood the dials sit in.
        var hoodR = new RectangleF(10, 12, W - 20, H - 24);
        using (var hood = RoundRect(hoodR, 170))
        {
            using (var b = new LinearGradientBrush(hoodR, Color.FromArgb(6, 6, 7), Color.FromArgb(20, 20, 23), 90f)) g.FillPath(b, hood);
            using (var p = new Pen(Color.FromArgb(70, 255, 255, 255), 1.4f)) g.DrawPath(p, hood);
            using (var p = new Pen(Color.FromArgb(160, 0, 0, 0), 5f)) { g.TranslateTransform(0, 3); g.DrawPath(p, hood); g.TranslateTransform(0, -3); }
        }

        Bezel(g, TachC, DialR);
        Bezel(g, SpeedC, DialR);
        Bezel(g, FuelC, SmallR);
        Bezel(g, TempC, SmallR);

        Color ink = AnalogInk;
        float glow = Night ? 1f : 0;

        // Tachometer: 0-8 x1000 r/min, red from 6500.
        Scale(g, TachC, DialR, 8, 1, 5, 1, 6.5f, ink, glow, v => ((int)v).ToString(), 31);
        GlowText(g, "x1000 r/min", "Bahnschrift", 13, Dim(ink, 0.85f), TachC.X, TachC.Y + 36, glow * 0.5f);

        // Speedometer, with the other unit on a small inner scale like real export clusters.
        float max = MaxSpeed;
        Scale(g, SpeedC, DialR, max, Mph ? 10 : 20, 2, Mph ? 20 : 20, float.MaxValue, ink, glow, v => ((int)v).ToString(), Mph ? 24 : 21);
        GlowText(g, Unit, "Bahnschrift", 15, Dim(ink, 0.85f), SpeedC.X, SpeedC.Y + 36, glow * 0.5f);
        InnerScale(g, Dim(ink, 0.6f));

        // Small gauges.
        SmallScale(g, FuelC, ink, glow, "E", "F", "FUEL", redLow: true);
        SmallScale(g, TempC, ink, glow, "C", "H", "TEMP", redLow: false);

        // LCD windows: clock (tach), odometer (speedo), gear and info (centre).
        Lcd(g, new RectangleF(TachC.X - 46, TachC.Y + 86, 92, 28));
        Lcd(g, new RectangleF(SpeedC.X - 62, SpeedC.Y + 86, 124, 28));
        Lcd(g, GearBox);
        Lcd(g, InfoBox);
        Text(g, "GEAR", F(10, "Bahnschrift SemiBold"), Dim(ink, 0.55f), GearBox.X + GearBox.Width / 2, GearBox.Y - 8);

        // Unlit tell-tales.
        foreach (var (l, r) in AnalogLamps()) DrawLamp(g, l, r, false, Color.FromArgb(34, 36, 40));
    }

    static IEnumerable<(Lamp, RectangleF)> AnalogLamps()
    {
        Lamp[] order = [Lamp.Left, Lamp.HighBeam, Lamp.Engine, Lamp.Battery, Lamp.Fuel, Lamp.Temp, Lamp.Right];
        for (int i = 0; i < order.Length; i++)
            yield return (order[i], new RectangleF(398 + i * 30 + 2, 36, 26, 26));
    }

    /// <summary>Chrome ring with a bevel, a drop shadow and a dark face.</summary>
    static void Bezel(Graphics g, PointF c, float r)
    {
        for (int i = 0; i < 6; i++)
            using (var b = new SolidBrush(Color.FromArgb(26, 0, 0, 0))) g.FillEllipse(b, Circle(c.X, c.Y + 5, r + 14 + i * 2));

        var outer = Circle(c, r + 12);
        using (var b = new LinearGradientBrush(outer, Color.White, Color.Black, 60f))
        {
            b.InterpolationColors = new ColorBlend
            {
                Colors = [Color.FromArgb(250, 251, 253), Color.FromArgb(150, 153, 158), Color.FromArgb(38, 40, 44), Color.FromArgb(200, 203, 208), Color.FromArgb(58, 60, 64)],
                Positions = [0, 0.3f, 0.55f, 0.8f, 1],
            };
            g.FillEllipse(b, outer);
        }
        var inner = Circle(c, r + 5);
        using (var b = new LinearGradientBrush(inner, Color.White, Color.Black, 240f))
        {
            b.InterpolationColors = new ColorBlend
            {
                Colors = [Color.FromArgb(230, 232, 236), Color.FromArgb(90, 92, 96), Color.FromArgb(24, 25, 28)],
                Positions = [0, 0.5f, 1],
            };
            g.FillEllipse(b, inner);
        }
        using (var face = new GraphicsPath())
        {
            face.AddEllipse(Circle(c, r));
            using var pb = new PathGradientBrush(face)
            {
                CenterColor = Color.FromArgb(40, 42, 46),
                SurroundColors = [Color.FromArgb(8, 8, 10)],
                CenterPoint = new PointF(c.X, c.Y - r * 0.35f),
            };
            g.FillPath(pb, face);
        }
        using (var p = new Pen(Color.FromArgb(120, 0, 0, 0), 3)) g.DrawEllipse(p, Circle(c, r - 1.5f));
    }

    static void Scale(Graphics g, PointF c, float r, float max, float major, int minorPer, float labelEvery, float redFrom,
        Color ink, float glow, Func<float, string> label, float fontSize)
    {
        if (redFrom < max)
        {
            float a0 = DialAngle(redFrom, max), a1 = DialAngle(max, max);
            using var red = new Pen(RedZone, 9);
            g.DrawArc(red, Circle(c, r - 12), a0, a1 - a0);
        }
        float step = major / minorPer;
        int n = (int)Math.Round(max / step);
        for (int i = 0; i <= n; i++)
        {
            float v = i * step;
            bool isMajor = i % minorPer == 0;
            float a = DialAngle(v, max);
            Color col = v >= redFrom ? Color.FromArgb(255, 70, 60) : ink;
            float len = isMajor ? 18 : 9;
            var p1 = Polar(c, r - 7, a);
            var p2 = Polar(c, r - 7 - len, a);
            if (glow > 0)
                using (var gp = new Pen(Color.FromArgb((int)(55 * glow), col), isMajor ? 9 : 5) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(gp, p1, p2);
            using var pen = new Pen(col, isMajor ? 3.4f : 1.6f);
            g.DrawLine(pen, p1, p2);

            if (isMajor && Math.Abs(v / labelEvery - MathF.Round(v / labelEvery)) < 0.01f)
            {
                var lp = Polar(c, r - 44, a);
                GlowText(g, label(v), "Bahnschrift SemiBold", fontSize, col, lp.X, lp.Y, glow * 0.7f);
            }
        }
    }

    void InnerScale(Graphics g, Color ink)
    {
        // km/h dial shows mph inside and vice versa.
        float factor = Mph ? 0.621371f : 1.609344f; // other unit → main unit
        float otherMax = MaxSpeed / factor;
        using var pen = new Pen(ink, 1.3f);
        for (int u = 0; u <= otherMax; u += 20)
        {
            float a = DialAngle(u * factor, MaxSpeed);
            g.DrawLine(pen, Polar(SpeedC, DialR - 70, a), Polar(SpeedC, DialR - 76, a));
            if (u % 40 == 0 && u > 0)
            {
                var lp = Polar(SpeedC, DialR - 88, a);
                Text(g, u.ToString(), F(11, "Bahnschrift"), ink, lp.X, lp.Y);
            }
        }
        Text(g, Mph ? "km/h" : "mph", F(10, "Bahnschrift"), ink, SpeedC.X, SpeedC.Y - 46);
    }

    /// <summary>Half-circle scale across the top of a small dial (200° to 340°).</summary>
    static void SmallScale(Graphics g, PointF c, Color ink, float glow, string lo, string hi, string name, bool redLow)
    {
        using var red = new Pen(RedZone, 5);
        if (redLow) g.DrawArc(red, Circle(c, SmallR - 9), 200, 22);
        else g.DrawArc(red, Circle(c, SmallR - 9), 318, 22);
        using var pen = new Pen(ink, 2.2f);
        for (int i = 0; i <= 4; i++)
        {
            float a = 200 + i * 35;
            g.DrawLine(pen, Polar(c, SmallR - 5, a), Polar(c, SmallR - (i % 2 == 0 ? 15 : 11), a));
        }
        var lp = Polar(c, SmallR - 24, 205);
        GlowText(g, lo, "Bahnschrift SemiBold", 12, ink, lp.X, lp.Y, glow * 0.6f);
        lp = Polar(c, SmallR - 24, 335);
        GlowText(g, hi, "Bahnschrift SemiBold", 12, ink, lp.X, lp.Y, glow * 0.6f);
        Text(g, name, F(9, "Bahnschrift SemiBold"), Dim(ink, 0.6f), c.X, c.Y + 18);
    }

    /// <summary>Recessed negative LCD window.</summary>
    static void Lcd(Graphics g, RectangleF r)
    {
        using var path = RoundRect(r, 5);
        using (var b = new LinearGradientBrush(r, Color.FromArgb(4, 6, 5), Color.FromArgb(16, 18, 16), 90f)) g.FillPath(b, path);
        using (var p = new Pen(Color.FromArgb(110, 0, 0, 0), 2)) g.DrawPath(p, path);
        using (var p = new Pen(Color.FromArgb(40, 255, 255, 255), 1))
            g.DrawLine(p, r.X + 5, r.Bottom + 0.5f, r.Right - 5, r.Bottom + 0.5f);
    }

    void AnalogLive(Graphics g, DashState s)
    {
        Color lcd = LcdInk;
        float glow = Night ? 1f : 0.45f;

        // Clock and odometer.
        GlowText(g, Clock, "Bahnschrift SemiBold", 20, lcd, TachC.X, TachC.Y + 100.5f, glow);
        Drum(g, new RectangleF(SpeedC.X - 58, SpeedC.Y + 89, 116, 22), ToUnits(s.OdoKm));

        // Gear.
        string gear = BulbCheck ? "8" : s.Gear;
        GlowText(g, gear, "Bahnschrift SemiBold", 70, lcd, GearBox.X + GearBox.Width / 2, GearBox.Y + GearBox.Height / 2 + 2, glow);

        // Info display: trip on top, a rotating line below.
        GlowText(g, $"TRIP {ToUnits(s.TripKm):0.0} {(Mph ? "mi" : "km")}", "Bahnschrift SemiBold", 15, lcd, InfoBox.X + 10, InfoBox.Y + 16, glow * 0.7f, StringAlignment.Near);
        var clip = g.Clip;
        g.SetClip(RectangleF.Inflate(InfoBox, -6, -2));
        string line = InfoLine(s, false);
        using (var f = new Font(Fam("Bahnschrift"), 15, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            float tw = g.MeasureString(line, f).Width;
            float x = InfoBox.X + 10;
            if (tw > InfoBox.Width - 20) x -= (float)((t * 40) % (tw + 40)) - 20;
            GlowText(g, line, "Bahnschrift", 15, lcd, x, InfoBox.Y + 42, glow * 0.6f, StringAlignment.Near);
        }
        g.Clip = clip;

        LampRow(g, s, AnalogLamps(), Color.Transparent);

        // Needles.
        AnalogNeedle(g, TachC, DialR - 14, 28, 7.5f, DialAngle(rpm / 1000, 8));
        AnalogNeedle(g, SpeedC, DialR - 14, 28, 7.5f, DialAngle(spd, MaxSpeed));
        AnalogNeedle(g, FuelC, SmallR - 8, 8, 4, 200 + 140 * Math.Clamp(fuel, 0, 1));
        AnalogNeedle(g, TempC, SmallR - 8, 8, 4, 200 + 140 * Math.Clamp((temp - 40) / 90, 0, 1));

        Glass(g, TachC, DialR);
        Glass(g, SpeedC, DialR);
        Glass(g, FuelC, SmallR);
        Glass(g, TempC, SmallR);
    }

    void AnalogNeedle(Graphics g, PointF c, float len, float tail, float width, float deg)
    {
        float a = deg * MathF.PI / 180;
        var dir = new PointF(MathF.Cos(a), MathF.Sin(a));
        var nrm = new PointF(-dir.Y, dir.X);
        PointF At(float along, float side) => new(c.X + dir.X * along + nrm.X * side, c.Y + dir.Y * along + nrm.Y * side);
        PointF[] shape = [At(-tail, -width * 0.6f), At(len * 0.2f, -width / 2), At(len, -0.9f), At(len + 2, 0), At(len, 0.9f), At(len * 0.2f, width / 2), At(-tail, width * 0.6f)];

        // Shadow on the face.
        var shadow = shape.Select(p => new PointF(p.X + 3, p.Y + 5)).ToArray();
        using (var sb = new SolidBrush(Color.FromArgb(100, 0, 0, 0))) g.FillPolygon(sb, shadow);

        if (Night)
            using (var gp = new Pen(Color.FromArgb(70, Needle), width * 1.4f) { LineJoin = LineJoin.Round }) g.DrawPolygon(gp, shape);
        using (var b = new LinearGradientBrush(At(0, -width), At(0, width), Color.FromArgb(255, 130, 70), Color.FromArgb(200, 50, 10)))
            g.FillPolygon(b, shape);

        // Hub cap.
        float cap = Math.Max(7, width * 2.3f);
        var capR = Circle(c, cap);
        using (var b = new LinearGradientBrush(capR, Color.FromArgb(90, 92, 98), Color.FromArgb(10, 10, 12), 60f)) g.FillEllipse(b, capR);
        using (var p = new Pen(Color.FromArgb(90, 255, 255, 255), 1)) g.DrawEllipse(p, Circle(c, cap - 1));
    }

    /// <summary>Curved glass reflection across the upper-left of a dial.</summary>
    static void Glass(Graphics g, PointF c, float r)
    {
        using var face = new GraphicsPath();
        face.AddEllipse(Circle(c, r));
        var clip = g.Clip;
        g.SetClip(face, CombineMode.Intersect);
        var hl = new RectangleF(c.X - r * 1.25f, c.Y - r * 1.3f, r * 2.1f, r * 1.55f);
        using (var b = new LinearGradientBrush(hl, Color.FromArgb(34, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 70f))
            g.FillEllipse(b, hl);
        g.Clip = clip;
    }

    /// <summary>Mechanical odometer drums; the tenths drum (and carries) roll smoothly.</summary>
    static void Drum(Graphics g, RectangleF r, double value)
    {
        const int digits = 7; // six whole + tenths
        float cw = r.Width / digits;
        double v = Math.Max(0, value) * 10;
        double whole = Math.Floor(v), frac = v - whole;
        var clip = g.Clip;
        var font = F(r.Height * 0.78f, "Bahnschrift SemiBold");
        for (int i = 0; i < digits; i++)
        {
            int place = digits - 1 - i;
            double pow = Math.Pow(10, place);
            int d = (int)(Math.Floor(whole / pow) % 10);
            double lower = whole % pow;
            double roll = place == 0 || lower == pow - 1 ? frac : 0;
            roll = roll < 0.75 ? 0 : (roll - 0.75) / 0.25; // drums flick over at the end, like the real thing

            var cell = new RectangleF(r.X + i * cw + 0.8f, r.Y, cw - 1.6f, r.Height);
            bool tenths = place == 0;
            using (var b = new LinearGradientBrush(cell, Color.Black, Color.Black, 90f))
            {
                Color mid = tenths ? Color.FromArgb(232, 232, 226) : Color.FromArgb(26, 26, 28);
                b.InterpolationColors = new ColorBlend { Colors = [Dim(mid, 0.35f), mid, Dim(mid, 0.35f)], Positions = [0, 0.5f, 1] };
                g.FillRectangle(b, cell);
            }
            g.SetClip(cell);
            Color ink = tenths ? Color.FromArgb(20, 20, 20) : Color.FromArgb(235, 235, 230);
            float cy = cell.Y + cell.Height / 2 + 1;
            Text(g, d.ToString(), font, ink, cell.X + cell.Width / 2, cy - (float)roll * cell.Height);
            Text(g, ((d + 1) % 10).ToString(), font, ink, cell.X + cell.Width / 2, cy + (1 - (float)roll) * cell.Height);
            g.Clip = clip;
        }
    }
}
