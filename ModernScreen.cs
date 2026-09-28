using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace RetroRadio;

/// <summary>
/// The high-resolution colour screen used by the modern radio models: album cover, tags, a smooth
/// analyzer and modern menus, laid out for widescreen, strip (single-DIN) or portrait screens.
/// Everything is drawn in display units (GW × 262) like the dot-matrix screen, so clicks line up.
/// </summary>
sealed partial class RadioForm
{
    enum ModernLayout { Wide, Strip, Portrait }

    // Menu rows on the modern screen (shared with click handling).
    const float ModernRowTop = 44, ModernRowH = 26;

    static readonly Dictionary<(float, FontStyle, string), Font> FontCache = new();

    static Font F(float size, FontStyle style = FontStyle.Regular, string family = "Segoe UI")
    {
        var key = (size, style, family);
        if (!FontCache.TryGetValue(key, out var f)) FontCache[key] = f = new Font(family, size, style, GraphicsUnit.Pixel);
        return f;
    }

    ModernLayout ScreenLayout => (GW / GH) switch { > 2.6f => ModernLayout.Strip, < 1.1f => ModernLayout.Portrait, _ => ModernLayout.Wide };

    /// <summary>Text scale: portrait screens are narrow in display units, so everything shrinks a bit.</summary>
    float TS => ScreenLayout switch { ModernLayout.Portrait => 0.78f, ModernLayout.Strip => 1.45f, _ => 1f };

    TrackMeta? CurrentMeta => current >= 0 && current < tracks.Count ? TrackMeta.For(tracks[current]) : null;

    Color ModernAccent => CurrentMeta?.Accent is { IsEmpty: false } a ? a : Palettes[paletteIdx].Main;

    static readonly StringFormat Ellipsis = new() { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };

    static void MText(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment align = StringAlignment.Near, bool wrap = false)
    {
        using var b = new SolidBrush(c);
        using var sf = new StringFormat(Ellipsis) { Alignment = align };
        if (wrap) sf.FormatFlags = 0;
        sf.LineAlignment = wrap ? StringAlignment.Near : StringAlignment.Center;
        g.DrawString(s, f, b, r, sf);
    }

    static GraphicsPath Pill(RectangleF r, float rad) => FaceplateRenderer.RoundRect(r, Math.Min(rad, Math.Min(r.Width, r.Height) / 2));

    // ───────────────────────────── frame ─────────────────────────────

    void DrawModern(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        var meta = CurrentMeta;
        var accent = ModernAccent;
        var all = new RectangleF(0, 0, GW, GH);

        // Background: the cover, hugely blurred and dimmed — or a soft accent gradient.
        using (var bg = new LinearGradientBrush(all, Color.FromArgb(16, 18, 24), Color.FromArgb(6, 6, 9), 90f)) g.FillRectangle(bg, all);
        if (meta?.Backdrop != null)
        {
            using var dim = new ImageAttributes();
            dim.SetColorMatrix(new ColorMatrix { Matrix33 = 0.55f });
            dim.SetWrapMode(WrapMode.TileFlipXY);
            float side = Math.Max(GW, GH) * 1.3f;
            g.DrawImage(meta.Backdrop, Rectangle.Round(new RectangleF((GW - side) / 2, (GH - side) / 2, side, side)), 0, 0, meta.Backdrop.Width, meta.Backdrop.Height, GraphicsUnit.Pixel, dim);
        }
        else
        {
            using var glow = new GraphicsPath();
            glow.AddEllipse(-GW * 0.2f, -GH * 0.6f, GW * 0.9f, GH * 1.4f);
            using var gb = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(70, accent), SurroundColors = [Color.FromArgb(0, accent)] };
            g.FillPath(gb, glow);
        }
        using (var shade = new LinearGradientBrush(all, Color.FromArgb(90, 0, 0, 0), Color.FromArgb(190, 0, 0, 0), 90f)) g.FillRectangle(shade, all);

        if (bootAnim && now < BootIntro)
        {
            ModernBoot(g, accent);
            return;
        }

