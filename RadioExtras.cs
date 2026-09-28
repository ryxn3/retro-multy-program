using System.Drawing.Drawing2D;

namespace RetroRadio;

/// <summary>
/// Sound settings (EQ, loudness, reverb, crossfade, "outside the car"), the settings sections, sleep timer,
/// smart shuffle, search, BPM, the idle clock, two-colour VFD, bass pulse, automatic night mode, the modern
/// radios' flip-down screen, the CD-slot animation, button/knob sounds, voice lines, mini mode,
/// the tray icon and start-with-Windows.
/// </summary>
sealed partial class RadioForm
{
    // ───────────────────────────── sound ─────────────────────────────

    static readonly (string Name, float Bass, float Mid, float Treble)[] EqPresets =
    [
        ("FLAT", 0, 0, 0), ("ROCK", 5, -1, 4), ("POP", -1, 3, 2), ("JAZZ", 3, 0, 2), ("CLUB", 7, 0, 4),
        ("JDM BASS", 10, -2, 3), ("VOCAL", -3, 4, 1), ("CUSTOM", 0, 0, 0),
    ];
    static readonly int[] CrossfadeSteps = [0, 2, 4, 6, 8, 10];
    static readonly string[] ReverbNames = ["OFF", "ROOM", "HALL", "GARAGE", "ECHO"];

    int eqPreset;
    float eqBass, eqMid, eqTreble;
    bool loudness, outsideCar;
    ReverbMode reverbMode;
    int crossfadeIdx;
    bool eqMode;
    int eqBand; // 0 bass, 1 mid, 2 treble, 3 loudness

    void ApplySound()
    {
        engine.Sound.Bass = eqBass;
        engine.Sound.Mid = eqMid;
        engine.Sound.Treble = eqTreble;
        engine.Sound.Loudness = loudness;
        engine.Sound.Muffle = outsideCar;
        engine.Sound.Reverb = reverbMode;
        engine.CrossfadeSeconds = CrossfadeSteps[crossfadeIdx];
    }

    void SetEqPreset(int i)
    {
        eqPreset = Wrap(i, EqPresets.Length);
        if (EqPresets[eqPreset].Name == "CUSTOM") return;
        (_, eqBass, eqMid, eqTreble) = EqPresets[eqPreset];
        ApplySound();
    }

    void AdjustBand(int band, float delta)
    {
        switch (band)
        {
            case 0: eqBass = Math.Clamp(eqBass + delta, -12, 12); break;
            case 1: eqMid = Math.Clamp(eqMid + delta, -12, 12); break;
            case 2: eqTreble = Math.Clamp(eqTreble + delta, -12, 12); break;
            default: loudness = !loudness; break;
        }
        if (band < 3) eqPreset = Array.FindIndex(EqPresets, p => p.Name == "CUSTOM");
        ApplySound();
        KnobClick();
    }

    void OpenEq()
    {
        eqMode = true;
        settingsMode = listMode = scMode = false;
    }

    // ───────────────────────────── button and knob sounds ─────────────────────────────

    readonly ClickSounds clicks = new();
    string buttonSound = ClickSounds.Off, knobSound = ClickSounds.Off;
    int clickVolume = 6;

    void ButtonClick() => clicks.Play(buttonSound, false, clickVolume / 10f);
    void KnobClick() => clicks.Play(knobSound, true, clickVolume / 10f);

    // ───────────────────────────── repainting ─────────────────────────────

    // 0 = AUTO (60 while playing, 30 when idle), 1 = always 60, 2 = always 30.
    int frameRate;
    bool memoryTidied;
    static readonly string[] FrameRateNames = ["AUTO", "60 FPS", "30 FPS"];

    FaceplateRenderer.Live? lastLive;
    object? lastFaceKey;

    /// <summary>
    /// Each frame only the display (and the few faceplate parts that are animating) is repainted; the whole
    /// radio is redrawn only when something on the faceplate changed. Far less to draw and to hand to Windows.
    /// </summary>
    void InvalidateChanged()
    {
        var live = LiveState() with { Now = 0 };
        var faceKey = (faceCache, paletteIdx, night, brightness, miniMode, Design);
        bool whole = miniMode || Shutting || Booting || cdAt >= 0 || bassPulse && player.IsPlaying
            || live != lastLive || !Equals(faceKey, lastFaceKey);
        lastLive = live;
        lastFaceKey = faceKey;
        if (whole)
        {
            Invalidate();
            return;
        }
        Invalidate(Pixels(RectangleF.Inflate(Glass, 4, 4)));
        // The play key's breathing glow and the blinking security LED move on their own.
        if (player.IsPlaying && Design.GlowRings)
            foreach (var k in buttons)
                if (k.Id == Btn.Play) Invalidate(Pixels(RectangleF.Inflate(new RectangleF(k.X, k.Y, k.W, k.H), 16, 14)));
        if (!player.IsPlaying) Invalidate(Pixels(new RectangleF(Design.TheftLed.X - 10, Design.TheftLed.Y - 10, 27, 27)));
    }

    Rectangle Pixels(RectangleF r) => Rectangle.Round(new RectangleF(r.X * S, r.Y * S, r.Width * S, r.Height * S));

    // ───────────────────────────── settings sections ─────────────────────────────

    static readonly (string Section, string[] Names)[] Sections =
    [
        ("RADIO", ["RADIO MODEL", "IMPORT RADIO", "COLOR", "VISUALIZER", "LIGHTING", "AUTO NIGHT", "BRIGHTNESS", "TWO-COLOR", "BASS PULSE", "FALL SPEED", "PEAK HOLD", "FLIP SCREEN", "WINDOW SIZE"]),
        ("SOUND", ["EQUALIZER", "EQ PRESET", "LOUDNESS", "REVERB", "OUTSIDE CAR", "CROSSFADE", "PLAY SPEED", "BUTTON SOUND", "KNOB SOUND", "CLICK VOLUME"]),
        ("PLAYBACK", ["REPEAT", "SHUFFLE", "SMART SHUFFLE", "SEEK STEP", "TIME DISPLAY", "SLEEP TIMER", "BPM", "LYRICS", "SEARCH", "CLEAR PLAYLIST"]),
        ("DISPLAY", ["LANGUAGE", "JDM STYLE", "CLOCK WHEN IDLE", "IDLE DEMO", "DISPLAY VIDEO", "VOICE LINES", "VOICE EVERY"]),
        ("POWER", ["STARTUP ANIM", "SHUTDOWN ANIM", "STARTUP SOUND", "SHUTDOWN SOUND", "SOUND VOLUME", "CUSTOM SOUND"]),
        ("SYSTEM", ["FRAME RATE", "MINI MODE", "ALWAYS ON TOP", "TRAY ICON", "START WITH WINDOWS"]),
        ("ONLINE", ["FILES ONLY", "MUSIC SERVICE", "ONLINE MUSIC", "INTERNET RADIO"]),
    ];

