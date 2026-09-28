namespace RetroLens;

/// <summary>
/// Looks inspired by specific cameras, film stocks and phones, grouped by brand.
/// (Approximations of each device's typical look, not official profiles.)
/// </summary>
static class BrandPresets
{
    static Preset P(string brand, string name, Action<Look> set)
    {
        var l = new Look();
        set(l);
        return new Preset(brand, name, l);
    }

    /// <summary>A 2000s CCD point-and-shoot: punchy colour, early highlight clipping, a touch of noise.</summary>
    static void Ccd(Look l, int res, int jpeg)
    {
        l.Aspect = AspectRatio.Classic4x3; l.Resolution = res; l.Jpeg = jpeg;
        l.Sharpen = 1.1f; l.Saturation = 1.2f; l.Contrast = 1.12f; l.Blown = 0.35f; l.Noise = 0.12f; l.Bloom = 0.15f;
    }

    /// <summary>An early camera phone: tiny sensor, smeary, noisy, heavy JPEG.</summary>
    static void Phone(Look l, int res, int jpeg)
    {
        l.Aspect = AspectRatio.Classic4x3; l.Resolution = res; l.Jpeg = jpeg;
        l.Blur = 0.5f; l.Noise = 0.45f; l.ChromaNoise = 0.4f; l.Blown = 0.5f; l.Vignette = 0.3f; l.Saturation = 0.85f;
    }

    /// <summary>A film stock on a 35mm camera.</summary>
    static void Film(Look l, float grain)
    {
        l.Aspect = AspectRatio.Photo3x2; l.Resolution = 1400; l.Grain = grain; l.GrainSize = 1.6f; l.Vignette = 0.2f;
    }

    /// <summary>A consumer camcorder with its on-screen display.</summary>
    static void Camcorder(Look l, int res, string date)
    {
        l.Aspect = AspectRatio.Classic4x3; l.Resolution = res; l.Interlace = 0.45f; l.Sharpen = 1.4f; l.ChromaBleed = 0.3f;
        l.Noise = 0.18f; l.Stamp = StampKind.VhsRec; l.StampText = date; l.Audio = AudioLook.CamcorderMic;
    }

