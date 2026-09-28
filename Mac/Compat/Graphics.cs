using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using SkiaSharp;

namespace System.Drawing
{
    /// <summary>
    /// GDI+'s Graphics on a Skia canvas. Transform, clip and quality settings are tracked here (like GDI+ does)
    /// and applied around every draw call, so Save/Restore and clip replacement behave the same.
    /// </summary>
    public sealed class Graphics : IDisposable
    {
        readonly SKCanvas canvas;
        readonly bool ownsCanvas;
        SKMatrix matrix = SKMatrix.Identity;
        SKPath? clip; // device space; null = no clip

        internal Graphics(SKCanvas canvas, bool ownsCanvas)
        {
            this.canvas = canvas;
            this.ownsCanvas = ownsCanvas;
        }

        public static Graphics FromImage(Image image)
        {
            if (image is not Bitmap b) throw new ArgumentException("Parameter is not valid.");
            return new Graphics(new SKCanvas(b.Alive), true);
        }

        public SmoothingMode SmoothingMode { get; set; } = SmoothingMode.None;
        public InterpolationMode InterpolationMode { get; set; } = InterpolationMode.Bilinear;
        public PixelOffsetMode PixelOffsetMode { get; set; }
        public CompositingQuality CompositingQuality { get; set; }
        public CompositingMode CompositingMode { get; set; }
        public TextRenderingHint TextRenderingHint { get; set; }
        public GraphicsUnit PageUnit { get; set; } = GraphicsUnit.Display;
        public float PageScale { get; set; } = 1;
        public float DpiX => 96;
        public float DpiY => 96;

        bool AA => SmoothingMode is SmoothingMode.AntiAlias or SmoothingMode.HighQuality;

        // ─── transform ───

        public Matrix Transform
        {
            get => new(matrix);
            set => matrix = value.M;
        }

        void Apply(SKMatrix op, MatrixOrder order) => matrix = order == MatrixOrder.Prepend ? SKMatrix.Concat(matrix, op) : SKMatrix.Concat(op, matrix);

        public void ScaleTransform(float sx, float sy, MatrixOrder order = MatrixOrder.Prepend) => Apply(SKMatrix.CreateScale(sx, sy), order);
        public void TranslateTransform(float dx, float dy, MatrixOrder order = MatrixOrder.Prepend) => Apply(SKMatrix.CreateTranslation(dx, dy), order);
        public void RotateTransform(float angle, MatrixOrder order = MatrixOrder.Prepend) => Apply(SKMatrix.CreateRotationDegrees(angle), order);
        public void MultiplyTransform(Matrix m, MatrixOrder order = MatrixOrder.Prepend) => Apply(m.M, order);
        public void ResetTransform() => matrix = SKMatrix.Identity;

        // ─── state ───

        public GraphicsState Save() => new()
        {
            Matrix = matrix,
            Clip = clip == null ? null : new SKPath(clip),
            Smoothing = SmoothingMode,
            Interpolation = InterpolationMode,
            PixelOffset = PixelOffsetMode,
            CompositingQuality = CompositingQuality,
            CompositingMode = CompositingMode,
            TextHint = TextRenderingHint,
        };

        public void Restore(GraphicsState s)
        {
            matrix = s.Matrix;
            clip?.Dispose();
            clip = s.Clip == null ? null : new SKPath(s.Clip);
            SmoothingMode = s.Smoothing;
            InterpolationMode = s.Interpolation;
            PixelOffsetMode = s.PixelOffset;
            CompositingQuality = s.CompositingQuality;
            CompositingMode = s.CompositingMode;
            TextRenderingHint = s.TextHint;
        }

        // ─── clipping ───

        public Region Clip
        {
            get
            {
                if (clip == null) return new Region();
                var world = new SKPath(clip);
                if (matrix.TryInvert(out var inv)) world.Transform(inv);
                return new Region(world);
            }
            set => SetClip(value, CombineMode.Replace);
        }