    // ───────────────────────────── files only ─────────────────────────────

    /// <summary>
    /// Files only: no Spotify, SoundCloud, internet radio or online lyrics — the radio just plays your own
    /// music files and never goes online.
    /// </summary>
    bool filesOnly;

    void SetFilesOnly(bool on)
    {
        filesOnly = on;
        shownSettings = null; // the ONLINE section changes
        if (!on) return;
        scMode = false;
        bool wasOnline = current >= 0 && current < tracks.Count && IsOnline(tracks[current]);
        LeaveSpotify();
        playToken++; // cancels anything still connecting
        if (wasOnline)
        {
            engine.Eject();
            stopped = true;
        }
    }

    /// <summary>With files only on, says so and returns true (the online screen doesn't open).</summary>
    bool BlockedOffline()
    {
        if (!filesOnly) return false;
        Flash("FILES ONLY - ONLINE IS OFF", 2.5);
        return true;
    }

    /// <summary>The next of your own files from <paramref name="i"/> on (wrapping), or -1 if the list has none.</summary>
    int NextLocal(int i)
    {
        for (int k = 0; k < tracks.Count; k++)
        {
            int j = (i + k) % tracks.Count;
            if (!IsOnline(tracks[j])) return j;
        }
        return -1;
    }

    string? settingsSection;
    List<Setting>? shownSettings;
    string? shownFor = "\0";

    /// <summary>What the settings menu lists now: the sections, or one section's settings plus BACK.</summary>
    List<Setting> settings
    {
        get
        {
            if (shownSettings != null && shownFor == settingsSection) return shownSettings;
            shownFor = settingsSection;
            if (settingsSection == null)
            {
                shownSettings = [.. Sections.Select(s => new Setting(s.Section, () => ">", _ => EnterSection(s.Section))),
                    allSettings.First(s => s.Name == "EXIT")];
            }
            else
            {
                var names = Sections.First(s => s.Section == settingsSection).Names;
                if (filesOnly && settingsSection == "ONLINE") names = ["FILES ONLY"];
                shownSettings = [.. names.Select(n => allSettings.FirstOrDefault(s => s.Name == n)).Where(s => s != null).Select(s => s!),
                    new Setting("BACK", () => "", _ => LeaveSection())];
            }
            return shownSettings;
        }
    }

    void EnterSection(string s)
    {
        settingsSection = s;
        setCursor = setTop = 0;
    }

    void LeaveSection()
    {
        int i = Array.FindIndex(Sections, x => x.Section == settingsSection);
        settingsSection = null;
        setCursor = Math.Max(0, i);
        setTop = 0;
    }

    /// <summary>The settings added in this part (merged into the sections above).</summary>
    List<Setting> ExtraSettings() =>
    [
        // Sound.
        new("EQUALIZER", () => EqPresets[eqPreset].Name, _ => OpenEq()),
        new("EQ PRESET", () => EqPresets[eqPreset].Name, d => SetEqPreset(eqPreset + d)),
        new("LOUDNESS", () => OnOff(loudness), _ => { loudness = !loudness; ApplySound(); }),
        new("REVERB", () => ReverbNames[(int)reverbMode], d => { reverbMode = (ReverbMode)Wrap((int)reverbMode + d, ReverbNames.Length); ApplySound(); }),
        new("OUTSIDE CAR", () => OnOff(outsideCar), _ => { outsideCar = !outsideCar; ApplySound(); }),
        new("CROSSFADE", () => crossfadeIdx == 0 ? "OFF" : $"{CrossfadeSteps[crossfadeIdx]} SEC", d =>
        {
            crossfadeIdx = Wrap(crossfadeIdx + d, CrossfadeSteps.Length);
            ApplySound();
        }),
        new("BUTTON SOUND", () => ClickSounds.Label(buttonSound), d =>
        {
            var c = ClickSounds.Choices(false);
            buttonSound = c[Wrap(Math.Max(0, Array.IndexOf(c, buttonSound)) + d, c.Length)];
            ButtonClick();
        }),
        new("KNOB SOUND", () => ClickSounds.Label(knobSound), d =>
        {
            var c = ClickSounds.Choices(true);
            knobSound = c[Wrap(Math.Max(0, Array.IndexOf(c, knobSound)) + d, c.Length)];
            KnobClick();
        }),
        new("CLICK VOLUME", () => $"{clickVolume * 10}%", d =>
        {
            clickVolume = Math.Clamp(clickVolume + d, 1, 10);
            ButtonClick();
        }),

        // Playback.
        new("SMART SHUFFLE", () => OnOff(smartShuffle), _ => smartShuffle = !smartShuffle),
        new("SLEEP TIMER", () => sleepAt > 0 ? $"{Math.Ceiling((sleepAt - now) / 60)} MIN" : "OFF", d =>
        {
            sleepIdx = Wrap(sleepIdx + d, SleepSteps.Length);
            sleepAt = SleepSteps[sleepIdx] == 0 ? -1 : now + SleepSteps[sleepIdx] * 60;
        }),
        new("BPM", () => OnOff(bpmOn), _ => bpmOn = !bpmOn),
        new("SEARCH", () => "", _ => StartSearch()),

        // Radio / display.
        new("AUTO NIGHT", () => autoNight ? "SUNSET" : "OFF", _ =>
        {
            autoNight = !autoNight;
            nextNightCheck = 0;
        }),
        new("TWO-COLOR", () => OnOff(twoColor), _ => twoColor = !twoColor),
        new("BASS PULSE", () => OnOff(bassPulse), _ => bassPulse = !bassPulse),
        new("FLIP SCREEN", () => Design.Screen == ScreenKind.Modern ? OnOff(flipScreen) : "-", _ =>
        {
            flipScreen = !flipScreen;
            if (flipScreen && Design.Screen == ScreenKind.Modern) ReplayBoot();
        }),
        new("CLOCK WHEN IDLE", () => OnOff(clockIdle), _ => clockIdle = !clockIdle),
        new("VOICE LINES", () => OnOff(voiceOn), _ =>
        {
            voiceOn = !voiceOn;
            if (voiceOn) SayVoice(VoiceLines.Greeting(DateTime.Now));
            else voice.Stop();
        }),
        new("VOICE EVERY", () => $"{VoiceSteps[voiceEvery]} MIN", d =>
        {
            voiceEvery = Wrap(voiceEvery + d, VoiceSteps.Length);
            ScheduleVoice();
        }),

        // System.
        new("FRAME RATE", () => FrameRateNames[frameRate], d => frameRate = Wrap(frameRate + d, FrameRateNames.Length)),
        new("MINI MODE", () => OnOff(miniMode), _ => SetMini(!miniMode)),
#if !MAC
        new("TRAY ICON", () => OnOff(trayOn), _ => SetTray(!trayOn)),
        new("START WITH WINDOWS", () => OnOff(StartsWithWindows), _ => StartsWithWindows = !StartsWithWindows),
#endif

        // Online.
        new("FILES ONLY", () => OnOff(filesOnly), _ => SetFilesOnly(!filesOnly)),
        new("INTERNET RADIO", () => ">", _ => OpenInternetRadio()),
    ];

