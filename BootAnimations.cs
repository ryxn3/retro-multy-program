using System.Drawing.Drawing2D;

namespace RetroRadio;

enum BootStyle { Off, Classic, Scanner, Rain, Static, Terminal, Bounce }
enum ShutStyle { Off, TvOff, Fade, Dissolve, Wipe, Fall }

/// <summary>The power-on intros and power-off effects you can choose between in SETTINGS.</summary>
sealed partial class RadioForm
{
    BootStyle bootStyle = BootStyle.Classic;
    ShutStyle shutStyle = ShutStyle.TvOff;
    bool shutdownPreview; // a power-off shown from settings: the radio comes back on afterwards

    bool bootAnim => bootStyle != BootStyle.Off;
    bool shutdownAnim => shutStyle != ShutStyle.Off;

    static readonly string[] BootNames = ["OFF", "CLASSIC", "SCANNER", "RAIN", "STATIC", "TERMINAL", "BOUNCE"];
    static readonly string[] ShutNames = ["OFF", "TV OFF", "FADE", "DISSOLVE", "WIPE", "FALL"];

    void DrawBootIntro(Graphics g, Inks ink)
    {
        switch (bootStyle)
        {
            case BootStyle.Scanner: BootScanner(g, ink); break;
            case BootStyle.Rain: BootRain(g, ink); break;
            case BootStyle.Static: BootStatic(g, ink); break;
            case BootStyle.Terminal: BootTerminal(g, ink); break;
            case BootStyle.Bounce: BootBounce(g, ink); break;
            default: BootClassic(g, ink); break;
        }
    }

    // ───────────────────────────── shared pieces ─────────────────────────────

    const float LogoPitch = 5.2f, LogoY = 58;

    string LogoText => DotFont.Normalize(Design.Brand);

    string BootTagline => DotFont.Normalize(Jdm ? "デジタル・メディア・レシーバー" : "DIGITAL MEDIA RECEIVER");

    /// <summary>The brand in big dots, plus the tagline and model underneath.</summary>
    void DrawLogo(Graphics g, Inks ink, Brush brush, float yOffset = 0, bool details = true)
    {
        string logo = LogoText;
        float lw = DotFont.Width(logo, LogoPitch);
        DotText(g, brush, logo, (GW - lw) / 2, LogoY + yOffset, LogoPitch);
        if (!details) return;
        float lineY = LogoY + 7 * LogoPitch + 12;
        g.FillRectangle(ink.Accent, 40, lineY, GW - 80, 3);
        string tag = BootTagline;
        DotText(g, ink.Main, tag, (GW - DotFont.Width(tag, 2.2f)) / 2, lineY + 22, 2.2f);
        string model = DotFont.Normalize(Design.Model);
        DotText(g, ink.Accent, model, (GW - DotFont.Width(model, 1.6f)) / 2, lineY + 52, 1.6f);
    }

    /// <summary>The dots that make up the logo, for effects that build it dot by dot.</summary>
    List<RectangleF> LogoDots()
    {
        var list = new List<RectangleF>();
        string logo = LogoText;
        DotFont.Emit(list, logo, (GW - DotFont.Width(logo, LogoPitch)) / 2, LogoY, LogoPitch, LogoPitch * 0.8f);
        return list;
    }

