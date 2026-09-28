namespace RetroRadio;

/// <summary>A recorded power-on or power-off sound shipped inside the program, with the word the display shows.</summary>
sealed record Chime(string Id, string Name, string File, string Greeting);

/// <summary>The JDM startup and shutdown sounds, plus RANDOM and (for startup) the user's own file.</summary>
static class Chimes
{
    public const string Off = "off", Random = "random", Custom = "custom";

    public static readonly Chime[] Startup =
    [
        new("welcome", "WELCOME", "startup_1_welcome_system.mp3", "ようこそ"),
        new("okaeri", "OKAERI", "startup_2_okaeri.mp3", "おかえり"),
        new("ohayou", "OHAYOU", "startup_3_ohayou.mp3", "おはよう"),
        new("konbanwa", "KONBANWA", "startup_4_konbanwa.mp3", "こんばんは"),
        new("engine", "ENGINE", "startup_5_engine.mp3", "エンジン始動"),
    ];

    public static readonly Chime[] Shutdown =
    [
        new("systemoff", "SYSTEM OFF", "shutdown_1_system_off.mp3", "さようなら"),
        new("otsukare", "OTSUKARE", "shutdown_2_otsukare.mp3", "おつかれさま"),
        new("mata", "MATA", "shutdown_3_mata.mp3", "またね"),
        new("oyasumi", "OYASUMI", "shutdown_4_oyasumi.mp3", "おやすみ"),
        new("anzen", "ANZEN UNTEN", "shutdown_5_anzen_unten.mp3", "安全運転で"),
    ];

    static Chime[] List(bool startup) => startup ? Startup : Shutdown;

    /// <summary>What the setting steps through: OFF, each sound, RANDOM, then (startup only) CUSTOM.</summary>
    public static string[] Choices(bool startup) =>
        [Off, .. List(startup).Select(c => c.Id), Random, .. startup ? new[] { Custom } : []];

    public static string Label(string id, bool startup) => id switch
    {
        Off => "OFF",
        Random => "RANDOM",
        Custom => "CUSTOM",
        _ => List(startup).FirstOrDefault(c => c.Id == id)?.Name ?? "OFF",
    };

    /// <summary>The sound to play: RANDOM picks one (never the same one twice in a row).</summary>
    public static Chime? Pick(string id, bool startup, Chime? last = null)
    {
        var list = List(startup);
        if (id != Random) return list.FirstOrDefault(c => c.Id == id);
        var pool = list.Where(c => c != last).ToArray();
        return pool[System.Random.Shared.Next(pool.Length)];
    }

    /// <summary>Unpacks a built-in sound to a temp file (once) so it can be decoded like any file.</summary>
    public static string? FilePath(Chime c)
    {
        string path = Path.Combine(Path.GetTempPath(), "RetroRadio", c.File);
        if (File.Exists(path)) return path;
        using var s = typeof(Chimes).Assembly.GetManifestResourceStream(c.File);
        if (s == null) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var f = File.Create(path);
        s.CopyTo(f);
        return path;
    }
}