    // ───────────────────────────── playback helpers ─────────────────────────────

    static readonly int[] SleepSteps = [0, 15, 30, 45, 60, 90];
    int sleepIdx;
    double sleepAt = -1;
    bool smartShuffle = true;
    readonly Queue<int> recentlyPlayed = new();

    static string ArtistOf(string title)
    {
        int dash = title.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? title[..dash].Split(',')[0].Trim().ToLowerInvariant() : "";
    }

    /// <summary>A random next track that isn't by the same artist and wasn't played recently.</summary>
    int SmartPick()
    {
        string artist = current >= 0 && current < tracks.Count ? ArtistOf(TitleOf(tracks[current])) : "";
        int keep = Math.Min(20, tracks.Count / 2);
        for (int attempt = 0; attempt < 30; attempt++)
        {
            int n = rng.Next(tracks.Count);
            if (n == current) continue;
            if (attempt < 25 && recentlyPlayed.Contains(n) && tracks.Count > 2) continue;
            if (attempt < 20 && artist.Length > 0 && ArtistOf(TitleOf(tracks[n])) == artist) continue;
            return n;
        }
        int m;
        do m = rng.Next(tracks.Count); while (m == current && tracks.Count > 1);
        return m;
    }

    void RememberPlayed(int i)
    {
        recentlyPlayed.Enqueue(i);
        while (recentlyPlayed.Count > Math.Max(1, Math.Min(20, tracks.Count / 2))) recentlyPlayed.Dequeue();
    }

    // Search: typing in the track list narrows it down.
    bool searching;
    string listFilter = "";

    /// <summary>The playlist as the list shows it: every track, or those matching the search.</summary>
    List<int> ListView
    {
        get
        {
            if (listFilter.Length == 0) return [.. Enumerable.Range(0, tracks.Count)];
            string q = listFilter.ToLowerInvariant();
            return [.. Enumerable.Range(0, tracks.Count).Where(i => TitleOf(tracks[i]).ToLowerInvariant().Contains(q))];
        }
    }

    void StartSearch()
    {
        listMode = true;
        settingsMode = scMode = eqMode = false;
        searching = true;
        listFilter = "";
        listCursor = listTop = 0;
    }

    void EndSearch(bool keepFilter = false)
    {
        searching = false;
        if (!keepFilter) listFilter = "";
    }

    // BPM.
    bool bpmOn;
    float bpmShown;
    double bpmNext;

    string BpmText
    {
        get
        {
            if (!bpmOn || !engine.IsPlaying || spotify.Active) return "";
            if (now >= bpmNext)
            {
                bpmNext = now + 1.5;
                bpmShown = engine.Bpm.Estimate();
            }
            return bpmShown > 0 ? $"{bpmShown:0} BPM" : "";
        }
    }

    // ───────────────────────────── display extras ─────────────────────────────

    bool clockIdle = true, twoColor, bassPulse, autoNight, flipScreen = true;
    double lastActivity;
    Inks? inksAlt;

    /// <summary>Two-colour VFD: the text zones glow in the accent colour, the spectrum in the main one.</summary>
    Inks TextInks(Inks ink)
    {
        if (!twoColor) return ink;
        var pal = Palettes[paletteIdx];
        if (inksAlt == null || inksAlt.Palette.Name != pal.Name + "~" || inksAlt.Low != ink.Low)
        {
            inksAlt?.Dispose();
            inksAlt = new Inks(new Palette(pal.Name + "~", pal.Accent, pal.Main), ink.Low);
        }
        return inksAlt;
    }

    bool ShowIdleClock => clockIdle && !player.IsPlaying && !(spotify.Active && spotify.IsPlaying) && !Booting
        && now - lastActivity > 3 && !listMode && !settingsMode && !scMode && !eqMode;

    /// <summary>A big dot-matrix clock with the date, while nothing is playing.</summary>
    void DrawBigClock(Graphics g, Inks ink)
    {
        var t = DateTime.Now;
        string hm = t.ToString("HH") + ((int)(now * 2) % 2 == 0 ? ":" : " ") + t.ToString("mm");
        const float p = 7.2f;
        float w = DotFont.Width(hm, p);
        DotText(g, ink.Main, hm, (GW - w) / 2, 76, p);
        string sec = t.ToString("ss");
        DotText(g, ink.Accent, sec, (GW + w) / 2 + 8, 76 + 7 * p - 7 * 2.4f, 2.4f);
        var culture = Lang.Current switch
        {
            Language.German => "de-DE", Language.Spanish => "es-ES", Language.French => "fr-FR", Language.Polish => "pl-PL",
            Language.Swedish => "sv-SE", Language.Russian => "ru-RU", Language.Japanese => "ja-JP", _ => "en-GB",
        };
        string date = DotFont.Normalize(t.ToString(Jdm ? "yyyy年M月d日 dddd" : "dddd d MMMM yyyy", new System.Globalization.CultureInfo(Jdm ? "ja-JP" : culture)));
        DotText(g, ink.Main, date, (GW - DotFont.Width(date, 2f)) / 2, 76 + 7 * p + 16, 2f);
    }

