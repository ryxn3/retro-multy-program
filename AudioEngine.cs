using System.Diagnostics;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RetroRadio;

/// <summary>
/// The radio's player. One sound output stays open; each track plays on a "deck", and decks are mixed so
/// songs can crossfade. The mix runs through the tone controls and effects, feeds the visualizer and the
/// BPM counter, then the volume. A ring buffer of recent samples serves the visualizer.
/// </summary>
sealed class AudioEngine : IDisposable, IPlayback
{
    const int FftPow = 11;
    const int FftSize = 1 << FftPow;
    public const int Rate = 44100;

    readonly float[] ring = new float[FftSize];
    readonly object ringLock = new();
    int ringPos;
    readonly Complex[] fft = new Complex[FftSize];
    readonly float[] window = new float[FftSize];

    readonly SynchronizationContext? ui = SynchronizationContext.Current;
    readonly object deckLock = new();
    readonly DeckMixer mixer;
    readonly SoundProcessor sound;
    readonly MasterStage master;
    WaveOutEvent? output;
    Deck? deck;                       // the current track
    readonly List<Deck> fading = [];  // tracks crossfading out
    volatile bool paused = true;
    float volume = 0.6f;
    float speed = 1f;
    bool muted;

    public readonly BpmDetector Bpm = new();

    public event EventHandler? TrackFinished;

    public AudioEngine()
    {
        for (int i = 0; i < FftSize; i++)
            window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
        mixer = new DeckMixer(this);
        sound = new SoundProcessor(mixer);
        master = new MasterStage(new TapSampleProvider(sound, OnSamples));
    }

    public SoundProcessor Sound => sound;

    /// <summary>Seconds of overlap between songs when one runs into the next (0 = off).</summary>
    public float CrossfadeSeconds { get; set; }

    public bool IsLoaded => deck != null;
    public bool IsPlaying => deck != null && !paused;
    public bool IsPaused => deck != null && paused;
    public bool IsLive => deck?.Source.IsLive ?? false;
    public TimeSpan Position => deck?.Source.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => deck?.Source.TotalTime ?? TimeSpan.Zero;

    /// <summary>0..1 linear slider position; applied with a squared curve so it feels natural.</summary>
    public float Volume
    {
        get => volume;
        set { volume = Math.Clamp(value, 0f, 1f); ApplyVolume(); }
    }

    public bool Muted
    {
        get => muted;
        set { muted = value; ApplyVolume(); }
    }

    /// <summary>Playback rate, 0.5x–2x. Like a tape deck's pitch control, pitch follows speed.</summary>
    public float Speed
    {
        get => speed;
        set
        {
            speed = Math.Clamp(value, 0.5f, 2f);
            lock (deckLock)
            {
                if (deck != null) deck.Varispeed.Speed = speed;
                foreach (var d in fading) d.Varispeed.Speed = speed;
            }
        }
    }

    /// <summary>Extra gain multiplier used for fading out (1 = normal).</summary>
    public float FadeGain
    {
        get => fade;
        set { fade = Math.Clamp(value, 0f, 1f); ApplyVolume(); }
    }
    float fade = 1f;

    /// <summary>Turns the music down under a voice line (1 = normal).</summary>
    public float Duck
    {
        get => duck;
        set { duck = Math.Clamp(value, 0f, 1f); ApplyVolume(); }
    }
    float duck = 1f;

    const float MaxGain = 0.7f;

    void ApplyVolume()
    {
        // Full volume peaks about 3 dB under full scale, like other players: music mastered at 0 dBFS would
        // otherwise go over when Windows converts it for the sound card (most run at 48 kHz) and crackle.
        master.Gain = muted ? 0f : volume * volume * fade * duck * MaxGain;
        sound.KnobVolume = volume;
    }

    void EnsureOutput()
    {
        if (output != null) return;
        // Generous buffering: the radio renders big images now and then, and the music must not skip.
        var o = new WaveOutEvent { DesiredLatency = 300, NumberOfBuffers = 4 };
        o.Init(master);
        output = o;
        ApplyVolume();
    }

    public void Load(string path) => Load(new FileTrackSource(path));