        public RectangleF ClipBounds => clip == null ? new RectangleF(-4194304, -4194304, 8388608, 8388608) : Clip.GetBounds(this);
        public RectangleF VisibleClipBounds => ClipBounds;
        public bool IsClipEmpty => clip != null && clip.IsEmpty;

        public void SetClip(RectangleF r, CombineMode mode = CombineMode.Replace)
        {
            using var p = new SKPath();
            p.AddRect(Sk.R(r));
            ClipWorld(p, mode);
        }

        public void SetClip(Rectangle r, CombineMode mode = CombineMode.Replace) => SetClip((RectangleF)r, mode);
        public void SetClip(GraphicsPath path, CombineMode mode = CombineMode.Replace) => ClipWorld(path.P, mode);

        public void SetClip(Region region, CombineMode mode = CombineMode.Replace)
        {
            if (region.Path == null)
            {
                if (mode is CombineMode.Replace or CombineMode.Union) ResetClip();
                return;
            }
            ClipWorld(region.Path, mode);
        }

        public void SetClip(Graphics g) { clip?.Dispose(); clip = g.clip == null ? null : new SKPath(g.clip); }
        public void IntersectClip(RectangleF r) => SetClip(r, CombineMode.Intersect);
        public void IntersectClip(Rectangle r) => SetClip(r, CombineMode.Intersect);
        public void IntersectClip(Region r) => SetClip(r, CombineMode.Intersect);
        public void ExcludeClip(Rectangle r) => SetClip(r, CombineMode.Exclude);
        public void ExcludeClip(Region r) => SetClip(r, CombineMode.Exclude);

        public void ResetClip()
        {
            clip?.Dispose();
            clip = null;
        }

        void ClipWorld(SKPath world, CombineMode mode)
        {
            var dev = new SKPath(world);
            dev.Transform(matrix);
            SKPath? result = mode switch
            {
                CombineMode.Replace => dev,
                CombineMode.Intersect => clip == null ? dev : clip.Op(dev, SKPathOp.Intersect),
                CombineMode.Union => clip == null ? null : clip.Op(dev, SKPathOp.Union),
                CombineMode.Exclude => (clip ?? Sk.Infinite()).Op(dev, SKPathOp.Difference),
                CombineMode.Xor => (clip ?? Sk.Infinite()).Op(dev, SKPathOp.Xor),
                CombineMode.Complement => dev.Op(clip ?? Sk.Infinite(), SKPathOp.Difference),
                _ => dev,
            };
            if (result != dev) dev.Dispose();
            if (result != clip) clip?.Dispose();
            clip = result;
        }

        // ─── the draw-call wrapper ───

        void Begin()
        {
            canvas.Save();
            if (clip != null)
            {
                canvas.ResetMatrix();
                canvas.ClipPath(clip, SKClipOperation.Intersect, true);
            }
            canvas.SetMatrix(matrix);
        }

        void End() => canvas.Restore();

        SKPaint Fill(Brush b)
        {
            var p = new SKPaint { IsAntialias = AA, Style = SKPaintStyle.Fill };
            b.Apply(p);
            if (CompositingMode == CompositingMode.SourceCopy) p.BlendMode = SKBlendMode.Src;
            return p;
        }

        SKPaint Stroke(Pen pen)
        {
            var p = new SKPaint { IsAntialias = AA };
            pen.Apply(p);
            if (CompositingMode == CompositingMode.SourceCopy) p.BlendMode = SKBlendMode.Src;
            return p;
        }

        void Draw(Action<SKCanvas> draw)
        {
            Begin();
            try { draw(canvas); }
            finally { End(); }
        }

        // ─── fills ───

        public void Clear(Color color)
        {
            canvas.Save();
            canvas.ResetMatrix();
            if (clip != null) canvas.ClipPath(clip, SKClipOperation.Intersect, true);
            canvas.DrawColor(Sk.C(color), SKBlendMode.Src);
            canvas.Restore();
        }