    /// <summary>Bass pulse: the keys' backlight throbs with the kick drum.</summary>
    void DrawBassPulse(Graphics g)
    {
        if (!bassPulse || levels.Length < 3 || !player.IsPlaying && !(spotify.Active && spotify.IsPlaying)) return;
        float bass = Math.Clamp((levels[0] + levels[1] + levels[2]) / 3 * 1.3f - 0.25f, 0, 1);
        if (bass <= 0.02f) return;
        var c = Palettes[paletteIdx].Main;
        using var glow = new SolidBrush(Color.FromArgb((int)(bass * 70), c));
        using var edge = new Pen(Color.FromArgb((int)(bass * 170), c), 1.4f);
        foreach (var k in buttons)
        {
            var r = RectangleF.Inflate(new RectangleF(k.X, k.Y, k.W, k.H), 2, 2);
            if (k.Kind == KeyKind.Knob)
            {
                g.DrawEllipse(edge, r);
                continue;
            }
            using var path = RoundRect(r, Math.Min(6, Math.Min(r.Width, r.Height) / 2));
            g.FillPath(glow, path);
            g.DrawPath(edge, path);
        }
    }

    // Automatic night mode at sunset.
    double nextNightCheck;

    void CheckAutoNight()
    {
        if (!autoNight || now < nextNightCheck) return;
        nextNightCheck = now + 60;
        bool dark = !Sun.IsDaylight(DateTime.Now);
        if (dark == night) return;
        night = dark;
        if (IsHandleCreated) RebuildFaceplates();
    }

    /// <summary>
    /// The modern radios' motorised screen: it folds up from its hinge at power-on and back down at power-off.
    /// Returns how far it's open (1 = fully).
    /// </summary>
    float FlipOpen()
    {
        if (!flipScreen || Design.Screen != ScreenKind.Modern) return 1;
        if (Shutting)
        {
            double c = now - shutdownAt - ShutCollapse;
            return c <= 0 ? 1 : (float)Math.Max(0, 1 - c / 0.7);
        }
        if (!bootAnim) return 1;
        double t = (now - 0.1) / 0.95;
        if (t >= 1) return 1;
        if (t <= 0) return 0;
        // Ease out with a small bounce as the motor stops.
        return (float)(1 - Math.Pow(1 - t, 3) + Math.Sin(t * Math.PI) * 0.06 * (1 - t));
    }

    // ───────────────────────────── the CD slot swallowing a disc ─────────────────────────────

    double cdAt = -1;
    PointF cdFrom;

    void StartCdAnimation(PointF from)
    {
        cdAt = now;
        cdFrom = from;
    }

    void DrawCdAnimation(Graphics g)
    {
        if (cdAt < 0) return;
        double t = now - cdAt;
        if (t > 0.95)
        {
            cdAt = -1;
            return;
        }
        var target = Design.ShowSlot
            ? new PointF(Design.Slot.R.X + Design.Slot.R.Width / 2, Design.Slot.R.Y + Design.Slot.R.Height / 2)
            : new PointF(Glass.X + Glass.Width / 2, Glass.Y - 4);
        float r = Design.ShowSlot ? Math.Min(60, Design.Slot.R.Width / 2 - 6) : 44;
        PointF c;
        float squash = 1;
        if (t < 0.45)
        {
            float u = (float)(t / 0.45);
            u = u * u * (3 - 2 * u);
            c = new PointF(cdFrom.X + (target.X - cdFrom.X) * u, cdFrom.Y + (target.Y - r * 0.6f - cdFrom.Y) * u);
        }
        else
        {
            // Into the slot: the disc tips flat and slides in.
            float u = (float)((t - 0.45) / 0.45);
            c = new PointF(target.X, target.Y - r * 0.6f * (1 - u));
            squash = Math.Max(0.04f, 1 - u);
            if (t >= 0.9) return;
        }
        var box = new RectangleF(c.X - r, c.Y - r * squash, r * 2, r * 2 * squash);
        using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillEllipse(shadow, box.X + 3, box.Y + 5, box.Width, box.Height);
        using (var disc = new LinearGradientBrush(box, Color.FromArgb(235, 238, 242), Color.FromArgb(150, 156, 166), 45f)) g.FillEllipse(disc, box);
        // Rainbow sheen, like light on a real CD.
        Color[] rainbow = [Color.FromArgb(90, 255, 80, 80), Color.FromArgb(90, 255, 220, 80), Color.FromArgb(90, 80, 255, 140), Color.FromArgb(90, 80, 170, 255), Color.FromArgb(90, 200, 90, 255)];
        for (int i = 0; i < rainbow.Length; i++)
            using (var b = new SolidBrush(rainbow[i])) g.FillPie(b, box.X, box.Y, box.Width, box.Height, 200 + i * 14 + (float)(t * 90), 12);
        var hole = new RectangleF(c.X - r * 0.16f, c.Y - r * 0.16f * squash, r * 0.32f, r * 0.32f * squash);
        using (var hb = new SolidBrush(Color.FromArgb(40, 42, 46))) g.FillEllipse(hb, hole);
        using var edge = new Pen(Color.FromArgb(120, 255, 255, 255), 1);
        g.DrawEllipse(edge, box);
    }

    // ───────────────────────────── voice lines ─────────────────────────────

    static readonly int[] VoiceSteps = [5, 10, 15, 30, 60];
    bool voiceOn = true;
    int voiceEvery = 2;
    double nextVoiceAt = -1, voiceEndsAt = -1, greetAt = -1;
    VoiceLine? lastVoice;
    bool saidTurbo, saidAlmost;
    readonly VoicePlayer voice = new();

