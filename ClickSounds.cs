using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RetroRadio;

/// <summary>
/// Button and knob sounds: a few built in (synthesized), plus any sound files the user drops into
/// %AppData%\RetroRadio\clicks. They play on their own quick, always-open output.
/// </summary>
sealed class ClickSounds : IDisposable
{
    const int Rate = 44100;
    public const string Off = "OFF";

    static readonly (string Name, Func<float[]> Make)[] BuiltIn =
    [
        ("CLICK", () => Synth(0.018, t => Noise(t) * Math.Exp(-t * 900) * 0.9 + Math.Sin(2 * Math.PI * 3200 * t) * Math.Exp(-t * 1400) * 0.4)),
        ("SOFT", () => Synth(0.03, t => Math.Sin(2 * Math.PI * 900 * t) * Math.Exp(-t * 260) * 0.55 + Noise(t) * Math.Exp(-t * 600) * 0.2)),
        ("TACTILE", () => Synth(0.06, t => Tick(t, 0) + Tick(t, 0.038) * 0.6)),
        ("BEEP", () => Synth(0.05, t => Math.Sin(2 * Math.PI * 2400 * t) * Env(t, 0.05) * 0.35)),
        ("MECHANICAL", () => Synth(0.05, t => Noise(t) * Math.Exp(-t * 450) * 0.7 + Math.Sin(2 * Math.PI * 180 * t) * Math.Exp(-t * 120) * 0.35)),
    ];

    static readonly (string Name, Func<float[]> Make)[] BuiltInKnob =
    [
        ("DETENT", () => Synth(0.012, t => Noise(t) * Math.Exp(-t * 1500) * 0.6 + Math.Sin(2 * Math.PI * 4200 * t) * Math.Exp(-t * 2500) * 0.3)),
        ("RATCHET", () => Synth(0.02, t => Noise(t) * Math.Exp(-t * 700) * 0.8)),
        ("SOFT", () => Synth(0.02, t => Math.Sin(2 * Math.PI * 1300 * t) * Math.Exp(-t * 500) * 0.35)),
    ];

    static readonly Random Rng = new(7);
    static double Noise(double t) => Rng.NextDouble() * 2 - 1;
    static double Tick(double t, double at) => t < at ? 0 : Noise(t) * Math.Exp(-(t - at) * 1000) + Math.Sin(2 * Math.PI * 2600 * (t - at)) * Math.Exp(-(t - at) * 1600) * 0.4;
    static double Env(double t, double len) => Math.Min(1, t * 2000) * Math.Min(1, (len - t) * 400);

    static float[] Synth(double seconds, Func<double, double> f)
    {
        var s = new float[(int)(Rate * seconds)];
        for (int i = 0; i < s.Length; i++) s[i] = (float)f((double)i / Rate);
        return s;
    }

    static string UserFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroRadio", "clicks");

    static IEnumerable<string> UserFiles()
    {
        try
        {
            if (!Directory.Exists(UserFolder)) return [];
            return Directory.EnumerateFiles(UserFolder)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".wav" or ".mp3" or ".ogg" or ".flac" or ".m4a" or ".aif" or ".aiff")
                .OrderBy(f => f);
        }
        catch (IOException) { return []; }
    }

    /// <summary>The choices for the button (or knob) sound setting: OFF, built-ins, then the user's files.</summary>
    public static string[] Choices(bool knob) =>
        [Off, .. (knob ? BuiltInKnob : BuiltIn).Select(b => b.Name), .. UserFiles().Select(f => "FILE:" + Path.GetFileName(f))];

    public static string Label(string choice) => choice.StartsWith("FILE:") ? Path.GetFileNameWithoutExtension(choice[5..]).ToUpperInvariant() : choice;

    readonly Dictionary<string, float[]?> cache = [];
    WaveOutEvent? output;
    MixingSampleProvider? mix;

    float[]? Samples(string choice, bool knob)
    {
        string key = (knob ? "k:" : "b:") + choice;
        if (cache.TryGetValue(key, out var hit)) return hit;
        float[]? data = null;
        try
        {
            if (choice.StartsWith("FILE:"))
            {
                // A user's sound: decoded once to mono 44.1 kHz, at most half a second.
                using var r = new AudioFileReader(Path.Combine(UserFolder, choice[5..]));
                ISampleProvider s = r;
                if (s.WaveFormat.Channels == 2) s = new StereoToMonoSampleProvider(s);
                else if (s.WaveFormat.Channels > 2) return null;
                if (s.WaveFormat.SampleRate != Rate) s = new WdlResamplingSampleProvider(s, Rate);
                var buf = new float[Rate / 2];
                int n = 0, got;
                while (n < buf.Length && (got = s.Read(buf, n, buf.Length - n)) > 0) n += got;
                data = buf[..n];
            }
            else
            {
                data = (knob ? BuiltInKnob : BuiltIn).FirstOrDefault(b => b.Name == choice).Make?.Invoke();
            }
        }
        catch (Exception) { data = null; }
        cache[key] = data;
        return data;
    }

    /// <summary>Plays a sound at 0..1 volume (knob detents overlap freely).</summary>
    public void Play(string choice, bool knob, float volume)
    {
        if (choice == Off || volume <= 0) return;
        var data = Samples(choice, knob);
        if (data == null || data.Length == 0) return;
        try
        {
            if (output == null)
            {
                mix = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1)) { ReadFully = true };
                output = new WaveOutEvent { DesiredLatency = 70, NumberOfBuffers = 2 };
                output.Init(mix);
                output.Play();
            }
            mix!.AddMixerInput(new VolumeSampleProvider(new Clip(data)) { Volume = volume });
        }
        catch (Exception) { }
    }

    sealed class Clip(float[] data) : ISampleProvider
    {
        int pos;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, data.Length - pos);
            data.AsSpan(pos, n).CopyTo(buffer.AsSpan(offset, n)); // not Array.Copy: the buffer may be NAudio's disguised byte[]
            pos += n;
            return n;
        }
    }

    public void Dispose()
    {
        output?.Stop();
        output?.Dispose();
        output = null;
    }
}
