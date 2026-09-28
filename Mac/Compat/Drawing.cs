// A small System.Drawing (GDI+) look-alike built on SkiaSharp, so the radio's drawing code runs on macOS.
// Only what Retro Radio uses is here; behaviour follows GDI+ closely enough for identical layouts.
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace System.Drawing
{
    public enum GraphicsUnit { World, Display, Pixel, Point, Inch, Document, Millimeter }

    [Flags]
    public enum FontStyle { Regular = 0, Bold = 1, Italic = 2, Underline = 4, Strikeout = 8 }

    public enum StringAlignment { Near, Center, Far }

    [Flags]
    public enum StringFormatFlags
    {
        DirectionRightToLeft = 1, DirectionVertical = 2, FitBlackBox = 4, DisplayFormatControl = 32,
        NoFontFallback = 1024, MeasureTrailingSpaces = 2048, NoWrap = 4096, LineLimit = 8192, NoClip = 16384,
    }

    public enum StringTrimming { None, Character, Word, EllipsisCharacter, EllipsisWord, EllipsisPath }

    public enum ContentAlignment
    {
        TopLeft = 1, TopCenter = 2, TopRight = 4, MiddleLeft = 16, MiddleCenter = 32, MiddleRight = 64,
        BottomLeft = 256, BottomCenter = 512, BottomRight = 1024,
    }

    internal static class Sk
    {
        public static SKColor C(Color c) => new(c.R, c.G, c.B, c.A);
        public static SKRect R(RectangleF r) => new(r.Left, r.Top, r.Right, r.Bottom);
        public static SKRect R(Rectangle r) => new(r.Left, r.Top, r.Right, r.Bottom);
        public static SKPoint P(PointF p) => new(p.X, p.Y);
        public static RectangleF R(SKRect r) => new(r.Left, r.Top, r.Width, r.Height);

        /// <summary>A rectangle standing in for "everything" (GDI+'s infinite region).</summary>
        public static SKPath Infinite()
        {
            var p = new SKPath();
            p.AddRect(new SKRect(-1e7f, -1e7f, 1e7f, 1e7f));
            return p;
        }
    }

    // ───────────────────────────── images ─────────────────────────────

    public abstract class Image : IDisposable, ICloneable
    {
        internal SKBitmap Sk = null!;
        bool disposed;

        public int Width => Alive.Width;
        public int Height => Alive.Height;
        public Size Size => new(Width, Height);
        public PixelFormat PixelFormat { get; internal set; } = PixelFormat.Format32bppArgb;
        public float HorizontalResolution => 96;
        public float VerticalResolution => 96;

        internal SKBitmap Alive => disposed ? throw new ArgumentException("Parameter is not valid.") : Sk;

        public static Image FromStream(Stream stream) => Bitmap.Decode(stream);

        public static Image FromFile(string path)
        {
            using var f = File.OpenRead(path);
            return Bitmap.Decode(f);
        }

        public void Save(string path) => Save(path, ImageFormat.ForPath(path));

        public void Save(string path, ImageFormat format)
        {
            using var fs = File.Create(path);
            Save(fs, format);
        }

        public void Save(Stream stream, ImageFormat format)
        {
            using var img = SKImage.FromBitmap(Alive);
            using var data = img.Encode(format.Sk, 92);
            data.SaveTo(stream);
        }

        public object Clone() => new Bitmap(this);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Sk?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Premultiplied BGRA pixels in memory, like GDI+'s 32bppPArgb.</summary>
    public sealed class Bitmap : Image
    {
        public Bitmap(int width, int height) : this(width, height, PixelFormat.Format32bppArgb) { }

        public Bitmap(int width, int height, PixelFormat format)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("Parameter is not valid.");
            Sk = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            Sk.Erase(SKColors.Transparent);
            PixelFormat = format;
        }

        public Bitmap(Image original) : this(original, original.Width, original.Height) { }
        public Bitmap(Image original, Size size) : this(original, size.Width, size.Height) { }

        public Bitmap(Image original, int width, int height) : this(width, height)
        {
            using var c = new SKCanvas(Sk);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            c.DrawBitmap(original.Alive, new SKRect(0, 0, width, height), paint);
        }

        public Bitmap(string path)
        {
            using var f = File.OpenRead(path);
            Sk = DecodeSk(f);
        }

        public Bitmap(Stream stream) => Sk = DecodeSk(stream);

        internal Bitmap(SKBitmap bitmap) => Sk = bitmap;

        internal static Bitmap Decode(Stream stream) => new(DecodeSk(stream));

        static SKBitmap DecodeSk(Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            using var decoded = SKBitmap.Decode(ms) ?? throw new ArgumentException("Parameter is not valid.");
            var bgra = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var c = new SKCanvas(bgra))
            {
                c.Clear(SKColors.Transparent);
                c.DrawBitmap(decoded, 0, 0);
            }
            return bgra;
        }

        public Color GetPixel(int x, int y)
        {
            var c = Alive.GetPixel(x, y); // unpremultiplied
            return Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);
        }

        public void SetPixel(int x, int y, Color color) => Alive.SetPixel(x, y, Drawing.Sk.C(color));

        public void MakeTransparent() { }

        /// <summary>Copies pixels out (un-premultiplying for the non-P formats), and back in on UnlockBits.</summary>
        public BitmapData LockBits(Rectangle rect, ImageLockMode mode, PixelFormat format)
        {
            var bmp = Alive;
            if (rect.X < 0 || rect.Y < 0 || rect.Right > bmp.Width || rect.Bottom > bmp.Height || rect.Width <= 0 || rect.Height <= 0)
                throw new ArgumentException("Parameter is not valid.");
            int stride = rect.Width * 4;
            var buffer = Marshal.AllocHGlobal(stride * rect.Height);
            var data = new BitmapData { Width = rect.Width, Height = rect.Height, Stride = stride, Scan0 = buffer, PixelFormat = format, Rect = rect, Mode = mode };
            if (mode != ImageLockMode.WriteOnly)
            {
                bool premul = format == PixelFormat.Format32bppPArgb;
                var row = new byte[stride];
                IntPtr src = bmp.GetPixels();
                int srcStride = bmp.RowBytes;
                for (int y = 0; y < rect.Height; y++)
                {
                    Marshal.Copy(src + (rect.Y + y) * srcStride + rect.X * 4, row, 0, stride);
                    if (!premul) Unpremultiply(row);
                    if (format == PixelFormat.Format32bppRgb || format == PixelFormat.Format24bppRgb)
                        for (int i = 3; i < stride; i += 4) row[i] = 255;
                    Marshal.Copy(row, 0, buffer + y * stride, stride);
                }
            }
            return data;
        }

        public void UnlockBits(BitmapData data)
        {
            try
            {
                if (data.Mode == ImageLockMode.ReadOnly) return;
                var bmp = Alive;
                bool premul = data.PixelFormat == PixelFormat.Format32bppPArgb;
                bool opaque = data.PixelFormat is PixelFormat.Format32bppRgb or PixelFormat.Format24bppRgb;
                var row = new byte[data.Stride];
                IntPtr dst = bmp.GetPixels();
                int dstStride = bmp.RowBytes;
                for (int y = 0; y < data.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                    if (opaque) for (int i = 3; i < row.Length; i += 4) row[i] = 255;
                    else if (!premul) Premultiply(row);
                    Marshal.Copy(row, 0, dst + (data.Rect.Y + y) * dstStride + data.Rect.X * 4, data.Stride);
                }
                bmp.NotifyPixelsChanged();
            }
            finally
            {
                Marshal.FreeHGlobal(data.Scan0);
                data.Scan0 = IntPtr.Zero;
            }
        }

        static void Unpremultiply(byte[] px)
        {
            for (int i = 0; i < px.Length; i += 4)
            {
                int a = px[i + 3];
                if (a == 255 || a == 0) continue;
                px[i] = (byte)Math.Min(255, px[i] * 255 / a);
                px[i + 1] = (byte)Math.Min(255, px[i + 1] * 255 / a);
                px[i + 2] = (byte)Math.Min(255, px[i + 2] * 255 / a);
            }
        }

        static void Premultiply(byte[] px)
        {
            for (int i = 0; i < px.Length; i += 4)
            {
                int a = px[i + 3];
                if (a == 255) continue;
                px[i] = (byte)((px[i] * a + 127) / 255);
                px[i + 1] = (byte)((px[i + 1] * a + 127) / 255);
                px[i + 2] = (byte)((px[i + 2] * a + 127) / 255);
            }
        }
    }

    // ───────────────────────────── fonts ─────────────────────────────

    public sealed class FontFamily : IDisposable
    {
        public FontFamily(string name) => Name = name;
        public string Name { get; }

        public static FontFamily GenericSansSerif => new("Segoe UI");
        public static FontFamily GenericMonospace => new("Consolas");
        public static FontFamily GenericSerif => new("Times New Roman");

        /// <summary>The installed families, plus the Windows names this app asks for (they're mapped to Mac fonts).</summary>
        public static FontFamily[] Families =>
        [
            .. new[] { "Segoe UI", "Bahnschrift", "Consolas" }.Select(n => new FontFamily(n)),
            .. SKFontManager.Default.FontFamilies.Distinct().OrderBy(n => n).Select(n => new FontFamily(n)),
        ];

        public bool IsStyleAvailable(FontStyle style) => true;

        // Metrics in design units of a 2048-unit em, like most TrueType fonts.
        public int GetEmHeight(FontStyle style) => 2048;
        public int GetCellAscent(FontStyle style) => (int)Math.Round(-Metrics(style).Ascent);
        public int GetCellDescent(FontStyle style) => (int)Math.Round(Metrics(style).Descent);
        public int GetLineSpacing(FontStyle style) { var m = Metrics(style); return (int)Math.Round(-m.Ascent + m.Descent + m.Leading); }

        SKFontMetrics Metrics(FontStyle style)
        {
            using var p = new SKPaint { Typeface = FontMap.Resolve(Name, style), TextSize = 2048 };
            return p.FontMetrics;
        }
        public void Dispose() { }
        public override string ToString() => $"[FontFamily: Name={Name}]";
    }

    public sealed class Font : IDisposable, ICloneable
    {
        public Font(string familyName, float emSize) : this(familyName, emSize, FontStyle.Regular, GraphicsUnit.Point) { }
        public Font(string familyName, float emSize, FontStyle style) : this(familyName, emSize, style, GraphicsUnit.Point) { }
        public Font(string familyName, float emSize, GraphicsUnit unit) : this(familyName, emSize, FontStyle.Regular, unit) { }
        public Font(FontFamily family, float emSize) : this(family.Name, emSize, FontStyle.Regular, GraphicsUnit.Point) { }
        public Font(FontFamily family, float emSize, FontStyle style) : this(family.Name, emSize, style, GraphicsUnit.Point) { }
        public Font(FontFamily family, float emSize, FontStyle style, GraphicsUnit unit) : this(family.Name, emSize, style, unit) { }
        public Font(Font prototype, FontStyle newStyle) : this(prototype.Name, prototype.Size, newStyle, prototype.Unit) { }

        public Font(string familyName, float emSize, FontStyle style, GraphicsUnit unit)
        {
            if (emSize <= 0 || float.IsNaN(emSize)) throw new ArgumentException("Parameter is not valid.");
            Name = familyName;
            Size = emSize;
            Style = style;
            Unit = unit;
            Px = unit switch
            {
                GraphicsUnit.Point => emSize * 96f / 72f,
                GraphicsUnit.Inch => emSize * 96f,
                GraphicsUnit.Millimeter => emSize * 96f / 25.4f,
                GraphicsUnit.Document => emSize * 96f / 300f,
                _ => emSize,
            };
            IsIcon = FontMap.IsIconFont(familyName);
            Typeface = FontMap.Resolve(familyName, style);
        }

        public string Name { get; }
        public float Size { get; }
        public FontStyle Style { get; }
        public GraphicsUnit Unit { get; }
        public bool Bold => (Style & FontStyle.Bold) != 0;
        public bool Italic => (Style & FontStyle.Italic) != 0;
        public bool Underline => (Style & FontStyle.Underline) != 0;
        public FontFamily FontFamily => new(Name);
        public float SizeInPoints => Px * 72f / 96f;
        public int Height => (int)Math.Ceiling(GetHeight());

        internal readonly float Px;
        internal readonly bool IsIcon;
        internal readonly SKTypeface Typeface;

        public float GetHeight() => TextLayout.Metrics(this).LineHeight;

        public float GetHeight(Graphics g) => GetHeight();
        public object Clone() => new Font(Name, Size, Style, Unit);
        public void Dispose() { }
    }

    /// <summary>Maps the Windows font names the app uses to fonts every Mac has.</summary>
    internal static class FontMap
    {
        static readonly Dictionary<(string, FontStyle), SKTypeface> cache = [];

        public static bool IsIconFont(string name) =>
            name.StartsWith("Segoe MDL2", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Segoe Fluent", StringComparison.OrdinalIgnoreCase);

        public static SKTypeface Resolve(string name, FontStyle style)
        {
            lock (cache)
            {
                if (cache.TryGetValue((name, style), out var hit)) return hit;
                var tf = Find(name, style);
                cache[(name, style)] = tf;
                return tf;
            }
        }

        static SKTypeface Find(string name, FontStyle style)
        {
            string n = name.ToLowerInvariant();
            int weight = (style & FontStyle.Bold) != 0 ? 700 : 400;
            var width = SKFontStyleWidth.Normal;
            if (n.Contains("semibold")) weight = Math.Max(weight, 600);
            else if (n.Contains("semilight")) weight = 350;
            else if (n.Contains("light")) weight = 300;
            if (n.Contains("black")) weight = 900;
            else if (n.Contains(" bold")) weight = 700;
            if (n.Contains("condensed") || n.Contains("semiconden")) width = SKFontStyleWidth.Condensed;
            var slant = (style & FontStyle.Italic) != 0 ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
            var fs = new SKFontStyle(weight, (int)width, slant);

            string[] candidates =
                n.StartsWith("segoe ui symbol") || n.StartsWith("segoe ui emoji") ? ["Segoe UI Symbol", "Apple Symbols", "Apple Color Emoji", "Helvetica Neue"]
                : n.StartsWith("segoe") ? ["Segoe UI", "Helvetica Neue", "Helvetica", "Arial"]
                : n.StartsWith("bahnschrift") ? [name, "Bahnschrift", "DIN Alternate", "DIN Condensed", "Avenir Next Condensed", "Helvetica Neue"]
                : n.StartsWith("consolas") ? ["Consolas", "Menlo", "Monaco", "Courier New"]
                : n.StartsWith("ms gothic") || n.StartsWith("yu gothic") || n.StartsWith("meiryo") ? ["MS Gothic", "Hiragino Sans", "Hiragino Kaku Gothic ProN", "Osaka", "PingFang SC"]
                : n.StartsWith("malgun") || n.StartsWith("gulim") ? ["Malgun Gothic", "Apple SD Gothic Neo", "AppleGothic"]
                : n.StartsWith("arial black") ? ["Arial Black", "Helvetica Neue"]
                : [name, "Helvetica Neue", "Arial"];
            foreach (var c in candidates)
            {
                var tf = SKFontManager.Default.MatchFamily(c, fs);
                if (tf != null) return tf;
            }
            return SKTypeface.FromFamilyName(null, fs) ?? SKTypeface.Default;
        }
    }

    public sealed class StringFormat : IDisposable, ICloneable
    {
        public StringFormat() { }
        public StringFormat(StringFormatFlags flags) => FormatFlags = flags;
        public StringFormat(StringFormat other)
        {
            Alignment = other.Alignment;
            LineAlignment = other.LineAlignment;
            FormatFlags = other.FormatFlags;
            Trimming = other.Trimming;
            Typographic = other.Typographic;
        }

        public StringAlignment Alignment { get; set; }
        public StringAlignment LineAlignment { get; set; }
        public StringFormatFlags FormatFlags { get; set; }
        public StringTrimming Trimming { get; set; } = StringTrimming.Character;
        internal bool Typographic;

        public static StringFormat GenericDefault => new();
        public static StringFormat GenericTypographic => new() { Typographic = true, FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.LineLimit, Trimming = StringTrimming.None };

        public object Clone() => new StringFormat(this);
        public void Dispose() { }
    }

    // ───────────────────────────── brushes and pens ─────────────────────────────

    public abstract class Brush : IDisposable, ICloneable
    {
        internal abstract void Apply(SKPaint paint);
        public abstract object Clone();
        public virtual void Dispose() { }
    }

    public sealed class SolidBrush : Brush
    {
        public SolidBrush(Color color) => Color = color;
        public Color Color { get; set; }
        internal override void Apply(SKPaint paint) => paint.Color = Sk.C(Color);
        public override object Clone() => new SolidBrush(Color);
    }

    public static class Brushes
    {
        public static Brush White => new SolidBrush(Color.White);
        public static Brush Black => new SolidBrush(Color.Black);
        public static Brush Transparent => new SolidBrush(Color.Transparent);
        public static Brush Gray => new SolidBrush(Color.Gray);
    }

    public sealed class TextureBrush : Brush
    {
        readonly Image image;
        public TextureBrush(Image image) : this(image, WrapMode.Tile) { }
        public TextureBrush(Image image, WrapMode mode)
        {
            this.image = new Bitmap(image);
            WrapMode = mode;
        }

        public WrapMode WrapMode { get; set; }
        public Matrix Transform { get; set; } = new();

        public void TranslateTransform(float dx, float dy, MatrixOrder order = MatrixOrder.Prepend) => Transform.Translate(dx, dy, order);
        public void ScaleTransform(float sx, float sy, MatrixOrder order = MatrixOrder.Prepend) => Transform.Scale(sx, sy, order);
        public void RotateTransform(float angle, MatrixOrder order = MatrixOrder.Prepend) => Transform.Rotate(angle, order);
        public void ResetTransform() => Transform = new Matrix();

        internal override void Apply(SKPaint paint)
        {
            var (tx, ty) = WrapMode switch
            {
                WrapMode.TileFlipX => (SKShaderTileMode.Mirror, SKShaderTileMode.Repeat),
                WrapMode.TileFlipY => (SKShaderTileMode.Repeat, SKShaderTileMode.Mirror),
                WrapMode.TileFlipXY => (SKShaderTileMode.Mirror, SKShaderTileMode.Mirror),
                WrapMode.Clamp => (SKShaderTileMode.Clamp, SKShaderTileMode.Clamp),
                _ => (SKShaderTileMode.Repeat, SKShaderTileMode.Repeat),
            };
            paint.Shader = SKShader.CreateBitmap(image.Alive, tx, ty, Transform.M);
            paint.Color = SKColors.White;
        }

        public override object Clone() => new TextureBrush(image, WrapMode) { Transform = Transform.Clone() };
        public override void Dispose() => image.Dispose();
    }

    public enum PenAlignment { Center, Inset, Outset, Left, Right }

    public sealed class Pen : IDisposable, ICloneable
    {
        public Pen(Color color, float width = 1) { Color = color; Width = width; }
        public Pen(Brush brush, float width = 1) { Brush = brush; Width = width; if (brush is SolidBrush sb) Color = sb.Color; }

        public Color Color { get; set; }
        public Brush? Brush { get; set; }
        public float Width { get; set; }
        public LineCap StartCap { get; set; }
        public LineCap EndCap { get; set; }
        public DashCap DashCap { get; set; }
        public LineJoin LineJoin { get; set; }
        public DashStyle DashStyle { get; set; }
        public float[]? DashPattern { get; set; }
        public float DashOffset { get; set; }
        public float MiterLimit { get; set; } = 10;
        public PenAlignment Alignment { get; set; }

        public void SetLineCap(LineCap start, LineCap end, DashCap dash) { StartCap = start; EndCap = end; DashCap = dash; }

        internal void Apply(SKPaint paint)
        {
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = Width <= 0 ? 0 : Width;
            if (Brush != null && Brush is not SolidBrush) Brush.Apply(paint);
            else paint.Color = Sk.C(Brush is SolidBrush sb ? sb.Color : Color);
            static bool Round(LineCap c) => c is LineCap.Round or LineCap.RoundAnchor;
            static bool Square(LineCap c) => c is LineCap.Square or LineCap.SquareAnchor;
            paint.StrokeCap = Round(StartCap) || Round(EndCap) ? SKStrokeCap.Round : Square(StartCap) || Square(EndCap) ? SKStrokeCap.Square : SKStrokeCap.Butt;
            paint.StrokeJoin = LineJoin switch { LineJoin.Round => SKStrokeJoin.Round, LineJoin.Bevel => SKStrokeJoin.Bevel, _ => SKStrokeJoin.Miter };
            paint.StrokeMiter = MiterLimit;
            float w = Math.Max(1, Width);
            float[]? dashes = DashStyle switch
            {
                DashStyle.Dash => [3 * w, w],
                DashStyle.Dot => [w, w],
                DashStyle.DashDot => [3 * w, w, w, w],
                DashStyle.DashDotDot => [3 * w, w, w, w, w, w],
                DashStyle.Custom when DashPattern is { Length: >= 2 } p => p.Select(v => v * w).ToArray(),
                _ => null,
            };
            if (dashes != null) paint.PathEffect = SKPathEffect.CreateDash(dashes, DashOffset * w);
        }

        public object Clone() => MemberwiseClone();
        public void Dispose() { }
    }

    // ───────────────────────────── regions ─────────────────────────────

    public sealed class Region : IDisposable
    {
        internal SKPath? Path; // null = infinite

        public Region() { }
        public Region(RectangleF rect) { Path = new SKPath(); Path.AddRect(Sk.R(rect)); }
        public Region(Rectangle rect) : this((RectangleF)rect) { }
        public Region(GraphicsPath path) => Path = new SKPath(path.P);
        internal Region(SKPath? path) => Path = path;

        public bool IsInfinite(Graphics g) => Path == null;
        public bool IsEmpty(Graphics g) => Path != null && Path.IsEmpty;

        void Combine(SKPath other, SKPathOp op)
        {
            var mine = Path ?? Sk.Infinite();
            var result = mine.Op(other, op) ?? new SKPath();
            if (Path != mine) mine.Dispose();
            Path?.Dispose();
            Path = result;
        }

        static SKPath RectPath(RectangleF r)
        {
            var p = new SKPath();
            p.AddRect(Sk.R(r));
            return p;
        }

        public void Intersect(RectangleF r) { using var p = RectPath(r); Combine(p, SKPathOp.Intersect); }
        public void Intersect(Rectangle r) => Intersect((RectangleF)r);
        public void Intersect(GraphicsPath path) => Combine(path.P, SKPathOp.Intersect);
        public void Intersect(Region r) { if (r.Path != null) Combine(r.Path, SKPathOp.Intersect); }
        public void Union(RectangleF r) { if (Path == null) return; using var p = RectPath(r); Combine(p, SKPathOp.Union); }
        public void Union(Rectangle r) => Union((RectangleF)r);
        public void Union(GraphicsPath path) { if (Path != null) Combine(path.P, SKPathOp.Union); }
        public void Union(Region r) { if (Path == null) return; if (r.Path == null) { Path.Dispose(); Path = null; } else Combine(r.Path, SKPathOp.Union); }
        public void Exclude(RectangleF r) { using var p = RectPath(r); Combine(p, SKPathOp.Difference); }
        public void Exclude(Rectangle r) => Exclude((RectangleF)r);
        public void Exclude(GraphicsPath path) => Combine(path.P, SKPathOp.Difference);
        public void Exclude(Region r) { if (r.Path == null) { MakeEmpty(); return; } Combine(r.Path, SKPathOp.Difference); }
        public void Xor(GraphicsPath path) => Combine(path.P, SKPathOp.Xor);

        public void MakeInfinite() { Path?.Dispose(); Path = null; }
        public void MakeEmpty() { Path?.Dispose(); Path = new SKPath(); }
        public void Translate(float dx, float dy) => Path?.Transform(SKMatrix.CreateTranslation(dx, dy));
        public void Transform(Matrix m) => Path?.Transform(m.M);

        public bool IsVisible(PointF p) => Path == null || Path.Contains(p.X, p.Y);
        public bool IsVisible(float x, float y) => IsVisible(new PointF(x, y));
        public bool IsVisible(Point p) => IsVisible((PointF)p);
        public bool IsVisible(PointF p, Graphics g) => IsVisible(p);

        public RectangleF GetBounds(Graphics g) => Path == null ? new RectangleF(-4194304, -4194304, 8388608, 8388608) : Sk.R(Path.Bounds);

        public Region Clone() => new(Path == null ? null : new SKPath(Path));
        public void Dispose() { Path?.Dispose(); Path = null; }
    }
}

