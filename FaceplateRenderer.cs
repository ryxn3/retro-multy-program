using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RetroRadio;

/// <summary>Turns a <see cref="RadioDesign"/> into a realistic, lit faceplate image (shared by the radio and Radio Designer).</summary>
static class FaceplateRenderer
{
    /// <summary>What changes from frame to frame: drawn on top of the cached faceplate.</summary>
    public sealed record Live(Btn Hover, Btn Pressed, bool Playing, float Volume, float Speed, bool Muted, double Now, Color MuteColor, bool Night = false);

    static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);
    public static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    public static Color Lighter(Color c, float t) => Mix(c, Color.White, t);
    public static Color Darker(Color c, float t) => Mix(c, Color.Black, t);

    public static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        KeyDesign.AddRoundRect(p, r, rad);
        return p;
    }

    static GraphicsPath Ellipse(PointF c, float r)
    {
        var p = new GraphicsPath();
        p.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
        return p;
    }

    static float Luma(Color c) => (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;

    /// <summary>Keeps printed text legible: swaps to near-black or near-white when it would blend into its background.</summary>
    public static Color Readable(Color text, Color background)
    {
        float bg = Luma(background);
        if (Math.Abs(Luma(text) - bg) >= 0.3f) return text;
        return bg > 0.5f ? Color.FromArgb(text.A, 30, 30, 34) : Color.FromArgb(text.A, 232, 234, 238);
    }

    static bool IsTransport(Btn id) => id is Btn.Prev or Btn.Play or Btn.Next or Btn.SeekBack or Btn.SeekFwd;

    // A fresh object each time: GDI+ objects must not be shared between the UI thread and background renders.
    static StringFormat Centered => new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    static StringFormat Fmt(TextAlign a) => new()
    {
        Alignment = a switch { TextAlign.Left => StringAlignment.Near, TextAlign.Right => StringAlignment.Far, _ => StringAlignment.Center },
        LineAlignment = StringAlignment.Center,
    };

    static Font LabelFont(float size) => new("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);

    static string? iconFamily;

    /// <summary>Windows 11's icon font, or Windows 10's.</summary>
    static Font IconFont(float size)
    {
        if (iconFamily == null)
        {
            using var probe = new Font("Segoe Fluent Icons", 10);
            iconFamily = probe.Name == "Segoe Fluent Icons" ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        }
        return new Font(iconFamily, size, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    /// <summary>Modern icons for each control, from the Segoe icon font.</summary>
    public static string IconFor(Btn id) => id switch
    {
        Btn.Open => "\uE8E5", Btn.Folder => "\uE8B7", Btn.List => "\uE8FD", Btn.Settings => "\uE713", Btn.Vis => "\uE9D2",
        Btn.Mute => "\uE74F", Btn.VolUp => "\uE995", Btn.VolDown => "\uE993", Btn.Shuffle => "\uE8B1", Btn.Repeat => "\uE8EE",
        Btn.Stop => "\uE71A", Btn.Power => "\uE7E8", Btn.Prev => "\uE892", Btn.Next => "\uE893", Btn.Play => "\uE768",
        Btn.SeekBack => "\uEB9E", Btn.SeekFwd => "\uEB9D", Btn.Eject => "\uE8B6", Btn.Detach => "\uE8A7",
        _ => "",
    };

    /// <summary>Draws a key's icon glyph (play/pause swaps with the playing state).</summary>
    static void DrawGlyph(Graphics g, KeyDesign k, RectangleF box, Color color, bool playing = false)
    {
        string glyph = k.Id == Btn.Play ? (playing ? "\uE769" : "\uE768") : k.Icon;
        using var f = IconFont(Math.Clamp(Math.Min(k.W, k.H) * 0.46f, 9, 30));
        using var b = new SolidBrush(color);
        g.DrawString(glyph, f, b, box, Centered);
    }

    // ───────────────────────────── full render ─────────────────────────────

    /// <summary>
    /// Renders the faceplate. With <paramref name="pressed"/>, every key is drawn pushed in (used for press animations).
    /// With <paramref name="night"/>, the room light is off and the illumination glows, like a car at night.
    /// </summary>
    public static Bitmap Render(RadioDesign d, float s, bool pressed = false, bool night = false)
    {
        // Rendered at twice the size, then scaled down smoothly: no stair-stepped edges or pixel noise.
        const int Supersample = 2;
        using var big = RenderAt(d, s * Supersample, pressed, night);
        var bmp = new Bitmap((int)Math.Ceiling(d.Width * s), (int)Math.Ceiling(d.Height * s), PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        using var wrap = new ImageAttributes();
        wrap.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(big, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, big.Width, big.Height, GraphicsUnit.Pixel, wrap);
        return bmp;
    }

    static Bitmap RenderAt(RadioDesign d, float s, bool pressed, bool night)
    {
        using var r = new Relief(d.Width, d.Height, s);
        var g = r.G;
        var face = d.Face.R;

        // Nothing around the unit: the window is cut to the faceplate's shape.
        g.Clear(Color.Black);

        using var facePath = RoundRect(face, d.Corner);
        PaintFinish(g, facePath, d);
        r.Emboss(facePath, 12, Math.Clamp(d.Corner * 0.5f, 3, 7), Profile.Round, d.FaceMaterial);

        PaintTrim(r, d);
        foreach (var bed in d.Beds)
        {
            using var bp = RoundRect(bed.Rect, bed.Radius);
            using var bb = new SolidBrush(Darker(d.Face3, 0.45f));
            g.FillPath(bb, bp);
            r.Emboss(bp, -4, 3, Profile.Round, Mat.Plastic);
        }
        PaintDisplayWindow(r, d);
        PaintExtras(r, d);
        foreach (var k in d.Keys)
        {
            if (k.Kind == KeyKind.Knob) PaintKnob(r, d, k);
            else PaintKey(r, d, k, pressed);
        }
        PaintPrinting(g, d);

        var bmp = r.Bake(night);
        using (var eg = Graphics.FromImage(bmp))
        {
            eg.SmoothingMode = SmoothingMode.AntiAlias;
            eg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            eg.ScaleTransform(s, s);
            PaintEmissive(eg, d, night);
        }
        return bmp;
    }

    static void PaintFinish(Graphics g, GraphicsPath face, RadioDesign d)
    {
        var box = d.Face.R;
        using (var fb = new LinearGradientBrush(box, Color.White, Color.Black, 90f))
        {
            fb.InterpolationColors = new ColorBlend
            {
                Colors = [d.Face1, Mix(d.Face1, d.Face2, 0.6f), d.Face2, Mix(d.Face2, d.Face3, 0.4f), d.Face3],
                Positions = [0f, 0.1f, 0.52f, 0.9f, 1f],
            };
            g.FillPath(fb, face);
        }

        var saved = g.Save();
        g.SetClip(face);
        var rnd = new Random(7);
        switch (d.Finish)
        {
            case FaceFinish.Brushed:
                using (var light = new Pen(Color.FromArgb(26, 255, 255, 255), 0.6f))
                using (var dark = new Pen(Color.FromArgb(20, 0, 0, 0), 0.6f))
                {
                    int n = (int)(box.Width * box.Height / 450);
                    for (int i = 0; i < n; i++)
                    {
                        float y = box.Top + (float)rnd.NextDouble() * box.Height;
                        float x0 = box.Left + (float)rnd.NextDouble() * box.Width * 0.7f;
                        float len = 60 + (float)rnd.NextDouble() * 500;
                        g.DrawLine(i % 2 == 0 ? light : dark, x0, y, x0 + len, y);
                    }
                }
                // Broad anisotropic sheen bands.
                using (var band = new LinearGradientBrush(box, Color.Transparent, Color.Transparent, 0f))
                {
                    band.InterpolationColors = new ColorBlend
                    {
                        Colors = [Color.FromArgb(0, 255, 255, 255), Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(30, 0, 0, 0), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(28, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)],
                        Positions = [0f, 0.18f, 0.34f, 0.5f, 0.66f, 0.82f, 1f],
                    };
                    g.FillRectangle(band, box);
                }
                break;

            case FaceFinish.Matte:
                using (var l = new SolidBrush(Color.FromArgb(16, 255, 255, 255)))
                using (var dk = new SolidBrush(Color.FromArgb(32, 0, 0, 0)))
                {
                    int n = (int)(box.Width * box.Height / 55);
                    for (int i = 0; i < n; i++)
                    {
                        float x = box.Left + (float)rnd.NextDouble() * box.Width;
                        float y = box.Top + (float)rnd.NextDouble() * box.Height;
                        g.FillRectangle(i % 2 == 0 ? l : dk, x, y, 0.9f, 0.9f);
                    }
                }
                break;

            case FaceFinish.Gloss:
            case FaceFinish.Plain:
                if (d.Finish == FaceFinish.Gloss)
                {
                    using var sheen = new LinearGradientBrush(new RectangleF(box.X, box.Y, box.Width, box.Height * 0.45f),
                        Color.FromArgb(50, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f);
                    g.FillRectangle(sheen, box.X, box.Y, box.Width, box.Height * 0.45f);
                }
                break;

            case FaceFinish.Wood:
            {
                int n = (int)(box.Height * 0.4f);
                for (int i = 0; i < n; i++)
                {
                    float y0 = box.Top - 10 + (float)rnd.NextDouble() * (box.Height + 20);
                    float amp = 1.5f + (float)rnd.NextDouble() * 4;
                    float f = 1f / (50 + (float)rnd.NextDouble() * 90);
                    float ph = (float)rnd.NextDouble() * 6.28f, ph2 = (float)rnd.NextDouble() * 6.28f;
                    var pts = new List<PointF>();
                    for (float x = box.Left - 5; x <= box.Right + 5; x += 6)
                        pts.Add(new PointF(x, y0 + amp * MathF.Sin(x * f + ph) + 1.2f * MathF.Sin(x * f * 4.3f + ph2)));
                    bool streak = i % 5 == 0;
                    var col = streak ? Color.FromArgb(22 + rnd.Next(20), 220, 160, 100) : Color.FromArgb(30 + rnd.Next(60), 40, 18, 6);
                    using var pen = new Pen(col, 0.5f + (float)rnd.NextDouble() * 1.8f);
                    g.DrawCurve(pen, pts.ToArray(), 0.5f);
                }
                break;
            }

            case FaceFinish.Carbon:
                using (var tile = new Bitmap(12, 12, PixelFormat.Format32bppArgb))
                {
                    using (var tg = Graphics.FromImage(tile))
                    {
                        for (int cy = 0; cy < 2; cy++)
                        for (int cx = 0; cx < 2; cx++)
                        {
                            var cell = new RectangleF(cx * 6, cy * 6, 6, 6);
                            using var cb = new LinearGradientBrush(RectangleF.Inflate(cell, 0.01f, 0.01f), d.Face3, d.Face3, (cx + cy) % 2 == 0 ? 0 : 90)
                            {
                                InterpolationColors = new ColorBlend { Colors = [d.Face3, Lighter(d.Face1, 0.08f), d.Face3], Positions = [0f, 0.5f, 1f] },
                            };
                            tg.FillRectangle(cb, cell);
                        }
                    }
                    using var tb = new TextureBrush(tile, WrapMode.Tile);
                    g.FillPath(tb, face);
                }
                break;
        }
        g.Restore(saved);
    }

    static void PaintTrim(Relief r, RadioDesign d)
    {
        var g = r.G;
        var face = d.Face.R;
        if (d.ChromeTrim)
        {
            using var ring = RoundRect(RectangleF.Inflate(face, -3, -3), Math.Max(1, d.Corner - 3));
            using var inner = RoundRect(RectangleF.Inflate(face, -10, -10), Math.Max(1, d.Corner - 10));
            ring.AddPath(inner, false);
            ring.FillMode = FillMode.Alternate;
            using var cb = new LinearGradientBrush(face, C(245, 245, 242), C(150, 150, 146), 90f);
            g.FillPath(cb, ring);
            r.Emboss(ring, 3, 2, Profile.Round, Mat.Chrome);
        }
        if (d.Pinstripe)
        {
            using var stripe = new Pen(d.Trim, 1.4f);
            using var inset = RoundRect(RectangleF.Inflate(face, -7, -7), Math.Max(1, d.Corner - 4));
            g.DrawPath(stripe, inset);
        }
        if (d.Bolts)
        {
            foreach (var c in new PointF[] { new(face.X + 20, face.Y + 20), new(face.Right - 20, face.Y + 20), new(face.X + 20, face.Bottom - 20), new(face.Right - 20, face.Bottom - 20) })
            {
                using var bolt = Ellipse(c, 7.5f);
                using var bb = new SolidBrush(C(176, 178, 180));
                g.FillPath(bb, bolt);
                r.Emboss(bolt, 4, 3, Profile.Round, Mat.Chrome);
                using var hex = new GraphicsPath();
                hex.AddPolygon(Enumerable.Range(0, 6).Select(i => new PointF(c.X + 3.4f * MathF.Cos(i * MathF.PI / 3), c.Y + 3.4f * MathF.Sin(i * MathF.PI / 3))).ToArray());
                using var hb = new SolidBrush(C(40, 40, 42));
                g.FillPath(hb, hex);
                r.Emboss(hex, -2.5f, 0.8f, Profile.Linear, Mat.Plastic);
            }
        }
        if (d.ShowSeam)
        {
            using var seam = RoundRect(d.Seam.R, 1);
            using var sb = new SolidBrush(Darker(d.Face3, 0.6f));
            g.FillPath(sb, seam);
            r.Emboss(seam, -1.6f, 0.8f, Profile.Linear);
        }
    }

    static void PaintDisplayWindow(Relief r, RadioDesign d)
    {
        var g = r.G;
        var bezel = d.Bezel.R;
        var glass = d.Glass;
        float bc = Math.Clamp(d.Corner - 4, 4, 18);

        using (var groove = RoundRect(RectangleF.Inflate(bezel, 3, 3), bc + 3))
        {
            using var gb = new SolidBrush(Darker(d.Face3, 0.55f));
            g.FillPath(gb, groove);
            r.Emboss(groove, -2, 1.5f);
        }
        using (var body = RoundRect(bezel, bc))
        {
            using var bb = new LinearGradientBrush(bezel, d.Bezel1, d.Bezel2, 90f);
            g.FillPath(bb, body);
            r.Emboss(body, 3, 3, Profile.Round, d.BezelMaterial);
        }
        using (var window = RoundRect(RectangleF.Inflate(glass, 7, 7), 8))
        {
            using var wb = new SolidBrush(C(8, 8, 10));
            g.FillPath(wb, window);
            r.Emboss(window, -9, 7, Profile.Linear, Mat.Gloss);
        }
        using (var lens = RoundRect(glass, 6))
        {
            g.FillPath(Brushes.Black, lens);
            r.SetMaterial(lens, Mat.Glass);
        }
    }

    static void PaintExtras(Relief r, RadioDesign d)
    {
        var g = r.G;
        if (d.ShowSlot)
        {
            var slot = d.Slot.R;
            var lipR = RectangleF.Inflate(slot, 12, 8);
            using var lip = RoundRect(lipR, lipR.Height / 2);
            using (var lb = new SolidBrush(C(30, 30, 32))) g.FillPath(lb, lip);
            r.Emboss(lip, 3, 3, Profile.Round, Mat.Rubber);
            using var hole = RoundRect(slot, slot.Height / 2);
            g.FillPath(Brushes.Black, hole);
            r.Emboss(hole, -12, 1, Profile.Linear);
        }
        if (d.ShowIr)
        {
            using var ir = RoundRect(d.Ir.R, 4);
            using var ib = new LinearGradientBrush(d.Ir.R, C(70, 12, 18), C(25, 4, 6), 90f);
            g.FillPath(ib, ir);
            r.Emboss(ir, -1.5f, 1, Profile.Round, Mat.Gloss);
        }
        if (d.ShowUsb)
        {
            using var usb = RoundRect(d.Usb.R, 4);
            using var ub = new SolidBrush(Darker(d.Face3, 0.4f));
            g.FillPath(ub, usb);
            r.Emboss(usb, 2.5f, 2, Profile.Round, Mat.Rubber);
            using var f = LabelFont(7.5f);
            using var tb = new SolidBrush(Color.FromArgb(190, Readable(d.Print, Darker(d.Face3, 0.4f))));
            g.DrawString("USB", f, tb, d.Usb.R, Centered);
        }
        if (d.ShowAux)
        {
            var c = d.Aux.P;
            using var ring = Ellipse(c, 9);
            using (var rb = new SolidBrush(C(200, 200, 204))) g.FillPath(rb, ring);
            r.Emboss(ring, 2, 1.5f, Profile.Round, Mat.Chrome);
            using var hole = Ellipse(c, 4.5f);
            g.FillPath(Brushes.Black, hole);
            r.Emboss(hole, -10, 1, Profile.Linear);
            using var f = LabelFont(7.5f);
            using var tb = new SolidBrush(d.Print);
            g.DrawString("AUX", f, tb, new RectangleF(c.X - 42, c.Y - 7, 28, 14), Fmt(TextAlign.Right));
        }
    }

    // ───────────────────────────── keys ─────────────────────────────

    static (Color C1, Color C2, Color Text, Mat Mat) KeyLook(RadioDesign d, KeyDesign k)
    {
        if (k.Kind == KeyKind.Dome) return (Lighter(d.DomeColor, 0.35f), Darker(d.DomeColor, 0.45f), Color.White, Mat.Gloss);
        var look = IsTransport(k.Id) || k.Kind is KeyKind.ChevronL or KeyKind.ChevronR
            ? (d.Transport1, d.Transport2, d.TransportText, d.TransportMaterial)
            : (d.Key1, d.Key2, d.KeyText, d.KeyMaterial);
        if (k.Kind == KeyKind.Chrome) look.Item4 = Mat.Chrome;
        return look;
    }

    static void PaintKey(Relief r, RadioDesign d, KeyDesign k, bool pressed)
    {
        var g = r.G;
        var (c1, c2, text, mat) = KeyLook(d, k);
        if (pressed)
        {
            c1 = Darker(c1, 0.12f);
            c2 = Darker(c2, 0.12f);
        }

        // Dark gap around the key.
        if (k.Kind != KeyKind.Touch)
        {
            using var well = k.Path(2.5f);
            using var wb = new SolidBrush(Darker(d.Face3, 0.65f));
            g.FillPath(wb, well);
            r.Emboss(well, -2.5f, 1);
        }

        using var path = k.Path();
        var b = path.GetBounds();
        using (var fill = new LinearGradientBrush(RectangleF.Inflate(b, 0.5f, 0.5f), c1, c2, 90f)) g.FillPath(fill, path);
        if (k.Kind == KeyKind.Touch)
        {
            using var ring = new Pen(Color.FromArgb(120, d.KeyEdge), 1);
            g.DrawPath(ring, path);
        }

        float minSide = Math.Min(k.W, k.H);
        (float elev, float bevel, Profile prof) = k.Kind switch
        {
            KeyKind.Dome => (pressed ? 4f : 10f, minSide / 2, Profile.Round),
            KeyKind.Round => (pressed ? 2.5f : 7f, minSide * 0.28f, Profile.Round),
            KeyKind.Chrome => (pressed ? 2f : 6f, 3f, Profile.Smooth),
            KeyKind.Touch => (pressed ? 0.2f : 0.8f, 1f, Profile.Round),
            _ => (pressed ? 2f : 6f, Math.Min(5, minSide * 0.3f), Profile.Round),
        };
        r.Emboss(path, elev, bevel, prof, mat);

        // Printed legend (the play/pause symbol is drawn live because it changes).
        var face = k.Tilt != 0 ? k.Rect : b;
        text = Readable(text, Mix(c1, c2, 0.5f));
        if (k.Icon.Length > 0)
        {
            if (k.Id != Btn.Play) DrawGlyph(g, k, face, text);
        }
        else if (k.LabelAt == LabelAt.On && k.Label.Length > 0)
        {
            bool seek = k.Id is Btn.SeekBack or Btn.SeekFwd;
            var labelBox = seek ? new RectangleF(face.X + (k.Id == Btn.SeekBack ? 16 : 0), face.Y, face.Width - 16, face.Height) : face;
            using var f = LabelFont(Math.Min(9, k.H * 0.42f));
            using var tb = new SolidBrush(text);
            g.DrawString(k.Label, f, tb, labelBox, Centered);
            if (seek) DrawIcon(g, k.Id, new PointF(k.Id == Btn.SeekBack ? face.X + 15 : face.Right - 15, face.Y + face.Height / 2), text, 0.75f);
        }
        else if (k.Id != Btn.Play)
        {
            DrawIcon(g, k.Id, new PointF(face.X + face.Width / 2, face.Y + face.Height / 2), text, Math.Clamp(minSide / 40, 0.6f, 1.3f));
        }
        PaintOuterLabel(g, d, k);
    }

    static void PaintOuterLabel(Graphics g, RadioDesign d, KeyDesign k)
    {
        if (k.Label.Length == 0 || k.LabelAt is LabelAt.On or LabelAt.None) return;
        var rc = k.Rect;
        RectangleF box = k.LabelAt switch
        {
            LabelAt.Left => new(rc.X - 76, rc.Y - 4, 70, rc.Height + 8),
            LabelAt.Right => new(rc.Right + 6, rc.Y - 4, 70, rc.Height + 8),
            LabelAt.Above => new(rc.X - 30, rc.Y - 18, rc.Width + 60, 14),
            _ => new(rc.X - 30, rc.Bottom + 3, rc.Width + 60, 14),
        };
        var align = k.LabelAt switch { LabelAt.Left => TextAlign.Right, LabelAt.Right => TextAlign.Left, _ => TextAlign.Center };
        using var f = LabelFont(9);
        using var tb = new SolidBrush(Readable(d.Print, d.Face2));
        g.DrawString(k.Label, f, tb, box, Fmt(align));
    }

    /// <summary>Transport symbols: ◀◀ ▶▶ ⏏ ▶ ❚❚.</summary>
    public static void DrawIcon(Graphics g, Btn id, PointF c, Color color, float scale, bool playing = false)
    {
        using var ink = new SolidBrush(color);
        float cx = c.X, cy = c.Y, s = scale;
        switch (id)
        {
            case Btn.Play when playing:
                g.FillRectangle(ink, cx - 8 * s, cy - 8 * s, 5 * s, 16 * s);
                g.FillRectangle(ink, cx + 3 * s, cy - 8 * s, 5 * s, 16 * s);
                break;
            case Btn.Play:
                g.FillPolygon(ink, new PointF[] { new(cx - 6 * s, cy - 9 * s), new(cx + 10 * s, cy), new(cx - 6 * s, cy + 9 * s) });
                break;
            case Btn.Eject:
                g.FillPolygon(ink, new PointF[] { new(cx - 8 * s, cy + 1 * s), new(cx, cy - 8 * s), new(cx + 8 * s, cy + 1 * s) });
                g.FillRectangle(ink, cx - 8 * s, cy + 4 * s, 16 * s, 3.5f * s);
                break;
            case Btn.Prev or Btn.Next or Btn.SeekBack or Btn.SeekFwd:
                bool left = id is Btn.Prev or Btn.SeekBack;
                for (int k = 0; k < 2; k++)
                {
                    float x = cx + (left ? 5 - k * 10 : -5 + k * 10) * s;
                    PointF[] tri = left
                        ? [new(x - 6 * s, cy), new(x + 4 * s, cy - 6 * s), new(x + 4 * s, cy + 6 * s)]
                        : [new(x + 6 * s, cy), new(x - 4 * s, cy - 6 * s), new(x - 4 * s, cy + 6 * s)];
                    g.FillPolygon(ink, tri);
                }
                break;
        }
    }

    static void PaintKnob(Relief r, RadioDesign d, KeyDesign k)
    {
        var g = r.G;
        var c = k.Center;
        float rad = k.Radius;

        using (var well = Ellipse(c, rad + 7))
        {
            using var wb = new SolidBrush(Darker(d.Face3, 0.6f));
            g.FillPath(wb, well);
            r.Emboss(well, -6, 3);
        }
        using (var body = Ellipse(c, rad))
        {
            using var bb = new LinearGradientBrush(new RectangleF(c.X - rad, c.Y - rad, rad * 2, rad * 2), d.Knob1, d.Knob2, 90f);
            g.FillPath(bb, body);
            r.Emboss(body, 16, Math.Max(2.5f, rad * 0.11f), Profile.Round, d.KnobMaterial);
        }

        // Knurled grip around the rim.
        int ridges = Math.Clamp((int)(rad * 1.6f), 24, 90);
        using (var dark = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
        {
            for (int i = 0; i < ridges; i++)
            {
                float a = i * MathF.PI * 2 / ridges;
                using var ridge = new GraphicsPath();
                float w = MathF.PI * 2 * rad / ridges * 0.45f, len = Math.Max(3, rad * 0.14f);
                ridge.AddRectangle(new RectangleF(rad - len, -w / 2, len, w));
                using var m = new Matrix();
                m.Translate(c.X, c.Y);
                m.Rotate(a * 180 / MathF.PI);
                ridge.Transform(m);
                g.FillPath(dark, ridge);
                r.Emboss(ridge, 0.9f, 0.5f, Profile.Round);
            }
        }

        // Cap with spun-metal rings.
        float capR = rad - Math.Max(6, rad * 0.2f);
        using (var cap = Ellipse(c, capR))
        {
            using var cb = new LinearGradientBrush(new RectangleF(c.X - capR, c.Y - capR, capR * 2, capR * 2), Mix(d.Knob1, d.Knob2, 0.15f), Mix(d.Knob1, d.Knob2, 0.75f), 60f);
            g.FillPath(cb, cap);
            if (d.KnobMaterial is Mat.Chrome or Mat.Brushed or Mat.Gloss)
            {
                for (float rr = 2; rr < capR; rr += 1.3f)
                {
                    using var p = new Pen(Color.FromArgb(((int)(rr * 7)) % 2 == 0 ? 22 : 12, (int)rr % 2 == 0 ? Color.White : Color.Black), 0.6f);
                    g.DrawEllipse(p, c.X - rr, c.Y - rr, rr * 2, rr * 2);
                }
            }
            r.Emboss(cap, 1.2f, 1.5f, Profile.Round);
        }
        PaintOuterLabel(g, d, k);
    }

    static void PaintPrinting(Graphics g, RadioDesign d)
    {
        foreach (var p in d.Prints)
        {
            using var f = LabelFont(p.Size);
            using var b = new SolidBrush(Color.FromArgb(200, d.Print));
            g.DrawString(p.Text, f, b, p.Rect, Fmt(p.Align));
        }

        var style = FontStyle.Bold | (d.BrandItalic ? FontStyle.Italic : 0);
        using var brandFont = new Font(d.BrandFont, d.BrandSize, style, GraphicsUnit.Pixel);
        using var brandInk = new SolidBrush(d.BrandInk);
        DrawAnchored(g, d.Brand, brandFont, brandInk, d.BrandAt.P, d.BrandAlign);
        using var modelFont = LabelFont(8.5f);
        using var modelInk = new SolidBrush(Color.FromArgb(190, d.Print));
        DrawAnchored(g, d.Model, modelFont, modelInk, d.ModelAt.P, d.ModelAlign);
    }

    static void DrawAnchored(Graphics g, string text, Font f, Brush b, PointF at, TextAlign align)
    {
        var size = g.MeasureString(text, f);
        float x = align switch { TextAlign.Center => at.X - size.Width / 2, TextAlign.Right => at.X - size.Width, _ => at.X };
        g.DrawString(text, f, b, x, at.Y);
    }

    /// <summary>Things that give off light, drawn after the lighting pass so shading doesn't dim them.</summary>
    static void PaintEmissive(Graphics g, RadioDesign d, bool night)
    {
        if (d.NeonBezel)
        {
            using var bz = RoundRect(d.Bezel.R, Math.Clamp(d.Corner - 4, 4, 18));
            using var halo = new Pen(Color.FromArgb(60, d.BezelRim), 6f);
            using var line = new Pen(d.BezelRim, 1.3f);
            g.DrawPath(halo, bz);
            g.DrawPath(line, bz);
        }
        foreach (var k in d.Keys)
        {
            if (k.Kind == KeyKind.Knob)
            {
                if (!d.GlowRings) continue;
                // Illuminated ring just outside the knob.
                float rad = k.Radius;
                var c = k.Center;
                using var outer = Ellipse(c, rad + 7);
                using var gb = new PathGradientBrush(outer)
                {
                    CenterColor = night ? Lighter(d.Glow, 0.15f) : d.Glow,
                    SurroundColors = [Color.FromArgb(0, d.Glow)],
                    FocusScales = new PointF(rad / (rad + 7), rad / (rad + 7)),
                };
                using var knob = Ellipse(c, rad);
                using var region = new Region(outer);
                region.Exclude(knob);
                var saved = g.Clip;
                g.Clip = region;
                g.FillPath(gb, outer);
                g.Clip = saved;
                continue;
            }
            if (d.NeonKeys)
            {
                using var path = k.Path();
                using var halo = new Pen(Color.FromArgb(55, d.KeyEdge), 5f);
                using var line = new Pen(d.KeyEdge, 1.3f);
                g.DrawPath(halo, path);
                g.DrawPath(line, path);
            }
            if (night && k.LabelAt is not (LabelAt.On or LabelAt.None) && k.Label.Length > 0)
            {
                // Printed labels next to keys catch a little of the illumination.
                var rc = k.Rect;
                RectangleF box = k.LabelAt switch
                {
                    LabelAt.Left => new(rc.X - 76, rc.Y - 4, 70, rc.Height + 8),
                    LabelAt.Right => new(rc.Right + 6, rc.Y - 4, 70, rc.Height + 8),
                    LabelAt.Above => new(rc.X - 30, rc.Y - 18, rc.Width + 60, 14),
                    _ => new(rc.X - 30, rc.Bottom + 3, rc.Width + 60, 14),
                };
                var align = k.LabelAt switch { LabelAt.Left => TextAlign.Right, LabelAt.Right => TextAlign.Left, _ => TextAlign.Center };
                using var f = LabelFont(9);
                using var tb = new SolidBrush(Color.FromArgb(150, d.NightLight));
                g.DrawString(k.Label, f, tb, box, Fmt(align));
            }
            if ((d.BacklitLabels || night) && k.LabelAt == LabelAt.On && k.Kind != KeyKind.Dome)
            {
                var light = night ? d.NightLight : d.Glow;
                var glowText = Lighter(light, night ? 0.25f : 0.55f);
                using var path = k.Path();
                var b = path.GetBounds();
                using var halo = new SolidBrush(Color.FromArgb(night ? 60 : 45, light));
                if (k.Icon.Length > 0)
                {
                    if (k.Id != Btn.Play)
                    {
                        foreach (var o in new[] { new PointF(-1, 0), new PointF(1, 0), new PointF(0, -1), new PointF(0, 1) })
                            DrawGlyph(g, k, new RectangleF(b.X + o.X, b.Y + o.Y, b.Width, b.Height), Color.FromArgb(night ? 60 : 45, light));
                        DrawGlyph(g, k, b, glowText);
                    }
                }
                else if (k.Label.Length > 0)
                {
                    bool seek = k.Id is Btn.SeekBack or Btn.SeekFwd;
                    var box = seek ? new RectangleF(b.X + (k.Id == Btn.SeekBack ? 16 : 0), b.Y, b.Width - 16, b.Height) : b;
                    using var f = LabelFont(Math.Min(9, k.H * 0.42f));
                    foreach (var o in new[] { new PointF(-1, 0), new PointF(1, 0), new PointF(0, -1), new PointF(0, 1) })
                        g.DrawString(k.Label, f, halo, new RectangleF(box.X + o.X, box.Y + o.Y, box.Width, box.Height), Centered);
                    using var tb = new SolidBrush(glowText);
                    g.DrawString(k.Label, f, tb, box, Centered);
                    if (seek) DrawIcon(g, k.Id, new PointF(k.Id == Btn.SeekBack ? b.X + 15 : b.Right - 15, b.Y + b.Height / 2), glowText, 0.75f);
                }
                else if (k.Id != Btn.Play)
                {
                    DrawIcon(g, k.Id, new PointF(b.X + b.Width / 2, b.Y + b.Height / 2), glowText, Math.Clamp(Math.Min(k.W, k.H) / 40, 0.6f, 1.3f));
                }
            }
        }
    }

    // ───────────────────────────── live overlay ─────────────────────────────

    /// <summary>Area to copy from the "all pressed" render when a key is held down.</summary>
    public static RectangleF PressRegion(KeyDesign k)
    {
        using var p = k.Path();
        return RectangleF.Inflate(p.GetBounds(), 14, 14);
    }

    /// <summary>Hover highlights, the play/pause symbol, knob pointers and indicator lights.</summary>
    public static void DrawLive(Graphics g, RadioDesign d, Live st)
    {
        foreach (var k in d.Keys)
        {
            bool down = st.Pressed == k.Id;
            if (st.Hover == k.Id && !down)
            {
                using var path = k.Path();
                using var hv = new SolidBrush(Color.FromArgb(24, 255, 255, 255));
                g.FillPath(hv, path);
            }
            if (k.Kind == KeyKind.Knob) DrawPointer(g, d, k, st);
            else if (k.Id == Btn.Play) DrawPlayKey(g, d, k, st, down);
        }
        DrawLights(g, d, st);
    }

    static void DrawPlayKey(Graphics g, RadioDesign d, KeyDesign k, Live st, bool down)
    {
        using var path = k.Path();
        var b = path.GetBounds();
        var c = new PointF(b.X + b.Width / 2, b.Y + b.Height / 2 + (down ? 1.2f : 0));
        if (st.Playing && d.GlowRings)
        {
            float pulse = 0.75f + 0.25f * (float)Math.Sin(st.Now * 3);
            if (k.Kind == KeyKind.Dome)
            {
                var halo = RectangleF.Inflate(b, 12, 10);
                using var hp = new GraphicsPath();
                hp.AddEllipse(halo);
                using var hb = new PathGradientBrush(hp) { CenterColor = Color.FromArgb((int)(110 * pulse), d.Glow), SurroundColors = [Color.FromArgb(0, d.Glow)] };
                g.FillPath(hb, hp);
            }
            else
            {
                using var lit = new SolidBrush(Color.FromArgb((int)(60 * pulse), d.Glow));
                g.FillPath(lit, path);
            }
        }
        var (_, _, text, _) = KeyLook(d, k);
        var color = st.Night ? Lighter(d.NightLight, 0.25f)
            : k.Kind == KeyKind.Dome ? Color.FromArgb(240, 250, 255)
            : d.BacklitLabels ? Lighter(d.Glow, 0.55f) : text;
        if (k.Icon.Length > 0) DrawGlyph(g, k, new RectangleF(b.X, b.Y + (down ? 1.2f : 0), b.Width, b.Height), color, st.Playing);
        else DrawIcon(g, Btn.Play, c, color, Math.Clamp(Math.Min(k.W, k.H) / 44, 0.6f, 1.3f), st.Playing);
    }

    static void DrawPointer(Graphics g, RadioDesign d, KeyDesign k, Live st)
    {
        bool isSpeed = k.Id == Btn.SpeedKnob;
        float frac = isSpeed ? (MathF.Log2(st.Speed) + 1) / 2 : st.Volume;
        double ang = (-225 + 270 * frac) * Math.PI / 180;
        float px = (float)Math.Cos(ang), py = (float)Math.Sin(ang);
        var c = k.Center;
        float rad = k.Radius, capR = rad - Math.Max(6, rad * 0.2f);
        var color = isSpeed
            ? (Math.Abs(st.Speed - 1) < 0.001f ? d.Pointer : Color.FromArgb(255, 190, 90))
            : (st.Muted ? st.MuteColor : d.Pointer);
        float w = Math.Clamp(rad / 10, 2.2f, 5);
        using var groove = new Pen(Color.FromArgb(120, 0, 0, 0), w + 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(groove, c.X + px * capR * 0.3f, c.Y + py * capR * 0.3f, c.X + px * capR * 0.92f, c.Y + py * capR * 0.92f);
        if (st.Night)
        {
            // Illuminated pointer.
            using var glow = new Pen(Color.FromArgb(70, color), w + 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(glow, c.X + px * capR * 0.3f, c.Y + py * capR * 0.3f, c.X + px * capR * 0.9f, c.Y + py * capR * 0.9f);
        }
        using var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, c.X + px * capR * 0.3f, c.Y + py * capR * 0.3f, c.X + px * capR * 0.9f, c.Y + py * capR * 0.9f);
    }

    static void DrawLights(Graphics g, RadioDesign d, Live st)
    {
        var led = st.Playing ? d.Led : Darker(d.Led, 0.72f);
        foreach (var p in d.Leds) DrawLed(g, new RectangleF(p.X, p.Y, 12, 5), led, st.Playing);
        bool blink = !st.Playing && (st.Now % 1.2) < 0.15;
        DrawLed(g, new RectangleF(d.TheftLed.X, d.TheftLed.Y, 7, 7), blink || st.Muted ? C(255, 40, 40) : C(90, 20, 20), blink || st.Muted);
    }

    public static void DrawLed(Graphics g, RectangleF r, Color c, bool lit)
    {
        if (lit)
        {
            var halo = RectangleF.Inflate(r, 8, 8);
            using var hp = new GraphicsPath();
            hp.AddEllipse(halo);
            using var hb = new PathGradientBrush(hp) { CenterColor = Color.FromArgb(150, c), SurroundColors = [Color.FromArgb(0, c)] };
            g.FillPath(hb, hp);
        }
        using var p = RoundRect(r, Math.Min(r.Width, r.Height) / 2);
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }
}
