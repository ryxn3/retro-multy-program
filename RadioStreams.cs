using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using NAudio.Wave;

namespace RetroRadio;

/// <summary>An internet radio station from the radio-browser.info directory.</summary>
sealed record Station(string Name, string Url, string Codec, int Bitrate, string Country);

/// <summary>
/// radio-browser.info: a free, community-run directory of internet radio stations (no account needed).
/// </summary>
static class RadioBrowser
{
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20), BaseAddress = new Uri("https://all.api.radio-browser.info/") };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRadio/1.0 (https://github.com/ryxn3/retro-multy-program)");
        return h;
    }

    static async Task<List<Station>> Get(string path, CancellationToken ct)
    {
        var arr = JsonNode.Parse(await Http.GetStringAsync(path, ct)) as JsonArray ?? [];
        return arr
            .Select(n => new Station(
                ((string?)n?["name"] ?? "?").Trim(),
                (string?)n?["url_resolved"] ?? (string?)n?["url"] ?? "",
                ((string?)n?["codec"] ?? "").ToUpperInvariant(),
                (int?)n?["bitrate"] ?? 0,
                (string?)n?["countrycode"] ?? ""))
            .Where(s => s.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && s.Name.Length > 0)
            // MP3 and AAC streams play everywhere; others need ffmpeg.
            .OrderBy(s => s.Codec is "MP3" or "AAC" or "AAC+" ? 0 : 1)
            .DistinctBy(s => s.Name.ToLowerInvariant())
            .ToList();
    }

    const string Common = "hidebroken=true&order=clickcount&reverse=true&limit=60";

    public static Task<List<Station>> Top(CancellationToken ct) => Get($"json/stations/search?{Common}", ct);
    public static Task<List<Station>> ByTag(string tag, CancellationToken ct) => Get($"json/stations/search?tag={Uri.EscapeDataString(tag)}&{Common}", ct);
    public static Task<List<Station>> ByCountry(string code, CancellationToken ct) => Get($"json/stations/search?countrycode={code}&{Common}", ct);
    public static Task<List<Station>> Search(string q, CancellationToken ct) => Get($"json/stations/search?name={Uri.EscapeDataString(q)}&{Common}", ct);

    /// <summary>Queue entries look like fm://&lt;escaped url&gt;/&lt;escaped name&gt;.</summary>
    public static string Entry(Station s) => $"fm://{Uri.EscapeDataString(s.Url)}/{Uri.EscapeDataString(s.Name)}";

    public static bool IsEntry(string e) => e.StartsWith("fm://", StringComparison.Ordinal);

    public static (string Url, string Name) Parse(string e)
    {
        var parts = e[5..].Split('/', 2);
        return (Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "RADIO");
    }
}

