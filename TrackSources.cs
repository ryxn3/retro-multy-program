using System.Diagnostics;
using NAudio.Wave;

namespace RetroRadio;

/// <summary>Something the engine can play: a local file or an internet stream.</summary>
interface ITrackSource : IDisposable
{
    ISampleProvider Samples { get; }
    TimeSpan CurrentTime { get; set; }
    TimeSpan TotalTime { get; }

    /// <summary>A live stream (internet radio): no length, no seeking.</summary>
    bool IsLive => false;
}

sealed class FileTrackSource(string path) : ITrackSource
{
    readonly AudioFileReader reader = new(path);

    public ISampleProvider Samples => reader;
    public TimeSpan TotalTime => reader.TotalTime;

    public TimeSpan CurrentTime
    {
        get => reader.CurrentTime;
        set => reader.CurrentTime = value;
    }

    public void Dispose() => reader.Dispose();
}

/// <summary>A progressive (plain MP3) internet stream, read through Media Foundation.</summary>
sealed class UrlTrackSource : ITrackSource
{
    readonly MediaFoundationReader reader;
    readonly TimeSpan knownLength;

    public UrlTrackSource(string url, TimeSpan knownLength)
    {
        reader = new MediaFoundationReader(url);
        Samples = reader.ToSampleProvider();
        this.knownLength = knownLength;
    }

    public ISampleProvider Samples { get; }
    public TimeSpan TotalTime => reader.TotalTime > TimeSpan.Zero ? reader.TotalTime : knownLength;

    public TimeSpan CurrentTime
    {
        get => reader.CurrentTime;
        set => reader.CurrentTime = value;
    }

    public void Dispose() => reader.Dispose();
}

/// <summary>
/// Streams anything ffmpeg can open (e.g. HLS playlists) as raw float PCM through a pipe.
/// Seeking restarts ffmpeg at the new position.
/// </summary>
sealed class FfmpegTrackSource : ITrackSource, ISampleProvider
{
    const int Rate = 44100, Channels = 2;
    readonly string ffmpeg, url;
    readonly object gate = new();
    Process? proc;
    Stream? pipe;
    TimeSpan offset;
    long framesRead;
    byte[] bytes = [];

    public FfmpegTrackSource(string ffmpegPath, string url, TimeSpan totalTime)
    {
        ffmpeg = ffmpegPath;
        this.url = url;
        TotalTime = totalTime;
        Start(TimeSpan.Zero);
    }

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, Channels);
    public ISampleProvider Samples => this;
    public TimeSpan TotalTime { get; }

    public TimeSpan CurrentTime
    {
        get => offset + TimeSpan.FromSeconds(framesRead / (double)Rate);
        set
        {
            lock (gate) Start(value);
        }
    }

    void Start(TimeSpan at)
    {
        Stop();
        var psi = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-v", "error", "-ss", at.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                     "-i", url, "-vn", "-f", "f32le", "-ac", "2", "-ar", "44100", "-" })
            psi.ArgumentList.Add(a);
        proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start ffmpeg.");
        proc.ErrorDataReceived += (_, _) => { };
        proc.BeginErrorReadLine();
        pipe = proc.StandardOutput.BaseStream;
        offset = at;
        framesRead = 0;
    }

    void Stop()
    {
        try
        {
            if (proc is { HasExited: false }) proc.Kill();
        }
        catch (InvalidOperationException) { }
        proc?.Dispose();
        proc = null;
        pipe = null;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (gate)
        {
            if (pipe == null) return 0;
            int need = count * 4;
            if (bytes.Length < need) bytes = new byte[need];
            int got = 0;
            try
            {
                while (got < need)
                {
                    int n = pipe.Read(bytes, got, need - got);
                    if (n <= 0) break;
                    got += n;
                }
            }
            catch (IOException)
            {
                // Pipe closed (seek or dispose) — treat as end of what we have.
            }
            int samples = got / 4 / Channels * Channels;
            Buffer.BlockCopy(bytes, 0, buffer, offset * 4, samples * 4);
            framesRead += samples / Channels;
            return samples;
        }
    }

    public void Dispose()
    {
        lock (gate) Stop();
    }
}

/// <summary>Finds ffmpeg the same way the video converter does (on a Mac, where Homebrew and MacPorts put it).</summary>
static class FfmpegLocator
{
    public static string? Find()
    {
        var saved = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroRadio", "converter.txt");
        try
        {
            if (File.Exists(saved))
            {
                var p = File.ReadAllText(saved).Trim();
                if (File.Exists(p)) return p;
            }
        }
        catch (IOException) { }

        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (OperatingSystem.IsWindows())
            dirs.InsertRange(0, [AppContext.BaseDirectory, @"C:\ffmpeg\bin", @"C:\msys64\ucrt64\bin", @"C:\Program Files\ffmpeg\bin"]);
        else
            dirs.InsertRange(0, [AppContext.BaseDirectory, "/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin"]);
        string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (var d in dirs)
        {
            try
            {
                var p = Path.Combine(d.Trim('"'), exe);
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return null;
    }
}
