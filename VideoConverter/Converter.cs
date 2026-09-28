using System.Diagnostics;
using System.Globalization;
using RetroRadio;

namespace RadioVideoConverter;

enum FitMode { Fill, Fit, Stretch }
enum DitherMode { FloydSteinberg, Ordered, Threshold, Gray4 }

sealed record ConvertOptions(
    int Fps = 15,
    FitMode Fit = FitMode.Fill,
    DitherMode Mode = DitherMode.FloydSteinberg,
    float Brightness = 0f,   // -1..1
    float Contrast = 1f,     // 0.5..2
    bool Invert = false,
    double Start = 0,        // seconds
    double Length = 0,       // seconds, 0 = to the end
    bool Color = false);     // full-color dots instead of one display color

/// <summary>Turns ordinary video into radio display frames, using ffmpeg to decode.</summary>
static class Converter
{
    public const int W = RdvVideo.DefaultWidth, H = RdvVideo.DefaultHeight;

    static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroRadio", "converter.txt");

    // ───────────────────────────── ffmpeg ─────────────────────────────

    public static string? FfmpegPath { get; set; } = FindFfmpeg();

    static string? FindFfmpeg()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var saved = File.ReadAllText(SettingsPath).Trim();
                if (File.Exists(saved)) return saved;
            }
        }
        catch (IOException) { }

        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        dirs.InsertRange(0, [AppContext.BaseDirectory, @"C:\ffmpeg\bin", @"C:\msys64\ucrt64\bin", @"C:\Program Files\ffmpeg\bin"]);
        foreach (var d in dirs)
        {
            try
            {
                var p = Path.Combine(d.Trim('"'), "ffmpeg.exe");
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public static void RememberFfmpeg(string path)
    {
        FfmpegPath = path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, path);
        }
        catch (IOException) { }
    }

    static string Ffmpeg => FfmpegPath ?? throw new InvalidOperationException("ffmpeg.exe was not found.");

    static string ScaleFilter(FitMode fit) => fit switch
    {
        FitMode.Fit => $"scale={W}:{H}:force_original_aspect_ratio=decrease:flags=area,pad={W}:{H}:(ow-iw)/2:(oh-ih)/2:black",
        FitMode.Stretch => $"scale={W}:{H}:flags=area",
        _ => $"scale={W}:{H}:force_original_aspect_ratio=increase:flags=area,crop={W}:{H}",
    };

    static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    static Process StartFfmpeg(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(Ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start ffmpeg.");
        return p;
    }

    /// <summary>Video length in seconds, read from ffmpeg's banner.</summary>
    public static double? GetDuration(string input)
    {
        using var p = StartFfmpeg(["-hide_banner", "-i", input]);
        p.StandardOutput.ReadToEnd();
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        int i = err.IndexOf("Duration: ", StringComparison.Ordinal);
        if (i < 0) return null;
        var part = err.Substring(i + 10, 11); // hh:mm:ss.cc
        return TimeSpan.TryParse(part, CultureInfo.InvariantCulture, out var ts) ? ts.TotalSeconds : null;
    }

    static string PixFmt(bool color) => color ? "rgb24" : "gray";

    /// <summary>One frame at the given time (W x H gray bytes, or W x H x 3 RGB bytes for color), or null.</summary>
    public static byte[]? GrabFrame(string input, double seconds, FitMode fit, bool color = false)
    {
        using var p = StartFfmpeg(["-v", "error", "-ss", Num(seconds), "-i", input, "-frames:v", "1",
            "-vf", ScaleFilter(fit) + ",format=" + PixFmt(color), "-f", "rawvideo", "-pix_fmt", PixFmt(color), "-"]);
        p.StandardError.ReadToEndAsync();
        var buf = new byte[W * H * (color ? 3 : 1)];
        int got = ReadFully(p.StandardOutput.BaseStream, buf);
        p.WaitForExit();
        return got == buf.Length ? buf : null;
    }

    static int ReadFully(Stream s, byte[] buf)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int n = s.Read(buf, total, buf.Length - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    /// <summary>Converts a video into an .rdv file. Returns the number of frames written.</summary>
    public static int Convert(string input, string output, ConvertOptions o, IProgress<double>? progress, CancellationToken ct)
    {
        double? duration = GetDuration(input);
        double span = o.Length > 0 ? o.Length : Math.Max(0, (duration ?? 0) - o.Start);
        int expected = Math.Max(1, (int)(span * o.Fps));

        var args = new List<string> { "-v", "error" };
        if (o.Start > 0) args.AddRange(["-ss", Num(o.Start)]);
        args.AddRange(["-i", input]);
        if (o.Length > 0) args.AddRange(["-t", Num(o.Length)]);
        args.AddRange(["-an", "-vf", $"fps={o.Fps}," + ScaleFilter(o.Fit) + ",format=" + PixFmt(o.Color), "-f", "rawvideo", "-pix_fmt", PixFmt(o.Color), "-"]);

        using var p = StartFfmpeg(args);
        var errTask = p.StandardError.ReadToEndAsync();
        string tmp = output + ".part";
        int count;
        try
        {
            using (var writer = new RdvVideo.Writer(tmp, W, H, Dither.BitsFor(o), o.Fps))
            {
                var buf = new byte[W * H * (o.Color ? 3 : 1)];
                while (ReadFully(p.StandardOutput.BaseStream, buf) == buf.Length)
                {
                    if (ct.IsCancellationRequested)
                    {
                        p.Kill();
                        throw new OperationCanceledException(ct);
                    }
                    writer.Add(Dither.Apply(buf, W, H, o));
                    progress?.Report(Math.Min(1, writer.Count / (double)expected));
                }
                count = writer.Count;
            }
            p.WaitForExit();
            if (count == 0) throw new InvalidOperationException("ffmpeg produced no frames: " + errTask.Result.Trim());
            File.Move(tmp, output, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { }
            throw;
        }
        progress?.Report(1);
        return count;
    }

    public static int RunCommandLine(string[] args)
    {
        var o = new ConvertOptions();
        for (int i = 2; i + 1 < args.Length; i += 2)
        {
            string v = args[i + 1].ToLowerInvariant();
            o = args[i] switch
            {
                "--fps" => o with { Fps = int.Parse(v) },
                "--fit" => o with { Fit = v switch { "fit" => FitMode.Fit, "stretch" => FitMode.Stretch, _ => FitMode.Fill } },
                "--mode" => o with { Mode = v switch { "ordered" => DitherMode.Ordered, "threshold" => DitherMode.Threshold, "gray4" => DitherMode.Gray4, _ => DitherMode.FloydSteinberg } },
                "--length" => o with { Length = double.Parse(v, CultureInfo.InvariantCulture) },
                "--start" => o with { Start = double.Parse(v, CultureInfo.InvariantCulture) },
                "--color" => o with { Color = v is "on" or "1" or "true" or "yes" },
                _ => o,
            };
        }
        try
        {
            Convert(args[0], args[1], o, null, CancellationToken.None);
            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}

/// <summary>Turns video frames into the radio's dots: 1-bit or 2-bit brightness, or RGB 3-3-2 color.</summary>
static class Dither
{
    static readonly int[,] Bayer4 = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };

    public static int BitsFor(ConvertOptions o) => o.Color ? RdvVideo.ColorBits : o.Mode == DitherMode.Gray4 ? 2 : 1;

    static float Tone(byte b, ConvertOptions o)
    {
        float v = b / 255f;
        v = (v - 0.5f) * o.Contrast + 0.5f + o.Brightness;
        if (o.Invert) v = 1 - v;
        return Math.Clamp(v, 0, 1);
    }

    /// <summary>
    /// A color frame (RGB bytes) to one RGB 3-3-2 byte per dot. Each channel is dithered to its few levels
    /// (8 red, 8 green, 4 blue) with the chosen style, which keeps gradients smooth with only 256 colors.
    /// </summary>
    public static byte[] ApplyColor(byte[] rgb, int w, int h, ConvertOptions o)
    {
        int[] maxes = [7, 7, 3];
        var ch = new float[3][];
        for (int c = 0; c < 3; c++)
        {
            ch[c] = new float[w * h];
            for (int i = 0; i < w * h; i++) ch[c][i] = Tone(rgb[i * 3 + c], o);
        }
        var q = new int[3][];
        for (int c = 0; c < 3; c++)
        {
            int max = maxes[c];
            var f = ch[c];
            var r = q[c] = new int[w * h];
            switch (o.Mode)
            {
                case DitherMode.Threshold:
                    for (int i = 0; i < f.Length; i++) r[i] = Math.Clamp((int)MathF.Round(f[i] * max), 0, max);
                    break;
                case DitherMode.Ordered:
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            float t = (Bayer4[y & 3, x & 3] + 0.5f) / 16f;
                            r[y * w + x] = Math.Clamp((int)(f[y * w + x] * max + t), 0, max);
                        }
                    break;
                default:
                    ErrorDiffuse(f, r, w, h, max);
                    break;
            }
        }
        var result = new byte[w * h];
        for (int i = 0; i < result.Length; i++) result[i] = (byte)(q[0][i] << 5 | q[1][i] << 2 | q[2][i]);
        return result;
    }

    /// <summary>Serpentine Floyd–Steinberg to max + 1 levels.</summary>
    static void ErrorDiffuse(float[] f, int[] result, int w, int h, int max)
    {
        for (int y = 0; y < h; y++)
        {
            bool ltr = (y & 1) == 0;
            int dir = ltr ? 1 : -1;
            for (int k = 0; k < w; k++)
            {
                int x = ltr ? k : w - 1 - k;
                int i = y * w + x;
                float old = f[i];
                int q = Math.Clamp((int)MathF.Round(old * max), 0, max);
                result[i] = q;
                float err = old - q / (float)max;
                if (x + dir >= 0 && x + dir < w) f[i + dir] += err * 7 / 16f;
                if (y + 1 < h)
                {
                    if (x - dir >= 0 && x - dir < w) f[i + w - dir] += err * 3 / 16f;
                    f[i + w] += err * 5 / 16f;
                    if (x + dir >= 0 && x + dir < w) f[i + w + dir] += err * 1 / 16f;
                }
            }
        }
    }

    public static byte[] Apply(byte[] frame, int w, int h, ConvertOptions o)
    {
        if (o.Color) return ApplyColor(frame, w, h, o);
        byte[] gray = frame;
        int max = (1 << BitsFor(o)) - 1;
        var f = new float[w * h];
        for (int i = 0; i < f.Length; i++)
        {
            float v = gray[i] / 255f;
            v = (v - 0.5f) * o.Contrast + 0.5f + o.Brightness;
            if (o.Invert) v = 1 - v;
            f[i] = Math.Clamp(v, 0, 1);
        }

        var result = new byte[w * h];
        switch (o.Mode)
        {
            case DitherMode.Threshold:
                for (int i = 0; i < f.Length; i++) result[i] = (byte)(f[i] >= 0.5f ? 1 : 0);
                break;

            case DitherMode.Ordered:
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        result[y * w + x] = (byte)(f[y * w + x] > (Bayer4[y & 3, x & 3] + 0.5f) / 16f ? 1 : 0);
                break;

            default:
                // Serpentine Floyd–Steinberg error diffusion to 2 or 4 levels.
                for (int y = 0; y < h; y++)
                {
                    bool ltr = (y & 1) == 0;
                    int dir = ltr ? 1 : -1;
                    for (int k = 0; k < w; k++)
                    {
                        int x = ltr ? k : w - 1 - k;
                        int i = y * w + x;
                        float old = f[i];
                        int q = Math.Clamp((int)MathF.Round(old * max), 0, max);
                        result[i] = (byte)q;
                        float err = old - q / (float)max;
                        void Spread(int xx, int yy, float amount)
                        {
                            if (xx >= 0 && xx < w && yy < h) f[yy * w + xx] += err * amount;
                        }
                        Spread(x + dir, y, 7 / 16f);
                        Spread(x - dir, y + 1, 3 / 16f);
                        Spread(x, y + 1, 5 / 16f);
                        Spread(x + dir, y + 1, 1 / 16f);
                    }
                }
                break;
        }
        return result;
    }
}
