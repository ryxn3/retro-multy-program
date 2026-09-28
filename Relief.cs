using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RetroRadio;

/// <summary>Surface materials; each reacts to light differently.</summary>
enum Mat : byte { Plastic, Brushed, Chrome, Gloss, Rubber, Varnish, Clearcoat, Backlit, Glass = 255 }

enum Profile { Round, Linear, Smooth }

/// <summary>
/// A tiny 2.5D renderer: shapes are painted into a height map plus a color (albedo) map,
/// then lit from the top-left with diffuse shading, specular highlights, environment
/// reflections (for chrome and gloss), soft cast shadows and ambient occlusion.
/// </summary>
sealed class Relief : IDisposable
{
    public readonly int W, H;
    public readonly float S;
    public readonly float[] Height;
    public readonly Mat[] Mats;
    public readonly Bitmap Albedo;
    public readonly Graphics G;

    public Relief(float logicalW, float logicalH, float scale)
    {
        S = scale;
        W = (int)Math.Ceiling(logicalW * scale);
        H = (int)Math.Ceiling(logicalH * scale);
        Height = new float[W * H];
        Mats = new Mat[W * H];
        Albedo = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        G = Graphics.FromImage(Albedo);
        G.SmoothingMode = SmoothingMode.AntiAlias;
        G.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        G.ScaleTransform(scale, scale);
    }

    public void Dispose()
    {
        G.Dispose();
        Albedo.Dispose();
    }

    /// <summary>
    /// Raises (positive) or sinks (negative) the area inside the path by <paramref name="elevation"/>
    /// logical pixels, with edges rounded over <paramref name="bevel"/> pixels. Optionally sets the material.
    /// </summary>
    public void Emboss(GraphicsPath path, float elevation, float bevel, Profile profile = Profile.Round, Mat? mat = null)
    {
        var b = path.GetBounds();
        int x0 = Math.Max(0, (int)Math.Floor(b.X * S) - 2), y0 = Math.Max(0, (int)Math.Floor(b.Y * S) - 2);
        int x1 = Math.Min(W, (int)Math.Ceiling(b.Right * S) + 2), y1 = Math.Min(H, (int)Math.Ceiling(b.Bottom * S) + 2);
        int bw = x1 - x0, bh = y1 - y0;
        if (bw <= 0 || bh <= 0) return;

        var cov = Coverage(path, x0, y0, bw, bh);

        // Chamfer distance transform: distance (in device px) from each inside pixel to the edge.
        const float Big = 1e6f, D = 1.4142f;
        var dist = new float[bw * bh];
        for (int i = 0; i < dist.Length; i++) dist[i] = cov[i] >= 0.5f ? Big : 0;
        for (int y = 0; y < bh; y++)
        {
            for (int x = 0; x < bw; x++)
            {
                int i = y * bw + x;
                if (dist[i] == 0) continue;
                float d = dist[i];
                if (x > 0) d = Math.Min(d, dist[i - 1] + 1);
                if (y > 0)
                {
                    d = Math.Min(d, dist[i - bw] + 1);
                    if (x > 0) d = Math.Min(d, dist[i - bw - 1] + D);
                    if (x < bw - 1) d = Math.Min(d, dist[i - bw + 1] + D);
                }
                if (x == 0 || y == 0 || x == bw - 1 || y == bh - 1) d = Math.Min(d, 1);
                dist[i] = d;
            }
        }
        for (int y = bh - 1; y >= 0; y--)
        {
            for (int x = bw - 1; x >= 0; x--)
            {
                int i = y * bw + x;
                if (dist[i] == 0) continue;
                float d = dist[i];
                if (x < bw - 1) d = Math.Min(d, dist[i + 1] + 1);
                if (y < bh - 1)
                {
                    d = Math.Min(d, dist[i + bw] + 1);
                    if (x < bw - 1) d = Math.Min(d, dist[i + bw + 1] + D);
                    if (x > 0) d = Math.Min(d, dist[i + bw - 1] + D);
                }
                dist[i] = d;
            }
        }

        float bevelPx = Math.Max(0.5f, bevel * S), elevPx = elevation * S;
        for (int y = 0; y < bh; y++)
        {
            int row = (y + y0) * W + x0;
            for (int x = 0; x < bw; x++)
            {
                int i = y * bw + x;
                float c = cov[i];
                if (c <= 0) continue;
                float t = Math.Clamp((dist[i] - 0.5f) / bevelPx, 0, 1);
                float f = profile switch
                {
                    Profile.Linear => t,
                    Profile.Smooth => t * t * (3 - 2 * t),
                    _ => MathF.Sqrt(1 - (1 - t) * (1 - t)),
                };
                Height[row + x] += elevPx * f * Math.Min(1, c);
                if (mat is Mat m && c >= 0.5f) Mats[row + x] = m;
            }
        }
    }

