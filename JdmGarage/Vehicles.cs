using System.Drawing.Drawing2D;

namespace JdmGarage;

enum Shape { Hatch, Coupe, Wedge, Sedan, KeiVan, Dekotora }
enum Rim { Mesh, FiveSpoke, SixSpoke, Dish, Steel, Chrome, Gold }

[Flags]
enum Extra
{
    None = 0,
    Wing = 1,          // rear spoiler
    Popups = 2,        // pop-up headlights (up at night)
    Panda = 4,         // two-tone: black lower half
    Takeyari = 8,      // bosozoku bamboo-spear exhausts
    Deppa = 16,        // bosozoku chin spoiler
    Chrome = 32,       // chrome belt line and bumpers
    Stripe = 64,       // side racing stripe
    Low = 128,         // shakotan: slammed on the ground
    Ornament = 256,    // hood ornament
}

/// <summary>One vehicle in the garage, and the radio that's fitted in it.</summary>
sealed record Car(
    string Name, string Japanese, string Year, Shape Shape,
    Color Paint, Color Second, Rim Rim, Extra Extras,
    string Radio, string[] Specs, string Note, string Mural = "");

static class Garage
{
    static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

    public static readonly Car[] Cars =
    [
        new("SPRINTER TRUENO AE86", "ハチロク", "1985", Shape.Hatch, C(242, 242, 238), C(22, 22, 24), Rim.Mesh,
            Extra.Popups | Extra.Panda, "TOUGE 86",
            ["4A-GE  1.6 L  I4", "130 PS  ·  FR", "5-SPEED MANUAL", "940 KG"],
            "Mountain pass legend. Best heard at 4 a.m. on the way down."),
        new("SKYLINE GT-R  R34", "スカイライン", "1999", Shape.Sedan, C(30, 70, 170), C(18, 18, 20), Rim.SixSpoke,
            Extra.Wing, "KENSEI DDX-9",
            ["RB26DETT  2.6 L  TWIN TURBO", "280 PS  ·  ATTESA AWD", "6-SPEED GETRAG", "1560 KG"],
            "Godzilla in Bayside Blue. The multi-function display was ahead of its time."),
        new("SUPRA RZ  JZA80", "スープラ", "1993", Shape.Coupe, C(236, 110, 20), C(20, 20, 22), Rim.FiveSpoke,
            Extra.Wing, "XPLODE GT-2001",
            ["2JZ-GTE  3.0 L  TWIN TURBO", "280 PS  ·  FR", "6-SPEED V160", "1510 KG"],
            "Sequential turbos and an amp that can shake the mirrors loose."),
        new("RX-7 TYPE R  FD3S", "アールエックスセブン", "1992", Shape.Wedge, C(200, 16, 24), C(20, 20, 22), Rim.FiveSpoke,
            Extra.Popups | Extra.Wing, "NEON 2003",
            ["13B-REW  TWIN ROTOR", "255 PS  ·  FR", "5-SPEED MANUAL", "1260 KG"],
            "Brap brap. Keep the revs up and the neon on."),
        new("NSX  NA1", "エヌエスエックス", "1990", Shape.Wedge, C(236, 236, 230), C(16, 16, 18), Rim.SixSpoke,
            Extra.Popups, "CARROZZA DEH-9",
            ["C30A  3.0 L  V6 VTEC", "280 PS  ·  MIDSHIP", "5-SPEED MANUAL", "1350 KG"],
            "Aluminium supercar you can drive every day. Clean stereo, clean lines."),
        new("SILVIA SPEC-R  S15", "シルビア", "1999", Shape.Coupe, C(120, 70, 160), C(20, 20, 22), Rim.Mesh,
            Extra.Wing | Extra.Low, "NAKAMURA TD-1200",
            ["SR20DET  2.0 L  TURBO", "250 PS  ·  FR", "6-SPEED MANUAL", "1250 KG"],
            "Drift missile. The tape deck survived every spin."),
        new("KAIDO RACER  C210", "街道レーサー", "1979", Shape.Sedan, C(96, 30, 130), C(236, 190, 60), Rim.Dish,
            Extra.Takeyari | Extra.Deppa | Extra.Stripe | Extra.Low | Extra.Wing, "KAIDO RACER",
            ["L28  2.8 L  I6", "OIL COOLER  ·  FR", "TAKEYARI EXHAUST", "SHAKOTAN"],
            "Bosozoku style: bamboo-spear pipes, a chin spoiler to the floor and the loudest horn in town."),
        new("CENTURY  VIP", "センチュリー", "1997", Shape.Sedan, C(14, 14, 18), C(200, 170, 90), Rim.Chrome,
            Extra.Chrome | Extra.Ornament | Extra.Low, "IMPERIAL GOLD",
            ["1GZ-FE  5.0 L  V12", "280 PS  ·  FR", "WOOL SEATS, LACE COVERS", "2050 KG"],
            "The only Japanese V12. Rear-seat remote for the radio, naturally."),
        new("CIMA  Y31", "シーマ", "1988", Shape.Sedan, C(232, 120, 180), C(60, 200, 210), Rim.Mesh,
            Extra.Chrome | Extra.Stripe, "VAPOR 1989",
            ["VG30DET  3.0 L  V6 TURBO", "255 PS  ·  FR", "BUBBLE-ERA LUXURY", "1580 KG"],
            "Bought with bubble-economy money. City pop on permanent repeat."),
        new("HIJET  KEI VAN", "軽バン", "1990", Shape.KeiVan, C(236, 226, 196), C(90, 150, 200), Rim.Steel,
            Extra.None, "POCKETSONIC PS-89",
            ["EB  660 CC  3-CYL", "40 PS  ·  4WD", "4-SPEED MANUAL", "760 KG"],
            "Tiny, slow and unstoppable. Delivers groceries and good vibes."),
        new("DEKOTORA  KINRYU", "デコトラ 金龍", "1983", Shape.Dekotora, C(200, 28, 28), C(236, 196, 70), Rim.Gold,
            Extra.Chrome, "DEKOTORA GOLD",
            ["10 T  CAB-OVER", "V8  DIESEL", "400+ MARKER LIGHTS", "HAND-PAINTED MURALS"],
            "Golden Dragon. Chrome everywhere, a painted dragon on the box and a radio made of gold.", "金龍"),
        new("DEKOTORA  SAKURAMARU", "デコトラ 桜丸", "1987", Shape.Dekotora, C(240, 150, 190), C(250, 240, 250), Rim.Chrome,
            Extra.Chrome, "SAKURA POP",
            ["4 T  CAB-OVER", "I6  DIESEL", "PINK UNDERGLOW", "SAKURA MURALS"],
            "Cherry-blossom art truck. Every light is a different shade of pink.", "桜丸"),
        new("DEKOTORA  GINGA", "デコトラ 銀河", "1991", Shape.Dekotora, C(20, 40, 110), C(120, 220, 255), Rim.Chrome,
            Extra.Chrome, "HAULER HX",
            ["10 T  CAB-OVER", "V10  DIESEL", "STAINLESS BUMPERS", "GALAXY MURALS"],
            "Galaxy runner. Hauls fish overnight from Aomori with the CB and the tape deck on.", "銀河"),
    ];

