using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using RetroRadio;

namespace RetroLens;

/// <summary>
/// Applies a <see cref="Look"/> to one picture (or one video frame). The order mimics a real camera:
/// lens → sensor → color processing → noise → compression → display → overlays.
/// </summary>
static class Processor
{
    /// <summary>
    /// Processes one image. <paramref name="frame"/> and <paramref name="time"/> drive the things that move
    /// in video (noise, flicker, tracking, timecode). <paramref name="maxOutputWidth"/> limits size for previews.
    /// </summary>
    public static Bitmap Apply(Bitmap src, Look l, int frame = 0, double time = 0, int maxOutputWidth = 0)
    {
        var crop = CropRect(src.Size, l.Aspect);
        float aspect = crop.Width / (float)crop.Height;
        int w = Math.Clamp(l.Resolution, 16, 4096), h = Math.Max(8, (int)Math.Round(w / aspect));
        if (w > crop.Width) { w = crop.Width; h = Math.Max(8, crop.Height); }

        var rng = new Random(frame * 7919 + 17);
        var f = Sample(src, crop, w, h, l, rng);
        ColorGrade(f, l);
        if (l.Blur > 0.05f) Blur(f, l.Blur);
        if (l.Sharpen > 0.01f) Sharpen(f, l.Sharpen);
        if (l.Bloom > 0.01f) Bloom(f, l.Bloom, l.BloomThreshold);
        Tape(f, l, rng, frame);
        Texture(f, l, rng, frame);
        Lighting(f, l, rng, frame, time);
        Quantize(f, l);

        using var small = f.ToBitmap();
        using var compressed = l.Jpeg > 0 ? JpegRoundTrip(small, l.Jpeg) : null;
        var working = compressed ?? small;

        int outW = l.OutputWidth > 0 ? l.OutputWidth : Math.Clamp(crop.Width, w, 1920);
        if (maxOutputWidth > 0) outW = Math.Min(outW, maxOutputWidth);
        outW = Math.Max(outW, 16);
        int outH = Math.Max(8, (int)Math.Round(outW / aspect));
        var output = new Bitmap(outW, outH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(output))
        {
            g.InterpolationMode = l.Upscale == Upscale.Pixelated ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using var wrap = new ImageAttributes();
            wrap.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(working, new Rectangle(0, 0, outW, outH), 0, 0, working.Width, working.Height, GraphicsUnit.Pixel, wrap);
        }

        if (l.Scanlines > 0 || l.CrtMask > 0 || l.Curvature > 0) Crt(output, l, h);
        Overlay(output, l, frame, time);
        return l.Border == BorderKind.None ? output : AddBorder(output, l);
    }

    public static Rectangle CropRect(Size s, AspectRatio a)
    {
        float target = a switch
        {
            AspectRatio.Classic4x3 => 4f / 3, AspectRatio.Photo3x2 => 3f / 2, AspectRatio.Square => 1, AspectRatio.Wide16x9 => 16f / 9,
            _ => s.Width / (float)s.Height,
        };
        float cur = s.Width / (float)s.Height;
        return cur > target
            ? new Rectangle((int)((s.Width - s.Height * target) / 2), 0, (int)(s.Height * target), s.Height)
            : new Rectangle(0, (int)((s.Height - s.Width / target) / 2), s.Width, (int)(s.Width / target));
    }

    // ───────────────────────────── frame buffer ─────────────────────────────

    sealed class Img(int w, int h)
    {
        public readonly int W = w, H = h;
        public readonly float[] R = new float[w * h], G = new float[w * h], B = new float[w * h];

        public Bitmap ToBitmap()
        {
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            var px = new int[W * H];
            for (int i = 0; i < px.Length; i++)
                px[i] = (255 << 24) | (To8(R[i]) << 16) | (To8(G[i]) << 8) | To8(B[i]);
            var d = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(px, 0, d.Scan0, px.Length);
            bmp.UnlockBits(d);
            return bmp;
        }
    }

    static int To8(float v) => v <= 0 ? 0 : v >= 1 ? 255 : (int)(v * 255 + 0.5f);

