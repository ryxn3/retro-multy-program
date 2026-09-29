using SkiaSharp;

namespace System.Drawing.Drawing2D
{
    public enum SmoothingMode { Invalid = -1, Default, HighSpeed, HighQuality, None, AntiAlias }
    public enum InterpolationMode { Invalid = -1, Default, Low, High, Bilinear, Bicubic, NearestNeighbor, HighQualityBilinear, HighQualityBicubic }
    public enum PixelOffsetMode { Invalid = -1, Default, HighSpeed, HighQuality, None, Half }
    public enum CompositingQuality { Invalid = -1, Default, HighSpeed, HighQuality, GammaCorrected, AssumeLinear }
    public enum CompositingMode { SourceOver, SourceCopy }
    public enum LineCap { Flat = 0, Square = 1, Round = 2, Triangle = 3, NoAnchor = 16, SquareAnchor = 17, RoundAnchor = 18, DiamondAnchor = 19, ArrowAnchor = 20, Custom = 255 }
    public enum DashCap { Flat = 0, Round = 2, Triangle = 3 }
    public enum LineJoin { Miter, Bevel, Round, MiterClipped }
    public enum DashStyle { Solid, Dash, Dot, DashDot, DashDotDot, Custom }
    public enum WrapMode { Tile, TileFlipX, TileFlipY, TileFlipXY, Clamp }
    public enum FillMode { Alternate, Winding }
    public enum CombineMode { Replace, Intersect, Union, Xor, Exclude, Complement }
    public enum MatrixOrder { Prepend, Append }
    public enum LinearGradientMode { Horizontal, Vertical, ForwardDiagonal, BackwardDiagonal }

    public sealed class GraphicsState
    {
        internal SKMatrix Matrix;
        internal SKPath? Clip;
        internal SmoothingMode Smoothing;
        internal InterpolationMode Interpolation;
        internal PixelOffsetMode PixelOffset;
        internal CompositingQuality CompositingQuality;
        internal CompositingMode CompositingMode;
        internal Text.TextRenderingHint TextHint;
    }

    /// <summary>GDI+'s affine matrix; Prepend (the default) applies the new operation first.</summary>
    public sealed class Matrix : IDisposable, ICloneable
    {
        internal SKMatrix M;

        public Matrix() => M = SKMatrix.Identity;
        internal Matrix(SKMatrix m) => M = m;

        public Matrix(float m11, float m12, float m21, float m22, float dx, float dy) =>
            M = new SKMatrix(m11, m21, dx, m12, m22, dy, 0, 0, 1);

        public float[] Elements => [M.ScaleX, M.SkewY, M.SkewX, M.ScaleY, M.TransX, M.TransY];
        public float OffsetX => M.TransX;
        public float OffsetY => M.TransY;
        public bool IsIdentity => M.IsIdentity;
        public bool IsInvertible => M.IsInvertible;

        void Apply(SKMatrix op, MatrixOrder order) => M = order == MatrixOrder.Prepend ? SKMatrix.Concat(M, op) : SKMatrix.Concat(op, M);

        public void Scale(float sx, float sy, MatrixOrder order = MatrixOrder.Prepend) => Apply(SKMatrix.CreateScale(sx, sy), order);
        public void Translate(float dx, float dy, MatrixOrder order = MatrixOrder.Prepend) => Apply(SKMatrix.CreateTranslation(dx, dy), order);
        public void Rotate(float angle, MatrixOrder order = MatrixOrder.Prepend) => Apply(SKMatrix.CreateRotationDegrees(angle), order);
        public void RotateAt(float angle, PointF point, MatrixOrder order = MatrixOrder.Prepend) => Apply(SKMatrix.CreateRotationDegrees(angle, point.X, point.Y), order);
        public void Shear(float sx, float sy, MatrixOrder order = MatrixOrder.Prepend) => Apply(SKMatrix.CreateSkew(sx, sy), order);
        public void Multiply(Matrix m, MatrixOrder order = MatrixOrder.Prepend) => Apply(m.M, order);
        public void Reset() => M = SKMatrix.Identity;