        public void FillPath(Brush b, GraphicsPath path) { using var p = Fill(b); Draw(c => c.DrawPath(path.P, p)); }
        public void FillRectangle(Brush b, RectangleF r) { using var p = Fill(b); Draw(c => c.DrawRect(Sk.R(r), p)); }
        public void FillRectangle(Brush b, Rectangle r) => FillRectangle(b, (RectangleF)r);
        public void FillRectangle(Brush b, float x, float y, float w, float h) => FillRectangle(b, new RectangleF(x, y, w, h));
        public void FillRectangle(Brush b, int x, int y, int w, int h) => FillRectangle(b, new RectangleF(x, y, w, h));

        public void FillRectangles(Brush b, RectangleF[] rects)
        {
            if (rects.Length == 0) return;
            using var p = Fill(b);
            Draw(c =>
            {
                foreach (var r in rects) c.DrawRect(Sk.R(r), p);
            });
        }

        public void FillRectangles(Brush b, Rectangle[] rects) => FillRectangles(b, rects.Select(r => (RectangleF)r).ToArray());

        public void FillEllipse(Brush b, RectangleF r) { using var p = Fill(b); Draw(c => c.DrawOval(Sk.R(r), p)); }
        public void FillEllipse(Brush b, Rectangle r) => FillEllipse(b, (RectangleF)r);
        public void FillEllipse(Brush b, float x, float y, float w, float h) => FillEllipse(b, new RectangleF(x, y, w, h));
        public void FillEllipse(Brush b, int x, int y, int w, int h) => FillEllipse(b, new RectangleF(x, y, w, h));

        public void FillPolygon(Brush b, PointF[] pts) => FillPolygon(b, pts, FillMode.Alternate);

        public void FillPolygon(Brush b, PointF[] pts, FillMode mode)
        {
            if (pts.Length < 3) return;
            using var gp = new GraphicsPath(mode);
            gp.AddPolygon(pts);
            FillPath(b, gp);
        }

        public void FillPolygon(Brush b, Point[] pts) => FillPolygon(b, pts.Select(p => (PointF)p).ToArray());

        public void FillPie(Brush b, float x, float y, float w, float h, float start, float sweep)
        {
            using var gp = new GraphicsPath();
            gp.AddPie(x, y, w, h, start, sweep);
            FillPath(b, gp);
        }

        public void FillPie(Brush b, RectangleF r, float start, float sweep) => FillPie(b, r.X, r.Y, r.Width, r.Height, start, sweep);
        public void FillPie(Brush b, Rectangle r, float start, float sweep) => FillPie(b, r.X, r.Y, r.Width, r.Height, start, sweep);

        public void FillRegion(Brush b, Region region)
        {
            using var p = Fill(b);
            if (region.Path == null) Draw(c => c.DrawPaint(p));
            else Draw(c => c.DrawPath(region.Path, p));
        }

        public void FillClosedCurve(Brush b, PointF[] pts, FillMode mode = FillMode.Alternate, float tension = 0.5f)
        {
            using var gp = new GraphicsPath(mode);
            gp.AddClosedCurve(pts, tension);
            FillPath(b, gp);
        }

        // ─── strokes ───

        public void DrawPath(Pen pen, GraphicsPath path) { using var p = Stroke(pen); Draw(c => c.DrawPath(path.P, p)); }
        public void DrawLine(Pen pen, float x1, float y1, float x2, float y2) { using var p = Stroke(pen); Draw(c => c.DrawLine(x1, y1, x2, y2, p)); }
        public void DrawLine(Pen pen, PointF a, PointF b) => DrawLine(pen, a.X, a.Y, b.X, b.Y);
        public void DrawLine(Pen pen, Point a, Point b) => DrawLine(pen, a.X, a.Y, b.X, b.Y);
        public void DrawLine(Pen pen, int x1, int y1, int x2, int y2) => DrawLine(pen, (float)x1, y1, x2, y2);

