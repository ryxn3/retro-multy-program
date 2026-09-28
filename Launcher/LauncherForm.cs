using System.Drawing.Drawing2D;
using System.Reflection;
using RetroRadio;

namespace RetroLauncher;

/// <summary>The main window: one card per program, with install / update / open.</summary>
sealed class LauncherForm : Form
{
    public static readonly Color Back = Color.FromArgb(13, 14, 18);
    public static readonly Color Card = Color.FromArgb(24, 26, 32);
    public static readonly Color Edge = Color.FromArgb(46, 49, 58);
    public static readonly Color Fore = Color.FromArgb(226, 230, 238);
    public static readonly Color Dim = Color.FromArgb(140, 146, 160);
    public static readonly Color Cyan = Color.FromArgb(110, 235, 255);
    public static readonly Color Amber = Color.FromArgb(255, 186, 70);

    public static readonly AppInfo[] Apps =
    [
        new("radio", "Retro Radio", "The music player",
            "A car-stereo music player with 39 radio models, day and night lighting, a dot-matrix display " +
            "with 3D spectrum analyzers and videos, speed and volume knobs, SoundCloud and Spotify.",
            "RetroRadio-win-x64.zip", "RetroRadio.exe", "radio.png"),
        new("converter", "Radio Video Converter", "Videos for the radio display",
            "Turns any video into a dot-matrix animation (.rdv) that plays on the radio's display, with a live preview. " +
            "Needs ffmpeg (free, from ffmpeg.org).",
            "RadioVideoConverter-win-x64.zip", "RadioVideoConverter.exe", "converter.png"),
        new("designer", "Radio Designer", "Build your own radio",
            "Design a complete custom radio — size, shape, finish, materials, colors, fonts, the display and every key " +
            "and knob — then install it straight into Retro Radio.",
            "RadioDesigner-win-x64.zip", "RadioDesigner.exe", "designer.png"),
        new("lens", "Retro Lens", "Retro photo & video editor",
            "Make photos and videos look like they came from a Nintendo DSi, Game Boy Camera, VHS camcorder, Super 8, " +
            "Polaroid, CRT, fisheye skate cam and 25 more — with 40+ settings to tweak, your own presets, and MP4/GIF export.",
            "RetroLens-win-x64.zip", "RetroLens.exe", "lens.png"),
        new("dash", "Retro Dash", "Car gauges for your PC",
            "A glowing car instrument cluster driven by your PC: CPU revs the tachometer, network speed moves the " +
            "speedometer, memory is fuel. 80s digital, classic analog and modern styles, day and night lighting, " +
            "a demo drive, and it shows the song playing in Retro Radio.",
            "RetroDash-win-x64.zip", "RetroDash.exe", "dash.png"),
        new("garage", "JDM Garage", "Dekotora & JDM car collection",
            "Walk through a neon-lit garage of JDM legends and glowing dekotora art trucks — AE86, R34, Supra, RX-7, " +
            "a bosozoku kaido racer, a kei van and more. Every vehicle has its own radio fitted: press play to open " +
            "Retro Radio in that car's radio.",
            "JdmGarage-win-x64.zip", "JdmGarage.exe", "garage.png"),
    ];

    const string LauncherAsset = "RetroLauncher-win-x64.zip";

    readonly List<AppCard> cards = [];
    readonly Button btnCheck = new();
    readonly Label lblStatus = new();
    readonly Panel banner = new();
    readonly Label bannerText = new();
    readonly Button bannerButton = new();
    readonly LinkLabel lnkNotes = new(), lnkRepo = new(), lnkFolder = new();
    Release? release;
    bool checking;

    public LauncherForm()
    {
        Text = "Retro Multy Program";
        BackColor = Back;
        ForeColor = Fore;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1080, 880);
        MinimumSize = new Size(900, 600);
        DoubleBuffered = true;

        var header = new HeaderPanel { Dock = DockStyle.Top, Height = 118 };
        Controls.Add(header);

        btnCheck.Text = "Check for updates";
        Style(btnCheck, primary: false);
        btnCheck.Size = new Size(170, 34);
        btnCheck.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnCheck.Location = new Point(header.Width - 190, 26);
        btnCheck.Click += async (_, _) => await CheckAsync();
        header.Controls.Add(btnCheck);
        lblStatus.AutoSize = false;
        lblStatus.TextAlign = ContentAlignment.TopRight;
        lblStatus.Size = new Size(360, 40);
        lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblStatus.Location = new Point(header.Width - 380, 66);
        lblStatus.ForeColor = Dim;
        lblStatus.BackColor = Color.Transparent;
        header.Controls.Add(lblStatus);

