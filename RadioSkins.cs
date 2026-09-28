using System.Drawing.Imaging;

namespace RetroRadio;

/// <summary>Which radio model is showing, and drawing it (via <see cref="FaceplateRenderer"/>).</summary>
sealed partial class RadioForm
{
    static List<RadioDesign> designs = BuiltInRadios.All();
    static int styleIdx;
    static RadioDesign Design => designs[Math.Clamp(styleIdx, 0, designs.Count - 1)];

    // Layout of the current model (logical pixels).
    static float BW => Design.Width;
    static float BH => Design.Height;
    static RectangleF Glass => Design.Glass;
    static float GW => Design.DisplayWidth;
    static float K => Design.DisplayScale;

    Bitmap? pressedCache;
    int renderToken;

    /// <summary>Built-in models followed by the ones imported from Radio Designer.</summary>
    static void ReloadDesigns()
    {
        string current = designs.Count > 0 ? Design.Name : "";
        designs = [.. BuiltInRadios.All(), .. RadioDesign.LoadCustom()];
        int i = designs.FindIndex(d => d.Name == current);
        styleIdx = i >= 0 ? i : Math.Clamp(styleIdx, 0, designs.Count - 1);
    }

    void BuildButtons()
    {
        buttons.Clear();
        buttons.AddRange(Design.Keys);
    }

    /// <summary>Switches to another radio model and rebuilds everything that depends on it.</summary>
    void ApplyStyle(int idx)
    {
        styleIdx = Wrap(idx, designs.Count);
        paletteIdx = Design.Palette;
        hover = pressed = Btn.None;
        BuildButtons();
        if (!IsHandleCreated) return;
        // Keep the window centred where it was while its shape changes.
        var center = new Point(Left + Width / 2, Top + Height / 2);
        ApplyScale();
        Location = new Point(center.X - Width / 2, center.Y - Height / 2);
    }

    void ApplyStyle(string name)
    {
        int i = designs.FindIndex(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) ApplyStyle(i);
    }