        public void DrawLines(Pen pen, PointF[] pts)
        {
            if (pts.Length < 2) return;
            using var gp = new GraphicsPath();
            gp.AddLines(pts);
            DrawPath(pen, gp);
        }

        public void DrawLines(Pen pen, Point[] pts) => DrawLines(pen, pts.Select(p => (PointF)p).ToArray());

        public void DrawRectangle(Pen pen, RectangleF r) { using var p = Stroke(pen); Draw(c => c.DrawRect(Sk.R(r), p)); }
        public void DrawRectangle(Pen pen, Rectangle r) => DrawRectangle(pen, (RectangleF)r);
        public void DrawRectangle(Pen pen, float x, float y, float w, float h) => DrawRectangle(pen, new RectangleF(x, y, w, h));
        public void DrawRectangle(Pen pen, int x, int y, int w, int h) => DrawRectangle(pen, new RectangleF(x, y, w, h));

        public void DrawRectangles(Pen pen, RectangleF[] rects)
        {
            foreach (var r in rects) DrawRectangle(pen, r);
        }

        public void DrawEllipse(Pen pen, RectangleF r) { using var p = Stroke(pen); Draw(c => c.DrawOval(Sk.R(r), p)); }
        public void DrawEllipse(Pen pen, Rectangle r) => DrawEllipse(pen, (RectangleF)r);
        public void DrawEllipse(Pen pen, float x, float y, float w, float h) => DrawEllipse(pen, new RectangleF(x, y, w, h));
        public void DrawEllipse(Pen pen, int x, int y, int w, int h) => DrawEllipse(pen, new RectangleF(x, y, w, h));

        public void DrawPolygon(Pen pen, PointF[] pts)
        {
            if (pts.Length < 2) return;
            using var gp = new GraphicsPath();
            gp.AddPolygon(pts);
            DrawPath(pen, gp);
        }

        public void DrawPolygon(Pen pen, Point[] pts) => DrawPolygon(pen, pts.Select(p => (PointF)p).ToArray());

        public void DrawArc(Pen pen, RectangleF r, float start, float sweep)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using var p = Stroke(pen);
            Draw(c =>
            {
                using var path = new SKPath();
                path.AddArc(Sk.R(r), start, sweep);
                c.DrawPath(path, p);
            });
        }

        public void DrawArc(Pen pen, Rectangle r, float start, float sweep) => DrawArc(pen, (RectangleF)r, start, sweep);
        public void DrawArc(Pen pen, float x, float y, float w, float h, float start, float sweep) => DrawArc(pen, new RectangleF(x, y, w, h), start, sweep);
        public void DrawArc(Pen pen, int x, int y, int w, int h, int start, int sweep) => DrawArc(pen, new RectangleF(x, y, w, h), start, sweep);

        public void DrawPie(Pen pen, RectangleF r, float start, float sweep)
        {
            using var gp = new GraphicsPath();
            gp.AddPie(r.X, r.Y, r.Width, r.Height, start, sweep);
            DrawPath(pen, gp);
        }

        public void DrawCurve(Pen pen, PointF[] pts) => DrawCurve(pen, pts, 0.5f);

        public void DrawCurve(Pen pen, PointF[] pts, float tension)
        {
            if (pts.Length < 2) return;
            using var gp = new GraphicsPath();
            gp.AddCurve(pts, tension);
            DrawPath(pen, gp);
        }

        public void DrawClosedCurve(Pen pen, PointF[] pts)
        {
            using var gp = new GraphicsPath();
            gp.AddClosedCurve(pts);
            DrawPath(pen, gp);
        }

        public void DrawBezier(Pen pen, PointF a, PointF b, PointF c, PointF d)
        {
            using var gp = new GraphicsPath();
            gp.AddBezier(a, b, c, d);
            DrawPath(pen, gp);
        }

