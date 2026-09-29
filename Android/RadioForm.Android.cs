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

    /// <summary>The first time, the radio starts with the phone's music folder in its list.</summary>
    internal void AddLibraryIfEmpty(string musicDir)
    {
        if (tracks.Count > 0 || !Directory.Exists(musicDir)) return;
        AddPaths([musicDir], playFirstNew: false);
    }
}