/// <summary>
/// A live MP3 radio stream with Shoutcast/Icecast song titles ("ICY" metadata). Audio is decoded on a
/// background thread into a buffer the radio plays from; the title of the song on air is kept up to date.
/// </summary>
sealed class IcyStreamSource : ITrackSource
{
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var h = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("RetroRadio/1.0");
        return h;
    }

    readonly CancellationTokenSource cts = new();
    readonly Stopwatch clock = new();
    BufferedWaveProvider? buffer;
    ISampleProvider? samples;

    public string StreamTitle { get; private set; } = "";
    public event Action<string>? TitleChanged;

    public ISampleProvider Samples => samples!;
    public TimeSpan TotalTime => TimeSpan.Zero;
    public TimeSpan CurrentTime { get => clock.Elapsed; set { } }
    public bool IsLive => true;

    /// <summary>Connects and waits for the first audio; throws if the stream isn't MP3 or can't be reached.</summary>
    public static async Task<IcyStreamSource> OpenAsync(string url, CancellationToken ct)
    {
        var src = new IcyStreamSource();
        try
        {
            await src.StartAsync(url, ct);
            return src;
        }
        catch
        {
            src.Dispose();
            throw;
        }
    }

    async Task StartAsync(string url, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Icy-MetaData", "1");
        var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        string type = res.Content.Headers.ContentType?.MediaType ?? "";
        if (type.Contains("mpegurl") || type.Contains("x-scpls") || url.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".pls", StringComparison.OrdinalIgnoreCase))
        {
            // A playlist file: follow its first stream.
            string text = await res.Content.ReadAsStringAsync(ct);
            string? next = text.Split('\n').Select(l => l.Trim()).Select(l => l.StartsWith("File", StringComparison.OrdinalIgnoreCase) && l.Contains('=') ? l[(l.IndexOf('=') + 1)..] : l)
                .FirstOrDefault(l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase));
            res.Dispose();
            if (next == null) throw new ServiceException("STATION NOT AVAILABLE");
            await StartAsync(next, ct);
            return;
        }
        if (!(type.Contains("mpeg") || type.Contains("mp3") || type.Length == 0))
        {
            res.Dispose();
            throw new NotSupportedException(type); // AAC and others: the caller falls back to another player
        }
        int metaInt = res.Headers.TryGetValues("icy-metaint", out var mi) && int.TryParse(mi.FirstOrDefault(), out int m) ? m : 0;
        var net = await res.Content.ReadAsStreamAsync(ct);
        var icy = new IcyDemuxStream(net, metaInt, t =>
        {
            StreamTitle = t;
            TitleChanged?.Invoke(t);
        });

        // Decode the first frame here, so the format is known before playback starts.
        var first = await Task.Run(() => Mp3Frame.LoadFromStream(icy), ct) ?? throw new ServiceException("STATION NOT AVAILABLE");
        var fmt = new Mp3WaveFormat(first.SampleRate, first.ChannelMode == ChannelMode.Mono ? 1 : 2, first.FrameLength, first.BitRate);
        var decoder = new NLayer.NAudioSupport.Mp3FrameDecompressor(fmt);
        buffer = new BufferedWaveProvider(decoder.OutputFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(20),
            DiscardOnBufferOverflow = true,
            ReadFully = true, // silence while it buffers, never "the end"
        };
        samples = buffer.ToSampleProvider();
        clock.Start();
        var token = cts.Token;
        new Thread(() => Pump(icy, first, decoder, token)) { IsBackground = true, Name = "Radio stream" }.Start();
    }

    void Pump(Stream icy, Mp3Frame first, IMp3FrameDecompressor decoder, CancellationToken ct)
    {
        var pcm = new byte[16384 * 4];
        try
        {
            var frame = first;
            while (!ct.IsCancellationRequested && frame != null)
            {
                int n = decoder.DecompressFrame(frame, pcm, 0);
                if (n > 0) buffer!.AddSamples(pcm, 0, n);
                // Don't run too far ahead of playback.
                while (buffer!.BufferedDuration > TimeSpan.FromSeconds(8) && !ct.IsCancellationRequested) Thread.Sleep(50);
                frame = Mp3Frame.LoadFromStream(icy);
            }
        }
        catch (Exception) { } // the stream ended or dropped: it just goes quiet
        finally
        {
            decoder.Dispose();
            icy.Dispose();
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        clock.Stop();
    }

    /// <summary>Removes the song-title blocks Shoutcast servers mix into the audio every "metaint" bytes.</summary>
    sealed class IcyDemuxStream(Stream inner, int metaInt, Action<string> onTitle) : Stream
    {
        int untilMeta = metaInt;
        long read; // the MP3 frame reader asks for Position

        /// <summary>Fills the whole request (the MP3 frame reader expects that), unless the stream ends.</summary>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = ReadSome(buffer, offset + total, count - total);
                if (n <= 0) break;
                total += n;
            }
            read += total;
            return total;
        }

        int ReadSome(byte[] buffer, int offset, int count)
        {
            if (metaInt <= 0) return inner.Read(buffer, offset, count);
            if (untilMeta == 0)
            {
                ReadMeta();
                untilMeta = metaInt;
            }
            int n = inner.Read(buffer, offset, Math.Min(count, untilMeta));
            untilMeta -= n;
            return n;
        }

        void ReadMeta()
        {
            int len = inner.ReadByte() * 16;
            if (len <= 0) return;
            var meta = new byte[len];
            int got = 0;
            while (got < len)
            {
                int n = inner.Read(meta, got, len - got);
                if (n <= 0) return;
                got += n;
            }
            string text = Encoding.UTF8.GetString(meta).TrimEnd('\0');
            int a = text.IndexOf("StreamTitle='", StringComparison.Ordinal);
            if (a < 0) return;
            a += 13;
            int b = text.IndexOf("';", a, StringComparison.Ordinal);
            string title = (b > a ? text[a..b] : text[a..]).Trim();
            if (title.Length > 0) onTitle(title);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

/// <summary>A live stream in another format (AAC…), played through ffmpeg or Windows' own decoder.</summary>
sealed class LiveFallbackSource(ITrackSource inner) : ITrackSource
{
    readonly Stopwatch clock = Stopwatch.StartNew();
    public ISampleProvider Samples => inner.Samples;
    public TimeSpan TotalTime => TimeSpan.Zero;
    public TimeSpan CurrentTime { get => clock.Elapsed; set { } }
    public bool IsLive => true;
    public void Dispose() => inner.Dispose();
}
