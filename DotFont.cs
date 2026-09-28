using System.Drawing.Text;
using System.Globalization;
using System.Text;

namespace RetroRadio;

/// <summary>
/// A classic 5x7 dot-matrix font, the kind used on VFD car-stereo displays: Latin, Cyrillic and Greek
/// capitals are hand-drawn 5x7 glyphs; any other script (Japanese, Chinese, Korean, Arabic, …) is drawn
/// from a system font as "fine dots" — a half-pitch 14-row grid on the same line, like full-dot displays.
/// </summary>
static class DotFont
{
    public const char Play = '▶';
    public const char Pause = '‖';
    public const char Stop = '■';
    public const char BarLeft = '▌';
    public const char BarRight = '▐';
    public const char Note = '♪';

    static readonly Dictionary<char, byte[]> Glyphs = new();

    static DotFont()
    {
        Add(' ', "00000 00000 00000 00000 00000 00000 00000");
        Add('0', "01110 10001 10011 10101 11001 10001 01110");
        Add('1', "00100 01100 00100 00100 00100 00100 01110");
        Add('2', "01110 10001 00001 00010 00100 01000 11111");
        Add('3', "11111 00010 00100 00010 00001 10001 01110");
        Add('4', "00010 00110 01010 10010 11111 00010 00010");
        Add('5', "11111 10000 11110 00001 00001 10001 01110");
        Add('6', "00110 01000 10000 11110 10001 10001 01110");
        Add('7', "11111 00001 00010 00100 01000 01000 01000");
        Add('8', "01110 10001 10001 01110 10001 10001 01110");
        Add('9', "01110 10001 10001 01111 00001 00010 01100");
        Add('A', "01110 10001 10001 11111 10001 10001 10001");
        Add('B', "11110 10001 10001 11110 10001 10001 11110");
        Add('C', "01110 10001 10000 10000 10000 10001 01110");
        Add('D', "11100 10010 10001 10001 10001 10010 11100");
        Add('E', "11111 10000 10000 11110 10000 10000 11111");
        Add('F', "11111 10000 10000 11110 10000 10000 10000");
        Add('G', "01110 10001 10000 10111 10001 10001 01111");
        Add('H', "10001 10001 10001 11111 10001 10001 10001");
        Add('I', "01110 00100 00100 00100 00100 00100 01110");
        Add('J', "00111 00010 00010 00010 00010 10010 01100");
        Add('K', "10001 10010 10100 11000 10100 10010 10001");
        Add('L', "10000 10000 10000 10000 10000 10000 11111");
        Add('M', "10001 11011 10101 10101 10001 10001 10001");
        Add('N', "10001 10001 11001 10101 10011 10001 10001");
        Add('O', "01110 10001 10001 10001 10001 10001 01110");
        Add('P', "11110 10001 10001 11110 10000 10000 10000");
        Add('Q', "01110 10001 10001 10001 10101 10010 01101");
        Add('R', "11110 10001 10001 11110 10100 10010 10001");
        Add('S', "01111 10000 10000 01110 00001 00001 11110");
        Add('T', "11111 00100 00100 00100 00100 00100 00100");
        Add('U', "10001 10001 10001 10001 10001 10001 01110");
        Add('V', "10001 10001 10001 10001 10001 01010 00100");
        Add('W', "10001 10001 10001 10101 10101 10101 01010");
        Add('X', "10001 10001 01010 00100 01010 10001 10001");
        Add('Y', "10001 10001 10001 01010 00100 00100 00100");
        Add('Z', "11111 00001 00010 00100 01000 10000 11111");
        Add('\'', "01100 00100 01000 00000 00000 00000 00000");
        Add('"', "01010 01010 01010 00000 00000 00000 00000");
        Add('.', "00000 00000 00000 00000 00000 01100 01100");
        Add(',', "00000 00000 00000 00000 01100 00100 01000");
        Add(':', "00000 01100 01100 00000 01100 01100 00000");
        Add(';', "00000 01100 01100 00000 01100 00100 01000");
        Add('-', "00000 00000 00000 11111 00000 00000 00000");
        Add('_', "00000 00000 00000 00000 00000 00000 11111");
        Add('+', "00000 00100 00100 11111 00100 00100 00000");
        Add('=', "00000 00000 11111 00000 11111 00000 00000");
        Add('/', "00000 00001 00010 00100 01000 10000 00000");
        Add('\\', "00000 10000 01000 00100 00010 00001 00000");
        Add('(', "00010 00100 01000 01000 01000 00100 00010");
        Add(')', "01000 00100 00010 00010 00010 00100 01000");
        Add('[', "01110 01000 01000 01000 01000 01000 01110");
        Add(']', "01110 00010 00010 00010 00010 00010 01110");
        Add('<', "00010 00100 01000 10000 01000 00100 00010");
        Add('>', "01000 00100 00010 00001 00010 00100 01000");
        Add('!', "00100 00100 00100 00100 00100 00000 00100");
        Add('?', "01110 10001 00001 00010 00100 00000 00100");
        Add('&', "01100 10010 10100 01000 10101 10010 01101");
        Add('#', "01010 01010 11111 01010 11111 01010 01010");
        Add('%', "11000 11001 00010 00100 01000 10011 00011");
        Add('*', "00000 10101 01110 11111 01110 10101 00000");
        Add('@', "01110 10001 10111 10101 10111 10000 01110");
        Add('$', "00100 01111 10100 01110 00101 11110 00100");
        Add('|', "00100 00100 00100 00100 00100 00100 00100");
        Add('~', "00000 00000 01000 10101 00010 00000 00000");
        Add('·', "00000 00000 00000 00100 00000 00000 00000"); // middle dot
        Add(Play, "10000 11000 11100 11110 11100 11000 10000");
        Add(Pause, "00000 11011 11011 11011 11011 11011 00000");
        Add(Stop, "00000 11111 11111 11111 11111 11111 00000");
        Add(BarLeft, "11000 11000 11000 11000 11000 11000 11000");
        Add(BarRight, "00011 00011 00011 00011 00011 00011 00011");
        Add(Note, "00111 00101 00100 00100 01100 11100 11000");

        // Cyrillic capitals (the ones that look like Latin letters are aliased below).
        Add('Б', "11111 10000 10000 11110 10001 10001 11110");
        Add('Г', "11111 10000 10000 10000 10000 10000 10000");
        Add('Ґ', "00001 11111 10000 10000 10000 10000 10000");
        Add('Д', "00110 01010 01010 01010 01010 11111 10001");
        Add('Ж', "10101 10101 10101 01110 10101 10101 10101");
        Add('З', "01110 10001 00001 00110 00001 10001 01110");
        Add('И', "10001 10001 10011 10101 11001 10001 10001");
        Add('Й', "01010 00100 10001 10011 10101 11001 10001");
        Add('Л', "00111 01001 01001 01001 01001 01001 10001");
        Add('П', "11111 10001 10001 10001 10001 10001 10001");
        Add('У', "10001 10001 10001 01111 00001 10001 01110");
        Add('Ф', "00100 01110 10101 10101 10101 01110 00100");
        Add('Ц', "10010 10010 10010 10010 10010 11111 00001");
        Add('Ч', "10001 10001 10001 01111 00001 00001 00001");
        Add('Ш', "10101 10101 10101 10101 10101 10101 11111");
        Add('Щ', "10101 10101 10101 10101 10101 11111 00001");
        Add('Ъ', "11000 01000 01000 01110 01001 01001 01110");
        Add('Ы', "10001 10001 10001 11101 10011 10011 11101");
        Add('Ь', "10000 10000 10000 11110 10001 10001 11110");
        Add('Э', "01110 10001 00001 00111 00001 10001 01110");
        Add('Ю', "10010 10101 10101 11101 10101 10101 10010");
        Add('Я', "01111 10001 10001 01111 00101 01001 10001");
        Add('Ё', "01010 00000 11111 10000 11110 10000 11111");
        Add('Є', "01110 10001 10000 11100 10000 10001 01110");
        Add('Ї', "01010 00000 01110 00100 00100 00100 01110");
        Add('Ў', "01010 00100 10001 01111 00001 10001 01110");

        // Greek capitals.
        Add('Γ', "11111 10000 10000 10000 10000 10000 10000");
        Add('Δ', "00100 00100 01010 01010 10001 10001 11111");
        Add('Θ', "01110 10001 10001 11111 10001 10001 01110");
        Add('Λ', "00100 01010 01010 10001 10001 10001 10001");
        Add('Ξ', "11111 00000 00000 01110 00000 00000 11111");
        Add('Π', "11111 10001 10001 10001 10001 10001 10001");
        Add('Σ', "11111 10000 01000 00100 01000 10000 11111");
        Add('Φ', "00100 01110 10101 10101 10101 01110 00100");
        Add('Ψ', "10101 10101 10101 01110 00100 00100 00100");
        Add('Ω', "01110 10001 10001 10001 01010 01010 11011");
    }