        public void Invert()
        {
            if (M.TryInvert(out var inv)) M = inv;
        }

        public void TransformPoints(PointF[] pts)
        {
            for (int i = 0; i < pts.Length; i++)
            {
                var p = M.MapPoint(pts[i].X, pts[i].Y);
                pts[i] = new PointF(p.X, p.Y);
            }
        }

        public void TransformPoints(Point[] pts)
        {
            for (int i = 0; i < pts.Length; i++)
            {
                var p = M.MapPoint(pts[i].X, pts[i].Y);
                pts[i] = new Point((int)MathF.Round(p.X), (int)MathF.Round(p.Y));
            }
        }

        public void TransformVectors(PointF[] pts)
        {
            for (int i = 0; i < pts.Length; i++)
            {
                var p = M.MapVector(pts[i].X, pts[i].Y);
                pts[i] = new PointF(p.X, p.Y);
            }
        }

        public Matrix Clone() => new(M);
        object ICloneable.Clone() => Clone();
        public void Dispose() { }
    }

    public sealed class GraphicsPath : IDisposable, ICloneable
    {
        internal readonly SKPath P;
        bool newFigure = true;

        public GraphicsPath() : this(FillMode.Alternate) { }
        public GraphicsPath(FillMode mode) { P = new SKPath(); FillMode = mode; }
        internal GraphicsPath(SKPath p) { P = p; }

        public FillMode FillMode
        {
            get => P.FillType == SKPathFillType.Winding ? FillMode.Winding : FillMode.Alternate;
            set => P.FillType = value == FillMode.Winding ? SKPathFillType.Winding : SKPathFillType.EvenOdd;
        }

        public int PointCount => P.PointCount;
        public PointF[] PathPoints => P.Points.Select(p => new PointF(p.X, p.Y)).ToArray();
        public PointF GetLastPoint() => P.LastPoint is var p ? new PointF(p.X, p.Y) : PointF.Empty;

        void Line(float x, float y)
        {
            if (newFigure || P.PointCount == 0) { P.MoveTo(x, y); newFigure = false; }
            else P.LineTo(x, y);
        }

        public void StartFigure() => newFigure = true;

        public void CloseFigure()
        {
            P.Close();
            newFigure = true;
        }

        public void CloseAllFigures() => CloseFigure();

        public void AddLine(float x1, float y1, float x2, float y2)
        {
            Line(x1, y1);
            P.LineTo(x2, y2);
        }

        public void AddLine(PointF a, PointF b) => AddLine(a.X, a.Y, b.X, b.Y);
        public void AddLine(Point a, Point b) => AddLine(a.X, a.Y, b.X, b.Y);

        public void AddLines(PointF[] pts)
        {
            if (pts.Length == 0) return;
            Line(pts[0].X, pts[0].Y);
            for (int i = 1; i < pts.Length; i++) P.LineTo(pts[i].X, pts[i].Y);
        }

        public void AddArc(float x, float y, float w, float h, float start, float sweep)
        {
            if (w <= 0 || h <= 0) return;
            var oval = new SKRect(x, y, x + w, y + h);
            if (newFigure || P.PointCount == 0)
            {
                P.ArcTo(oval, start, sweep, true);
                newFigure = false;
            }
            else P.ArcTo(oval, start, sweep, false);
        }

        public void AddArc(RectangleF r, float start, float sweep) => AddArc(r.X, r.Y, r.Width, r.Height, start, sweep);
        public void AddArc(Rectangle r, float start, float sweep) => AddArc(r.X, r.Y, r.Width, r.Height, start, sweep);