        // ─── images ───

        SKPaint ImagePaint(ImageAttributes? attr, float scale)
        {
            var p = new SKPaint
            {
                IsAntialias = AA,
                FilterQuality = InterpolationMode switch
                {
                    InterpolationMode.NearestNeighbor => SKFilterQuality.None,
                    InterpolationMode.HighQualityBicubic or InterpolationMode.High => SKFilterQuality.High,
                    InterpolationMode.HighQualityBilinear or InterpolationMode.Bicubic => SKFilterQuality.Medium,
                    // Plain bilinear (GDI+'s default) — but big reductions still get mipmaps so they don't sparkle.
                    _ => scale < 0.6f ? SKFilterQuality.Medium : SKFilterQuality.Low,
                },
            };
            if (attr?.Matrix != null)
            {
                if (attr.Matrix.IsAlphaOnly(out float a)) p.Color = SKColors.White.WithAlpha((byte)Math.Clamp(a * 255, 0, 255));
                else p.ColorFilter = attr.Matrix.ToFilter();
            }
            if (CompositingMode == CompositingMode.SourceCopy) p.BlendMode = SKBlendMode.Src;
            return p;
        }

        void DrawBitmap(Image img, SKRect src, SKRect dst, ImageAttributes? attr = null)
        {
            if (dst.Width == 0 || dst.Height == 0 || src.Width <= 0 || src.Height <= 0) return;
            float scale = Math.Min(Math.Abs(dst.Width * matrix.ScaleX / src.Width), Math.Abs(dst.Height * matrix.ScaleY / src.Height));
            using var p = ImagePaint(attr, scale);
            var bmp = img.Alive;
            Draw(c => c.DrawBitmap(bmp, src, dst, p));
        }

        public void DrawImage(Image img, float x, float y) => DrawBitmap(img, new SKRect(0, 0, img.Width, img.Height), SKRect.Create(x, y, img.Width, img.Height));
        public void DrawImage(Image img, int x, int y) => DrawImage(img, (float)x, y);
        public void DrawImage(Image img, PointF p) => DrawImage(img, p.X, p.Y);
        public void DrawImage(Image img, Point p) => DrawImage(img, (float)p.X, p.Y);
        public void DrawImage(Image img, RectangleF dest) => DrawBitmap(img, new SKRect(0, 0, img.Width, img.Height), Sk.R(dest));
        public void DrawImage(Image img, Rectangle dest) => DrawImage(img, (RectangleF)dest);
        public void DrawImage(Image img, float x, float y, float w, float h) => DrawImage(img, new RectangleF(x, y, w, h));
        public void DrawImage(Image img, int x, int y, int w, int h) => DrawImage(img, new RectangleF(x, y, w, h));
        public void DrawImage(Image img, RectangleF dest, RectangleF src, GraphicsUnit unit) => DrawBitmap(img, Sk.R(src), Sk.R(dest));
        public void DrawImage(Image img, Rectangle dest, Rectangle src, GraphicsUnit unit) => DrawBitmap(img, Sk.R(src), Sk.R(dest));

        public void DrawImage(Image img, Rectangle dest, int sx, int sy, int sw, int sh, GraphicsUnit unit, ImageAttributes? attr = null) =>
            DrawBitmap(img, SKRect.Create(sx, sy, sw, sh), Sk.R(dest), attr);

        public void DrawImage(Image img, Rectangle dest, float sx, float sy, float sw, float sh, GraphicsUnit unit, ImageAttributes? attr = null) =>
            DrawBitmap(img, SKRect.Create(sx, sy, sw, sh), Sk.R(dest), attr);

        public void DrawImage(Image img, PointF[] destPoints) => DrawImage(img, destPoints, new RectangleF(0, 0, img.Width, img.Height), GraphicsUnit.Pixel);