    public static List<Preset> All() =>
    [
        // ── Sony ──
        P("Sony", "Cyber-shot (2003)", l => { Ccd(l, 1280, 70); l.Temperature = -0.05f; }),
        P("Sony", "Mavica floppy-disk camera", l => { Ccd(l, 640, 22); l.Blur = 0.5f; l.Saturation = 0.95f; l.Blown = 0.55f; l.Noise = 0.25f; }),
        P("Sony", "Handycam Hi8 (1996)", l => { Camcorder(l, 560, "JUL.04 1996"); l.Saturation = 1.2f; }),
        P("Sony", "Handycam NightShot", l =>
        {
            l.Resolution = 480; l.Palette = PaletteKind.NightVision; l.Noise = 0.6f; l.Vignette = 0.8f; l.Bloom = 0.4f; l.BloomThreshold = 0.5f;
            l.Stamp = StampKind.VhsRec; l.StampText = "NIGHTSHOT"; l.Interlace = 0.3f;
        }),
        P("Sony", "Sony Ericsson K750i", l => { Phone(l, 800, 32); l.Sharpen = 1.4f; l.Noise = 0.3f; l.Temperature = 0.1f; }),
        P("Sony", "PlayStation EyeToy", l => { Phone(l, 320, 25); l.Blur = 0.9f; l.Tint = -0.25f; l.FrameRate = 15; }),
        P("Sony", "PSP Go!Cam", l => { Phone(l, 480, 35); l.Saturation = 1.05f; l.Sharpen = 0.8f; }),

        // ── Canon ──
        P("Canon", "PowerShot (2003)", l => { Ccd(l, 1280, 65); l.Temperature = 0.15f; l.Stamp = StampKind.DateOrange; l.StampText = "'03 8 21"; }),
        P("Canon", "IXUS / Digital ELPH", l => { Ccd(l, 1600, 75); l.Sharpen = 1.4f; l.Contrast = 1.2f; l.Saturation = 1.3f; }),
        P("Canon", "AE-1 on 35mm film", l => { Film(l, 0.4f); l.Temperature = 0.1f; l.Fade = 0.06f; l.Vignette = 0.3f; }),
        P("Canon", "MiniDV camcorder", l => { Camcorder(l, 720, "DEC.24 2003"); l.Interlace = 0.6f; l.Sharpen = 1.8f; }),

        // ── Nikon ──
        P("Nikon", "Coolpix (2001)", l => { Ccd(l, 1024, 60); l.Aberration = 2; l.Blown = 0.4f; l.Sharpen = 1.3f; }),
        P("Nikon", "FM2 with black & white film", l => { Film(l, 0.7f); l.Saturation = 0; l.Contrast = 1.4f; l.GrainSize = 2f; l.Vignette = 0.35f; }),
        P("Nikon", "F3 with slide film", l => { Film(l, 0.2f); l.Saturation = 1.3f; l.Contrast = 1.25f; l.Temperature = 0.05f; }),

        // ── Kodak ──
        P("Kodak", "EasyShare (2006)", l => { Ccd(l, 1200, 60); l.Saturation = 1.35f; l.Temperature = 0.2f; l.Blown = 0.45f; l.Stamp = StampKind.DateYellow; l.StampText = "'06 6 18"; }),
        P("Kodak", "Portra 400", l => { Film(l, 0.3f); l.Fade = 0.08f; l.Saturation = 0.9f; l.Temperature = 0.12f; l.Contrast = 0.95f; }),
        P("Kodak", "Gold 200", l => { Film(l, 0.35f); l.Temperature = 0.3f; l.Saturation = 1.2f; l.Fade = 0.05f; l.Contrast = 1.05f; }),
        P("Kodak", "Kodachrome 64", l => { Film(l, 0.15f); l.Saturation = 1.4f; l.Contrast = 1.3f; l.HueShift = -4; l.Temperature = 0.05f; }),
        P("Kodak", "Tri-X 400 (black & white)", l => { Film(l, 0.75f); l.Saturation = 0; l.Contrast = 1.45f; l.GrainSize = 2.2f; }),
        P("Kodak", "Disposable with flash", l =>
        {
            Film(l, 0.5f); l.Blown = 0.5f; l.Vignette = 0.5f; l.LightLeak = 0.2f; l.Temperature = 0.3f; l.Blur = 0.4f;
            l.Stamp = StampKind.DateOrange; l.StampText = "'99 12 31";
        }),

        // ── Fujifilm ──
        P("Fujifilm", "FinePix (2004)", l => { Ccd(l, 1280, 60); l.Saturation = 1.3f; l.Tint = -0.15f; }),
        P("Fujifilm", "Superia 400", l => { Film(l, 0.4f); l.Tint = -0.12f; l.Saturation = 1.15f; l.Contrast = 1.1f; }),
        P("Fujifilm", "Velvia 50", l => { Film(l, 0.1f); l.Saturation = 1.8f; l.Contrast = 1.35f; l.Temperature = -0.05f; }),
        P("Fujifilm", "QuickSnap disposable", l => { Film(l, 0.5f); l.Blown = 0.45f; l.Vignette = 0.45f; l.Tint = -0.1f; l.Blur = 0.4f; }),
        P("Fujifilm", "Instax Mini", l =>
        {
            l.Aspect = AspectRatio.Photo3x2; l.Resolution = 900; l.Fade = 0.1f; l.Temperature = -0.1f; l.Saturation = 1.1f; l.Blown = 0.3f;
            l.Border = BorderKind.InstantSquare; l.Vignette = 0.2f;
        }),

        // ── Olympus ──
        P("Olympus", "Camedia (2000)", l => { Ccd(l, 1024, 55); l.Aberration = 2; l.Blown = 0.45f; l.Noise = 0.15f; }),
        P("Olympus", "Trip 35", l => { Film(l, 0.35f); l.Temperature = 0.2f; l.Vignette = 0.5f; l.Blur = 0.3f; }),
        P("Olympus", "Pen half-frame", l => { Film(l, 0.5f); l.Aspect = AspectRatio.Classic4x3; l.Fade = 0.1f; l.Vignette = 0.4f; }),

        // ── Panasonic ──
        P("Panasonic", "Lumix (2005)", l => { Ccd(l, 1400, 65); l.Sharpen = 1.5f; l.Noise = 0.2f; }),
        P("Panasonic", "VHS-C camcorder", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 330; l.Blur = 0.9f; l.ChromaBleed = 0.75f; l.ColorShift = 3; l.Jitter = 0.35f;
            l.Tracking = 0.3f; l.Noise = 0.3f; l.Stamp = StampKind.VhsRec; l.StampText = "MAY.12 1991"; l.Audio = AudioLook.CamcorderMic;
        }),

