// macOS stand-ins for the Windows-only parts of NAudio: audio output through PortAudio (Core Audio on a Mac),
// and file/stream readers built from NAudio.Core's portable readers, NLayer (MP3) and macOS's own decoder.
using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using PortAudioSharp;

namespace NAudio.Wave
{
    /// <summary>Plays a sample stream on the default output device. Decoding runs on its own thread, ahead of the sound card.</summary>
    public sealed class WaveOutEvent : IWavePlayer
    {
        static bool paReady;
        static readonly object paLock = new();

        readonly SynchronizationContext? sync = SynchronizationContext.Current;
        readonly object ringLock = new();
        ISampleProvider? source;
        PortAudioSharp.Stream? stream;
        PortAudioSharp.Stream.Callback? callback; // kept alive while native code holds it
        Thread? feeder;
        float[] ring = [];
        int readPos, writePos, filled;
        int srcChannels = 2;
        volatile bool ended, disposed, stopRaised, finishQueued;
        volatile PlaybackState state = PlaybackState.Stopped;

        public int DesiredLatency { get; set; } = 150;
        public int NumberOfBuffers { get; set; } = 2;
        public float Volume { get; set; } = 1;
        public PlaybackState PlaybackState => state;
        public WaveFormat OutputWaveFormat => source?.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        static void EnsurePortAudio()
        {
            lock (paLock)
            {
                if (paReady) return;
                try { PortAudio.LoadNativeLibrary(); } catch (Exception) { }
                PortAudio.Initialize();
                paReady = true;
            }
        }

        public void Init(IWaveProvider provider)
        {
            source = provider.ToSampleProvider();
            srcChannels = source.WaveFormat.Channels;
            int rate = source.WaveFormat.SampleRate;
            ring = new float[Math.Max(4096, rate * 2 * Math.Max(60, DesiredLatency) / 1000)];
        }

        public void Play()
        {
            if (source == null || disposed) return;
            EnsurePortAudio();
            if (stream == null)
            {
                lock (ringLock) readPos = writePos = filled = 0;
                ended = stopRaised = finishQueued = false;
                Open();
            }
            state = PlaybackState.Playing;
        }

        public void Pause()
        {
            if (state == PlaybackState.Playing) state = PlaybackState.Paused;
        }

        public void Stop()
        {
            if (state == PlaybackState.Stopped && stream == null) return;
            state = PlaybackState.Stopped;
            Close();
            RaiseStopped();
        }

        void Open()
        {
            var fmt = source!.WaveFormat;
            int device = PortAudio.DefaultOutputDevice;
            if (device == PortAudio.NoDevice) throw new InvalidOperationException("No sound output device.");
            var info = PortAudio.GetDeviceInfo(device);
            var p = new StreamParameters
            {
                device = device,
                channelCount = 2,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = Math.Max(info.defaultLowOutputLatency, 0.02),
                hostApiSpecificStreamInfo = IntPtr.Zero,
            };
            callback = OnAudio;
            stream = new PortAudioSharp.Stream(null, p, fmt.SampleRate, 0, StreamFlags.ClipOff, callback, null);
            feeder = new Thread(Feed) { IsBackground = true, Name = "Audio decode", Priority = ThreadPriority.AboveNormal };
            feeder.Start();
            stream.Start();
        }

        void Close()
        {
            var s = stream;
            stream = null;
            if (s != null)
            {
                try { s.Stop(); } catch (Exception) { }
                try { s.Dispose(); } catch (Exception) { }
            }
        }

        /// <summary>Keeps the ring buffer topped up from the source (the tap for the visualizer runs here).</summary>
        void Feed()
        {
            var chunk = new float[2048];
            while (!disposed && stream != null)
            {
                if (state != PlaybackState.Playing || ended)
                {
                    Thread.Sleep(5);
                    continue;
                }
                int space;
                lock (ringLock) space = ring.Length - filled;
                int want = Math.Min(chunk.Length, space) / srcChannels * srcChannels;
                if (want < srcChannels * 64)
                {
                    Thread.Sleep(4);
                    continue;
                }
                int got;
                try { got = source!.Read(chunk, 0, want); }
                catch (Exception) { got = 0; }
                if (got <= 0)
                {
                    ended = true;
                    continue;
                }
                lock (ringLock)
                {
                    for (int i = 0; i < got; i++)
                    {
                        ring[writePos] = chunk[i];
                        writePos = (writePos + 1) % ring.Length;
                    }
                    filled += got;
                }
            }
        }