namespace System.Drawing.Imaging
{
    public enum PixelFormat
    {
        Format24bppRgb = 137224, Format32bppRgb = 139273, Format32bppArgb = 2498570, Format32bppPArgb = 925707,
        Format8bppIndexed = 198659, Format1bppIndexed = 196865,
    }

    public enum ImageLockMode { ReadOnly = 1, WriteOnly = 2, ReadWrite = 3, UserInputBuffer = 4 }

    public sealed class BitmapData
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Stride { get; set; }
        public IntPtr Scan0 { get; set; }
        public PixelFormat PixelFormat { get; set; }
        internal Rectangle Rect;
        internal ImageLockMode Mode;
    }

    public sealed class ImageFormat
    {
        internal readonly SKEncodedImageFormat Sk;
        ImageFormat(SKEncodedImageFormat f) => Sk = f;
        public static ImageFormat Png { get; } = new(SKEncodedImageFormat.Png);
        public static ImageFormat Jpeg { get; } = new(SKEncodedImageFormat.Jpeg);
        public static ImageFormat Bmp { get; } = new(SKEncodedImageFormat.Bmp);
        public static ImageFormat Gif { get; } = new(SKEncodedImageFormat.Gif);

        internal static ImageFormat ForPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => Jpeg,
            ".bmp" => Bmp,
            _ => Png,
        };
    }

    public enum ColorAdjustType { Default, Bitmap, Brush, Pen, Text, Count, Any }
    public enum ColorMatrixFlag { Default, SkipGrays, AltGrays }

    /// <summary>GDI+'s 5x5 colour matrix (rows: source R, G, B, A, 1).</summary>
    public sealed class ColorMatrix
    {
        internal readonly float[,] M = new float[5, 5];

        public ColorMatrix()
        {
            for (int i = 0; i < 5; i++) M[i, i] = 1;
        }

        public ColorMatrix(float[][] m)
        {
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                    M[r, c] = m[r][c];
        }

        public float this[int row, int column] { get => M[row, column]; set => M[row, column] = value; }
        public float Matrix00 { get => M[0, 0]; set => M[0, 0] = value; }
        public float Matrix11 { get => M[1, 1]; set => M[1, 1] = value; }
        public float Matrix22 { get => M[2, 2]; set => M[2, 2] = value; }
        public float Matrix33 { get => M[3, 3]; set => M[3, 3] = value; }
        public float Matrix40 { get => M[4, 0]; set => M[4, 0] = value; }
        public float Matrix41 { get => M[4, 1]; set => M[4, 1] = value; }
        public float Matrix42 { get => M[4, 2]; set => M[4, 2] = value; }
        public float Matrix43 { get => M[4, 3]; set => M[4, 3] = value; }

        /// <summary>Just an alpha multiplier? Then it's drawn with a plain alpha instead of a colour filter.</summary>
        internal bool IsAlphaOnly(out float alpha)
        {
            alpha = M[3, 3];
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                    if (!(r == 3 && c == 3) && M[r, c] != (r == c ? 1 : 0)) return false;
            return true;
        }

        internal SKColorFilter ToFilter()
        {
            var m = new float[20];
            for (int o = 0; o < 4; o++)
            {
                for (int i = 0; i < 4; i++) m[o * 5 + i] = M[i, o];
                m[o * 5 + 4] = M[4, o];
            }
            return SKColorFilter.CreateColorMatrix(m);
        }
    }

    public sealed class ImageAttributes : IDisposable
    {
        internal ColorMatrix? Matrix;
        public void SetColorMatrix(ColorMatrix m) => Matrix = m;
        public void SetColorMatrix(ColorMatrix m, ColorMatrixFlag flags) => Matrix = m;
        public void SetColorMatrix(ColorMatrix m, ColorMatrixFlag flags, ColorAdjustType type) => Matrix = m;
        public void SetWrapMode(Drawing2D.WrapMode mode) { }
        public void Dispose() { }
    }
}

namespace System.Drawing.Text
{
    public enum TextRenderingHint { SystemDefault, SingleBitPerPixelGridFit, SingleBitPerPixel, AntiAliasGridFit, AntiAlias, ClearTypeGridFit }
}