        /// <summary>Draws the source rectangle into the parallelogram given by its upper-left, upper-right and lower-left corners.</summary>
        public void DrawImage(Image img, PointF[] destPoints, RectangleF src, GraphicsUnit unit, ImageAttributes? attr = null)
        {
            if (destPoints.Length < 3 || src.Width <= 0 || src.Height <= 0) return;
            PointF ul = destPoints[0], ur = destPoints[1], ll = destPoints[2];
            var map = new SKMatrix(
                (ur.X - ul.X) / src.Width, (ll.X - ul.X) / src.Height, ul.X,
                (ur.Y - ul.Y) / src.Width, (ll.Y - ul.Y) / src.Height, ul.Y,
                0, 0, 1);
            map = SKMatrix.Concat(map, SKMatrix.CreateTranslation(-src.X, -src.Y));
            var saved = matrix;
            matrix = SKMatrix.Concat(matrix, map);
            try { DrawBitmap(img, Sk.R(src), Sk.R(src), attr); }
            finally { matrix = saved; }
        }

        public void DrawImageUnscaled(Image img, int x, int y) => DrawImage(img, (float)x, y);
        public void DrawImageUnscaled(Image img, Point p) => DrawImage(img, (float)p.X, p.Y);

        // ─── text ───

        public void DrawString(string? s, Font font, Brush brush, float x, float y) => DrawString(s, font, brush, new RectangleF(x, y, 0, 0), null);
        public void DrawString(string? s, Font font, Brush brush, float x, float y, StringFormat? format) => DrawString(s, font, brush, new RectangleF(x, y, 0, 0), format);
        public void DrawString(string? s, Font font, Brush brush, PointF p) => DrawString(s, font, brush, new RectangleF(p.X, p.Y, 0, 0), null);
        public void DrawString(string? s, Font font, Brush brush, PointF p, StringFormat? format) => DrawString(s, font, brush, new RectangleF(p.X, p.Y, 0, 0), format);
        public void DrawString(string? s, Font font, Brush brush, RectangleF layout) => DrawString(s, font, brush, layout, null);

        public void DrawString(string? s, Font font, Brush brush, RectangleF layout, StringFormat? format)
        {
            if (string.IsNullOrEmpty(s)) return;
            using var paint = Fill(brush);
            paint.IsAntialias = TextRenderingHint is not (TextRenderingHint.SingleBitPerPixel or TextRenderingHint.SingleBitPerPixelGridFit);
            bool clipToLayout = layout.Width > 0 && layout.Height > 0 && (format == null || (format.FormatFlags & StringFormatFlags.NoClip) == 0);
            Draw(c =>
            {
                if (clipToLayout) c.ClipRect(Sk.R(layout));
                if (font.IsIcon)
                {
                    IconGlyphs.Draw(c, s, font.Px, layout, format, paint);
                    return;
                }
                foreach (var run in TextLayout.Layout(s, font, layout, format))
                {
                    TextLayout.Style(paint, font, run.Typeface);
                    c.DrawText(run.Text, run.X, run.Baseline, paint);
                    if (font.Underline)
                    {
                        float w = paint.MeasureText(run.Text);
                        c.DrawRect(run.X, run.Baseline + font.Px * 0.1f, w, Math.Max(1, font.Px / 14), paint);
                    }
                }
            });
        }

        public SizeF MeasureString(string? s, Font font) => TextLayout.Measure(s, font, 0, null);
        public SizeF MeasureString(string? s, Font font, int width) => TextLayout.Measure(s, font, width, null);
        public SizeF MeasureString(string? s, Font font, int width, StringFormat? format) => TextLayout.Measure(s, font, width, format);
        public SizeF MeasureString(string? s, Font font, SizeF layout) => TextLayout.Measure(s, font, layout.Width, null);
        public SizeF MeasureString(string? s, Font font, SizeF layout, StringFormat? format) => TextLayout.Measure(s, font, layout.Width, format);
        public SizeF MeasureString(string? s, Font font, PointF origin, StringFormat? format) => TextLayout.Measure(s, font, 0, format);

