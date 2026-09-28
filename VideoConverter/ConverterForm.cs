using System.Drawing.Drawing2D;
using RetroRadio;

namespace RadioVideoConverter;

/// <summary>Pick a video, tweak how it looks on the dot display, and save it as .rdv.</summary>
sealed class ConverterForm : Form
{
    static readonly Color Back = Color.FromArgb(18, 18, 22);
    static readonly Color Panel = Color.FromArgb(30, 31, 37);
    static readonly Color Fore = Color.FromArgb(220, 225, 235);
    static readonly Color Dim = Color.FromArgb(140, 146, 158);
    static readonly Color Cyan = Color.FromArgb(110, 235, 255);

    readonly TextBox txtInput = new(), txtOutput = new();
    readonly DotPreview preview = new();
    readonly TrackBar trkTime = new(), trkBright = new(), trkContrast = new();
    readonly ComboBox cmbFit = new(), cmbFps = new(), cmbMode = new();
    readonly CheckBox chkInvert = new(), chkColor = new();
    readonly NumericUpDown numStart = new(), numLength = new();
    readonly Button btnBrowse = new(), btnSave = new(), btnConvert = new(), btnCancel = new(), btnPlay = new();
    readonly ProgressBar progress = new();
    readonly Label lblTime = new(), lblStatus = new(), lblInfo = new();
    readonly System.Windows.Forms.Timer debounce = new() { Interval = 180 };
    readonly System.Windows.Forms.Timer anim = new();

    double duration;
    byte[]? rawFrame;          // undithered frame at the scrub position (gray, or RGB when color is on)
    FitMode rawFit;
    bool rawColor;
    double rawTime = -1;
    List<byte[]>? clip;        // dithered preview clip while "Play preview" runs
    int clipPos;
    CancellationTokenSource? cts;
    bool busy;

    public ConverterForm(string? initialFile, bool color = false)
    {
        Text = "Radio Video Converter";
        BackColor = Back;
        ForeColor = Fore;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 630);
        AllowDrop = true;

        var title = new Label
        {
            Text = "RADIO VIDEO CONVERTER",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = Cyan,
            Location = new Point(12, 10),
            AutoSize = true,
        };
        var subtitle = new Label
        {
            Text = "Turns any video into a dot-matrix animation (.rdv) for the radio display.",
            ForeColor = Dim,
            Location = new Point(14, 40),
            AutoSize = true,
        };

        AddLabel("Video", 12, 70);
        Setup(txtInput, 70, 67, 620);
        txtInput.ReadOnly = true;
        SetupButton(btnBrowse, "Browse…", 700, 65, 108);
        btnBrowse.Click += (_, _) => BrowseInput();

        preview.Location = new Point(12, 100);
        preview.Size = new Size(796, 210);

        trkTime.Location = new Point(12, 318);
        trkTime.Size = new Size(700, 30);
        trkTime.Maximum = 1000;
        trkTime.TickStyle = TickStyle.None;
        trkTime.ValueChanged += (_, _) => { StopClip(); UpdateTimeLabel(); debounce.Restart(); };
        lblTime.Location = new Point(718, 322);
        lblTime.AutoSize = true;
        lblTime.ForeColor = Dim;

        AddLabel("Framing", 12, 364);
        SetupCombo(cmbFit, 80, 360, 130, ["Fill (crop)", "Fit (bars)", "Stretch"]);
        AddLabel("FPS", 230, 364);
        SetupCombo(cmbFps, 265, 360, 70, ["10", "12", "15", "20", "25", "30"]);
        cmbFps.SelectedIndex = 2;
        AddLabel("Style", 355, 364);
        SetupCombo(cmbMode, 395, 360, 230, ["Dither (smooth)", "Pattern (retro)", "Hard (high contrast)", "4-level gray dither"]);
        SetupButton(btnPlay, "▶  Play preview", 640, 358, 168);
        btnPlay.Click += (_, _) => TogglePlayPreview();

        AddLabel("Brightness", 12, 404);
        SetupTrack(trkBright, 90, 398, 190, -100, 100, 0);
        AddLabel("Contrast", 300, 404);
        SetupTrack(trkContrast, 365, 398, 190, 50, 250, 100);
        chkInvert.Text = "Invert";
        chkInvert.Location = new Point(575, 402);
        chkInvert.AutoSize = true;
        chkInvert.CheckedChanged += (_, _) => Redraw();
        chkColor.Text = "Color";
        chkColor.Location = new Point(650, 402);
        chkColor.AutoSize = true;
        chkColor.ForeColor = Cyan;
        chkColor.CheckedChanged += (_, _) => { StopClip(); debounce.Restart(); };
        chkColor.Checked = color;

