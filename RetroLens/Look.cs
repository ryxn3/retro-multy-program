using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetroLens;

enum AspectRatio { Original, Classic4x3, Photo3x2, Square, Wide16x9 }
enum Upscale { Smooth, Pixelated }
enum PaletteKind { Full, Posterize, GameBoyGreen, GameBoyGray, VirtualBoy, Nes, Cga, Ega16, Web256, NightVision }
enum DitherKind { None, Pattern, Diffusion }
enum StampKind { None, DateOrange, DateYellow, VhsRec, VhsPlay, Cctv }
enum BorderKind { None, Polaroid, InstantSquare, FilmStrip, WhiteMat, Tv }
enum AudioLook { Original, CamcorderMic, TinySpeaker, OldRadio, Mute }

/// <summary>Marks a setting that shows up as a control in the editor.</summary>
[AttributeUsage(AttributeTargets.Property)]
sealed class ParamAttribute(string group, string label, float min = 0, float max = 1) : Attribute
{
    public string Group => group;
    public string Label => label;
    public float Min => min;
    public float Max => max;
    public string? Help { get; init; }
}

/// <summary>Every knob of the retro look. Presets are just filled-in copies of this.</summary>
sealed class Look
{
    // ── Camera ──
    [Param("Camera", "Aspect")] public AspectRatio Aspect { get; set; } = AspectRatio.Original;
    [Param("Camera", "Sensor width (px)", 48, 1920, Help = "Lower = blockier, like old cameras")] public int Resolution { get; set; } = 1280;
    [Param("Camera", "Output width (0 = auto)", 0, 3840)] public int OutputWidth { get; set; }
    [Param("Camera", "Upscaling")] public Upscale Upscale { get; set; } = Upscale.Smooth;
    [Param("Camera", "Zoom", 0.5f, 2.5f)] public float Zoom { get; set; } = 1;

    // ── Lens ──
    [Param("Lens", "Fisheye / barrel", -0.6f, 1.2f, Help = "Negative = pincushion")] public float Fisheye { get; set; }
    [Param("Lens", "Round fisheye frame")] public bool FisheyeCircle { get; set; }
    [Param("Lens", "Chromatic aberration", 0, 12)] public float Aberration { get; set; }
    [Param("Lens", "Soft focus", 0, 6)] public float Blur { get; set; }
    [Param("Lens", "Sharpen (halos)", 0, 4)] public float Sharpen { get; set; }
    [Param("Lens", "Vignette")] public float Vignette { get; set; }

    // ── Color ──
    [Param("Color", "Brightness", -0.6f, 0.6f)] public float Brightness { get; set; }
    [Param("Color", "Contrast", 0.2f, 2.5f)] public float Contrast { get; set; } = 1;
    [Param("Color", "Saturation", 0, 2.5f)] public float Saturation { get; set; } = 1;
    [Param("Color", "Gamma", 0.4f, 2.5f)] public float Gamma { get; set; } = 1;
    [Param("Color", "Temperature", -1, 1)] public float Temperature { get; set; }
    [Param("Color", "Tint (green/magenta)", -1, 1)] public float Tint { get; set; }
    [Param("Color", "Hue shift", -180, 180)] public float HueShift { get; set; }
    [Param("Color", "Faded blacks", 0, 0.5f)] public float Fade { get; set; }
    [Param("Color", "Sepia")] public float Sepia { get; set; }
    [Param("Color", "Blown highlights")] public float Blown { get; set; }
    [Param("Color", "Cross-process")] public float CrossProcess { get; set; }

    // ── Light ──
    [Param("Light", "Bloom / glow")] public float Bloom { get; set; }
    [Param("Light", "Bloom threshold")] public float BloomThreshold { get; set; } = 0.7f;
    [Param("Light", "Light leak")] public float LightLeak { get; set; }

    // ── Texture ──
    [Param("Texture", "Sensor noise")] public float Noise { get; set; }
    [Param("Texture", "Color noise")] public float ChromaNoise { get; set; }
    [Param("Texture", "Film grain")] public float Grain { get; set; }
    [Param("Texture", "Grain size", 1, 4)] public float GrainSize { get; set; } = 1.5f;
    [Param("Texture", "Dust & scratches")] public float Dust { get; set; }

    // ── Digital ──
    [Param("Digital", "Palette")] public PaletteKind Palette { get; set; } = PaletteKind.Full;
    [Param("Digital", "Posterize levels", 2, 32)] public int Levels { get; set; } = 8;
    [Param("Digital", "Dither")] public DitherKind Dither { get; set; } = DitherKind.None;
    [Param("Digital", "JPEG quality (0 = off)", 0, 100)] public int Jpeg { get; set; }
    [Param("Digital", "Interlacing")] public float Interlace { get; set; }