        TopBar(g, accent);
        if (eqMode) ModernEq(g, accent);
        else if (scMode) ModernOnline(g, accent);
        else if (settingsMode) ModernMenu(g, accent, Lang.T("SETTINGS"), settings.Count, ref setCursor, ref setTop,
            i => (Lang.T(settings[i].Name), Lang.Value(settings[i].Value())));
        else if (listMode)
        {
            var view = ListView;
            string head = searching ? $"{Lang.T("SEARCH")}: {listFilter}_" : Lang.F("PLAYLIST  ·  {0} TRACKS", tracks.Count);
            ModernMenu(g, accent, head, view.Count, ref listCursor, ref listTop,
                j => view[j] is var i && true ? ((i == current ? "♪  " : "") + (TrackMeta.For(tracks[i]) is { } m && m.Title.Length > 0 ? (m.Artist.Length > 0 ? $"{m.Title}  —  {m.Artist}" : m.Title) : TitleOf(tracks[i])), $"{i + 1}") : ("", ""));
        }
        else if (ShowIdleClock) ModernClock(g, accent);
        else NowPlaying(g, meta, accent);

        if (now < volOverlayUntil) ModernPopup(g, accent);
        if (now < flashUntil) Toast(g, flashText, accent);
    }

    void TopBar(Graphics g, Color accent)
    {
        float s = TS;
        var font = F(10 * s, FontStyle.Bold);
        MText(g, DateTime.Now.ToString("HH:mm"), F(11 * s, FontStyle.Bold), Color.White, new RectangleF(12, 6, 80, 16));

        // Status icons on the right: source, shuffle, repeat, mute.
        var icons = new List<(string Text, bool On)>
        {
            (tracks.Count == 0 ? Lang.T("NO MEDIA") : SourceLabel() switch { "SC" => "SOUNDCLOUD", "SPOT" => "SPOTIFY", var src => src }, true),
            ("SHUF", shuffle),
            (repeat == RepeatMode.One ? "RPT 1" : "RPT", repeat != RepeatMode.Off),
        };
        if (engine.Muted) icons.Add(("MUTE", true));
        if (Math.Abs(engine.Speed - 1) > 0.001f) icons.Add((SpeedText().ToLowerInvariant(), true));
        float x = GW - 12;
        for (int i = icons.Count - 1; i >= 0; i--)
        {
            var (t, on) = icons[i];
            var size = g.MeasureString(t, font);
            var r = new RectangleF(x - size.Width - 10, 6, size.Width + 10, 15);
            if (on)
            {
                using var pill = Pill(r, 7);
                using var pb = new SolidBrush(Color.FromArgb(i == 0 ? 60 : 110, i == 0 ? Color.White : accent));
                g.FillPath(pb, pill);
            }
            MText(g, t, font, on ? Color.White : Color.FromArgb(90, 255, 255, 255), r, StringAlignment.Center);
            x = r.X - 5;
            if (ScreenLayout == ModernLayout.Portrait && i <= icons.Count - 3) break;
        }
    }

    // ───────────────────────────── now playing ─────────────────────────────

    void NowPlaying(Graphics g, TrackMeta? meta, Color accent)
    {
        string name = meta is { Title.Length: > 0 } ? meta.Title : tracks.Count == 0 ? Lang.T("No music yet") : title;
        string artist = meta?.Artist ?? "";
        string album = meta == null ? "" : meta.Year > 0 && meta.Album.Length > 0 ? $"{meta.Album}  ·  {meta.Year}" : meta.Album;
        if (CurrentLyric() is { } lyric && (lyric.Line.Length > 0 || lyric.Next.Length > 0))
        {
            // Lyrics take the artist and album lines: the line being sung, then the next one.
            artist = lyric.Line.Length > 0 ? lyric.Line : "♪";
            album = lyric.Next;
        }
        if (tracks.Count == 0)
        {
            artist = Lang.T("Drop music files or folders here");
            album = Lang.T("or press OPEN / FOLDER");
        }
        string info = tracks.Count == 0 ? Lang.T("READY") : Lang.F("TRACK {0} OF {1}", Math.Max(current, 0) + 1, tracks.Count);
        float s = TS;

        RectangleF art, textArea, spec;
        switch (ScreenLayout)
        {
            case ModernLayout.Strip:
            {
                float side = ProgressY - 34;
                art = new RectangleF(14, 28, side, side);
                textArea = new RectangleF(art.Right + 22, 34, GW * 0.48f, side - 8);
                spec = new RectangleF(textArea.Right + 14, 44, GW - textArea.Right - 30, ProgressY - 60);
                break;
            }
            case ModernLayout.Portrait:
            {
                float side = Math.Min(GW - 36, GH * 0.47f);
                art = new RectangleF((GW - side) / 2, 26, side, side);
                textArea = new RectangleF(12, art.Bottom + 6, GW - 24, 58);
                spec = new RectangleF(16, textArea.Bottom + 2, GW - 32, ProgressY - textArea.Bottom - 8);
                break;
            }
            default:
            {
                float side = GH - 30 - 50;
                art = new RectangleF(16, 30, side, side);
                textArea = new RectangleF(art.Right + 20, 34, GW - art.Right - 36, 110);
                spec = new RectangleF(textArea.X, textArea.Bottom + 8, textArea.Width, art.Bottom - textArea.Bottom - 8);
                break;
            }
        }

        DrawCover(g, meta, art, accent);

        // Text block.
        var t = textArea;
        bool portrait = ScreenLayout == ModernLayout.Portrait;
        var align = portrait ? StringAlignment.Center : StringAlignment.Near;
        if (!portrait)
        {
            MText(g, info, F(9 * s, FontStyle.Bold), accent, new RectangleF(t.X, t.Y, t.Width, 14), align);
            t.Y += 16;
        }
        float titleSize = 24 * s;
        var titleFont = F(titleSize, FontStyle.Bold, "Segoe UI Semibold");
        if (!portrait && g.MeasureString(name, titleFont).Width > t.Width)
        {
            // Long titles get two lines, a size smaller.
            titleFont = F(titleSize * 0.8f, FontStyle.Bold, "Segoe UI Semibold");
            MText(g, name, titleFont, Color.White, new RectangleF(t.X, t.Y, t.Width, titleFont.Size * 2.5f), align, wrap: true);
            t.Y += titleFont.Size * 2.5f;
        }
        else
        {
            MText(g, name, titleFont, Color.White, new RectangleF(t.X, t.Y, t.Width, titleSize * 1.35f), align);
            t.Y += titleSize * 1.35f;
        }
        MText(g, artist, F(15 * s), Color.FromArgb(225, 255, 255, 255), new RectangleF(t.X, t.Y, t.Width, 20 * s), align);
        t.Y += 19 * s;
        MText(g, album, F(11.5f * s), Color.FromArgb(150, 255, 255, 255), new RectangleF(t.X, t.Y, t.Width, 16 * s), align);

        if (spec.Height > 12) ModernSpectrum(g, spec, accent);
        ProgressBar(g, accent);

        // What's next.
        if (ScreenLayout != ModernLayout.Portrait && tracks.Count > 1 && !shuffle)
        {
            int next = (current + 1) % tracks.Count;
            var m = TrackMeta.For(tracks[next]);
            string nx = m is { Title.Length: > 0 } ? (m.Artist.Length > 0 ? $"{m.Title} — {m.Artist}" : m.Title) : TitleOf(tracks[next]);
            MText(g, Lang.T("UP NEXT") + "  " + nx, F(9.5f), Color.FromArgb(120, 255, 255, 255), new RectangleF(16, GH - 20, GW - 32, 14), StringAlignment.Center);
        }
    }

    void DrawCover(Graphics g, TrackMeta? meta, RectangleF r, Color accent)
    {
        // Soft drop shadow.
        for (int i = 6; i >= 1; i--)
        {
            using var sp = Pill(RectangleF.Inflate(new RectangleF(r.X, r.Y + 3, r.Width, r.Height), i * 1.5f, i * 1.5f), 10 + i);
            using var sb = new SolidBrush(Color.FromArgb(14, 0, 0, 0));
            g.FillPath(sb, sp);
        }
        using var clip = Pill(r, 8);
        var saved = g.Save();
        g.SetClip(clip);
        if (meta?.Cover != null)
        {
            g.DrawImage(meta.Cover, r);
        }
        else
        {
            // No cover: a gradient tile with a note, tinted by the theme.
            using (var gb = new LinearGradientBrush(r, Color.FromArgb(255, Lerp(accent, Color.Black, 0.35f)), Color.FromArgb(255, Lerp(accent, Color.Black, 0.8f)), 45f))
                g.FillRectangle(gb, r);
            var nf = F(r.Height * 0.42f, FontStyle.Regular, "Segoe UI Symbol"); // cached: not disposed here
            MText(g, "♫", nf, Color.FromArgb(150, 255, 255, 255), r, StringAlignment.Center);
        }
        // Glassy highlight.
        using (var hl = new LinearGradientBrush(r, Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 60f))
            g.FillRectangle(hl, r.X, r.Y, r.Width, r.Height * 0.5f);
        g.Restore(saved);
        using var edge = new Pen(Color.FromArgb(60, 255, 255, 255), 0.8f);
        g.DrawPath(edge, clip);
    }

    static Color Lerp(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>A smooth, mirrored analyzer of rounded bars in the accent colour.</summary>
    void ModernSpectrum(Graphics g, RectangleF r, Color accent)
    {
        int n = Math.Clamp((int)(r.Width / 7), 12, 64);
        float slot = r.Width / n, w = Math.Max(1.5f, slot * 0.62f);
        float baseY = r.Y + r.Height * 0.72f;
        using var up = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, baseY - r.Y + 1), Color.White, accent, 90f);
        using var down = new SolidBrush(Color.FromArgb(55, accent));
        for (int i = 0; i < n; i++)
        {
            float lv = SampleLevel(i / (float)(n - 1));
            float h = Math.Max(1.5f, lv * (baseY - r.Y));
            float x = r.X + i * slot + (slot - w) / 2;
            using var bar = Pill(new RectangleF(x, baseY - h, w, h), w / 2);
            g.FillPath(up, bar);
            float rh = Math.Max(1, h * 0.35f);
            using var refl = Pill(new RectangleF(x, baseY + 2, w, Math.Min(rh, r.Bottom - baseY - 2)), w / 2);
            g.FillPath(down, refl);
        }
    }

    /// <summary>Spectrum level at 0..1 across the bands (whatever band count the current visualizer uses).</summary>
    float SampleLevel(float t)
    {
        if (levels.Length == 0) return 0;
        float f = t * (levels.Length - 1);
        int i = (int)f;
        float a = levels[Math.Clamp(i, 0, levels.Length - 1)], b = levels[Math.Clamp(i + 1, 0, levels.Length - 1)];
        return Math.Clamp(a + (b - a) * (f - i), 0, 1);
    }

    void ProgressBar(Graphics g, Color accent)
    {
        double dur = player.Duration.TotalSeconds, pos = player.Position.TotalSeconds;
        float frac = dur > 0 ? (float)Math.Clamp(pos / dur, 0, 1) : 0;
        var track = new RectangleF(16, ProgressY + 1, GW - 32, 4);
        using (var tp = Pill(track, 2))
        using (var tb = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
            g.FillPath(tb, tp);
        if (frac > 0)
        {
            using var fp = Pill(new RectangleF(track.X, track.Y, Math.Max(4, track.Width * frac), track.Height), 2);
            using var fb = new SolidBrush(accent);
            g.FillPath(fb, fp);
        }
        float kx = track.X + track.Width * frac;
        g.FillEllipse(Brushes.White, kx - 4.5f, track.Y - 2.5f, 9, 9);

        var times = F(10 * TS, FontStyle.Bold);
        string el = FormatTime(pos), rem = dur > 0 ? "-" + FormatTime(Math.Max(0, dur - pos)) : "--:--";
        MText(g, el, times, Color.FromArgb(200, 255, 255, 255), new RectangleF(16, ProgressY + 8, 80, 14));
        MText(g, rem, times, Color.FromArgb(200, 255, 255, 255), new RectangleF(GW - 96, ProgressY + 8, 80, 14), StringAlignment.Far);

        // Transport state in the middle.
        string state = stopped || !player.IsLoaded ? "■  " + Lang.T("STOPPED") : player.IsPlaying ? "▶  " + Lang.T("PLAYING") : "❚❚  " + Lang.T("PAUSED");
        MText(g, state, F(9 * TS, FontStyle.Bold), Color.FromArgb(160, 255, 255, 255), new RectangleF(GW / 2 - 70, ProgressY + 8, 140, 14), StringAlignment.Center);
    }

    static string FormatTime(double s) => s >= 3600 ? TimeSpan.FromSeconds(s).ToString(@"h\:mm\:ss") : TimeSpan.FromSeconds(s).ToString(@"m\:ss");

    // ───────────────────────────── menus ─────────────────────────────

    int ModernRows => Math.Max(1, (int)((ProgressY - ModernRowTop - 4) / ModernRowH));

    /// <summary>Which menu row a click at display height <paramref name="y"/> hits (or -1).</summary>
    int MenuRowAt(float y)
    {
        if (Design.Screen == ScreenKind.Modern)
            return y >= ModernRowTop && y < ModernRowTop + ModernRows * ModernRowH ? (int)((y - ModernRowTop) / ModernRowH) : -1;
        return y >= 62 && y < 62 + ListRows * 17 ? (int)((y - 62) / 17) : -1;
    }

    void ModernMenu(Graphics g, Color accent, string title, int count, ref int cursor, ref int top, Func<int, (string Name, string Value)> row)
    {
        MText(g, title, F(10, FontStyle.Bold), accent, new RectangleF(14, 24, GW - 28, 16));
        int rows = ModernRows;
        cursor = Math.Clamp(cursor, 0, Math.Max(0, count - 1));
        if (cursor < top) top = cursor;
        if (cursor >= top + rows) top = cursor - rows + 1;
        top = Math.Clamp(top, 0, Math.Max(0, count - rows));

        var nameFont = F(12.5f * TS);
        var valueFont = F(11.5f * TS, FontStyle.Bold);
        for (int r = 0; r < rows && top + r < count; r++)
        {
            int i = top + r;
            var (name, value) = row(i);
            var rect = new RectangleF(10, ModernRowTop + r * ModernRowH, GW - 26, ModernRowH - 3);
            bool sel = i == cursor;
            if (sel)
            {
                using var p = Pill(rect, 7);
                using var b = new SolidBrush(Color.FromArgb(95, accent));
                g.FillPath(b, p);
                using var bar = new SolidBrush(accent);
                g.FillRectangle(bar, rect.X + 1, rect.Y + 5, 3, rect.Height - 10);
            }
            else if (r % 2 == 1)
            {
                using var zebra = new SolidBrush(Color.FromArgb(14, 255, 255, 255));
                g.FillRectangle(zebra, rect);
            }
            float valueW = value.Length == 0 ? 0 : Math.Min(rect.Width * 0.45f, g.MeasureString(value, valueFont).Width + 8);
            MText(g, name, nameFont, Color.White, new RectangleF(rect.X + 12, rect.Y, rect.Width - valueW - 20, rect.Height));
            if (value.Length > 0)
                MText(g, sel ? $"‹ {value} ›" : value, valueFont, sel ? Color.White : accent, new RectangleF(rect.Right - valueW - 24, rect.Y, valueW + 18, rect.Height), StringAlignment.Far);
        }
        if (count > rows)
        {
            float trackH = rows * ModernRowH, h = Math.Max(10, trackH * rows / count);
            float y = ModernRowTop + (trackH - h) * top / Math.Max(1, count - rows);
            using var tb = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
            g.FillRectangle(tb, GW - 12, ModernRowTop, 3, trackH);
            using var hb = new SolidBrush(accent);
            g.FillRectangle(hb, GW - 12, y, 3, h);
        }
        if (count == 0) MText(g, Lang.T("Empty"), nameFont, Color.FromArgb(140, 255, 255, 255), new RectangleF(14, ModernRowTop, GW - 28, ModernRowH));
    }

    void ModernOnline(Graphics g, Color accent)
    {
        if (scTyping)
        {
            MText(g, $"SEARCH {scSearchKind} ON {svc.Name}", F(10, FontStyle.Bold), accent, new RectangleF(14, 24, GW - 28, 16));
            var box = new RectangleF(14, 60, GW - 28, 40);
            using (var p = Pill(box, 10))
            {
                using var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
                g.FillPath(b, p);
                using var pen = new Pen(accent, 1.5f);
                g.DrawPath(pen, p);
            }
            bool caret = now % 1 < 0.55;
            MText(g, "🔍  " + scInput + (caret ? "|" : " "), F(17 * TS), Color.White, new RectangleF(box.X + 14, box.Y, box.Width - 28, box.Height));
            MText(g, "Enter = search      Esc = back", F(11), Color.FromArgb(150, 255, 255, 255), new RectangleF(14, 110, GW - 28, 18));
            return;
        }
        string title = scBusy ? Lang.T("LOADING") + new string('.', (int)(now * 3) % 4) : scTitle;
        int top = scTop;
        ModernMenu(g, accent, title, scRows.Count, ref scCursor, ref top, i => (Lang.T(scRows[i].Text), Lang.Value(scRows[i].Right)));
        scTop = top;
    }

    // ───────────────────────────── popups ─────────────────────────────

    void ModernPopup(Graphics g, Color accent)
    {
        bool speed = speedOverlay;
        float w = Math.Min(GW - 40, 360 * TS), h = 46;
        var r = new RectangleF((GW - w) / 2, GH - h - 44, w, h);
        using (var p = Pill(r, 14))
        {
            using var b = new SolidBrush(Color.FromArgb(215, 20, 22, 28));
            g.FillPath(b, p);
            using var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1);
            g.DrawPath(pen, p);
        }
        string label = Lang.T(speed ? "SPEED" : engine.Muted ? "MUTED" : "VOLUME");
        string value = speed ? SpeedText().ToLowerInvariant() : ((int)Math.Round(engine.Volume * 40)).ToString();
        float frac = speed ? (MathF.Log2(engine.Speed) + 1) / 2 : engine.Muted ? 0 : engine.Volume;
        MText(g, label, F(10, FontStyle.Bold), Color.FromArgb(180, 255, 255, 255), new RectangleF(r.X + 16, r.Y + 5, 100, 14));
        MText(g, value, F(16, FontStyle.Bold), Color.White, new RectangleF(r.Right - 76, r.Y, 60, r.Height), StringAlignment.Far);
        var bar = new RectangleF(r.X + 16, r.Y + 27, r.Width - 104, 6);
        using (var bp = Pill(bar, 3))
        using (var bb = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
            g.FillPath(bb, bp);
        if (speed)
        {
            // Speed grows out from the middle (1x).
            float mid = bar.X + bar.Width / 2, x = bar.X + bar.Width * frac;
            using var fb = new SolidBrush(accent);
            g.FillRectangle(fb, Math.Min(mid, x), bar.Y, Math.Abs(x - mid), bar.Height);
            g.FillRectangle(Brushes.White, mid - 1, bar.Y - 2, 2, bar.Height + 4);
        }
        else if (frac > 0)
        {
            using var fp = Pill(new RectangleF(bar.X, bar.Y, Math.Max(6, bar.Width * frac), bar.Height), 3);
            using var fb = new SolidBrush(accent);
            g.FillPath(fb, fp);
        }
    }

    void Toast(Graphics g, string text, Color accent)
    {
        var f = F(12 * TS, FontStyle.Bold);
        var size = g.MeasureString(text, f);
        float w = Math.Min(GW - 30, size.Width + 32);
        var r = new RectangleF((GW - w) / 2, 26, w, 26);
        using var p = Pill(r, 13);
        using (var b = new SolidBrush(Color.FromArgb(225, 24, 26, 32))) g.FillPath(b, p);
        using (var pen = new Pen(Color.FromArgb(160, accent), 1.2f)) g.DrawPath(pen, p);
        MText(g, text, f, Color.White, r, StringAlignment.Center);
    }

    /// <summary>The tone controls as three sliders, on the modern screens.</summary>
    void ModernEq(Graphics g, Color accent)
    {
        float s = TS;
        MText(g, $"{Lang.T("EQUALIZER")}  ·  {EqPresets[eqPreset].Name}", F(12 * s, FontStyle.Bold), accent, new RectangleF(16, 30, GW - 32, 20));
        string[] names = [Lang.T("BASS"), Lang.T("MID"), Lang.T("TREBLE")];
        float[] vals = [eqBass, eqMid, eqTreble];
        float top = 62, bottom = GH - 46, colW = (GW - 140) / 3;
        for (int b = 0; b < 3; b++)
        {
            float cx = 24 + b * colW + colW / 2;
            var track = new RectangleF(cx - 3, top, 6, bottom - top);
            using (var tp = Pill(track, 3))
            using (var tb = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillPath(tb, tp);
            float mid = (top + bottom) / 2, y = mid - vals[b] / 12 * (bottom - top) / 2;
            var fill = RectangleF.FromLTRB(cx - 3, Math.Min(y, mid), cx + 3, Math.Max(y, mid));
            using (var fb = new SolidBrush(accent)) g.FillRectangle(fb, fill);
            using (var kb = new SolidBrush(eqBand == b ? Color.White : Color.FromArgb(220, 230, 235, 240))) g.FillEllipse(kb, cx - 9, y - 9, 18, 18);
            MText(g, names[b], F(11 * s, eqBand == b ? FontStyle.Bold : FontStyle.Regular), Color.White, new RectangleF(cx - colW / 2, bottom + 8, colW, 16), StringAlignment.Center);
            MText(g, (vals[b] > 0 ? "+" : "") + vals[b].ToString("0") + " dB", F(10 * s), Color.FromArgb(170, 255, 255, 255), new RectangleF(cx - colW / 2, top - 20, colW, 16), StringAlignment.Center);
        }
        var box = new RectangleF(GW - 100, (top + bottom) / 2 - 16, 80, 32);
        using (var bp = Pill(box, 16))
        using (var bb = new SolidBrush(loudness ? accent : Color.FromArgb(40, 255, 255, 255))) g.FillPath(bb, bp);
        MText(g, Lang.T("LOUD"), F(11 * s, FontStyle.Bold), Color.White, box, StringAlignment.Center);
    }

    /// <summary>A big clock while nothing is playing.</summary>
    void ModernClock(Graphics g, Color accent)
    {
        var t = DateTime.Now;
        MText(g, t.ToString("HH:mm"), F(Math.Min(96, GW / 5f) * TS, FontStyle.Regular, "Segoe UI Light"), Color.White, new RectangleF(0, GH * 0.2f, GW, GH * 0.4f), StringAlignment.Center);
        MText(g, t.ToString(Jdm ? "yyyy年M月d日 dddd" : "dddd d MMMM", new System.Globalization.CultureInfo(Jdm ? "ja-JP" : "en-GB")), F(15 * TS), Color.FromArgb(200, 255, 255, 255),
            new RectangleF(0, GH * 0.62f, GW, 24), StringAlignment.Center);
    }

    void ModernBoot(Graphics g, Color accent)
    {
        double t = now;
        float a = (float)Math.Clamp((t - 0.3) / 0.6, 0, 1);
        var brand = F(Math.Min(46, GW / 9f) * TS, FontStyle.Bold, "Segoe UI Semibold");
        MText(g, Design.Brand, brand, Color.FromArgb((int)(255 * a), Color.White), new RectangleF(0, GH * 0.28f, GW, GH * 0.24f), StringAlignment.Center);
        MText(g, Design.Model, F(12 * TS), Color.FromArgb((int)(170 * a), Color.White), new RectangleF(0, GH * 0.52f, GW, 18), StringAlignment.Center);
        // A light sweep and a loading line.
        float sweep = (float)Math.Clamp((t - 0.6) / 1.4, 0, 1);
        var line = new RectangleF(GW * 0.3f, GH * 0.68f, GW * 0.4f, 3);
        using (var lp = Pill(line, 1.5f))
        using (var lb = new SolidBrush(Color.FromArgb((int)(60 * a), 255, 255, 255)))
            g.FillPath(lb, lp);
        using (var fp = Pill(new RectangleF(line.X, line.Y, Math.Max(3, line.Width * sweep), line.Height), 1.5f))
        using (var fb = new SolidBrush(Color.FromArgb((int)(255 * a), accent)))
            g.FillPath(fb, fp);
    }
}