        AddLabel("Start (s)", 12, 450);
        SetupNum(numStart, 80, 447, 0, 36000, 0);
        AddLabel("Length (s, 0 = all)", 180, 450);
        SetupNum(numLength, 295, 447, 0, 3600, 0);
        lblInfo.Location = new Point(400, 450);
        lblInfo.AutoSize = true;
        lblInfo.ForeColor = Dim;

        AddLabel("Save as", 12, 494);
        Setup(txtOutput, 70, 491, 620);
        SetupButton(btnSave, "Save as…", 700, 489, 108);
        btnSave.Click += (_, _) => BrowseOutput();

        SetupButton(btnConvert, "CONVERT", 12, 534, 160, 36);
        btnConvert.BackColor = Color.FromArgb(20, 90, 110);
        btnConvert.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        btnConvert.Click += async (_, _) => await ConvertAsync();
        SetupButton(btnCancel, "Cancel", 180, 534, 90, 36);
        btnCancel.Enabled = false;
        btnCancel.Click += (_, _) => cts?.Cancel();
        progress.Location = new Point(285, 540);
        progress.Size = new Size(523, 24);

        lblStatus.Location = new Point(12, 582);
        lblStatus.Size = new Size(796, 40);
        lblStatus.ForeColor = Dim;

        Controls.AddRange([title, subtitle, preview, trkTime, lblTime, chkInvert, chkColor, lblInfo, progress, lblStatus]);

        debounce.Tick += async (_, _) => { debounce.Stop(); await RefreshPreviewAsync(); };
        anim.Tick += (_, _) =>
        {
            if (clip == null || clip.Count == 0) return;
            preview.SetFrame(clip[clipPos], Dither.BitsFor(CurrentOptions()));
            clipPos = (clipPos + 1) % clip.Count;
        };

        if (Converter.FfmpegPath == null)
            SetStatus("ffmpeg.exe was not found. Click CONVERT or Browse and you'll be asked to locate it (get it from ffmpeg.org).");
        else
            SetStatus("Pick a video (or drag one onto this window).");

