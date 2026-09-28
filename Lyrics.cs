using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RetroRadio;

/// <summary>Time-synced lyrics: each line with the second it starts.</summary>
sealed class Lyrics(List<(double T, string Line)> lines)
{
    public readonly List<(double T, string Line)> Lines = lines;

    /// <summary>The line being sung at this position (and the one after it), or null before the first line.</summary>
    public (string Line, string Next)? At(double seconds)
    {
        int i = Lines.FindLastIndex(l => l.T <= seconds);
        if (i < 0) return null;
        return (Lines[i].Line, i + 1 < Lines.Count ? Lines[i + 1].Line : "");
    }
}

/// <summary>
/// Looks lyrics up on LRCLIB (lrclib.net), a free, open lyrics database with timestamps, needing no account.
/// Spotify's own lyrics aren't available to apps. Lookups run in the background and are cached per song.
/// </summary>
static partial class LyricsService
{
    static readonly HttpClient Http = CreateClient();
    static readonly ConcurrentDictionary<string, Lyrics?> Cache = new();
    static readonly ConcurrentDictionary<string, byte> Loading = new();

    static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRadio/1.0 (https://github.com/ryxn3/retro-multy-program)");
        return h;
    }

    public enum Status { Loading, Found, None }

    /// <summary>Cached lyrics for a song; starts a lookup the first time it's asked.</summary>
    public static (Status, Lyrics?) For(string artist, string title, double durationSec)
    {
        title = Clean(title);
        artist = artist.Trim();
        if (title.Length == 0) return (Status.None, null);
        string key = $"{artist}\n{title}".ToLowerInvariant();
        if (Cache.TryGetValue(key, out var hit)) return (hit == null ? Status.None : Status.Found, hit);
        if (Loading.TryAdd(key, 0))
        {
            _ = Task.Run(async () =>
            {
                Lyrics? found = null;
                try { found = await FetchAsync(artist, title, durationSec); }
                catch (Exception) { } // offline or not found: no lyrics
                Cache[key] = found;
                Loading.TryRemove(key, out _);
            });
        }
        return (Status.Loading, null);
    }

    static async Task<Lyrics?> FetchAsync(string artist, string title, double duration)
    {
        static string Q(string s) => Uri.EscapeDataString(s);
        if (artist.Length > 0)
        {
            string url = $"https://lrclib.net/api/get?artist_name={Q(artist)}&track_name={Q(title)}";
            if (duration > 1) url += $"&duration={Math.Round(duration).ToString(CultureInfo.InvariantCulture)}";
            using var res = await Http.GetAsync(url);
            if (res.IsSuccessStatusCode && Parse(JsonNode.Parse(await res.Content.ReadAsStringAsync())) is { } exact) return exact;
            if (res.StatusCode != HttpStatusCode.NotFound && !res.IsSuccessStatusCode) return null;
        }
        // Search, preferring a synced result with a similar length.
        string search = artist.Length > 0 ? $"track_name={Q(title)}&artist_name={Q(artist)}" : $"q={Q(title)}";
        var arr = JsonNode.Parse(await Http.GetStringAsync($"https://lrclib.net/api/search?{search}")) as JsonArray;
        var best = arr?
            .Where(n => !string.IsNullOrEmpty((string?)n?["syncedLyrics"]))
            .OrderBy(n => duration > 1 ? Math.Abs(((double?)n?["duration"] ?? 0) - duration) : 0)
            .FirstOrDefault();
        return Parse(best);
    }

    static Lyrics? Parse(JsonNode? n)
    {
        string? lrc = (string?)n?["syncedLyrics"];
        if (string.IsNullOrWhiteSpace(lrc)) return null;
        var lines = new List<(double, string)>();
        foreach (var raw in lrc.Split('\n'))
        {
            var stamps = Stamp().Matches(raw);
            if (stamps.Count == 0) continue;
            string text = Stamp().Replace(raw, "").Trim();
            foreach (Match m in stamps)
            {
                double t = int.Parse(m.Groups[1].Value) * 60 + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                lines.Add((t, text));
            }
        }
        lines.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return lines.Count == 0 ? null : new Lyrics(lines);
    }

    /// <summary>Drops "(Remastered 2011)", "- Radio Edit", "[Live]" and the like so the song is found.</summary>
    static string Clean(string title)
    {
        title = Extras().Replace(title, "");
        int dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && Regex.IsMatch(title[(dash + 3)..], "remaster|edit|version|live|mono|stereo|mix", RegexOptions.IgnoreCase))
            title = title[..dash];
        return title.Trim();
    }

    [GeneratedRegex(@"\[(\d+):(\d+(?:\.\d+)?)\]")]
    private static partial Regex Stamp();

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*(remaster|edit|version|live|mono|stereo|feat\.?|ft\.)[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Extras();
}