        banner.Dock = DockStyle.Top;
        banner.Height = 46;
        banner.BackColor = Color.FromArgb(52, 40, 12);
        banner.Visible = false;
        bannerText.AutoSize = false;
        bannerText.Dock = DockStyle.Fill;
        bannerText.TextAlign = ContentAlignment.MiddleLeft;
        bannerText.Padding = new Padding(18, 0, 0, 0);
        bannerText.ForeColor = Amber;
        bannerButton.Text = "Update launcher";
        Style(bannerButton, primary: true);
        bannerButton.Dock = DockStyle.Right;
        bannerButton.Width = 170;
        bannerButton.Click += async (_, _) => await UpdateLauncherAsync();
        banner.Controls.Add(bannerText);
        banner.Controls.Add(bannerButton);
        Controls.Add(banner);
        banner.BringToFront();
        header.SendToBack();

        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(22, 16, 22, 16),
            BackColor = Back,
        };
        foreach (var app in Apps)
        {
            var card = new AppCard(app, this);
            cards.Add(card);
            list.Controls.Add(card);
        }
        list.Resize += (_, _) =>
        {
            foreach (var c in cards) c.Width = list.ClientSize.Width - list.Padding.Horizontal - 4;
        };
        Controls.Add(list);
        list.BringToFront();

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Color.FromArgb(18, 19, 24) };
        var ver = new Label
        {
            Text = $"Launcher v{Program.Version.ToString(3)}",
            ForeColor = Dim,
            AutoSize = true,
            Location = new Point(22, 12),
        };
        Link(lnkNotes, "What's new", 170, (_, _) => ShowNotes());
        Link(lnkFolder, "Open install folder", 280, (_, _) =>
        {
            Directory.CreateDirectory(Installer.Root);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Installer.Root) { UseShellExecute = true });
        });
        Link(lnkRepo, "github.com/" + GitHub.Owner + "/" + GitHub.Repo, 440, (_, _) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(GitHub.RepoUrl) { UseShellExecute = true }));
        footer.Controls.AddRange([ver, lnkNotes, lnkFolder, lnkRepo]);
        Controls.Add(footer);

        Shown += async (_, _) =>
        {
            ActiveControl = null;
            list.AutoScrollPosition = Point.Empty;
            await CheckAsync();
        };
        foreach (var c in cards) c.UpdateState(null, "Checking GitHub…");
        ClassicFrame.Apply(this);
    }

    void Link(LinkLabel l, string text, int x, LinkLabelLinkClickedEventHandler click)
    {
        l.Text = text;
        l.AutoSize = true;
        l.Location = new Point(x, 12);
        l.LinkColor = Cyan;
        l.ActiveLinkColor = Color.White;
        l.LinkBehavior = LinkBehavior.HoverUnderline;
        l.LinkClicked += click;
    }

    public static void Style(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.Cursor = Cursors.Hand;
        b.Font = new Font("Segoe UI", 9.5f, primary ? FontStyle.Bold : FontStyle.Regular);
        b.BackColor = primary ? Color.FromArgb(22, 110, 132) : Color.FromArgb(34, 37, 45);
        b.ForeColor = primary ? Color.White : Fore;
        b.FlatAppearance.BorderColor = primary ? Color.FromArgb(60, 190, 220) : Edge;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(30, 138, 164) : Color.FromArgb(44, 48, 58);
    }

    public Release? CurrentRelease => release;

    public async Task CheckAsync()
    {
        if (checking) return;
        checking = true;
        btnCheck.Enabled = false;
        lblStatus.Text = "Checking GitHub for updates…";
        string? error = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            release = await GitHub.LatestAsync(cts.Token);
        }
        catch (Exception ex)
        {
            error = ex is HttpRequestException or TaskCanceledException ? "Couldn't reach GitHub (offline?)" : ex.Message;
        }
        finally
        {
            checking = false;
            btnCheck.Enabled = true;
        }

        if (error != null) lblStatus.Text = error;
        else if (release == null) lblStatus.Text = "No release has been published on GitHub yet.";
        else
        {
            int updates = Apps.Count(a => Installer.Installed(a) is { } i && release.Asset(a.AssetName) is { } asset && asset.Id != i.AssetId);
            lblStatus.Text = $"Latest release: {release.Tag}  ·  checked {DateTime.Now:HH:mm}\n" +
                             (updates == 0 ? "Everything installed is up to date." : $"{updates} update{(updates == 1 ? "" : "s")} available.");
        }
        foreach (var c in cards) c.UpdateState(release, error);

        // Is there a newer launcher than this one?
        bool newer = release != null && release.Asset(LauncherAsset) != null && GitHub.ParseTag(release.Tag) > Program.Version;
        banner.Visible = newer;
        if (newer) bannerText.Text = $"A new version of this launcher is available ({release!.Tag}).";
    }

    async Task UpdateLauncherAsync()
    {
        if (release == null) return;
        bannerButton.Enabled = false;
        var prog = new Progress<(long Done, long Total)>(p =>
            bannerText.Text = p.Total > 0 ? $"Downloading launcher… {p.Done * 100 / p.Total}%" : "Downloading launcher…");
        try
        {
            await Installer.SelfUpdateAsync(release, LauncherAsset, prog, CancellationToken.None);
            bannerText.Text = "Restarting…";
            Close();
        }
        catch (Exception ex)
        {
            bannerText.Text = "Launcher update failed: " + ex.Message;
            bannerButton.Enabled = true;
        }
    }

    void ShowNotes()
    {
        string text = release == null ? "No release information yet — press \"Check for updates\"."
            : $"{(release.Title.Length > 0 ? release.Title : release.Tag)}\r\nPublished {release.Published.LocalDateTime:d MMM yyyy}\r\n\r\n" +
              (release.Notes.Length > 0 ? release.Notes.Replace("\n", "\r\n") : "(no notes)");
        using var f = new Form
        {
            Text = "What's new",
            ClientSize = new Size(620, 460),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Back,
            ForeColor = Fore,
            MinimizeBox = false,
            MaximizeBox = false,
            AutoScaleMode = AutoScaleMode.Dpi,
        };
        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = Card,
            ForeColor = Fore,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 10f),
            Text = text,
        };
        f.Controls.Add(box);
        ClassicFrame.Apply(f);
        f.ShowDialog(this);
    }

    public static Image? LoadImage(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + name, StringComparison.OrdinalIgnoreCase));
        if (res == null) return null;
        using var s = asm.GetManifestResourceStream(res)!;
        return Image.FromStream(new MemoryStream(ReadAll(s)));
    }

    static byte[] ReadAll(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>Title strip styled like the radio's glowing dot-matrix display.</summary>
sealed class HeaderPanel : Panel
{
    public HeaderPanel()
    {
        DoubleBuffered = true;
        BackColor = LauncherForm.Back;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        var glass = new RectangleF(20 * scale, 16 * scale, Math.Min(Width - 420 * scale, 640 * scale), Height - 30 * scale);
        using (var path = LauncherForm.Round(glass, 10 * scale))
        {
            using var bg = new LinearGradientBrush(glass, Color.FromArgb(8, 13, 20), Color.FromArgb(2, 3, 6), 90f);
            g.FillPath(bg, path);
            using var rim = new Pen(Color.FromArgb(50, 110, 235, 255), 1.2f);
            g.DrawPath(rim, path);
        }

        const string title = "RETRO MULTY PROGRAM";
        float pitch = Math.Min(3.2f * scale, (glass.Width - 40 * scale) / (title.Length * 6));
        var dots = new List<RectangleF>();
        DotFont.Emit(dots, title, glass.X + 20 * scale, glass.Y + 18 * scale, pitch, pitch * 0.8f);
        var cyan = LauncherForm.Cyan;
        // Soft glow, then the dots themselves.
        using (var halo = new SolidBrush(Color.FromArgb(30, cyan)))
            foreach (var d in dots) g.FillEllipse(halo, RectangleF.Inflate(d, pitch * 0.9f, pitch * 0.9f));
        using (var lit = new SolidBrush(cyan)) g.FillRectangles(lit, dots.ToArray());

        dots.Clear();
        string sub = DotFont.Normalize("♪ RADIO · CONVERTER · DESIGNER · LENS · DASH");
        float sp = pitch * 0.55f;
        DotFont.Emit(dots, sub, glass.X + 20 * scale, glass.Y + 18 * scale + 7 * pitch + 12 * scale, sp, sp * 0.8f);
        using (var amber = new SolidBrush(LauncherForm.Amber)) g.FillRectangles(amber, dots.ToArray());
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
}

/// <summary>One program: its picture, what it is, and the buttons to get and run it.</summary>
sealed class AppCard : Control
{
    readonly AppInfo app;
    readonly LauncherForm owner;
    readonly Image? image;
    readonly Label lblName = new(), lblTag = new(), lblDesc = new(), lblState = new();
    readonly ProgressBar bar = new();
    readonly Button btnMain = new(), btnOpen = new(), btnRemove = new(), btnShortcut = new();
    bool busy;

    public AppCard(AppInfo app, LauncherForm owner)
    {
        this.app = app;
        this.owner = owner;
        image = LauncherForm.LoadImage(app.Image);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = LauncherForm.Back;
        Height = 208;
        Width = 1000;
        Margin = new Padding(0, 0, 0, 18);

        lblName.Text = app.Name;
        lblName.Font = new Font("Segoe UI Semibold", 17f);
        lblTag.Text = app.Tagline.ToUpperInvariant();
        lblTag.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        lblTag.ForeColor = LauncherForm.Cyan;
        lblDesc.Text = app.Description;
        lblDesc.ForeColor = LauncherForm.Dim;
        lblState.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        foreach (var l in new[] { lblName, lblTag, lblDesc, lblState })
        {
            l.BackColor = LauncherForm.Card;
            l.AutoSize = false;
            if (l != lblTag) l.ForeColor = l == lblDesc ? LauncherForm.Dim : LauncherForm.Fore;
            Controls.Add(l);
        }

        bar.Visible = false;
        Controls.Add(bar);

        LauncherForm.Style(btnMain, primary: true);
        LauncherForm.Style(btnOpen, primary: false);
        LauncherForm.Style(btnRemove, primary: false);
        LauncherForm.Style(btnShortcut, primary: false);
        btnOpen.Text = "Open";
        btnRemove.Text = "Uninstall";
        btnShortcut.Text = "Desktop shortcut";
        btnMain.Click += async (_, _) => await MainAction();
        btnOpen.Click += (_, _) => Run(() => Installer.Launch(app));
        btnRemove.Click += (_, _) => Uninstall();
        btnShortcut.Click += (_, _) => Run(() =>
        {
            Installer.DesktopShortcut(app);
            lblState.Text = "Shortcut added to your desktop.";
        });
        Controls.AddRange([btnMain, btnOpen, btnRemove, btnShortcut]);
        Layout();
    }

    const int ImgW = 384, Pad = 16;

    new void Layout()
    {
        float s = DeviceDpi / 96f;
        int x = (int)((ImgW + Pad * 2) * s), w = Math.Max(200, Width - x - (int)(Pad * s));
        lblName.SetBounds(x, (int)(12 * s), w, (int)(34 * s));
        lblTag.SetBounds(x, (int)(46 * s), w, (int)(18 * s));
        lblDesc.SetBounds(x, (int)(66 * s), w, (int)(54 * s));
        lblState.SetBounds(x, (int)(122 * s), w, (int)(22 * s));
        bar.SetBounds(x, (int)(145 * s), w, (int)(8 * s));
        int bx = x, by = (int)(158 * s), bh = (int)(34 * s);
        foreach (var b in new[] { btnMain, btnOpen, btnShortcut, btnRemove })
        {
            if (!b.Visible) continue;
            int bw = (int)((b == btnMain ? 150 : b == btnShortcut ? 150 : 110) * s);
            b.SetBounds(bx, by, bw, bh);
            bx += bw + (int)(10 * s);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Layout();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        float s = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var card = LauncherForm.Round(r, 12 * s))
        {
            using var b = new SolidBrush(LauncherForm.Card);
            g.FillPath(b, card);
            using var pen = new Pen(LauncherForm.Edge, 1);
            g.DrawPath(pen, card);
        }
        var imgR = new RectangleF(Pad * s, Pad * s, ImgW * s, Height - Pad * 2 * s);
        using var clip = LauncherForm.Round(imgR, 8 * s);
        var saved = g.Save();
        g.SetClip(clip);
        g.FillRectangle(Brushes.Black, imgR);
        if (image != null)
        {
            // Cover the frame, keeping the picture's shape.
            float k = Math.Max(imgR.Width / image.Width, imgR.Height / image.Height);
            float iw = image.Width * k, ih = image.Height * k;
            g.DrawImage(image, imgR.X + (imgR.Width - iw) / 2, imgR.Y + (imgR.Height - ih) / 2, iw, ih);
        }
        g.Restore(saved);
        using var border = new Pen(Color.FromArgb(70, 255, 255, 255), 1);
        g.DrawPath(border, clip);
    }

    /// <summary>Refreshes the buttons and status line from what's installed and what's on GitHub.</summary>
    public void UpdateState(Release? release, string? error)
    {
        if (busy) return;
        var installed = Installer.Installed(app);
        var asset = release?.Asset(app.AssetName);
        bool update = installed != null && asset != null && asset.Id != installed.AssetId;

        btnOpen.Visible = update;
        btnRemove.Visible = btnShortcut.Visible = installed != null;
        btnMain.Enabled = true;
        if (installed == null)
        {
            btnMain.Text = "INSTALL";
            btnMain.Enabled = asset != null;
            lblState.ForeColor = LauncherForm.Dim;
            lblState.Text = asset != null ? $"Not installed  ·  {asset.Size / 1048576.0:0} MB download  ·  {release!.Tag}"
                : error ?? (release == null ? "Not released on GitHub yet." : "Not in the latest release.");
        }
        else if (update)
        {
            btnMain.Text = "UPDATE";
            lblState.ForeColor = LauncherForm.Amber;
            lblState.Text = $"Update available: {release!.Tag}  (you have {installed.Tag})";
        }
        else
        {
            btnMain.Text = "OPEN";
            lblState.ForeColor = Color.FromArgb(120, 230, 140);
            lblState.Text = $"Installed  ·  {installed.Tag}" + (error != null ? $"  ·  {error}" : release != null ? "  ·  up to date" : "");
        }
        Layout();
    }

    async Task MainAction()
    {
        var installed = Installer.Installed(app);
        var release = owner.CurrentRelease;
        if (installed != null && (release?.Asset(app.AssetName) is not { } a || a.Id == installed.AssetId))
        {
            Run(() => Installer.Launch(app));
            return;
        }
        if (release == null) return;

        if (Installer.IsRunning(app))
        {
            if (MessageBox.Show(this, $"{app.Name} is running. Close it and update?", "Update", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Installer.CloseRunning(app);
        }

        busy = true;
        foreach (var b in new[] { btnMain, btnOpen, btnRemove, btnShortcut }) b.Enabled = false;
        bar.Visible = true;
        bar.Value = 0;
        lblState.ForeColor = LauncherForm.Cyan;
        var prog = new Progress<(long Done, long Total)>(p =>
        {
            bar.Value = p.Total > 0 ? (int)(p.Done * 100 / p.Total) : 0;
            lblState.Text = p.Total > 0 ? $"Downloading…  {p.Done / 1048576.0:0.0} / {p.Total / 1048576.0:0.0} MB" : "Downloading…";
        });
        try
        {
            await Installer.InstallAsync(app, release, prog, CancellationToken.None);
            busy = false;
            UpdateState(release, null);
            lblState.Text = $"Installed {release.Tag}. Press OPEN to start it.";
        }
        catch (Exception ex)
        {
            busy = false;
            UpdateState(release, null);
            lblState.ForeColor = Color.FromArgb(255, 110, 100);
            lblState.Text = "Install failed: " + ex.Message;
        }
        finally
        {
            bar.Visible = false;
            foreach (var b in new[] { btnMain, btnOpen, btnRemove, btnShortcut }) b.Enabled = true;
        }
    }

    void Uninstall()
    {
        if (MessageBox.Show(this, $"Remove {app.Name}? Your music, settings and radios are kept.", "Uninstall",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        if (Installer.IsRunning(app)) Installer.CloseRunning(app);
        Run(() => Installer.Uninstall(app));
        UpdateState(owner.CurrentRelease, null);
    }

    void Run(Action a)
    {
        try
        {
            a();
        }
        catch (Exception ex)
        {
            lblState.ForeColor = Color.FromArgb(255, 110, 100);
            lblState.Text = ex.Message;
        }
    }
}
