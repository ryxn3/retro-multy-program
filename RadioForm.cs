using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RetroRadio;

enum Vis { Fan, Grid, Bars, Scope, Video, Circle, Vu, Cd, Fire, Stars }
enum RepeatMode { Off, All, One }


sealed record Palette(string Name, Color Main, Color Accent);

/// <summary>The whole head unit: a borderless, fully custom-drawn window.</summary>
sealed partial class RadioForm : Form
{
    // Everything is laid out in these logical units and scaled by the DPI factor S.
    const float GH = 262;
    const int DispSS = 2;

    // Display-local layout.
    const float ProgressY = 214;
    const float StatusY = 230;
    const float SpecBase = 200;

    static readonly string[] AudioExt = [".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".aif", ".aiff", ".mp2", ".opus"];

    static readonly Palette[] Palettes =
    [
        new("ICE", Color.FromArgb(110, 235, 255), Color.FromArgb(255, 60, 70)),
        new("AQUA", Color.FromArgb(120, 245, 235), Color.FromArgb(255, 175, 60)),
        new("AMBER", Color.FromArgb(255, 180, 60), Color.FromArgb(255, 80, 50)),
        new("GREEN", Color.FromArgb(120, 255, 140), Color.FromArgb(255, 225, 90)),
        new("BLUE", Color.FromArgb(120, 150, 255), Color.FromArgb(255, 110, 210)),
        new("RED", Color.FromArgb(255, 70, 70), Color.FromArgb(255, 200, 120)),
        new("VAPOR", Color.FromArgb(120, 235, 255), Color.FromArgb(255, 90, 220)),
    ];

    readonly AudioEngine engine = new();
    readonly List<string> tracks = new();
    int current = -1;
    bool stopped = true;
    bool shuffle;
    RepeatMode repeat = RepeatMode.All;
    bool remainingTime;
    int paletteIdx;
    Vis vis = Vis.Fan;
    bool listMode;

    // Settings menu (shown on the display).
    sealed record Setting(string Name, Func<string> Value, Action<int> Change);
    static readonly string[] BrightNames = ["LOW", "MID", "HIGH"];
    static readonly string[] FallNames = ["SLOW", "NORMAL", "FAST"];
    static readonly float[] FallRates = [0.8f, 1.5f, 2.8f];
    static readonly int[] SeekSteps = [5, 10, 30];
    static readonly float[] SizeScales = [0.75f, 1f, 1.25f, 1.5f];
    readonly List<Setting> allSettings;
    bool settingsMode;
    int setCursor, setTop;
    double clearArmedUntil;
    int brightness = 1, fallSpeed = 1, seekStep, sizeIdx = 1;
    bool showPeaks = true, onTop, idleDemo = true, lyricsOn;
    string shownLyric = "";
    string startupSound = "welcome", shutdownSound = "systemoff"; // Chimes ids
    Chime? bootChime, offChime; // the sounds actually playing (RANDOM picks one each time)
    bool japaneseGreeting = true; // JDM style: Japanese greetings (matching the sounds), radio names and boot text

    /// <summary>JDM style, or the whole radio in Japanese.</summary>
    bool Jdm => japaneseGreeting || Lang.Current == Language.Japanese;

    /// <summary>A radio's name as the settings show it: in Japanese for JDM style.</summary>
    string ModelName(RadioDesign d) => Jdm && Lang.JapaneseModelNames.TryGetValue(d.Name, out var ja) ? ja : d.Name;
    int chimeVolume; // 0 = follow the volume knob, 1..10 = 10%..100%

    float? ChimeVolume => chimeVolume == 0 ? null : chimeVolume / 10f;
    double shutdownSoundEnd;
    bool shutdownHidden;
    string customSound = "";

    // Power-on sequence: segment test + logo until BootIntro, then HELLO with a spectrum sweep until BootLen.
    const double BootIntro = 2.3, BootLen = 3.5;
    bool Booting => bootAnim && now < BootLen;

    // Power-off sequence: "GOOD BYE" + music fade, then the screen collapses to a line, a dot, and goes dark.
    const double ShutCollapse = 1.2, ShutLen = 2.3;
    bool shutdownDone, snapshotRun;
    double shutdownAt = -1, shutdownTestAt = -1;
    bool Shutting => shutdownAt >= 0;
    int listCursor, listTop;
    const int ListRows = 8;
    string title = "";
    readonly Random rng = new();

    string flashText = "";
    double flashStart;
    double flashUntil, volOverlayUntil;
    double now, lastFrame, marqueeStart, lastHistory;

    float[] levels = new float[22], peaks = new float[22], peakHold = new float[22], raw = new float[22];
    const int HistDepth = 8;
    readonly float[][] history = new float[HistDepth][];
    int histPos;
    readonly float[] wave = new float[512];

    readonly List<KeyDesign> buttons = new();
    string? pendingStyle;
    Btn hover = Btn.None, pressed = Btn.None;
    Btn knobDrag = Btn.None;
    float knobStartY, knobStartVal;
    bool speedOverlay;
    RectangleF timeBox;

    float S = 1;
    Bitmap? faceCache, disp, glow1, glow2, glowAll;
    Inks? inks;
    readonly List<RectangleF> dots = new(4096);
    readonly List<RectangleF> dots2 = new(4096);
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer frameTimer = new() { Interval = 15 };
    readonly System.Windows.Forms.Timer repeatTimer = new();
    float glowA1 = 0.95f, glowA2 = 0.75f;
    readonly ImageAttributes glowWrap = MakeWrap(); // mirrored edges, so the glow doesn't fade at the rim
    readonly Font labelFont = new("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
    readonly Font tinyFont = new("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font brandFont = new("Segoe UI", 11f, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel);

    string? snapshotPath;
    bool forceVolOverlay, styleFromArgs, openScAtStart;
    double snapshotAt;

    static string StatePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroRadio", "state.txt");

    public RadioForm(string[] args)
    {
        Text = "Retro Radio";
#if !MAC
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); // the exe's icon, for the taskbar and Alt+Tab
#endif
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        AllowDrop = true;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        BackColor = Color.Black;

        for (int i = 0; i < HistDepth; i++) history[i] = new float[16];
        InitServices();
        allSettings = [.. BuildSettings(), .. ExtraSettings()];
        ReloadDesigns();
        LoadState();
        if (pendingStyle != null)
        {
            int si = designs.FindIndex(d => d.Name == pendingStyle);
            if (si < 0 && int.TryParse(pendingStyle, out var n)) si = n;
            if (si >= 0) styleIdx = Math.Clamp(si, 0, designs.Count - 1);
        }
        bootChime = Chimes.Pick(startupSound, true);
        ApplySound();
        BuildButtons();
        UpdateGlow();

        var files = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--snapshot" && i + 1 < args.Length) { snapshotPath = args[++i]; snapshotAt = 4.5; snapshotRun = true; }
            else if (args[i] == "--shutdown-at" && i + 1 < args.Length) shutdownTestAt = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            else if (args[i] == "--snapshot-at" && i + 1 < args.Length) snapshotAt = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            else if (args[i] == "--vis" && i + 1 < args.Length) vis = (Vis)int.Parse(args[++i]);
            else if (args[i] == "--list") listMode = true;
            else if (args[i] == "--settings") settingsMode = true;
            else if (args[i] == "--night") night = true;
            else if (args[i] == "--day") night = false;
            else if (args[i] == "--soundcloud") openScAtStart = true;
            else if (args[i] == "--spotify") { serviceIdx = 1; openScAtStart = true; }
            else if (args[i] == "--style" && i + 1 < args.Length) { styleIdx = Math.Clamp(int.Parse(args[++i]), 0, designs.Count - 1); paletteIdx = Design.Palette; styleFromArgs = true; }
            else if (args[i] == "--vol-overlay") forceVolOverlay = true;
            else if (args[i] == "--lang" && i + 1 < args.Length && Enum.TryParse<Language>(args[++i], true, out var lng)) Lang.Current = lng;
            else if (args[i] == "--mute") engine.Muted = true;
            else if (args[i] == "--eq") eqMode = true;
            else if (args[i] == "--twocolor") twoColor = true;
            else if (args[i] == "--mini") miniMode = true;
            else if (args[i] == "--pulse") bassPulse = true;
            else if (args[i] == "--bpm") bpmOn = true;
            else if (args[i] == "--section" && i + 1 < args.Length) { settingsMode = true; settingsSection = args[++i].ToUpperInvariant(); }
            else if (args[i] == "--boot" && i + 1 < args.Length && Enum.TryParse<BootStyle>(args[++i], true, out var bst)) bootStyle = bst;
            else if (args[i] == "--shut" && i + 1 < args.Length && Enum.TryParse<ShutStyle>(args[++i], true, out var sst)) shutStyle = sst;
            else files.Add(args[i]);
        }
        // .rdv files are display videos; anything else is music.
        foreach (var rdv in files.Where(f => f.EndsWith(".rdv", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            LoadVideo(rdv, quiet: true);
            vis = Vis.Video;
            files.Remove(rdv);
        }
        if (files.Count > 0)
        {
            tracks.Clear();
            current = -1;
            AddPaths(files, playFirstNew: true);
        }
        if (forceVolOverlay) volOverlayUntil = 1e9;
        if (styleFromArgs) BuildButtons();
        if (openScAtStart) OpenSoundCloud();

        engine.TrackFinished += (_, _) => BeginInvoke(OnTrackFinished);
        frameTimer.Tick += (_, _) => Frame();
        repeatTimer.Tick += (_, _) =>
        {
            repeatTimer.Interval = 70;
            if (pressed != Btn.None) Execute(pressed);
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TopMost = onTop || miniMode;
        ApplyScale();
        frameTimer.Start();
        if (trayOn && snapshotPath == null) SetTray(true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        clock.Restart();
        if (snapshotPath == null)
        {
            PlayStartupSound();
            // The time-of-day greeting follows the startup sound.
            if (voiceOn) greetAt = now + Math.Max(lastChimeLength, 1.5) + 0.7;
        }
    }

    void PlayStartupSound()
    {
        if (engine.Muted || startupSound == Chimes.Off) return;
        try
        {
            string? file = startupSound == Chimes.Custom ? customSound : bootChime is { } c ? Chimes.FilePath(c) : null;
            if (file != null) lastChimeLength = engine.PlayEffect(file, ChimeVolume);
        }
        catch (Exception)
        {
            Flash("SOUND ERROR");
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScale();
    }

    void ApplyScale()
    {
        S = DeviceDpi / 96f * SizeScales[sizeIdx];
        var size = miniMode
            ? new Size((int)Math.Ceiling((Glass.Width + MiniPad * 2) * S), (int)Math.Ceiling((Glass.Height + MiniPad * 2) * S))
            : new Size((int)Math.Ceiling(BW * S), (int)Math.Ceiling(BH * S));
        MinimumSize = MaximumSize = Size.Empty;
        ClientSize = size;
        MinimumSize = MaximumSize = Size;
        RebuildFaceplates();
        // Cut the window to the radio's own outline, so there's no box around it.
        using (var outline = miniMode
                   ? FaceplateRenderer.RoundRect(new RectangleF(Glass.X - MiniPad, Glass.Y - MiniPad, Glass.Width + MiniPad * 2, Glass.Height + MiniPad * 2), 10)
                   : FaceplateRenderer.RoundRect(Design.Face.R, Design.Corner))
        using (var m = new System.Drawing.Drawing2D.Matrix())
        {
            m.Scale(S, S);
            if (miniMode) m.Translate(-MiniOrigin.X, -MiniOrigin.Y);
            outline.Transform(m);
            var old = Region;
            Region = new Region(outline);
            old?.Dispose();
        }
        disp?.Dispose(); glow1?.Dispose(); glow2?.Dispose(); glowAll?.Dispose();
        // The display is drawn at twice the size and scaled down, so every dot comes out even and smooth.
        int dw = (int)Math.Ceiling(GW * S * K * DispSS), dh = (int)Math.Ceiling(GH * S * K * DispSS);
        disp = new Bitmap(dw, dh, PixelFormat.Format32bppPArgb);
        glow1 = new Bitmap(Math.Max(1, dw / (4 * DispSS)), Math.Max(1, dh / (4 * DispSS)), PixelFormat.Format32bppPArgb);
        glow2 = new Bitmap(Math.Max(1, dw / (10 * DispSS)), Math.Max(1, dh / (10 * DispSS)), PixelFormat.Format32bppPArgb);
        glowAll = new Bitmap(glow1.Width, glow1.Height, PixelFormat.Format32bppPArgb);
        Invalidate();
    }

    // ───────────────────────────── layout ─────────────────────────────

    // ───────────────────────────── frame loop ─────────────────────────────

    void Frame()
    {
        now = clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(0.1, now - lastFrame);
        lastFrame = now;
        if (shutdownTestAt >= 0 && now >= shutdownTestAt && !Shutting) BeginShutdown();
        if (Shutting) UpdateShutdown();
        spotify.Poll();
        engine.Loopback = spotify.Active && spotify.IsPlaying;
        CheckAutoNight();
        UpdateVoice();
        UpdateTrayText();
        if (sleepAt > 0 && now >= sleepAt)
        {
            // Sleep timer: power off, with the animation and sound.
            sleepAt = -1;
            sleepIdx = 0;
            if (!Shutting) Close();
        }
        UpdateLevels(dt);
        ShareNowPlaying();
        InvalidateChanged();
        // Nothing playing and nothing moving: a calmer frame rate is plenty.
        bool busy = player.IsPlaying || Shutting || Booting || cdAt >= 0 || now - lastActivity < 3;
        int interval = frameRate switch { 1 => 15, 2 => 33, _ => busy ? 15 : 33 };
        if (!memoryTidied && now > BootLen + 4)
        {
            // Starting up (decoding sounds, building fonts and faceplates) leaves a lot of garbage that .NET
            // would otherwise keep for a long time, because the radio allocates so little afterwards.
            memoryTidied = true;
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        if (frameTimer.Interval != interval) frameTimer.Interval = interval;

        if (snapshotPath != null && now >= snapshotAt)
        {
            using var bmp = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) PaintAll(g);
            bmp.Save(snapshotPath, ImageFormat.Png);
            snapshotPath = null;
            Close();
        }
    }

    const int VisCount = 10;

    static string VisName(Vis v) => v switch
    {
        Vis.Fan => "3D FAN", Vis.Grid => "3D GRID", Vis.Bars => "BARS", Vis.Scope => "SCOPE", Vis.Circle => "CIRCLE",
        Vis.Vu => "VU METER", Vis.Cd => "CD", Vis.Fire => "FIRE", Vis.Stars => "STARS", _ => "VIDEO",
    };

    double lastChimeLength;

    int BandCount => vis switch { Vis.Fan => 22, Vis.Grid => 16, Vis.Bars => 32, Vis.Circle => 48, Vis.Fire => 30, _ => 16 };

    void UpdateLevels(float dt)
    {
        int n = BandCount;
        if (levels.Length != n)
        {
            levels = new float[n]; peaks = new float[n]; peakHold = new float[n]; raw = new float[n];
        }

        if (Shutting)
        {
            Array.Clear(raw);
        }
        else if (Booting)
        {
            // Power-on sweep (dark during the logo intro).
            float phase = now < BootIntro ? -1 : (float)((now - BootIntro) / 1.0);
            for (int i = 0; i < n; i++)
            {
                float x = (float)i / (n - 1);
                raw[i] = Math.Clamp(1.1f - Math.Abs(x - phase) * 3.5f, 0, 1);
            }
        }
        else if (player.IsPlaying)
        {
            engine.GetSpectrum(raw);
        }
        else if (tracks.Count == 0 && idleDemo)
        {
            // Demo "attract" wave while there's nothing to play.
            for (int i = 0; i < n; i++)
                raw[i] = 0.14f + 0.12f * (float)Math.Sin(now * 1.9 + i * 0.5) + 0.06f * (float)Math.Sin(now * 3.1 - i * 0.9);
        }
        else Array.Clear(raw);

        for (int i = 0; i < n; i++)
        {
            float r = raw[i];
            levels[i] = r > levels[i] ? levels[i] + (r - levels[i]) * 0.7f : Math.Max(r, levels[i] - dt * FallRates[fallSpeed]);
            if (levels[i] >= peaks[i])
            {
                peaks[i] = levels[i];
                peakHold[i] = (float)now + 0.45f;
            }
            else if (now > peakHold[i])
            {
                peaks[i] = Math.Max(levels[i], peaks[i] - dt * 0.75f);
            }
        }
        if (!showPeaks) Array.Clear(peaks);

        if (vis == Vis.Grid && now - lastHistory > 0.065)
        {
            lastHistory = now;
            histPos = (histPos + 1) % HistDepth;
            Array.Copy(levels, history[histPos], Math.Min(levels.Length, history[histPos].Length));
        }
        if (vis is Vis.Scope or Vis.Circle or Vis.Vu) engine.GetWaveform(wave);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            PaintAll(e.Graphics);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // A frame that can't be drawn is skipped rather than crashing the radio; rebuild the images once.
            if (!repairing)
            {
                repairing = true;
                BeginInvoke(() =>
                {
                    try { ApplyScale(); } catch (Exception) { }
                    repairing = false;
                });
            }
        }
    }

    bool repairing;

    void PaintAll(Graphics g)
    {
        if (faceCache == null) return;
        var pal = Palettes[paletteIdx];
        if (inks == null || inks.Palette != pal || inks.Low != (brightness == 0))
        {
            inks?.Dispose();
            inks = new Inks(pal, brightness == 0);
        }

        g.CompositingQuality = CompositingQuality.HighSpeed;
        // Mini mode shows only the display: shift the whole radio so it sits in the small window.
        if (miniMode) g.TranslateTransform(-MiniOrigin.X * S, -MiniOrigin.Y * S);
        DrawFaceplate(g, !Shutting || now - shutdownAt < ShutCollapse);
        g.ScaleTransform(S, S);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        FaceplateRenderer.DrawLive(g, Design, LiveState());
        DrawBassPulse(g);
        RenderDisplay(inks);
        DrawGlass(g);
        DrawCdAnimation(g);
    }

    // ───────────────────────────── display ─────────────────────────────

    void DrawGlass(Graphics g)
    {
        using var path = RoundRect(Glass, 6);
        using (var bg = new LinearGradientBrush(Glass, Color.FromArgb(6, 10, 16), Color.FromArgb(1, 2, 4), 90f)) g.FillPath(bg, path);

        var st = g.Save();
        g.SetClip(path);

        // During power-off the picture squashes vertically, then horizontally, like an old tube.
        float sy = 1, sx = 1;
        double c = Shutting ? now - shutdownAt - ShutCollapse : -1;
        if (c > 0 && shutStyle == ShutStyle.TvOff)
        {
            sy = (float)Math.Max(0.008, 1 - c / 0.28);
            if (c > 0.28) sx = (float)Math.Max(0, 1 - (c - 0.28) / 0.25);
        }
        float cx = Glass.X + Glass.Width / 2, cy = Glass.Y + Glass.Height / 2;
        var destF = new RectangleF(cx - Glass.Width * sx / 2, cy - Glass.Height * sy / 2, Glass.Width * sx, Glass.Height * sy);

        if (Design.Screen == ScreenKind.Modern && FlipOpen() is var flip && flip < 1)
        {
            // The motorised screen folding up (power-on) or down (power-off) on its bottom hinge.
            if (flip > 0.01f)
            {
                var fr = new RectangleF(Glass.X, Glass.Bottom - Glass.Height * flip, Glass.Width, Glass.Height * flip);
                g.DrawImage(disp!, fr);
                using var lip = new SolidBrush(Color.FromArgb(200, 26, 28, 32));
                g.FillRectangle(lip, fr.X, fr.Y, fr.Width, 3);
            }
        }
        else if (c > 0 && DrawShutdownPicture(g, c, destF))
        {
            // Drawn by the chosen power-off effect.
        }
        else if (sx > 0.01)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            PointF[] corners = [destF.Location, new(destF.Right, destF.Top), new(destF.Left, destF.Bottom)];
            if (Design.Screen != ScreenKind.Modern)
            {
                // The glow's strength is already in its pixels (see ScaleAlpha), so no slow color filter here.
                // Sampling half a pixel inside the edges keeps the rim bright without the slower edge-wrap mode.
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.DrawImage(glowAll!, destF, Inset(glowAll!), GraphicsUnit.Pixel);
            }
            if (c > 0)
            {
                g.DrawImage(disp!, destF);
            }
            else
            {
                // Exactly half size: plain bilinear on pixel centres averages each 2x2 block, as the slow filter did.
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(disp!, destF);
                g.PixelOffsetMode = PixelOffsetMode.Default;
            }
        }
        if (c > 0.12 && shutStyle == ShutStyle.TvOff) DrawCollapseLine(g, c, destF, cx, cy);
        if (c <= 0 && Design.Screen != ScreenKind.Modern) DrawVfdDetails(g);

        // Glass reflection.
        using (var refl = new LinearGradientBrush(new RectangleF(Glass.X, Glass.Y, Glass.Width, Glass.Height * 0.6f),
                   Color.FromArgb(22, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 75f))
        using (var rp = new GraphicsPath())
        {
            rp.AddPolygon(new PointF[] { new(Glass.X, Glass.Y), new(Glass.X + Glass.Width * 0.62f, Glass.Y), new(Glass.X + Glass.Width * 0.38f, Glass.Bottom), new(Glass.X, Glass.Bottom) });
            g.FillPath(refl, rp);
        }
        g.Restore(st);
        using var edge = new Pen(Color.FromArgb(200, 0, 0, 0), 3f);
        g.DrawPath(edge, path);
    }

    /// <summary>The bright line (then dot) left behind as the screen collapses.</summary>
    void DrawCollapseLine(Graphics g, double c, RectangleF r, float cx, float cy)
    {
        var hot = inks!.Hot.Color;
        if (c < 0.53)
        {
            int a = (int)Math.Clamp((c - 0.12) / 0.16 * 255, 0, 255);
            float h = Math.Max(2.5f, r.Height);
            var line = new RectangleF(r.X, cy - h / 2, Math.Max(3, r.Width), h);
            using (var halo = new SolidBrush(Color.FromArgb(a / 4, hot)))
                g.FillRectangle(halo, RectangleF.Inflate(line, 0, 5));
            using var b = new SolidBrush(Color.FromArgb(a, hot));
            g.FillRectangle(b, line);
            return;
        }
        // Lingering dot that fades away.
        double f = 1 - Math.Clamp((c - 0.53) / 0.45, 0, 1);
        if (f <= 0) return;
        float rad = 2 + 3 * (float)f;
        var glow = new RectangleF(cx - rad * 5, cy - rad * 5, rad * 10, rad * 10);
        using (var gp = new GraphicsPath())
        {
            gp.AddEllipse(glow);
            using var gb = new PathGradientBrush(gp) { CenterColor = Color.FromArgb((int)(160 * f), hot), SurroundColors = [Color.FromArgb(0, hot)] };
            g.FillPath(gb, gp);
        }
        using var dot = new SolidBrush(Color.FromArgb((int)(255 * f), Color.White));
        g.FillEllipse(dot, cx - rad, cy - rad, rad * 2, rad * 2);
    }

    void RenderDisplay(Inks ink)
    {
        using (var g = Graphics.FromImage(disp!))
        {
            g.Clear(Color.Transparent);
            g.ScaleTransform(S * K * DispSS, S * K * DispSS);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool volOverlay = now < volOverlayUntil;
            if (Design.Screen == ScreenKind.Modern) DrawModern(g);
            else if (bootAnim && now < BootIntro) DrawBootIntro(g, ink);
            else
            {
                var text = TextInks(ink); // two-colour VFD: the text zones in the other colour
                DrawHeader(g, text, volOverlay);
                DrawMarquee(g, text);
                if (volOverlay && speedOverlay) DrawSpeedMeter(g, ink);
                else if (volOverlay) DrawVolumeWedge(g, ink);
                else if (eqMode) DrawEq(g, ink);
                else if (scMode) DrawSoundCloud(g, ink);
                else if (settingsMode) DrawSettings(g, ink);
                else if (listMode) DrawList(g, ink);
                else if (ShowIdleClock) DrawBigClock(g, text);
                else
                {
                    switch (vis)
                    {
                        case Vis.Circle: DrawCircle(g, ink); break;
                        case Vis.Vu: DrawVu(g, ink); break;
                        case Vis.Cd: DrawCd(g, ink); break;
                        case Vis.Fire: DrawFire(g, ink); break;
                        case Vis.Stars: DrawStars(g, ink); break;
                        case Vis.Fan: DrawFan(g, ink); break;
                        case Vis.Grid: DrawGrid(g, ink); break;
                        case Vis.Bars: DrawBars(g, ink); break;
                        case Vis.Scope: DrawScope(g, ink); break;
                        case Vis.Video: DrawVideo(g, ink); break;
                    }
                }
                DrawProgress(g, ink);
                DrawStatus(g, text);
            }
        }
        if (Design.Screen == ScreenKind.Modern) return; // LCDs don't bloom
        using (var sg = Graphics.FromImage(glow1!))
        {
            sg.Clear(Color.Transparent);
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.DrawImage(disp!, new Rectangle(0, 0, glow1!.Width, glow1.Height));
        }
        using (var sg = Graphics.FromImage(glow2!))
        {
            sg.Clear(Color.Transparent);
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.DrawImage(glow1!, new Rectangle(0, 0, glow2!.Width, glow2.Height));
        }
        ScaleAlpha(glow1!, glowA1);
        ScaleAlpha(glow2!, glowA2);
        // Both glows layered once at small size, so only one image is stretched over the display each frame.
        using (var sg = Graphics.FromImage(glowAll!))
        {
            sg.Clear(Color.Transparent);
            sg.InterpolationMode = InterpolationMode.Bilinear;
            sg.DrawImage(glow2!, new RectangleF(0, 0, glowAll!.Width, glowAll.Height), Inset(glow2!), GraphicsUnit.Pixel);
            sg.DrawImageUnscaled(glow1!, 0, 0);
            if (brightness == 2) sg.DrawImageUnscaled(glow1!, 0, 0);
        }
    }

    byte[] alphaScratch = [];

    /// <summary>Fades a (small, premultiplied) image in place: cheaper than a color filter when it's drawn big.</summary>
    void ScaleAlpha(Bitmap bmp, float a)
    {
        if (a >= 0.999f) return;
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            int n = data.Stride * data.Height;
            if (alphaScratch.Length < n) alphaScratch = new byte[n];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, alphaScratch, 0, n);
            int k = (int)(a * 256);
            for (int i = 0; i < n; i++) alphaScratch[i] = (byte)(alphaScratch[i] * k >> 8);
            System.Runtime.InteropServices.Marshal.Copy(alphaScratch, 0, data.Scan0, n);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    const int FillChunk = 2048; // 32 KB of rectangles: stays out of the large-object heap
    readonly RectangleF[] fillChunk = new RectangleF[FillChunk];
    readonly RectangleF[][] fillSmall = [new RectangleF[32], new RectangleF[128], new RectangleF[512]];

    /// <summary>
    /// Fills the dots in fixed-size batches from one reused array. Copying the whole list into a new array
    /// every frame made hundreds of MB of short-lived large arrays that .NET only cleans up now and then.
    /// </summary>
    void Fill(Graphics g, Brush b, List<RectangleF> list)
    {
        var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
        while (all.Length >= FillChunk)
        {
            all[..FillChunk].CopyTo(fillChunk);
            g.FillRectangles(b, fillChunk);
            all = all[FillChunk..];
        }
        if (all.Length > 0)
        {
            // The rest goes in the smallest reused array it fits; the unused tail is empty rectangles.
            var arr = fillChunk;
            foreach (var small in fillSmall)
                if (small.Length >= all.Length) { arr = small; break; }
            all.CopyTo(arr);
            Array.Clear(arr, all.Length, arr.Length - all.Length);
            g.FillRectangles(b, arr);
        }
        list.Clear();
    }

    void DotText(Graphics g, Brush b, string s, float x, float y, float pitch)
    {
        DotFont.Emit(dots, s, x, y, pitch, pitch * 0.8f);
        Fill(g, b, dots);
    }

    string SourceLabel()
    {
        if (current < 0 || current >= tracks.Count) return "--";
        if (IsSc(tracks[current])) return "SC";
        if (RadioBrowser.IsEntry(tracks[current])) return "FM";
        if (IsSp(tracks[current])) return "SPOT";
        var ext = Path.GetExtension(tracks[current]).TrimStart('.').ToUpperInvariant();
        return ext.Length > 4 ? ext[..4] : ext;
    }

    void DrawHeader(Graphics g, Inks ink, bool volOverlay)
    {
        const float y = 10, p = 3f;
        float right = GW - 16;

        if (Shutting)
        {
            // Blink once, like the unit saying bye.
            if (now - shutdownAt < 0.15) return;
            string bye = DotFont.Normalize(ByeText);
            DotText(g, ink.Main, bye, (GW - DotFont.Width(bye, p)) / 2, y, p);
            timeBox = RectangleF.Empty;
            return;
        }

        if (volOverlay && speedOverlay)
        {
            DotText(g, ink.Main, DotFont.Normalize(Lang.T("SPEED")), 16, y, p);
            string v = SpeedText();
            DotText(g, ink.Main, v, right - DotFont.Width(v, p), y, p);
            timeBox = RectangleF.Empty;
            return;
        }
        if (volOverlay)
        {
            DotText(g, ink.Main, DotFont.Normalize(Lang.T("VOLUME")), 16, y, p);
            string v = engine.Muted ? DotFont.Normalize(Lang.T("MUTE")) : ((int)Math.Round(engine.Volume * 40)).ToString();
            DotText(g, ink.Main, v, right - DotFont.Width(v, p), y, p);
            timeBox = RectangleF.Empty;
            return;
        }
        if (now < flashUntil)
        {
            var f = DotFont.Normalize(flashText);
            float fw = DotFont.Width(f, p);
            // Messages wider than the display scroll through it.
            float fx = fw <= GW - 8 ? (GW - fw) / 2 : GW - (float)((now - flashStart) * 7 * 6 * p) % (fw + GW);
            DotText(g, ink.Main, f, fx, y, p);
            timeBox = RectangleF.Empty;
            return;
        }
        if (Booting)
        {
            string hello = DotFont.Normalize(HelloText);
            DotText(g, ink.Main, hello, (GW - DotFont.Width(hello, p)) / 2, y, p);
            return;
        }
        if (tracks.Count == 0)
        {
            DotText(g, ink.Main, DotFont.Normalize(Lang.T("NO DISC")), 16, y, p);
            DotText(g, ink.Main, "--'--", right - DotFont.Width("--'--", p), y, p);
            timeBox = RectangleF.Empty;
            return;
        }

        string left = $"{SourceLabel()} -{Math.Max(current, 0) + 1:00}";
        DotText(g, ink.Main, left, 16, y, p);
        char state = stopped || !player.IsLoaded || (!player.IsPlaying && !player.IsPaused) ? DotFont.Stop : player.IsPlaying ? DotFont.Play : DotFont.Pause;
        float sx = 16 + DotFont.Width(left, p) + p * 6;
        DotText(g, ink.Accent, state.ToString(), sx, y, p);

        // Boxed P-TIME / R-TIME tag, drawn inverse like on the Kenwood.
        string tag = remainingTime ? "R-TIME" : "P-TIME";
        const float tp = 1.8f;
        float tw = DotFont.Width(tag, tp);
        timeBox = new RectangleF(sx + p * 10, y + 2, tw + 8, 7 * tp + 6);
        g.FillRectangle(ink.Main, timeBox);
        DotText(g, ink.Ink, tag, timeBox.X + 4, timeBox.Y + 3, tp);

        var pos = player.Position;
        var t = remainingTime ? player.Duration - pos : pos;
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        string time = $"{(remainingTime ? "-" : "")}{(int)t.TotalMinutes:00}'{t.Seconds:00}";
        bool blinkOff = player.IsPaused && !stopped && (now % 1.0) > 0.6;
        if (!blinkOff) DotText(g, ink.Main, time, right - DotFont.Width(time, p), y, p);
    }

    void DrawMarquee(Graphics g, Inks ink)
    {
        const float y = 40, p = 2f;
        DotText(g, ink.Accent, DotFont.Note.ToString(), 16, y, p);

        var lyric = !Shutting && !scMode && !settingsMode && !listMode ? CurrentLyric() : null;
        string lyricText = lyric is { Line.Length: > 0 } ly ? ly.Line : "";
        if (lyricText != shownLyric)
        {
            shownLyric = lyricText;
            marqueeStart = now - 1.2; // lyric lines start scrolling almost straight away
        }
        string s = Shutting ? (japaneseGreeting ? "ありがとうございました" : Lang.T("SEE YOU NEXT TIME"))
            : searching ? $"{Lang.T("SEARCH")}: {listFilter}" + ((int)(now * 3) % 2 == 0 ? "_" : " ")
            : eqMode ? $"{Lang.T("EQUALIZER")} · {EqPresets[eqPreset].Name}  -  {Lang.T("ARROWS OR CLICK - ESC TO CLOSE")}"
            : scMode ? ScMarquee()
            : settingsMode ? Lang.T("SETTINGS - CLICK OR USE ARROW KEYS TO CHANGE")
            : listMode ? Lang.F("LIST {0} TRK - DOUBLE-CLICK TO PLAY", tracks.Count)
            : tracks.Count == 0 ? Lang.T("DROP MUSIC FILES OR FOLDERS HERE  -  OR PRESS OPEN / FOLDER")
            : lyricText.Length > 0 ? lyricText
            : title;
        s = DotFont.Normalize(s);
        const float x0 = 34;
        float width = GW - 16 - x0;
        float w = DotFont.Width(s, p);
        var st = g.Save();
        g.SetClip(new RectangleF(x0, y - 1, width, 7 * p + 2));
        if (w <= width)
        {
            DotText(g, ink.Main, s, x0, y, p);
        }
        else
        {
            float gap = 60;
            double elapsed = Math.Max(0, now - marqueeStart - 1.5);
            float off = (float)(elapsed * (lyricText.Length > 0 ? 75 : 38) % (w + gap));
            off = (float)Math.Floor(off / p) * p;
            DotFont.Emit(dots, s, x0 - off, y, p, p * 0.8f);
            DotFont.Emit(dots, s, x0 - off + w + gap, y, p, p * 0.8f);
            Fill(g, ink.Main, dots);
        }
        g.Restore(st);
    }

    static void AddQuad(GraphicsPath path, PointF b, float ux, float uy, float nx, float ny, float a, float c, float w)
    {
        float hw = w / 2;
        path.AddPolygon(new PointF[]
        {
            new(b.X + ux * a - nx * hw, b.Y + uy * a - ny * hw),
            new(b.X + ux * a + nx * hw, b.Y + uy * a + ny * hw),
            new(b.X + ux * c + nx * hw, b.Y + uy * c + ny * hw),
            new(b.X + ux * c - nx * hw, b.Y + uy * c - ny * hw),
        });
    }

    /// <summary>Alpine-style 3D "stage" analyzer: segmented bars fanning out from a vanishing point.</summary>
    void DrawFan(Graphics g, Inks ink)
    {
        int n = levels.Length;
        const int segs = 16;
        const float segLen = 7.6f, segGap = 2f, width = 13f;
        float cx = GW / 2, vy = SpecBase + 560;

        using var ghost = new GraphicsPath { FillMode = FillMode.Winding };
        using var lit = new GraphicsPath { FillMode = FillMode.Winding };
        using var hot = new GraphicsPath { FillMode = FillMode.Winding };
        using var refl = new GraphicsPath { FillMode = FillMode.Winding };

        for (int i = 0; i < n; i++)
        {
            float t = (i + 0.5f) / n - 0.5f;
            var b = new PointF(cx + t * GW * 0.84f, SpecBase - 6 * (1 - 4 * t * t));
            float dx = b.X - cx, dy = b.Y - vy;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            float ux = dx / len, uy = dy / len;
            float nx = -uy, ny = ux;

            int on = (int)Math.Round(levels[i] * segs);
            int pk = Math.Min(segs - 1, (int)Math.Round(peaks[i] * segs));
            for (int s = 0; s < segs; s++)
            {
                float a = s * segLen, c = a + segLen - segGap;
                float w = width * (1 - s * 0.012f);
                var target = s < on ? (s >= segs * 0.7f ? hot : lit) : (s == pk && pk > 0 ? hot : ghost);
                AddQuad(target, b, ux, uy, nx, ny, a, c, w);
                if (s < 2 && s < on) AddQuad(refl, b, ux, uy, nx, ny, -a - 3, -c - 3, w);
            }
        }
        g.FillPath(ink.Ghost, ghost);
        g.FillPath(ink.Main, lit);
        g.FillPath(ink.Hot, hot);
        g.FillPath(ink.Reflection, refl);

        // Stage "ceiling" beams in the corners that flash on the bass.
        float bass = (levels[0] + levels[1] + levels[2]) / 3;
        var brush = bass > 0.55f ? ink.Accent : ink.AccentGhost;
        for (int k = 0; k < 16; k++)
        {
            float f = k / 15f;
            dots.Add(new RectangleF(14 + f * 96, 60 + f * 22, 3, 3));
            dots.Add(new RectangleF(GW - 17 - f * 96, 60 + f * 22, 3, 3));
        }
        Fill(g, brush, dots);
    }

    /// <summary>Kenwood-style dot grid: the live spectrum in front, history receding in red behind it.</summary>
    void DrawGrid(Graphics g, Inks ink)
    {
        int n = levels.Length;
        const int rows = 20;
        var vp = new PointF(GW * 0.3f, 24);
        float spacing = (GW - 40) / n;

        for (int d = HistDepth - 1; d >= 0; d--)
        {
            float k = 1 - d * 0.075f;
            float[] lv = d == 0 ? levels : history[(histPos - d + 1 + HistDepth * 2) % HistDepth];
            float pitch = 5.1f * k, size = pitch * 0.66f;
            float yb = vp.Y + (SpecBase - vp.Y) * k;
            for (int i = 0; i < n; i++)
            {
                float xf = 20 + (i + 0.5f) * spacing;
                float x = vp.X + (xf - vp.X) * k;
                int on = (int)Math.Round((i < lv.Length ? lv[i] : 0) * rows);
                int pk = d == 0 && showPeaks ? (int)Math.Round(peaks[i] * rows) : -1;
                for (int r = 0; r < rows; r++)
                {
                    bool isOn = r < on;
                    if (!isOn && d != 0 && r != pk) continue;
                    for (int c = -1; c <= 1; c++)
                    {
                        var rect = new RectangleF(x + c * pitch - size / 2, yb - r * pitch - size, size, size);
                        if (isOn || r == pk) dots.Add(rect);
                        else dots2.Add(rect);
                    }
                }
            }
            if (d == 0)
            {
                Fill(g, ink.Ghost, dots2);
                Fill(g, ink.Main, dots);
            }
            else
            {
                using var b = new SolidBrush(Color.FromArgb((int)(200 * (1 - d / (float)HistDepth * 0.8f)), ((SolidBrush)ink.Accent).Color));
                Fill(g, b, dots);
            }
        }
    }

    void DrawBars(Graphics g, Inks ink)
    {
        int n = levels.Length;
        const int segs = 22;
        const float top = 66, segH = (SpecBase - top) / segs;
        float slot = (GW - 32) / n, w = slot - 4;
        for (int i = 0; i < n; i++)
        {
            float x = 16 + i * slot + 2;
            int on = (int)Math.Round(levels[i] * segs);
            int pk = (int)Math.Round(peaks[i] * segs);
            for (int s = 0; s < segs; s++)
            {
                var r = new RectangleF(x, SpecBase - (s + 1) * segH + 1, w, segH - 1.6f);
                if (s < on) (s >= segs * 0.82f ? dots2 : dots).Add(r);
                else if (s == pk && pk > 0) dots2.Add(r);
                else g.FillRectangle(ink.Ghost, r);
            }
        }
        Fill(g, ink.Main, dots);
        Fill(g, ink.Accent, dots2);
    }

    void DrawScope(Graphics g, Inks ink)
    {
        const float mid = 133, amp = 62;
        for (float x = 16; x < GW - 16; x += 12)
            for (float y = mid - 60; y <= mid + 60; y += 30)
                dots2.Add(new RectangleF(x, y, 1.6f, 1.6f));
        Fill(g, ink.Ghost, dots2);

        int n = wave.Length / 2;
        float step = (GW - 32) / n;
        for (int i = 0; i < n; i++)
        {
            float v = wave[i * 2];
            if (!player.IsPlaying) v = (float)(0.08 * Math.Sin(now * 2 + i * 0.05));
            float y = mid - Math.Clamp(v * 1.6f, -1, 1) * amp;
            dots.Add(new RectangleF(16 + i * step, y - 1.3f, 2.6f, 2.6f));
        }
        Fill(g, ink.Main, dots);
    }

    string SpeedText() => engine.Speed.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "X";

    /// <summary>Centered meter shown while turning the speed knob: lit from 1x toward slow or fast.</summary>
    void DrawSpeedMeter(Graphics g, Inks ink)
    {
        const int cols = 41, mid = cols / 2;
        float span = GW - 60;
        int at = mid + (int)Math.Round(MathF.Log2(engine.Speed) * mid);
        for (int i = 0; i < cols; i++)
        {
            float x = 30 + i * span / cols;
            int h = 3 + Math.Abs(i - mid) * 17 / mid;
            bool lit = i == mid || (i >= Math.Min(at, mid) && i <= Math.Max(at, mid));
            var list = i == mid ? dots2 : lit ? dots : null;
            for (int r = 0; r < h; r++)
            {
                var rect = new RectangleF(x, SpecBase - 4 - r * 6.2f, 4.4f, 4.4f);
                if (list != null) list.Add(rect);
                else g.FillRectangle(ink.AccentGhost, rect);
            }
        }
        Fill(g, ink.Accent, dots);
        Fill(g, ink.Main, dots2);
        const float lp = 1.8f;
        DotText(g, ink.Main, "SLOW", 30, 70, lp);
        DotText(g, ink.Main, "1X", 30 + mid * span / cols + 2 - DotFont.Width("1X", lp) / 2, 70, lp);
        DotText(g, ink.Main, "FAST", GW - 30 - DotFont.Width("FAST", lp), 70, lp);
    }

    // ───────────────────────────── display video ─────────────────────────────

    RdvVideo.Player? video;
    string videoPath = "";
    double videoStart;
    readonly List<RectangleF> dots3 = new(4096), ghostDots = new(10000);

    void PickVideo()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Pick a radio video",
            Filter = "Radio video (*.rdv)|*.rdv|All files|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadVideo(dlg.FileName);
    }

    void LoadVideo(string path, bool quiet = false)
    {
        try
        {
            var v = new RdvVideo.Player(path);
            video?.Dispose();
            video = v;
            videoPath = path;
            videoStart = now;
            if (quiet) return;
            vis = Vis.Video;
            listMode = settingsMode = false;
            Flash("VIDEO LOADED");
        }
        catch (Exception)
        {
            if (!quiet) Flash("VIDEO ERROR");
        }
    }

    /// <summary>Plays the loaded .rdv animation in the analyzer area, dot for dot.</summary>
    void DrawVideo(Graphics g, Inks ink)
    {
        const float top = 60, areaH = 146;
        if (video == null || video.Count == 0)
        {
            const string a = "NO VIDEO", b = "MAKE ONE WITH RADIO VIDEO CONVERTER";
            DotText(g, ink.Main, a, (GW - DotFont.Width(a, 3f)) / 2, 100, 3f);
            DotText(g, ink.Accent, b, (GW - DotFont.Width(b, 1.8f)) / 2, 140, 1.8f);
            return;
        }

        float areaW = GW - 32;
        float pitch = Math.Min(areaW / video.Width, areaH / video.Height);
        float x0 = (GW - pitch * video.Width) / 2, y0 = top + (areaH - pitch * video.Height) / 2;
        float size = pitch * 0.8f;
        int fi = (int)(Math.Max(0, now - videoStart) * video.Fps) % video.Count;
        var frame = video.Frame(fi);
        if (video.IsColor)
        {
            DrawColorVideo(g, ink, frame, x0, y0, pitch, size);
            return;
        }
        int max = video.MaxLevel;

        for (int y = 0; y < video.Height; y++)
        {
            for (int x = 0; x < video.Width; x++)
            {
                int lv = video.Level(frame, x, y);
                var r = new RectangleF(x0 + x * pitch, y0 + y * pitch, size, size);
                if (lv == 0) ghostDots.Add(r);
                else if (lv == max) dots.Add(r);
                else if (lv * 3 >= max * 2) dots2.Add(r);
                else dots3.Add(r);
            }
        }
        var main = ((SolidBrush)ink.Main).Color;
        Fill(g, ink.Ghost, ghostDots);
        using (var low = new SolidBrush(Color.FromArgb(90, main))) Fill(g, low, dots3);
        using (var mid = new SolidBrush(Color.FromArgb(170, main))) Fill(g, mid, dots2);
        Fill(g, ink.Main, dots);
    }

    /// <summary>The big red dot wedge shown while changing volume.</summary>
    void DrawVolumeWedge(Graphics g, Inks ink)
    {
        const int cols = 40;
        int lit = engine.Muted ? 0 : (int)Math.Round(engine.Volume * cols);
        float span = GW - 60;
        for (int i = 0; i < cols; i++)
        {
            float x = 30 + i * span / cols;
            int h = 3 + (int)(i * 19f / (cols - 1));
            var list = i < lit ? dots : dots2;
            for (int r = 0; r < h; r++)
            {
                list.Add(new RectangleF(x, SpecBase - 4 - r * 6.2f, 4.4f, 4.4f));
                list.Add(new RectangleF(x + 6, SpecBase - 4 - r * 6.2f, 4.4f, 4.4f));
            }
        }
        Fill(g, ink.AccentGhost, dots2);
        Fill(g, ink.Accent, dots);
    }

    void DrawList(Graphics g, Inks ink)
    {
        const float top = 64, rowH = 17, p = 1.8f;
        var view = ListView;
        if (view.Count == 0)
        {
            DotText(g, ink.Main, DotFont.Normalize(Lang.T(tracks.Count == 0 ? "EMPTY" : "NOTHING FOUND")), 20, top, p);
            return;
        }
        listCursor = Math.Clamp(listCursor, 0, view.Count - 1);
        if (listCursor < listTop) listTop = listCursor;
        if (listCursor >= listTop + ListRows) listTop = listCursor - ListRows + 1;
        listTop = Math.Clamp(listTop, 0, Math.Max(0, view.Count - ListRows));

        for (int row = 0; row < ListRows; row++)
        {
            int vi = listTop + row;
            if (vi >= view.Count) break;
            int idx = view[vi];
            float y = top + row * rowH;
            string name = DotFont.Normalize($"{idx + 1:00} {TitleOf(tracks[idx])}");
            name = DotFont.Fit(name, GW - 60, p);
            if (vi == listCursor)
            {
                g.FillRectangle(ink.Main, 16, y - 2, GW - 44, 7 * p + 4);
                DotText(g, ink.Ink, name, 20, y, p);
            }
            else DotText(g, idx == current ? ink.Accent : ink.Main, name, 20, y, p);
            if (idx == current) DotText(g, ink.Accent, DotFont.Play.ToString(), GW - 24, y, p);
        }

        // Scroll bar.
        float trackH = ListRows * rowH;
        g.FillRectangle(ink.Ghost, GW - 12, top - 2, 3, trackH);
        if (view.Count > ListRows)
        {
            float h = trackH * ListRows / view.Count;
            float y = top - 2 + (trackH - h) * listTop / (view.Count - ListRows);
            g.FillRectangle(ink.Main, GW - 12, y, 3, h);
        }
    }

    void DrawProgress(Graphics g, Inks ink)
    {
        const int segs = 72;
        float span = GW - 32, slot = span / segs;
        double dur = player.Duration.TotalSeconds;
        float frac = dur > 0 ? (float)(player.Position.TotalSeconds / dur) : 0;
        int on = (int)(frac * segs);
        for (int i = 0; i < segs; i++)
        {
            var r = new RectangleF(16 + i * slot, ProgressY, slot - 2, 4);
            (i < on ? dots : i == on && dur > 0 ? dots2 : dots2).Add(r);
        }
        Fill(g, ink.Ghost, dots2);
        Fill(g, ink.Main, dots);
        if (dur > 0) g.FillRectangle(ink.Hot, 16 + on * slot, ProgressY - 1, slot - 2, 6);
    }

    void DrawStatus(Graphics g, Inks ink)
    {
        const float p = 1.6f;
        float x = 16;

        void Tag(string s, bool on, Brush onBrush, Pen onPen)
        {
            float w = DotFont.Width(s, p);
            var box = new RectangleF(x, StatusY - 3, w + 7, 7 * p + 6);
            g.DrawRectangle(on ? onPen : ink.GhostPen, box.X, box.Y, box.Width, box.Height);
            DotText(g, on ? onBrush : ink.Ghost, s, x + 4, StatusY, p);
            x += box.Width + 7;
        }

        Tag(repeat == RepeatMode.One ? "RPT1" : "RPT", repeat != RepeatMode.Off, ink.Main, ink.MainPen);
        Tag("SHF", shuffle, ink.Main, ink.MainPen);
        Tag("MUTE", engine.Muted, ink.Accent, ink.AccentPen);
        Tag(vis switch
        {
            Vis.Fan => "FAN", Vis.Grid => "GRID", Vis.Bars => "BAR", Vis.Scope => "SCOPE", Vis.Circle => "CIRC", Vis.Vu => "VU",
            Vis.Cd => "CD", Vis.Fire => "FIRE", Vis.Stars => "STAR", _ => "VID",
        }, true, ink.Main, ink.MainPen);
        Tag(scMode ? "SC" : settingsMode ? "SET" : "LIST", listMode || settingsMode || scMode, ink.Main, ink.MainPen);

        string trk = tracks.Count > 0 ? $"{tracks.Count} TRK" : "";
        DotText(g, ink.Main, trk, x + 6, StatusY, p);
        float extraX = x + 6 + DotFont.Width(trk, p) + 12;
        if (Math.Abs(engine.Speed - 1) > 0.001f)
        {
            DotText(g, ink.Accent, SpeedText(), extraX, StatusY, p);
            extraX += DotFont.Width(SpeedText(), p) + 10;
        }
        string bpm = BpmText;
        if (bpm.Length > 0) DotText(g, ink.Accent, bpm, extraX, StatusY, p);
        if (sleepAt > 0)
        {
            string z = $"Z{Math.Ceiling((sleepAt - now) / 60)}";
            DotText(g, ink.Accent, z, extraX + (bpm.Length > 0 ? DotFont.Width(bpm, p) + 10 : 0), StatusY, p);
        }

        // Volume meter on the right.
        const int vsegs = 20;
        float right = GW - 16;
        string vs = ((int)Math.Round(engine.Volume * 40)).ToString("00");
        DotText(g, ink.Main, vs, right - DotFont.Width(vs, p), StatusY, p);
        float barRight = right - DotFont.Width(vs, p) - 6;
        int von = engine.Muted ? 0 : (int)Math.Round(engine.Volume * vsegs);
        for (int i = 0; i < vsegs; i++)
        {
            float h = 3 + i * 0.4f;
            var r = new RectangleF(barRight - (vsegs - i) * 4.2f, StatusY + 11 - h, 3f, h);
            (i < von ? dots : dots2).Add(r);
        }
        Fill(g, ink.Ghost, dots2);
        Fill(g, ink.Main, dots);
        DotText(g, ink.Main, "VOL", barRight - vsegs * 4.2f - DotFont.Width("VOL", p) - 5, StatusY, p);
    }

    // ───────────────────────────── input ─────────────────────────────

    PointF ToLogical(Point p) => new(p.X / S + MiniOrigin.X, p.Y / S + MiniOrigin.Y);

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Shutting) return;
        lastActivity = now;
        var p = ToLogical(e.Location);
        if (scMode && Glass.Contains(p) && e.Button is MouseButtons.Left or MouseButtons.Right
            && ScClick(ToDisplay(p), e.Button == MouseButtons.Right)) return;
        if (e.Button == MouseButtons.Right && settingsMode && Glass.Contains(p))
        {
            float ly = ToDisplay(p).Y;
            int row = MenuRowAt(ly), idx = setTop + row;
            if (row >= 0 && idx < settings.Count)
            {
                setCursor = idx;
                settings[idx].Change(-1);
            }
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        var b = HitTest(p);
        if (b != null)
        {
            pressed = b.Id;
            Capture = true;
            if (b.Id == Btn.SpeedKnob && e.Clicks >= 2)
            {
                SetSpeed(1f); // double-click snaps back to normal speed
            }
            else if (b.Kind == KeyKind.Knob)
            {
                knobDrag = b.Id;
                knobStartY = p.Y;
                knobStartVal = b.Id == Btn.Knob ? engine.Volume : MathF.Log2(engine.Speed);
            }
            else
            {
                ButtonClick();
                Execute(b.Id);
                if (b.Repeats)
                {
                    repeatTimer.Interval = 380;
                    repeatTimer.Start();
                }
            }
            return;
        }
        if (Glass.Contains(p) && HandleGlassClick(ToDisplay(p), e.Clicks)) return;

        // Anything else drags the window around.
#if MAC
        BeginWindowDrag();
#else
        ReleaseCapture();
        SendMessage(Handle, 0xA1, 2, IntPtr.Zero);
#endif
    }

    bool HandleGlassClick(PointF p, int clicks)
    {
        if (eqMode && EqClick(p)) return true;
        if (miniMode && clicks >= 2 && !(p.Y >= ProgressY - 6 && p.Y <= ProgressY + 10))
        {
            SetMini(false); // double-click the mini display to get the whole radio back
            return true;
        }
        if (p.Y >= ProgressY - 6 && p.Y <= ProgressY + 10 && player.IsLoaded)
        {
            float frac = Math.Clamp((p.X - 16) / (GW - 32), 0, 1);
            player.Seek(TimeSpan.FromSeconds(player.Duration.TotalSeconds * frac));
            return true;
        }
        if (timeBox.Contains(p))
        {
            remainingTime = !remainingTime;
            return true;
        }
        if (settingsMode && MenuRowAt(p.Y) >= 0)
        {
            int idx = setTop + MenuRowAt(p.Y);
            if (idx < settings.Count)
            {
                // Clicking the value side (or an already-selected row) changes it.
                bool change = idx == setCursor || p.X > GW / 2;
                setCursor = idx;
                if (change) settings[idx].Change(+1);
            }
            return true;
        }
        if (listMode && MenuRowAt(p.Y) >= 0 && tracks.Count > 0)
        {
            var view = ListView;
            int vi = listTop + MenuRowAt(p.Y);
            if (vi < view.Count)
            {
                listCursor = vi;
                if (clicks >= 2) PlayIndex(view[vi]);
            }
            return true;
        }
        return false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = ToLogical(e.Location);
        if (knobDrag == Btn.Knob)
        {
            int step = (int)Math.Round(engine.Volume * 40);
            SetVolume(knobStartVal + (knobStartY - p.Y) / 140f);
            if ((int)Math.Round(engine.Volume * 40) != step) KnobClick(); // a detent
            return;
        }
        if (knobDrag == Btn.SpeedKnob)
        {
            float before = engine.Speed;
            SetSpeed(MathF.Pow(2, Math.Clamp(knobStartVal + (knobStartY - p.Y) / 120f, -1, 1)));
            if (engine.Speed != before) KnobClick();
            return;
        }
        var b = HitTest(p);
        hover = b?.Id ?? Btn.None;
        Cursor = b != null ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        pressed = Btn.None;
        knobDrag = Btn.None;
        Capture = false;
        repeatTimer.Stop();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = Btn.None;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int steps = e.Delta / 120;
        var p = ToLogical(e.Location);
        lastActivity = now;
        if (steps != 0 && !(Glass.Contains(p) && (scMode || settingsMode || listMode || eqMode))) KnobClick();
        if (eqMode && Glass.Contains(p)) AdjustBand(eqBand, eqBand == 3 ? 0 : steps);
        else if (scMode && Glass.Contains(p)) scCursor = Math.Clamp(scCursor - steps, 0, Math.Max(0, scRows.Count - 1));
        else if (settingsMode && Glass.Contains(p)) setCursor = Math.Clamp(setCursor - steps, 0, settings.Count - 1);
        else if (listMode && Glass.Contains(p)) listCursor = Math.Clamp(listCursor - steps, 0, Math.Max(0, ListView.Count - 1));
        else if (HitTest(p)?.Id == Btn.SpeedKnob) SetSpeed(engine.Speed + steps * 0.05f);
        else SetVolume(engine.Volume + steps / 40f);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Shutting) return true;
        lastActivity = now;
        if (searching)
        {
            switch (keyData)
            {
                case Keys.Back:
                    if (listFilter.Length > 0) listFilter = listFilter[..^1];
                    listCursor = 0;
                    return true;
                case Keys.Escape: EndSearch(); return true;
                case Keys.Enter:
                    var found = ListView;
                    if (found.Count > 0) PlayIndex(found[Math.Clamp(listCursor, 0, found.Count - 1)]);
                    EndSearch(keepFilter: false);
                    return true;
                case Keys.Up: listCursor = Math.Max(0, listCursor - 1); return true;
                case Keys.Down: listCursor = Math.Min(ListView.Count - 1, listCursor + 1); return true;
            }
            if ((keyData & ~Keys.Shift) is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9 or Keys.Space) return base.ProcessCmdKey(ref msg, keyData); // typed in OnKeyPress
        }
        if (eqMode && EqKey(keyData)) return true;
        if (scMode && ScKey(keyData)) return true;
        if (scMode && scTyping) return base.ProcessCmdKey(ref msg, keyData);
        if (settingsMode)
        {
            switch (keyData)
            {
                case Keys.Up: setCursor = Math.Max(0, setCursor - 1); return true;
                case Keys.Down: setCursor = Math.Min(settings.Count - 1, setCursor + 1); return true;
                case Keys.Left: settings[setCursor].Change(-1); return true;
                case Keys.Right: case Keys.Enter: settings[setCursor].Change(+1); return true;
                case Keys.Escape: case Keys.C:
                    if (settingsSection != null) LeaveSection();
                    else settingsMode = false;
                    return true;
            }
        }
        switch (keyData)
        {
            case Keys.Space: Execute(Btn.Play); return true;
            case Keys.Left: Execute(Btn.SeekBack); return true;
            case Keys.Right: Execute(Btn.SeekFwd); return true;
            case Keys.Up:
                if (listMode) listCursor = Math.Max(0, listCursor - 1); else Execute(Btn.VolUp);
                return true;
            case Keys.Down:
                if (listMode) listCursor = Math.Min(ListView.Count - 1, listCursor + 1); else Execute(Btn.VolDown);
                return true;
            case Keys.Enter:
                if (listMode && ListView is { Count: > 0 } lv) PlayIndex(lv[Math.Clamp(listCursor, 0, lv.Count - 1)]);
                return true;
            case Keys.E: OpenEq(); return true;
            case Keys.D: SetMini(!miniMode); return true;
            case Keys.OemQuestion: case Keys.Control | Keys.F: StartSearch(); return true;
            case Keys.N: Execute(Btn.Next); return true;
            case Keys.P: case Keys.B: Execute(Btn.Prev); return true;
            case Keys.S: Execute(Btn.Stop); return true;
            case Keys.O: Execute(Btn.Open); return true;
            case Keys.F: Execute(Btn.Folder); return true;
            case Keys.L: Execute(Btn.List); return true;
            case Keys.C: Execute(Btn.Settings); return true;
            case Keys.K: OpenSoundCloud(); return true;
            case Keys.V: Execute(Btn.Vis); return true;
            case Keys.M: Execute(Btn.Mute); return true;
            case Keys.H: Execute(Btn.Shuffle); return true;
            case Keys.R: Execute(Btn.Repeat); return true;
            case Keys.T: remainingTime = !remainingTime; return true;
            case Keys.OemOpenBrackets: SetSpeed(engine.Speed - 0.05f); return true;
            case Keys.OemCloseBrackets: SetSpeed(engine.Speed + 0.05f); return true;
            case Keys.OemPipe: SetSpeed(1f); return true;
            case Keys.Escape:
                if (listMode) { listMode = false; EndSearch(); }
                else if (miniMode) SetMini(false);
                else WindowState = FormWindowState.Minimized;
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
        // Radio videos go to the display; everything else is music.
        foreach (var design in paths.Where(p => p.EndsWith(".radio.json", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            ImportDesign(design);
            paths = paths.Where(p => p != design).ToArray();
        }
        var vids = paths.Where(p => Path.GetExtension(p).Equals(".rdv", StringComparison.OrdinalIgnoreCase)).ToList();
        if (vids.Count > 0) LoadVideo(vids[0]);
        var rest = paths.Except(vids).ToList();
        if (rest.Count > 0)
        {
            StartCdAnimation(ToLogical(PointToClient(new Point(e.X, e.Y))));
            AddPaths(rest, playFirstNew: !player.IsPlaying);
        }
    }

    // ───────────────────────────── actions ─────────────────────────────

    void Flash(string s, double seconds = 1.4)
    {
        flashText = Lang.T(s);
        flashStart = now;
        if (s.Length > 20) seconds = Math.Max(seconds, 2 + (s.Length + 20) / 7.0); // time to scroll through once
        flashUntil = now + seconds;
        volOverlayUntil = 0;
    }

    void SetVolume(float v)
    {
        engine.Volume = v;
        if (spotify.Active) spotify.SetVolume(engine.Muted ? 0 : engine.Volume);
        speedOverlay = false;
        volOverlayUntil = now + 1.5;
        flashUntil = 0;
    }

    void SetSpeed(float v, bool overlay = true)
    {
        // Steps of 0.05x with a small detent at normal speed.
        v = MathF.Round(Math.Clamp(v, 0.5f, 2f) * 20) / 20;
        if (Math.Abs(v - 1) < 0.03f) v = 1;
        if (spotify.Active && v != 1)
        {
            Flash("NO SPEED ON SPOTIFY");
            return;
        }
        engine.Speed = v;
        if (!overlay) return;
        speedOverlay = true;
        volOverlayUntil = now + 1.5;
        flashUntil = 0;
    }

    void Execute(Btn id)
    {
        switch (id)
        {
            case Btn.Open: OpenFiles(); break;
            case Btn.Folder: OpenFolder(); break;
            case Btn.List:
                listMode = !listMode;
                settingsMode = scMode = eqMode = false;
                EndSearch();
                listCursor = Math.Max(0, current);
                break;
            case Btn.Settings:
                settingsMode = !settingsMode;
                settingsSection = null;
                eqMode = false;
                if (settingsMode) ReloadDesigns(); // pick up radios installed from Radio Designer
                listMode = scMode = false;
                break;
            case Btn.Vis:
                vis = (Vis)(((int)vis + 1) % VisCount);
                listMode = settingsMode = false;
                Flash(VisName(vis));
                break;
            case Btn.Mute:
                engine.Muted = !engine.Muted;
                if (spotify.Active) spotify.SetVolume(engine.Muted ? 0 : engine.Volume);
                Flash(engine.Muted ? "MUTE ON" : "MUTE OFF");
                break;
            case Btn.VolUp: SetVolume(engine.Volume + 1 / 40f); break;
            case Btn.VolDown: SetVolume(engine.Volume - 1 / 40f); break;
            case Btn.Shuffle:
                shuffle = !shuffle;
                Flash(shuffle ? "SHUFFLE ON" : "SHUFFLE OFF");
                break;
            case Btn.Repeat:
                repeat = (RepeatMode)(((int)repeat + 1) % 3);
                if (spotify.Active) spotify.SetRepeat(repeat switch { RepeatMode.One => "track", RepeatMode.All => "context", _ => "off" });
                Flash(repeat switch { RepeatMode.Off => "REPEAT OFF", RepeatMode.All => "REPEAT ALL", _ => "REPEAT ONE" });
                break;
            case Btn.Stop:
                player.Stop();
                stopped = true;
                break;
            case Btn.Power: Close(); break; // OnFormClosing runs the power-off animation
            case Btn.Eject: OpenFiles(); break; // "insert a disc"
            case Btn.Detach: WindowState = FormWindowState.Minimized; break; // take the faceplate off
            case Btn.Play:
                if (!player.IsLoaded)
                {
                    if (tracks.Count > 0) PlayIndex(Math.Max(current, 0));
                    else OpenFiles();
                }
                else if (stopped)
                {
                    player.Play();
                    stopped = false;
                }
                else player.TogglePause();
                break;
            case Btn.Next: Next(auto: false); break;
            case Btn.Prev: Prev(); break;
            case Btn.SeekBack: player.Seek(player.Position - TimeSpan.FromSeconds(SeekSteps[seekStep])); break;
            case Btn.SeekFwd: player.Seek(player.Position + TimeSpan.FromSeconds(SeekSteps[seekStep])); break;
        }
    }

    void OpenFiles()
    {
        using var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Insert disc",
            Filter = "Audio files|" + string.Join(";", AudioExt.Select(e => "*" + e)) + "|All files|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddPaths(dlg.FileNames, playFirstNew: true);
    }

    void OpenFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Pick a music folder", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddPaths([dlg.SelectedPath], playFirstNew: true);
    }

    static bool IsAudio(string path) => AudioExt.Contains(Path.GetExtension(path).ToLowerInvariant());

    void AddPaths(IEnumerable<string> paths, bool playFirstNew)
    {
        int before = tracks.Count;
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                tracks.AddRange(Directory.EnumerateFiles(p, "*", opts).Where(IsAudio).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            else if (File.Exists(p) && IsAudio(p))
                tracks.Add(p);
        }
        int added = tracks.Count - before;
        if (added == 0)
        {
            Flash("NO AUDIO FILES");
            return;
        }
        Flash(Lang.F(added == 1 ? "{0} TRACK ADDED" : "{0} TRACKS ADDED", added));
        if (playFirstNew) PlayIndex(before);
    }

    static string TitleOf(string path) => RadioBrowser.IsEntry(path) ? RadioBrowser.Parse(path).Name
        : IsOnline(path) ? ParseSc(path).Title : Path.GetFileNameWithoutExtension(path).Replace('_', ' ');

    void PlayIndex(int i)
    {
        if (i < 0 || i >= tracks.Count) return;
        if (filesOnly && IsOnline(tracks[i]))
        {
            // Files only: online entries in the list are skipped.
            i = NextLocal(i);
            if (i < 0)
            {
                Flash("FILES ONLY - ONLINE IS OFF", 2.5);
                LeaveSpotify();
                engine.Eject();
                stopped = true;
                return;
            }
        }
        current = i;
        title = TitleOf(tracks[i]);
        marqueeStart = now;
        RememberPlayed(i);
        if (i != tracks.Count - 1) saidAlmost = false;
        if (RadioBrowser.IsEntry(tracks[i]))
        {
            PlayStation(i);
            return;
        }
        if (IsSc(tracks[i]))
        {
            PlayRemote(i);
            return;
        }
        if (IsSp(tracks[i]))
        {
            PlaySpotify(i);
            return;
        }
        LeaveSpotify();
        playToken++; // cancels any online track still connecting
        try
        {
            engine.Load(tracks[i]);
            player.Play();
            stopped = false;
        }
        catch (Exception)
        {
            stopped = true;
            Flash("READ ERROR");
        }
    }

    void Next(bool auto)
    {
        if (tracks.Count == 0) return;
        if (InSpotifyPlaylist)
        {
            if (!auto) spotify.Skip(forward: true); // Spotify moves through the playlist by itself
            return;
        }
        int n;
        if (shuffle && tracks.Count > 1)
        {
            if (smartShuffle) n = SmartPick();
            else
            {
                do n = rng.Next(tracks.Count); while (n == current);
            }
        }
        else
        {
            n = current + 1;
            if (n >= tracks.Count)
            {
                if (auto && repeat == RepeatMode.Off)
                {
                    player.Stop();
                    stopped = true;
                    return;
                }
                n = 0;
            }
        }
        PlayIndex(n);
    }

    void Prev()
    {
        if (tracks.Count == 0) return;
        if (InSpotifyPlaylist)
        {
            spotify.Skip(forward: false);
            return;
        }
        if (player.Position.TotalSeconds > 3)
        {
            player.Seek(TimeSpan.Zero);
            return;
        }
        PlayIndex(current <= 0 ? tracks.Count - 1 : current - 1);
    }

    void OnTrackFinished()
    {
        if (repeat == RepeatMode.One) PlayIndex(current);
        else Next(auto: true);
    }

    // ───────────────────────────── settings ─────────────────────────────

    static int Wrap(int v, int n) => ((v % n) + n) % n;
    static string OnOff(bool b) => b ? "ON" : "OFF";

    List<Setting> BuildSettings() =>
    [
        new("RADIO MODEL", () => ModelName(Design), d => ApplyStyle(styleIdx + d)),
        new("IMPORT RADIO", () => $"{designs.Count} MODELS", _ => PickDesign()),
        new("COLOR", () => Palettes[paletteIdx].Name, d => paletteIdx = Wrap(paletteIdx + d, Palettes.Length)),
        new("VISUALIZER", () => VisName(vis), d => vis = (Vis)Wrap((int)vis + d, VisCount)),
        new("LANGUAGE", () => Lang.Names[(int)Lang.Current], d =>
        {
            Lang.Current = (Language)Wrap((int)Lang.Current + d, Lang.Names.Length);
            UpdateTitle();
        }),
        new("JDM STYLE", () => OnOff(japaneseGreeting), _ =>
        {
            japaneseGreeting = !japaneseGreeting;
            ReplayBoot(); // show it
        }),
        new("MUSIC SERVICE", () => ServiceNames[serviceIdx], _ => serviceIdx = 1 - serviceIdx),
        new("ONLINE MUSIC", () => svc.SignedIn ? svc.Username ?? "SIGNED IN" : svc.HasKeys ? "SIGN IN" : "SET UP", _ => OpenSoundCloud()),
        new("DISPLAY VIDEO", () => video == null ? "PICK .RDV FILE" : ShortName(videoPath), _ => PickVideo()),
        new("LIGHTING", () => night ? "NIGHT" : "DAY", _ =>
        {
            night = !night;
            if (IsHandleCreated) RebuildFaceplates();
        }),
        new("BRIGHTNESS", () => BrightNames[brightness], d => { brightness = Wrap(brightness + d, 3); UpdateGlow(); }),
        new("FALL SPEED", () => FallNames[fallSpeed], d => fallSpeed = Wrap(fallSpeed + d, 3)),
        new("PEAK HOLD", () => OnOff(showPeaks), _ => showPeaks = !showPeaks),
        new("TIME DISPLAY", () => remainingTime ? "REMAINING" : "ELAPSED", _ => remainingTime = !remainingTime),
        new("LYRICS", () => filesOnly ? "-" : OnOff(lyricsOn), _ =>
        {
            if (BlockedOffline()) return;
            lyricsOn = !lyricsOn;
            if (lyricsOn) Flash("SYNCED LYRICS FROM LRCLIB.NET", 3);
        }),
        new("PLAY SPEED", () => SpeedText(), d => SetSpeed(engine.Speed + d * 0.05f, overlay: false)),
        new("SEEK STEP", () => $"{SeekSteps[seekStep]} SEC", d => seekStep = Wrap(seekStep + d, SeekSteps.Length)),
        new("REPEAT", () => repeat switch { RepeatMode.Off => "OFF", RepeatMode.All => "ALL", _ => "ONE" },
            d => repeat = (RepeatMode)Wrap((int)repeat + d, 3)),
        new("SHUFFLE", () => OnOff(shuffle), _ => shuffle = !shuffle),
        new("STARTUP ANIM", () => BootNames[(int)bootStyle], d =>
        {
            bootStyle = (BootStyle)Wrap((int)bootStyle + d, BootNames.Length);
            if (bootAnim) ReplayBoot(); // show it
        }),
        new("SHUTDOWN ANIM", () => ShutNames[(int)shutStyle], d =>
        {
            shutStyle = (ShutStyle)Wrap((int)shutStyle + d, ShutNames.Length);
            if (shutdownAnim && !Shutting) BeginShutdown(preview: true); // show it; the radio comes back on
        }),
        new("STARTUP SOUND", () => Chimes.Label(startupSound, true), ChangeStartupSound),
        new("SOUND VOLUME", () => chimeVolume == 0 ? "= MUSIC" : $"{chimeVolume * 10}%", d =>
        {
            chimeVolume = Wrap(chimeVolume + d, 11);
            PlayStartupSound(); // hear the new level
        }),
        new("SHUTDOWN SOUND", () => Chimes.Label(shutdownSound, false), d =>
        {
            var order = Chimes.Choices(false);
            shutdownSound = order[Wrap(Math.Max(0, Array.IndexOf(order, shutdownSound)) + d, order.Length)];
            if (shutdownSound != Chimes.Off && !engine.Muted) PlayShutdownSound(); // a preview
        }),
        new("CUSTOM SOUND", () => customSound.Length == 0 ? "PICK FILE" : ShortName(customSound), _ => PickCustomSound()),
        new("WINDOW SIZE", () => $"{SizeScales[sizeIdx] * 100:0}%", d => { sizeIdx = Wrap(sizeIdx + d, SizeScales.Length); ApplyScale(); }),
        new("ALWAYS ON TOP", () => OnOff(onTop), _ => { onTop = !onTop; TopMost = onTop; }),
        new("IDLE DEMO", () => OnOff(idleDemo), _ => idleDemo = !idleDemo),
        new("CLEAR PLAYLIST", () => now < clearArmedUntil ? "PRESS AGAIN" : tracks.Count > 0 ? $"{tracks.Count} TRK" : "EMPTY", _ =>
        {
            // Needs two presses so a stray click can't wipe the playlist.
            if (now < clearArmedUntil) ClearPlaylist();
            else if (tracks.Count > 0) clearArmedUntil = now + 3;
        }),
        new("EXIT", () => "", _ => settingsMode = false),
    ];

    static string ShortName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Length > 16 ? name[..15] + "~" : name;
    }

    void ChangeStartupSound(int d)
    {
        var order = Chimes.Choices(true);
        startupSound = order[Wrap(Math.Max(0, Array.IndexOf(order, startupSound)) + d, order.Length)];
        if (startupSound == Chimes.Custom && !File.Exists(customSound) && !PickCustomSound()) return;
        bootChime = Chimes.Pick(startupSound, true, bootChime);
        PlayStartupSound();
    }

    bool PickCustomSound()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Pick a startup sound",
            Filter = "Audio files|" + string.Join(";", AudioExt.Select(e => "*" + e)) + "|All files|*.*",
        };
#if ANDROID
        // The phone's picker answers later.
        dlg.Picked = files =>
        {
            customSound = files[0];
            startupSound = Chimes.Custom;
            PlayStartupSound();
        };
#endif
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        customSound = dlg.FileName;
        startupSound = Chimes.Custom;
        PlayStartupSound();
        return true;
    }

    /// <summary>Runs the power-on animation again (used as a preview from settings).</summary>
    void ReplayBoot()
    {
        clock.Restart();
        now = lastFrame = lastHistory = marqueeStart = 0;
        flashUntil = volOverlayUntil = 0;
        settingsMode = false;
    }

    void UpdateGlow()
    {
        (glowA1, glowA2) = brightness switch { 0 => (0.45f, 0.3f), 1 => (0.95f, 0.75f), _ => (1f, 1f) };
    }

    void ClearPlaylist()
    {
        clearArmedUntil = 0;
        engine.Eject();
        tracks.Clear();
        current = -1;
        title = "";
        stopped = true;
        Flash("PLAYLIST CLEARED");
    }

    void DrawSettings(Graphics g, Inks ink)
    {
        const float top = 64, rowH = 17, p = 1.8f;
        setCursor = Math.Clamp(setCursor, 0, settings.Count - 1);
        if (setCursor < setTop) setTop = setCursor;
        if (setCursor >= setTop + ListRows) setTop = setCursor - ListRows + 1;

        for (int row = 0; row < ListRows; row++)
        {
            int idx = setTop + row;
            if (idx >= settings.Count) break;
            var item = settings[idx];
            float y = top + row * rowH;
            bool sel = idx == setCursor;
            string val = DotFont.Normalize(Lang.Value(item.Value()));
            if (sel && val.Length > 0) val = "< " + val + " >";
            if (sel) g.FillRectangle(ink.Main, 16, y - 2, GW - 44, 7 * p + 4);
            DotText(g, sel ? ink.Ink : ink.Main, DotFont.Fit(DotFont.Normalize(Lang.T(item.Name)), GW - 60 - DotFont.Width(val, p), p), 20, y, p);
            DotText(g, sel ? ink.Ink : ink.Accent, val, GW - 32 - DotFont.Width(val, p), y, p);
        }

        float trackH = ListRows * rowH;
        g.FillRectangle(ink.Ghost, GW - 12, top - 2, 3, trackH);
        float h = trackH * ListRows / settings.Count;
        float sy = top - 2 + (trackH - h) * setTop / Math.Max(1, settings.Count - ListRows);
        g.FillRectangle(ink.Main, GW - 12, sy, 3, h);
    }

    /// <summary>Power-on intro: every segment lights in a wipe, then the logo types itself out.</summary>
    void BootClassic(Graphics g, Inks ink)
    {
        double t = now;
        if (t < 0.25) return;

        if (t < 1.0)
        {
            // Segment test: all dots ripple on from the center, like a real VFD self-test.
            float radius = (float)((t - 0.25) / 0.55) * (GW / 2 + 60);
            for (float x = 10; x < GW - 8; x += 6)
            {
                for (float y = 8; y < GH - 8; y += 6)
                {
                    float dx = x - GW / 2, dy = (y - GH / 2) * 2.2f;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist < radius) (dist > radius - 50 ? dots2 : dots).Add(new RectangleF(x, y, 3.4f, 3.4f));
                }
            }
            Fill(g, ink.Reflection, dots);
            Fill(g, ink.Hot, dots2);
            return;
        }

        // Brief blackout between the test and the logo.
        if (t < 1.12) return;

        string logo = DotFont.Normalize(Design.Brand);
        const float lp = 5.2f;
        float lw = DotFont.Width(logo, lp), lx = (GW - lw) / 2, ly = 58;
        int shown = Math.Min(logo.Length, (int)((t - 1.12) / 0.08) + 1);
        DotText(g, ink.Main, logo[..shown], lx, ly, lp);
        if (shown < logo.Length || t < 1.75)
        {
            // The newest letter flares white.
            DotText(g, ink.Hot, logo[shown - 1].ToString(), lx + (shown - 1) * 6 * lp, ly, lp);
        }

        // Scan bar sweeping under the logo.
        float lineY = ly + 7 * lp + 12;
        float prog = (float)Math.Clamp((t - 1.2) / 0.55, 0, 1);
        const int segs = 60;
        float slot = (GW - 80) / segs;
        for (int i = 0; i < segs; i++)
            (i < prog * segs ? dots : dots2).Add(new RectangleF(40 + i * slot, lineY, slot - 2, 3));
        Fill(g, ink.Ghost, dots2);
        Fill(g, ink.Accent, dots);

        if (t > 1.6)
        {
            string tag = BootTagline;
            const float tp = 2.2f;
            int chars = Math.Min(tag.Length, (int)((t - 1.6) / 0.022));
            DotText(g, ink.Main, tag[..chars], (GW - DotFont.Width(tag, tp)) / 2, lineY + 22, tp);
        }
        if (t > 1.9)
        {
            string model = DotFont.Normalize(Design.Model);
            DotText(g, ink.Accent, model, (GW - DotFont.Width(model, 1.6f)) / 2, lineY + 52, 1.6f);
        }
    }