        unsafe StreamCallbackResult OnAudio(IntPtr input, IntPtr output, uint frames, ref StreamCallbackTimeInfo time, StreamCallbackFlags flags, IntPtr user)
        {
            var outp = (float*)output;
            int n = (int)frames;
            float vol = Volume;
            if (state != PlaybackState.Playing)
            {
                for (int i = 0; i < n * 2; i++) outp[i] = 0;
                return StreamCallbackResult.Continue;
            }
            lock (ringLock)
            {
                for (int f = 0; f < n; f++)
                {
                    if (filled >= srcChannels)
                    {
                        float l = ring[readPos];
                        float r = srcChannels > 1 ? ring[(readPos + 1) % ring.Length] : l;
                        readPos = (readPos + srcChannels) % ring.Length;
                        filled -= srcChannels;
                        outp[f * 2] = l * vol;
                        outp[f * 2 + 1] = r * vol;
                    }
                    else
                    {
                        outp[f * 2] = outp[f * 2 + 1] = 0;
                    }
                }
                if (ended && filled < srcChannels && !finishQueued)
                {
                    finishQueued = true;
                    state = PlaybackState.Stopped;
                    ThreadPool.QueueUserWorkItem(_ => { Close(); RaiseStopped(); });
                }
            }
            return StreamCallbackResult.Continue;
        }

        void RaiseStopped()
        {
            if (stopRaised) return;
            stopRaised = true;
            var args = new StoppedEventArgs();
            if (sync != null) sync.Post(_ => PlaybackStopped?.Invoke(this, args), null);
            else PlaybackStopped?.Invoke(this, args);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stopRaised = true; // disposing isn't "the track finished"
            state = PlaybackState.Stopped;
            Close();
        }
    }

    /// <summary>
    /// Opens a music file as float samples. MP3 is decoded in .NET (NLayer), WAV/AIFF are read directly, and
    /// anything else (M4A/AAC/ALAC/FLAC/…) is decoded once to a temporary WAV by macOS's built-in afconvert
    /// (or by ffmpeg when it's installed).
    /// </summary>
    public class AudioFileReader : WaveStream, ISampleProvider
    {
        readonly WaveStream source;
        readonly ISampleProvider samples;
        readonly object gate = new();
        readonly string? tempFile;

        public AudioFileReader(string fileName)
        {
            FileName = fileName;
            source = MacDecoders.Open(fileName, out tempFile);
            samples = source.ToSampleProvider();
            WaveFormat = samples.WaveFormat;
        }

        public string FileName { get; }
        public override WaveFormat WaveFormat { get; }
        public float Volume { get; set; } = 1;

        long SourceToOut(long p) => p / source.WaveFormat.BlockAlign * WaveFormat.BlockAlign;
        long OutToSource(long p) => p / WaveFormat.BlockAlign * source.WaveFormat.BlockAlign;

        public override long Length => SourceToOut(source.Length);

        public override long Position
        {
            get { lock (gate) return SourceToOut(source.Position); }
            set { lock (gate) source.Position = OutToSource(value); }
        }

        public override TimeSpan TotalTime => source.TotalTime;

