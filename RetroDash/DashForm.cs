using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;
using RetroRadio;

namespace RetroDash;

enum LightMode { Auto, Day, Night }

sealed class DashSettings
{
    public ClusterStyle Style { get; set; } = ClusterStyle.Analog;
    public bool Demo { get; set; }
    public bool Mph { get; set; }
    public LightMode Light { get; set; } = LightMode.Auto;
    public int Accent { get; set; }
    public float Size { get; set; } = 0.9f;
    public bool OnTop { get; set; }
    public bool Welcomed { get; set; }

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroDash", "settings.json");

    public static DashSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<DashSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception) { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { }
    }
}

/// <summary>The cluster window: a timer drives telemetry and animation; right-click for everything else.</summary>
sealed class DashForm : Form
{
    public static readonly (string Name, Color? Color)[] Accents =
    [
        ("Style default", null),
        ("Ice blue", Color.FromArgb(56, 189, 248)),
        ("VFD teal", Color.FromArgb(93, 242, 220)),
        ("Racing red", Color.FromArgb(255, 64, 56)),
        ("Amber", Color.FromArgb(255, 150, 40)),
        ("Acid green", Color.FromArgb(120, 255, 90)),
        ("Violet", Color.FromArgb(170, 120, 255)),
        ("Hot pink", Color.FromArgb(255, 80, 180)),
    ];

    static readonly (string Name, float Size)[] Sizes = [("Small", 0.65f), ("Medium", 0.9f), ("Large", 1.2f)];

    readonly Telemetry tel = new();
    readonly Cluster cluster = new();
    readonly DashSettings set = DashSettings.Load();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    double last;