    public void SetMaterial(GraphicsPath path, Mat mat) => Emboss(path, 0, 1, Profile.Linear, mat);

    float[] Coverage(GraphicsPath path, int x0, int y0, int bw, int bh)
    {
        using var bmp = new Bitmap(bw, bh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(-x0, -y0);
            g.ScaleTransform(S, S);
            g.FillPath(Brushes.White, path);
        }
        var data = bmp.LockBits(new Rectangle(0, 0, bw, bh), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new int[bw * bh];
        for (int y = 0; y < bh; y++) Marshal.Copy(data.Scan0 + y * data.Stride, px, y * bw, bw);
        bmp.UnlockBits(data);
        var cov = new float[bw * bh];
        for (int i = 0; i < px.Length; i++) cov[i] = ((px[i] >> 24) & 0xFF) / 255f;
        return cov;
    }

    // ───────────────────────────── lighting ─────────────────────────────

    /// <summary>How a material reacts to light. Metals tint their reflections; everything else reflects white.</summary>
    readonly record struct MatProps(float Spec, float Shine, float Reflect, bool Metal, bool Aniso, float Bump, float Grain);

    static MatProps Props(Mat m) => m switch
    {
        Mat.Brushed => new(0.55f, 45, 0.55f, true, true, 0.22f, 0.02f),
        Mat.Chrome => new(1.3f, 260, 0.92f, true, false, 0.0f, 0.0f),
        Mat.Gloss => new(1.1f, 320, 0.05f, false, false, 0.0f, 0.004f),
        Mat.Rubber => new(0.07f, 8, 0.0f, false, false, 0.14f, 0.03f),
        Mat.Varnish => new(0.7f, 140, 0.045f, false, false, 0.10f, 0.012f),
        Mat.Clearcoat => new(1.1f, 300, 0.05f, false, false, 0.0f, 0.004f),
        Mat.Backlit => new(0.35f, 36, 0.03f, false, false, 0.08f, 0.02f),
        _ => new(0.22f, 22, 0.03f, false, false, 0.10f, 0.03f),
    };

    static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>A soft rectangular light panel in reflection space.</summary>
    static float Panel(float rx, float ry, float cx, float cy, float w, float h, float soft) =>
        Smooth(w + soft, w - soft, Math.Abs(rx - cx)) * Smooth(h + soft, h - soft, Math.Abs(ry - cy));

    /// <summary>
    /// The room the radio reflects (linear light): a big soft light above, a window strip on the
    /// left, a dim car interior in front and a dark floor. This is what makes chrome and gloss read as real.
    /// </summary>
    static float Environment(float rx, float ry)
    {
        float e = 0.035f + 0.05f * Smooth(0.8f, -0.6f, ry);               // dim interior, brighter upward
        e += 3.2f * Panel(rx, ry, -0.12f, -0.62f, 0.62f, 0.2f, 0.12f);     // overhead softbox
        e += 1.4f * Panel(rx, ry, -0.78f, -0.18f, 0.08f, 0.42f, 0.06f);    // window strip, left
        e += 0.5f * Panel(rx, ry, 0.72f, -0.3f, 0.06f, 0.25f, 0.08f);      // small light, right
        e += 0.12f * Smooth(0.25f, 0.9f, ry);                              // floor bounce
        return e;
    }

    static readonly float[] ToLinear = Enumerable.Range(0, 256).Select(i =>
    {
        float c = i / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }).ToArray();

    static byte ToSrgb(float lin)
    {
        // Filmic tone curve (ACES fit), then sRGB.
        float x = lin * 0.9f;
        x = (x * (2.51f * x + 0.03f)) / (x * (2.43f * x + 0.59f) + 0.14f);
        x = Math.Clamp(x, 0, 1);
        float s = x <= 0.0031308f ? x * 12.92f : 1.055f * MathF.Pow(x, 1 / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)(s * 255 + 0.5f), 0, 255);
    }

