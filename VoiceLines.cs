using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RetroRadio;

/// <summary>One of the spoken JDM navigation-style lines, with what the display shows while it plays.</summary>
sealed record VoiceLine(string Id, string Japanese, string English);

/// <summary>
/// The spoken lines: a greeting that fits the local time at power-on, then now and then a line that fits
/// the moment (late at night, a long session, the last song, the speed knob turned up…), like a car's
/// navigation voice. The music is turned down while one plays.
/// </summary>
static class VoiceLines
{
    public static readonly VoiceLine[] All =
    [
        new("01_ohayou", "おはようございます", "GOOD MORNING"),
        new("02_konnichiwa", "こんにちは", "GOOD AFTERNOON"),
        new("03_konbanwa", "こんばんは", "GOOD EVENING"),
        new("04_oyasumi", "おやすみなさい", "GOOD NIGHT"),
        new("05_ii_tenki", "いい天気ですね", "NICE WEATHER TODAY"),
        new("06_ame", "雨ですね　気をつけて", "IT'S RAINING - DRIVE CAREFULLY"),
        new("07_yoru", "夜のドライブ　素敵ですね", "NIGHT DRIVE IS LOVELY"),
        new("08_kyukei", "長時間の運転です　休憩しましょう", "LONG DRIVE - TAKE A BREAK"),
        new("09_mizu", "水分補給を忘れずに", "DON'T FORGET TO DRINK WATER"),
        new("10_anzen", "安全運転でお願いします", "PLEASE DRIVE SAFELY"),
        new("11_seatbelt", "シートベルトを締めてください", "FASTEN YOUR SEATBELT"),
        new("12_ongaku", "いい音楽ですね", "NICE MUSIC"),
        new("13_kyou_mo", "今日もドライブしましょう", "LET'S DRIVE TODAY TOO"),
        new("14_touge", "峠モード起動", "TOUGE MODE ACTIVATED"),
        new("15_turbo", "ターボ準備完了", "TURBO READY"),
        new("16_kakkoii", "この車　かっこいい", "THIS CAR IS SO COOL"),
        new("17_yukkuri", "ゆっくり行きましょう", "LET'S TAKE IT EASY"),
        new("18_mou_sugu", "もうすぐです", "ALMOST THERE"),
        new("19_ganbatte", "今日も頑張りましょう", "LET'S DO OUR BEST TODAY"),
        new("20_arigatou", "いつも運転ありがとう", "THANKS FOR ALWAYS DRIVING ME"),
    ];

    public static VoiceLine Get(string idPrefix) => All.First(v => v.Id.StartsWith(idPrefix, StringComparison.Ordinal));

    /// <summary>The greeting for the time of day on the PC's clock.</summary>
    public static VoiceLine Greeting(DateTime now) => now.Hour switch
    {
        >= 5 and < 11 => Get("01"),
        >= 11 and < 17 => Get("02"),
        >= 17 and < 22 => Get("03"),
        _ => Get("04"),
    };

    /// <summary>A line that fits the moment (never the same one twice in a row).</summary>
    public static VoiceLine Pick(DateTime now, double sessionMinutes, VoiceLine? last, Random rng)
    {
        var pool = new List<string>();
        int h = now.Hour;
        if (h >= 21 || h < 4) pool.AddRange(["07", "07", "17", "10"]);
        if (h >= 23 || h < 4) pool.Add("04");
        if (h >= 5 && h < 11) pool.AddRange(["13", "19", "19"]);
        if (sessionMinutes > 60) pool.AddRange(["08", "09", "09"]);
        pool.AddRange(["10", "12", "12", "16", "20", "14", "13"]);
        var choices = pool.Select(Get).Where(v => v != last).ToList();
        return choices[rng.Next(choices.Count)];
    }

    static string ResourceName(VoiceLine v) => "voice_" + v.Id + ".mp3";

    /// <summary>The line's audio, decoded to float samples (null if missing).</summary>
    public static (float[] Data, WaveFormat Format)? Load(VoiceLine v)
    {
        string path = Path.Combine(Path.GetTempPath(), "RetroRadio", ResourceName(v));
        if (!File.Exists(path))
        {
            using var s = typeof(VoiceLines).Assembly.GetManifestResourceStream(ResourceName(v));
            if (s == null) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var f = File.Create(path);
            s.CopyTo(f);
        }
        using var r = new AudioFileReader(path);
        var all = new List<float>();
        var buf = new float[r.WaveFormat.SampleRate * r.WaveFormat.Channels];
        int n;
        while ((n = r.Read(buf, 0, buf.Length)) > 0) all.AddRange(buf.AsSpan(0, n));
        return ([.. all], WaveFormat.CreateIeeeFloatWaveFormat(r.WaveFormat.SampleRate, r.WaveFormat.Channels));
    }
}

/// <summary>Plays a voice line on its own output (the whole clip is decoded up front, so it can't stutter).</summary>
sealed class VoicePlayer : IDisposable
{
    WaveOutEvent? output;

    /// <summary>Plays the line and returns its length in seconds.</summary>
    public double Play(VoiceLine line, float volume)
    {
        Stop();
        var clip = VoiceLines.Load(line);
        if (clip == null) return 0;
        var (data, fmt) = clip.Value;
        var src = new VolumeSampleProvider(new Clip(data, fmt)) { Volume = volume };
        output = new WaveOutEvent { DesiredLatency = 250, NumberOfBuffers = 2 };
        output.Init(src);
        output.Play();
        return (double)data.Length / (fmt.SampleRate * fmt.Channels);
    }

    public void Stop()
    {
        output?.Stop();
        output?.Dispose();
        output = null;
    }

    public void Dispose() => Stop();

    sealed class Clip(float[] data, WaveFormat format) : ISampleProvider
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
}