    public DashForm()
    {
        Text = "Retro Dash";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        Icon = MakeIcon();
        KeyPreview = true;

        if (!set.Welcomed)
        {
            cluster.Hint = "Right-click for styles, demo drive and more";
            set.Welcomed = true;
        }
        ApplySettings();
        ContextMenuStrip = new ContextMenuStrip();
        ContextMenuStrip.Opening += (_, _) => BuildMenu(ContextMenuStrip);
        BuildMenu(ContextMenuStrip);
        MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) CycleStyle(); };

        timer.Tick += (_, _) => Tick();
        timer.Start();
        ClassicFrame.Apply(this);
        ClientSize = DesignSize();
    }

    Size DesignSize()
    {
        float k = DeviceDpi / 96f * set.Size;
        return new Size((int)(Cluster.W * k), (int)(Cluster.H * k));
    }

    void ApplySettings()
    {
        tel.Demo = set.Demo;
        cluster.Style = set.Style;
        cluster.Demo = set.Demo;
        cluster.Mph = set.Mph;
        cluster.AccentOverride = Accents[Math.Clamp(set.Accent, 0, Accents.Length - 1)].Color;
        cluster.Night = IsNight();
        TopMost = set.OnTop;
        if (Owner != null) Owner.TopMost = set.OnTop; // the glass frame
        set.Save();
    }

    bool IsNight() => set.Light switch
    {
        LightMode.Day => false,
        LightMode.Night => true,
        _ => DateTime.Now.Hour is >= 19 or < 7,
    };

    void Tick()
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = Math.Min(0.1, now - last);
        last = now;
        tel.Update(dt);
        cluster.Night = IsNight();
        cluster.Step(tel.State, dt);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            cluster.Draw(e.Graphics, ClientSize, tel.State);
        }
        catch (ArgumentException)
        {
            // A resize mid-paint; the next frame repaints.
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.S: CycleStyle(); break;
            case Keys.D: set.Demo = !set.Demo; ApplySettings(); break;
            case Keys.U: set.Mph = !set.Mph; ApplySettings(); break;
            case Keys.N: set.Light = IsNight() ? LightMode.Day : LightMode.Night; ApplySettings(); break;
            case Keys.T: set.OnTop = !set.OnTop; ApplySettings(); break;
            case Keys.R: tel.ResetTrip(); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    void CycleStyle()
    {
        set.Style = (ClusterStyle)(((int)set.Style + 1) % 3);
        ApplySettings();
        cluster.Ignition();
    }

    void BuildMenu(ContextMenuStrip m)
    {
        m.Items.Clear();
        ToolStripMenuItem Item(string text, bool check, Action a) => new(text, null, (_, _) => { a(); ApplySettings(); }) { Checked = check };

        var style = new ToolStripMenuItem("Cluster style");
        (ClusterStyle, string)[] styles = [(ClusterStyle.Digital, "80s digital (VFD)"), (ClusterStyle.Analog, "Classic analog"), (ClusterStyle.Modern, "Modern digital")];
        foreach (var (st, name) in styles)
            style.DropDownItems.Add(Item(name, set.Style == st, () => { set.Style = st; cluster.Ignition(); }));
        m.Items.Add(style);

        var data = new ToolStripMenuItem("Drive with");
        data.DropDownItems.Add(Item("My PC (live)", !set.Demo, () => set.Demo = false));
        data.DropDownItems.Add(Item("Demo drive", set.Demo, () => set.Demo = true));
        m.Items.Add(data);

        var light = new ToolStripMenuItem("Lighting");
        foreach (var lm in Enum.GetValues<LightMode>())
            light.DropDownItems.Add(Item(lm == LightMode.Auto ? "Auto (night after 7 pm)" : lm.ToString(), set.Light == lm, () => set.Light = lm));
        m.Items.Add(light);

        var color = new ToolStripMenuItem("Color");
        for (int i = 0; i < Accents.Length; i++)
        {
            int idx = i;
            var it = Item(Accents[i].Name, set.Accent == i, () => set.Accent = idx);
            if (Accents[i].Color is { } c) it.Image = Swatch(c);
            color.DropDownItems.Add(it);
        }
        m.Items.Add(color);

        var units = new ToolStripMenuItem("Units");
        units.DropDownItems.Add(Item("km/h", !set.Mph, () => set.Mph = false));
        units.DropDownItems.Add(Item("mph", set.Mph, () => set.Mph = true));
        m.Items.Add(units);

        var size = new ToolStripMenuItem("Size");
        foreach (var (name, k) in Sizes)
            size.DropDownItems.Add(Item(name, Math.Abs(set.Size - k) < 0.01f, () => { set.Size = k; ClientSize = DesignSize(); }));
        m.Items.Add(size);

        m.Items.Add(Item("Always on top", set.OnTop, () => set.OnTop = !set.OnTop));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem("Reset trip", null, (_, _) => tel.ResetTrip()));
        m.Items.Add(new ToolStripMenuItem("What do the gauges show?", null, (_, _) => Explain()));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Close()));
    }

    static Bitmap Swatch(Color c)
    {
        var bmp = new Bitmap(14, 14);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var b = new SolidBrush(c);
        g.FillEllipse(b, 1, 1, 12, 12);
        return bmp;
    }

    void Explain() => MessageBox.Show(this,
        "Retro Dash drives on your PC:\n\n" +
        "Rev counter  =  CPU load (idle ~800 rpm, redline = flat out)\n" +
        "Speedometer  =  network speed, download + upload\n" +
        "Fuel  =  free memory\n" +
        "Temperature  =  CPU load over the last minute\n" +
        "Odometer  =  all network data ever (1 km per MB), Trip since start\n" +
        "Indicators  =  downloading (left) and uploading (right)\n" +
        "High beam  =  over 5 MB/s   ·   Check engine  =  CPU maxed for 5 s\n" +
        "Battery lamp  =  laptop battery under 20% and unplugged\n\n" +
        "The info display also shows the song playing in Retro Radio.\n\n" +
        "Keys: S style · D demo drive · N day/night · U units · T on top · R reset trip\n" +
        "Double-click to change style.",
        "Retro Dash", MessageBoxButtons.OK, MessageBoxIcon.Information);

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var ring = new Pen(Color.FromArgb(220, 224, 230), 6);
            using var red = new Pen(Color.FromArgb(230, 40, 30), 6);
            using var needle = new Pen(Color.FromArgb(255, 90, 30), 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var face = new SolidBrush(Color.FromArgb(20, 20, 24));
            g.FillEllipse(face, 4, 4, 56, 56);
            g.DrawArc(ring, 8, 8, 48, 48, 135, 210);
            g.DrawArc(red, 8, 8, 48, 48, 345, 60);
            g.DrawLine(needle, 32, 34, 49, 17);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        timer.Stop();
        tel.Save();
        set.Save();
        cluster.Dispose();
        base.OnFormClosing(e);
    }

    /// <summary>Renders the cluster off-screen after a stretch of demo driving (for the launcher picture and tests).</summary>
    public static void Snapshot(string path, ClusterStyle style, bool night, double seconds, float scale)
    {
        var tel = new Telemetry { Demo = true };
        using var cluster = new Cluster { Style = style, Night = night, Demo = true };
        const double dt = 1 / 60.0;
        for (double t = 0; t < seconds; t += dt)
        {
            tel.Update(dt);
            cluster.Step(tel.State, dt);
        }
        tel.State.NowPlaying = "Test Artist - Night Drive";
        var px = new Size((int)(Cluster.W * scale), (int)(Cluster.H * scale));
        using var bmp = new Bitmap(px.Width, px.Height);
        using (var g = Graphics.FromImage(bmp)) cluster.Draw(g, px, tel.State);
        bmp.Save(path);
    }
}
