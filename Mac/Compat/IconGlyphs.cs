using SkiaSharp;

namespace System.Drawing
{
    /// <summary>
    /// Vector stand-ins for the Segoe icon-font glyphs the modern radios put on their keys
    /// (Microsoft's icon fonts aren't on a Mac). Each is drawn in a unit square.
    /// </summary>
    internal static class IconGlyphs
    {
        public static void Draw(SKCanvas c, string s, float em, RectangleF layout, StringFormat? fmt, SKPaint fill)
        {
            if (s.Length == 0) return;
            float size = em * 0.92f;
            var ha = fmt?.Alignment ?? StringAlignment.Near;
            var va = fmt?.LineAlignment ?? StringAlignment.Near;
            float cx = layout.Width > 0
                ? ha switch { StringAlignment.Center => layout.X + layout.Width / 2, StringAlignment.Far => layout.Right - size / 2, _ => layout.X + size / 2 }
                : ha switch { StringAlignment.Center => layout.X, StringAlignment.Far => layout.X - size / 2, _ => layout.X + size / 2 };
            float cy = layout.Height > 0
                ? va switch { StringAlignment.Center => layout.Y + layout.Height / 2, StringAlignment.Far => layout.Bottom - size / 2, _ => layout.Y + size / 2 }
                : va switch { StringAlignment.Center => layout.Y, StringAlignment.Far => layout.Y - size / 2, _ => layout.Y + size / 2 };

            c.Save();
            c.Translate(cx - size / 2, cy - size / 2);
            c.Scale(size);
            using var stroke = fill.Clone();
            stroke.Style = SKPaintStyle.Stroke;
            stroke.StrokeWidth = 0.085f;
            stroke.StrokeCap = SKStrokeCap.Round;
            stroke.StrokeJoin = SKStrokeJoin.Round;
            using var solid = fill.Clone();
            solid.Style = SKPaintStyle.Fill;
            Glyph(c, s[0], solid, stroke);
            c.Restore();
        }

        static SKPath Poly(params float[] xy)
        {
            var p = new SKPath();
            p.MoveTo(xy[0], xy[1]);
            for (int i = 2; i < xy.Length; i += 2) p.LineTo(xy[i], xy[i + 1]);
            p.Close();
            return p;
        }

        static void Speaker(SKCanvas c, SKPaint solid)
        {
            using var p = Poly(0.08f, 0.38f, 0.26f, 0.38f, 0.5f, 0.16f, 0.5f, 0.84f, 0.26f, 0.62f, 0.08f, 0.62f);
            c.DrawPath(p, solid);
        }