    /// <summary>How long the vehicle is drawn, relative to a normal car.</summary>
    public static float LengthOf(Car c) => c.Shape switch
    {
        Shape.Dekotora => 1.28f,
        Shape.KeiVan => 0.78f,
        _ when c.Name.StartsWith("CENTURY", StringComparison.Ordinal) => 1.1f,
        _ => 1f,
    };

    // Side profiles, front to the right, ground at y = 0, a car is 100 units long.
    static readonly Dictionary<Shape, (PointF[] Body, PointF[] Glass, float[] Wheels, float Radius)> Profiles = new()
    {
        [Shape.Hatch] = (P(1, -8, 0, -15, 0.5f, -23, 4, -26, 21, -33.6f, 25, -34.3f, 53, -34.5f, 66, -24.5f, 97, -21.5f, 100, -18, 100, -10, 98, -7),
                         P(24, -32.3f, 52, -32.8f, 62, -25.5f, 10, -25.8f), [17, 81], 7.2f),
        [Shape.Coupe] = (P(0, -11, 1, -20, 4, -24, 14, -26, 22, -28, 34, -33, 42, -34, 52, -33.5f, 62, -26, 90, -21, 98, -17, 100, -12, 99, -8, 1, -8),
                         P(34, -31.5f, 51, -32, 59, -26.5f, 25, -26.6f), [18, 81], 7.5f),
        [Shape.Wedge] = (P(0, -12, 1, -20, 8, -24, 20, -26, 36, -31, 46, -32, 56, -30, 64, -24, 92, -18, 99, -14, 100, -10, 98, -7, 2, -7),
                         P(36, -29.5f, 52, -29.5f, 60, -24.5f, 28, -25), [18, 82], 7.3f),
        [Shape.Sedan] = (P(0, -11, 0.5f, -22, 2, -25, 15, -26, 24, -35, 28, -36, 56, -36, 68, -26, 97, -24, 100, -19, 100, -10, 98, -7, 2, -7),
                         P(26, -34, 55, -34, 64, -26.5f, 21, -26.5f), [17, 81], 7.3f),
        [Shape.KeiVan] = (P(0, -8, 0, -41, 2, -45, 62, -46, 88, -45.5f, 96, -39, 100, -26, 100, -9, 98, -6, 2, -6),
                          P(67, -43.5f, 88, -43, 95, -32, 67, -31), [17, 82], 6.8f),
    };

