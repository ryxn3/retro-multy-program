using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace RetroRadio;

/// <summary>
/// Spotify Web API. Spotify does not let other apps stream its audio, so the radio browses
/// here and then remote-controls the user's Spotify app (Spotify Connect, needs Premium).
/// Sign-in is PKCE, so only a Client ID is needed — no secret.
/// </summary>
sealed class SpotifyClient : IMusicService
{
    const string Accounts = "https://accounts.spotify.com";
    const string Api = "https://api.spotify.com/v1";
    const string Scopes = "user-read-private playlist-read-private playlist-read-collaborative user-library-read " +
                          "user-read-playback-state user-modify-playback-state user-read-currently-playing";

    static string StorePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroRadio", "spotify.dat");

    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    string? accessToken, refreshToken;
    DateTime accessExpires;

    public string Name => "SPOTIFY";
    public string EntryPrefix => "sp://";
    public bool NeedsSecret => false;
    public bool CanBrowseSignedOut => false;
    public string AppsUrl => "https://developer.spotify.com/dashboard";
    public string SetupHelp =>
        "Spotify only lets registered apps connect. It's free:\n\n" +
        "1. Open the Spotify developer dashboard below and create an app (tick 'Web API').\n" +
        "2. Add the Redirect URI shown below, exactly.\n" +
        "3. Paste the app's Client ID here (no secret needed).\n\n" +
        "Playback is done by your Spotify app; controlling it needs Spotify Premium.";
    public string ClientId { get; private set; } = "";
    public string? Username { get; private set; }
    public bool HasKeys => ClientId.Length > 0;
    public bool SignedIn => refreshToken != null;

    public SpotifyClient() => Load();

    // ───────────────────────────── storage ─────────────────────────────