        public void AddBezier(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
        {
            Line(x1, y1);
            P.CubicTo(x2, y2, x3, y3, x4, y4);
        }

        public void AddBezier(PointF a, PointF b, PointF c, PointF d) => AddBezier(a.X, a.Y, b.X, b.Y, c.X, c.Y, d.X, d.Y);

        public void AddCurve(PointF[] pts, float tension = 0.5f)
        {
            if (pts.Length < 2) return;
            Line(pts[0].X, pts[0].Y);
            Curves.AppendCardinal(P, pts, tension, closed: false);
        }

        public void AddClosedCurve(PointF[] pts, float tension = 0.5f)
        {
            if (pts.Length < 3) return;
            P.MoveTo(pts[0].X, pts[0].Y);
            Curves.AppendCardinal(P, pts, tension, closed: true);
            P.Close();
            newFigure = true;
        }

        public void AddEllipse(float x, float y, float w, float h)
        {
            P.AddOval(new SKRect(x, y, x + w, y + h));
            newFigure = true;
        }

        public void AddEllipse(RectangleF r) => AddEllipse(r.X, r.Y, r.Width, r.Height);
        public void AddEllipse(Rectangle r) => AddEllipse(r.X, r.Y, r.Width, r.Height);

        public void AddRectangle(RectangleF r)
        {
            P.AddRect(Sk.R(r));
            newFigure = true;
        }

        public void AddRectangle(Rectangle r) => AddRectangle((RectangleF)r);

        public void AddRectangles(RectangleF[] rs)
        {
            foreach (var r in rs) AddRectangle(r);
        }

        public void AddPolygon(PointF[] pts)
        {
            if (pts.Length < 2) return;
            P.AddPoly(pts.Select(Sk.P).ToArray(), true);
            newFigure = true;
        }

        public void AddPolygon(Point[] pts) => AddPolygon(pts.Select(p => (PointF)p).ToArray());

        public void AddPie(float x, float y, float w, float h, float start, float sweep)
        {
            var oval = new SKRect(x, y, x + w, y + h);
            P.MoveTo(oval.MidX, oval.MidY);
            P.ArcTo(oval, start, sweep, false);
            P.Close();
            newFigure = true;
        }

        public void AddPath(GraphicsPath other, bool connect)
        {
            P.AddPath(other.P, connect ? SKPathAddMode.Extend : SKPathAddMode.Append);
            newFigure = true;
        }

        public void AddString(string s, FontFamily family, int style, float emSize, PointF origin, StringFormat? format) =>
            AddString(s, family, style, emSize, new RectangleF(origin.X, origin.Y, 0, 0), format);

        public void AddString(string s, FontFamily family, int style, float emSize, Point origin, StringFormat? format) =>
            AddString(s, family, style, emSize, new RectangleF(origin.X, origin.Y, 0, 0), format);

        /// <summary>Text as outlines, laid out the same way DrawString lays it out.</summary>
        public void AddString(string s, FontFamily family, int style, float emSize, RectangleF layout, StringFormat? format)
        {
            using var font = new Font(family.Name, emSize, (FontStyle)style, GraphicsUnit.Pixel);
            using var paint = new SKPaint();
            foreach (var run in TextLayout.Layout(s, font, layout, format))
            {
                TextLayout.Style(paint, font, run.Typeface);
                using var path = paint.GetTextPath(run.Text, run.X, run.Baseline);
                if (path != null) P.AddPath(path);
            }
            newFigure = true;
        }

        public RectangleF GetBounds() => P.PointCount == 0 ? RectangleF.Empty : Sk.R(P.TightBounds);
        public RectangleF GetBounds(Matrix? m) => GetBounds(m, null);

        public RectangleF GetBounds(Matrix? m, Pen? pen)
        {
            using var copy = new SKPath(P);
            if (m != null) copy.Transform(m.M);
            var b = copy.PointCount == 0 ? SKRect.Empty : copy.TightBounds;
            if (pen != null) b.Inflate(pen.Width / 2, pen.Width / 2);
            return Sk.R(b);
        }

        public bool IsVisible(float x, float y) => P.Contains(x, y);
        public bool IsVisible(PointF p) => P.Contains(p.X, p.Y);
        public bool IsVisible(Point p) => P.Contains(p.X, p.Y);
        public bool IsVisible(PointF p, Graphics? g) => P.Contains(p.X, p.Y);

        public bool IsOutlineVisible(PointF p, Pen pen)
        {
            using var paint = new SKPaint();
            pen.Apply(paint);
            using var outline = paint.GetFillPath(P);
            return outline?.Contains(p.X, p.Y) ?? false;
        }

        public void Transform(Matrix m) => P.Transform(m.M);

        public void Widen(Pen pen)
        {
            using var paint = new SKPaint();
            pen.Apply(paint);
            using var outline = paint.GetFillPath(P);
            P.Reset();
            if (outline != null) P.AddPath(outline);
            P.FillType = SKPathFillType.Winding;
        }

        public void Flatten() { }

        public void Reverse()
        {
            using var copy = new SKPath(P);
            P.Reset();
            P.AddPathReverse(copy);
        }

        public void Reset()
        {
            P.Reset();
            newFigure = true;
        }

        public GraphicsPath Clone() => new(new SKPath(P));
        object ICloneable.Clone() => Clone();
        public void Dispose() => P.Dispose();
    }