        // ── JVC ──
        P("JVC", "VHS-C (1985)", l =>
        {
            l.Aspect = AspectRatio.Classic4x3; l.Resolution = 300; l.Blur = 1.1f; l.ChromaBleed = 0.9f; l.ColorShift = 4; l.Jitter = 0.5f;
            l.Tracking = 0.5f; l.Noise = 0.4f; l.Fade = 0.1f; l.Saturation = 0.9f; l.Stamp = StampKind.VhsRec; l.StampText = "OCT.26 1985";
            l.Audio = AudioLook.OldRadio;
        }),
        P("JVC", "Everio hard-drive camcorder", l => { Camcorder(l, 640, "AUG.09 2006"); l.Jpeg = 22; l.Interlace = 0.4f; l.Sharpen = 1f; }),

        // ── Nokia ──
        P("Nokia", "7650 — first camera phone", l => { Phone(l, 160, 20); l.Saturation = 0.7f; }),
        P("Nokia", "3650", l => { Phone(l, 320, 20); l.Tint = 0.2f; }),
        P("Nokia", "N95 (5 megapixel)", l => { Phone(l, 1600, 60); l.Blur = 0; l.Sharpen = 1.2f; l.Noise = 0.2f; l.ChromaNoise = 0.15f; l.Vignette = 0.15f; l.Saturation = 1.05f; }),

        // ── Motorola ──
        P("Motorola", "RAZR V3 (VGA)", l => { Phone(l, 480, 20); l.Blur = 0.6f; l.Tint = -0.2f; l.Vignette = 0.35f; }),

        // ── Apple ──
        P("Apple", "iPhone (2007)", l => { Phone(l, 1200, 55); l.Blur = 0.3f; l.Noise = 0.25f; l.ChromaNoise = 0.15f; l.Saturation = 0.95f; l.Blown = 0.35f; }),
        P("Apple", "iPod nano video camera", l => { Phone(l, 640, 30); l.Fisheye = 0.08f; l.FrameRate = 30; }),

        // ── Samsung ──
        P("Samsung", "Flip phone (2005)", l => { Phone(l, 320, 18); l.Temperature = -0.2f; l.Sharpen = 1.2f; }),

        // ── Nintendo ──
        P("Nintendo", "Game Boy Advance screen", l =>
        {
            l.Resolution = 240; l.Aspect = AspectRatio.Photo3x2; l.Upscale = Upscale.Pixelated; l.Palette = PaletteKind.Posterize; l.Levels = 32;
            l.Gamma = 0.75f; l.Saturation = 0.85f; l.Scanlines = 0.2f;
        }),
        P("Nintendo", "Virtual Boy (red)", l =>
        {
            l.Resolution = 384; l.Aspect = AspectRatio.Photo3x2; l.Upscale = Upscale.Pixelated; l.Palette = PaletteKind.VirtualBoy; l.Dither = DitherKind.Pattern;
            l.Contrast = 1.4f;
        }),

        // ── Polaroid ──
        P("Polaroid", "SX-70", l =>
        {
            l.Aspect = AspectRatio.Square; l.Resolution = 900; l.Fade = 0.2f; l.Temperature = 0.2f; l.Tint = 0.1f; l.Contrast = 0.85f;
            l.Saturation = 0.75f; l.Blur = 0.8f; l.Border = BorderKind.Polaroid; l.Vignette = 0.3f;
        }),
        P("Polaroid", "Expired instant film", l =>
        {
            l.Aspect = AspectRatio.Square; l.Resolution = 800; l.Fade = 0.3f; l.Tint = 0.25f; l.Saturation = 0.6f; l.LightLeak = 0.35f;
            l.Blur = 0.9f; l.Border = BorderKind.Polaroid; l.Vignette = 0.5f; l.Dust = 0.3f;
        }),

        // ── Lomography ──
        P("Lomography", "Diana F+", l => { Film(l, 0.45f); l.Aspect = AspectRatio.Square; l.Blur = 1.4f; l.Vignette = 1; l.LightLeak = 0.45f; l.Saturation = 1.3f; }),
        P("Lomography", "Holga", l => { Film(l, 0.5f); l.Aspect = AspectRatio.Square; l.Blur = 1.1f; l.Vignette = 1; l.Aberration = 3; l.Contrast = 1.3f; l.LightLeak = 0.3f; }),
        P("Lomography", "Fisheye One", l => { Film(l, 0.4f); l.Aspect = AspectRatio.Square; l.Fisheye = 1.1f; l.FisheyeCircle = true; l.Saturation = 1.3f; }),

        // ── Microsoft ──
        P("Microsoft", "Xbox Live Vision webcam", l => { Phone(l, 320, 25); l.Blur = 0.7f; l.FrameRate = 15; l.Tint = -0.1f; }),
    ];
}