    static float Hash(int a, int b = 0)
    {
        uint h = (uint)(a * 374761393 + b * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    // ───────────────────────────── power-on intros ─────────────────────────────

    /// <summary>A Knight Rider light bar sweeps across twice, then the logo flashes on.</summary>
    void BootScanner(Graphics g, Inks ink)
    {
        double t = now;
        if (t < 0.2) return;
        if (t < 1.5)
        {
            double u = (t - 0.2) / 1.3 * 2; // two passes
            double pos = u % 1, dir = (int)u % 2 == 0 ? 1 : -1;
            float bar = (float)(dir > 0 ? pos : 1 - pos) * (GW - 20) + 10;
            for (float x = 10; x < GW - 8; x += 6)
            {
                float d = (bar - x) * (float)dir; // behind the bar = trail
                var target = d < 3 && d > -3 ? dots2 : d > 0 && d < 60 ? dots : null;
                if (target == null) continue;
                for (float y = 8; y < GH - 8; y += 6) target.Add(new RectangleF(x, y, 3.4f, 3.4f));
            }
            Fill(g, ink.Reflection, dots);
            Fill(g, ink.Hot, dots2);
            return;
        }
        DrawLogo(g, ink, t < 1.6 ? ink.Hot : ink.Main);
    }

    /// <summary>Digital rain falls down the display; the logo's dots stay lit where the rain passes.</summary>
    void BootRain(Graphics g, Inks ink)
    {
        double t = now;
        if (t < 0.15) return;
        const float step = 6;
        int cols = (int)((GW - 16) / step);
        float fade = (float)Math.Clamp((2.1 - t) / 0.5, 0, 1);
        float Head(int c) => (float)((t - 0.15 - Hash(c) * 0.5) * (240 + Hash(c, 7) * 200)) - 20;
        for (int c = 0; c < cols && fade > 0; c++)
        {
            float x = 10 + c * step, head = Head(c);
            for (int k = 0; k < 7; k++)
            {
                float y = head - k * step;
                if (y < 8 || y > GH - 10) continue;
                (k == 0 ? dots2 : dots).Add(new RectangleF(x, (float)Math.Floor(y / step) * step + 2, 3.4f, 3.4f));
            }
        }
        if (fade > 0)
        {
            using var trail = new SolidBrush(Color.FromArgb((int)(55 * fade), ink.Main.Color));
            Fill(g, trail, dots);
            using var heads = new SolidBrush(Color.FromArgb((int)(255 * fade), ink.Hot.Color));
            Fill(g, heads, dots2);
        }
        // Logo dots light up once the rain in their column has passed them.
        foreach (var d in LogoDots())
        {
            int c = Math.Clamp((int)((d.X - 10) / step), 0, cols - 1);
            if (Head(c) > d.Y) dots.Add(d);
        }
        Fill(g, ink.Main, dots);
        if (t > 1.7) DrawLogo(g, ink, ink.Main);
    }

    /// <summary>TV-style static that clears, leaving the logo behind.</summary>
    void BootStatic(Graphics g, Inks ink)
    {
        double t = now;
        if (t < 0.2) return;
        int frame = (int)(t * 30);
        float density = 1 - (float)Math.Clamp((t - 0.7) / 1.0, 0, 1);
        if (density > 0)
        {
            int i = 0;
            for (float x = 10; x < GW - 8; x += 6)
                for (float y = 8; y < GH - 8; y += 6, i++)
                {
                    float r = Hash(i, frame);
                    if (r < density * 0.55f) (r < density * 0.08f ? dots2 : dots).Add(new RectangleF(x, y, 3.4f, 3.4f));
                }
            Fill(g, ink.Reflection, dots);
            Fill(g, ink.Main, dots2);
        }
        // The logo condenses out of the noise.
        var logo = LogoDots();
        float show = (float)Math.Clamp((t - 0.6) / 1.1, 0, 1);
        for (int k = 0; k < logo.Count; k++)
            if (Hash(k, 99) < show) dots.Add(logo[k]);
        Fill(g, ink.Main, dots);
        if (t > 1.75) DrawLogo(g, ink, ink.Main);
    }

    /// <summary>An old computer boot screen types out its checks, then shows the logo.</summary>
    void BootTerminal(Graphics g, Inks ink)
    {
        double t = now;
        if (t < 0.15) return;
        if (t < 1.75)
        {
            string[] lines = Jdm
                ?
                [
                    DotFont.Normalize($"{Design.Brand} BIOS V1.0"),
                    DotFont.Normalize("メモリー点検 .... OK"),
                    DotFont.Normalize("音響DSP ...... OK"),
                    DotFont.Normalize("アンプ 4X50W . OK"),
                    DotFont.Normalize("表示管 ....... OK"),
                    DotFont.Normalize("システム起動中"),
                ]
                :
                [
                    DotFont.Normalize($"{Design.Brand} BIOS V1.0"),
                    "MEMORY TEST ...... OK",
                    "DSP ENGINE ....... OK",
                    "AMPLIFIER 4X50W .. OK",
                    "VFD DISPLAY ...... OK",
                    "LOADING SYSTEM",
                ];
            const float p = 2f, lineH = 22;
            double typed = (t - 0.15) / 0.0085; // characters typed so far
            for (int i = 0; i < lines.Length; i++)
            {
                int n = (int)Math.Clamp(typed, 0, lines[i].Length);
                if (n <= 0) break;
                DotText(g, i == 0 ? ink.Accent : ink.Main, lines[i][..n], 18, 18 + i * lineH, p);
                if (n < lines[i].Length)
                {
                    if ((int)(t * 8) % 2 == 0) g.FillRectangle(ink.Hot, 18 + DotFont.Width(lines[i][..n], p) + 3, 18 + i * lineH, 5 * p, 7 * p);
                    break;
                }
                typed -= lines[i].Length + 6; // a short pause after each line
                if (i == lines.Length - 1)
                {
                    string dotsText = new('.', (int)(t * 6) % 4);
                    DotText(g, ink.Main, dotsText, 18 + DotFont.Width(lines[i], p) + 4, 18 + i * lineH, p);
                }
            }
            return;
        }
        DrawLogo(g, ink, t < 1.82 ? ink.Hot : ink.Main);
    }

    /// <summary>Bars shoot up and bounce back down, then the logo drops in with a bounce.</summary>
    void BootBounce(Graphics g, Inks ink)
    {
        double t = now;
        if (t < 0.2) return;
        const int bars = 20;
        float slot = (GW - 40) / bars;
        for (int i = 0; i < bars; i++)
        {
            double s = t - 0.2 - i * 0.025;
            if (s < 0) continue;
            double h = Math.Max(0, Math.Sin(Math.Min(s * 5, Math.PI)) * (0.7 + 0.3 * Hash(i)) * Math.Exp(-s * 1.2));
            int segs = (int)(h * 26);
            for (int k = 0; k < segs; k++)
                (k == segs - 1 ? dots2 : dots).Add(new RectangleF(20 + i * slot + 2, GH - 20 - k * 8, slot - 5, 5));
        }
        Fill(g, ink.Main, dots);
        Fill(g, ink.Hot, dots2);
        if (t > 1.0)
        {
            // Drop from above with a damped bounce.
            double s = Math.Clamp((t - 1.0) / 0.9, 0, 1);
            double drop = Math.Abs(Math.Cos(s * Math.PI * 2.5)) * Math.Pow(1 - s, 2);
            DrawLogo(g, ink, ink.Main, -(float)(drop * 90), details: t > 1.6);
        }
    }

    // ───────────────────────────── power-off effects ─────────────────────────────

    /// <summary>
    /// Draws the display picture during a power-off (after GOOD BYE). <paramref name="c"/> runs from 0 to about 1.1 s.
    /// Returns false when the style is the classic TV collapse, which DrawGlass handles itself.
    /// </summary>
    bool DrawShutdownPicture(Graphics g, double c, RectangleF dest)
    {
        if (shutStyle == ShutStyle.TvOff) return false;
        float u = (float)Math.Clamp(c / 0.9, 0, 1);
        switch (shutStyle)
        {
            case ShutStyle.Fade:
            {
                using var attr = FadeAttr(1 - u);
                DrawPicture(g, dest, attr);
                break;
            }
            case ShutStyle.Dissolve:
            {
                // Cells of the picture switch off in a random order.
                using var keep = new GraphicsPath();
                const float cell = 7;
                int i = 0;
                for (float x = dest.X; x < dest.Right; x += cell)
                    for (float y = dest.Y; y < dest.Bottom; y += cell, i++)
                        if (Hash(i, 5) > u) keep.AddRectangle(new RectangleF(x, y, cell, cell));
                var st = g.Save();
                g.SetClip(keep, CombineMode.Intersect);
                DrawPicture(g, dest, null);
                g.Restore(st);
                break;
            }
            case ShutStyle.Wipe:
            {
                // A bright line sweeps across, switching the picture off behind it.
                float edge = dest.X + dest.Width * u;
                var st = g.Save();
                g.SetClip(new RectangleF(edge, dest.Y, dest.Right - edge, dest.Height), CombineMode.Intersect);
                DrawPicture(g, dest, null);
                g.Restore(st);
                if (u < 1)
                {
                    var hot = inks!.Hot.Color;
                    using (var halo = new SolidBrush(Color.FromArgb(70, hot))) g.FillRectangle(halo, edge - 6, dest.Y, 12, dest.Height);
                    using var line = new SolidBrush(Color.FromArgb(230, Color.White));
                    g.FillRectangle(line, edge - 1.5f, dest.Y, 3, dest.Height);
                }
                break;
            }
            case ShutStyle.Fall:
            {
                // The picture drops out of the bottom, speeding up as it goes, and dims.
                float drop = u * u * dest.Height * 1.1f;
                using var attr = FadeAttr(1 - u * 0.7f);
                DrawPicture(g, new RectangleF(dest.X, dest.Y + drop, dest.Width, dest.Height), attr);
                break;
            }
        }
        return true;
    }

    static System.Drawing.Imaging.ImageAttributes FadeAttr(float alpha)
    {
        var a = new System.Drawing.Imaging.ImageAttributes();
        a.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = Math.Clamp(alpha, 0, 1) });
        return a;
    }

    /// <summary>The display image (with its glow on VFD screens), drawn into a rectangle.</summary>
    void DrawPicture(Graphics g, RectangleF dest, System.Drawing.Imaging.ImageAttributes? fade)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        PointF[] corners = [dest.Location, new(dest.Right, dest.Top), new(dest.Left, dest.Bottom)];
        var full = new RectangleF(0, 0, disp!.Width, disp.Height);
        if (Design.Screen != ScreenKind.Modern && fade == null)
        {
            g.DrawImage(glowAll!, dest, Inset(glowAll!), GraphicsUnit.Pixel);
        }
        if (fade != null) g.DrawImage(disp, corners, full, GraphicsUnit.Pixel, fade);
        else g.DrawImage(disp, dest);
    }
}