    // ── Tape (VHS) ──
    [Param("Tape", "Color bleed")] public float ChromaBleed { get; set; }
    [Param("Tape", "Color shift (px)", 0, 10)] public float ColorShift { get; set; }
    [Param("Tape", "Wobble / jitter")] public float Jitter { get; set; }
    [Param("Tape", "Tracking noise")] public float Tracking { get; set; }

    // ── Screen (CRT) ──
    [Param("Screen", "Scanlines")] public float Scanlines { get; set; }
    [Param("Screen", "RGB mask")] public float CrtMask { get; set; }
    [Param("Screen", "Curvature")] public float Curvature { get; set; }
    [Param("Screen", "Flicker")] public float Flicker { get; set; }
    [Param("Screen", "Gate weave (film shake)")] public float GateWeave { get; set; }

    // ── Overlay ──
    [Param("Overlay", "Stamp")] public StampKind Stamp { get; set; } = StampKind.None;
    [Param("Overlay", "Stamp text")] public string StampText { get; set; } = "'05 7 14";
    [Param("Overlay", "Border")] public BorderKind Border { get; set; } = BorderKind.None;
    [Param("Overlay", "Border caption")] public string Caption { get; set; } = "";

    // ── Video ──
    [Param("Video", "Frame rate (0 = original)", 0, 60)] public int FrameRate { get; set; }
    [Param("Video", "Audio")] public AudioLook Audio { get; set; } = AudioLook.Original;

    public Look Clone() => JsonSerializer.Deserialize<Look>(JsonSerializer.Serialize(this, Json), Json)!;

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
}

sealed record Preset(string Category, string Name, Look Look);

/// <summary>The built-in looks.</summary>
static class Presets
{
    static Preset P(string cat, string name, Action<Look> set)
    {
        var l = new Look();
        set(l);
        return new Preset(cat, name, l);
    }

    static readonly string[] StyleOrder = ["Basic", "Handheld", "Digital", "Tape", "Film", "Screen", "Lens"];

    /// <summary>Style sections first, then camera brands A–Z.</summary>
    public static List<Preset> All()
    {
        var list = Builtin().Concat(BrandPresets.All()).Select((p, i) => (p, i)).ToList();
        int Rank(Preset p) => Array.IndexOf(StyleOrder, p.Category) is var r and >= 0 ? r : StyleOrder.Length;
        return list.OrderBy(x => Rank(x.p)).ThenBy(x => Rank(x.p) < StyleOrder.Length ? "" : x.p.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.i).Select(x => x.p).ToList();
    }