    void RebuildFaceplates()
    {
        // Render the new faceplate first; only then swap it in, so painting never meets a disposed image.
        Bitmap fresh;
        try
        {
            fresh = FaceplateRenderer.Render(Design, S, night: night);
        }
        catch (Exception)
        {
            Flash("DISPLAY ERROR", 2);
            return; // keep showing the previous faceplate
        }
        var oldFace = faceCache;
        var oldPressed = pressedCache;
        faceCache = fresh;
        spillKey = null; // the lit copy is remade from the new faceplate
        pressedCache = null;
        oldFace?.Dispose();
        oldPressed?.Dispose();
        // The pushed-in version is only needed once a key is pressed, so it's made in the background.
        var (design, scale, isNight, token) = (Design, S, night, ++renderToken);
        Task.Run(() => FaceplateRenderer.Render(design, scale, pressed: true, night: isNight)).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && token == renderToken && !IsDisposed) BeginInvoke(() =>
            {
                if (token == renderToken) pressedCache = t.Result;
                else t.Result.Dispose();
            });
        });
    }

    /// <summary>Imports a .radio.json made with Radio Designer and switches to it.</summary>
    void ImportDesign(string file)
    {
        try
        {
            var installed = RadioDesign.Install(file);
            var name = RadioDesign.Load(installed).Name;
            ReloadDesigns();
            ApplyStyle(name);
            Flash("RADIO IMPORTED");
        }
        catch (Exception)
        {
            Flash("NOT A RADIO FILE", 2.5);
        }
    }

    void PickDesign()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Import a radio design",
            Filter = "Radio design (*.radio.json)|*.radio.json|JSON|*.json",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) ImportDesign(dlg.FileName);
    }

    KeyDesign? HitTest(PointF p)
    {
        foreach (var b in buttons)
        {
            if (b.Kind == KeyKind.Knob)
            {
                float dx = p.X - b.Center.X, dy = p.Y - b.Center.Y;
                if (dx * dx + dy * dy <= b.Radius * b.Radius) return b;
                continue;
            }
            using var path = b.Path(2);
            if (path.IsVisible(p)) return b;
        }
        return null;
    }

    /// <summary>Faceplate: cached render, the held key swapped for its pressed-in version, then live details.</summary>
    void DrawFaceplate(Graphics g, bool spill)
    {
        // The bottom layer: copied straight, no blending or filtering.
        var face = spill ? LitFace() : faceCache!;
        var mode = g.CompositingMode;
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        // Only the part being repainted.
        var part = Rectangle.Intersect(Rectangle.Ceiling(g.ClipBounds), new Rectangle(0, 0, face.Width, face.Height));
        if (part.Width > 0 && part.Height > 0) g.DrawImage(face, part, part, GraphicsUnit.Pixel);
        g.CompositingMode = mode;
        var held = pressed == Btn.None ? null : buttons.FirstOrDefault(b => b.Id == pressed && b.Kind != KeyKind.Knob);
        if (held != null && pressedCache != null)
        {
            var r = FaceplateRenderer.PressRegion(held);
            var px = Rectangle.Round(new RectangleF(r.X * S, r.Y * S, r.Width * S, r.Height * S));
            px.Intersect(new Rectangle(0, 0, pressedCache.Width, pressedCache.Height));
            if (px.Width > 0 && px.Height > 0 && pressedCache.Width == faceCache.Width) g.DrawImage(pressedCache, px, px, GraphicsUnit.Pixel);
        }
    }

    FaceplateRenderer.Live LiveState() => new(hover, pressed, player.IsPlaying, engine.Volume, engine.Speed, engine.Muted, now, Palettes[paletteIdx].Accent, night);

    bool night;

    Bitmap? litFace;
    (Bitmap Face, Color Main, bool Night)? spillKey;

    /// <summary>
    /// The faceplate with the display's light already on it. The light only changes with the color and
    /// day/night, so it's drawn once here instead of every frame (it was the costliest part of a frame).
    /// </summary>
    Bitmap LitFace()
    {
        var main = Design.Screen == ScreenKind.Modern ? ModernAccent : Palettes[paletteIdx].Main;
        var key = (faceCache!, main, night);
        if (litFace != null && spillKey == key) return litFace;
        var lit = new Bitmap(faceCache!.Width, faceCache.Height, faceCache.PixelFormat);
        using (var g = Graphics.FromImage(lit))
        {
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(faceCache, 0, 0, faceCache.Width, faceCache.Height);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.ScaleTransform(S, S);
            DrawDisplaySpill(g, main);
        }
        litFace?.Dispose();
        litFace = lit;
        spillKey = key;
        return lit;
    }

    /// <summary>
    /// Light from the display spilling onto the bezel around it — faint by day, strong at night.
    /// </summary>
    void DrawDisplaySpill(Graphics g, Color main)
    {
        float strength = night ? 1f : 0.35f;
        var r = Glass;
        // Wide, soft wash of display light over the faceplate.
        var wash = RectangleF.Inflate(r, r.Width * 0.35f, r.Height * 0.6f);
        using (var wp = new System.Drawing.Drawing2D.GraphicsPath())
        {
            wp.AddEllipse(wash);
            using var wb = new System.Drawing.Drawing2D.PathGradientBrush(wp)
            {
                CenterColor = Color.FromArgb((int)(strength * 30), main),
                SurroundColors = [Color.FromArgb(0, main)],
                FocusScales = new PointF(0.45f, 0.35f),
            };
            g.FillPath(wb, wp);
        }
        for (int i = 1; i <= 12; i++)
        {
            int a = (int)(strength * 26 * (1 - i / 13f) * (1 - i / 13f));
            if (a <= 0) continue;
            using var pen = new Pen(Color.FromArgb(a, main), 2.2f);
            using var p = FaceplateRenderer.RoundRect(RectangleF.Inflate(r, i * 2, i * 2), 6 + i * 2);
            g.DrawPath(pen, p);
        }
    }

    /// <summary>What makes a VFD look like a VFD: the thin filament wires in front of the glow, behind smoky glass.</summary>
    void DrawVfdDetails(Graphics g)
    {
        var r = Glass;
        using var wire = new Pen(Color.FromArgb(60, 0, 0, 0), 0.7f);
        using var wireLit = new Pen(Color.FromArgb(14, 255, 230, 200), 0.4f);
        foreach (float f in new[] { 0.14f, 0.36f, 0.58f, 0.8f })
        {
            float y = r.Y + r.Height * f;
            g.DrawLine(wire, r.X, y, r.Right, y + 0.6f);
            g.DrawLine(wireLit, r.X, y - 0.6f, r.Right, y);
        }
        // Smoky tinted lens.
        using var tint = new SolidBrush(Color.FromArgb(night ? 10 : 18, 30, 40, 50));
        g.FillRectangle(tint, r);
    }

    /// <summary>Converts a window point to display-content coordinates.</summary>
    static PointF ToDisplay(PointF p) => new((p.X - Glass.X) / K, (p.Y - Glass.Y) / K);
}