    internal static class Curves
    {
        /// <summary>GDI+ cardinal splines as cubic Béziers (control points at tension/3 of the neighbour chord).</summary>
        public static void AppendCardinal(SKPath path, PointF[] pts, float tension, bool closed)
        {
            int n = pts.Length;
            float t = tension / 3f;
            PointF At(int i) => closed ? pts[((i % n) + n) % n] : pts[Math.Clamp(i, 0, n - 1)];
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                PointF p0 = At(i - 1), p1 = At(i), p2 = At(i + 1), p3 = At(i + 2);
                var c1 = new SKPoint(p1.X + (p2.X - p0.X) * t, p1.Y + (p2.Y - p0.Y) * t);
                var c2 = new SKPoint(p2.X - (p3.X - p1.X) * t, p2.Y - (p3.Y - p1.Y) * t);
                path.CubicTo(c1, c2, new SKPoint(p2.X, p2.Y));
            }
        }
    }

    public sealed class ColorBlend
    {
        public ColorBlend() { }
        public ColorBlend(int count) { Colors = new Color[count]; Positions = new float[count]; }
        public Color[] Colors { get; set; } = [];
        public float[] Positions { get; set; } = [];
    }

    public sealed class Blend
    {
        public Blend() { }
        public Blend(int count) { Factors = new float[count]; Positions = new float[count]; }
        public float[] Factors { get; set; } = [];
        public float[] Positions { get; set; } = [];
    }

    /// <summary>Shared colour-stop logic for the two gradient brushes.</summary>
    public abstract class GradientBrush : Brush
    {
        public ColorBlend? InterpolationColors { get; set; }
        public Blend? Blend { get; set; }
        public WrapMode WrapMode { get; set; }
        public bool GammaCorrection { get; set; }
        public Matrix Transform { get; set; } = new();

        public void TranslateTransform(float dx, float dy, MatrixOrder order = MatrixOrder.Prepend) => Transform.Translate(dx, dy, order);
        public void ScaleTransform(float sx, float sy, MatrixOrder order = MatrixOrder.Prepend) => Transform.Scale(sx, sy, order);
        public void RotateTransform(float angle, MatrixOrder order = MatrixOrder.Prepend) => Transform.Rotate(angle, order);
        public void MultiplyTransform(Matrix m, MatrixOrder order = MatrixOrder.Prepend) => Transform.Multiply(m, order);
        public void ResetTransform() => Transform = new Matrix();

        /// <summary>Triangular blend: from colour 1 up to colour 2 at <paramref name="focus"/> and back.</summary>
        public void SetBlendTriangularShape(float focus, float scale = 1) => Blend = new Blend
        {
            Positions = focus <= 0 ? [0, 1] : focus >= 1 ? [0, 1] : [0, focus, 1],
            Factors = focus <= 0 ? [scale, 0] : focus >= 1 ? [0, scale] : [0, scale, 0],
        };

