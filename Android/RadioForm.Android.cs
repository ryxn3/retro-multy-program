using RetroRadio.Droid;

namespace RetroRadio;

/// <summary>The few things the radio does differently on a touch screen.</summary>
sealed partial class RadioForm
{
    /// <summary>Keys and knobs react the moment they're touched; the menus and lists on the display scroll with a swipe.</summary>
    protected internal override TouchMode TouchModeAt(Point p)
    {
        var lp = ToLogical(p);
        if (HitTest(lp) != null) return TouchMode.Press;
        bool menu = listMode || settingsMode || scMode || eqMode;
        return menu && Glass.Contains(lp) ? TouchMode.Scroll : TouchMode.Press;
    }

    /// <summary>True while the radio waits for typing (search), so the phone shows its keyboard.</summary>
    internal bool WantsTextInput => searching || (scMode && scTyping);

    /// <summary>Saves the playlist and settings now: Android may close a background app without asking.</summary>
    internal void SaveNow()
    {
        if (!Shutting) SaveState();
    }

    /// <summary>The radio's size in pixels at 96 DPI, so the phone can pick the scale that fills its screen before the first (slow) faceplate render.</summary>
    internal SizeF SizeAt96()
    {
        float s = SizeScales[sizeIdx];
        return miniMode ? new SizeF((Glass.Width + MiniPad * 2) * s, (Glass.Height + MiniPad * 2) * s) : new SizeF(BW * s, BH * s);
    }

    /// <summary>Phones run at 30 frames a second unless the user picks otherwise: smooth enough, and much cooler and longer on battery.</summary>
    internal void UsePhoneDefaults(string markerFile)
    {
        if (File.Exists(markerFile)) return;
        frameRate = 2; // 30 FPS
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(markerFile)!);
            File.WriteAllText(markerFile, "1");
        }
        catch (IOException) { }
    }

    /// <summary>The first time, the radio starts with the phone's music folder in its list.</summary>
    internal void AddLibraryIfEmpty(string musicDir)
    {
        if (tracks.Count > 0 || !Directory.Exists(musicDir)) return;
        AddPaths([musicDir], playFirstNew: false);
    }
}
