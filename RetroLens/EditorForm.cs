using System.Drawing.Drawing2D;
using System.Reflection;
using RetroRadio;

namespace RetroLens;

/// <summary>Retro Lens: pick a look, tweak everything, export photos and videos.</summary>
sealed class EditorForm : Form
{
    static readonly Color Back = Color.FromArgb(18, 19, 24);
    static readonly Color Panel = Color.FromArgb(28, 30, 37);
    static readonly Color Edge = Color.FromArgb(48, 51, 61);
    static readonly Color Fore = Color.FromArgb(224, 228, 236);
    static readonly Color Dim = Color.FromArgb(140, 146, 160);
    static readonly Color Accent = Color.FromArgb(255, 176, 70);

    Look look = new();
    string? sourcePath;
    bool isVideo;
    VideoIO.Info? videoInfo;
    Bitmap? source;          // full-size picture (or the current video frame)
    Bitmap? previewSource;   // smaller copy used for fast previews
    Bitmap? result;
    int renderGen;
    bool loadingControls;
    bool showOriginal;
    float split = -1;        // 0..1 divider position, or -1 when split view is off
    CancellationTokenSource? exportCts;

    readonly ListBox presetList = new();
    readonly PreviewBox preview = new();
    readonly FlowLayoutPanel paramPanel = new();
    readonly TrackBar timeBar = new();
    readonly Label timeLabel = new();
    readonly Panel timePanel = new();
    readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripProgressBar progress = new() { Visible = false, Width = 220 };
    readonly ToolStripButton btnCancel = new("Cancel") { Visible = false };
    readonly ToolStripButton btnSplit = new("Split view") { CheckOnClick = true };
    readonly System.Windows.Forms.Timer debounce = new() { Interval = 110 };
    readonly List<(PropertyInfo Prop, Control Control, Label? Value)> bindings = [];
    List<Preset> presets = [];
    readonly List<object> rows = []; // section headers (string) and presets
    readonly TextBox search = new();

    Preset? SelectedPreset => presetList.SelectedIndex >= 0 && presetList.SelectedIndex < rows.Count ? rows[presetList.SelectedIndex] as Preset : null;

    public EditorForm(string? file)
    {
        Text = "Retro Lens";
        BackColor = Back;
        ForeColor = Fore;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1480, 900);
        MinimumSize = new Size(1000, 640);
        AllowDrop = true;
        KeyPreview = true;

        BuildToolbar();
        BuildPresetList();
        BuildParams();

        timePanel.Dock = DockStyle.Bottom;
        timePanel.Height = 44;
        timePanel.BackColor = Panel;
        timePanel.Visible = false;
        timeBar.Dock = DockStyle.Fill;
        timeBar.Maximum = 1000;
        timeBar.TickStyle = TickStyle.None;
        timeBar.ValueChanged += (_, _) => { UpdateTimeLabel(); debounce.Stop(); debounce.Start(); timeChanged = true; };
        timeLabel.Dock = DockStyle.Right;
        timeLabel.Width = 150;
        timeLabel.TextAlign = ContentAlignment.MiddleCenter;
        timeLabel.ForeColor = Dim;
        timePanel.Controls.Add(timeBar);
        timePanel.Controls.Add(timeLabel);