    void ScheduleVoice() => nextVoiceAt = now + VoiceSteps[voiceEvery] * 60 * (0.8 + rng.NextDouble() * 0.4);

    /// <summary>Speaks a line, turns the music down under it, and shows it on the display.</summary>
    void SayVoice(VoiceLine line)
    {
        if (!voiceOn || engine.Muted) return;
        try
        {
            double len = voice.Play(line, ChimeVolume ?? Math.Max(0.5f, engine.Volume));
            if (len <= 0) return;
            lastVoice = line;
            voiceEndsAt = now + len + 0.3;
            engine.Duck = 0.35f;
            if (spotify.Active) spotify.SetVolume(engine.Muted ? 0 : engine.Volume * 0.4f);
            Flash(Jdm ? line.Japanese : line.English, Math.Max(2.2, len + 0.6));
        }
        catch (Exception) { }
    }

    /// <summary>Called every frame: the greeting after power-on, lines now and then, and un-ducking the music.</summary>
    void UpdateVoice()
    {
        if (voiceEndsAt > 0 && now >= voiceEndsAt)
        {
            voiceEndsAt = -1;
            engine.Duck = 1;
            if (spotify.Active) spotify.SetVolume(engine.Muted ? 0 : engine.Volume);
        }
        if (!voiceOn || Shutting || snapshotPath != null) return;
        if (greetAt > 0 && now >= greetAt)
        {
            greetAt = -1;
            SayVoice(VoiceLines.Greeting(DateTime.Now));
            ScheduleVoice();
            return;
        }
        if (voiceEndsAt > 0) return;
        bool playing = player.IsPlaying || (spotify.Active && spotify.IsPlaying);
        // Event lines.
        if (playing && engine.Speed >= 1.25f && !saidTurbo)
        {
            saidTurbo = true;
            SayVoice(VoiceLines.Get("15"));
            return;
        }
        if (engine.Speed < 1.1f) saidTurbo = false;
        if (playing && !saidAlmost && tracks.Count > 3 && current == tracks.Count - 1 && repeat == RepeatMode.Off && !shuffle
            && player.Duration.TotalSeconds > 60 && player.Duration - player.Position < TimeSpan.FromSeconds(40))
        {
            saidAlmost = true;
            SayVoice(VoiceLines.Get("18"));
            return;
        }
        // Now and then.
        if (nextVoiceAt < 0) ScheduleVoice();
        if (playing && now >= nextVoiceAt && !settingsMode && !scMode && !listMode && !eqMode)
        {
            SayVoice(VoiceLines.Pick(DateTime.Now, (DateTime.Now - sessionStart).TotalMinutes, lastVoice, rng));
            ScheduleVoice();
        }
    }

    readonly DateTime sessionStart = DateTime.Now;

    // ───────────────────────────── mini mode ─────────────────────────────

    bool miniMode;
    const float MiniPad = 8;

    /// <summary>Where the mini window starts, in faceplate units (just around the display).</summary>
    PointF MiniOrigin => miniMode ? new PointF(Glass.X - MiniPad, Glass.Y - MiniPad) : PointF.Empty;

    void SetMini(bool on)
    {
        if (on == miniMode) return;
        var screenAnchor = PointToScreen(Point.Empty);
        miniMode = on;
        settingsMode = false;
        ApplyScale();
        TopMost = miniMode || onTop;
        // Keep the display where it was on screen.
        var shift = new Size((int)((Glass.X - MiniPad) * S), (int)((Glass.Y - MiniPad) * S));
        Location = on ? screenAnchor + shift : screenAnchor - shift;
        Flash(on ? "MINI MODE" : Design.Name);
    }

    // ───────────────────────────── tray icon and start with Windows ─────────────────────────────

    bool trayOn;
#if !MAC
    NotifyIcon? tray;