        if (initialFile != null) Shown += async (_, _) => await LoadInputAsync(initialFile);
        RetroRadio.ClassicFrame.Apply(this);
    }

    // ───────────────────────────── control setup ─────────────────────────────

    void AddLabel(string text, int x, int y) =>
        Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Dim });

    void Setup(TextBox t, int x, int y, int w)
    {
        t.Location = new Point(x, y);
        t.Width = w;
        t.BackColor = Panel;
        t.ForeColor = Fore;
        t.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(t);
    }

    void SetupButton(Button b, string text, int x, int y, int w, int h = 28)
    {
        b.Text = text;
        b.Location = new Point(x, y);
        b.Size = new Size(w, h);
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = Panel;
        b.ForeColor = Fore;
        b.FlatAppearance.BorderColor = Color.FromArgb(70, 74, 84);
        Controls.Add(b);
    }

    void SetupCombo(ComboBox c, int x, int y, int w, string[] items)
    {
        c.Location = new Point(x, y);
        c.Width = w;
        c.DropDownStyle = ComboBoxStyle.DropDownList;
        c.FlatStyle = FlatStyle.Flat;
        c.BackColor = Panel;
        c.ForeColor = Fore;
        c.Items.AddRange(items);
        c.SelectedIndex = 0;
        c.SelectedIndexChanged += (_, _) => { StopClip(); Redraw(); if (c == cmbFit) debounce.Restart(); };
        Controls.Add(c);
    }

    void SetupTrack(TrackBar t, int x, int y, int w, int min, int max, int value)
    {
        t.Location = new Point(x, y);
        t.Width = w;
        t.Minimum = min;
        t.Maximum = max;
        t.Value = value;
        t.TickStyle = TickStyle.None;
        t.ValueChanged += (_, _) => Redraw();
        Controls.Add(t);
    }

    void SetupNum(NumericUpDown n, int x, int y, int min, int max, int value)
    {
        n.Location = new Point(x, y);
        n.Width = 80;
        n.Minimum = min;
        n.Maximum = max;
        n.Value = value;
        n.DecimalPlaces = 1;
        n.BackColor = Panel;
        n.ForeColor = Fore;
        Controls.Add(n);
    }

    void SetStatus(string s) => lblStatus.Text = s;

    ConvertOptions CurrentOptions() => new(
        Fps: int.Parse((string)cmbFps.SelectedItem!),
        Fit: (FitMode)cmbFit.SelectedIndex,
        Mode: (DitherMode)cmbMode.SelectedIndex,
        Brightness: trkBright.Value / 200f,
        Contrast: trkContrast.Value / 100f,
        Invert: chkInvert.Checked,
        Start: (double)numStart.Value,
        Length: (double)numLength.Value,
        Color: chkColor.Checked);

    // ───────────────────────────── files ─────────────────────────────

    bool EnsureFfmpeg()
    {
        if (Converter.FfmpegPath != null) return true;
        MessageBox.Show(this, "This converter uses ffmpeg to read videos, and it wasn't found.\n\nDownload it from ffmpeg.org, then point me at ffmpeg.exe.",
            "ffmpeg needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        using var dlg = new OpenFileDialog { Title = "Locate ffmpeg.exe", Filter = "ffmpeg.exe|ffmpeg.exe|Programs|*.exe" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        Converter.RememberFfmpeg(dlg.FileName);
        return true;
    }

    void BrowseInput()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Pick a video",
            Filter = "Videos|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.wmv;*.m4v;*.gif;*.flv;*.mpg;*.mpeg|All files|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) _ = LoadInputAsync(dlg.FileName);
    }

    void BrowseOutput()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Save radio video",
            Filter = "Radio video (*.rdv)|*.rdv",
            FileName = Path.GetFileName(txtOutput.Text),
            InitialDirectory = Path.GetDirectoryName(txtOutput.Text),
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) txtOutput.Text = dlg.FileName;
    }

    async Task LoadInputAsync(string path)
    {
        if (!EnsureFfmpeg()) return;
        StopClip();
        txtInput.Text = path;
        txtOutput.Text = Path.ChangeExtension(path, ".rdv");
        SetStatus("Reading video…");
        duration = await Task.Run(() => Converter.GetDuration(path)) ?? 0;
        lblInfo.Text = duration > 0 ? $"Video length: {TimeSpan.FromSeconds(duration):m\\:ss}   ·   Output: {Converter.W}×{Converter.H} dots" : "Couldn't read the length.";
        rawTime = -1;
        trkTime.Value = 0;
        await RefreshPreviewAsync();
        SetStatus("Adjust the look, then press CONVERT. Drag the slider to preview other moments.");
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) _ = LoadInputAsync(files[0]);
    }

    // ───────────────────────────── preview ─────────────────────────────

    double ScrubSeconds => duration * trkTime.Value / 1000.0;

    void UpdateTimeLabel() => lblTime.Text = TimeSpan.FromSeconds(ScrubSeconds).ToString(@"m\:ss\.f");

    async Task RefreshPreviewAsync()
    {
        if (txtInput.Text.Length == 0 || Converter.FfmpegPath == null) return;
        var fit = (FitMode)cmbFit.SelectedIndex;
        bool color = chkColor.Checked;
        double t = ScrubSeconds;
        if (rawFrame != null && rawFit == fit && rawColor == color && Math.Abs(rawTime - t) < 0.01)
        {
            Redraw();
            return;
        }
        string input = txtInput.Text;
        var frame = await Task.Run(() => Converter.GrabFrame(input, t, fit, color));
        if (frame == null) return;
        rawFrame = frame;
        rawFit = fit;
        rawColor = color;
        rawTime = t;
        Redraw();
    }

    void Redraw()
    {
        if (clip != null || rawFrame == null) return;
        var o = CurrentOptions();
        if (rawColor != o.Color) return; // the right kind of frame is on its way
        preview.SetFrame(Dither.Apply(rawFrame, Converter.W, Converter.H, o), Dither.BitsFor(o));
    }

    void StopClip()
    {
        if (clip == null) return;
        anim.Stop();
        clip = null;
        btnPlay.Text = "▶  Play preview";
        Redraw();
    }

    async void TogglePlayPreview()
    {
        if (clip != null)
        {
            StopClip();
            return;
        }
        if (txtInput.Text.Length == 0 || !EnsureFfmpeg()) return;
        var o = CurrentOptions() with { Start = ScrubSeconds, Length = 6 };
        btnPlay.Enabled = false;
        btnPlay.Text = "Loading…";
        string input = txtInput.Text;
        string tmp = Path.Combine(Path.GetTempPath(), $"rdv-preview-{Environment.ProcessId}.rdv");
        try
        {
            await Task.Run(() => Converter.Convert(input, tmp, o, null, CancellationToken.None));
            var v = RdvVideo.Load(tmp);
            clip = v.Frames.Select(f => Unpack(v, f)).ToList();
            clipPos = 0;
            anim.Interval = Math.Max(15, (int)(1000 / o.Fps));
            anim.Start();
            btnPlay.Text = "■  Stop preview";
        }
        catch (Exception ex)
        {
            btnPlay.Text = "▶  Play preview";
            SetStatus("Preview failed: " + ex.Message);
        }
        finally
        {
            btnPlay.Enabled = true;
            try { File.Delete(tmp); } catch (IOException) { }
        }
    }

    static byte[] Unpack(RdvVideo v, byte[] frame)
    {
        var levels = new byte[v.Width * v.Height];
        for (int y = 0; y < v.Height; y++)
            for (int x = 0; x < v.Width; x++)
                levels[y * v.Width + x] = (byte)v.Level(frame, x, y);
        return levels;
    }

    // ───────────────────────────── convert ─────────────────────────────

    async Task ConvertAsync()
    {
        if (busy) return;
        if (!EnsureFfmpeg()) return;
        if (!File.Exists(txtInput.Text))
        {
            BrowseInput();
            return;
        }
        if (txtOutput.Text.Length == 0) BrowseOutput();
        if (txtOutput.Text.Length == 0) return;

        StopClip();
        busy = true;
        btnConvert.Enabled = false;
        btnCancel.Enabled = true;
        progress.Value = 0;
        cts = new CancellationTokenSource();
        var o = CurrentOptions();
        string input = txtInput.Text, output = txtOutput.Text;
        var prog = new Progress<double>(v => progress.Value = (int)(v * 100));
        SetStatus("Converting…");
        try
        {
            int frames = await Task.Run(() => Converter.Convert(input, output, o, prog, cts.Token));
            var len = TimeSpan.FromSeconds(frames / (double)o.Fps);
            SetStatus($"Done: {frames} frames ({len:m\\:ss}) saved to {output}\nOn the radio: drag the .rdv file onto it, or use SETTINGS → DISPLAY VIDEO.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled.");
            progress.Value = 0;
        }
        catch (Exception ex)
        {
            SetStatus("Conversion failed: " + ex.Message);
        }
        finally
        {
            busy = false;
            btnConvert.Enabled = true;
            btnCancel.Enabled = false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        cts?.Cancel();
        base.OnFormClosing(e);
    }
}

