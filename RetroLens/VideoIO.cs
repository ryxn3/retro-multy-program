using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace RetroLens;

/// <summary>Reads and writes video through ffmpeg (which must be installed; see ffmpeg.org).</summary>
static class VideoIO
{
    static readonly string[] VideoExt = [".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".m4v", ".flv", ".mpg", ".mpeg", ".3gp", ".mts"];
    public static bool IsVideo(string path) => VideoExt.Contains(Path.GetExtension(path).ToLowerInvariant());

    static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroRadio", "converter.txt");

    public static string? Ffmpeg { get; private set; } = Find();

    static string? Find()
    {
        try
        {
            if (File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Trim() is { } saved && File.Exists(saved)) return saved;
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

    public static void Remember(string path)
    {
        Ffmpeg = path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, path);
        }
        catch (IOException) { }
    }

    static Process Start(IEnumerable<string> args, bool stdin = false)
    {
        var psi = new ProcessStartInfo(Ffmpeg ?? throw new InvalidOperationException("ffmpeg.exe wasn't found."))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start ffmpeg.");
    }

    static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public sealed record Info(int Width, int Height, double Fps, double Duration, bool HasAudio);

    /// <summary>Size, frame rate, length and whether there's sound, read from ffmpeg's banner.</summary>
    public static Info Probe(string path)
    {
        using var p = Start(["-hide_banner", "-i", path]);
        p.StandardOutput.ReadToEnd();
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        double dur = 0, fps = 30;
        int w = 0, h = 0;
        int di = err.IndexOf("Duration: ", StringComparison.Ordinal);
        if (di >= 0 && TimeSpan.TryParse(err.Substring(di + 10, 11), CultureInfo.InvariantCulture, out var ts)) dur = ts.TotalSeconds;
        var video = err.Split('\n').FirstOrDefault(line => line.Contains("Video:"));
        if (video != null)
        {
            var m = System.Text.RegularExpressions.Regex.Match(video, @"(\d{2,5})x(\d{2,5})");
            if (m.Success) { w = int.Parse(m.Groups[1].Value); h = int.Parse(m.Groups[2].Value); }
            var f = System.Text.RegularExpressions.Regex.Match(video, @"([\d.]+) fps");
            if (f.Success) double.TryParse(f.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out fps);
        }
        return new Info(w, h, fps > 0 ? fps : 30, dur, err.Contains("Audio:"));
    }

    /// <summary>Decodes any picture ffmpeg understands (webp, heic, …) or one video frame at <paramref name="seconds"/>.</summary>
    public static Bitmap? GrabFrame(string path, double seconds, int maxWidth)
    {
        using var p = Start(["-v", "error", "-ss", Num(seconds), "-i", path, "-frames:v", "1",
            "-vf", $"scale='min({maxWidth},iw)':-2", "-f", "image2pipe", "-vcodec", "png", "-"]);
        p.StandardError.ReadToEndAsync();
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit();
        if (ms.Length == 0) return null;
        ms.Position = 0;
        using var img = new Bitmap(ms);
        return new Bitmap(img);
    }

    static string AudioFilter(AudioLook a) => a switch
    {
        AudioLook.CamcorderMic => "highpass=f=180,lowpass=f=7000,acompressor=threshold=0.1:ratio=4,volume=1.3",
        AudioLook.TinySpeaker => "highpass=f=600,lowpass=f=3500,acrusher=bits=8:mode=log:aa=1:mix=0.35,volume=1.4",
        AudioLook.OldRadio => "highpass=f=300,lowpass=f=3000,volume=1.2",
        _ => "",
    };

    /// <summary>
    /// Runs every frame of a video through the look and writes the result (MP4 or GIF by extension).
    /// </summary>
    public static async Task ExportAsync(string input, string output, Look look, IProgress<double> progress, CancellationToken ct)
    {
        var info = Probe(input);
        double fps = look.FrameRate > 0 ? look.FrameRate : info.Fps;
        int decodeW = Math.Min(info.Width > 0 ? info.Width : 1280, 1280);
        decodeW -= decodeW % 2;
        int decodeH = info.Width > 0 ? (int)Math.Round(info.Height * (decodeW / (double)info.Width)) : 720;
        decodeH -= decodeH % 2;
        int total = Math.Max(1, (int)(info.Duration * fps));

        using var dec = Start(["-v", "error", "-i", input, "-an", "-vf", $"fps={Num(fps)},scale={decodeW}:{decodeH}", "-f", "rawvideo", "-pix_fmt", "bgra", "-"]);
        _ = dec.StandardError.ReadToEndAsync(ct);

        // The first frame fixes the output size.
        int frameBytes = decodeW * decodeH * 4;
        var buf = new byte[frameBytes];
        Process? enc = null;
        Stream? encIn = null;
        Task<string>? encErr = null;
        int outW = 0, outH = 0, n = 0;
        try
        {
            while (await ReadFullyAsync(dec.StandardOutput.BaseStream, buf, ct) == frameBytes)
            {
                ct.ThrowIfCancellationRequested();
                using var src = new Bitmap(decodeW, decodeH, PixelFormat.Format32bppArgb);
                var d = src.LockBits(new Rectangle(0, 0, decodeW, decodeH), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(buf, 0, d.Scan0, frameBytes);
                src.UnlockBits(d);
                // Opaque alpha (rawvideo bgra from ffmpeg may leave it 0).
                using var processed = Processor.Apply(src, look, n + 1, n / fps);

                if (enc == null)
                {
                    outW = processed.Width - processed.Width % 2;
                    outH = processed.Height - processed.Height % 2;
                    enc = StartEncoder(input, output, look, info, fps, outW, outH);
                    encIn = enc.StandardInput.BaseStream;
                    encErr = enc.StandardError.ReadToEndAsync();
                }
                await encIn!.WriteAsync(ToBgr(processed, outW, outH), ct);
                n++;
                progress.Report(Math.Min(1, n / (double)total));
            }
            if (enc == null) throw new InvalidOperationException("ffmpeg couldn't read any frames from that video.");
            encIn!.Close();
            await enc.WaitForExitAsync(ct);
            if (enc.ExitCode != 0) throw new InvalidOperationException("ffmpeg couldn't write the video: " + (await encErr!).Trim().Split('\n').LastOrDefault());
        }
        catch
        {
            try { if (!dec.HasExited) dec.Kill(); } catch (InvalidOperationException) { }
            try { if (enc is { HasExited: false }) enc.Kill(); } catch (InvalidOperationException) { }
            throw;
        }
        finally
        {
            enc?.Dispose();
        }
    }

    static Process StartEncoder(string input, string output, Look look, Info info, double fps, int w, int h)
    {
        var args = new List<string> { "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", $"{w}x{h}", "-r", Num(fps), "-i", "-" };
        bool gif = output.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
        if (gif)
        {
            args.AddRange(["-vf", "split[a][b];[a]palettegen=max_colors=192[p];[b][p]paletteuse=dither=bayer:bayer_scale=3", "-loop", "0", output]);
            return Start(args, stdin: true);
        }
        bool audio = info.HasAudio && look.Audio != AudioLook.Mute;
        if (audio) args.AddRange(["-i", input, "-map", "0:v", "-map", "1:a?"]);
        args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18", "-preset", "medium"]);
        if (audio)
        {
            var af = AudioFilter(look.Audio);
            if (af.Length > 0) args.AddRange(["-af", af]);
            args.AddRange(["-c:a", "aac", "-b:a", "160k", "-shortest"]);
        }
        else args.Add("-an");
        args.AddRange(["-movflags", "+faststart", output]);
        return Start(args, stdin: true);
    }

    static byte[] ToBgr(Bitmap bmp, int w, int h)
    {
        var outBuf = new byte[w * h * 3];
        var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        for (int y = 0; y < h; y++) Marshal.Copy(d.Scan0 + y * d.Stride, outBuf, y * w * 3, w * 3);
        bmp.UnlockBits(d);
        return outBuf;
    }

    static async Task<int> ReadFullyAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(total), ct);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }
}