    static List<Preset> Builtin() =>
    [
        P("Basic", "Original (no effect)", _ => { }),

        // ── Handhelds & consoles ──
        P("Nintendo", "DSi camera", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 256; l.Upscale = Upscale.Smooth;
            l.Sharpen = 1.6f; l.Noise = 0.35f; l.ChromaNoise = 0.3f; l.Saturation = 0.85f; l.Contrast = 1.15f;
            l.Temperature = -0.25f; l.Blown = 0.35f; l.Jpeg = 35; l.Vignette = 0.15f;
        }),
        P("Nintendo", "3DS camera", l =>
        {
            l.Aspect = AspectRatio.Wide16x9; l.Resolution = 400; l.Sharpen = 1.2f; l.Noise = 0.25f; l.ChromaNoise = 0.2f;
            l.Saturation = 1.1f; l.Contrast = 1.1f; l.Jpeg = 45; l.Temperature = -0.1f;
        }),
        P("Nintendo", "Game Boy Camera", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 128; l.Upscale = Upscale.Pixelated; l.Contrast = 1.4f;
            l.Sharpen = 1.2f; l.Palette = PaletteKind.GameBoyGreen; l.Dither = DitherKind.Pattern; l.Border = BorderKind.WhiteMat;
        }),
        P("Nintendo", "Game Boy Camera (grey)", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 128; l.Upscale = Upscale.Pixelated; l.Contrast = 1.4f;
            l.Sharpen = 1.2f; l.Palette = PaletteKind.GameBoyGray; l.Dither = DitherKind.Pattern;
        }),
        P("Handheld", "Early phone (VGA)", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 320; l.Blur = 0.6f; l.Noise = 0.45f; l.ChromaNoise = 0.45f;
            l.Aberration = 2.5f; l.Tint = 0.2f; l.Saturation = 0.8f; l.Blown = 0.4f; l.Jpeg = 22; l.Vignette = 0.3f;
        }),
        P("Nintendo", "NES 8-bit", l =>
        {
            l.Resolution = 192; l.Upscale = Upscale.Pixelated; l.Palette = PaletteKind.Nes; l.Dither = DitherKind.Pattern;
            l.Contrast = 1.2f; l.Saturation = 1.3f;
        }),

        // ── Digital cameras & webcams ──
        P("Digital", "2000s digital camera", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 1024; l.Sharpen = 1.1f; l.Contrast = 1.15f; l.Saturation = 1.2f;
            l.Temperature = 0.15f; l.Bloom = 0.35f; l.Blown = 0.45f; l.Noise = 0.12f; l.Jpeg = 60; l.Stamp = StampKind.DateOrange;
        }),
        P("Digital", "Webcam 2006", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 320; l.Blur = 1f; l.Noise = 0.5f; l.ChromaNoise = 0.4f;
            l.Tint = -0.35f; l.Blown = 0.6f; l.Contrast = 1.2f; l.Saturation = 0.75f; l.Jpeg = 18; l.FrameRate = 15;
        }),
        P("Digital", "256-color GIF", l =>
        {
            l.Resolution = 480; l.Palette = PaletteKind.Web256; l.Dither = DitherKind.Diffusion;
        }),
        P("Digital", "Windows 95 (16 colors)", l =>
        {
            l.Resolution = 320; l.Upscale = Upscale.Pixelated; l.Palette = PaletteKind.Ega16; l.Dither = DitherKind.Diffusion;
        }),
        P("Digital", "CGA (4 colors)", l =>
        {
            l.Resolution = 200; l.Upscale = Upscale.Pixelated; l.Palette = PaletteKind.Cga; l.Dither = DitherKind.Pattern; l.Contrast = 1.3f;
        }),
        P("Digital", "Deep-fried JPEG", l =>
        {
            l.Resolution = 480; l.Saturation = 2.2f; l.Contrast = 1.8f; l.Sharpen = 3.5f; l.Jpeg = 4; l.Noise = 0.3f;
        }),

        // ── Tape ──
        P("Tape", "VHS camcorder", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 352; l.Blur = 0.8f; l.ChromaBleed = 0.7f; l.ColorShift = 3;
            l.Jitter = 0.35f; l.Tracking = 0.35f; l.Noise = 0.3f; l.Saturation = 1.15f; l.Contrast = 1.1f; l.Fade = 0.06f;
            l.Sharpen = 0.8f; l.Stamp = StampKind.VhsRec; l.StampText = "JAN.01 1998"; l.Interlace = 0.3f; l.Audio = AudioLook.CamcorderMic;
        }),
        P("Tape", "VHS tape (worn)", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 300; l.Blur = 1.2f; l.ChromaBleed = 1f; l.ColorShift = 5;
            l.Jitter = 0.7f; l.Tracking = 0.8f; l.Noise = 0.5f; l.Saturation = 0.8f; l.Fade = 0.12f; l.Stamp = StampKind.VhsPlay;
            l.StampText = "JUN.14 1994"; l.Audio = AudioLook.OldRadio;
        }),
        P("Tape", "Hi8 / MiniDV", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 640; l.Sharpen = 1.8f; l.Interlace = 0.6f; l.ChromaBleed = 0.25f;
            l.Saturation = 1.2f; l.Noise = 0.15f; l.Blown = 0.3f; l.Audio = AudioLook.CamcorderMic;
        }),

        // ── Film ──
        P("Film", "Super 8", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 640; l.Blur = 1f; l.Grain = 0.6f; l.GrainSize = 2f; l.Temperature = 0.35f;
            l.Fade = 0.1f; l.Vignette = 0.55f; l.Flicker = 0.4f; l.GateWeave = 0.5f; l.Dust = 0.35f; l.LightLeak = 0.25f;
            l.Saturation = 1.1f; l.Contrast = 1.1f; l.FrameRate = 18; l.Audio = AudioLook.Mute;
        }),
        P("Film", "1920s silent film", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 560; l.Blur = 1.2f; l.Saturation = 0; l.Sepia = 0.6f; l.Contrast = 1.35f;
            l.Grain = 0.8f; l.GrainSize = 2.2f; l.Vignette = 0.8f; l.Flicker = 0.7f; l.GateWeave = 0.7f; l.Dust = 0.8f;
            l.FrameRate = 16; l.Audio = AudioLook.Mute;
        }),
        P("Film", "Disposable camera", l =>
        {
            l.Aspect = AspectRatio.Photo3x2; l.Resolution = 1200; l.Grain = 0.45f; l.Temperature = 0.3f; l.Fade = 0.08f; l.Vignette = 0.45f;
            l.Saturation = 1.15f; l.Contrast = 1.1f; l.Blown = 0.4f; l.LightLeak = 0.35f; l.Blur = 0.4f; l.Stamp = StampKind.DateOrange;
        }),
        P("Film", "Polaroid", l =>
        {
            l.Aspect = AspectRatio.Square; l.Resolution = 900; l.Fade = 0.18f; l.Temperature = 0.1f; l.Tint = 0.12f; l.Contrast = 0.9f;
            l.Saturation = 0.8f; l.Vignette = 0.35f; l.Blur = 0.6f; l.Grain = 0.25f; l.Border = BorderKind.Polaroid; l.Bloom = 0.2f;
        }),
        P("Film", "Instax", l =>
        {
            l.Aspect = AspectRatio.Photo3x2; l.Resolution = 900; l.Fade = 0.1f; l.Temperature = -0.1f; l.Saturation = 1.1f;
            l.Contrast = 1.05f; l.Blown = 0.3f; l.Border = BorderKind.InstantSquare; l.Vignette = 0.2f;
        }),
        P("Film", "Lomo", l =>
        {
            l.Resolution = 1200; l.Vignette = 0.9f; l.Saturation = 1.5f; l.Contrast = 1.4f; l.CrossProcess = 0.6f; l.Grain = 0.3f; l.Blur = 0.4f;
        }),
        P("Film", "35mm film strip", l =>
        {
            l.Aspect = AspectRatio.Photo3x2; l.Resolution = 1200; l.Grain = 0.35f; l.Temperature = 0.12f; l.Fade = 0.05f;
            l.Border = BorderKind.FilmStrip; l.Vignette = 0.25f;
        }),

        // ── Screens & surveillance ──
        P("Screen", "CRT TV", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 480; l.Blur = 0.5f; l.Scanlines = 0.65f; l.CrtMask = 0.45f; l.Curvature = 0.45f;
            l.Bloom = 0.45f; l.BloomThreshold = 0.55f; l.Saturation = 1.2f; l.Border = BorderKind.Tv; l.Flicker = 0.1f;
        }),
        P("Screen", "Arcade monitor", l =>
        {
            l.Resolution = 320; l.Upscale = Upscale.Pixelated; l.Scanlines = 0.8f; l.CrtMask = 0.6f; l.Curvature = 0.3f; l.Bloom = 0.6f;
            l.BloomThreshold = 0.5f; l.Saturation = 1.3f; l.Contrast = 1.2f;
        }),
        P("Screen", "Security camera", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 352; l.Saturation = 0; l.Contrast = 1.3f; l.Noise = 0.55f; l.Blur = 0.8f;
            l.Fisheye = 0.35f; l.Stamp = StampKind.Cctv; l.StampText = "CAM 01"; l.Jpeg = 25; l.FrameRate = 10; l.Audio = AudioLook.Mute;
        }),
        P("Screen", "Night vision", l =>
        {
            l.Resolution = 480; l.Palette = PaletteKind.NightVision; l.Noise = 0.7f; l.Bloom = 0.5f; l.BloomThreshold = 0.5f;
            l.Vignette = 0.9f; l.Contrast = 1.4f; l.Brightness = 0.1f; l.Scanlines = 0.25f;
        }),

        // ── Lenses ──
        P("Lens", "Fisheye", l => { l.Fisheye = 1.0f; l.Aspect = AspectRatio.Square; l.Vignette = 0.4f; l.Aberration = 2; }),
        P("Lens", "Round fisheye (skate video)", l =>
        {
            l.Fisheye = 1.15f; l.FisheyeCircle = true; l.Aspect = AspectRatio.Classic4x3; l.Resolution = 640; l.Aberration = 3;
            l.Sharpen = 1f; l.Interlace = 0.4f; l.Saturation = 1.2f; l.Audio = AudioLook.CamcorderMic;
        }),
        P("Lens", "Toy camera", l =>
        {
            l.Aspect = AspectRatio.Square; l.Resolution = 800; l.Blur = 1.5f; l.Vignette = 1f; l.Aberration = 4; l.Saturation = 1.3f;
            l.LightLeak = 0.4f; l.Fisheye = 0.2f;
        }),
        P("Lens", "Dreamy glow", l => { l.Bloom = 0.8f; l.BloomThreshold = 0.45f; l.Blur = 0.8f; l.Fade = 0.1f; l.Temperature = 0.2f; l.Saturation = 0.9f; }),
    ];

    static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroLens", "presets");

    public static List<Preset> Mine()
    {
        var list = new List<Preset>();
        if (!Directory.Exists(Folder)) return list;
        foreach (var f in Directory.EnumerateFiles(Folder, "*.json").OrderBy(f => f))
        {
            try { list.Add(new Preset("My presets", Path.GetFileNameWithoutExtension(f), JsonSerializer.Deserialize<Look>(File.ReadAllText(f), Look.Json)!)); }
            catch (Exception) { }
        }
        return list;
    }

    public static void Save(string name, Look look)
    {
        Directory.CreateDirectory(Folder);
        string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        File.WriteAllText(Path.Combine(Folder, (safe.Length == 0 ? "preset" : safe) + ".json"), JsonSerializer.Serialize(look, Look.Json));
    }

    public static void Delete(string name)
    {
        var f = Path.Combine(Folder, name + ".json");
        if (File.Exists(f)) File.Delete(f);
    }
}