/// <summary>Shows dot levels the way the radio's display does.</summary>
sealed class DotPreview : Control
{
    byte[]? levels;
    int bits = 1;

    public DotPreview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void SetFrame(byte[] lv, int bitDepth)
    {
        levels = lv;
        bits = bitDepth;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(6, 10, 16), Color.FromArgb(1, 2, 4), 90f))
            g.FillRectangle(bg, ClientRectangle);

        int w = Converter.W, h = Converter.H;
        float pitch = Math.Min((Width - 16f) / w, (Height - 16f) / h);
        float x0 = (Width - pitch * w) / 2, y0 = (Height - pitch * h) / 2;
        float size = pitch * 0.78f;
        if (bits == RdvVideo.ColorBits)
        {
            PaintColor(g, w, h, x0, y0, pitch, size);
            return;
        }
        int max = (1 << bits) - 1;
        var main = Color.FromArgb(110, 235, 255);
        var lists = new List<RectangleF>[max + 1];
        for (int i = 0; i <= max; i++) lists[i] = new List<RectangleF>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                lists[levels == null ? 0 : Math.Min(max, (int)levels[y * w + x])].Add(new RectangleF(x0 + x * pitch, y0 + y * pitch, size, size));

        for (int lv = 0; lv <= max; lv++)
        {
            if (lists[lv].Count == 0) continue;
            int alpha = lv == 0 ? 24 : 255 * lv / max;
            using var b = new SolidBrush(Color.FromArgb(alpha, main));
            g.FillRectangles(b, lists[lv].ToArray());
        }
        if (levels == null)
        {
            using var f = new Font("Segoe UI", 11f);
            TextRenderer.DrawText(g, "Preview appears here", f, ClientRectangle, Color.FromArgb(90, 110, 235, 255),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Color dots, one batch per color; black dots show as the faint unlit grid.</summary>
    void PaintColor(Graphics g, int w, int h, float x0, float y0, float pitch, float size)
    {
        var lists = new List<RectangleF>?[256];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int v = levels![y * w + x];
                (lists[v] ??= []).Add(new RectangleF(x0 + x * pitch, y0 + y * pitch, size, size));
            }
        for (int v = 0; v < 256; v++)
        {
            if (lists[v] == null) continue;
            using var b = new SolidBrush(v == 0 ? Color.FromArgb(24, 110, 235, 255) : RdvVideo.ColorOf(v));
            g.FillRectangles(b, lists[v]!.ToArray());
        }
    }
}

static class TimerExtensions
{
    /// <summary>Restarts the countdown (used to debounce preview refreshes).</summary>
    public static void Restart(this System.Windows.Forms.Timer t)
    {
        t.Stop();
        t.Start();
    }
}