        /// <summary>Bell-shaped blend, approximated with a few smooth stops.</summary>
        public void SetSigmaBellShape(float focus, float scale = 1)
        {
            var pos = new List<float>();
            var fac = new List<float>();
            for (int i = 0; i <= 16; i++)
            {
                float x = i / 16f;
                float d = x < focus ? (focus - x) / Math.Max(focus, 1e-3f) : (x - focus) / Math.Max(1 - focus, 1e-3f);
                pos.Add(x);
                fac.Add(scale * MathF.Exp(-4.5f * d * d));
            }
            Blend = new Blend { Positions = [.. pos], Factors = [.. fac] };
        }

        /// <summary>Colour stops from 0 (colour a) to 1 (colour b).</summary>
        internal (SKColor[] Colors, float[] Positions) Stops(Color a, Color b)
        {
            if (InterpolationColors is { Colors.Length: >= 2 } ic && ic.Positions.Length == ic.Colors.Length)
                return (ic.Colors.Select(Sk.C).ToArray(), ic.Positions.ToArray());
            if (Blend is { Factors.Length: >= 2 } bl && bl.Positions.Length == bl.Factors.Length)
                return (bl.Factors.Select(f => Sk.C(Mix(a, b, f))).ToArray(), bl.Positions.ToArray());
            return ([Sk.C(a), Sk.C(b)], [0, 1]);
        }

        internal static Color Mix(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }

    public sealed class LinearGradientBrush : GradientBrush
    {
        PointF p1, p2;

        public LinearGradientBrush(PointF point1, PointF point2, Color color1, Color color2)
        {
            p1 = point1;
            p2 = point2;
            LinearColors = [color1, color2];
            Rectangle = RectangleF.FromLTRB(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y));
        }

        public LinearGradientBrush(Point point1, Point point2, Color color1, Color color2) : this((PointF)point1, (PointF)point2, color1, color2) { }

        public LinearGradientBrush(RectangleF rect, Color color1, Color color2, float angle, bool isAngleScaleable = false)
        {
            if (rect.Width <= 0 || rect.Height <= 0) throw new ArgumentException("Parameter is not valid.");
            LinearColors = [color1, color2];
            Rectangle = rect;
            // The gradient runs along the angle; colour 1 and colour 2 sit on the rectangle's outermost corners.
            float a = angle * MathF.PI / 180;
            var dir = new PointF(MathF.Cos(a), MathF.Sin(a));
            var c = new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            float min = float.MaxValue, max = float.MinValue;
            foreach (var p in new[] { new PointF(rect.Left, rect.Top), new PointF(rect.Right, rect.Top), new PointF(rect.Left, rect.Bottom), new PointF(rect.Right, rect.Bottom) })
            {
                float d = (p.X - c.X) * dir.X + (p.Y - c.Y) * dir.Y;
                min = Math.Min(min, d);
                max = Math.Max(max, d);
            }
            p1 = new PointF(c.X + dir.X * min, c.Y + dir.Y * min);
            p2 = new PointF(c.X + dir.X * max, c.Y + dir.Y * max);
        }

        public LinearGradientBrush(Rectangle rect, Color color1, Color color2, float angle, bool isAngleScaleable = false)
            : this((RectangleF)rect, color1, color2, angle, isAngleScaleable) { }

        public LinearGradientBrush(RectangleF rect, Color color1, Color color2, LinearGradientMode mode)
            : this(rect, color1, color2, mode switch
            {
                LinearGradientMode.Vertical => 90f,
                LinearGradientMode.ForwardDiagonal => 45f,
                LinearGradientMode.BackwardDiagonal => 135f,
                _ => 0f,
            }) { }

        public LinearGradientBrush(Rectangle rect, Color color1, Color color2, LinearGradientMode mode) : this((RectangleF)rect, color1, color2, mode) { }

        public Color[] LinearColors { get; set; }
        public RectangleF Rectangle { get; }

