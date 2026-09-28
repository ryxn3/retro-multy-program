using System.Drawing.Drawing2D;

namespace RetroRadio;

/// <summary>Radio Designer: build a complete custom radio and install it into Retro Radio.</summary>
sealed class DesignerForm : Form
{
    static readonly Color Back = Color.FromArgb(24, 25, 30);
    static readonly Color Panel = Color.FromArgb(34, 36, 42);
    static readonly Color Fore = Color.FromArgb(222, 226, 234);
    static readonly Color Accent = Color.FromArgb(110, 235, 255);
    static readonly Color[] PaletteMain =
    [
        Color.FromArgb(110, 235, 255), Color.FromArgb(120, 245, 235), Color.FromArgb(255, 180, 60), Color.FromArgb(120, 255, 140),
        Color.FromArgb(120, 150, 255), Color.FromArgb(255, 70, 70), Color.FromArgb(120, 235, 255),
    ];

    RadioDesign design;
    string? path;
    bool dirty;

    readonly Canvas canvas = new();
    readonly Panel scroller = new() { AutoScroll = true, Dock = DockStyle.Fill };
    readonly PropertyGrid grid = new() { Dock = DockStyle.Fill };
    readonly Label gridTitle = new() { Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
    readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripComboBox zoomBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    readonly ToolStripComboBox snapBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    readonly System.Windows.Forms.Timer renderTimer = new() { Interval = 220 };
    readonly Stack<string> undo = new(), redo = new();

    float zoom = 1f;
    bool nightView;
    Bitmap? render;
    bool renderStale = true;

    // Selection & dragging.
    object? selected;           // KeyDesign, BedDesign, PrintDesign, "display", "brand", "model" or null (whole radio)
    enum Drag { None, Move, Resize }
    Drag drag;
    int handle;                 // 0..7 when resizing
    PointF dragStart;
    RectangleF dragOrig;
    string? dragSnapshot;

    public DesignerForm(string? file)
    {
        Text = "Radio Designer";
        BackColor = Back;
        ForeColor = Fore;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1500, 900);
        KeyPreview = true;
        AllowDrop = true;

        design = BuiltInRadios.All()[0];
        design.Name = "MY RADIO";

        BuildMenus();

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, BackColor = Back };
        Controls.Add(split);
        split.BringToFront();
        split.SplitterDistance = Math.Max(400, ClientSize.Width - 390);
        scroller.BackColor = Color.FromArgb(14, 14, 17);
        scroller.Controls.Add(canvas);
        split.Panel1.Controls.Add(scroller);

        grid.BackColor = Panel;
        grid.ViewBackColor = Panel;
        grid.ViewForeColor = Fore;
        grid.LineColor = Color.FromArgb(48, 50, 58);
        grid.CategoryForeColor = Accent;
        grid.CategorySplitterColor = Color.FromArgb(48, 50, 58);
        grid.HelpBackColor = Panel;
        grid.HelpForeColor = Fore;
        grid.ToolbarVisible = false;
        grid.PropertySort = PropertySort.Categorized;
        grid.PropertyValueChanged += (_, _) => Changed(pushUndo: false);
        grid.SelectedGridItemChanged += (_, _) => { };
        gridTitle.BackColor = Color.FromArgb(40, 42, 50);
        gridTitle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        split.Panel2.Controls.Add(grid);
        split.Panel2.Controls.Add(gridTitle);

        canvas.Owner = this;
        renderTimer.Tick += (_, _) =>
        {
            renderTimer.Stop();
            if (drag == Drag.None) RenderNow();
        };