    /// <summary>
    /// Takes ownership of the source and makes it the current track. If the previous track was ending by
    /// itself and crossfade is on, the two overlap; otherwise the old one fades out in a blink.
    /// </summary>
    public void Load(ITrackSource r)
    {
        Deck d;
        try
        {
            d = new Deck(r, speed);
        }
        catch
        {
            r.Dispose();
            throw;
        }
        EnsureOutput();
        lock (deckLock)
        {
            if (deck != null)
            {
                float secs = deck.EndingSignalled && CrossfadeSeconds > 0 && !paused ? CrossfadeSeconds : 0.08f;
                deck.FadeTo(0, secs);
                fading.Add(deck);
                if (secs > 0.1f) d.StartFaded(secs);
            }
            deck = d;
        }
        Bpm.Reset();
        output!.Play(); // keeps running; with nothing playing it outputs silence
    }

    public void Play()
    {
        if (deck == null) return;
        EnsureOutput();
        paused = false;
        output!.Play();
    }

    public void Pause() => paused = true;

    public void TogglePause()
    {
        if (IsPlaying) Pause(); else Play();
    }

    /// <summary>Stops like a real deck: pause and rewind to the top of the track.</summary>
    public void Stop()
    {
        if (deck == null) return;
        paused = true;
        lock (deckLock)
        {
            foreach (var f in fading) f.Dispose();
            fading.Clear();
            if (!deck.Source.IsLive) deck.Source.CurrentTime = TimeSpan.Zero;
            deck.Varispeed.Reset();
            deck.EndingSignalled = deck.Ended = false;
        }
        ClearRing();
    }

