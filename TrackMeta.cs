using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RetroRadio;

/// <summary>What the modern screens show about a track: tags, the album cover and colours taken from it.</summary>
sealed class TrackMeta
{
    public string Title = "", Artist = "", Album = "";
    public uint Year;
    public Bitmap? Cover;     // square, up to 600 px
    public Bitmap? Backdrop;  // tiny copy: drawn large it becomes a soft blurred background
    public Color Accent = Color.Empty;

    static readonly ConcurrentDictionary<string, TrackMeta?> Cache = new();
    static readonly ConcurrentDictionary<string, byte> Loading = new();
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Artwork links for online tracks, filled in while browsing SoundCloud or Spotify.</summary>
    public static readonly ConcurrentDictionary<string, string> OnlineArt = new();

    /// <summary>Returns what's known so far; starts loading in the background the first time.</summary>
    public static TrackMeta? For(string entry)
    {
        if (Cache.TryGetValue(entry, out var m)) return m;
        if (Loading.TryAdd(entry, 0))
        {
            Task.Run(async () =>
            {
                TrackMeta? meta = null;
                try { meta = await LoadAsync(entry); }
                catch (Exception) { }
                Cache[entry] = meta;
                Loading.TryRemove(entry, out _);
            });
        }
        return null;
    }

    static async Task<TrackMeta?> LoadAsync(string entry)
    {
        var meta = new TrackMeta();
        byte[]? picture = null;

        if (entry.StartsWith("sc://", StringComparison.Ordinal) || entry.StartsWith("sp://", StringComparison.Ordinal))
        {
            var parts = entry[5..].Split('/', 3);
            string name = parts.Length > 2 ? Uri.UnescapeDataString(parts[2]) : "";
            int dash = name.IndexOf(" - ", StringComparison.Ordinal);
            (meta.Artist, meta.Title) = dash > 0 ? (name[..dash], name[(dash + 3)..]) : ("", name);
            meta.Album = entry.StartsWith("sp") ? "Spotify" : "SoundCloud";
            if (OnlineArt.TryGetValue(parts[0], out var url)) picture = await Http.GetByteArrayAsync(url);
        }
        else
        {
            meta.Title = Path.GetFileNameWithoutExtension(entry).Replace('_', ' ');
            try
            {
                using var f = TagLib.File.Create(entry);
                var t = f.Tag;
                if (!string.IsNullOrWhiteSpace(t.Title)) meta.Title = t.Title.Trim();
                meta.Artist = (t.FirstPerformer ?? t.FirstAlbumArtist ?? "").Trim();
                meta.Album = (t.Album ?? "").Trim();
                meta.Year = t.Year;
                var pic = t.Pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? t.Pictures.FirstOrDefault();
                picture = pic?.Data?.Data;
            }
            catch (Exception)
            {
                // Untagged or unsupported: fall back to the file name.
            }
            if (meta.Artist.Length == 0)
            {
                int dash = meta.Title.IndexOf(" - ", StringComparison.Ordinal);
                if (dash > 0) (meta.Artist, meta.Title) = (meta.Title[..dash].Trim(), meta.Title[(dash + 3)..].Trim());
            }
            picture ??= FolderArt(entry);
        }

        if (picture != null)
        {
            try
            {
                using var ms = new MemoryStream(picture);
                using var img = Image.FromStream(ms);
                meta.Cover = Square(img, 600);
                meta.Backdrop = Square(img, 24);
                meta.Accent = AccentOf(meta.Backdrop);
            }
            catch (Exception) { }
        }
        return meta;
    }

    /// <summary>cover.jpg / folder.jpg / front.png next to the file, or the only picture in the folder.</summary>
    static byte[]? FolderArt(string file)
    {
        try
        {
            var dir = Path.GetDirectoryName(file);
            if (dir == null || !Directory.Exists(dir)) return null;
            var pics = Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
            string[] names = ["cover", "folder", "front", "album", "art"];
            var pick = pics.FirstOrDefault(p => names.Contains(Path.GetFileNameWithoutExtension(p).ToLowerInvariant())) ?? (pics.Count == 1 ? pics[0] : null);
            return pick == null ? null : File.ReadAllBytes(pick);
        }
        catch (Exception)
        {
            return null;
        }
    }

    static Bitmap Square(Image img, int size)
    {
        int side = Math.Min(img.Width, img.Height);
        size = Math.Min(size, Math.Max(8, side));
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(img, new Rectangle(0, 0, size, size), (img.Width - side) / 2, (img.Height - side) / 2, side, side, GraphicsUnit.Pixel);
        return bmp;
    }

    /// <summary>A lively colour from the cover to tint the interface with.</summary>
    static Color AccentOf(Bitmap tiny)
    {
        double best = -1;
        Color pick = Color.FromArgb(110, 200, 255);
        for (int y = 0; y < tiny.Height; y++)
        {
            for (int x = 0; x < tiny.Width; x++)
            {
                var c = tiny.GetPixel(x, y);
                double score = c.GetSaturation() * (0.35 + c.GetBrightness());
                if (c.GetBrightness() < 0.2 || c.GetBrightness() > 0.95) score *= 0.3;
                if (score > best) { best = score; pick = c; }
            }
        }
        // Brighten so it reads on a dark screen.
        float h = pick.GetHue(), s = Math.Max(0.45f, pick.GetSaturation()), l = Math.Clamp(pick.GetBrightness(), 0.55f, 0.72f);
        return FromHsl(h, s, l);
    }

    static Color FromHsl(float h, float s, float l)
    {
        float c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        (float r, float g, float b) = h switch
        {
            < 60 => (c, x, 0f), < 120 => (x, c, 0f), < 180 => (0f, c, x), < 240 => (0f, x, c), < 300 => (x, 0f, c), _ => (c, 0f, x),
        };
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }
}