    void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            var j = JsonNode.Parse(ProtectedData.Unprotect(File.ReadAllBytes(StorePath), null, DataProtectionScope.CurrentUser))!;
            ClientId = (string?)j["clientId"] ?? "";
            refreshToken = (string?)j["refreshToken"];
            Username = (string?)j["username"];
        }
        catch (Exception)
        {
            // Unreadable: start fresh.
        }
    }

    void Save()
    {
        var j = new JsonObject { ["clientId"] = ClientId, ["refreshToken"] = refreshToken, ["username"] = Username };
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllBytes(StorePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(j.ToJsonString()), null, DataProtectionScope.CurrentUser));
    }

    public void SetKeys(string clientId, string clientSecret)
    {
        ClientId = clientId.Trim();
        accessToken = refreshToken = null;
        Username = null;
        Save();
    }

    public void SignOut()
    {
        accessToken = refreshToken = null;
        Username = null;
        Save();
    }

    // ───────────────────────────── auth ─────────────────────────────

    public async Task SignInAsync(CancellationToken ct)
    {
        if (!HasKeys) throw new ServiceException("SET UP APP KEY FIRST");
        var (verifier, challenge, state) = OAuthLoopback.NewPkce();
        string url = $"{Accounts}/authorize?client_id={Uri.EscapeDataString(ClientId)}&response_type=code" +
                     $"&redirect_uri={Uri.EscapeDataString(OAuthLoopback.RedirectUri)}&code_challenge_method=S256&code_challenge={challenge}" +
                     $"&state={state}&scope={Uri.EscapeDataString(Scopes)}";
        string code = await OAuthLoopback.AuthorizeAsync(url, state, ct);
        await TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = OAuthLoopback.RedirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier,
        }, ct);
        var me = await SendAsync(HttpMethod.Get, "/me", null, ct);
        Username = (string?)me?["display_name"] ?? (string?)me?["id"];
        Save();
    }

    async Task TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var res = await http.PostAsync(Accounts + "/api/token", new FormUrlEncodedContent(form), ct);
        if (!res.IsSuccessStatusCode) throw new ServiceException($"LOGIN ERROR {(int)res.StatusCode}");
        var j = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))!;
        accessToken = (string?)j["access_token"];
        refreshToken = (string?)j["refresh_token"] ?? refreshToken;
        accessExpires = DateTime.UtcNow.AddSeconds(((int?)j["expires_in"] ?? 3600) - 60);
    }

    async Task<string> AccessAsync(CancellationToken ct)
    {
        if (accessToken != null && DateTime.UtcNow < accessExpires) return accessToken;
        if (refreshToken == null) throw new ServiceException("PLEASE SIGN IN");
        try
        {
            await TokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = ClientId,
            }, ct);
            Save();
        }
        catch (ServiceException)
        {
            refreshToken = null;
            Username = null;
            Save();
            throw new ServiceException("PLEASE SIGN IN AGAIN");
        }
        return accessToken!;
    }

    /// <summary>Calls the Web API. Returns the JSON body, or null for empty responses.</summary>
    async Task<JsonNode?> SendAsync(HttpMethod method, string pathOrUrl, JsonNode? body, CancellationToken ct)
    {
        string url = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? pathOrUrl : Api + pathOrUrl;
        for (int attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessAsync(ct));
            if (body != null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            else if (method != HttpMethod.Get) req.Content = new StringContent("", Encoding.UTF8, "application/json");
            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                accessToken = null;
                continue;
            }
            string text = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode) return text.Length == 0 ? null : JsonNode.Parse(text);

            string reason = "", message = "";
            try
            {
                var err = JsonNode.Parse(text)?["error"];
                reason = (string?)err?["reason"] ?? "";
                message = (string?)err?["message"] ?? "";
            }
            catch (Exception) { }
            throw reason switch
            {
                "PREMIUM_REQUIRED" => new ServiceException("NEEDS SPOTIFY PREMIUM"),
                "NO_ACTIVE_DEVICE" => new NoDeviceException(),
                _ when res.StatusCode == HttpStatusCode.Forbidden => new ApiException(403, Forbidden(message)),
                _ when res.StatusCode == HttpStatusCode.NotFound => new ApiException(404, "NOT FOUND ON SPOTIFY"),
                _ when res.StatusCode == (HttpStatusCode)429 => new ServiceException("SPOTIFY BUSY - WAIT"),
                _ => new ServiceException($"SPOTIFY ERROR {(int)res.StatusCode}"),
            };
        }
    }

    internal sealed class NoDeviceException() : Exception("NO_ACTIVE_DEVICE");

    /// <summary>An API error whose status code the caller can react to; the message is for the display.</summary>
    internal sealed class ApiException(int status, string message) : Exception(message)
    {
        public int Status { get; } = status;
    }

    /// <summary>Spotify's 403s mean very different things; show the real reason.</summary>
    static string Forbidden(string message)
    {
        string m = message.ToLowerInvariant();
        if (m.Contains("premium")) return "NEEDS SPOTIFY PREMIUM";
        if (m.Contains("not registered") || m.Contains("developer dashboard"))
            return "ADD YOUR ACCOUNT UNDER USER MANAGEMENT IN THE SPOTIFY DEVELOPER DASHBOARD";
        if (m.Contains("restriction")) return "SPOTIFY WON'T DO THAT RIGHT NOW";
        if (m.Contains("scope")) return "PLEASE SIGN IN AGAIN";
        return message.Length > 0 ? "SPOTIFY: " + message.ToUpperInvariant() : "SPOTIFY SAYS NO (403)";
    }

    // ───────────────────────────── browsing ─────────────────────────────

    static string Q(string s) => Uri.EscapeDataString(s);

    static ScTrack? ParseTrack(JsonNode? n)
    {
        if (n?["track"] is JsonObject inner) n = inner;
        else if (n?["item"] is JsonObject item) n = item; // newer playlist /items responses
        if (n == null || (string?)n["type"] is { } t && t != "track") return null;
        string? uri = (string?)n["uri"];
        if (uri == null || uri.StartsWith("spotify:local:", StringComparison.Ordinal)) return null;
        var artists = n["artists"] as JsonArray;
        string artist = artists == null ? "" : string.Join(", ", artists.Select(a => (string?)a?["name"]).Where(a => a != null));
        bool playable = (bool?)n["is_playable"] ?? true;
        string? art = (string?)(n["album"]?["images"] as JsonArray)?.FirstOrDefault()?["url"];
        return new ScTrack(uri, (string?)n["name"] ?? "?", artist, (int?)n["duration_ms"] ?? 0, playable ? "playable" : "blocked", art);
    }

    static ScPlaylist? ParsePlaylist(JsonNode? n)
    {
        if (n == null) return null;
        string? id = (string?)n["id"];
        if (id == null) return null;
        int total = (int?)n["tracks"]?["total"] ?? (int?)n["items"]?["total"] ?? 0;
        return new ScPlaylist(id, (string?)n["name"] ?? "?", total);
    }

    static ScPage<T> Page<T>(JsonNode? container, Func<JsonNode?, T?> parse) where T : class
    {
        var items = (container?["items"] as JsonArray ?? []).Select(parse).Where(x => x != null).Select(x => x!).ToList();
        return new ScPage<T>(items, (string?)container?["next"]);
    }

    public async Task<ScPage<ScTrack>> SearchTracksAsync(string q, CancellationToken ct) =>
        Page((await SendAsync(HttpMethod.Get, $"/search?type=track&limit=40&q={Q(q)}", null, ct))?["tracks"], ParseTrack);

    public async Task<ScPage<ScPlaylist>> SearchPlaylistsAsync(string q, CancellationToken ct) =>
        Page((await SendAsync(HttpMethod.Get, $"/search?type=playlist&limit=40&q={Q(q)}", null, ct))?["playlists"], ParsePlaylist);

    public async Task<ScPage<ScPlaylist>> MyPlaylistsAsync(CancellationToken ct) =>
        Page(await SendAsync(HttpMethod.Get, "/me/playlists?limit=50", null, ct), ParsePlaylist);

    public async Task<ScPage<ScTrack>> MyLikesAsync(CancellationToken ct) =>
        Page(await SendAsync(HttpMethod.Get, "/me/tracks?limit=50", null, ct), ParseTrack);

    /// <summary>
    /// A playlist's songs. Spotify no longer lets apps in development mode read playlists the user
    /// doesn't own or collaborate on; those throw <see cref="PlaylistLockedException"/> and can still be played whole.
    /// </summary>
    public async Task<ScPage<ScTrack>> PlaylistTracksAsync(string playlistId, CancellationToken ct)
    {
        try
        {
            JsonNode? j;
            try
            {
                j = await SendAsync(HttpMethod.Get, $"/playlists/{playlistId}/items?limit=100", null, ct);
            }
            catch (ApiException e) when (e.Status == 404)
            {
                j = await SendAsync(HttpMethod.Get, $"/playlists/{playlistId}/tracks?limit=100", null, ct); // older API
            }
            return Page(j, ParseTrack);
        }
        catch (ApiException e) when (e.Status is 403 or 404)
        {
            throw new PlaylistLockedException(playlistId);
        }
    }

    public async Task<ScPage<ScTrack>> MoreTracksAsync(string next, CancellationToken ct)
    {
        var j = await SendAsync(HttpMethod.Get, next, null, ct);
        return Page(j?["tracks"] ?? j, ParseTrack);
    }

    public async Task<ScPage<ScPlaylist>> MorePlaylistsAsync(string next, CancellationToken ct)
    {
        var j = await SendAsync(HttpMethod.Get, next, null, ct);
        return Page(j?["playlists"] ?? j, ParsePlaylist);
    }

    // ───────────────────────────── remote control (Spotify Connect) ─────────────────────────────

    public sealed record PlayerState(bool Playing, int ProgressMs, int DurationMs, string? Uri, string Title);

    public async Task<PlayerState?> GetStateAsync(CancellationToken ct)
    {
        var j = await SendAsync(HttpMethod.Get, "/me/player", null, ct);
        if (j == null) return null;
        var item = j["item"];
        var track = ParseTrack(item);
        return new PlayerState((bool?)j["is_playing"] ?? false, (int?)j["progress_ms"] ?? 0, (int?)item?["duration_ms"] ?? 0,
            (string?)item?["uri"], track == null ? "" : track.Artist.Length > 0 ? $"{track.Artist} - {track.Title}" : track.Title);
    }

    /// <summary>Starts these tracks on the user's Spotify app, waking a device if none is active.</summary>
    public Task PlayAsync(IReadOnlyList<string> uris, int offset, CancellationToken ct) => PlayBodyAsync(new JsonObject
    {
        ["uris"] = new JsonArray(uris.Select(u => (JsonNode)JsonValue.Create(u)!).ToArray()),
        ["offset"] = new JsonObject { ["position"] = offset },
    }, ct);

    /// <summary>Plays a whole playlist or album (spotify:playlist:...) on the user's Spotify app.</summary>
    public Task PlayContextAsync(string contextUri, CancellationToken ct) =>
        PlayBodyAsync(new JsonObject { ["context_uri"] = contextUri }, ct);

    public Task NextAsync(CancellationToken ct) => Command(HttpMethod.Post, "/me/player/next", ct);
    public Task PreviousAsync(CancellationToken ct) => Command(HttpMethod.Post, "/me/player/previous", ct);

    async Task PlayBodyAsync(JsonObject body, CancellationToken ct)
    {
        try
        {
            await SendAsync(HttpMethod.Put, "/me/player/play", body, ct);
        }
        catch (NoDeviceException)
        {
            var devices = (await SendAsync(HttpMethod.Get, "/me/player/devices", null, ct))?["devices"] as JsonArray;
            var pick = devices?.FirstOrDefault(d => (string?)d?["type"] == "Computer") ?? devices?.FirstOrDefault();
            string? id = (string?)pick?["id"];
            if (id == null) throw new ServiceException("OPEN THE SPOTIFY APP");
            await SendAsync(HttpMethod.Put, $"/me/player/play?device_id={Q(id)}", body.DeepClone(), ct);
        }
    }

    async Task Command(HttpMethod m, string path, CancellationToken ct)
    {
        try
        {
            await SendAsync(m, path, null, ct);
        }
        catch (NoDeviceException)
        {
            throw new ServiceException("OPEN THE SPOTIFY APP");
        }
    }

    public Task ResumeAsync(CancellationToken ct) => Command(HttpMethod.Put, "/me/player/play", ct);
    public Task PauseAsync(CancellationToken ct) => Command(HttpMethod.Put, "/me/player/pause", ct);
    public Task SeekAsync(int ms, CancellationToken ct) => Command(HttpMethod.Put, $"/me/player/seek?position_ms={Math.Max(0, ms)}", ct);
    public Task VolumeAsync(int percent, CancellationToken ct) => Command(HttpMethod.Put, $"/me/player/volume?volume_percent={Math.Clamp(percent, 0, 100)}", ct);
    public Task ShuffleAsync(bool on, CancellationToken ct) => Command(HttpMethod.Put, $"/me/player/shuffle?state={(on ? "true" : "false")}", ct);
    public Task RepeatAsync(string mode, CancellationToken ct) => Command(HttpMethod.Put, $"/me/player/repeat?state={mode}", ct);
}