    public void Seek(TimeSpan t)
    {
        var d = deck;
        if (d == null || d.Source.IsLive) return;
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t > d.Source.TotalTime) t = d.Source.TotalTime - TimeSpan.FromMilliseconds(50);
        lock (deckLock)
        {
            d.Source.CurrentTime = t;
            d.Varispeed.Reset();
            d.EndingSignalled = d.Ended = false;
        }
    }

    /// <summary>Unloads the current track entirely.</summary>
    public void Eject()
    {
        lock (deckLock)
        {
            deck?.Dispose();
            deck = null;
            foreach (var f in fading) f.Dispose();
            fading.Clear();
        }
        paused = true;
        ClearRing();
    }

    void RaiseTrackFinished()
    {
        if (ui != null) ui.Post(_ => TrackFinished?.Invoke(this, EventArgs.Empty), null);
        else ThreadPool.QueueUserWorkItem(_ => TrackFinished?.Invoke(this, EventArgs.Empty));
    }

    void ClearRing()
    {
        lock (ringLock) Array.Clear(ring);
    }

    void OnSamples(float[] buffer, int offset, int count)
    {
        if (paused && fading.Count == 0) return;
        Bpm.Feed(buffer, offset, count);
        lock (ringLock)
        {
            for (int i = offset; i + 1 < offset + count; i += 2)
            {
                ring[ringPos] = (buffer[i] + buffer[i + 1]) * 0.5f;
                ringPos = (ringPos + 1) & (FftSize - 1);
            }
        }
    }

    /// <summary>One track: its source brought to 44.1 kHz stereo, with varispeed and a fader.</summary>
    sealed class Deck : IDisposable
    {
        public readonly ITrackSource Source;
        public readonly VarispeedSampleProvider Varispeed;
        readonly ISampleProvider output;
        float gain = 1, target = 1, step;
        public bool EndingSignalled, Ended;

        public Deck(ITrackSource source, float speed)
        {
            Source = source;
            Varispeed = new VarispeedSampleProvider(source.Samples) { Speed = speed };
            ISampleProvider s = Varispeed;
            int ch = s.WaveFormat.Channels;
            if (ch == 1) s = new MonoToStereoSampleProvider(s);
            else if (ch > 2) s = new FirstTwoChannels(s);
            if (s.WaveFormat.SampleRate != Rate) s = new WdlResamplingSampleProvider(s, Rate);
            output = s;
        }

        public void StartFaded(float seconds)
        {
            gain = 0;
            FadeTo(1, seconds);
        }

        public void FadeTo(float to, float seconds)
        {
            target = to;
            step = Math.Abs(to - gain) / Math.Max(1, seconds * Rate);
        }

        public bool Silent => gain <= 0 && target <= 0;

        /// <summary>Adds this deck's audio into the mix buffer.</summary>
        public void MixInto(float[] mix, int offset, float[] tmp, int count)
        {
            int n = Ended ? 0 : output.Read(tmp, 0, count);
            if (n < count) Ended = true;
            for (int i = 0; i + 1 < n; i += 2)
            {
                if (gain != target) gain = gain < target ? Math.Min(target, gain + step) : Math.Max(target, gain - step);
                mix[offset + i] += tmp[i] * gain;
                mix[offset + i + 1] += tmp[i + 1] * gain;
            }
        }

        public void Dispose() => Source.Dispose();
    }

    /// <summary>Sums the current deck and any decks fading out; signals when the current track ends.</summary>
    sealed class DeckMixer(AudioEngine e) : ISampleProvider
    {
        float[] tmp = new float[8192];
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            // Not Array.Clear: NAudio hands over its byte buffer disguised as a float[], and Array.Clear would
            // clear bytes, leaving three quarters of the last round's audio in it (a loud echo every buffer cycle).
            buffer.AsSpan(offset, count).Clear();
            if (tmp.Length < count) tmp = new float[count];
            lock (e.deckLock)
            {
                if (e.paused) return count; // silence; the output keeps running
                var d = e.deck;
                if (d != null)
                {
                    d.MixInto(buffer, offset, tmp, count);
                    // Crossfade: tell the radio to start the next song a little before this one ends.
                    if (!d.EndingSignalled && !d.Source.IsLive && e.CrossfadeSeconds > 0 && d.Source.TotalTime.TotalSeconds > e.CrossfadeSeconds * 3
                        && (d.Source.TotalTime - d.Source.CurrentTime).TotalSeconds <= e.CrossfadeSeconds * e.speed)
                    {
                        d.EndingSignalled = true;
                        e.RaiseTrackFinished();
                    }
                    else if (d.Ended && !d.EndingSignalled)
                    {
                        d.EndingSignalled = true;
                        e.RaiseTrackFinished();
                    }
                }
                for (int i = e.fading.Count - 1; i >= 0; i--)
                {
                    var f = e.fading[i];
                    f.MixInto(buffer, offset, tmp, count);
                    if (f.Silent || f.Ended)
                    {
                        f.Dispose();
                        e.fading.RemoveAt(i);
                    }
                }
            }
            return count; // always full: the output never stops by itself
        }
    }

    sealed class FirstTwoChannels(ISampleProvider source) : ISampleProvider
    {
        readonly int ch = source.WaveFormat.Channels;
        float[] buf = [];
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            if (buf.Length < frames * ch) buf = new float[frames * ch];
            int n = source.Read(buf, 0, frames * ch) / ch;
            for (int f = 0; f < n; f++)
            {
                buffer[offset + f * 2] = buf[f * ch];
                buffer[offset + f * 2 + 1] = buf[f * ch + 1];
            }
            return n * 2;
        }
    }

    /// <summary>
    /// Master volume and a look-ahead peak limiter: when a loud song, the EQ or the reverb would go over full
    /// scale, the level is turned down smoothly just before the peak instead of the waveform being squashed
    /// (squashing is what sounds like broken speakers).
    /// </summary>
    sealed class MasterStage(ISampleProvider source) : ISampleProvider
    {
        const float Ceiling = 0.89f; // -1 dBFS: leaves room for the peaks resampling creates
        const int Look = 96; // ~2 ms look-ahead, in frames
        static readonly float Release = (float)(1 - Math.Exp(-1.0 / (0.25 * Rate))); // back up over ~250 ms

        public volatile float Gain = 1;
        float current = 1, env = 1;
        readonly float[] delay = new float[Look * 2];
        readonly float[] need = new float[Look]; // the gain each queued frame needs
        int dpos;
        bool boosted;
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (!boosted)
            {
                // The sound thread must never wait behind the busy UI.
                boosted = true;
                try { Thread.CurrentThread.Priority = ThreadPriority.Highest; } catch (Exception) { }
            }
            int n = source.Read(buffer, offset, count);
            float g = Gain;
            float stepG = (g - current) / Math.Max(1, n / 2);
            for (int i = offset; i + 1 < offset + n; i += 2)
            {
                current += stepG;
                float l = buffer[i] * current, r = buffer[i + 1] * current;
                float peak = Math.Max(Math.Abs(l), Math.Abs(r));
                if (!float.IsFinite(peak)) { l = r = 0; peak = 0; }
                need[dpos] = peak > Ceiling ? Ceiling / peak : 1;

                // Head for the lowest gain any frame in the look-ahead window needs, so it's there when that frame plays.
                float target = 1;
                for (int k = 0; k < Look; k++) if (need[k] < target) target = need[k];
                env += target < env ? (target - env) * (5f / Look) : (target - env) * Release;

                float dl = delay[dpos * 2], dr = delay[dpos * 2 + 1];
                delay[dpos * 2] = l;
                delay[dpos * 2 + 1] = r;
                dpos = (dpos + 1) % Look;
                buffer[i] = Math.Clamp(dl * env, -Ceiling, Ceiling);
                buffer[i + 1] = Math.Clamp(dr * env, -Ceiling, Ceiling);
            }
            current = g;
            return n;
        }
    }

    // ───────────────────────────── system audio (for Spotify) ─────────────────────────────

    WasapiLoopbackCapture? loopback;
    int loopRate = 48000;

    /// <summary>
    /// When on, the visualizer listens to everything the PC is playing (used while the
    /// Spotify app does the playback, since its audio never passes through the radio).
    /// </summary>
    public bool Loopback
    {
        get => loopback != null || simulated;
        set
        {
            if (value == (loopback != null || simulated)) return;
            if (!value)
            {
                if (simulated)
                {
                    simulated = false;
                    return;
                }
                loopback!.StopRecording();
                loopback.Dispose();
                loopback = null;
                ClearRing();
                return;
            }
            try
            {
                var cap = new WasapiLoopbackCapture();
                var fmt = cap.WaveFormat;
                loopRate = fmt.SampleRate;
                int ch = fmt.Channels;
                bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat || (fmt is WaveFormatExtensible && fmt.BitsPerSample == 32);
                float loopPeak = 0;
                cap.DataAvailable += (_, e) =>
                {
                    lock (ringLock)
                    {
                        int frame = ch * (isFloat ? 4 : 2);
                        int start = ringPos;
                        float blockPeak = 0;
                        for (int i = 0; i + frame <= e.BytesRecorded; i += frame)
                        {
                            float sum = 0;
                            for (int c = 0; c < ch; c++)
                                sum += isFloat ? BitConverter.ToSingle(e.Buffer, i + c * 4) : BitConverter.ToInt16(e.Buffer, i + c * 2) / 32768f;
                            float v = sum / ch;
                            blockPeak = Math.Max(blockPeak, Math.Abs(v));
                            ring[ringPos] = v;
                            ringPos = (ringPos + 1) & (FftSize - 1);
                        }
                        // The captured sound is after Spotify's own volume and loudness levelling, so it's
                        // much quieter than a local file (which is analysed before the volume knob).
                        // Automatic gain brings it back to full scale; silence isn't boosted.
                        loopPeak = Math.Max(blockPeak, loopPeak * 0.997f);
                        float gain = loopPeak < 0.002f ? 1 : Math.Clamp(0.9f / loopPeak, 1, 40);
                        if (gain > 1)
                            for (int k = start; k != ringPos; k = (k + 1) & (FftSize - 1)) ring[k] *= gain;
                    }
                };
                cap.StartRecording();
                loopback = cap;
            }
            catch (Exception)
            {
                loopback = null;
                simulated = true; // the system's sound can't be captured (e.g. on macOS): the analyzer dances by itself
            }
        }
    }

    bool Feeding => IsPlaying || loopback != null;

    // When the system's sound can't be captured, a made-up but musical-looking spectrum keeps the display alive.
    bool simulated;
    readonly Random simRng = new();
    float[] simLevels = [];
    readonly Stopwatch simClock = Stopwatch.StartNew();

    void Simulate(float[] bands)
    {
        if (simLevels.Length != bands.Length) simLevels = new float[bands.Length];
        double t = simClock.Elapsed.TotalSeconds;
        double beat = t * 2.05 % 1; // about 123 bpm
        float kick = (float)Math.Exp(-beat * 7);
        int n = bands.Length;
        for (int b = 0; b < n; b++)
        {
            float x = (float)b / Math.Max(1, n - 1);
            float target = 0.62f * (1 - x) * (0.45f + 0.55f * kick)
                + 0.3f * (float)(0.5 + 0.5 * Math.Sin(t * 1.3 + b * 0.7)) * (0.6f + 0.4f * (1 - x))
                + (float)simRng.NextDouble() * 0.22f * (0.4f + x);
            simLevels[b] += (Math.Clamp(target, 0, 1) - simLevels[b]) * 0.35f;
            bands[b] = simLevels[b];
        }
    }

    /// <summary>Fills bands with 0..1 levels on a log frequency scale (40 Hz – 16 kHz).</summary>
    public void GetSpectrum(float[] bands)
    {
        if (simulated && !IsPlaying)
        {
            Simulate(bands);
            return;
        }
        if (!Feeding)
        {
            Array.Clear(bands);
            return;
        }
        lock (ringLock)
        {
            for (int i = 0; i < FftSize; i++)
            {
                fft[i].X = ring[(ringPos + i) & (FftSize - 1)] * window[i];
                fft[i].Y = 0;
            }
        }
        FastFourierTransform.FFT(true, FftPow, fft);

        float sr = loopback != null ? loopRate : Rate;
        int n = bands.Length;
        const double fMin = 40, fMax = 16000;
        for (int b = 0; b < n; b++)
        {
            double f0 = fMin * Math.Pow(fMax / fMin, (double)b / n);
            double f1 = fMin * Math.Pow(fMax / fMin, (double)(b + 1) / n);
            int i0 = Math.Clamp((int)(f0 / sr * FftSize), 1, FftSize / 2 - 1);
            int i1 = Math.Clamp((int)Math.Ceiling(f1 / sr * FftSize), i0 + 1, FftSize / 2);
            double max = 0;
            for (int i = i0; i < i1; i++)
            {
                double m = Math.Sqrt(fft[i].X * fft[i].X + fft[i].Y * fft[i].Y);
                if (m > max) max = m;
            }
            // Tilt so highs are as lively as lows, then map roughly -66..-12 dB to 0..1.
            double db = 20 * Math.Log10(max + 1e-9) + 14.0 * b / n;
            bands[b] = (float)Math.Clamp((db + 66) / 54, 0, 1);
        }
    }

    /// <summary>Copies the most recent samples (oldest first) for the oscilloscope view.</summary>
    public void GetWaveform(float[] dest)
    {
        if (!Feeding)
        {
            Array.Clear(dest);
            return;
        }
        lock (ringLock)
        {
            int start = ringPos - dest.Length;
            for (int i = 0; i < dest.Length; i++)
                dest[i] = ring[(start + i) & (FftSize - 1)];
        }
    }

    WaveOutEvent? fxOut;