        static void Glyph(SKCanvas c, char g, SKPaint solid, SKPaint stroke)
        {
            switch (g)
            {
                case '': // play
                {
                    using var p = Poly(0.24f, 0.12f, 0.86f, 0.5f, 0.24f, 0.88f);
                    c.DrawPath(p, solid);
                    break;
                }
                case '': // pause
                    c.DrawRoundRect(0.22f, 0.14f, 0.18f, 0.72f, 0.03f, 0.03f, solid);
                    c.DrawRoundRect(0.6f, 0.14f, 0.18f, 0.72f, 0.03f, 0.03f, solid);
                    break;
                case '': // stop
                    c.DrawRoundRect(0.2f, 0.2f, 0.6f, 0.6f, 0.05f, 0.05f, solid);
                    break;
                case '': // previous
                {
                    c.DrawRect(0.14f, 0.18f, 0.1f, 0.64f, solid);
                    using var p = Poly(0.86f, 0.18f, 0.3f, 0.5f, 0.86f, 0.82f);
                    c.DrawPath(p, solid);
                    break;
                }
                case '': // next
                {
                    c.DrawRect(0.76f, 0.18f, 0.1f, 0.64f, solid);
                    using var p = Poly(0.14f, 0.18f, 0.7f, 0.5f, 0.14f, 0.82f);
                    c.DrawPath(p, solid);
                    break;
                }
                case '': // seek back
                {
                    using var a = Poly(0.5f, 0.2f, 0.06f, 0.5f, 0.5f, 0.8f);
                    using var b = Poly(0.94f, 0.2f, 0.5f, 0.5f, 0.94f, 0.8f);
                    c.DrawPath(a, solid);
                    c.DrawPath(b, solid);
                    break;
                }
                case '': // seek forward
                {
                    using var a = Poly(0.06f, 0.2f, 0.5f, 0.5f, 0.06f, 0.8f);
                    using var b = Poly(0.5f, 0.2f, 0.94f, 0.5f, 0.5f, 0.8f);
                    c.DrawPath(a, solid);
                    c.DrawPath(b, solid);
                    break;
                }
                case '': // eject
                {
                    using var p = Poly(0.5f, 0.16f, 0.86f, 0.56f, 0.14f, 0.56f);
                    c.DrawPath(p, solid);
                    c.DrawRect(0.14f, 0.68f, 0.72f, 0.12f, solid);
                    break;
                }
                case '': // mute
                    Speaker(c, solid);
                    c.DrawLine(0.64f, 0.36f, 0.92f, 0.64f, stroke);
                    c.DrawLine(0.92f, 0.36f, 0.64f, 0.64f, stroke);
                    break;
                case '': // volume up
                    Speaker(c, solid);
                    c.DrawArc(new SKRect(0.42f, 0.32f, 0.78f, 0.68f), -50, 100, false, stroke);
                    c.DrawArc(new SKRect(0.36f, 0.16f, 1.0f, 0.84f), -50, 100, false, stroke);
                    break;
                case '': // volume down
                    Speaker(c, solid);
                    c.DrawArc(new SKRect(0.42f, 0.32f, 0.78f, 0.68f), -50, 100, false, stroke);
                    break;
                case '': // power
                    c.DrawArc(new SKRect(0.16f, 0.2f, 0.84f, 0.88f), -60, 300, false, stroke);
                    c.DrawLine(0.5f, 0.1f, 0.5f, 0.5f, stroke);
                    break;
                case '': // folder
                {
                    using var p = Poly(0.08f, 0.22f, 0.4f, 0.22f, 0.48f, 0.32f, 0.92f, 0.32f, 0.92f, 0.8f, 0.08f, 0.8f);
                    c.DrawPath(p, stroke);
                    c.DrawLine(0.08f, 0.42f, 0.92f, 0.42f, stroke);
                    break;
                }
                case '': // open file
                {
                    using var p = Poly(0.2f, 0.08f, 0.6f, 0.08f, 0.8f, 0.28f, 0.8f, 0.92f, 0.2f, 0.92f);
                    c.DrawPath(p, stroke);
                    c.DrawLine(0.58f, 0.1f, 0.58f, 0.3f, stroke);
                    c.DrawLine(0.58f, 0.3f, 0.78f, 0.3f, stroke);
                    break;
                }
                case '': // list
                    for (int i = 0; i < 3; i++)
                    {
                        float y = 0.24f + i * 0.26f;
                        c.DrawCircle(0.14f, y, 0.06f, solid);
                        c.DrawLine(0.32f, y, 0.9f, y, stroke);
                    }
                    break;
                case '': // settings (gear)
                {
                    using var p = new SKPath();
                    for (int i = 0; i < 16; i++)
                    {
                        float a = i * MathF.PI / 8, r = i % 2 == 0 ? 0.44f : 0.34f;
                        float a2 = (i + 1) * MathF.PI / 8;
                        var p1 = new SKPoint(0.5f + r * MathF.Cos(a), 0.5f + r * MathF.Sin(a));
                        var p2 = new SKPoint(0.5f + r * MathF.Cos(a2), 0.5f + r * MathF.Sin(a2));
                        if (i == 0) p.MoveTo(p1);
                        p.LineTo(p1);
                        p.LineTo(p2);
                    }
                    p.Close();
                    c.DrawPath(p, stroke);
                    c.DrawCircle(0.5f, 0.5f, 0.13f, stroke);
                    break;
                }
                case '': // visualizer
                {
                    float[] h = [0.35f, 0.62f, 0.45f, 0.78f];
                    for (int i = 0; i < 4; i++) c.DrawRect(0.1f + i * 0.21f, 0.88f - h[i], 0.14f, h[i], solid);
                    break;
                }
                case '': // shuffle
                {
                    using var p1 = new SKPath();
                    p1.MoveTo(0.08f, 0.28f);
                    p1.CubicTo(0.45f, 0.28f, 0.5f, 0.72f, 0.86f, 0.72f);
                    using var p2 = new SKPath();
                    p2.MoveTo(0.08f, 0.72f);
                    p2.CubicTo(0.45f, 0.72f, 0.5f, 0.28f, 0.86f, 0.28f);
                    c.DrawPath(p1, stroke);
                    c.DrawPath(p2, stroke);
                    using var h1 = Poly(0.96f, 0.28f, 0.8f, 0.16f, 0.8f, 0.4f);
                    using var h2 = Poly(0.96f, 0.72f, 0.8f, 0.6f, 0.8f, 0.84f);
                    c.DrawPath(h1, solid);
                    c.DrawPath(h2, solid);
                    break;
                }
                case '': // repeat
                {
                    using var p = new SKPath();
                    p.AddRoundRect(new SKRect(0.12f, 0.26f, 0.88f, 0.74f), 0.16f, 0.16f);
                    c.DrawPath(p, stroke);
                    using var h = Poly(0.62f, 0.12f, 0.8f, 0.26f, 0.62f, 0.4f);
                    c.DrawPath(h, solid);
                    break;
                }
                case '': // detach (open in new window)
                    c.DrawRoundRect(0.12f, 0.24f, 0.62f, 0.62f, 0.06f, 0.06f, stroke);
                    c.DrawLine(0.48f, 0.5f, 0.9f, 0.1f, stroke);
                    c.DrawLine(0.62f, 0.1f, 0.9f, 0.1f, stroke);
                    c.DrawLine(0.9f, 0.1f, 0.9f, 0.38f, stroke);
                    break;
                default:
                    c.DrawCircle(0.5f, 0.5f, 0.12f, solid);
                    break;
            }
        }
    }
}