/// <summary>
/// Makes the Spotify app look like a local deck to the radio: polls what it's playing,
/// and sends play / pause / seek / volume to it.
/// </summary>
sealed class SpotifyRemote(SpotifyClient client) : IPlayback
{
    bool playing;
    int progressMs, durationMs;
    DateTime fetchedAt = DateTime.UtcNow, lastPoll, ignoreUntil;
    string? uri;
    bool polling, volumeBusy;
    int pendingVolume = -1;

    /// <summary>True while the radio's current track is a Spotify one.</summary>
    public bool Active { get; set; }
    public string? CurrentUri => uri;

    public event Action<string, string>? TrackChanged; // uri, title
    public event Action<string>? Error;

    public bool IsLoaded => Active;
    public bool IsPlaying => Active && playing;
    public bool IsPaused => Active && !playing;
    public TimeSpan Duration => TimeSpan.FromMilliseconds(durationMs);

    public TimeSpan Position
    {
        get
        {
            double ms = progressMs + (playing ? (DateTime.UtcNow - fetchedAt).TotalMilliseconds : 0);
            return TimeSpan.FromMilliseconds(Math.Clamp(ms, 0, Math.Max(0, durationMs)));
        }
    }

    /// <summary>
    /// Sends a command. Only commands the listener asked for (play, pause, skip, seek) report failures;
    /// the radio's own background syncing (shuffle, repeat, volume) fails silently, since there's nothing
    /// to do about it and a message out of nowhere is just annoying.
    /// </summary>
    void Fire(Func<CancellationToken, Task> call, bool quiet = false)
    {
        // Optimistic UI: don't let a poll that raced the command undo it.
        ignoreUntil = DateTime.UtcNow.AddSeconds(1.5);
        _ = Run();
        async Task Run()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await call(cts.Token);
            }
            catch (Exception ex)
            {
                if (!quiet) Report(Describe(ex));
            }
        }
    }

    string? lastError;
    DateTime lastErrorAt;

    /// <summary>Shows an error, but the same one at most once a minute.</summary>
    void Report(string message)
    {
        if (message == lastError && DateTime.UtcNow - lastErrorAt < TimeSpan.FromMinutes(1)) return;
        lastError = message;
        lastErrorAt = DateTime.UtcNow;
        Error?.Invoke(message);
    }

    static string Describe(Exception ex) => ex switch
    {
        ServiceException s => s.Message,
        SpotifyClient.ApiException a => a.Message,
        SpotifyClient.NoDeviceException => "OPEN SPOTIFY ON ONE OF YOUR DEVICES",
        HttpRequestException => "NO CONNECTION",
        OperationCanceledException => "SPOTIFY NOT RESPONDING",
        _ => "SPOTIFY ERROR",
    };

    /// <summary>Plays a whole playlist on Spotify; Spotify runs through it by itself.</summary>
    public async Task StartContextAsync(string contextUri)
    {
        volumeRefused = false;
        Active = true;
        playing = true;
        progressMs = 0;
        fetchedAt = DateTime.UtcNow;
        ignoreUntil = DateTime.UtcNow.AddSeconds(2);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await client.PlayContextAsync(contextUri, cts.Token);
    }

    public void Skip(bool forward) => Fire(forward ? client.NextAsync : client.PreviousAsync);

    public async Task StartAsync(IReadOnlyList<string> uris, int offset, int durationHint)
    {
        volumeRefused = false;
        Active = true;
        playing = true;
        progressMs = 0;
        durationMs = durationHint;
        fetchedAt = DateTime.UtcNow;
        ignoreUntil = DateTime.UtcNow.AddSeconds(2);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await client.PlayAsync(uris, offset, cts.Token);
    }

    public void Play()
    {
        Sync();
        playing = true;
        Fire(client.ResumeAsync);
    }

    public void Pause()
    {
        Sync();
        playing = false;
        Fire(client.PauseAsync);
    }

    public void TogglePause()
    {
        if (playing) Pause(); else Play();
    }

    public void Stop()
    {
        Pause();
        Seek(TimeSpan.Zero);
    }

    public void Seek(TimeSpan t)
    {
        progressMs = (int)Math.Clamp(t.TotalMilliseconds, 0, Math.Max(0, durationMs - 500));
        fetchedAt = DateTime.UtcNow;
        int ms = progressMs;
        Fire(ct => client.SeekAsync(ms, ct));
    }

    public void SetShuffle(bool on) => Fire(ct => client.ShuffleAsync(on, ct), quiet: true);
    public void SetRepeat(string mode) => Fire(ct => client.RepeatAsync(mode, ct), quiet: true);

    // Some devices (phones, some speakers) don't let apps change their volume; then we stop asking.
    bool volumeRefused;

    /// <summary>Volume knob → Spotify volume, sent at most a few times a second.</summary>
    public void SetVolume(float v)
    {
        if (volumeRefused) return;
        pendingVolume = (int)Math.Round(v * 100);
        if (volumeBusy) return;
        volumeBusy = true;
        _ = Send();
        async Task Send()
        {
            try
            {
                while (pendingVolume >= 0)
                {
                    int vol = pendingVolume;
                    pendingVolume = -1;
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await client.VolumeAsync(vol, cts.Token);
                    await Task.Delay(250);
                }
            }
            catch (SpotifyClient.ApiException a) when (a.Status == 403)
            {
                volumeRefused = true; // this device's volume can't be controlled from here: stop trying
            }
            catch (Exception)
            {
                // Volume follows the knob in the background; a failure isn't worth interrupting the music for.
            }
            finally
            {
                volumeBusy = false;
            }
        }
    }

    void Sync()
    {
        progressMs = (int)Position.TotalMilliseconds;
        fetchedAt = DateTime.UtcNow;
    }

    /// <summary>Call every frame; asks Spotify what's playing about once a second.</summary>
    public async void Poll()
    {
        if (!Active || polling || (DateTime.UtcNow - lastPoll).TotalSeconds < 1) return;
        polling = true;
        lastPoll = DateTime.UtcNow;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var st = await client.GetStateAsync(cts.Token);
            if (st == null || !Active || DateTime.UtcNow < ignoreUntil) return;
            playing = st.Playing;
            progressMs = st.ProgressMs;
            durationMs = st.DurationMs;
            fetchedAt = DateTime.UtcNow;
            if (st.Uri != null && st.Uri != uri)
            {
                uri = st.Uri;
                TrackChanged?.Invoke(st.Uri, st.Title);
            }
        }
        catch (Exception)
        {
            // Polling is best-effort; commands report their own errors.
        }
        finally
        {
            polling = false;
        }
    }
}

/// <summary>Spotify won't hand this playlist's songs to the app, but it can still play it.</summary>
sealed class PlaylistLockedException(string playlistId) : Exception("PLAYLIST LOCKED")
{
    public string PlaylistId { get; } = playlistId;
}
