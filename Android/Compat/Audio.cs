// Android stand-ins for the Windows-only parts of NAudio: sound output through the app's AudioTrack, and
// file/stream readers built from NAudio.Core's portable readers, NLayer (MP3) and Android's own decoders.
using NAudio.Wave;
using RetroRadio.Droid;

namespace NAudio.Wave
{
    /// <summary>Plays a sample stream on the phone's speaker or headphones. Decoding runs on its own thread.</summary>
    public sealed class WaveOutEvent : IWavePlayer
    {
        readonly object gate = new();
        ISampleProvider? source;
        IAudioSink? sink;
        Thread? feeder;
        float volume = 1;
        volatile bool stopFeeding, stopRaised;
        volatile PlaybackState state = PlaybackState.Stopped;

        public int DesiredLatency { get; set; } = 150;
        public int NumberOfBuffers { get; set; } = 2;
        public PlaybackState PlaybackState => state;
        public WaveFormat OutputWaveFormat => source?.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public float Volume
        {
            get => volume;
            set
            {
                volume = value;
                if (sink != null) sink.Volume = value;
            }
        }

        public void Init(IWaveProvider provider) => source = provider.ToSampleProvider();

        public void Play()
        {
            if (source == null) return;
            lock (gate)
            {
                if (sink == null)
                {
                    stopFeeding = stopRaised = false;
                    sink = Host.Current.OpenAudio(source.WaveFormat.SampleRate);
                    sink.Volume = volume;
                    var s = sink;
                    feeder = new Thread(() => Feed(s)) { IsBackground = true, Name = "Audio decode", Priority = ThreadPriority.AboveNormal };
                    feeder.Start();
                }
                state = PlaybackState.Playing;
                sink.Play();
            }
        }

        public void Pause()
        {
            lock (gate)
            {
                if (state != PlaybackState.Playing) return;
                state = PlaybackState.Paused;
                sink?.Pause();
            }
        }

        public void Stop()
        {
            if (state == PlaybackState.Stopped && sink == null) return;
            state = PlaybackState.Stopped;
            Close();
            RaiseStopped();
        }

        void Close()
        {
            IAudioSink? s;
            Thread? t;
            lock (gate)
            {
                s = sink;
                t = feeder;
                sink = null;
                feeder = null;
                stopFeeding = true;
            }
            if (t != null && t != Thread.CurrentThread) t.Join(500);
            if (s != null)
            {
                try { s.Pause(); s.Flush(); } catch (Exception) { }
                s.Dispose();
            }
        }

        /// <summary>Keeps the output topped up from the source (the tap for the visualizer runs here).</summary>
        void Feed(IAudioSink s)
        {
            int channels = source!.WaveFormat.Channels;
            var chunk = new float[1024 * channels];
            var stereo = new float[2048];
            long written = 0;
            while (!stopFeeding)
            {
                if (state != PlaybackState.Playing)
                {
                    Thread.Sleep(10);
                    continue;
                }
                int got;
                try { got = source.Read(chunk, 0, chunk.Length); }
                catch (Exception) { got = 0; }
                if (got <= 0)
                {
                    // The end: let the last samples play out, then report it like WinForms does.
                    while (!stopFeeding && s.FramesPlayed < written) Thread.Sleep(10);
                    if (stopFeeding) return;
                    state = PlaybackState.Stopped;
                    Host.Current.Post(() => { Close(); RaiseStopped(); });
                    return;
                }
                int frames = got / channels;
                for (int f = 0; f < frames; f++)
                {
                    float l = chunk[f * channels];
                    stereo[f * 2] = l;
                    stereo[f * 2 + 1] = channels > 1 ? chunk[f * channels + 1] : l;
                }
                int count = frames * 2, done = 0;
                while (done < count && !stopFeeding)
                {
                    int n = s.Write(stereo, done, count - done);
                    done += n;
                    if (n == 0) Thread.Sleep(5);
                }
                written += frames;
            }
        }

        void RaiseStopped()
        {
            if (stopRaised) return;
            stopRaised = true;
            var args = new StoppedEventArgs();
            if (Host.Current.IsUiThread) PlaybackStopped?.Invoke(this, args);
            else Host.Current.Post(() => PlaybackStopped?.Invoke(this, args));
        }

        public void Dispose()
        {
            stopRaised = true; // disposing isn't "the track finished"
            state = PlaybackState.Stopped;
            Close();
        }
    }

    /// <summary>
    /// Opens a music file as float samples. MP3 is decoded in .NET (NLayer), WAV/AIFF are read directly, and
    /// anything else (M4A/AAC/FLAC/OGG/Opus…) goes through Android's own decoders.
    /// </summary>
    public class AudioFileReader : WaveStream, ISampleProvider
    {
        readonly WaveStream source;
        readonly ISampleProvider samples;
        readonly object gate = new();

        public AudioFileReader(string fileName)
        {
            FileName = fileName;
            source = AndroidDecoders.Open(fileName);
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
            if (disposing) source.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>An internet stream (SoundCloud and other http audio), decoded by Android as it downloads.</summary>
    public class MediaFoundationReader : WaveStream
    {
        readonly WaveStream reader;

        public MediaFoundationReader(string url) => reader = Host.Current.OpenDecoder(url);

        public override WaveFormat WaveFormat => reader.WaveFormat;
        public override long Length => reader.Length;
        public override long Position { get => reader.Position; set => reader.Position = value; }
        public override TimeSpan TotalTime => reader.TotalTime;
        public override TimeSpan CurrentTime { get => reader.CurrentTime; set => reader.CurrentTime = value; }
        public override int Read(byte[] buffer, int offset, int count) => reader.Read(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing) reader.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Windows can listen to everything the PC plays; Android apps can't hear other apps,
    /// so this reports "not available" and the radio animates its visualizer by itself during Spotify.
    /// </summary>
    public class WasapiLoopbackCapture : IDisposable
    {
        public WasapiLoopbackCapture() => throw new PlatformNotSupportedException("System audio capture isn't available on Android.");
        public WaveFormat WaveFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;
        public void StartRecording() { }
        public void StopRecording() => RecordingStopped?.Invoke(this, new StoppedEventArgs());
        public void Dispose() { _ = DataAvailable; }
    }

    internal static class AndroidDecoders
    {
        public static WaveStream Open(string file)
        {
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
            catch (Exception)
            {
                // Fall through to Android's decoder.
            }
            return Host.Current.OpenDecoder(file);
        }
    }
}