        public override TimeSpan CurrentTime
        {
            get { lock (gate) return source.CurrentTime; }
            set { lock (gate) source.CurrentTime = value < TimeSpan.Zero ? TimeSpan.Zero : value; }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (gate)
            {
                int n = samples.Read(buffer, offset, count);
                if (Volume != 1)
                    for (int i = 0; i < n; i++) buffer[offset + i] *= Volume;
                return n;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var wb = new WaveBuffer(buffer);
            int floats = Read(wb.FloatBuffer, offset / 4, count / 4);
            return floats * 4;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                source.Dispose();
                if (tempFile != null) try { File.Delete(tempFile); } catch (IOException) { }
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>An internet stream: downloaded first (Windows streams it through Media Foundation), then decoded like a file.</summary>
    public class MediaFoundationReader : WaveStream
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
        readonly AudioFileReader reader;
        readonly string download;

        public MediaFoundationReader(string url)
        {
            using var res = Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            res.EnsureSuccessStatusCode();
            string type = res.Content.Headers.ContentType?.MediaType ?? "";
            string ext = type.Contains("mpeg") || type.Contains("mp3") ? ".mp3"
                : type.Contains("mp4") || type.Contains("aac") || type.Contains("m4a") ? ".m4a"
                : type.Contains("ogg") ? ".ogg"
                : type.Contains("wav") ? ".wav"
                : Path.GetExtension(new Uri(url).AbsolutePath) is { Length: > 1 } e ? e : ".mp3";
            download = MacDecoders.TempPath(ext);
            using (var fs = File.Create(download))
                res.Content.ReadAsStream().CopyTo(fs);
            reader = new AudioFileReader(download);
        }

        public override WaveFormat WaveFormat => reader.WaveFormat;
        public override long Length => reader.Length;
        public override long Position { get => reader.Position; set => reader.Position = value; }
        public override TimeSpan TotalTime => reader.TotalTime;
        public override TimeSpan CurrentTime { get => reader.CurrentTime; set => reader.CurrentTime = value; }
        public override int Read(byte[] buffer, int offset, int count) => reader.Read(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                reader.Dispose();
                try { File.Delete(download); } catch (IOException) { }
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Windows can listen to everything the PC plays; macOS can't without extra drivers,
    /// so this reports "not available" and the radio animates its visualizer by itself during Spotify.
    /// </summary>
    public class WasapiLoopbackCapture : IDisposable
    {
        public WasapiLoopbackCapture() => throw new PlatformNotSupportedException("System audio capture isn't available on macOS.");
        public WaveFormat WaveFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;
        public void StartRecording() { }
        public void StopRecording() => RecordingStopped?.Invoke(this, new StoppedEventArgs());
        public void Dispose() { _ = DataAvailable; }
    }

    internal static class MacDecoders
    {
        public static string TempPath(string ext)
        {
            string dir = Path.Combine(Path.GetTempPath(), "RetroRadio");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, Guid.NewGuid().ToString("N") + ext);
        }

        public static WaveStream Open(string file, out string? temp)
        {
            temp = null;
            string ext = Path.GetExtension(file).ToLowerInvariant();
            try
            {
                switch (ext)
                {
                    case ".mp3":
                    case ".mp2":
                        return new Mp3FileReaderBase(file, wf => new NLayer.NAudioSupport.Mp3FrameDecompressor(wf));
                    case ".wav":
                    {
                        var w = new WaveFileReader(file);
                        if (w.WaveFormat.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible) return w;
                        w.Dispose();
                        break;
                    }
                    case ".aif":
                    case ".aiff":
                        return new AiffFileReader(file);
                }
            }
            catch (Exception) when (ext != ".mp3")
            {
                // Fall through to the system decoder.
            }

            temp = TempPath(".wav");
            if (Decode(file, temp)) return new WaveFileReader(temp);
            try { File.Delete(temp); } catch (IOException) { }
            temp = null;
            throw new InvalidDataException("This file type needs ffmpeg (brew install ffmpeg).");
        }

        /// <summary>Decodes to a float WAV with afconvert (built into macOS), or ffmpeg if that fails.</summary>
        static bool Decode(string input, string output)
        {
            if (OperatingSystem.IsMacOS() && Run("/usr/bin/afconvert", ["-f", "WAVE", "-d", "LEF32", input, output])) return true;
            string? ffmpeg = FindFfmpeg();
            return ffmpeg != null && Run(ffmpeg, ["-v", "quiet", "-y", "-i", input, "-vn", "-acodec", "pcm_f32le", "-f", "wav", output]);
        }

        static string? FindFfmpeg()
        {
            string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            string[] dirs = ["/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin", .. (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)];
            return dirs.Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists);
        }

        static bool Run(string exe, string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.StandardError.ReadToEndAsync();
                p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(120_000))
                {
                    p.Kill();
                    return false;
                }
                return p.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