    // ───────────────────────────── persistence ─────────────────────────────

    void LoadState()
    {
        try
        {
            if (!File.Exists(StatePath)) return;
            foreach (var line in File.ReadAllLines(StatePath))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line[..eq], v = line[(eq + 1)..];
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                switch (k)
                {
                    case "vol": engine.Volume = float.Parse(v, inv); break;
                    case "speed": engine.Speed = Math.Clamp(float.Parse(v, inv), 0.5f, 2f); break;
                    case "style": pendingStyle = v; break;
                    case "pal": paletteIdx = Math.Clamp(int.Parse(v), 0, Palettes.Length - 1); break;
                    case "vis": vis = (Vis)Math.Clamp(int.Parse(v), 0, VisCount - 1); break;
                    case "video": LoadVideo(v, quiet: true); break;
                    case "shuffle": shuffle = v == "1"; break;
                    case "repeat": repeat = (RepeatMode)Math.Clamp(int.Parse(v), 0, 2); break;
                    case "rtime": remainingTime = v == "1"; break;
                    case "current": current = int.Parse(v); break;
                    case "service": serviceIdx = v == "SPOTIFY" ? 1 : 0; break;
                    case "night": night = v == "1"; break;
                    case "bright": brightness = Math.Clamp(int.Parse(v), 0, 2); break;
                    case "fall": fallSpeed = Math.Clamp(int.Parse(v), 0, 2); break;
                    case "peak": showPeaks = v == "1"; break;
                    case "seek": seekStep = Math.Clamp(int.Parse(v), 0, SeekSteps.Length - 1); break;
                    case "size": sizeIdx = Math.Clamp(int.Parse(v), 0, SizeScales.Length - 1); break;
                    case "top": onTop = v == "1"; break;
                    case "boot": bootStyle = v == "1" ? BootStyle.Classic : BootStyle.Off; break; // older versions
                    case "bootstyle": if (Enum.TryParse<BootStyle>(v, out var bs)) bootStyle = bs; break;
                    case "shutstyle": if (Enum.TryParse<ShutStyle>(v, out var ss)) shutStyle = ss; break;
                    case "demo": idleDemo = v == "1"; break;
                    case "lyrics": lyricsOn = v == "1"; break;
                    case "filesonly": filesOnly = v == "1"; break;
                    case "fps": frameRate = Math.Clamp(int.TryParse(v, out var fr) ? fr : 0, 0, 2); break;
                    case "shutanim": shutStyle = v == "1" ? ShutStyle.TvOff : ShutStyle.Off; break; // older versions
                    case "offsound": shutdownSound = v == "1" ? "systemoff" : Chimes.Off; break; // older versions
                    case "startsound": startupSound = Chimes.Choices(true).Contains(v) ? v : startupSound; break;
                    case "stopsound": shutdownSound = Chimes.Choices(false).Contains(v) ? v : shutdownSound; break;
                    case "lang": if (Enum.TryParse<Language>(v, out var lang)) Lang.Current = lang; break;
                    case "greetjp": japaneseGreeting = v == "1"; break;
                    case "chimevol": chimeVolume = Math.Clamp(int.Parse(v), 0, 10); break;
                    case "sound": startupSound = v switch { "0" => Chimes.Off, "5" => Chimes.Custom, _ => "welcome" }; break; // older versions
                    case "soundfile": customSound = v; break;
                    case "eq":
                        var eqv = v.Split(',').Select(x => float.Parse(x, inv)).ToArray();
                        (eqBass, eqMid, eqTreble) = (eqv[0], eqv[1], eqv[2]);
                        break;
                    case "eqpreset": eqPreset = Math.Clamp(int.Parse(v), 0, EqPresets.Length - 1); break;
                    case "loud": loudness = v == "1"; break;
                    case "reverb": if (Enum.TryParse<ReverbMode>(v, out var rv)) reverbMode = rv; break;
                    case "outside": outsideCar = v == "1"; break;
                    case "xfade": crossfadeIdx = Math.Clamp(int.Parse(v), 0, CrossfadeSteps.Length - 1); break;
                    case "btnsound": buttonSound = v; break;
                    case "knobsound": knobSound = v; break;
                    case "clickvol": clickVolume = Math.Clamp(int.Parse(v), 1, 10); break;
                    case "smartshuffle": smartShuffle = v == "1"; break;
                    case "bpm": bpmOn = v == "1"; break;
                    case "clock": clockIdle = v == "1"; break;
                    case "twocolor": twoColor = v == "1"; break;
                    case "pulse": bassPulse = v == "1"; break;
                    case "autonight": autoNight = v == "1"; break;
                    case "flip": flipScreen = v == "1"; break;
                    case "voice": voiceOn = v == "1"; break;
                    case "voiceevery": voiceEvery = Math.Clamp(int.Parse(v), 0, VoiceSteps.Length - 1); break;
                    case "mini": miniMode = v == "1"; break;
                    case "tray": trayOn = v == "1"; break;
                    case "track": if (IsOnline(v) || File.Exists(v)) tracks.Add(v); break;
                }
            }
            current = tracks.Count == 0 ? -1 : Math.Clamp(current, 0, tracks.Count - 1);
            if (current >= 0) title = TitleOf(tracks[current]);
        }
        catch
        {
            // A corrupt state file just means a fresh start.
        }
    }

    /// <summary>The lyric line being sung now (and the next one), when lyrics are on and the song was found.</summary>
    (string Line, string Next)? CurrentLyric()
    {
        if (!lyricsOn || filesOnly || stopped || !player.IsPlaying || current < 0 || current >= tracks.Count || engine.IsLive) return null;
        string artist, song;
        var meta = IsOnline(tracks[current]) ? null : TrackMeta.For(tracks[current]);
        if (meta is { Title.Length: > 0 }) (artist, song) = (meta.Artist, meta.Title);
        else
        {
            // Online tracks and untagged files are named "Artist - Title".
            int dash = title.IndexOf(" - ", StringComparison.Ordinal);
            (artist, song) = dash > 0 ? (title[..dash], title[(dash + 3)..]) : ("", title);
        }
        artist = artist.Split(", ")[0]; // the main artist finds the song best
        var (_, lyrics) = LyricsService.For(artist, song, player.Duration.TotalSeconds);
        return lyrics?.At(player.Position.TotalSeconds + 0.25);
    }

    string sharedNowPlaying = " ";

    /// <summary>Writes the playing song for other programs (Retro Dash shows it on its cluster).</summary>
    void ShareNowPlaying()
    {
        string np = !stopped && (player.IsPlaying || spotify.IsPlaying) ? title : "";
        if (np == sharedNowPlaying || snapshotPath != null) return;
        sharedNowPlaying = np;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroRadio");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "nowplaying.txt"), np);
        }
        catch (IOException) { }
    }

    void SaveState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var lines = new List<string>
            {
                "vol=" + engine.Volume.ToString(inv),
                "speed=" + engine.Speed.ToString(inv),
                "style=" + Design.Name,
                "pal=" + paletteIdx,
                "vis=" + (int)vis,
                "shuffle=" + (shuffle ? 1 : 0),
                "repeat=" + (int)repeat,
                "rtime=" + (remainingTime ? 1 : 0),
                "current=" + current,
                "service=" + ServiceNames[serviceIdx],
                "night=" + (night ? 1 : 0),
                "bright=" + brightness,
                "fall=" + fallSpeed,
                "peak=" + (showPeaks ? 1 : 0),
                "seek=" + seekStep,
                "size=" + sizeIdx,
                "top=" + (onTop ? 1 : 0),
                "bootstyle=" + bootStyle,
                "demo=" + (idleDemo ? 1 : 0),
                "lyrics=" + (lyricsOn ? 1 : 0),
                "filesonly=" + (filesOnly ? 1 : 0),
                "fps=" + frameRate,
                "shutstyle=" + shutStyle,
                "startsound=" + startupSound,
                "stopsound=" + shutdownSound,
                "lang=" + Lang.Current,
                "greetjp=" + (japaneseGreeting ? 1 : 0),
                "chimevol=" + chimeVolume,
                "soundfile=" + customSound,
                "eq=" + string.Join(",", new[] { eqBass, eqMid, eqTreble }.Select(x => x.ToString(inv))),
                "eqpreset=" + eqPreset,
                "loud=" + (loudness ? 1 : 0),
                "reverb=" + reverbMode,
                "outside=" + (outsideCar ? 1 : 0),
                "xfade=" + crossfadeIdx,
                "btnsound=" + buttonSound,
                "knobsound=" + knobSound,
                "clickvol=" + clickVolume,
                "smartshuffle=" + (smartShuffle ? 1 : 0),
                "bpm=" + (bpmOn ? 1 : 0),
                "clock=" + (clockIdle ? 1 : 0),
                "twocolor=" + (twoColor ? 1 : 0),
                "pulse=" + (bassPulse ? 1 : 0),
                "autonight=" + (autoNight ? 1 : 0),
                "flip=" + (flipScreen ? 1 : 0),
                "voice=" + (voiceOn ? 1 : 0),
                "voiceevery=" + voiceEvery,
                "mini=" + (miniMode ? 1 : 0),
                "tray=" + (trayOn ? 1 : 0),
                "video=" + videoPath,
            };
            lines.AddRange(tracks.Select(t => "track=" + t));
            File.WriteAllLines(StatePath, lines);
        }
        catch
        {
            // Not being able to save settings shouldn't stop the app from closing.
        }
    }

    /// <summary>The power-on word: the Japanese greeting that goes with the sound, or HELLO in the radio's language.</summary>
    string HelloText => japaneseGreeting ? bootChime?.Greeting ?? "こんにちは" : Lang.Hello;

    string ByeText => japaneseGreeting ? offChime?.Greeting ?? "さようなら" : Lang.Goodbye;

    void UpdateTitle() => Text = "Retro Radio";

    /// <summary>Plays the power-off sound; returns how long it lasts.</summary>
    double PlayShutdownSound()
    {
        try
        {
            offChime = Chimes.Pick(shutdownSound, false, offChime);
            return offChime != null && Chimes.FilePath(offChime) is { } file ? engine.PlayEffect(file, ChimeVolume) : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    void BeginShutdown(bool preview = false)
    {
        shutdownPreview = preview;
        if (preview)
        {
            shutdownAt = now;
            settingsMode = false;
            return;
        }
        if (shutdownSound == Chimes.Off || engine.Muted) offChime = Chimes.Pick(shutdownSound, false);
        shutdownAt = now;
        shutdownSoundEnd = shutdownSound != Chimes.Off && !engine.Muted ? now + PlayShutdownSound() : 0;
        settingsMode = listMode = false;
        pressed = hover = Btn.None;
        repeatTimer.Stop();
    }

    void UpdateShutdown()
    {
        double t = now - shutdownAt;
        if (shutdownPreview)
        {
            // Just showing the effect from settings: switch back on when it's done.
            if (t >= ShutLen + 0.4)
            {
                shutdownAt = -1;
                shutdownPreview = false;
                settingsMode = true;
            }
            return;
        }
        engine.FadeGain = (float)Math.Clamp(1 - t / 1.0, 0, 1);
        if (t >= 1.0 && player.IsPlaying) player.Pause();
        // No power-off animation, only the sound: disappear right away and quit when it ends.
        if (!shutdownAnim && !shutdownHidden)
        {
            shutdownHidden = true;
            Hide();
        }
        if (t >= ShutLen || !shutdownAnim)
        {
            // The power-off sound can outlast the animation: hide the radio and let it finish first.
            if (now < shutdownSoundEnd)
            {
                if (!shutdownHidden)
                {
                    shutdownHidden = true;
                    Hide();
                }
                return;
            }
            shutdownDone = true;
            Close();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Play the power-off animation first; it calls Close() again when it's done.
        if (e.CloseReason == CloseReason.UserClosing && (shutdownAnim || shutdownSound != Chimes.Off) && !shutdownDone && !snapshotRun
            && WindowState != FormWindowState.Minimized)
        {
            e.Cancel = true;
            if (!Shutting || shutdownPreview) BeginShutdown();
            return;
        }
        if (snapshotPath == null && !Environment.GetCommandLineArgs().Contains("--snapshot")) SaveState();
        frameTimer.Stop();
        engine.Dispose();
        clicks.Dispose();
        voice.Dispose();
#if !MAC
        tray?.Dispose();
#endif
        base.OnFormClosing(e);
    }

    // ───────────────────────────── helpers ─────────────────────────────

    static RectangleF Inset(Bitmap b) => new(0.5f, 0.5f, b.Width - 1, b.Height - 1);

    static ImageAttributes MakeWrap()
    {
        var a = new ImageAttributes();
        a.SetWrapMode(WrapMode.TileFlipXY);
        return a;
    }

    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>All brushes for the current display color scheme.</summary>
    sealed class Inks : IDisposable
    {
        public readonly Palette Palette;
        public readonly bool Low;
        public readonly SolidBrush Main, Hot, Ghost, Accent, AccentGhost, Reflection, Ink;
        public readonly Pen MainPen, GhostPen, AccentPen;

        public Inks(Palette pal, bool low)
        {
            Palette = pal;
            Low = low;
            // Low brightness just dims both display colors.
            var p = low ? new Palette(pal.Name, Dim(pal.Main), Dim(pal.Accent)) : pal;
            Main = new SolidBrush(p.Main);
            Hot = new SolidBrush(Blend(p.Main, Color.White, 0.5f));
            Ghost = new SolidBrush(Color.FromArgb(26, p.Main));
            Accent = new SolidBrush(p.Accent);
            AccentGhost = new SolidBrush(Color.FromArgb(34, p.Accent));
            Reflection = new SolidBrush(Color.FromArgb(55, p.Main));
            Ink = new SolidBrush(Color.FromArgb(4, 8, 12));
            MainPen = new Pen(p.Main, 1f);
            GhostPen = new Pen(Color.FromArgb(34, p.Main), 1f);
            AccentPen = new Pen(p.Accent, 1f);
        }

        static Color Dim(Color c) => Color.FromArgb((int)(c.R * 0.62f), (int)(c.G * 0.62f), (int)(c.B * 0.62f));

        static Color Blend(Color a, Color b, float t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        public void Dispose()
        {
            foreach (var b in new[] { Main, Hot, Ghost, Accent, AccentGhost, Reflection, Ink }) b.Dispose();
            MainPen.Dispose(); GhostPen.Dispose(); AccentPen.Dispose();
        }
    }
}