#if !MAC
    System.Media.SoundPlayer? fxPlayer;
#endif

    /// <summary>Plays a startup/shutdown sound on its own output, independent of the music. Returns its length in seconds.</summary>
    public double PlayEffect(string file, float? fixedVolume = null)
    {
        StopEffect();
        // Decode the whole sound up front (capped, so a whole song can't become the startup sound).
        using var reader = new AudioFileReader(file);
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
        var capped = reader.Take(TimeSpan.FromSeconds(8));
        var all = new List<float>(fmt.SampleRate * fmt.Channels * 5);
        var chunk = new float[fmt.SampleRate * fmt.Channels / 4];
        int n;
        while ((n = capped.Read(chunk, 0, chunk.Length)) > 0) all.AddRange(chunk.AsSpan(0, n));
        float[] data = [.. all];
        if (data.Length == 0) return 0;
        double seconds = (double)data.Length / (fmt.SampleRate * fmt.Channels);
        // Either its own volume setting, or following the volume knob (never quite silent).
        float gain = muted ? 0f : fixedVolume is { } fv ? fv * fv : Math.Max(0.15f, volume * volume);
#if MAC
        var o = new WaveOutEvent { DesiredLatency = 300 };
        o.Init(new VolumeSampleProvider(new ArraySampleProvider(data, fmt)) { Volume = gain });
        o.Play();
        fxOut = o;
#else
        // Handed to Windows as one finished WAV: the radio renders its faceplates at power-on and .NET pauses
        // its own threads to clean up memory, which made a .NET-fed player stutter. Windows plays this by itself.
        fxPlayer = new System.Media.SoundPlayer(new MemoryStream(ToWav(data, fmt, gain)));
        fxPlayer.Play();
#endif
        return seconds;
    }

    /// <summary>16-bit PCM WAV bytes for the samples, at the given gain.</summary>
    static byte[] ToWav(float[] data, WaveFormat fmt, float gain)
    {
        using var ms = new MemoryStream();
        using (var w = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(ms), new WaveFormat(fmt.SampleRate, 16, fmt.Channels)))
        {
            var pcm = new byte[data.Length * 2];
            for (int i = 0; i < data.Length; i++)
            {
                short s = (short)Math.Clamp((int)Math.Round(data[i] * gain * 32767), short.MinValue, short.MaxValue);
                pcm[i * 2] = (byte)s;
                pcm[i * 2 + 1] = (byte)(s >> 8);
            }
            w.Write(pcm, 0, pcm.Length);
        }
        return ms.ToArray();
    }

    void StopEffect()
    {
        fxOut?.Dispose();
        fxOut = null;
#if !MAC
        fxPlayer?.Stop();
        fxPlayer?.Dispose();
        fxPlayer = null;
#endif
    }

    public void Dispose()
    {
        Loopback = false;
        StopEffect();
        Eject();
        output?.Stop();
        output?.Dispose();
        output = null;
    }

    sealed class ArraySampleProvider(float[] data, WaveFormat format) : ISampleProvider
    {
        int pos;
        public WaveFormat WaveFormat => format;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, data.Length - pos);
            data.AsSpan(pos, n).CopyTo(buffer.AsSpan(offset, n)); // not Array.Copy: the buffer may be NAudio's disguised byte[]
            pos += n;
            return n;
        }
    }

    /// <summary>Resamples on the fly (linear interpolation) to play faster or slower.</summary>
    internal sealed class VarispeedSampleProvider(ISampleProvider source) : ISampleProvider
    {
        readonly int channels = source.WaveFormat.Channels;
        readonly object gate = new();
        float[] buf = new float[16384];
        int have;       // frames currently in buf
        double pos;     // fractional read position, in frames
        bool eof;

        public float Speed { get; set; } = 1f;
        public WaveFormat WaveFormat => source.WaveFormat;

        /// <summary>Drops buffered audio; call after seeking the source.</summary>
        public void Reset()
        {
            lock (gate)
            {
                have = 0;
                pos = 0;
                eof = false;
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (gate)
            {
                int ch = channels;
                int outFrames = count / ch;
                double step = Speed;

                // Discard frames we've already moved past.
                int drop = Math.Min((int)pos, have);
                if (drop > 0)
                {
                    Array.Copy(buf, drop * ch, buf, 0, (have - drop) * ch);
                    have -= drop;
                    pos -= drop;
                }

                int need = (int)Math.Ceiling(pos + outFrames * step) + 2;
                if (buf.Length < need * ch) Array.Resize(ref buf, need * ch * 2);
                while (have < need && !eof)
                {
                    int got = source.Read(buf, have * ch, (need - have) * ch);
                    if (got <= 0) eof = true;
                    else have += got / ch;
                }

                int written = 0;
                while (written < outFrames)
                {
                    int i0 = (int)pos;
                    if (i0 + 1 >= have) break;
                    float t = (float)(pos - i0);
                    int a = i0 * ch, b = a + ch, o = offset + written * ch;
                    for (int c = 0; c < ch; c++)
                        buffer[o + c] = buf[a + c] + (buf[b + c] - buf[a + c]) * t;
                    written++;
                    pos += step;
                }
                return written * ch;
            }
        }
    }

    sealed class TapSampleProvider(ISampleProvider source, Action<float[], int, int> onSamples) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = source.Read(buffer, offset, count);
            onSamples(buffer, offset, n);
            return n;
        }
    }
}