    static PointF[] P(params float[] v)
    {
        var pts = new PointF[v.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = new(v[i * 2], v[i * 2 + 1]);
        return pts;
    }

    /// <summary>
    /// Draws the vehicle side-on with the rear at x = 0 and the ground at y = 0, <paramref name="len"/> pixels long.
    /// <paramref name="spin"/> turns the wheels, <paramref name="t"/> runs the light shows.
    /// </summary>
    public static void Draw(Graphics g, Car c, float len, float spin, float t, bool night)
    {
        var state = g.Save();
        float u = len / 100f;
        g.ScaleTransform(u, u);
        if (c.Shape == Shape.Dekotora) DrawTruck(g, c, spin, t, night);
        else DrawCar(g, c, spin, t, night);
        g.Restore(state);
    }

    static void DrawCar(Graphics g, Car c, float spin, float t, bool night)
    {
        var (body, glass, wheels, radius) = Profiles[c.Shape];
        float drop = c.Extras.HasFlag(Extra.Low) ? 1.6f : 0;
        var st = g.Save();
        g.TranslateTransform(0, drop);

        // Exhausts behind the body.
        if (c.Extras.HasFlag(Extra.Takeyari))
        {
            using var pipe = new Pen(Color.FromArgb(200, 200, 205), 2.2f) { EndCap = LineCap.Round };
            using var tip = new Pen(Color.FromArgb(90, 90, 96), 2.2f);
            for (int i = 0; i < 2; i++)
            {
                float x = 6 + i * 3.2f;
                g.DrawLine(pipe, x, -11, x - 8 - i * 2, -34 - i * 4);
                g.DrawLine(tip, x - 7.5f - i * 2, -32.5f - i * 4, x - 8 - i * 2, -34 - i * 4);
            }
        }

        using var path = new GraphicsPath();
        path.AddClosedCurve(body, 0.12f);
        var bounds = path.GetBounds();

        // Paint: light on the shoulder, darker towards the sills.
        using (var paint = new LinearGradientBrush(new RectangleF(0, bounds.Top, 1, bounds.Height + 1),
            FaceplateLight(c.Paint, 0.35f), FaceplateDark(c.Paint, 0.35f), 90f))
        {
            paint.InterpolationColors = new ColorBlend
            {
                Colors = [FaceplateLight(c.Paint, 0.25f), FaceplateLight(c.Paint, 0.45f), c.Paint, FaceplateDark(c.Paint, 0.25f), FaceplateDark(c.Paint, 0.5f)],
                Positions = [0f, 0.3f, 0.45f, 0.8f, 1f],
            };
            g.FillPath(paint, path);
        }

        var clip = g.Clip;
        g.SetClip(path, CombineMode.Intersect);
        if (c.Extras.HasFlag(Extra.Panda))
        {
            using var lower = new SolidBrush(c.Second);
            g.FillRectangle(lower, -5, -15.5f, 115, 20);
        }
        if (c.Extras.HasFlag(Extra.Stripe))
        {
            using var stripe = new SolidBrush(c.Second);
            g.FillRectangle(stripe, -5, -17.5f, 115, 2.2f);
            g.FillRectangle(stripe, -5, -14.3f, 115, 0.9f);
        }
        if (c.Extras.HasFlag(Extra.Chrome))
        {
            using var chrome = new LinearGradientBrush(new RectangleF(0, -19, 1, 1.6f), Color.White, Color.FromArgb(120, 124, 132), 90f);
            g.FillRectangle(chrome, -5, -19, 115, 1.2f);
        }
        // Shoulder highlight and sill shadow.
        using (var sill = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            g.FillRectangle(sill, -5, -9.5f, 115, 4);
        g.Clip = clip;

        // Windows with a sky reflection.
        using (var gp = new GraphicsPath())
        {
            gp.AddClosedCurve(glass, 0.08f);
            var gb = gp.GetBounds();
            using var gl = new LinearGradientBrush(new RectangleF(gb.X, gb.Y - 0.1f, gb.Width, gb.Height + 0.2f),
                night ? Color.FromArgb(40, 52, 78) : Color.FromArgb(120, 150, 180), Color.FromArgb(14, 18, 26), 70f);
            g.FillPath(gl, gp);
            var cs = g.Save();
            g.SetClip(gp, CombineMode.Intersect);
            using var streak = new SolidBrush(Color.FromArgb(night ? 30 : 60, 255, 255, 255));
            g.FillPolygon(streak, [new PointF(gb.X + gb.Width * 0.3f, gb.Top), new PointF(gb.X + gb.Width * 0.45f, gb.Top), new PointF(gb.X + gb.Width * 0.3f, gb.Bottom), new PointF(gb.X + gb.Width * 0.15f, gb.Bottom)]);
            g.Restore(cs);
            using var frame = new Pen(Color.FromArgb(20, 20, 24), 0.6f);
            g.DrawPath(frame, gp);
            // B-pillar.
            float bx = c.Shape == Shape.KeiVan ? 79 : gb.X + gb.Width * (c.Shape == Shape.Sedan ? 0.5f : 0.62f);
            using var pillar = new Pen(Color.FromArgb(24, 24, 28), 1.4f);
            g.DrawLine(pillar, bx, gb.Top, bx, gb.Bottom);
        }
        if (c.Shape == Shape.KeiVan)
        {
            using var rear = new GraphicsPath();
            rear.AddClosedCurve(P(5, -43, 60, -43.5f, 60, -31, 5, -31), 0.08f);
            using var gl = new SolidBrush(night ? Color.FromArgb(34, 44, 64) : Color.FromArgb(90, 118, 146));
            g.FillPath(gl, rear);
            using var band = new SolidBrush(c.Second);
            g.FillRectangle(band, 0.5f, -25, 99, 3);
        }

        // Panel lines.
        using (var seam = new Pen(Color.FromArgb(70, 0, 0, 0), 0.35f))
        {
            float door0 = glass.Min(p => p.X) + 2, door1 = c.Shape == Shape.KeiVan ? 66 : glass.Max(p => p.X) - 2;
            g.DrawLine(seam, door0, -26, door0 + 1, -9);
            g.DrawLine(seam, door1, -26, door1 - 1, -9);
            g.DrawLine(seam, door1 - 6, -19, door1 - 3, -19);
        }

        // Lights.
        float front = body.Max(p => p.X), topFront = c.Shape switch { Shape.KeiVan => -24, Shape.Coupe or Shape.Wedge => -15.5f, _ => -19 };
        using (var tail = new SolidBrush(night ? Color.FromArgb(255, 50, 40) : Color.FromArgb(170, 20, 20)))
            g.FillRectangle(tail, -0.2f, topFront - 1.5f, 2, 3.2f);
        using (var head = new SolidBrush(night ? Color.FromArgb(255, 250, 225) : Color.FromArgb(200, 205, 210)))
        {
            if (c.Extras.HasFlag(Extra.Popups) && night)
            {
                using var pod = new SolidBrush(FaceplateDark(c.Paint, 0.2f));
                float py = body.Where(p => p.X > 85).Min(p => p.Y);
                g.FillRectangle(pod, front - 13, py - 1.8f, 7, 3f);
                g.FillRectangle(head, front - 6.4f, py - 1.6f, 0.9f, 2.6f);
            }
            else if (!c.Extras.HasFlag(Extra.Popups))
                g.FillRectangle(head, front - 2.2f, topFront - 1.2f, 2.2f, 2.4f);
            else
                g.FillRectangle(head, front - 1.3f, -13.5f, 1.3f, 1.5f); // turn signal / fog lamp
        }
        using (var bumper = new SolidBrush(c.Extras.HasFlag(Extra.Chrome) ? Color.FromArgb(210, 212, 218) : Color.FromArgb(40, FaceplateDark(c.Paint, 0.6f))))
        {
            g.FillRectangle(bumper, front - 3, -11, 3.2f, 1.6f);
            g.FillRectangle(bumper, -0.2f, -11, 3.2f, 1.6f);
        }
        if (c.Extras.HasFlag(Extra.Ornament))
        {
            using var orn = new SolidBrush(Color.FromArgb(230, 200, 110));
            g.FillEllipse(orn, 92, -26.8f, 1.6f, 2.4f);
        }
        if (c.Extras.HasFlag(Extra.Deppa))
        {
            using var chin = new SolidBrush(FaceplateDark(c.Paint, 0.3f));
            g.FillPolygon(chin, [new PointF(front - 10, -8), new PointF(front + 1, -9.5f), new PointF(front + 3, -4.2f), new PointF(front - 4, -3.6f)]);
        }

        // Rear wing.
        if (c.Extras.HasFlag(Extra.Wing))
        {
            float wy = c.Shape switch { Shape.Sedan => -25.6f, Shape.Hatch => -34, _ => body.Where(p => p.X < 20).Min(p => p.Y) };
            bool tall = c.Shape == Shape.Coupe || c.Extras.HasFlag(Extra.Takeyari);
            using var wing = new SolidBrush(FaceplateDark(c.Paint, 0.15f));
            using var stand = new SolidBrush(FaceplateDark(c.Paint, 0.45f));
            float h = tall ? 5f : 2.2f;
            g.FillRectangle(stand, 8, wy - h, 1.4f, h);
            g.FillPolygon(wing, [new PointF(1, wy - h - 0.2f), new PointF(15, wy - h - 1f), new PointF(15.5f, wy - h + 0.8f), new PointF(1.5f, wy - h + 1.2f)]);
        }

        g.Restore(st);

        // Wheel wells and wheels (these stay on the ground when the car is lowered).
        foreach (float wx in wheels)
            DrawWheel(g, c.Rim, wx, radius, spin, drop);
    }

    static void DrawWheel(Graphics g, Rim rim, float x, float r, float spin, float drop)
    {
        using (var well = new SolidBrush(Color.FromArgb(10, 10, 12)))
            g.FillEllipse(well, x - r - 1.2f, -r * 2 - 1.2f + drop * 0.5f, (r + 1.2f) * 2, (r + 1.2f) * 2);
        using (var tire = new SolidBrush(Color.FromArgb(24, 24, 26)))
            g.FillEllipse(tire, x - r, -r * 2, r * 2, r * 2);
        using (var wall = new Pen(Color.FromArgb(46, 46, 50), 0.4f))
            g.DrawEllipse(wall, x - r * 0.9f, -r - r * 0.9f, r * 1.8f, r * 1.8f);

        float rr = r * (rim == Rim.Steel ? 0.6f : 0.72f);
        var center = new PointF(x, -r);
        var (face, lip) = rim switch
        {
            Rim.Gold => (Color.FromArgb(222, 180, 70), Color.FromArgb(255, 230, 150)),
            Rim.Chrome or Rim.Dish => (Color.FromArgb(220, 224, 230), Color.White),
            Rim.Steel => (Color.FromArgb(200, 200, 196), Color.FromArgb(230, 230, 226)),
            Rim.Mesh => (Color.FromArgb(190, 192, 198), Color.FromArgb(235, 236, 240)),
            _ => (Color.FromArgb(150, 154, 162), Color.FromArgb(215, 218, 224)),
        };
        using (var disc = new LinearGradientBrush(new RectangleF(x - rr, -r - rr, rr * 2, rr * 2), FaceplateLight(face, 0.2f), FaceplateDark(face, 0.35f), 60f))
            g.FillEllipse(disc, x - rr, -r - rr, rr * 2, rr * 2);
        using (var lipPen = new Pen(lip, rim == Rim.Dish ? 1.4f : 0.5f))
            g.DrawEllipse(lipPen, x - rr, -r - rr, rr * 2, rr * 2);

        using var dark = new SolidBrush(Color.FromArgb(40, 42, 46));
        using var spoke = new Pen(FaceplateDark(face, 0.1f), 0.9f);
        int n = rim switch { Rim.Mesh => 12, Rim.FiveSpoke => 5, Rim.SixSpoke => 6, Rim.Dish => 8, Rim.Steel => 0, _ => 10 };
        float inner = rim == Rim.Dish ? rr * 0.55f : rr * 0.9f;
        for (int i = 0; i < n; i++)
        {
            double a = spin + i * Math.PI * 2 / n;
            if (rim is Rim.FiveSpoke or Rim.SixSpoke)
            {
                // Dark gaps between thick spokes.
                double a2 = a + Math.PI / n;
                var p1 = new PointF(center.X + (float)Math.Cos(a2 - 0.28) * rr * 0.3f, center.Y + (float)Math.Sin(a2 - 0.28) * rr * 0.3f);
                var p2 = new PointF(center.X + (float)Math.Cos(a2 - 0.3) * inner, center.Y + (float)Math.Sin(a2 - 0.3) * inner);
                var p3 = new PointF(center.X + (float)Math.Cos(a2 + 0.3) * inner, center.Y + (float)Math.Sin(a2 + 0.3) * inner);
                var p4 = new PointF(center.X + (float)Math.Cos(a2 + 0.28) * rr * 0.3f, center.Y + (float)Math.Sin(a2 + 0.28) * rr * 0.3f);
                g.FillPolygon(dark, [p1, p2, p3, p4]);
            }
            else
                g.DrawLine(spoke, center.X + (float)Math.Cos(a) * rr * 0.25f, center.Y + (float)Math.Sin(a) * rr * 0.25f,
                    center.X + (float)Math.Cos(a) * inner, center.Y + (float)Math.Sin(a) * inner);
        }
        if (rim == Rim.Steel)
            for (int i = 0; i < 4; i++)
            {
                double a = spin + i * Math.PI / 2;
                g.FillEllipse(dark, center.X + (float)Math.Cos(a) * rr * 0.6f - 0.6f, center.Y + (float)Math.Sin(a) * rr * 0.6f - 0.6f, 1.2f, 1.2f);
            }
        using var cap = new SolidBrush(FaceplateLight(face, 0.3f));
        g.FillEllipse(cap, x - rr * 0.22f, -r - rr * 0.22f, rr * 0.44f, rr * 0.44f);
    }

    // ---------- Dekotora art truck ----------

    static readonly Color[] MarkerColors =
    [
        Color.FromArgb(255, 70, 60), Color.FromArgb(255, 170, 40), Color.FromArgb(80, 255, 120),
        Color.FromArgb(60, 180, 255), Color.FromArgb(255, 90, 220),
    ];

    static void DrawTruck(Graphics g, Car c, float spin, float t, bool night)
    {
        var chromeTop = Color.FromArgb(250, 250, 252);
        var chromeLow = Color.FromArgb(120, 124, 134);

        // Chassis and underglow.
        using (var frame = new SolidBrush(Color.FromArgb(28, 28, 30)))
            g.FillRectangle(frame, 4, -11, 92, 3.5f);
        if (night)
        {
            using var glowPath = new GraphicsPath();
            glowPath.AddEllipse(-4, -6, 108, 10);
            using var glow = new PathGradientBrush(glowPath) { CenterColor = Color.FromArgb(150, c.Second), SurroundColors = [Color.FromArgb(0, c.Second)] };
            g.FillPath(glow, glowPath);
        }

        // The box, with its mural.
        var box = new RectangleF(0, -43, 77, 31);
        using (var bb = new LinearGradientBrush(box, FaceplateLight(c.Paint, 0.15f), FaceplateDark(c.Paint, 0.35f), 90f))
            g.FillRectangle(bb, box);
        DrawMural(g, c, RectangleF.Inflate(box, -3.2f, -3.2f), t, night);
        using (var chrome = new LinearGradientBrush(new RectangleF(0, box.Y, 1, box.Height), chromeTop, chromeLow, 90f))
        using (var trim = new Pen(chrome, 1.6f))
        {
            g.DrawRectangle(trim, box.X + 0.8f, box.Y + 0.8f, box.Width - 1.6f, box.Height - 1.6f);
            // Ribbed stainless skirt under the box.
            using var skirt = new LinearGradientBrush(new RectangleF(0, -12, 1, 3), chromeTop, chromeLow, 90f);
            g.FillRectangle(skirt, 2, -12, 75, 2.4f);
        }

        // Marker lights along the top and bottom of the box, chasing at night.
        for (int i = 0; i < 26; i++)
        {
            float x = 2 + i * 2.95f;
            DrawMarker(g, x, box.Top - 1.5f, i, t, night, 0);
            DrawMarker(g, x, box.Bottom + 0.1f, i, t, night, 13);
        }

        // Cab.
        var cab = P(79, -10, 79, -37, 83, -39.5f, 97, -39.5f, 100, -34, 100.5f, -10);
        using (var cp = new GraphicsPath())
        {
            cp.AddClosedCurve(cab, 0.06f);
            using var cb = new LinearGradientBrush(new RectangleF(79, -40, 1, 31), FaceplateLight(c.Paint, 0.3f), FaceplateDark(c.Paint, 0.3f), 90f);
            g.FillPath(cb, cp);
            var cs = g.Save();
            g.SetClip(cp, CombineMode.Intersect);
            using var band = new SolidBrush(c.Second);
            g.FillRectangle(band, 78, -22, 24, 3);
            g.Restore(cs);
        }
        // Windows.
        using (var gl = new LinearGradientBrush(new RectangleF(83, -37, 17, 11), night ? Color.FromArgb(50, 60, 90) : Color.FromArgb(130, 160, 190), Color.FromArgb(16, 20, 28), 70f))
        {
            g.FillPolygon(gl, [new PointF(83.5f, -36.5f), new PointF(91, -36.5f), new PointF(91, -27.5f), new PointF(83.5f, -27.5f)]);
            g.FillPolygon(gl, [new PointF(92.5f, -36.5f), new PointF(97.2f, -36.5f), new PointF(99.6f, -30), new PointF(99.6f, -27.5f), new PointF(92.5f, -27.5f)]);
        }
        // Visor with the andon (name board) and lights on the roof.
        using (var visor = new LinearGradientBrush(new RectangleF(80, -46, 1, 6), chromeTop, chromeLow, 90f))
            g.FillRectangle(visor, 80, -45.5f, 21.5f, 5.5f);
        using (var andon = new SolidBrush(night ? Color.FromArgb(255, 250, 230) : Color.FromArgb(235, 232, 220)))
            g.FillRectangle(andon, 82, -44.6f, 17.5f, 3.8f);
        using (var ink = new SolidBrush(c.Paint))
        using (var font = new Font("MS Gothic", 3.1f, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(c.Mural, font, ink, new RectangleF(82, -44.8f, 17.5f, 4.2f), fmt);
        }
        for (int i = 0; i < 9; i++) DrawMarker(g, 80.4f + i * 2.5f, -47f, i, t, night, 3);

        // Chrome bumper, grille, headlights, horns.
        using (var bump = new LinearGradientBrush(new RectangleF(0, -12, 1, 5), chromeTop, chromeLow, 90f))
        {
            g.FillRectangle(bump, 97, -13, 6, 5.6f);
            g.FillRectangle(bump, 88, -11.5f, 12, 1.6f);
            g.FillRectangle(bump, 81, -24, 1.3f, 12); // grab rail
        }
        using (var head = new SolidBrush(night ? Color.FromArgb(255, 250, 225) : Color.FromArgb(210, 214, 220)))
            g.FillRectangle(head, 99.6f, -18, 1.4f, 3);
        using (var horn = new SolidBrush(Color.FromArgb(230, 232, 236)))
        {
            g.FillPolygon(horn, [new PointF(85, -45.5f), new PointF(89, -47.2f), new PointF(89, -46.2f)]);
            g.FillPolygon(horn, [new PointF(90, -45.5f), new PointF(94, -47.2f), new PointF(94, -46.2f)]);
        }
        // Mudflaps.
        using (var flap = new SolidBrush(FaceplateDark(c.Paint, 0.2f)))
        {
            g.FillRectangle(flap, 38, -9, 1.2f, 7);
            g.FillRectangle(flap, 83, -9, 1.2f, 6);
        }

        // Wheels: dual rear axle and the steering axle.
        foreach (float wx in new[] { 17f, 29f, 91f })
            DrawWheel(g, c.Rim, wx, 5.4f, spin * 1.35f, 0);
    }

    static void DrawMarker(Graphics g, float x, float y, int i, float t, bool night, int phase)
    {
        var col = MarkerColors[(i + phase) % MarkerColors.Length];
        bool on = !night || ((int)(t * 8) + i) % 3 != 0;
        if (night && on)
        {
            using var halo = new SolidBrush(Color.FromArgb(70, col));
            g.FillEllipse(halo, x - 0.7f, y - 0.7f, 2.4f, 2.4f);
        }
        using var lamp = new SolidBrush(on ? (night ? FaceplateLight(col, 0.4f) : col) : FaceplateDark(col, 0.55f));
        g.FillEllipse(lamp, x - 0.2f, y - 0.2f, 1.4f, 1.4f);
    }

    /// <summary>The hand-painted side of the box: a sky, waves, a sun and the truck's name in big letters.</summary>
    static void DrawMural(Graphics g, Car c, RectangleF r, float t, bool night)
    {
        var st = g.Save();
        g.SetClip(r, CombineMode.Intersect);
        using (var sky = new LinearGradientBrush(r, FaceplateDark(c.Paint, 0.5f), FaceplateLight(c.Paint, 0.25f), 90f))
            g.FillRectangle(sky, r);

        // Rising sun rays.
        var sun = new PointF(r.X + r.Width * 0.72f, r.Y + r.Height * 0.62f);
        using (var ray = new SolidBrush(Color.FromArgb(70, c.Second)))
            for (int i = 0; i < 12; i++)
            {
                double a = Math.PI + i * Math.PI / 11;
                g.FillPolygon(ray, [sun,
                    new(sun.X + (float)Math.Cos(a - 0.06) * 60, sun.Y + (float)Math.Sin(a - 0.06) * 60),
                    new(sun.X + (float)Math.Cos(a + 0.06) * 60, sun.Y + (float)Math.Sin(a + 0.06) * 60)]);
            }
        using (var disc = new SolidBrush(FaceplateLight(c.Second, 0.2f)))
            g.FillEllipse(disc, sun.X - 6, sun.Y - 6, 12, 12);

        // Waves (Hokusai-ish curls).
        using (var sea = new SolidBrush(FaceplateDark(c.Paint, 0.55f)))
        using (var foam = new Pen(Color.FromArgb(230, 245, 245, 250), 0.6f))
        {
            using var wp = new GraphicsPath();
            var pts = new List<PointF> { new(r.Left, r.Bottom) };
            for (float x = r.Left; x <= r.Right + 4; x += 4)
                pts.Add(new(x, r.Bottom - 7 - (float)Math.Sin(x * 0.35 + t * 1.2) * 1.8f));
            pts.Add(new(r.Right, r.Bottom));
            wp.AddPolygon([.. pts]);
            g.FillPath(sea, wp);
            for (float x = r.Left + 2; x < r.Right; x += 8)
            {
                float y = r.Bottom - 7 - (float)Math.Sin(x * 0.35 + t * 1.2) * 1.8f;
                g.DrawArc(foam, x, y - 2, 4, 4, 180, 250);
            }
        }

        // Petals or stars drifting across.
        using (var fleck = new SolidBrush(Color.FromArgb(night ? 230 : 180, FaceplateLight(c.Second, 0.5f))))
            for (int i = 0; i < 14; i++)
            {
                float x = r.X + (float)((i * 37.3 + t * 3) % r.Width);
                float y = r.Y + (float)((i * 13.7) % (r.Height * 0.6));
                g.FillEllipse(fleck, x, y, 0.9f, 0.9f);
            }

        // The name, brush-painted with an outline.
        using (var font = new Font("MS Mincho", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var path = new GraphicsPath())
        {
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            path.AddString(c.Mural, font.FontFamily, (int)FontStyle.Bold, 13f, new RectangleF(r.X, r.Y + 1, r.Width * 0.62f, r.Height - 6), fmt);
            using var outline = new Pen(Color.FromArgb(20, 10, 10), 1.3f) { LineJoin = LineJoin.Round };
            g.DrawPath(outline, path);
            using var gold = new LinearGradientBrush(r, FaceplateLight(c.Second, 0.45f), FaceplateDark(c.Second, 0.25f), 90f);
            g.FillPath(gold, path);
        }
        g.Restore(st);
    }

    public static Color FaceplateLight(Color c, float t) => Mix(c, Color.White, t);
    public static Color FaceplateDark(Color c, float t) => Mix(c, Color.Black, t);
    static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb(a.A, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}