    void SetTray(bool on)
    {
        trayOn = on;
        if (!on)
        {
            tray?.Dispose();
            tray = null;
            ShowInTaskbar = true;
            return;
        }
        if (tray != null) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add(Lang.T("PLAY / PAUSE"), null, (_, _) => Execute(Btn.Play));
        menu.Items.Add(Lang.T("NEXT"), null, (_, _) => Execute(Btn.Next));
        menu.Items.Add(Lang.T("PREVIOUS"), null, (_, _) => Execute(Btn.Prev));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Lang.T("SHOW RADIO"), null, (_, _) => ShowFromTray());
        menu.Items.Add(Lang.T("MINI MODE"), null, (_, _) => { ShowFromTray(); SetMini(!miniMode); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Lang.T("POWER OFF"), null, (_, _) => { ShowFromTray(); Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = "Retro Radio", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
    }

    void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>With the tray icon on, minimising tucks the radio into the tray.</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (tray != null && WindowState == FormWindowState.Minimized) Hide();
    }

    void UpdateTrayText()
    {
        if (tray == null) return;
        string t = string.IsNullOrEmpty(title) ? "Retro Radio" : title;
        tray.Text = t.Length > 63 ? t[..60] + "..." : t;
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static bool StartsWithWindows
    {
        get
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("RetroRadio") != null;
        }
        set
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("RetroRadio", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("RetroRadio", false);
        }
    }
#else
    void SetTray(bool on) => trayOn = false;
    void UpdateTrayText() { }
#endif

    // ───────────────────────────── internet radio ─────────────────────────────

    static readonly (string Label, string Kind, string Arg)[] RadioCategories =
    [
        ("TOP STATIONS", "top", ""), ("JAPAN", "country", "JP"), ("CITY POP", "tag", "city pop"), ("J-POP", "tag", "jpop"),
        ("ANIME", "tag", "anime"), ("LOFI", "tag", "lofi"), ("SYNTHWAVE", "tag", "synthwave"), ("80S", "tag", "80s"),
        ("JAZZ", "tag", "jazz"), ("ROCK", "tag", "rock"), ("ELECTRONIC", "tag", "electronic"), ("HIP HOP", "tag", "hiphop"),
    ];

    void OpenInternetRadio()
    {
        if (BlockedOffline()) return;
        scMode = true;
        listMode = settingsMode = eqMode = false;
        scTyping = false;
        scBack.Clear();
        var rows = new List<ScRow> { new("SEARCH STATIONS", "", () => BeginTyping("STATIONS")) };
        foreach (var (label, kind, arg) in RadioCategories)
            rows.Add(new(label, "", () => LoadStationsAsync(label, kind, arg)));
        rows.Add(new("EXIT", "", () => { scMode = false; return Task.CompletedTask; }));
        scTitle = "INTERNET RADIO";
        scRows = rows;
        scCursor = scTop = 0;
    }

    async Task LoadStationsAsync(string label, string kind, string arg)
    {
        var list = kind switch
        {
            "country" => await RadioBrowser.ByCountry(arg, ScToken),
            "tag" => await RadioBrowser.ByTag(arg, ScToken),
            "search" => await RadioBrowser.Search(arg, ScToken),
            _ => await RadioBrowser.Top(ScToken),
        };
        if (list.Count == 0)
        {
            Flash("NOTHING FOUND");
            return;
        }
        var rows = list.Select((s, i) => new ScRow(s.Name, $"{s.Country} {(s.Bitrate > 0 ? s.Bitrate + "K" : s.Codec)}".Trim(), () =>
        {
            // The whole list becomes the "presets": NEXT / PREV switch stations.
            tracks.Clear();
            tracks.AddRange(list.Select(RadioBrowser.Entry));
            scMode = false;
            PlayIndex(i);
            return Task.CompletedTask;
        })).ToList();
        PushView(label, rows);
    }

    /// <summary>Tunes in to a station; the song on air shows on the display as the station sends it.</summary>
    async void PlayStation(int index)
    {
        LeaveSpotify();
        int token = ++playToken;
        var (url, name) = RadioBrowser.Parse(tracks[index]);
        title = name;
        stopped = false;
        Flash("TUNING...", 30);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            ITrackSource src;
            try
            {
                var icy = await IcyStreamSource.OpenAsync(url, cts.Token);
                icy.TitleChanged += onAir => BeginInvoke(() =>
                {
                    if (token != playToken) return;
                    title = $"{name}  ·  {onAir}";
                    marqueeStart = now;
                });
                src = icy;
            }
            catch (NotSupportedException)
            {
                // Not MP3 (AAC and friends): ffmpeg if it's installed, else Windows' own decoder.
                string? ffmpeg = FfmpegLocator.Find();
                src = await Task.Run(() => ffmpeg != null
                    ? new LiveFallbackSource(new FfmpegTrackSource(ffmpeg, url, TimeSpan.Zero))
                    : new LiveFallbackSource(new UrlTrackSource(url, TimeSpan.Zero)));
            }
            if (token != playToken)
            {
                src.Dispose();
                return;
            }
            engine.Load(src);
            player.Play();
            Flash(Jdm ? "放送中" : "ON AIR", 1.5);
        }
        catch (Exception ex)
        {
            if (token != playToken) return;
            stopped = true;
            Flash(ex is ServiceException s ? s.Message : ex is HttpRequestException or TaskCanceledException ? "STATION NOT AVAILABLE" : "STATION NOT AVAILABLE", 3);
        }
    }

    // ───────────────────────────── EQ screen ─────────────────────────────

    const float EqTop = 70, EqBottom = 196;

    /// <summary>The tone controls on the display: three bars around 0 dB, plus loudness.</summary>
    void DrawEq(Graphics g, Inks ink)
    {
        string[] names = [Lang.T("BASS"), Lang.T("MID"), Lang.T("TREBLE")];
        float[] vals = [eqBass, eqMid, eqTreble];
        const int segs = 24;
        float colW = (GW - 150) / 3, mid = (EqTop + 14 + EqBottom - 14) / 2, segH = (EqBottom - EqTop - 34) / segs;
        for (int b = 0; b < 3; b++)
        {
            float cx = 26 + b * colW + colW / 2;
            bool sel = eqBand == b;
            // Segments grow up (boost) or down (cut) from the centre line.
            int lit = (int)Math.Round(vals[b] / 12 * segs / 2);
            for (int s = -segs / 2; s < segs / 2; s++)
            {
                float y = mid - (s + 1) * segH;
                bool on = lit > 0 ? s >= 0 && s < lit : lit < 0 && s < 0 && s >= lit;
                var r = new RectangleF(cx - 14, y + 1, 28, segH - 1.6f);
                (on ? (Math.Abs(s) >= segs / 2 - 3 ? dots2 : dots) : null)?.Add(r);
                if (!on) g.FillRectangle(ink.Ghost, r);
            }
            g.FillRectangle(sel ? ink.Hot : ink.Main, cx - 20, mid - 0.8f, 40, 1.6f);
            Fill(g, ink.Main, dots);
            Fill(g, ink.Accent, dots2);
            string label = DotFont.Normalize(names[b]);
            string val = (vals[b] > 0 ? "+" : "") + vals[b].ToString("0") + "DB";
            var lb = sel ? ink.Hot : ink.Main;
            DotText(g, lb, label, cx - DotFont.Width(label, 1.8f) / 2, EqBottom - 10, 1.8f);
            DotText(g, sel ? ink.Hot : ink.Accent, val, cx - DotFont.Width(val, 1.6f) / 2, EqTop + 2, 1.6f);
            if (sel) g.DrawRectangle(ink.MainPen, cx - colW / 2 + 6, EqTop - 3, colW - 12, EqBottom - EqTop + 12);
        }
        // Loudness switch.
        float lx = GW - 112;
        var box = new RectangleF(lx, mid - 16, 90, 30);
        string loud = DotFont.Normalize(Lang.T("LOUD"));
        if (loudness) g.FillRectangle(ink.Main, box); else g.DrawRectangle(eqBand == 3 ? ink.MainPen : ink.GhostPen, box.X, box.Y, box.Width, box.Height);
        DotText(g, loudness ? ink.Ink : eqBand == 3 ? ink.Hot : ink.Ghost, loud, box.X + (box.Width - DotFont.Width(loud, 2.2f)) / 2, box.Y + 7, 2.2f);
    }

