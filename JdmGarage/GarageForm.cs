using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.Json;
using RetroRadio;

namespace JdmGarage;

/// <summary>
/// The garage: one vehicle at a time under the neon sign, with its specs and the radio fitted in it.
/// Arrows / mouse wheel drive the next one in, Enter opens Retro Radio with that car's radio.
/// </summary>
sealed class GarageForm : Form
{
    public const int W = 1280, H = 760;
    const float Ground = 520;

    readonly List<RadioDesign> radios = BuiltInRadios.All();
    readonly Dictionary<(int, bool), Bitmap> faceplates = [];
    readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Random rng = new();

    int index;
    int leaving = -1;          // the car driving out during a change
    int direction = 1;
    double changeStart = -10;  // when the current change began
    bool night = DateTime.Now.Hour is >= 18 or < 6;
    string status = "";
    double statusUntil;
    RectangleF playButton, nightButton, prevArrow, nextArrow;
    readonly List<RectangleF> dots = [];
    bool hoverPlay;

    static string StateFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JdmGarage", "state.json");

    public GarageForm(int? car = null, bool? nightMode = null)
    {
        Text = "JDM Garage";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        BackColor = Color.Black;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
        Icon = MakeIcon();
        LoadState();
        if (car != null) index = Math.Clamp(car.Value, 0, Garage.Cars.Length - 1);
        if (nightMode != null) night = nightMode.Value;

        timer.Tick += (_, _) => Invalidate();
        timer.Start();
        ClassicFrame.Apply(this);
        float k = DeviceDpi / 96f * 0.85f;
        ClientSize = new Size((int)(W * k), (int)(H * k));
        MinimumSize = new Size(640, 400);
    }

    Car Current => Garage.Cars[index];

    void LoadState()
    {
        try
        {
            if (!File.Exists(StateFile)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(StateFile));
            if (doc.RootElement.TryGetProperty("car", out var c)) index = Math.Clamp(c.GetInt32(), 0, Garage.Cars.Length - 1);
        }
        catch (Exception) { }
    }