    static int[] Pixels(Bitmap bmp)
    {
        var px = new int[bmp.Width * bmp.Height];
        var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < bmp.Height; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * bmp.Width, bmp.Width);
        bmp.UnlockBits(d);
        return px;
    }

    static void Store(Bitmap bmp, int[] px)
    {
        var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < bmp.Height; y++) Marshal.Copy(px, y * bmp.Width, d.Scan0 + y * d.Stride, bmp.Width);
        bmp.UnlockBits(d);
    }

    // ───────────────────────────── lens + sensor ─────────────────────────────

    /// <summary>Reads the picture through the lens: zoom, fisheye/barrel, chromatic aberration and film weave.</summary>
    static Img Sample(Bitmap src, Rectangle crop, int w, int h, Look l, Random rng)
    {
        int pw = Math.Min(crop.Width, w * 2), ph = Math.Min(crop.Height, h * 2);
        using var pre = new Bitmap(pw, ph, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(pre))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var wrap = new ImageAttributes();
            wrap.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(src, new Rectangle(0, 0, pw, ph), crop.X, crop.Y, crop.Width, crop.Height, GraphicsUnit.Pixel, wrap);
        }
        var px = Pixels(pre);
        var img = new Img(w, h);
        float aspect = w / (float)h, diag = MathF.Sqrt(aspect * aspect + 1);
        float k = l.Fisheye, ab = l.Aberration * 0.003f;
        // A round fisheye squeezes the whole picture into the circle; otherwise just soften the edges' stretch.
        float norm = (l.FisheyeCircle ? 1 + Math.Max(0, k) : 1 + Math.Max(0, k) * 0.45f) * l.Zoom;
        float weaveX = (float)(rng.NextDouble() - 0.5) * l.GateWeave * 0.012f, weaveY = (float)(rng.NextDouble() - 0.5) * l.GateWeave * 0.02f;

        Parallel.For(0, h, y =>
        {
            float ny = (y + 0.5f) / h * 2 - 1 + weaveY;
            for (int x = 0; x < w; x++)
            {
                float nx = (x + 0.5f) / w * 2 - 1 + weaveX;
                int i = y * w + x;
                float cx = nx * aspect, cy = ny;
                if (l.FisheyeCircle && cx * cx + cy * cy > 1)
                {
                    img.R[i] = img.G[i] = img.B[i] = 0;
                    continue;
                }
                float rr = MathF.Sqrt(cx * cx + cy * cy) / (l.FisheyeCircle ? 1 : diag);
                float s = (1 + k * rr * rr) / norm;
                img.R[i] = Bilinear(px, pw, ph, nx * s * (1 + ab * rr), ny * s * (1 + ab * rr), 16);
                img.G[i] = Bilinear(px, pw, ph, nx * s, ny * s, 8);
                img.B[i] = Bilinear(px, pw, ph, nx * s * (1 - ab * rr), ny * s * (1 - ab * rr), 0);
            }
        });
        return img;
    }

    static float Bilinear(int[] px, int w, int h, float nx, float ny, int shift)
    {
        float fx = (nx + 1) / 2 * w - 0.5f, fy = (ny + 1) / 2 * h - 0.5f;
        if (fx < -1 || fy < -1 || fx > w || fy > h) return 0;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float tx = fx - x0, ty = fy - y0;
        float Get(int x, int y) => ((px[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)] >> shift) & 0xFF) / 255f;
        return (Get(x0, y0) * (1 - tx) + Get(x0 + 1, y0) * tx) * (1 - ty) + (Get(x0, y0 + 1) * (1 - tx) + Get(x0 + 1, y0 + 1) * tx) * ty;
    }

    // ───────────────────────────── color ─────────────────────────────

    static void ColorGrade(Img f, Look l)
    {
        float cosH = MathF.Cos(l.HueShift * MathF.PI / 180), sinH = MathF.Sin(l.HueShift * MathF.PI / 180);
        float invGamma = 1 / l.Gamma;
        Parallel.For(0, f.H, y =>
        {
            for (int x = 0; x < f.W; x++)
            {
                int i = y * f.W + x;
                float r = f.R[i], g = f.G[i], b = f.B[i];

                // White balance.
                r += l.Temperature * 0.12f; b -= l.Temperature * 0.12f;
                g += l.Tint * -0.1f; r += l.Tint * 0.05f; b += l.Tint * 0.05f;

                // Hue rotation around the grey axis.
                if (l.HueShift != 0)
                {
                    float yy = 0.299f * r + 0.587f * g + 0.114f * b, ii = 0.596f * r - 0.274f * g - 0.322f * b, qq = 0.211f * r - 0.523f * g + 0.312f * b;
                    float i2 = ii * cosH - qq * sinH, q2 = ii * sinH + qq * cosH;
                    r = yy + 0.956f * i2 + 0.621f * q2; g = yy - 0.272f * i2 - 0.647f * q2; b = yy - 1.106f * i2 + 1.703f * q2;
                }

                r += l.Brightness; g += l.Brightness; b += l.Brightness;
                r = (r - 0.5f) * l.Contrast + 0.5f; g = (g - 0.5f) * l.Contrast + 0.5f; b = (b - 0.5f) * l.Contrast + 0.5f;

                float lum = 0.299f * r + 0.587f * g + 0.114f * b;
                r = lum + (r - lum) * l.Saturation; g = lum + (g - lum) * l.Saturation; b = lum + (b - lum) * l.Saturation;

                if (l.CrossProcess > 0)
                {
                    // Cross-processed slide film: yellow highlights, cyan-blue shadows, punchy curve.
                    float c = l.CrossProcess;
                    r = Mix(r, Curve(r, 1.3f), c); g = Mix(g, Curve(g, 1.15f), c); b = Mix(b, b * 0.75f + 0.12f, c);
                }
                if (l.Sepia > 0)
                {
                    float sr = lum * 1.07f + 0.08f, sg = lum * 0.9f + 0.04f, sb = lum * 0.7f;
                    r = Mix(r, sr, l.Sepia); g = Mix(g, sg, l.Sepia); b = Mix(b, sb, l.Sepia);
                }
                if (l.Blown > 0)
                {
                    // Cheap sensors clip early: push the brightest parts to white.
                    float t = Math.Clamp((lum - (1 - l.Blown * 0.45f)) / 0.15f, 0, 1) * l.Blown;
                    r = Mix(r, 1, t); g = Mix(g, 1, t); b = Mix(b, 1, t);
                }

                r = MathF.Pow(Math.Clamp(r, 0, 1), invGamma); g = MathF.Pow(Math.Clamp(g, 0, 1), invGamma); b = MathF.Pow(Math.Clamp(b, 0, 1), invGamma);
                if (l.Fade > 0)
                {
                    r = l.Fade + r * (1 - l.Fade); g = l.Fade + g * (1 - l.Fade); b = l.Fade * 0.9f + b * (1 - l.Fade);
                }
                f.R[i] = r; f.G[i] = g; f.B[i] = b;
            }
        });
    }

    static float Mix(float a, float b, float t) => a + (b - a) * t;
    static float Curve(float v, float s) => Math.Clamp(0.5f + (v - 0.5f) * s, 0, 1);

    // ───────────────────────────── blur, sharpen, bloom ─────────────────────────────

    static float[] BoxBlur(float[] src, int w, int h, int r)
    {
        if (r <= 0) return (float[])src.Clone();
        var tmp = new float[w * h];
        var dst = new float[w * h];
        float inv = 1f / (2 * r + 1);
        Parallel.For(0, h, y =>
        {
            float sum = 0;
            for (int k = -r; k <= r; k++) sum += src[y * w + Math.Clamp(k, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                tmp[y * w + x] = sum * inv;
                sum += src[y * w + Math.Min(w - 1, x + r + 1)] - src[y * w + Math.Max(0, x - r)];
            }
        });
        Parallel.For(0, w, x =>
        {
            float sum = 0;
            for (int k = -r; k <= r; k++) sum += tmp[Math.Clamp(k, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = sum * inv;
                sum += tmp[Math.Min(h - 1, y + r + 1) * w + x] - tmp[Math.Max(0, y - r) * w + x];
            }
        });
        return dst;
    }

    static float[] Soft(float[] c, int w, int h, float radius)
    {
        // Two box blurs ≈ a smooth gaussian; the fractional part fades in a third pass.
        int r = (int)radius;
        var a = BoxBlur(BoxBlur(c, w, h, Math.Max(1, r)), w, h, Math.Max(1, r));
        float frac = radius - r;
        if (r == 0)
        {
            for (int i = 0; i < a.Length; i++) a[i] = Mix(c[i], a[i], frac);
        }
        return a;
    }

    static void Blur(Img f, float radius)
    {
        var r = Soft(f.R, f.W, f.H, radius); var g = Soft(f.G, f.W, f.H, radius); var b = Soft(f.B, f.W, f.H, radius);
        Array.Copy(r, f.R, r.Length); Array.Copy(g, f.G, g.Length); Array.Copy(b, f.B, b.Length);
    }

    /// <summary>Unsharp mask; overdone, it gives the bright halos of cheap digital cameras.</summary>
    static void Sharpen(Img f, float amount)
    {
        foreach (var c in new[] { f.R, f.G, f.B })
        {
            var bl = BoxBlur(c, f.W, f.H, 1);
            for (int i = 0; i < c.Length; i++) c[i] = Math.Clamp(c[i] + (c[i] - bl[i]) * amount, 0, 1);
        }
    }

    static void Bloom(Img f, float amount, float threshold)
    {
        int n = f.W * f.H, r = Math.Max(2, f.W / 45);
        var bright = new float[3][];
        var chans = new[] { f.R, f.G, f.B };
        for (int c = 0; c < 3; c++)
        {
            var b = new float[n];
            for (int i = 0; i < n; i++)
            {
                float lum = 0.299f * f.R[i] + 0.587f * f.G[i] + 0.114f * f.B[i];
                b[i] = chans[c][i] * Math.Max(0, lum - threshold) / Math.Max(0.05f, 1 - threshold);
            }
            bright[c] = BoxBlur(BoxBlur(b, f.W, f.H, r), f.W, f.H, r);
        }
        for (int c = 0; c < 3; c++)
            for (int i = 0; i < n; i++) chans[c][i] = Math.Min(1, chans[c][i] + bright[c][i] * amount * 1.6f);
    }

    // ───────────────────────────── tape (VHS) ─────────────────────────────

    static void Tape(Img f, Look l, Random rng, int frame)
    {
        int w = f.W, h = f.H;
        if (l.ChromaBleed > 0 || l.ColorShift > 0)
        {
            // Split into brightness and color; smear and shift only the color, like analog video.
            var Y = new float[w * h]; var I = new float[w * h]; var Q = new float[w * h];
            for (int i = 0; i < Y.Length; i++)
            {
                float r = f.R[i], g = f.G[i], b = f.B[i];
                Y[i] = 0.299f * r + 0.587f * g + 0.114f * b;
                I[i] = 0.596f * r - 0.274f * g - 0.322f * b;
                Q[i] = 0.211f * r - 0.523f * g + 0.312f * b;
            }
            int rad = (int)(l.ChromaBleed * w / 60f);
            if (rad > 0)
            {
                I = HorizontalBlur(I, w, h, rad);
                Q = HorizontalBlur(Q, w, h, rad);
            }
            int shift = (int)Math.Round(l.ColorShift * w / 352f);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, j = y * w + Math.Clamp(x - shift, 0, w - 1);
                    float yy = Y[i], ii = I[j], qq = Q[j];
                    f.R[i] = yy + 0.956f * ii + 0.621f * qq;
                    f.G[i] = yy - 0.272f * ii - 0.647f * qq;
                    f.B[i] = yy - 1.106f * ii + 1.703f * qq;
                }
            }
        }

        if (l.Jitter > 0 || l.Interlace > 0)
        {
            // Wobbling lines (bad tape tension) and interlace combing.
            var chans = new[] { f.R, f.G, f.B };
            double phase = frame * 0.9 + rng.NextDouble();
            var row = new float[w];
            for (int y = 0; y < h; y++)
            {
                float off = (float)(Math.Sin(y * 0.09 + phase) * 0.6 + Math.Sin(y * 0.021 + phase * 0.3)) * l.Jitter * w / 250f;
                if (rng.NextDouble() < l.Jitter * 0.02) off += (float)(rng.NextDouble() - 0.5) * l.Jitter * w / 30f;
                if (l.Interlace > 0 && (y & 1) == 1) off += l.Interlace * w / 180f;
                if (Math.Abs(off) < 0.01f) continue;
                foreach (var c in chans)
                {
                    for (int x = 0; x < w; x++) row[x] = SampleRow(c, y * w, w, x - off);
                    Array.Copy(row, 0, c, y * w, w);
                }
                if (l.Interlace > 0 && (y & 1) == 1)
                    for (int x = 0; x < w; x++) { int i = y * w + x; f.R[i] *= 1 - l.Interlace * 0.15f; f.G[i] *= 1 - l.Interlace * 0.15f; f.B[i] *= 1 - l.Interlace * 0.15f; }
            }
        }

        if (l.Tracking > 0)
        {
            // A band of snowy, shifted lines rolling up the picture (near the bottom on stills).
            int band = Math.Max(2, (int)(h * 0.04f * (0.5f + l.Tracking)));
            int y0 = frame == 0 ? h - band - (int)(h * 0.05f) : (int)(h - (frame * 3 % (h + band)));
            for (int y = Math.Max(0, y0); y < Math.Min(h, y0 + band); y++)
            {
                float strength = l.Tracking * (1 - Math.Abs((y - y0) / (float)band - 0.5f) * 2);
                float off = (float)(rng.NextDouble() * w * 0.04 * strength);
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float snow = rng.NextDouble() < 0.5 * strength ? (float)rng.NextDouble() : 0;
                    float v = SampleRow(f.R, y * w, w, x - off);
                    f.R[i] = Mix(v, snow + 0.3f, strength * 0.7f);
                    f.G[i] = Mix(SampleRow(f.G, y * w, w, x - off), snow + 0.3f, strength * 0.7f);
                    f.B[i] = Mix(SampleRow(f.B, y * w, w, x - off), snow + 0.3f, strength * 0.7f);
                }
            }
        }
    }

    static float SampleRow(float[] c, int rowStart, int w, float x)
    {
        int x0 = (int)MathF.Floor(x);
        float t = x - x0;
        float a = c[rowStart + Math.Clamp(x0, 0, w - 1)], b = c[rowStart + Math.Clamp(x0 + 1, 0, w - 1)];
        return a + (b - a) * t;
    }

    static float[] HorizontalBlur(float[] src, int w, int h, int r)
    {
        var dst = new float[src.Length];
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < h; y++)
        {
            float sum = 0;
            for (int k = -r; k <= r; k++) sum += src[y * w + Math.Clamp(k, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                dst[y * w + x] = sum * inv;
                sum += src[y * w + Math.Min(w - 1, x + r + 1)] - src[y * w + Math.Max(0, x - r)];
            }
        }
        return dst;
    }

    // ───────────────────────────── texture ─────────────────────────────

    static void Texture(Img f, Look l, Random rng, int frame)
    {
        int n = f.W * f.H;
        if (l.Noise > 0 || l.ChromaNoise > 0)
        {
            int seed = frame * 131 + 7;
            Parallel.For(0, f.H, y =>
            {
                var r = new Random(seed + y * 977);
                for (int x = 0; x < f.W; x++)
                {
                    int i = y * f.W + x;
                    float lum = 0.299f * f.R[i] + 0.587f * f.G[i] + 0.114f * f.B[i];
                    float shadow = 1.4f - lum; // sensors are noisiest in the dark
                    float n0 = (float)(r.NextDouble() - 0.5) * l.Noise * 0.35f * shadow;
                    float cr = (float)(r.NextDouble() - 0.5) * l.ChromaNoise * 0.3f * shadow, cb = (float)(r.NextDouble() - 0.5) * l.ChromaNoise * 0.3f * shadow;
                    f.R[i] += n0 + cr; f.G[i] += n0 - (cr + cb) * 0.5f; f.B[i] += n0 + cb;
                }
            });
        }
        if (l.Grain > 0)
        {
            // Film grain: soft clumps, strongest in the mid-tones.
            var grain = new float[n];
            var r = new Random(frame * 31 + 3);
            for (int i = 0; i < n; i++) grain[i] = (float)(r.NextDouble() - 0.5);
            int rad = Math.Max(0, (int)MathF.Round(l.GrainSize - 1));
            if (rad > 0) grain = BoxBlur(grain, f.W, f.H, rad);
            float scale = l.Grain * 0.28f * (1 + rad * 0.9f);
            for (int i = 0; i < n; i++)
            {
                float lum = 0.299f * f.R[i] + 0.587f * f.G[i] + 0.114f * f.B[i];
                float g = grain[i] * scale * (1 - MathF.Abs(lum - 0.5f) * 1.2f);
                f.R[i] += g; f.G[i] += g; f.B[i] += g;
            }
        }
        if (l.Dust > 0)
        {
            // Specks of dust and the odd vertical scratch.
            int specks = (int)(l.Dust * n / 900f);
            for (int s = 0; s < specks; s++)
            {
                int x = rng.Next(f.W), y = rng.Next(f.H), rad = rng.Next(1, 3);
                float v = rng.NextDouble() < 0.7 ? 0.05f : 0.95f;
                for (int dy = -rad; dy <= rad; dy++)
                    for (int dx = -rad; dx <= rad; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= f.W || yy >= f.H || dx * dx + dy * dy > rad * rad) continue;
                        int i = yy * f.W + xx;
                        f.R[i] = Mix(f.R[i], v, 0.8f); f.G[i] = Mix(f.G[i], v, 0.8f); f.B[i] = Mix(f.B[i], v, 0.8f);
                    }
            }
            int scratches = rng.NextDouble() < l.Dust ? rng.Next(1, 3) : 0;
            for (int s = 0; s < scratches; s++)
            {
                float x = rng.Next(f.W), drift = (float)(rng.NextDouble() - 0.5) * 0.2f;
                for (int y = 0; y < f.H; y++, x += drift)
                {
                    int xi = Math.Clamp((int)x, 0, f.W - 1), i = y * f.W + xi;
                    f.R[i] = Mix(f.R[i], 0.9f, 0.5f); f.G[i] = Mix(f.G[i], 0.9f, 0.5f); f.B[i] = Mix(f.B[i], 0.85f, 0.5f);
                }
            }
        }
    }

    // ───────────────────────────── vignette, light leak, flicker ─────────────────────────────

    static void Lighting(Img f, Look l, Random rng, int frame, double time)
    {
        float flicker = 1 + (float)(rng.NextDouble() - 0.5) * l.Flicker * 0.35f;
        float aspect = f.W / (float)f.H;
        // A warm leak drifting in from one edge.
        float leakX = (float)(0.85 + 0.25 * Math.Sin(time * 0.7 + 1)), leakY = (float)(0.2 + 0.3 * Math.Sin(time * 0.43));
        Parallel.For(0, f.H, y =>
        {
            float ny = (y + 0.5f) / f.H * 2 - 1;
            for (int x = 0; x < f.W; x++)
            {
                int i = y * f.W + x;
                float nx = (x + 0.5f) / f.W * 2 - 1;
                float k = flicker;
                if (l.Vignette > 0)
                {
                    float d = (nx * nx * aspect / 1.2f + ny * ny) / (aspect / 1.2f + 1) * 2;
                    k *= 1 - l.Vignette * Math.Clamp(d * d * 0.9f, 0, 1);
                }
                f.R[i] *= k; f.G[i] *= k; f.B[i] *= k;
                if (l.LightLeak > 0)
                {
                    float dx = (x / (float)f.W - leakX) * aspect, dy = y / (float)f.H - leakY;
                    float leak = MathF.Exp(-(dx * dx + dy * dy) * 3.2f) * l.LightLeak;
                    f.R[i] += leak * 0.9f; f.G[i] += leak * 0.38f; f.B[i] += leak * 0.08f;
                }
            }
        });
    }

    // ───────────────────────────── palettes & dithering ─────────────────────────────

    static readonly int[] Bayer4 = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    static (float R, float G, float B)[] PaletteColors(PaletteKind p) => p switch
    {
        PaletteKind.GameBoyGreen => Rgb(0x0F380F, 0x306230, 0x8BAC0F, 0x9BBC0F),
        PaletteKind.GameBoyGray => Rgb(0x000000, 0x555555, 0xAAAAAA, 0xFFFFFF),
        PaletteKind.VirtualBoy => Rgb(0x000000, 0x550000, 0xA80000, 0xFF0000),
        PaletteKind.Cga => Rgb(0x000000, 0x55FFFF, 0xFF55FF, 0xFFFFFF),
        PaletteKind.Ega16 => Rgb(0x000000, 0x0000AA, 0x00AA00, 0x00AAAA, 0xAA0000, 0xAA00AA, 0xAA5500, 0xAAAAAA,
            0x555555, 0x5555FF, 0x55FF55, 0x55FFFF, 0xFF5555, 0xFF55FF, 0xFFFF55, 0xFFFFFF),
        PaletteKind.Nes => Rgb(0x7C7C7C, 0x0000FC, 0x0000BC, 0x4428BC, 0x940084, 0xA80020, 0xA81000, 0x881400, 0x503000, 0x007800,
            0x006800, 0x005800, 0x004058, 0x000000, 0xBCBCBC, 0x0078F8, 0x0058F8, 0x6844FC, 0xD800CC, 0xE40058, 0xF83800, 0xE45C10,
            0xAC7C00, 0x00B800, 0x00A800, 0x00A844, 0x008888, 0xF8F8F8, 0x3CBCFC, 0x6888FC, 0x9878F8, 0xF878F8, 0xF85898, 0xF87858,
            0xFCA044, 0xF8B800, 0xB8F818, 0x58D854, 0x58F898, 0x00E8D8, 0x787878, 0xFCFCFC, 0xA4E4FC, 0xB8B8F8, 0xD8B8F8, 0xF8B8F8,
            0xF8A4C0, 0xF0D0B0, 0xFCE0A8, 0xF8D878, 0xD8F878, 0xB8F8B8, 0xB8F8D8, 0x00FCFC, 0xF8D8F8),
        PaletteKind.Web256 => Enumerable.Range(0, 216).Select(i => (i / 36 * 0.2f, i / 6 % 6 * 0.2f, i % 6 * 0.2f))
            .Concat(Enumerable.Range(1, 20).Select(i => (i / 21f, i / 21f, i / 21f))).ToArray(),
        _ => [],
    };

    static (float, float, float)[] Rgb(params int[] hex) =>
        hex.Select(c => (((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f)).ToArray();

    static void Quantize(Img f, Look l)
    {
        int w = f.W, h = f.H;
        if (l.Palette == PaletteKind.Full)
        {
            for (int i = 0; i < w * h; i++) { f.R[i] = Math.Clamp(f.R[i], 0, 1); f.G[i] = Math.Clamp(f.G[i], 0, 1); f.B[i] = Math.Clamp(f.B[i], 0, 1); }
            return;
        }
        if (l.Palette == PaletteKind.NightVision)
        {
            for (int i = 0; i < w * h; i++)
            {
                float lum = Math.Clamp(0.299f * f.R[i] + 0.587f * f.G[i] + 0.114f * f.B[i], 0, 1);
                f.R[i] = lum * lum * 0.45f; f.G[i] = 0.06f + lum * 0.94f; f.B[i] = lum * lum * 0.35f;
            }
            return;
        }

        bool mono = l.Palette is PaletteKind.GameBoyGreen or PaletteKind.GameBoyGray or PaletteKind.VirtualBoy;
        var pal = l.Palette == PaletteKind.Posterize ? null : PaletteColors(l.Palette);
        int levels = Math.Clamp(l.Levels, 2, 32);
        float spread = pal == null ? 1f / (levels - 1) : mono ? 1f / 3 : 0.18f;
        var errR = l.Dither == DitherKind.Diffusion ? new float[w * h] : null;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float r = f.R[i], g = f.G[i], b = f.B[i];
                if (mono)
                {
                    float lum = 0.299f * r + 0.587f * g + 0.114f * b;
                    r = g = b = lum;
                }
                if (l.Dither == DitherKind.Pattern)
                {
                    float t = (Bayer4[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f - 0.5f;
                    r += t * spread; g += t * spread; b += t * spread;
                }
                r = Math.Clamp(r, 0, 1); g = Math.Clamp(g, 0, 1); b = Math.Clamp(b, 0, 1);

                float qr, qg, qb;
                float er, eg, eb; // rounding error, for error diffusion
                if (pal == null)
                {
                    qr = MathF.Round(r * (levels - 1)) / (levels - 1); qg = MathF.Round(g * (levels - 1)) / (levels - 1); qb = MathF.Round(b * (levels - 1)) / (levels - 1);
                    (er, eg, eb) = (r - qr, g - qg, b - qb);
                }
                else if (mono)
                {
                    int level = Math.Clamp((int)MathF.Round(r * 3), 0, 3);
                    (qr, qg, qb) = pal[level];
                    er = eg = eb = r - level / 3f; // error measured in brightness
                }
                else
                {
                    float best = float.MaxValue;
                    (qr, qg, qb) = pal[0];
                    foreach (var (pr, pg, pb) in pal)
                    {
                        float dr = r - pr, dg = g - pg, db = b - pb;
                        float d = dr * dr * 0.3f + dg * dg * 0.59f + db * db * 0.11f;
                        if (d < best) { best = d; qr = pr; qg = pg; qb = pb; }
                    }
                    (er, eg, eb) = (r - qr, g - qg, b - qb);
                }

                if (errR != null)
                {
                    // Floyd–Steinberg: push the rounding error onto the neighbours still to come.
                    Spread(f, w, h, x + 1, y, er, eg, eb, 7 / 16f);
                    Spread(f, w, h, x - 1, y + 1, er, eg, eb, 3 / 16f);
                    Spread(f, w, h, x, y + 1, er, eg, eb, 5 / 16f);
                    Spread(f, w, h, x + 1, y + 1, er, eg, eb, 1 / 16f);
                }
                f.R[i] = qr; f.G[i] = qg; f.B[i] = qb;
            }
        }
    }

    static void Spread(Img f, int w, int h, int x, int y, float er, float eg, float eb, float k)
    {
        if (x < 0 || x >= w || y >= h) return;
        int i = y * w + x;
        f.R[i] += er * k; f.G[i] += eg * k; f.B[i] += eb * k;
    }

    // ───────────────────────────── compression ─────────────────────────────

    static readonly ImageCodecInfo JpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    /// <summary>Real JPEG compression, so the blocks and ringing are the genuine article.</summary>
    static Bitmap JpegRoundTrip(Bitmap bmp, int quality)
    {
        using var ms = new MemoryStream();
        using (var ps = new EncoderParameters(1))
        {
            ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(quality, 1, 100));
            bmp.Save(ms, JpegCodec, ps);
        }
        ms.Position = 0;
        using var decoded = new Bitmap(ms);
        return new Bitmap(decoded);
    }

    // ───────────────────────────── CRT ─────────────────────────────

    static void Crt(Bitmap bmp, Look l, int sourceLines)
    {
        int w = bmp.Width, h = bmp.Height;
        var px = Pixels(bmp);
        var outPx = new int[px.Length];
        float period = Math.Max(2, h / (float)sourceLines);
        float aspect = w / (float)h;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float sx = x, sy = y;
                float mask = 1;
                if (l.Curvature > 0)
                {
                    // Bulging glass: sample from a barrel-warped position, black outside the tube.
                    float nx = x / (float)w * 2 - 1, ny = y / (float)h * 2 - 1;
                    float k = l.Curvature * 0.12f;
                    float wx = nx * (1 + k * ny * ny * aspect * 0.6f), wy = ny * (1 + k * nx * nx * 1.4f);
                    float edge = MathF.Max(MathF.Abs(wx), MathF.Abs(wy));
                    if (edge > 1) { outPx[y * w + x] = unchecked((int)0xFF000000); continue; }
                    // Soft rounded corners.
                    float cx = MathF.Max(0, MathF.Abs(wx) - 0.92f) / 0.08f, cy = MathF.Max(0, MathF.Abs(wy) - 0.9f) / 0.1f;
                    mask = Math.Clamp(1 - (cx * cx + cy * cy - 0.6f) * 2, 0, 1);
                    sx = (wx + 1) / 2 * (w - 1); sy = (wy + 1) / 2 * (h - 1);
                }
                int p = px[Math.Clamp((int)sy, 0, h - 1) * w + Math.Clamp((int)sx, 0, w - 1)];
                float r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
                if (l.Scanlines > 0)
                {
                    float phase = (sy % period) / period;
                    float line = 0.5f + 0.5f * MathF.Cos(phase * MathF.PI * 2);
                    float k = 1 - l.Scanlines * 0.7f * (1 - line);
                    r *= k; g *= k; b *= k;
                }
                if (l.CrtMask > 0)
                {
                    int t = x % 3;
                    float lo = 1 - l.CrtMask * 0.55f, hi = 1 + l.CrtMask * 0.25f;
                    r *= t == 0 ? hi : lo; g *= t == 1 ? hi : lo; b *= t == 2 ? hi : lo;
                }
                r *= mask; g *= mask; b *= mask;
                outPx[y * w + x] = (255 << 24) | (Math.Clamp((int)r, 0, 255) << 16) | (Math.Clamp((int)g, 0, 255) << 8) | Math.Clamp((int)b, 0, 255);
            }
        });
        Store(bmp, outPx);
    }

    // ───────────────────────────── overlays ─────────────────────────────

    static void Overlay(Bitmap bmp, Look l, int frame, double time)
    {
        if (l.Stamp == StampKind.None) return;
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        int w = bmp.Width, h = bmp.Height;
        switch (l.Stamp)
        {
            case StampKind.DateOrange:
            case StampKind.DateYellow:
            {
                var color = l.Stamp == StampKind.DateOrange ? Color.FromArgb(255, 150, 40) : Color.FromArgb(255, 220, 60);
                float digitH = h * 0.055f;
                float width = SevenSegWidth(l.StampText, digitH);
                DrawSevenSeg(g, l.StampText, new PointF(w - width - w * 0.05f, h - digitH - h * 0.05f), digitH, color);
                break;
            }
            case StampKind.VhsRec:
            case StampKind.VhsPlay:
            {
                float p = Math.Max(1.5f, h / 150f);
                var ts = TimeSpan.FromSeconds(time);
                bool rec = l.Stamp == StampKind.VhsRec;
                string top = rec ? "REC" : "PLAY " + DotFont.Play;
                float x0 = w * 0.06f, y0 = h * 0.07f;
                if (rec && (frame / 12) % 2 == 0 || rec && frame == 0)
                    using (var red = new SolidBrush(Color.FromArgb(235, 30, 30))) g.FillEllipse(red, x0, y0, p * 7, p * 7);
                OsdText(g, top, rec ? x0 + p * 10 : x0, y0, p);
                OsdText(g, "SP", x0, h - h * 0.07f - p * 7, p);
                string tc = $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}";
                OsdText(g, tc, w - x0 - DotFont.Width(tc, p), y0, p);
                string date = DotFont.Normalize(l.StampText);
                OsdText(g, date, w - x0 - DotFont.Width(date, p), h - h * 0.07f - p * 7, p);
                break;
            }
            case StampKind.Cctv:
            {
                float p = Math.Max(1.2f, h / 200f);
                var now = new DateTime(2026, 9, 27, 3, 14, 0).AddSeconds(time);
                OsdText(g, DotFont.Normalize(l.StampText), w * 0.04f, h * 0.05f, p);
                string stamp = now.ToString("yyyy-MM-dd HH:mm:ss");
                OsdText(g, stamp, w - w * 0.04f - DotFont.Width(stamp, p), h * 0.05f, p);
                break;
            }
        }
    }

    /// <summary>Camcorder on-screen text: white blocky letters with a black edge.</summary>
    static void OsdText(Graphics g, string text, float x, float y, float pitch)
    {
        var dots = new List<RectangleF>();
        DotFont.Emit(dots, text, x, y, pitch, pitch);
        var shadow = dots.Select(d => new RectangleF(d.X + pitch * 0.6f, d.Y + pitch * 0.6f, d.Width, d.Height)).ToArray();
        using (var b = new SolidBrush(Color.FromArgb(200, 0, 0, 0))) g.FillRectangles(b, shadow);
        g.FillRectangles(Brushes.White, dots.ToArray());
    }

    // Seven-segment digits for the classic film-camera date stamp.
    static readonly Dictionary<char, string> Segments = new()
    {
        ['0'] = "abcdef", ['1'] = "bc", ['2'] = "abdeg", ['3'] = "abcdg", ['4'] = "bcfg", ['5'] = "acdfg",
        ['6'] = "acdefg", ['7'] = "abc", ['8'] = "abcdefg", ['9'] = "abcdfg", ['-'] = "g",
    };

    static float SevenSegWidth(string text, float h) => text.Sum(c => c == ' ' ? h * 0.45f : c is '\'' or '.' or ':' ? h * 0.25f : h * 0.8f);

    static void DrawSevenSeg(Graphics g, string text, PointF at, float h, Color color)
    {
        float x = at.X, t = h * 0.13f, w = h * 0.45f;
        using var glow = new Pen(Color.FromArgb(70, color), t * 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var core = new Pen(color, t) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        foreach (char c in text)
        {
            if (c == ' ') { x += h * 0.45f; continue; }
            if (c is '\'' or '.' or ':')
            {
                float yy = c == '\'' ? at.Y : at.Y + h - t;
                foreach (var pen in new[] { glow, core }) g.DrawLine(pen, x + t, yy, x + t, yy + t * 1.5f);
                x += h * 0.25f;
                continue;
            }
            if (!Segments.TryGetValue(c, out var segs)) { x += h * 0.8f; continue; }
            float l = x + t, r = x + t + w, top = at.Y, mid = at.Y + h / 2, bot = at.Y + h;
            var lines = new Dictionary<char, (PointF, PointF)>
            {
                ['a'] = (new(l, top), new(r, top)), ['b'] = (new(r, top), new(r, mid)), ['c'] = (new(r, mid), new(r, bot)),
                ['d'] = (new(l, bot), new(r, bot)), ['e'] = (new(l, mid), new(l, bot)), ['f'] = (new(l, top), new(l, mid)), ['g'] = (new(l, mid), new(r, mid)),
            };
            foreach (var pen in new[] { glow, core })
                foreach (char s in segs)
                {
                    var (p1, p2) = lines[s];
                    // Slight italic slant like real stamps.
                    g.DrawLine(pen, p1.X + (bot - p1.Y) * 0.12f, p1.Y, p2.X + (bot - p2.Y) * 0.12f, p2.Y);
                }
            x += h * 0.8f;
        }
    }

    // ───────────────────────────── borders ─────────────────────────────

    static Bitmap AddBorder(Bitmap img, Look l)
    {
        int w = img.Width, h = img.Height;
        (int left, int top, int right, int bottom) = l.Border switch
        {
            BorderKind.Polaroid => ((int)(w * 0.06f), (int)(w * 0.06f), (int)(w * 0.06f), (int)(w * 0.26f)),
            BorderKind.InstantSquare => ((int)(w * 0.07f), (int)(w * 0.08f), (int)(w * 0.07f), (int)(w * 0.22f)),
            BorderKind.WhiteMat => ((int)(w * 0.08f), (int)(w * 0.08f), (int)(w * 0.08f), (int)(w * 0.08f)),
            BorderKind.FilmStrip => ((int)(w * 0.05f), (int)(h * 0.16f), (int)(w * 0.05f), (int)(h * 0.16f)),
            BorderKind.Tv => ((int)(w * 0.09f), (int)(w * 0.08f), (int)(w * 0.09f), (int)(w * 0.12f)),
            _ => (0, 0, 0, 0),
        };
        var outBmp = new Bitmap(w + left + right, h + top + bottom, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(outBmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        var full = new Rectangle(0, 0, outBmp.Width, outBmp.Height);
        var inner = new Rectangle(left, top, w, h);
        switch (l.Border)
        {
            case BorderKind.Polaroid:
            case BorderKind.InstantSquare:
            case BorderKind.WhiteMat:
            {
                using (var paper = new LinearGradientBrush(full, Color.FromArgb(250, 249, 244), Color.FromArgb(232, 229, 220), 90f)) g.FillRectangle(paper, full);
                // A faint paper texture.
                var rnd = new Random(5);
                using (var fleck = new SolidBrush(Color.FromArgb(10, 0, 0, 0)))
                    for (int i = 0; i < full.Width * full.Height / 60; i++) g.FillRectangle(fleck, rnd.Next(full.Width), rnd.Next(full.Height), 1, 1);
                g.DrawImage(img, inner);
                using (var edge = new Pen(Color.FromArgb(60, 0, 0, 0), 1)) g.DrawRectangle(edge, inner);
                if (l.Caption.Length > 0 && l.Border != BorderKind.WhiteMat)
                {
                    using var f = new Font("Segoe Print", Math.Max(8, bottom * 0.22f), FontStyle.Regular, GraphicsUnit.Pixel);
                    using var ink = new SolidBrush(Color.FromArgb(40, 40, 70));
                    g.DrawString(l.Caption, f, ink, new RectangleF(left, top + h, w, bottom), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                }
                break;
            }
            case BorderKind.FilmStrip:
            {
                g.Clear(Color.FromArgb(18, 14, 10));
                g.DrawImage(img, inner);
                // Sprocket holes above and below.
                float holeW = top * 0.34f, holeH = top * 0.46f, gap = holeW * 1.9f;
                using var hole = new SolidBrush(Color.FromArgb(235, 230, 215));
                for (float x = gap * 0.4f; x < full.Width; x += gap)
                {
                    g.FillRectangle(hole, x, top * 0.27f, holeW, holeH);
                    g.FillRectangle(hole, x, top + h + bottom * 0.27f, holeW, holeH);
                }
                using var f = new Font("Arial", Math.Max(7, top * 0.2f), FontStyle.Bold, GraphicsUnit.Pixel);
                using var ink = new SolidBrush(Color.FromArgb(240, 160, 40));
                g.DrawString("RETRO 400   ▸ 12A        ▸ 13        RETRO 400   ▸ 13A", f, ink, left, top + h + 2);
                break;
            }
            case BorderKind.Tv:
            {
                using (var plastic = new LinearGradientBrush(full, Color.FromArgb(64, 58, 52), Color.FromArgb(24, 22, 20), 90f)) g.FillRectangle(plastic, full);
                using (var screen = new GraphicsPath())
                {
                    float rad = Math.Min(w, h) * 0.08f;
                    screen.AddArc(inner.X, inner.Y, rad * 2, rad * 2, 180, 90);
                    screen.AddArc(inner.Right - rad * 2, inner.Y, rad * 2, rad * 2, 270, 90);
                    screen.AddArc(inner.Right - rad * 2, inner.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
                    screen.AddArc(inner.X, inner.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
                    screen.CloseFigure();
                    using (var shadow = new Pen(Color.FromArgb(160, 0, 0, 0), Math.Max(3, w * 0.012f))) g.DrawPath(shadow, screen);
                    g.SetClip(screen);
                    g.DrawImage(img, inner);
                    // Reflection on the glass.
                    using var glare = new LinearGradientBrush(inner, Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 60f);
                    g.FillRectangle(glare, inner.X, inner.Y, inner.Width, inner.Height / 2);
                    g.ResetClip();
                }
                // Control knobs under the screen.
                float kr = bottom * 0.22f;
                using var knob = new LinearGradientBrush(new RectangleF(0, top + h, 10, bottom), Color.FromArgb(130, 125, 118), Color.FromArgb(40, 38, 35), 90f);
                for (int i = 0; i < 2; i++) g.FillEllipse(knob, full.Width - left - kr * (2.6f + i * 2.8f), top + h + bottom * 0.5f - kr, kr * 2, kr * 2);
                using var logo = new Font("Arial Black", Math.Max(7, bottom * 0.2f), FontStyle.Bold, GraphicsUnit.Pixel);
                using var silver = new SolidBrush(Color.FromArgb(190, 185, 170));
                g.DrawString("TRINITRONIX", logo, silver, left, top + h + bottom * 0.32f);
                break;
            }
        }
        img.Dispose();
        return outBmp;
    }
}