        public void Flush() => canvas.Flush();

        public void Dispose()
        {
            clip?.Dispose();
            clip = null;
            if (ownsCanvas) canvas.Dispose();
        }
    }

    /// <summary>GDI+-style text layout: padding of 1/6 em, word wrap, alignment, ellipsis, and font fallback per character.</summary>
    internal static class TextLayout
    {
        public readonly record struct Run(string Text, float X, float Baseline, SKTypeface Typeface);

        sealed record Piece(string Text, SKTypeface Typeface);

        static float Pad(Font f, StringFormat? fmt) => fmt?.Typographic == true ? 0 : f.Px / 6f;

        /// <summary>Splits a line into runs that the font (or a fallback font) can actually draw.</summary>
        static List<Piece> Pieces(string line, Font font)
        {
            var list = new List<Piece>();
            var sb = new System.Text.StringBuilder();
            SKTypeface? cur = null;
            for (int i = 0; i < line.Length; i++)
            {
                int cp = char.ConvertToUtf32(line, i);
                string ch = char.ConvertFromUtf32(cp);
                if (cp > 0xFFFF) i++;
                var tf = font.Typeface.ContainsGlyph(cp) || char.IsWhiteSpace(ch[0]) ? font.Typeface
                    : SKFontManager.Default.MatchCharacter(font.Typeface.FamilyName, font.Typeface.FontStyle, null, cp) ?? font.Typeface;
                if (cur != null && tf != cur)
                {
                    list.Add(new Piece(sb.ToString(), cur));
                    sb.Clear();
                }
                cur = tf;
                sb.Append(ch);
            }
            if (cur != null && sb.Length > 0) list.Add(new Piece(sb.ToString(), cur));
            return list;
        }

        /// <summary>Sets a paint up to draw or measure text in this font (synthetic bold/italic when the face has none).</summary>
        public static void Style(SKPaint p, Font font, SKTypeface tf)
        {
            p.Typeface = tf;
            p.TextSize = font.Px;
            p.SubpixelText = true;
            p.FakeBoldText = font.Bold && !tf.IsBold;
            p.TextSkewX = font.Italic && !tf.IsItalic ? -0.2f : 0;
            p.TextAlign = SKTextAlign.Left;
        }

        static float Measure(string text, Font font, SKTypeface tf)
        {
            using var p = new SKPaint();
            Style(p, font, tf);
            return p.MeasureText(text);
        }

        static float Width(string line, Font font)
        {
            float w = 0;
            foreach (var p in Pieces(line, font)) w += Measure(p.Text, font, p.Typeface);
            return w;
        }

        public static (float LineHeight, float Ascent) Metrics(Font font)
        {
            using var p = new SKPaint();
            Style(p, font, font.Typeface);
            var m = p.FontMetrics;
            return (-m.Ascent + m.Descent + m.Leading, -m.Ascent + m.Leading / 2);
        }

        /// <summary>Breaks text into lines: at newlines, and at word boundaries to fit the width (when wrapping).</summary>
        static List<string> Lines(string s, Font font, float maxWidth, StringFormat? fmt)
        {
            var result = new List<string>();
            bool wrap = maxWidth > 0 && (fmt == null || (fmt.FormatFlags & StringFormatFlags.NoWrap) == 0);
            foreach (var para in s.Replace("\r\n", "\n").Split('\n'))
            {
                if (!wrap || Width(para, font) <= maxWidth)
                {
                    result.Add(para);
                    continue;
                }
                var words = para.Split(' ');
                var line = "";
                foreach (var word in words)
                {
                    string test = line.Length == 0 ? word : line + " " + word;
                    if (Width(test, font) <= maxWidth || line.Length == 0)
                    {
                        line = test;
                        // A single word that's too long is broken by character.
                        while (Width(line, font) > maxWidth && line.Length > 1)
                        {
                            int cut = line.Length - 1;
                            while (cut > 1 && Width(line[..cut], font) > maxWidth) cut--;
                            result.Add(line[..cut]);
                            line = line[cut..];
                        }
                    }
                    else
                    {
                        result.Add(line);
                        line = word;
                    }
                }
                result.Add(line);
            }
            return result;
        }

        static string Ellipsize(string line, Font font, float maxWidth)
        {
            if (Width(line, font) <= maxWidth) return line;
            for (int n = line.Length - 1; n > 0; n--)
            {
                string t = line[..n].TrimEnd() + "…";
                if (Width(t, font) <= maxWidth) return t;
            }
            return "…";
        }

        public static IEnumerable<Run> Layout(string s, Font font, RectangleF layout, StringFormat? fmt)
        {
            float pad = Pad(font, fmt);
            float avail = layout.Width > 0 ? Math.Max(1, layout.Width - 2 * pad) : 0;
            var lines = Lines(s, font, avail, fmt);
            var (lineH, ascent) = Metrics(font);

            // Lines that don't fit the height are dropped (GDI+ keeps partial lines unless LineLimit is set).
            if (layout.Height > 0 && lines.Count > 1)
            {
                int fit = Math.Max(1, (int)Math.Floor(layout.Height / lineH + (fmt != null && (fmt.FormatFlags & StringFormatFlags.LineLimit) != 0 ? 0 : 0.99)));
                if (lines.Count > fit)
                {
                    lines = lines.Take(fit).ToList();
                    if (fmt?.Trimming is StringTrimming.EllipsisCharacter or StringTrimming.EllipsisWord or StringTrimming.EllipsisPath)
                        lines[^1] = Ellipsize(lines[^1] + " ……", font, avail);
                }
            }
            if (avail > 0 && fmt?.Trimming is StringTrimming.EllipsisCharacter or StringTrimming.EllipsisWord or StringTrimming.EllipsisPath)
                for (int i = 0; i < lines.Count; i++) lines[i] = Ellipsize(lines[i], font, avail);

            float total = lines.Count * lineH;
            var va = fmt?.LineAlignment ?? StringAlignment.Near;
            var ha = fmt?.Alignment ?? StringAlignment.Near;
            float top = layout.Height > 0
                ? va switch { StringAlignment.Center => layout.Y + (layout.Height - total) / 2, StringAlignment.Far => layout.Bottom - total, _ => layout.Y }
                : va switch { StringAlignment.Center => layout.Y - total / 2, StringAlignment.Far => layout.Y - total, _ => layout.Y };

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                float w = Width(line, font);
                float x = layout.Width > 0
                    ? ha switch { StringAlignment.Center => layout.X + (layout.Width - w) / 2, StringAlignment.Far => layout.Right - pad - w, _ => layout.X + pad }
                    : ha switch { StringAlignment.Center => layout.X - w / 2, StringAlignment.Far => layout.X - pad - w, _ => layout.X + pad };
                float baseline = top + i * lineH + ascent;
                foreach (var p in Pieces(line, font))
                {
                    yield return new Run(p.Text, x, baseline, p.Typeface);
                    x += Measure(p.Text, font, p.Typeface);
                }
            }
        }

        public static SizeF Measure(string? s, Font font, float width, StringFormat? fmt)
        {
            if (string.IsNullOrEmpty(s)) return new SizeF(0, Metrics(font).LineHeight);
            float pad = Pad(font, fmt);
            var lines = Lines(s, font, width > 0 ? Math.Max(1, width - 2 * pad) : 0, fmt);
            float w = lines.Max(l => Width(l, font));
            return new SizeF(w + 2 * pad, lines.Count * Metrics(font).LineHeight);
        }
    }
}