        if (file != null) Open(file);
        else
        {
            RenderNow();
            Select(null);
        }
        UpdateTitle();
        ClassicFrame.Apply(this);
    }

    // ───────────────────────────── menus ─────────────────────────────

    void BuildMenus()
    {
        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = Panel, ForeColor = Fore, Padding = new Padding(6, 4, 6, 4), RenderMode = ToolStripRenderMode.System };

        var newMenu = new ToolStripDropDownButton("New from…") { ForeColor = Fore };
        foreach (var d in BuiltInRadios.All())
        {
            var name = d.Name;
            newMenu.DropDownItems.Add(name, null, (_, _) => NewFrom(name));
        }
        tools.Items.Add(newMenu);
        tools.Items.Add(ToolBtn("Open…", (_, _) => OpenDialog()));
        tools.Items.Add(ToolBtn("Save", (_, _) => Save(false)));
        tools.Items.Add(ToolBtn("Save as…", (_, _) => Save(true)));
        tools.Items.Add(new ToolStripSeparator());

        var add = new ToolStripDropDownButton("Add key") { ForeColor = Fore };
        foreach (Btn id in Enum.GetValues<Btn>())
        {
            if (id == Btn.None) continue;
            var b = id;
            add.DropDownItems.Add(id.ToString(), null, (_, _) => AddKey(b));
        }
        tools.Items.Add(add);
        tools.Items.Add(ToolBtn("Add tray", (_, _) => AddBed()));
        tools.Items.Add(ToolBtn("Add text", (_, _) => AddPrint()));
        tools.Items.Add(ToolBtn("Duplicate", (_, _) => Duplicate()));
        tools.Items.Add(ToolBtn("Delete", (_, _) => DeleteSelected()));
        tools.Items.Add(new ToolStripSeparator());
        tools.Items.Add(ToolBtn("Undo", (_, _) => Undo()));
        tools.Items.Add(ToolBtn("Redo", (_, _) => Redo()));
        tools.Items.Add(new ToolStripSeparator());

        tools.Items.Add(new ToolStripLabel("Zoom") { ForeColor = Fore });
        zoomBox.Items.AddRange(["50%", "75%", "100%", "125%", "150%"]);
        zoomBox.SelectedIndex = 2;
        zoomBox.SelectedIndexChanged += (_, _) =>
        {
            zoom = int.Parse(((string)zoomBox.SelectedItem!).TrimEnd('%')) / 100f;
            RenderNow();
        };
        tools.Items.Add(zoomBox);
        var nightBtn = new ToolStripButton("Night view") { ForeColor = Fore, CheckOnClick = true };
        nightBtn.CheckedChanged += (_, _) =>
        {
            nightView = nightBtn.Checked;
            RenderNow();
        };
        tools.Items.Add(nightBtn);
        tools.Items.Add(new ToolStripLabel("Snap") { ForeColor = Fore });
        snapBox.Items.AddRange(["off", "2", "5", "10"]);
        snapBox.SelectedIndex = 1;
        tools.Items.Add(snapBox);
        tools.Items.Add(new ToolStripSeparator());

        var install = ToolBtn("▶  Install into radio", (_, _) => Install());
        install.Font = new Font(Font, FontStyle.Bold);
        install.ForeColor = Accent;
        tools.Items.Add(install);
        Controls.Add(tools);

        var bar = new StatusStrip { BackColor = Panel, ForeColor = Fore, SizingGrip = false };
        bar.Items.Add(status);
        Controls.Add(bar);
    }

    ToolStripButton ToolBtn(string text, EventHandler click) => new(text, null, click) { ForeColor = Fore };

    int Snap => snapBox.SelectedIndex switch { 1 => 2, 2 => 5, 3 => 10, _ => 1 };
    float SnapV(float v) => MathF.Round(v / Snap) * Snap;

    // ───────────────────────────── files ─────────────────────────────

    void NewFrom(string name)
    {
        if (!ConfirmDiscard()) return;
        design = BuiltInRadios.All().First(d => d.Name == name);
        design.Name = name + " CUSTOM";
        path = null;
        dirty = true;
        undo.Clear(); redo.Clear();
        Select(null);
        RenderNow();
        UpdateTitle();
    }

    void OpenDialog()
    {
        if (!ConfirmDiscard()) return;
        using var dlg = new OpenFileDialog { Title = "Open radio design", Filter = "Radio design (*.radio.json)|*.radio.json|JSON|*.json" };
        if (dlg.ShowDialog(this) == DialogResult.OK) Open(dlg.FileName);
    }

    void Open(string file)
    {
        try
        {
            design = RadioDesign.Load(file);
            path = file;
            dirty = false;
            undo.Clear(); redo.Clear();
            Select(null);
            RenderNow();
            UpdateTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "That file couldn't be opened as a radio design:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    bool Save(bool asNew)
    {
        if (asNew || path == null)
        {
            using var dlg = new SaveFileDialog
            {
                Title = "Save radio design",
                Filter = "Radio design (*.radio.json)|*.radio.json",
                FileName = string.Concat(design.Name.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-') + ".radio.json",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            path = dlg.FileName;
        }
        design.Save(path);
        dirty = false;
        UpdateTitle();
        SetStatus($"Saved {path}");
        return true;
    }

    void Install()
    {
        if (path == null || dirty)
        {
            if (!Save(false)) return;
        }
        try
        {
            var dest = RadioDesign.Install(path!);
            MessageBox.Show(this,
                $"\"{design.Name}\" is now installed in Retro Radio.\n\nIn the radio open SETTINGS → RADIO MODEL and pick it " +
                "(or drag the .radio.json file onto the radio).\n\nInstalled to:\n" + dest,
                "Installed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Install failed: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    bool ConfirmDiscard()
    {
        if (!dirty) return true;
        var r = MessageBox.Show(this, "Save changes to this radio first?", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return r == DialogResult.No || (r == DialogResult.Yes && Save(false));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!ConfirmDiscard()) e.Cancel = true;
        base.OnFormClosing(e);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } f && ConfirmDiscard()) Open(f[0]);
    }

    void UpdateTitle() => Text = $"Radio Designer — {design.Name}{(dirty ? " •" : "")}";

    void SetStatus(string s) => status.Text = s;

    // ───────────────────────────── editing ─────────────────────────────

    void PushUndo(string? snapshot = null)
    {
        undo.Push(snapshot ?? design.ToJson());
        redo.Clear();
        if (undo.Count > 200) { var keep = undo.Take(200).Reverse().ToList(); undo.Clear(); keep.ForEach(undo.Push); }
    }

    void Undo()
    {
        if (undo.Count == 0) return;
        redo.Push(design.ToJson());
        Restore(undo.Pop());
    }

    void Redo()
    {
        if (redo.Count == 0) return;
        undo.Push(design.ToJson());
        Restore(redo.Pop());
    }

    void Restore(string json)
    {
        design = RadioDesign.FromJson(json);
        Select(null);
        dirty = true;
        RenderNow();
        UpdateTitle();
    }

    /// <summary>Call after any change. Property grid edits push their own undo in <see cref="OnGridEditing"/>.</summary>
    void Changed(bool pushUndo = true)
    {
        if (pushUndo) PushUndo();
        dirty = true;
        renderStale = true;
        UpdateTitle();
        CheckDesign();
        canvas.Invalidate();
        renderTimer.Stop();
        renderTimer.Start();
    }

    string lastGridSnapshot = "";

    void Select(object? obj)
    {
        selected = obj;
        grid.SelectedObject = obj switch
        {
            KeyDesign or BedDesign or PrintDesign => obj,
            _ => design,
        };
        gridTitle.Text = obj switch
        {
            KeyDesign k => $"  Key: {k.Id}",
            BedDesign => "  Tray",
            PrintDesign p => $"  Text: {p.Text}",
            "display" => "  Display (see category 4)",
            "brand" or "model" => "  Brand / model (category 1)",
            _ => "  Whole radio",
        };
        lastGridSnapshot = design.ToJson();
        canvas.Invalidate();
        CheckDesign();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Snapshot before each property-grid edit so it can be undone.
        grid.PropertyValueChanged += (_, _) =>
        {
            undo.Push(lastGridSnapshot);
            redo.Clear();
            lastGridSnapshot = design.ToJson();
        };
    }

    void AddKey(Btn id)
    {
        PushUndo();
        var f = design.Face.R;
        var k = new KeyDesign
        {
            Id = id,
            Kind = id is Btn.Knob or Btn.SpeedKnob ? KeyKind.Knob : id == Btn.Play ? KeyKind.Dome : KeyKind.Rubber,
            X = SnapV(f.X + f.Width / 2 - 40), Y = SnapV(f.Y + f.Height / 2 - 18),
            W = id is Btn.Knob or Btn.SpeedKnob ? 64 : 80, H = id is Btn.Knob or Btn.SpeedKnob ? 64 : 34,
            Label = id switch
            {
                Btn.Knob => "VOLUME", Btn.SpeedKnob => "SPEED", Btn.Prev or Btn.Next or Btn.Play => "",
                Btn.SeekBack or Btn.SeekFwd => "SEEK", Btn.VolUp => "VOL +", Btn.VolDown => "VOL -", _ => id.ToString().ToUpperInvariant(),
            },
            LabelAt = id is Btn.Knob or Btn.SpeedKnob ? LabelAt.Below : LabelAt.On,
        };
        design.Keys.Add(k);
        Select(k);
        Changed(false);
    }

    void AddBed()
    {
        PushUndo();
        var f = design.Face.R;
        var b = new BedDesign { X = SnapV(f.X + 40), Y = SnapV(f.Bottom - 120), W = 240, H = 80, Radius = 8 };
        design.Beds.Add(b);
        Select(b);
        Changed(false);
    }

    void AddPrint()
    {
        PushUndo();
        var f = design.Face.R;
        var p = new PrintDesign { Text = "MY TEXT", X = SnapV(f.X + f.Width / 2 - 100), Y = SnapV(f.Bottom - 30), W = 200, H = 16 };
        design.Prints.Add(p);
        Select(p);
        Changed(false);
    }

    void Duplicate()
    {
        switch (selected)
        {
            case KeyDesign k:
                PushUndo();
                var c = k.Clone();
                c.X += 12; c.Y += 12;
                design.Keys.Add(c);
                Select(c);
                Changed(false);
                break;
            case BedDesign b:
                PushUndo();
                var nb = new BedDesign { X = b.X + 12, Y = b.Y + 12, W = b.W, H = b.H, Radius = b.Radius };
                design.Beds.Add(nb);
                Select(nb);
                Changed(false);
                break;
            case PrintDesign p:
                PushUndo();
                var np = new PrintDesign { Text = p.Text, X = p.X + 12, Y = p.Y + 12, W = p.W, H = p.H, Align = p.Align, Size = p.Size };
                design.Prints.Add(np);
                Select(np);
                Changed(false);
                break;
        }
    }

    void DeleteSelected()
    {
        bool removed = selected switch
        {
            KeyDesign k => Remove(design.Keys, k),
            BedDesign b => Remove(design.Beds, b),
            PrintDesign p => Remove(design.Prints, p),
            _ => false,
        };
        if (!removed) return;
        Select(null);
        Changed(false);
    }

    bool Remove<T>(List<T> list, T item)
    {
        PushUndo();
        return list.Remove(item);
    }

    /// <summary>Warns about things that would make the radio awkward to use.</summary>
    void CheckDesign()
    {
        var problems = new List<string>();
        foreach (var must in new[] { Btn.Play, Btn.Knob, Btn.Power })
            if (!design.Keys.Any(k => k.Id == must)) problems.Add($"no {must} control");
        var face = design.Face.R;
        int outside = design.Keys.Count(k => !face.Contains(k.Rect));
        if (outside > 0) problems.Add($"{outside} key(s) hang off the faceplate");
        var g = design.Glass;
        int overDisplay = design.Keys.Count(k => k.Rect.IntersectsWith(g));
        if (overDisplay > 0) problems.Add($"{overDisplay} key(s) cover the display");
        int overlaps = 0;
        for (int i = 0; i < design.Keys.Count; i++)
            for (int j = i + 1; j < design.Keys.Count; j++)
                if (Overlap(design.Keys[i], design.Keys[j])) overlaps++;
        if (overlaps > 0) problems.Add($"{overlaps} overlapping key pair(s)");

        string sel = selected is KeyDesign sk ? $"   ·   {sk.Id}  {sk.X:0},{sk.Y:0}  {sk.W:0}×{sk.H:0}" : "";
        SetStatus((problems.Count == 0 ? "✓ Looks good" : "⚠ " + string.Join(" · ", problems)) +
                  $"   ·   {design.Width:0}×{design.Height:0}, {design.Keys.Count} controls{sel}   ·   drag to move, drag corners to resize, arrows nudge, Del deletes, Ctrl+Z undo");
    }

    // ───────────────────────────── rendering ─────────────────────────────

    void RenderNow()
    {
        render?.Dispose();
        try
        {
            render = FaceplateRenderer.Render(design, zoom, night: nightView);
        }
        catch (Exception ex)
        {
            render = null;
            SetStatus("Render problem: " + ex.Message);
        }
        renderStale = false;
        canvas.Size = new Size((int)Math.Ceiling(design.Width * zoom) + 40, (int)Math.Ceiling(design.Height * zoom) + 40);
        canvas.Invalidate();
        CheckDesign();
    }

    const float Pad = 20;

    PointF ToDesign(Point p) => new((p.X - Pad) / zoom, (p.Y - Pad) / zoom);

    internal void PaintCanvas(Graphics g)
    {
        g.Clear(Color.FromArgb(14, 14, 17));
        if (render != null)
        {
            g.DrawImage(render, Pad, Pad, render.Width, render.Height);
        }
        g.TranslateTransform(Pad, Pad);
        g.ScaleTransform(zoom, zoom);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        DrawMockDisplay(g);
        FaceplateRenderer.DrawLive(g, design, new FaceplateRenderer.Live(Btn.None, Btn.None, true, 0.6f, 1f, false, 0.4, Color.Red, nightView));

        if (renderStale)
        {
            // While dragging, show outlines where things will end up.
            using var ghost = new Pen(Color.FromArgb(200, Accent), 1.5f / zoom) { DashStyle = DashStyle.Dash };
            foreach (var k in design.Keys)
            {
                using var p = k.Path();
                g.DrawPath(ghost, p);
            }
        }

        var sel = SelectionRect();
        if (sel is RectangleF r)
        {
            using var pen = new Pen(Accent, 1.5f / zoom);
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            if (CanResize)
            {
                using var hb = new SolidBrush(Accent);
                foreach (var h in Handles(r)) g.FillRectangle(hb, h);
            }
        }
    }

    /// <summary>A stand-in picture of the dot-matrix display so the layout reads right.</summary>
    void DrawMockDisplay(Graphics g)
    {
        var glass = design.Glass;
        using (var bg = new LinearGradientBrush(glass, Color.FromArgb(6, 10, 16), Color.FromArgb(1, 2, 4), 90f)) g.FillRectangle(bg, glass);
        var main = PaletteMain[Math.Clamp(design.Palette, 0, PaletteMain.Length - 1)];
        var st = g.Save();
        g.SetClip(glass);
        g.TranslateTransform(glass.X, glass.Y);
        g.ScaleTransform(design.DisplayScale, design.DisplayScale);
        float w = design.DisplayWidth;
        var dots = new List<RectangleF>();
        DotFont.Emit(dots, "MP3 -01 " + DotFont.Play, 16, 10, 3, 2.4f);
        const string time = "00'33";
        DotFont.Emit(dots, time, w - 16 - DotFont.Width(time, 3), 10, 3, 2.4f);
        DotFont.Emit(dots, DotFont.Note + " ARTIST - SONG TITLE", 16, 40, 2, 1.6f);
        using (var b = new SolidBrush(main)) g.FillRectangles(b, dots.ToArray());
        dots.Clear();
        int n = 24;
        for (int i = 0; i < n; i++)
        {
            float x = 20 + i * (w - 40) / n;
            int h = 4 + (int)(10 * (0.5 + 0.5 * Math.Sin(i * 0.7)));
            for (int s = 0; s < h; s++) dots.Add(new RectangleF(x, 196 - s * 8, (w - 40) / n - 4, 6));
        }
        using (var b = new SolidBrush(Color.FromArgb(200, main))) g.FillRectangles(b, dots.ToArray());
        g.Restore(st);
    }

    bool CanResize => selected is KeyDesign or BedDesign or PrintDesign or "display";

    RectangleF? SelectionRect() => selected switch
    {
        KeyDesign k => k.Tilt != 0 ? KeyBounds(k) : k.Rect,
        BedDesign b => b.Rect,
        PrintDesign p => p.Rect,
        "display" => design.Bezel.R,
        "brand" => TextBox(design.BrandAt, design.BrandAlign, design.Brand.Length * design.BrandSize * 0.62f, design.BrandSize * 1.3f),
        "model" => TextBox(design.ModelAt, design.ModelAlign, design.Model.Length * 5.5f, 12),
        _ => null,
    };

    static bool IsRound(KeyDesign k) => k.Kind is KeyKind.Knob or KeyKind.Round;

    /// <summary>Real overlap test (circles for knobs and round keys). Domes sit on top of other keys by design.</summary>
    static bool Overlap(KeyDesign a, KeyDesign b)
    {
        if (a.Kind == KeyKind.Dome || b.Kind == KeyKind.Dome) return false;
        if (IsRound(a) && IsRound(b))
        {
            float dx = a.Center.X - b.Center.X, dy = a.Center.Y - b.Center.Y;
            return MathF.Sqrt(dx * dx + dy * dy) < a.Radius + b.Radius - 2;
        }
        if (IsRound(a) || IsRound(b))
        {
            var (c, r) = IsRound(a) ? (a, b.Rect) : (b, a.Rect);
            float nx = Math.Clamp(c.Center.X, r.Left, r.Right), ny = Math.Clamp(c.Center.Y, r.Top, r.Bottom);
            float dx = c.Center.X - nx, dy = c.Center.Y - ny;
            return MathF.Sqrt(dx * dx + dy * dy) < c.Radius - 2;
        }
        return RectangleF.Inflate(a.Rect, -2, -2).IntersectsWith(b.Rect);
    }

    /// <summary>Rough area covered by the brand or model text, for clicking on it.</summary>
    static RectangleF TextBox(Pt at, TextAlign align, float w, float h)
    {
        float x = align switch { TextAlign.Center => at.X - w / 2, TextAlign.Right => at.X - w, _ => at.X };
        return new RectangleF(x - 4, at.Y - 4, w + 8, h + 8);
    }

    static RectangleF KeyBounds(KeyDesign k)
    {
        using var p = k.Path();
        return p.GetBounds();
    }

    IEnumerable<RectangleF> Handles(RectangleF r)
    {
        float s = 7 / zoom;
        foreach (var (x, y) in HandlePoints(r)) yield return new RectangleF(x - s / 2, y - s / 2, s, s);
    }

    static (float X, float Y)[] HandlePoints(RectangleF r) =>
    [
        (r.Left, r.Top), (r.X + r.Width / 2, r.Top), (r.Right, r.Top), (r.Right, r.Y + r.Height / 2),
        (r.Right, r.Bottom), (r.X + r.Width / 2, r.Bottom), (r.Left, r.Bottom), (r.Left, r.Y + r.Height / 2),
    ];

    object? HitTest(PointF p)
    {
        for (int i = design.Keys.Count - 1; i >= 0; i--)
        {
            using var path = design.Keys[i].Path(2);
            if (path.IsVisible(p)) return design.Keys[i];
        }
        foreach (var t in design.Prints) if (t.Rect.Contains(p)) return t;
        if (TextBox(design.BrandAt, design.BrandAlign, design.Brand.Length * design.BrandSize * 0.62f, design.BrandSize * 1.3f).Contains(p)) return "brand";
        if (TextBox(design.ModelAt, design.ModelAlign, design.Model.Length * 5.5f, 12).Contains(p)) return "model";
        foreach (var b in design.Beds) if (b.Rect.Contains(p)) return b;
        if (design.Bezel.R.Contains(p)) return "display";
        return null;
    }

    // ───────────────────────────── mouse ─────────────────────────────

    internal void CanvasMouseDown(MouseEventArgs e)
    {
        canvas.Focus();
        var p = ToDesign(e.Location);
        if (SelectionRect() is RectangleF r && CanResize)
        {
            var pts = HandlePoints(r);
            for (int i = 0; i < pts.Length; i++)
            {
                if (Math.Abs(p.X - pts[i].X) * zoom < 7 && Math.Abs(p.Y - pts[i].Y) * zoom < 7)
                {
                    StartDrag(Drag.Resize, p, r);
                    handle = i;
                    return;
                }
            }
        }
        var hit = HitTest(p);
        Select(hit);
        if (hit != null && SelectionRect() is RectangleF sr) StartDrag(Drag.Move, p, sr);
    }

    void StartDrag(Drag kind, PointF p, RectangleF orig)
    {
        drag = kind;
        dragStart = p;
        dragOrig = orig;
        dragSnapshot = design.ToJson();
    }

    internal void CanvasMouseMove(MouseEventArgs e)
    {
        var p = ToDesign(e.Location);
        if (drag == Drag.None)
        {
            canvas.Cursor = HitTest(p) != null ? Cursors.SizeAll : Cursors.Default;
            return;
        }
        float dx = p.X - dragStart.X, dy = p.Y - dragStart.Y;
        var r = dragOrig;
        if (drag == Drag.Move)
        {
            r.X = SnapV(dragOrig.X + dx);
            r.Y = SnapV(dragOrig.Y + dy);
        }
        else
        {
            float l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
            if (handle is 0 or 6 or 7) l = SnapV(dragOrig.Left + dx);
            if (handle is 2 or 3 or 4) rt = SnapV(dragOrig.Right + dx);
            if (handle is 0 or 1 or 2) t = SnapV(dragOrig.Top + dy);
            if (handle is 4 or 5 or 6) b = SnapV(dragOrig.Bottom + dy);
            r = RectangleF.FromLTRB(Math.Min(l, rt - 6), Math.Min(t, b - 6), Math.Max(rt, l + 6), Math.Max(b, t + 6));
        }
        ApplyRect(r);
        renderStale = true;
        dirty = true;
        canvas.Invalidate();
        CheckDesign();
    }

    internal void CanvasMouseUp()
    {
        if (drag == Drag.None) return;
        drag = Drag.None;
        if (dragSnapshot != null && dragSnapshot != design.ToJson()) PushUndo(dragSnapshot);
        dragSnapshot = null;
        grid.Refresh();
        UpdateTitle();
        RenderNow();
    }

    /// <summary>Moves/resizes whatever is selected to the rectangle <paramref name="r"/>.</summary>
    void ApplyRect(RectangleF r)
    {
        switch (selected)
        {
            case KeyDesign k:
                if (k.Kind is KeyKind.Knob or KeyKind.Round or KeyKind.Dome && drag == Drag.Resize && k.Kind == KeyKind.Knob)
                {
                    float size = Math.Max(r.Width, r.Height);
                    r = new RectangleF(r.X, r.Y, size, size);
                }
                if (k.Tilt != 0 && drag == Drag.Move)
                {
                    var b = KeyBounds(k);
                    k.X += r.X - b.X; k.Y += r.Y - b.Y;
                }
                else
                {
                    k.X = r.X; k.Y = r.Y; k.W = r.Width; k.H = r.Height;
                }
                break;
            case BedDesign bd:
                bd.X = r.X; bd.Y = r.Y; bd.W = r.Width; bd.H = r.Height;
                break;
            case PrintDesign pd:
                pd.X = r.X; pd.Y = r.Y; pd.W = r.Width; pd.H = r.Height;
                break;
            case "brand" or "model":
            {
                // Shift the anchor by however far the text box moved.
                var cur = SelectionRect()!.Value;
                var at = (string)selected == "brand" ? design.BrandAt : design.ModelAt;
                at.X += r.X - cur.X;
                at.Y += r.Y - cur.Y;
                break;
            }
            case "display":
                MoveDisplay(r);
                break;
        }
    }

    /// <summary>The display and its bezel move together; resizing zooms the display.</summary>
    void MoveDisplay(RectangleF r)
    {
        var old = design.Bezel.R;
        float padX = design.GlassX - old.X, padTop = design.GlassY - old.Y;
        float oldGlassBottom = design.Glass.Bottom;
        float padBottom = old.Bottom - oldGlassBottom;
        if (drag == Drag.Resize)
        {
            float glassW = Math.Max(80, r.Width - padX * 2);
            design.DisplayScale = Math.Clamp(glassW / design.DisplayWidth, 0.3f, 2f);
        }
        float dx = r.X - old.X, dy = r.Y - old.Y;
        design.GlassX = r.X + padX;
        design.GlassY = r.Y + padTop;
        var g = design.Glass;
        design.Bezel = new Box(r.X, r.Y, g.Width + padX * 2, g.Height + padTop + padBottom);
        foreach (var led in design.Leds) { led.X += dx; led.Y += g.Bottom - oldGlassBottom; }
        foreach (var p in design.Prints.Where(p => old.Contains(p.X + 1, p.Y + 1)))
        {
            p.X += dx;
            p.Y = g.Bottom + 3;
            p.W = design.Bezel.W;
        }
        if (drag == Drag.Resize)
            design.Leds = [new(g.X + 8, g.Bottom + padBottom / 2 - 2), new(g.Right - 20, g.Bottom + padBottom / 2 - 2)];
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (grid.ContainsFocus) return base.ProcessCmdKey(ref msg, keyData);
        switch (keyData)
        {
            case Keys.Control | Keys.Z: Undo(); return true;
            case Keys.Control | Keys.Y: Redo(); return true;
            case Keys.Control | Keys.S: Save(false); return true;
            case Keys.Control | Keys.D: Duplicate(); return true;
            case Keys.Delete: DeleteSelected(); return true;
            case Keys.Escape: Select(null); return true;
        }
        var step = (keyData & Keys.Shift) != 0 ? 10 : 1;
        var key = keyData & ~Keys.Shift;
        if (key is Keys.Left or Keys.Right or Keys.Up or Keys.Down && SelectionRect() is RectangleF r)
        {
            PushUndo();
            drag = Drag.Move;
            r.Offset(key == Keys.Left ? -step : key == Keys.Right ? step : 0, key == Keys.Up ? -step : key == Keys.Down ? step : 0);
            if (selected is KeyDesign k && k.Tilt != 0) { var b = KeyBounds(k); r = new RectangleF(r.X, r.Y, b.Width, b.Height); }
            ApplyRect(r);
            drag = Drag.None;
            Changed(false);
            grid.Refresh();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>The drawing surface inside the scroll panel.</summary>
    sealed class Canvas : Control
    {
        public DesignerForm? Owner;

        public Canvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        }

        protected override void OnPaint(PaintEventArgs e) => Owner?.PaintCanvas(e.Graphics);
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Owner?.CanvasMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Owner?.CanvasMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Owner?.CanvasMouseUp(); }
        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    }
}