    void SaveState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(new { car = index }));
        }
        catch (Exception) { }
    }

    // ---------- Input ----------

    void Go(int to, int dir)
    {
        to = (to % Garage.Cars.Length + Garage.Cars.Length) % Garage.Cars.Length;
        if (to == index) return;
        leaving = index;
        index = to;
        direction = dir;
        changeStart = clock.Elapsed.TotalSeconds;
        SaveState();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Right or Keys.D: Go(index + 1, 1); break;
            case Keys.Left or Keys.A: Go(index - 1, -1); break;
            case Keys.Enter or Keys.Space: PlayRadio(); break;
            case Keys.N: night = !night; break;
            case Keys.R: Go(rng.Next(Garage.Cars.Length), 1); break;
            case Keys.Escape: Close(); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseEventArgs e) => Go(index + (e.Delta < 0 ? 1 : -1), e.Delta < 0 ? 1 : -1);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool hover = playButton.Contains(ToLogical(e.Location));
        if (hover != hoverPlay) { hoverPlay = hover; Cursor = hover ? Cursors.Hand : Cursors.Default; }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var p = ToLogical(e.Location);
        if (playButton.Contains(p)) PlayRadio();
        else if (nightButton.Contains(p)) night = !night;
        else if (prevArrow.Contains(p)) Go(index - 1, -1);
        else if (nextArrow.Contains(p)) Go(index + 1, 1);
        else
            for (int i = 0; i < dots.Count; i++)
                if (RectangleF.Inflate(dots[i], 5, 8).Contains(p)) Go(i, i > index ? 1 : -1);
    }

    (float K, float X, float Y) Fit()
    {
        float k = Math.Min(ClientSize.Width / (float)W, ClientSize.Height / (float)H);
        return (k, (ClientSize.Width - W * k) / 2, (ClientSize.Height - H * k) / 2);
    }

    PointF ToLogical(Point p)
    {
        var (k, x, y) = Fit();
        return new((p.X - x) / k, (p.Y - y) / k);
    }

    // ---------- Launching the radio ----------

    void PlayRadio()
    {
        int style = radios.FindIndex(r => r.Name == Current.Radio);
        string? exe = FindRadio();
        if (exe == null)
        {
            Flash("RETRO RADIO NOT FOUND - INSTALL IT FROM THE LAUNCHER");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(exe, $"--style {Math.Max(0, style)}") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
            Flash("STARTING " + Current.Radio + " ...");
        }
        catch (Exception ex)
        {
            Flash("COULD NOT START THE RADIO: " + ex.Message.ToUpperInvariant());
        }
    }

    void Flash(string text)
    {
        status = text;
        statusUntil = clock.Elapsed.TotalSeconds + 4;
    }

    /// <summary>Next to this program, where the launcher installs it, or a local build.</summary>
    static string? FindRadio()
    {
        string here = AppContext.BaseDirectory;
        var candidates = new List<string> { Path.Combine(here, "RetroRadio.exe") };
        string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroMultyProgram", "apps", "radio");
        try
        {
            string info = Path.Combine(apps, "installed.json");
            if (File.Exists(info))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(info));
                if (doc.RootElement.TryGetProperty("ExePath", out var p) && p.GetString() is { } s) candidates.Add(s);
            }
            if (Directory.Exists(apps)) candidates.AddRange(Directory.EnumerateFiles(apps, "RetroRadio.exe", SearchOption.AllDirectories));
        }
        catch (Exception) { }
        // A developer build: walk up to the solution folder.
        for (var dir = new DirectoryInfo(here); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "RetroRadio.csproj")))
            {
                candidates.Add(Path.Combine(dir.FullName, "bin", "Release", "net8.0-windows", "RetroRadio.exe"));
                candidates.Add(Path.Combine(dir.FullName, "bin", "Debug", "net8.0-windows", "RetroRadio.exe"));
                break;
            }
        return candidates.FirstOrDefault(File.Exists);
    }

    // ---------- Drawing ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.Black);
        var (k, x, y) = Fit();
        g.TranslateTransform(x, y);
        g.ScaleTransform(k, k);
        g.SetClip(new RectangleF(0, 0, W, H));
        Render(g, (float)clock.Elapsed.TotalSeconds);
    }

    public void Render(Graphics g, float t)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        DrawRoom(g, t);

        // Drive-in / drive-out animation.
        double p = Math.Clamp((t - changeStart) / 1.1, 0, 1);
        double ease = 1 - Math.Pow(1 - p, 3);
        if (leaving >= 0 && p < 1)
        {
            double outP = Math.Clamp(p * 1.6, 0, 1);
            float off = (float)(-direction * outP * outP * 1500);
            DrawVehicleScene(g, Garage.Cars[leaving], off, t, off);
        }
        float inOff = p < 1 ? (float)(direction * (1 - ease) * 1500) : 0;
        DrawVehicleScene(g, Current, inOff, t, inOff);

        DrawHud(g, t);
    }

    void DrawRoom(Graphics g, float t)
    {
        // Back wall: concrete panels.
        var wall = new RectangleF(0, 0, W, 430);
        using (var wb = new LinearGradientBrush(wall, night ? Color.FromArgb(34, 36, 44) : Color.FromArgb(118, 120, 124), night ? Color.FromArgb(16, 17, 22) : Color.FromArgb(78, 80, 86), 90f))
            g.FillRectangle(wb, wall);
        using (var seam = new Pen(Color.FromArgb(night ? 40 : 60, 0, 0, 0), 2))
        {
            for (int i = 1; i < 8; i++) g.DrawLine(seam, i * 160, 60, i * 160, 430);
            g.DrawLine(seam, 0, 250, W, 250);
        }
        // Tie-holes in the concrete.
        using (var hole = new SolidBrush(Color.FromArgb(night ? 50 : 70, 0, 0, 0)))
            for (int i = 0; i < 8; i++)
                for (int j = 0; j < 2; j++)
                {
                    g.FillEllipse(hole, i * 160 + 40, 130 + j * 170, 6, 6);
                    g.FillEllipse(hole, i * 160 + 114, 130 + j * 170, 6, 6);
                }

        // Rolling shutter on the right, tire stack and tool board on the left.
        var shutter = new RectangleF(1010, 150, 230, 280);
        using (var sb = new SolidBrush(night ? Color.FromArgb(42, 44, 50) : Color.FromArgb(150, 152, 156)))
            g.FillRectangle(sb, shutter);
        using (var rib = new Pen(Color.FromArgb(night ? 60 : 80, 0, 0, 0), 1.5f))
            for (float yy = shutter.Top + 6; yy < shutter.Bottom; yy += 9) g.DrawLine(rib, shutter.Left, yy, shutter.Right, yy);
        using (var frame = new Pen(night ? Color.FromArgb(70, 72, 80) : Color.FromArgb(60, 60, 64), 6))
            g.DrawRectangle(frame, shutter.X, shutter.Y, shutter.Width, shutter.Height);

        var board = new RectangleF(40, 170, 250, 150);
        using (var bb = new SolidBrush(night ? Color.FromArgb(46, 38, 30) : Color.FromArgb(160, 130, 96)))
            g.FillRectangle(bb, board);
        using (var peg = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            for (float xx = board.Left + 10; xx < board.Right; xx += 14)
                for (float yy = board.Top + 10; yy < board.Bottom; yy += 14) g.FillEllipse(peg, xx, yy, 3, 3);
        using (var tool = new Pen(night ? Color.FromArgb(120, 124, 134) : Color.FromArgb(70, 72, 78), 5) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            for (int i = 0; i < 6; i++) g.DrawLine(tool, board.Left + 22 + i * 18, board.Top + 24, board.Left + 22 + i * 18, board.Top + 70 + i * 6);
            g.DrawEllipse(tool, board.Left + 150, board.Top + 30, 50, 50);
            g.DrawLine(tool, board.Left + 150, board.Top + 110, board.Left + 230, board.Top + 110);
        }
        using (var tyre = new SolidBrush(Color.FromArgb(night ? 12 : 30, night ? 12 : 30, night ? 14 : 34)))
        using (var tread = new Pen(Color.FromArgb(night ? 30 : 60, 255, 255, 255), 1))
            for (int i = 0; i < 4; i++)
            {
                var r = new RectangleF(60, 345 + i * 21 - 3, 130, 26);
                g.FillEllipse(tyre, r);
                g.DrawEllipse(tread, RectangleF.Inflate(r, -30, -7));
            }

        // Fluorescent tubes.
        for (int i = 0; i < 3; i++)
        {
            var tube = new RectangleF(170 + i * 390, 16, 160, 7);
            bool flicker = night && i == 2 && Math.Sin(t * 13) > 0.94;
            using var tb = new SolidBrush(flicker ? Color.FromArgb(120, 130, 140) : Color.FromArgb(235, 245, 255));
            if (!flicker)
            {
                using var glow = new SolidBrush(Color.FromArgb(night ? 26 : 16, 220, 240, 255));
                g.FillEllipse(glow, tube.X - 60, tube.Y - 40, tube.Width + 120, 110);
            }
            g.FillRectangle(tb, tube);
        }

        DrawNeon(g, t);

        // Floor: polished epoxy with parking lines.
        var floor = new RectangleF(0, 430, W, H - 430);
        using (var fb = new LinearGradientBrush(floor, night ? Color.FromArgb(28, 30, 36) : Color.FromArgb(96, 98, 104), night ? Color.FromArgb(8, 8, 10) : Color.FromArgb(46, 48, 52), 90f))
            g.FillRectangle(fb, floor);
        using (var baseboard = new SolidBrush(Color.FromArgb(night ? 80 : 60, 0, 0, 0)))
            g.FillRectangle(baseboard, 0, 426, W, 6);
        using (var line = new Pen(Color.FromArgb(night ? 110 : 160, 230, 200, 40), 5))
        {
            g.DrawLine(line, 90, 700, 250, 440);
            g.DrawLine(line, 1190, 700, 1030, 440);
        }
    }

    void DrawNeon(Graphics g, float t)
    {
        const string Sign = "JDM GARAGE";
        const string SignJp = "ガレージ";
        bool on = !night || Math.Sin(t * 2.3) > -0.97; // the odd flicker
        var pink = Color.FromArgb(255, 70, 170);
        var cyan = Color.FromArgb(80, 230, 255);
        using var font = new Font("Arial Black", 50, FontStyle.Italic, GraphicsUnit.Pixel);
        using var fontJp = new Font("MS Gothic", 32, FontStyle.Bold, GraphicsUnit.Pixel);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        // Mounting board.
        using (var board = new SolidBrush(Color.FromArgb(night ? 70 : 110, 10, 10, 14)))
            g.FillRectangle(board, 400, 36, 480, 118);
        DrawNeonText(g, Sign, font, new RectangleF(340, 36, 600, 76), fmt, pink, on);
        DrawNeonText(g, SignJp, fontJp, new RectangleF(340, 104, 600, 48), fmt, cyan, on || !night);
    }

    void DrawNeonText(Graphics g, string text, Font font, RectangleF r, StringFormat fmt, Color col, bool on)
    {
        using var path = new GraphicsPath();
        path.AddString(text, font.FontFamily, (int)font.Style, font.Size, r, fmt);
        if (on)
        {
            int[] widths = night ? [22, 14, 8] : [10, 6];
            foreach (int w in widths)
            {
                using var glow = new Pen(Color.FromArgb(night ? 34 : 20, col), w) { LineJoin = LineJoin.Round };
                g.DrawPath(glow, path);
            }
        }
        using var tube = new Pen(on ? Garage.FaceplateLight(col, night ? 0.55f : 0.3f) : Garage.FaceplateDark(col, 0.6f), 3.2f) { LineJoin = LineJoin.Round };
        g.DrawPath(tube, path);
    }

    /// <summary>The vehicle with its shadow, reflection and (at night) headlight beams, <paramref name="offset"/> px from center.</summary>
    void DrawVehicleScene(Graphics g, Car car, float offset, float t, float travelled)
    {
        float len = 560 * Garage.LengthOf(car);
        float x0 = (W - len) / 2 + offset;
        float wheelR = len / 100 * (car.Shape == Shape.Dekotora ? 5.4f : 7.3f);
        float spin = travelled / Math.Max(1, wheelR) * (direction >= 0 ? 1 : 1);

        // Headlight beams across the floor.
        if (night)
        {
            float fx = x0 + len;
            using var beam = new GraphicsPath();
            beam.AddPolygon([new PointF(fx, Ground - len * 0.17f), new PointF(fx + 520, Ground - 30), new PointF(fx + 520, Ground + 60), new PointF(fx, Ground - len * 0.1f)]);
            using var bb = new LinearGradientBrush(new RectangleF(fx, 0, 521, 1), Color.FromArgb(70, 255, 245, 210), Color.FromArgb(0, 255, 245, 210), 0f);
            g.FillPath(bb, beam);
        }

        // Contact shadow.
        using (var sp = new GraphicsPath())
        {
            sp.AddEllipse(x0 - len * 0.04f, Ground - 12, len * 1.08f, 28);
            using var shadow = new PathGradientBrush(sp) { CenterColor = Color.FromArgb(200, 0, 0, 0), SurroundColors = [Color.FromArgb(0, 0, 0, 0)] };
            g.FillPath(shadow, sp);
        }

        // Render once, draw it and its mirror image on the floor.
        int bw = (int)len + 60, bh = (int)(len * 0.62f) + 20;
        using var bmp = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb);
        using (var bg = Graphics.FromImage(bmp))
        {
            bg.SmoothingMode = SmoothingMode.AntiAlias;
            bg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            bg.TranslateTransform(20, bh - 2);
            Garage.Draw(bg, car, len, spin, t, night);
        }
        var dest = new RectangleF(x0 - 20, Ground - bh + 2, bw, bh);
        var ia = new ImageAttributes();
        ia.SetColorMatrix(new ColorMatrix { Matrix33 = night ? 0.22f : 0.16f });
        var st = g.Save();
        g.SetClip(new RectangleF(0, Ground, W, 150), CombineMode.Intersect);
        g.DrawImage(bmp, [new PointF(dest.X, Ground + bh - 2), new PointF(dest.Right, Ground + bh - 2), new PointF(dest.X, Ground - 2)],
            new RectangleF(0, 0, bw, bh), GraphicsUnit.Pixel, ia);
        g.Restore(st);
        // Fade the reflection into the floor.
        using (var fade = new LinearGradientBrush(new RectangleF(0, Ground, 1, 150), Color.FromArgb(0, 0, 0, 0), night ? Color.FromArgb(255, 8, 8, 10) : Color.FromArgb(255, 46, 48, 52), 90f))
        {
            var st2 = g.Save();
            g.SetClip(new RectangleF(0, Ground, W, 150), CombineMode.Intersect);
            g.FillRectangle(fade, 0, Ground, W, 150);
            g.Restore(st2);
        }
        g.DrawImage(bmp, dest);
    }

    void DrawHud(Graphics g, float t)
    {
        var car = Current;
        using var dim = new SolidBrush(Color.FromArgb(150, 160, 175));
        using var white = new SolidBrush(Color.FromArgb(236, 238, 244));
        using var amber = new SolidBrush(Color.FromArgb(255, 186, 70));
        using var small = new Font("Consolas", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        using var tiny = new Font("Consolas", 11, GraphicsUnit.Pixel);

        // Counter and day/night switch.
        g.DrawString($"{index + 1:00} / {Garage.Cars.Length:00}", small, white, 1180, 20);
        nightButton = new RectangleF(24, 16, 128, 30);
        using (var nb = new SolidBrush(Color.FromArgb(night ? 120 : 90, 0, 0, 0)))
            g.FillRectangle(nb, nightButton);
        using (var np = new Pen(Color.FromArgb(90, 255, 255, 255)))
            g.DrawRectangle(np, nightButton.X, nightButton.Y, nightButton.Width, nightButton.Height);
        g.DrawString(night ? "☾ NIGHT  [N]" : "☀ DAY    [N]", small, white, nightButton.X + 10, nightButton.Y + 7);

        // Arrows.
        prevArrow = new RectangleF(10, 300, 60, 120);
        nextArrow = new RectangleF(W - 70, 300, 60, 120);
        using (var arrow = new SolidBrush(Color.FromArgb(160, 255, 255, 255)))
        {
            g.FillPolygon(arrow, [new PointF(56, 320), new PointF(24, 360), new PointF(56, 400)]);
            g.FillPolygon(arrow, [new PointF(W - 56, 320), new PointF(W - 24, 360), new PointF(W - 56, 400)]);
        }

        // Position dots.
        dots.Clear();
        float dx = W / 2f - Garage.Cars.Length * 9;
        for (int i = 0; i < Garage.Cars.Length; i++)
        {
            var r = new RectangleF(dx + i * 18, 548, 10, 10);
            dots.Add(r);
            using var db = new SolidBrush(i == index ? Color.FromArgb(255, 70, 170) : Color.FromArgb(90, 255, 255, 255));
            g.FillEllipse(db, r);
        }

        // Info card.
        var card = new RectangleF(24, 572, 760, 170);
        DrawPanel(g, card);
        var vfd = new RectangleF(card.X + 16, card.Y + 14, 440, 34);
        using (var vb = new SolidBrush(Color.FromArgb(8, 16, 16)))
            g.FillRectangle(vb, vfd);
        var lit = new List<RectangleF>();
        string name = DotFont.Fit(car.Name, vfd.Width - 16, 3.1f);
        DotFont.Emit(lit, name, vfd.X + 8, vfd.Y + 6, 3.1f, 2.5f);
        using (var dotGlow = new SolidBrush(Color.FromArgb(60, 93, 242, 220)))
        using (var dot = new SolidBrush(Color.FromArgb(150, 255, 240)))
        {
            foreach (var d in lit) g.FillRectangle(dotGlow, RectangleF.Inflate(d, 1, 1));
            foreach (var d in lit) g.FillRectangle(dot, d);
        }
        using (var jp = new Font("Yu Gothic UI", 26, FontStyle.Bold, GraphicsUnit.Pixel))
            g.DrawString(car.Japanese, jp, white, vfd.Right + 14, vfd.Y - 2);
        g.DrawString(car.Year, small, amber, card.Right - 60, card.Y + 16);

        for (int i = 0; i < car.Specs.Length; i++)
        {
            float sx = card.X + 18 + (i % 2) * 330, sy = card.Y + 62 + (i / 2) * 22;
            g.FillRectangle(amber, sx, sy + 5, 5, 5);
            g.DrawString(car.Specs[i], small, white, sx + 12, sy);
        }
        using (var note = new Font("Segoe UI", 14, FontStyle.Italic, GraphicsUnit.Pixel))
            g.DrawString(car.Note, note, dim, new RectangleF(card.X + 18, card.Y + 112, card.Width - 36, 50));

        // Radio card.
        var rc = new RectangleF(800, 572, 456, 170);
        DrawPanel(g, rc);
        using (var jpSmall = new Font("Yu Gothic UI", 13, FontStyle.Bold, GraphicsUnit.Pixel))
            g.DrawString("車載ラジオ  ·  IN-CAR RADIO", jpSmall, dim, rc.X + 14, rc.Y + 8);
        var face = Faceplate(car);
        if (face != null)
        {
            var box = new RectangleF(rc.X + 14, rc.Y + 32, rc.Width - 28, 90);
            float s = Math.Min(box.Width / face.Width, box.Height / face.Height);
            var fr = new RectangleF(box.X + (box.Width - face.Width * s) / 2, box.Y + (box.Height - face.Height * s) / 2, face.Width * s, face.Height * s);
            g.DrawImage(face, fr);
        }
        playButton = new RectangleF(rc.X + 14, rc.Bottom - 40, rc.Width - 28, 30);
        using (var pb = new LinearGradientBrush(playButton, hoverPlay ? Color.FromArgb(255, 110, 190) : Color.FromArgb(230, 60, 150), Color.FromArgb(150, 20, 90), 90f))
            g.FillRectangle(pb, playButton);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString($"▶  PLAY IN {car.Radio}   [ENTER]", small, white, playButton, fmt);

        if (t < statusUntil)
        {
            var sr = new RectangleF(W / 2f - 300, 470, 600, 30);
            using var sb = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
            g.FillRectangle(sb, sr);
            g.DrawString(status, small, amber, sr, fmt);
        }
        g.DrawString("← →  DRIVE THROUGH THE COLLECTION     R  RANDOM", tiny, dim, 24, H - 16);
    }

    static void DrawPanel(Graphics g, RectangleF r)
    {
        using var bg = new SolidBrush(Color.FromArgb(215, 14, 15, 20));
        g.FillRectangle(bg, r);
        using var edge = new Pen(Color.FromArgb(70, 255, 255, 255));
        g.DrawRectangle(edge, r.X, r.Y, r.Width, r.Height);
        using var accent = new SolidBrush(Color.FromArgb(255, 70, 170));
        g.FillRectangle(accent, r.X, r.Y, 4, r.Height);
    }

    Bitmap? Faceplate(Car car)
    {
        if (faceplates.TryGetValue((index, night), out var bmp)) return bmp;
        var design = radios.FirstOrDefault(r => r.Name == car.Radio);
        if (design == null) return null;
        float s = Math.Min(430f / design.Width, 90f / design.Height) * 1.5f;
        bmp = FaceplateRenderer.Render(design, s, night: night);
        faceplates[(index, night)] = bmp;
        return bmp;
    }

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.TranslateTransform(2, 46);
            Garage.Draw(g, Garage.Cars[0], 60, 0, 0, false);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        timer.Stop();
        SaveState();
        foreach (var b in faceplates.Values) b.Dispose();
        base.OnFormClosing(e);
    }

    /// <summary>Renders the garage off-screen (for the launcher picture and tests).</summary>
    public static void Snapshot(string path, int car, bool night, float scale)
    {
        using var f = new GarageForm(car, night);
        using var bmp = new Bitmap((int)(W * scale), (int)(H * scale));
        using (var g = Graphics.FromImage(bmp))
        {
            g.ScaleTransform(scale, scale);
            f.Render(g, 5f);
        }
        bmp.Save(path);
    }
}