    /// <summary>Clicks on the EQ screen: pick a band, and set it from where you clicked.</summary>
    bool EqClick(PointF p)
    {
        float colW = (GW - 150) / 3, mid = (EqTop + 14 + EqBottom - 14) / 2, half = (EqBottom - EqTop - 34) / 2;
        if (p.X > GW - 120 && p.Y > mid - 20 && p.Y < mid + 20)
        {
            eqBand = 3;
            AdjustBand(3, 0);
            return true;
        }
        int b = (int)((p.X - 26) / colW);
        if (b < 0 || b > 2 || p.Y < EqTop || p.Y > EqBottom) return false;
        eqBand = b;
        float v = (float)Math.Round(Math.Clamp((mid - p.Y) / half, -1, 1) * 12);
        float cur = b == 0 ? eqBass : b == 1 ? eqMid : eqTreble;
        AdjustBand(b, v - cur);
        return true;
    }

    bool EqKey(Keys key)
    {
        switch (key)
        {
            case Keys.Left: eqBand = Wrap(eqBand - 1, 4); KnobClick(); return true;
            case Keys.Right: eqBand = Wrap(eqBand + 1, 4); KnobClick(); return true;
            case Keys.Up: AdjustBand(eqBand, eqBand == 3 ? 0 : 1); return true;
            case Keys.Down: AdjustBand(eqBand, eqBand == 3 ? 0 : -1); return true;
            case Keys.Enter: if (eqBand == 3) AdjustBand(3, 0); return true;
            case Keys.Escape: case Keys.E: case Keys.C: eqMode = false; return true;
        }
        return false;
    }

    // ───────────────────────────── extra visualizers ─────────────────────────────

    /// <summary>A round spectrum: bars radiating from a ring, with the waveform wobbling inside it.</summary>
    void DrawCircle(Graphics g, Inks ink)
    {
        float cx = GW / 2, cy = 133, r0 = 34;
        int n = levels.Length;
        for (int i = 0; i < n; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI * 2 / n;
            float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            int segs = 9, on = (int)Math.Round(levels[i] * segs);
            for (int s = 0; s < segs; s++)
            {
                float d = r0 + 6 + s * 4.6f;
                var rect = new RectangleF(cx + ca * d * 1.6f - 1.6f, cy + sa * d - 1.6f, 3.2f, 3.2f);
                if (s < on) (s >= 7 ? dots2 : dots).Add(rect);
                else if (s == 0) g.FillRectangle(ink.Ghost, rect);
            }
        }
        Fill(g, ink.Main, dots);
        Fill(g, ink.Hot, dots2);
        int m = 96;
        for (int i = 0; i < m; i++)
        {
            double a = i * Math.PI * 2 / m;
            float v = wave.Length > 0 ? wave[i * wave.Length / m] : 0;
            if (!player.IsPlaying) v = (float)(0.05 * Math.Sin(now * 2 + i * 0.3));
            float d = r0 - 6 + Math.Clamp(v * 22, -12, 12);
            dots.Add(new RectangleF(cx + (float)Math.Cos(a) * d * 1.6f - 1.2f, cy + (float)Math.Sin(a) * d - 1.2f, 2.4f, 2.4f));
        }
        Fill(g, ink.Accent, dots);
    }

    float vuL, vuR, vuPeakL, vuPeakR;

    /// <summary>Two VU meters with swinging needles, drawn in dots.</summary>
    void DrawVu(Graphics g, Inks ink)
    {
        // Loudness of the two halves of the recent waveform, with VU-meter ballistics.
        float rms(int from, int to)
        {
            double s = 0;
            for (int i = from; i < to; i++) s += wave[i] * wave[i];
            return (float)Math.Sqrt(s / Math.Max(1, to - from));
        }
        float l = player.IsPlaying ? rms(0, wave.Length / 2) : 0, r = player.IsPlaying ? rms(wave.Length / 2, wave.Length) : 0;
        float Db(float v) => Math.Clamp((float)((20 * Math.Log10(v + 1e-5) + 34) / 31), 0, 1.08f); // -34..-3 dBFS, the range music lives in
        vuL += (Db(l) - vuL) * 0.18f;
        vuR += (Db(r) - vuR) * 0.18f;
        vuPeakL = Math.Max(vuL, vuPeakL - 0.01f);
        vuPeakR = Math.Max(vuR, vuPeakR - 0.01f);
        float w = (GW - 60) / 2;
        Meter(24 + w / 2, vuL, vuPeakL, "L");
        Meter(36 + w * 1.5f, vuR, vuPeakR, "R");

        void Meter(float cx, float v, float peak, string ch)
        {
            float py = 196, rad = 118;
            // Scale: an arc from -26 dB to +3 dB; the last part is red.
            for (int i = 0; i <= 30; i++)
            {
                float u = i / 30f;
                double a = Math.PI * (1.25 + 0.5 * u);
                var pt = new PointF(cx + (float)Math.Cos(a) * rad, py + (float)Math.Sin(a) * rad);
                bool major = i % 5 == 0;
                (u > 0.86f ? dots2 : dots).Add(new RectangleF(pt.X - 1.5f, pt.Y - (major ? 4 : 1.5f), 3, major ? 6 : 3));
            }
            Fill(g, ink.Main, dots);
            Fill(g, ink.Accent, dots2);
            double na = Math.PI * (1.25 + 0.5 * Math.Clamp(v, 0, 1.05f));
            for (float d = 18; d < rad - 6; d += 4.2f)
                dots.Add(new RectangleF(cx + (float)Math.Cos(na) * d - 1.4f, py + (float)Math.Sin(na) * d - 1.4f, 2.8f, 2.8f));
            Fill(g, v > 0.86f ? ink.Accent : ink.Hot, dots);
            DotText(g, ink.Main, ch, cx - DotFont.Width(ch, 2.4f) / 2, py - 36, 2.4f);
            if (peak > 0.86f) g.FillEllipse(ink.Accent, cx + 44, py - 40, 7, 7);
        }
    }

    double cdAngle;