        internal override void Apply(SKPaint paint)
        {
            var (colors, pos) = Stops(LinearColors[0], LinearColors[1]);
            var mode = WrapMode switch { WrapMode.TileFlipX or WrapMode.TileFlipXY or WrapMode.TileFlipY => SKShaderTileMode.Mirror, _ => SKShaderTileMode.Clamp };
            paint.Shader = SKShader.CreateLinearGradient(Sk.P(p1), Sk.P(p2), colors, pos, mode, Transform.M);
            paint.Color = SKColors.White;
        }

        public override object Clone() => MemberwiseClone();
    }

    /// <summary>
    /// GDI+'s path gradient (centre colour fading out to the path's edge), drawn as a radial gradient
    /// that fills the path's bounding ellipse. Exact for ellipses, close for the soft glows the app uses.
    /// </summary>
    public sealed class PathGradientBrush : GradientBrush
    {
        readonly RectangleF bounds;

        public PathGradientBrush(GraphicsPath path)
        {
            bounds = path.GetBounds();
            CenterPoint = new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        }

        public PathGradientBrush(PointF[] points)
        {
            float x0 = points.Min(p => p.X), y0 = points.Min(p => p.Y), x1 = points.Max(p => p.X), y1 = points.Max(p => p.Y);
            bounds = RectangleF.FromLTRB(x0, y0, x1, y1);
            CenterPoint = new PointF(points.Average(p => p.X), points.Average(p => p.Y));
        }

        public PathGradientBrush(Point[] points) : this(points.Select(p => (PointF)p).ToArray()) { }

        public Color CenterColor { get; set; } = Color.White;
        public Color[] SurroundColors { get; set; } = [Color.White];
        public PointF CenterPoint { get; set; }
        public PointF FocusScales { get; set; }
        public RectangleF Rectangle => bounds;

        internal override void Apply(SKPaint paint)
        {
            float rx = Math.Max(bounds.Width / 2, 0.01f), ry = Math.Max(bounds.Height / 2, 0.01f);
            var bc = new PointF(bounds.X + rx, bounds.Y + ry);
            Color edge = SurroundColors.Length > 0 ? SurroundColors[0] : Color.White;

            SKColor[] colors;
            float[] pos;
            if (InterpolationColors is { Colors.Length: >= 2 } ic)
            {
                // GDI+ counts these from the edge (0) to the centre (1).
                colors = Enumerable.Reverse(ic.Colors).Select(Sk.C).ToArray();
                pos = Enumerable.Reverse(ic.Positions).Select(p => 1 - p).ToArray();
            }
            else if (Blend is { Factors.Length: >= 2 } bl)
            {
                colors = Enumerable.Reverse(bl.Factors).Select(f => Sk.C(Mix(edge, CenterColor, f))).ToArray();
                pos = Enumerable.Reverse(bl.Positions).Select(p => 1 - p).ToArray();
            }
            else
            {
                float focus = Math.Clamp(Math.Max(FocusScales.X, FocusScales.Y), 0, 0.99f);
                colors = focus > 0 ? [Sk.C(CenterColor), Sk.C(CenterColor), Sk.C(edge)] : [Sk.C(CenterColor), Sk.C(edge)];
                pos = focus > 0 ? [0, focus, 1] : [0, 1];
            }

            // Unit circle → the bounding ellipse; the focal point is the (possibly off-centre) centre point.
            var local = SKMatrix.Concat(Transform.M, SKMatrix.Concat(SKMatrix.CreateTranslation(bc.X, bc.Y), SKMatrix.CreateScale(rx, ry)));
            var focal = new SKPoint(Math.Clamp((CenterPoint.X - bc.X) / rx, -0.95f, 0.95f), Math.Clamp((CenterPoint.Y - bc.Y) / ry, -0.95f, 0.95f));
            paint.Shader = focal.Length < 1e-3f
                ? SKShader.CreateRadialGradient(new SKPoint(0, 0), 1, colors, pos, SKShaderTileMode.Clamp, local)
                : SKShader.CreateTwoPointConicalGradient(focal, 0, new SKPoint(0, 0), 1, colors, pos, SKShaderTileMode.Clamp, local);
            paint.Color = SKColors.White;
        }

        public override object Clone() => MemberwiseClone();
    }
}