    /// <summary>Letters that look exactly like a Latin capital share its glyph.</summary>
    static readonly Dictionary<char, string> Alias = new()
    {
        ['А'] = "A", ['В'] = "B", ['Е'] = "E", ['К'] = "K", ['М'] = "M", ['Н'] = "H", ['О'] = "O", ['Р'] = "P",
        ['С'] = "C", ['Т'] = "T", ['Х'] = "X", ['Ѕ'] = "S", ['Ј'] = "J", ['І'] = "I",
        ['Α'] = "A", ['Β'] = "B", ['Ε'] = "E", ['Ζ'] = "Z", ['Η'] = "H", ['Ι'] = "I", ['Κ'] = "K", ['Μ'] = "M",
        ['Ν'] = "N", ['Ο'] = "O", ['Ρ'] = "P", ['Τ'] = "T", ['Υ'] = "Y", ['Χ'] = "X",
        ['ß'] = "SS", ['ẞ'] = "SS", ['Ł'] = "L", ['Ø'] = "O", ['Đ'] = "D", ['Æ'] = "AE", ['Œ'] = "OE", ['Þ'] = "P",
        ['‘'] = "'", ['’'] = "'", ['`'] = "'", ['“'] = "\"", ['”'] = "\"", ['„'] = "\"", ['–'] = "-", ['—'] = "-",
        ['…'] = "...", ['\u00A0'] = " ", ['\u3000'] = " ",
    };