        preview.Dock = DockStyle.Fill;
        preview.Owner = this;
        var center = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Padding = new Padding(0) };
        center.Controls.Add(preview);
        center.Controls.Add(timePanel);

        Controls.Add(center);
        center.BringToFront();

        var bar = new StatusStrip { BackColor = Panel, ForeColor = Fore, SizingGrip = false };
        btnCancel.ForeColor = Fore;
        btnCancel.Click += (_, _) => exportCts?.Cancel();
        bar.Items.AddRange([status, progress, btnCancel]);
        Controls.Add(bar);

        debounce.Tick += async (_, _) =>
        {
            debounce.Stop();
            if (timeChanged && isVideo) await LoadVideoFrameAsync();
            timeChanged = false;
            await RenderAsync();
        };

        SelectPreset(presets.First(p => p.Name.StartsWith("VHS camcorder")));
        SetStatus(VideoIO.Ffmpeg == null
            ? "Open a photo (videos need ffmpeg from ffmpeg.org)."
            : "Open a photo or video, or drag one in. Pick a look on the left, fine-tune on the right.");
        if (file != null) Shown += async (_, _) => await OpenAsync(file);
        ClassicFrame.Apply(this);
    }

    bool timeChanged;

    // ───────────────────────────── layout ─────────────────────────────

    void BuildToolbar()
    {
        var t = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = Panel, ForeColor = Fore, Padding = new Padding(8, 4, 8, 4), RenderMode = ToolStripRenderMode.System };
        ToolStripButton B(string text, EventHandler click) => new(text, null, click) { ForeColor = Fore };
        t.Items.Add(B("Open…", async (_, _) => await OpenDialogAsync()));
        var export = B("Export…", async (_, _) => await ExportAsync());
        export.Font = new Font(Font, FontStyle.Bold);
        export.ForeColor = Accent;
        t.Items.Add(export);
        t.Items.Add(new ToolStripSeparator());
        var hold = B("Hold to compare", (_, _) => { });
        hold.MouseDown += (_, _) => { showOriginal = true; preview.Invalidate(); };
        hold.MouseUp += (_, _) => { showOriginal = false; preview.Invalidate(); };
        t.Items.Add(hold);
        btnSplit.ForeColor = Fore;
        btnSplit.CheckedChanged += (_, _) => { split = btnSplit.Checked ? 0.5f : -1; preview.Invalidate(); };
        t.Items.Add(btnSplit);
        t.Items.Add(new ToolStripSeparator());
        t.Items.Add(B("Surprise me", (_, _) => Surprise()));
        t.Items.Add(B("Reset look", (_, _) => { if (SelectedPreset is { } p) SelectPreset(p); }));
        t.Items.Add(B("Save preset…", (_, _) => SavePreset()));
        t.Items.Add(B("Delete preset", (_, _) => DeletePreset()));
        Controls.Add(t);
    }

    void BuildPresetList()
    {
        var side = new Panel { Dock = DockStyle.Left, Width = 250, BackColor = Panel, Padding = new Padding(8) };
        var title = new Label { Text = "LOOKS", Dock = DockStyle.Top, Height = 26, ForeColor = Accent, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        search.Dock = DockStyle.Top;
        search.PlaceholderText = "Search looks or brands…";
        search.BackColor = Back;
        search.ForeColor = Fore;
        search.BorderStyle = BorderStyle.FixedSingle;
        search.TextChanged += (_, _) => BuildRows();
        var gap = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Panel };
        presetList.Dock = DockStyle.Fill;
        presetList.BorderStyle = BorderStyle.None;
        presetList.BackColor = Panel;
        presetList.ForeColor = Fore;
        presetList.DrawMode = DrawMode.OwnerDrawFixed;
        presetList.ItemHeight = 26;
        presetList.DrawItem += DrawPresetItem;
        presetList.SelectedIndexChanged += (_, _) =>
        {
            if (loadingControls || presetList.SelectedIndex < 0) return;
            if (SelectedPreset is { } p) SelectPreset(p);
            else if (presetList.SelectedIndex + 1 < rows.Count)
                presetList.SelectedIndex++; // clicking a section header picks its first look
        };
        side.Controls.Add(presetList);
        side.Controls.Add(gap);
        side.Controls.Add(search);
        side.Controls.Add(title);
        Controls.Add(side);
        ReloadPresets();
    }

    void ReloadPresets()
    {
        presets = [.. Presets.All(), .. Presets.Mine()];
        BuildRows();
    }

    /// <summary>Fills the list: a header per section (style or brand), then its looks, filtered by the search box.</summary>
    void BuildRows()
    {
        var keep = SelectedPreset;
        string q = search.Text.Trim();
        rows.Clear();
        foreach (var group in presets.GroupBy(p => p.Category))
        {
            var match = group.Where(p => q.Length == 0 || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || p.Category.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (match.Count == 0) continue;
            rows.Add(group.Key);
            rows.AddRange(match);
        }
        loadingControls = true;
        presetList.BeginUpdate();
        presetList.Items.Clear();
        foreach (var r in rows) presetList.Items.Add(r);
        presetList.EndUpdate();
        if (keep != null && rows.IndexOf(keep) is var i and >= 0) presetList.SelectedIndex = i;
        loadingControls = false;
    }

    void DrawPresetItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= rows.Count) return;
        if (rows[e.Index] is string header)
        {
            // Section header: brand or style name.
            using (var hb = new SolidBrush(Color.FromArgb(22, 23, 29))) e.Graphics.FillRectangle(hb, e.Bounds);
            using var hf = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, header.ToUpperInvariant(), hf, new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 4, e.Bounds.Width - 16, e.Bounds.Height - 4),
                Accent, TextFormatFlags.VerticalCenter);
            using var line = new Pen(Color.FromArgb(90, Accent));
            e.Graphics.DrawLine(line, e.Bounds.Left + 8, e.Bounds.Bottom - 2, e.Bounds.Right - 8, e.Bounds.Bottom - 2);
            return;
        }
        var p = (Preset)rows[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using var bg = new SolidBrush(sel ? Color.FromArgb(70, 54, 26) : Panel);
        e.Graphics.FillRectangle(bg, e.Bounds);
        TextRenderer.DrawText(e.Graphics, p.Name, Font, new Rectangle(e.Bounds.X + 16, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height),
            sel ? Color.White : Fore, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    void BuildParams()
    {
        var side = new Panel { Dock = DockStyle.Right, Width = 370, BackColor = Panel };
        paramPanel.Dock = DockStyle.Fill;
        paramPanel.FlowDirection = FlowDirection.TopDown;
        paramPanel.WrapContents = false;
        paramPanel.AutoScroll = true;
        paramPanel.Padding = new Padding(10, 6, 6, 10);
        paramPanel.BackColor = Panel;
        side.Controls.Add(paramPanel);
        Controls.Add(side);

        string? group = null;
        foreach (var prop in typeof(Look).GetProperties())
        {
            var attr = prop.GetCustomAttribute<ParamAttribute>();
            if (attr == null) continue;
            if (attr.Group != group)
            {
                group = attr.Group;
                paramPanel.Controls.Add(new Label
                {
                    Text = group.ToUpperInvariant(),
                    ForeColor = Accent,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    AutoSize = false,
                    Width = 330,
                    Height = 28,
                    TextAlign = ContentAlignment.BottomLeft,
                });
            }
            AddParam(prop, attr);
        }
    }

    void AddParam(PropertyInfo prop, ParamAttribute attr)
    {
        var row = new Panel { Width = 330, Height = 30, Margin = new Padding(0, 1, 0, 1) };
        var name = new Label { Text = attr.Label, Left = 0, Top = 6, Width = 135, ForeColor = Fore, AutoEllipsis = true };
        if (attr.Help != null) new ToolTip().SetToolTip(name, attr.Help);
        row.Controls.Add(name);
        Control ctl;
        Label? valueLabel = null;
        var type = prop.PropertyType;

        if (type == typeof(float) || type == typeof(int))
        {
            var tb = new TrackBar { Left = 132, Top = 0, Width = 150, Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, AutoSize = false, Height = 28 };
            valueLabel = new Label { Left = 284, Top = 6, Width = 46, ForeColor = Dim, TextAlign = ContentAlignment.TopRight };
            tb.ValueChanged += (_, _) =>
            {
                if (loadingControls) return;
                float v = attr.Min + (attr.Max - attr.Min) * tb.Value / 1000f;
                prop.SetValue(look, type == typeof(int) ? (object)(int)MathF.Round(v) : v);
                valueLabel.Text = Format(prop.GetValue(look));
                Changed();
            };
            ctl = tb;
            row.Controls.Add(valueLabel);
        }
        else if (type.IsEnum)
        {
            var cb = new ComboBox { Left = 136, Top = 3, Width = 190, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Back, ForeColor = Fore };
            foreach (var v in Enum.GetValues(type)) cb.Items.Add(Pretty(v.ToString()!));
            cb.SelectedIndexChanged += (_, _) =>
            {
                if (loadingControls || cb.SelectedIndex < 0) return;
                prop.SetValue(look, Enum.GetValues(type).GetValue(cb.SelectedIndex));
                Changed();
            };
            ctl = cb;
        }
        else if (type == typeof(bool))
        {
            var ck = new CheckBox { Left = 136, Top = 5, Width = 30 };
            ck.CheckedChanged += (_, _) =>
            {
                if (loadingControls) return;
                prop.SetValue(look, ck.Checked);
                Changed();
            };
            ctl = ck;
        }
        else
        {
            var tx = new TextBox { Left = 136, Top = 3, Width = 190, BackColor = Back, ForeColor = Fore, BorderStyle = BorderStyle.FixedSingle };
            tx.TextChanged += (_, _) =>
            {
                if (loadingControls) return;
                prop.SetValue(look, tx.Text);
                Changed();
            };
            ctl = tx;
        }
        row.Controls.Add(ctl);
        paramPanel.Controls.Add(row);
        bindings.Add((prop, ctl, valueLabel));
    }

    static string Pretty(string enumName) =>
        System.Text.RegularExpressions.Regex.Replace(enumName, "(?<=[a-z0-9])([A-Z])", " $1").Replace("4x3", " 4:3").Replace("3x2", " 3:2").Replace("16x9", " 16:9");

    static string Format(object? v) => v switch
    {
        float f => Math.Abs(f) >= 10 ? f.ToString("0") : f.ToString("0.00"),
        int i => i.ToString(),
        _ => "",
    };

    /// <summary>Shows the current look's values in all the controls.</summary>
    void LoadControls()
    {
        loadingControls = true;
        foreach (var (prop, ctl, value) in bindings)
        {
            var attr = prop.GetCustomAttribute<ParamAttribute>()!;
            var v = prop.GetValue(look);
            switch (ctl)
            {
                case TrackBar tb:
                    float f = Convert.ToSingle(v);
                    tb.Value = (int)Math.Clamp(MathF.Round((f - attr.Min) / (attr.Max - attr.Min) * 1000), 0, 1000);
                    value!.Text = Format(v);
                    break;
                case ComboBox cb:
                    cb.SelectedIndex = Array.IndexOf(Enum.GetValues(prop.PropertyType), v);
                    break;
                case CheckBox ck:
                    ck.Checked = (bool)v!;
                    break;
                case TextBox tx:
                    tx.Text = (string?)v ?? "";
                    break;
            }
        }
        loadingControls = false;
    }

    // ───────────────────────────── presets ─────────────────────────────

    void SelectPreset(Preset p)
    {
        look = p.Look.Clone();
        loadingControls = true;
        int i = rows.IndexOf(p);
        if (i >= 0) presetList.SelectedIndex = i;
        loadingControls = false;
        LoadControls();
        Changed();
    }

    void Surprise()
    {
        var rnd = new Random();
        var basePreset = presets[rnd.Next(1, presets.Count)];
        look = basePreset.Look.Clone();
        look.Temperature = Math.Clamp(look.Temperature + (float)(rnd.NextDouble() - 0.5) * 0.6f, -1, 1);
        look.Saturation = Math.Clamp(look.Saturation * (0.7f + (float)rnd.NextDouble() * 0.7f), 0, 2.5f);
        look.Vignette = Math.Clamp(look.Vignette + (float)rnd.NextDouble() * 0.3f, 0, 1);
        if (rnd.NextDouble() < 0.4) look.Fisheye = (float)rnd.NextDouble() * 0.9f;
        if (rnd.NextDouble() < 0.4) look.LightLeak = (float)rnd.NextDouble() * 0.6f;
        if (rnd.NextDouble() < 0.4) look.Grain = (float)rnd.NextDouble() * 0.6f;
        if (rnd.NextDouble() < 0.3) look.HueShift = (float)(rnd.NextDouble() - 0.5) * 60;
        LoadControls();
        Changed();
        SetStatus($"Surprise: based on \"{basePreset.Name}\" with random tweaks.");
    }

    void SavePreset()
    {
        string? name = Prompt("Save preset", "Name for this look:", SelectedPreset?.Name + " (mine)");
        if (string.IsNullOrWhiteSpace(name)) return;
        Presets.Save(name, look);
        ReloadPresets();
        var saved = presets.FirstOrDefault(p => p.Category == "My presets" && p.Name == name.Trim());
        if (saved != null && rows.IndexOf(saved) is var i and >= 0)
        {
            loadingControls = true;
            presetList.SelectedIndex = i;
            loadingControls = false;
        }
        SetStatus($"Saved \"{name}\" under My presets.");
    }

    void DeletePreset()
    {
        var p = SelectedPreset;
        if (p == null || p.Category != "My presets")
        {
            SetStatus("Only your own presets can be deleted.");
            return;
        }
        if (MessageBox.Show(this, $"Delete \"{p.Name}\"?", "Delete preset", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        Presets.Delete(p.Name);
        ReloadPresets();
        SelectPreset(presets[0]);
    }

    string? Prompt(string title, string text, string initial)
    {
        using var f = new Form { Text = title, ClientSize = new Size(380, 120), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, BackColor = Back, ForeColor = Fore, AutoScaleMode = AutoScaleMode.Dpi };
        var lbl = new Label { Text = text, Left = 12, Top = 12, AutoSize = true };
        var box = new TextBox { Text = initial, Left = 12, Top = 36, Width = 356, BackColor = Panel, ForeColor = Fore, BorderStyle = BorderStyle.FixedSingle };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Left = 204, Top = 76, Width = 80, FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 288, Top = 76, Width = 80, FlatStyle = FlatStyle.Flat };
        f.Controls.AddRange([lbl, box, ok, cancel]);
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        ClassicFrame.Apply(f);
        return f.ShowDialog(this) == DialogResult.OK ? box.Text : null;
    }

    // ───────────────────────────── files ─────────────────────────────

    bool EnsureFfmpeg()
    {
        if (VideoIO.Ffmpeg != null) return true;
        MessageBox.Show(this, "Videos (and some picture formats) need ffmpeg, which wasn't found.\n\nDownload it free from ffmpeg.org, then point me at ffmpeg.exe.",
            "ffmpeg needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        using var dlg = new OpenFileDialog { Title = "Locate ffmpeg.exe", Filter = "ffmpeg.exe|ffmpeg.exe|Programs|*.exe" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        VideoIO.Remember(dlg.FileName);
        return true;
    }

    async Task OpenDialogAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open a photo or video",
            Filter = "Photos and videos|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.mp4;*.mov;*.mkv;*.avi;*.webm;*.wmv;*.m4v;*.3gp;*.mts|All files|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) await OpenAsync(dlg.FileName);
    }

    async Task OpenAsync(string path)
    {
        try
        {
            isVideo = VideoIO.IsVideo(path);
            if (isVideo && !EnsureFfmpeg()) return;
            sourcePath = path;
            SetStatus("Opening…");
            if (isVideo)
            {
                videoInfo = await Task.Run(() => VideoIO.Probe(path));
                timePanel.Visible = true;
                timeBar.Value = 0;
                UpdateTimeLabel();
                await LoadVideoFrameAsync();
            }
            else
            {
                timePanel.Visible = false;
                Bitmap? img = null;
                try
                {
                    using var loaded = new Bitmap(path);
                    img = new Bitmap(loaded);
                }
                catch (Exception) when (VideoIO.Ffmpeg != null)
                {
                    img = await Task.Run(() => VideoIO.GrabFrame(path, 0, 8000)); // webp, heic, …
                }
                SetSource(img ?? throw new InvalidOperationException("That picture couldn't be opened."));
            }
            Text = "Retro Lens — " + Path.GetFileName(path);
            SetStatus(isVideo && videoInfo != null
                ? $"Video: {videoInfo.Width}×{videoInfo.Height}, {videoInfo.Fps:0.##} fps, {TimeSpan.FromSeconds(videoInfo.Duration):m\\:ss}. Drag the timeline to preview other moments."
                : $"Photo: {source!.Width}×{source.Height}.");
            await RenderAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't open that file: " + ex.Message);
        }
    }

    async Task LoadVideoFrameAsync()
    {
        if (sourcePath == null || videoInfo == null) return;
        double t = videoInfo.Duration * timeBar.Value / 1000.0;
        var frame = await Task.Run(() => VideoIO.GrabFrame(sourcePath, Math.Min(t, Math.Max(0, videoInfo.Duration - 0.1)), 1280));
        if (frame != null) SetSource(frame);
    }

    void SetSource(Bitmap img)
    {
        source?.Dispose();
        previewSource?.Dispose();
        source = img;
        int max = 1600;
        if (img.Width > max)
        {
            previewSource = new Bitmap(max, (int)(img.Height * (max / (double)img.Width)));
            using var g = Graphics.FromImage(previewSource);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, 0, 0, previewSource.Width, previewSource.Height);
        }
        else previewSource = new Bitmap(img);
        preview.Invalidate();
    }

    void UpdateTimeLabel()
    {
        if (videoInfo == null) return;
        var t = TimeSpan.FromSeconds(videoInfo.Duration * timeBar.Value / 1000.0);
        timeLabel.Text = $"{t:m\\:ss\\.f} / {TimeSpan.FromSeconds(videoInfo.Duration):m\\:ss}";
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    protected override async void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) await OpenAsync(files[0]);
    }

    // ───────────────────────────── render & export ─────────────────────────────

    void Changed()
    {
        debounce.Stop();
        debounce.Start();
    }

    async Task RenderAsync()
    {
        if (previewSource == null) return;
        int gen = ++renderGen;
        var src = previewSource;
        var l = look.Clone();
        double time = isVideo && videoInfo != null ? videoInfo.Duration * timeBar.Value / 1000.0 : 0;
        int maxW = Math.Max(320, preview.Width);
        Bitmap? img;
        try
        {
            img = await Task.Run(() =>
            {
                lock (src) return Processor.Apply(src, l, 0, time, maxW);
            });
        }
        catch (Exception ex)
        {
            SetStatus("Render problem: " + ex.Message);
            return;
        }
        if (gen != renderGen)
        {
            img.Dispose();
            return;
        }
        result?.Dispose();
        result = img;
        preview.Invalidate();
    }

    async Task ExportAsync()
    {
        if (source == null || sourcePath == null)
        {
            await OpenDialogAsync();
            return;
        }
        string baseName = Path.GetFileNameWithoutExtension(sourcePath) + " (retro)";
        using var dlg = new SaveFileDialog
        {
            Title = isVideo ? "Export video" : "Save picture",
            Filter = isVideo ? "MP4 video|*.mp4|Animated GIF|*.gif" : "PNG picture|*.png|JPEG picture|*.jpg|Bitmap|*.bmp",
            FileName = baseName,
            InitialDirectory = Path.GetDirectoryName(sourcePath),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var l = look.Clone();
        progress.Value = 0;
        progress.Visible = true;
        btnCancel.Visible = isVideo;
        exportCts = new CancellationTokenSource();
        try
        {
            if (isVideo)
            {
                SetStatus("Exporting video… (every frame goes through the look)");
                var prog = new Progress<double>(v =>
                {
                    progress.Value = (int)(v * 100);
                    SetStatus($"Exporting video… {v * 100:0}%");
                });
                await VideoIO.ExportAsync(sourcePath, dlg.FileName, l, prog, exportCts.Token);
            }
            else
            {
                SetStatus("Saving…");
                Bitmap full;
                if (VideoIO.IsVideo(sourcePath)) full = source;
                else
                {
                    using var original = new Bitmap(sourcePath);
                    full = new Bitmap(original);
                }
                using var outImg = await Task.Run(() => Processor.Apply(full, l));
                var fmt = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => System.Drawing.Imaging.ImageFormat.Jpeg,
                    ".bmp" => System.Drawing.Imaging.ImageFormat.Bmp,
                    _ => System.Drawing.Imaging.ImageFormat.Png,
                };
                outImg.Save(dlg.FileName, fmt);
                if (full != source) full.Dispose();
            }
            SetStatus("Saved " + dlg.FileName);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Export cancelled.");
            try { File.Delete(dlg.FileName); } catch (IOException) { }
        }
        catch (Exception ex)
        {
            SetStatus("Export failed: " + ex.Message);
        }
        finally
        {
            progress.Visible = false;
            btnCancel.Visible = false;
        }
    }

    void SetStatus(string s) => status.Text = s;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.O)) { _ = OpenDialogAsync(); return true; }
        if (keyData == (Keys.Control | Keys.S)) { _ = ExportAsync(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ───────────────────────────── preview ─────────────────────────────

    internal void PaintPreview(Graphics g, Rectangle area)
    {
        g.Clear(Color.FromArgb(10, 10, 12));
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (previewSource == null)
        {
            using var f = new Font("Segoe UI", 14f);
            TextRenderer.DrawText(g, "Drop a photo or video here\n(or press Open…)", f, area, Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        var shown = showOriginal || result == null ? previewSource : result;
        var r = Fit(shown.Size, area);
        g.DrawImage(shown, r);
        if (split >= 0 && result != null && !showOriginal)
        {
            // Left of the divider: the original, framed like the result.
            var orig = Fit(previewSource.Size, r);
            float divider = r.X + r.Width * split;
            var clip = g.Clip;
            g.SetClip(new RectangleF(r.X, r.Y, divider - r.X, r.Height));
            using (var bg = new SolidBrush(Color.FromArgb(10, 10, 12))) g.FillRectangle(bg, r);
            g.DrawImage(previewSource, orig);
            g.Clip = clip;
            using var pen = new Pen(Accent, 2);
            g.DrawLine(pen, divider, r.Y, divider, r.Bottom);
            TextRenderer.DrawText(g, "ORIGINAL", Font, new Point((int)r.X + 8, (int)r.Y + 8), Color.White);
            TextRenderer.DrawText(g, "RETRO", Font, new Point((int)divider + 8, (int)r.Y + 8), Accent);
        }
        if (showOriginal) TextRenderer.DrawText(g, "ORIGINAL", Font, new Point(area.X + 12, area.Y + 12), Color.White);
    }

    internal void PreviewMouse(MouseEventArgs e, Rectangle area)
    {
        if (split < 0 || e.Button != MouseButtons.Left || result == null) return;
        var r = Fit(result.Size, area);
        split = Math.Clamp((e.X - r.X) / r.Width, 0.02f, 0.98f);
        preview.Invalidate();
    }

    static RectangleF Fit(Size img, RectangleF area)
    {
        float k = Math.Min(area.Width / img.Width, area.Height / img.Height);
        float w = img.Width * k, h = img.Height * k;
        return new RectangleF(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }

    sealed class PreviewBox : Control
    {
        public EditorForm? Owner;

        public PreviewBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        Rectangle Area => Rectangle.Inflate(ClientRectangle, -16, -16);
        protected override void OnPaint(PaintEventArgs e) => Owner?.PaintPreview(e.Graphics, Area);
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Owner?.PreviewMouse(e, Area); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Owner?.PreviewMouse(e, Area); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Owner?.Changed(); // re-render at the new size
        }
    }
}