    static float Hash(int x, int y)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ 0x9E3779B9u;
        h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }

    /// <summary>Lights the scene. At night the room light is almost off (the glow is added afterwards).</summary>
    public Bitmap Bake(bool night = false)
    {
        var alb = new int[W * H];
        var data = Albedo.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < H; y++) Marshal.Copy(data.Scan0 + y * data.Stride, alb, y * W, W);
        Albedo.UnlockBits(data);

        var props = new MatProps[256];
        for (int m = 0; m < 256; m++) props[m] = Props((Mat)m);

        // Micro-surface: brushed streaks, plastic grain and texture relief taken from the paint itself.
        var hm = new float[W * H];
        var rowNoise = new float[H];
        for (int y = 0; y < H; y++) rowNoise[y] = Hash(7, y) - 0.5f;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                var p = props[(int)Mats[i]];
                float bump = 0;
                if (p.Bump > 0)
                {
                    if (p.Aniso)
                        bump = (rowNoise[y] * 0.7f + (Hash(x / 23, y) - 0.5f) * 0.3f) * p.Bump * S;
                    else
                    {
                        int a = alb[i];
                        float lum = (((a >> 16) & 0xFF) * 0.3f + ((a >> 8) & 0xFF) * 0.59f + (a & 0xFF) * 0.11f) / 255f;
                        bump = ((Hash(x / 2, y / 2) - 0.5f) * 0.35f + lum * 0.8f) * p.Bump * S;
                    }
                }
                hm[i] = Height[i] + bump;
            }
        }

        var nearBlur = BoxBlur(Height, W, H, Math.Max(2, (int)(4 * S)));
        var farBlur = BoxBlur(Height, W, H, Math.Max(3, (int)(14 * S)));

        // Key light from the top-left, a cool fill from the bottom-right.
        var (lx, ly, lz) = Norm(-0.42f, -0.62f, 0.66f);
        var (fx, fy, fz) = Norm(0.55f, 0.45f, 0.7f);
        var (hx, hy, hz) = Norm(lx, ly, lz + 1);
        float sdx = lx / MathF.Sqrt(lx * lx + ly * ly), sdy = ly / MathF.Sqrt(lx * lx + ly * ly);
        float riseTan = lz / MathF.Sqrt(lx * lx + ly * ly);

        float keyI = night ? 0.1f : 1.0f, fillI = night ? 0.05f : 0.22f, ambI = night ? 0.05f : 0.2f, envI = night ? 0.09f : 1f;

        var outPx = new int[W * H];
        Parallel.For(0, H, y =>
        {
            float vy = y / (float)H - 0.5f;
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                int a = alb[i];
                var mat = Mats[i];
                if (mat == Mat.Glass)
                {
                    outPx[i] = a;
                    continue;
                }
                var p = props[(int)mat];
                float vx = x / (float)W - 0.5f;

                float gx = (hm[y * W + Math.Min(W - 1, x + 1)] - hm[y * W + Math.Max(0, x - 1)]) * 0.5f;
                float gy = (hm[Math.Min(H - 1, y + 1) * W + x] - hm[Math.Max(0, y - 1) * W + x]) * 0.5f;
                var (nx, ny, nz) = Norm(-gx, -gy, 1);

                // Soft shadow: march toward the light; the penumbra widens with distance.
                float h0 = Height[i], lit = 1;
                for (float t = 1.5f * S; t < 34 * S; t *= 1.45f)
                {
                    int qx = (int)(x + sdx * t), qy = (int)(y + sdy * t);
                    if (qx < 0 || qy < 0 || qx >= W || qy >= H) break;
                    float clearance = h0 + t * riseTan - Height[qy * W + qx];
                    lit = Math.Min(lit, Math.Clamp(0.5f + clearance / (t * 0.45f + 0.5f), 0, 1));
                }

                float ao = 1 - Math.Clamp((nearBlur[i] - h0) / (5 * S), 0, 0.45f) - Math.Clamp((farBlur[i] - h0) / (22 * S), 0, 0.3f);
                ao = Math.Max(0.25f, ao);

                float diffuse = Math.Max(0, nx * lx + ny * ly + nz * lz) * keyI * lit + Math.Max(0, nx * fx + ny * fy + nz * fz) * fillI + ambI;

                // Specular (Blinn), brushed metal only varies across the grain.
                float sx = nx, sy = ny, sz = nz;
                if (p.Aniso) (sx, sy, sz) = Norm(0, ny, nz);
                float nh = Math.Max(0, sx * hx + sy * hy + sz * hz);
                float spec = p.Spec * MathF.Pow(nh, p.Shine) * (p.Shine + 8) / 60f * keyI * lit;

                // Reflection with Fresnel; the view direction shifts across the plate like a real camera.
                float rx = 2 * nz * nx + vx * 0.55f, ry = 2 * nz * ny + vy * 0.55f;
                float fresnel = MathF.Pow(1 - nz, 5);
                float refl = p.Metal ? p.Reflect : p.Reflect + (p.Reflect > 0.02f ? 0.55f : 0.08f) * fresnel;
                float env = Environment(rx, ry) * envI * (0.35f + 0.65f * lit) * ao;

                float ar = ToLinear[(a >> 16) & 0xFF], ag = ToLinear[(a >> 8) & 0xFF], ab = ToLinear[a & 0xFF];
                float kd = p.Metal ? 1 - p.Reflect * 0.85f : 1;
                float r = ar * diffuse * kd * ao, g = ag * diffuse * kd * ao, b = ab * diffuse * kd * ao;
                if (p.Metal)
                {
                    r += env * refl * (0.35f + 0.65f * ar * 1.4f);
                    g += env * refl * (0.35f + 0.65f * ag * 1.4f);
                    b += env * refl * (0.35f + 0.65f * ab * 1.4f);
                    float tint = 0.5f;
                    r += spec * (1 - tint + tint * ar); g += spec * (1 - tint + tint * ag); b += spec * (1 - tint + tint * ab);
                }
                else
                {
                    r += env * refl + spec; g += env * refl + spec; b += env * refl + spec;
                }

                // Grime settles in crevices; a little dust and grain everywhere.
                float crevice = Math.Clamp((nearBlur[i] - h0) / (3 * S), 0, 1);
                float grime = 1 - crevice * 0.3f * (0.6f + 0.4f * Hash(x / 3, y / 3));
                float grain = 1 + (Hash(x, y) - 0.5f) * p.Grain;
                float vignette = 1 - 0.22f * (vx * vx + vy * vy);
                float k = grime * grain * vignette;
                r *= k; g *= k; b *= k;
                if (!night && Hash(x * 3 + 1, y * 7 + 3) > 0.9994f) { r += 0.05f; g += 0.05f; b += 0.05f; }

                outPx[i] = (255 << 24) | (ToSrgb(r) << 16) | (ToSrgb(g) << 8) | ToSrgb(b);
            }
        });

        var result = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        var od = result.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < H; y++) Marshal.Copy(outPx, y * W, od.Scan0 + y * od.Stride, W);
        result.UnlockBits(od);
        return result;
    }

    static (float, float, float) Norm(float x, float y, float z)
    {
        float l = MathF.Sqrt(x * x + y * y + z * z);
        return (x / l, y / l, z / l);
    }

    static float[] BoxBlur(float[] src, int w, int h, int r)
    {
        var tmp = new float[w * h];
        var dst = new float[w * h];
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < h; y++)
        {
            float sum = 0;
            for (int k = -r; k <= r; k++) sum += src[y * w + Math.Clamp(k, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                tmp[y * w + x] = sum * inv;
                sum += src[y * w + Math.Min(w - 1, x + r + 1)] - src[y * w + Math.Max(0, x - r)];
            }
        }
        for (int x = 0; x < w; x++)
        {
            float sum = 0;
            for (int k = -r; k <= r; k++) sum += tmp[Math.Clamp(k, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = sum * inv;
                sum += tmp[Math.Min(h - 1, y + r + 1) * w + x] - tmp[Math.Max(0, y - r) * w + x];
            }
        }
        return dst;
    }
}