    static void Add(char c, string rows)
    {
        var parts = rows.Split(' ');
        var bits = new byte[7];
        for (int r = 0; r < 7; r++) bits[r] = Convert.ToByte(parts[r], 2);
        Glyphs[c] = bits;
    }

    /// <summary>
    /// Makes text displayable: uppercases, turns accented Latin/Greek/Cyrillic letters into their plain capitals,
    /// and keeps every other script's characters (drawn as fine dots). Only unprintable characters become '?'.
    /// </summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s.Normalize(NormalizationForm.FormC)) // composed, so が stays one character
        {
            if (char.IsSurrogate(ch))
            {
                if (char.IsHighSurrogate(ch)) sb.Append('?'); // emoji and rare characters
                continue;
            }
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format) continue;
            if (char.IsControl(ch)) { sb.Append(' '); continue; }
            char u = char.ToUpperInvariant(ch);
            if (Glyphs.ContainsKey(u)) { sb.Append(u); continue; }
            if (Alias.TryGetValue(u, out var a) || Alias.TryGetValue(ch, out a)) { sb.Append(a); continue; }
            // Accented letters: use the plain capital when there is one (É → E, Ά → A, Й stays Й above).
            string d = u.ToString().Normalize(NormalizationForm.FormD);
            if (d.Length > 1 && (Glyphs.ContainsKey(d[0]) || Alias.ContainsKey(d[0])))
            {
                sb.Append(Alias.TryGetValue(d[0], out var ba) ? ba : d[0].ToString());
                continue;
            }
            sb.Append(u); // another script: drawn as fine dots
        }
        return sb.ToString();
    }

    /// <summary>How many dot columns a character takes, including the gap after it.</summary>
    static int Advance(char c) => Glyphs.ContainsKey(c) ? 6 : Fine(c) is { } f ? (f.Width + 1) / 2 + 1 : 6;

    public static float Width(string s, float pitch)
    {
        if (s.Length == 0) return 0;
        int cols = 0;
        foreach (char c in s) cols += Advance(c);
        return (cols - 1) * pitch;
    }

    /// <summary>Cuts text to fit a width, ending with '~' when something was cut.</summary>
    public static string Fit(string s, float maxWidth, float pitch)
    {
        if (Width(s, pitch) <= maxWidth) return s;
        float room = maxWidth - Width("~", pitch) - pitch;
        int n = s.Length;
        while (n > 0 && Width(s[..n], pitch) > room) n--;
        return s[..n] + "~";
    }

    /// <summary>Appends one small square per lit dot.</summary>
    public static void Emit(List<RectangleF> dots, string s, float x, float y, float pitch, float dotSize)
    {
        float gx = x;
        foreach (char ch in s)
        {
            if (Glyphs.TryGetValue(ch, out var g))
            {
                for (int r = 0; r < 7; r++)
                {
                    byte row = g[r];
                    if (row == 0) continue;
                    for (int c = 0; c < 5; c++)
                        if ((row & (1 << (4 - c))) != 0)
                            dots.Add(new RectangleF(gx + c * pitch, y + r * pitch, dotSize, dotSize));
                }
                gx += 6 * pitch;
                continue;
            }
            var f = Fine(ch);
            if (f == null)
            {
                gx += 6 * pitch;
                continue;
            }
            // Fine dots: half the pitch, twice the rows, same line height.
            float hp = pitch / 2, hd = dotSize / 2;
            for (int r = 0; r < FineRows; r++)
            {
                uint row = f.Rows[r];
                if (row == 0) continue;
                for (int c = 0; c < f.Width; c++)
                    if ((row & (1u << c)) != 0)
                        dots.Add(new RectangleF(gx + c * hp, y + r * hp, hd, hd));
            }
            gx += ((f.Width + 1) / 2 + 1) * pitch;
        }
    }

    // ───────────────────────────── fine dots for other scripts ─────────────────────────────

    const int FineRows = 14;

    sealed record FineGlyph(int Width, uint[] Rows);

    static readonly Dictionary<char, FineGlyph?> FineCache = new();

    static bool IsCjk(char c) => c is (>= '\u2E80' and <= '\u9FFF') or (>= '\uF900' and <= '\uFAFF') or (>= '\uFF00' and <= '\uFF60') or (>= '\uFFE0' and <= '\uFFEF');
    static bool IsHangul(char c) => c is (>= '\uAC00' and <= '\uD7AF') or (>= '\u1100' and <= '\u11FF') or (>= '\u3130' and <= '\u318F');

    /// <summary>A character from another script, rasterized once from a system font onto the 14-row grid.</summary>
    static FineGlyph? Fine(char c)
    {
        lock (FineCache)
        {
            if (FineCache.TryGetValue(c, out var hit)) return hit;
            FineGlyph? g;
            try { g = Rasterize(c); }
            catch (Exception) { g = null; }
            FineCache[c] = g;
            return g;
        }
    }

    static FineGlyph? Rasterize(char c)
    {
        bool cjk = IsCjk(c), hangul = IsHangul(c);
        // Japanese/Chinese use MS Gothic's hand-tuned 14-pixel bitmaps, which look just like an LCD font.
        bool strike = cjk;
        string family = cjk ? "MS Gothic" : hangul ? "Malgun Gothic" : "Segoe UI";
        int size = strike ? FineRows : 64;
        using var bmp = new Bitmap(size * 2, size * 2);
        using var gr = Graphics.FromImage(bmp);
        gr.Clear(Color.White);
        gr.TextRenderingHint = strike ? TextRenderingHint.SingleBitPerPixelGridFit : TextRenderingHint.AntiAlias;
        using var font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
        float ascent = font.FontFamily.GetCellAscent(FontStyle.Regular) * size / (float)font.FontFamily.GetEmHeight(FontStyle.Regular);
        using (var fmt = StringFormat.GenericTypographic)
            gr.DrawString(c.ToString(), font, Brushes.Black, 0, 0, fmt);

        // Map the em box (0.86 em above the baseline to 0.14 below) onto the 14 rows.
        float top = ascent - 0.86f * size, cell = size / (float)FineRows;
        int maxCols = Math.Min(28, (int)(bmp.Width / cell));
        var grid = new bool[FineRows, maxCols];
        for (int r = 0; r < FineRows; r++)
            for (int col = 0; col < maxCols; col++)
            {
                int ink = 0, n = 0;
                for (int py = (int)(top + r * cell); py < (int)(top + (r + 1) * cell); py++)
                    for (int px = (int)(col * cell); px < (int)((col + 1) * cell); px++)
                    {
                        if (px < 0 || py < 0 || px >= bmp.Width || py >= bmp.Height) continue;
                        n++;
                        if (bmp.GetPixel(px, py).R < 128) ink++;
                    }
                grid[r, col] = n > 0 && ink >= n * (strike ? 0.5 : 0.38);
            }

        // Full-width characters keep their whole cell; others are trimmed to their ink.
        int first = maxCols, last = -1;
        for (int col = 0; col < maxCols; col++)
            for (int r = 0; r < FineRows; r++)
                if (grid[r, col]) { first = Math.Min(first, col); last = Math.Max(last, col); }
        if (last < 0) return new FineGlyph(8, new uint[FineRows]); // blank (a space-like character)
        bool fullWidth = cjk || hangul;
        if (fullWidth) { first = 0; last = Math.Max(last, FineRows - 1); }
        int width = Math.Min(28, last - first + 1);
        var rows = new uint[FineRows];
        for (int r = 0; r < FineRows; r++)
            for (int col = 0; col < width; col++)
                if (grid[r, first + col]) rows[r] |= 1u << col;
        return new FineGlyph(width, rows);
    }
}