    /// <summary>A spinning disc: rings of dots with a highlight sweeping round, pulsing with the bass.</summary>
    void DrawCd(Graphics g, Inks ink)
    {
        if (player.IsPlaying) cdAngle += 0.16 * engine.Speed;
        float cx = GW / 2, cy = 134;
        float bass = levels.Length > 2 ? (levels[0] + levels[1] + levels[2]) / 3 : 0;
        for (int ring = 0; ring < 7; ring++)
        {
            float rad = 22 + ring * 9;
            int n = 18 + ring * 7;
            for (int i = 0; i < n; i++)
            {
                double a = i * Math.PI * 2 / n + cdAngle * (1 - ring * 0.02);
                double sweep = Math.Cos(a - cdAngle * 0.2 - 0.8);
                var rect = new RectangleF(cx + (float)Math.Cos(a) * rad * 1.5f - 1.3f, cy + (float)Math.Sin(a) * rad - 1.3f, 2.6f, 2.6f);
                if (sweep > 0.92) dots2.Add(rect);
                else if (sweep > 0.3 - bass * 0.6) dots.Add(rect);
                else g.FillRectangle(ink.Ghost, rect);
            }
        }
        Fill(g, ink.Main, dots);
        Fill(g, ink.Hot, dots2);
        g.FillEllipse(ink.Accent, cx - 7, cy - 5, 14, 10);
    }

    float[,]? fire;

    /// <summary>Flames rising from the spectrum.</summary>
    void DrawFire(Graphics g, Inks ink)
    {
        const int cols = 60, rows = 26;
        fire ??= new float[cols, rows];
        var f = fire;
        int n = levels.Length;
        for (int x = 0; x < cols; x++)
        {
            float lv = levels[Math.Min(n - 1, x * n / cols)];
            f[x, 0] = Math.Clamp(lv * 1.25f + (float)rng.NextDouble() * 0.25f * lv, 0, 1);
        }
        for (int y = rows - 1; y > 0; y--)
            for (int x = 0; x < cols; x++)
            {
                float a = f[x, y - 1], b = f[Math.Max(0, x - 1), y - 1], c = f[Math.Min(cols - 1, x + 1), y - 1];
                f[x, y] = Math.Max(0, (a * 2 + b + c) / 4 - 0.035f - (float)rng.NextDouble() * 0.03f);
            }
        float cw = (GW - 32) / cols, ch = (SpecBase - 62) / rows;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                float v = f[x, y];
                if (v < 0.12f) continue;
                var r = new RectangleF(16 + x * cw, SpecBase - (y + 1) * ch, cw - 1.2f, ch - 1.2f);
                (v > 0.62f ? dots2 : v > 0.3f ? dots : null)?.Add(r);
                if (v <= 0.3f) g.FillRectangle(ink.Reflection, r);
            }
        Fill(g, ink.Accent, dots);
        Fill(g, ink.Hot, dots2);
    }

    readonly List<(float X, float Y, float Z)> stars = [];

    /// <summary>Flying through stars; the music sets the speed, the kick drum makes them flash.</summary>
    void DrawStars(Graphics g, Inks ink)
    {
        float level = levels.Length > 0 ? levels.Average() : 0;
        float bass = levels.Length > 2 ? (levels[0] + levels[1] + levels[2]) / 3 : 0;
        float speed = 0.004f + level * 0.05f;
        while (stars.Count < 160) stars.Add(((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble()));
        float cx = GW / 2, cy = 133;
        for (int i = 0; i < stars.Count; i++)
        {
            var (x, y, z) = stars[i];
            z -= speed;
            if (z <= 0.02f)
            {
                stars[i] = ((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, 1);
                continue;
            }
            stars[i] = (x, y, z);
            float sx = cx + x / z * GW * 0.18f, sy = cy + y / z * 60;
            if (sx < 14 || sx > GW - 14 || sy < 62 || sy > SpecBase) continue;
            float size = Math.Clamp(3.6f * (1 - z), 1, 3.8f);
            (z < 0.25f && bass > 0.5f ? dots2 : dots).Add(new RectangleF(sx, sy, size, size));
        }
        Fill(g, ink.Main, dots);
        Fill(g, ink.Hot, dots2);
    }

    /// <summary>Sunrise and sunset for the PC's area (from its time zone), for automatic night mode.</summary>
    static class Sun
    {
        public static bool IsDaylight(DateTime local)
        {
            var (lat, lon) = Location();
            var utc = local.ToUniversalTime();
            int day = utc.DayOfYear;
            double gamma = 2 * Math.PI / 365 * (day - 1 + (utc.Hour - 12) / 24.0);
            double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma) - 0.006758 * Math.Cos(2 * gamma)
                + 0.000907 * Math.Sin(2 * gamma) - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);
            double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma) - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
            double latR = lat * Math.PI / 180;
            double cosHa = (Math.Cos(90.833 * Math.PI / 180) / (Math.Cos(latR) * Math.Cos(decl))) - Math.Tan(latR) * Math.Tan(decl);
            if (cosHa <= -1) return true;   // midnight sun
            if (cosHa >= 1) return false;   // polar night
            double ha = Math.Acos(cosHa) * 180 / Math.PI;
            double sunrise = 720 - 4 * (lon + ha) - eqTime, sunset = 720 - 4 * (lon - ha) - eqTime; // minutes UTC
            double m = utc.Hour * 60 + utc.Minute;
            return m >= sunrise && m <= sunset;
        }

        /// <summary>A rough location from the time zone (no location access needed).</summary>
        static (double Lat, double Lon) Location()
        {
            string id = TimeZoneInfo.Local.Id;
            double lon = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalHours * 15 - (TimeZoneInfo.Local.IsDaylightSavingTime(DateTime.Now) ? 15 : 0);
            double lat = id switch
            {
                var s when s.Contains("Tokyo") || s.Contains("Japan") => 35.7,
                var s when s.Contains("W. Europe") || s.Contains("Romance") || s.Contains("Central Europe") => 50.5,
                var s when s.Contains("GMT") || s.Contains("Greenwich") => 51.5,
                var s when s.Contains("FLE") || s.Contains("Russian") || s.Contains("Belarus") => 56,
                var s when s.Contains("Eastern Standard") || s.Contains("US Eastern") => 40.7,
                var s when s.Contains("Pacific") => 37,
                var s when s.Contains("Central Standard") || s.Contains("Mountain") => 39,
                var s when s.Contains("AUS") || s.Contains("E. Australia") => -33.9,
                var s when s.Contains("Korea") => 37.5,
                var s when s.Contains("China") || s.Contains("Taipei") => 31,
                _ => 48,
            };
            return (lat, lon);
        }
    }
}
