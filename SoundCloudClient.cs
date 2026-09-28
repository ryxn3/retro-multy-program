using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RetroRadio;

sealed record ScTrack(string Id, string Title, string Artist, int DurationMs, string Access, string? ArtUrl = null);
sealed record ScPlaylist(string Id, string Title, int TrackCount);
sealed record ScPage<T>(List<T> Items, string? Next);

/// <summary>
/// Talks to the official SoundCloud API. Needs the user's own registered app (Client ID + Secret).
/// Sign-in uses OAuth 2.1 with PKCE: SoundCloud's login page opens in the browser and redirects
/// back to a one-off listener on 127.0.0.1, so the radio never sees the user's password.
/// </summary>
sealed class SoundCloudClient : IMusicService
{
    public string Name => "SOUNDCLOUD";
    public string EntryPrefix => "sc://";
    public bool NeedsSecret => true;
    public bool CanBrowseSignedOut => true;
    public string AppsUrl => "https://soundcloud.com/you/apps";
    public string SetupHelp =>
        "SoundCloud only lets registered apps connect. It's free and takes a minute:\n\n" +
        "1. Open the SoundCloud apps page below and register an app.\n" +
        "2. Set its Redirect URI to exactly the address shown below.\n" +
        "3. Paste the app's Client ID and Client Secret here.";

    const string AuthBase = "https://secure.soundcloud.com";
    const string ApiBase = "https://api.soundcloud.com";

    static string StorePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroRadio", "soundcloud.dat");

    readonly HttpClient api = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly HttpClient noRedirect = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

    string? accessToken, refreshToken;
    DateTime accessExpires;
    string? appToken;
    DateTime appExpires;

    public string ClientId { get; private set; } = "";
    public string ClientSecret { get; private set; } = "";
    public string? Username { get; private set; }

    public bool HasKeys => ClientId.Length > 0 && ClientSecret.Length > 0;
    public bool SignedIn => refreshToken != null || (accessToken != null && DateTime.UtcNow < accessExpires);

    public SoundCloudClient() => Load();

    // ───────────────────────────── storage (encrypted with Windows DPAPI) ─────────────────────────────

    void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(StorePath), null, DataProtectionScope.CurrentUser);
            var j = JsonNode.Parse(plain)!;
            ClientId = (string?)j["clientId"] ?? "";
            ClientSecret = (string?)j["clientSecret"] ?? "";
            refreshToken = (string?)j["refreshToken"];
            Username = (string?)j["username"];
        }
        catch (Exception)
        {
            // Unreadable (e.g. copied from another Windows account): start fresh.
        }
    }

    void Save()
    {
        var j = new JsonObject
        {
            ["clientId"] = ClientId,
            ["clientSecret"] = ClientSecret,
            ["refreshToken"] = refreshToken,
            ["username"] = Username,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(j.ToJsonString()), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(StorePath, cipher);
    }

    public void SetKeys(string clientId, string clientSecret)
    {
        ClientId = clientId.Trim();
        ClientSecret = clientSecret.Trim();
        accessToken = refreshToken = appToken = null;
        Username = null;
        Save();
    }

    public void SignOut()
    {
        accessToken = refreshToken = null;
        Username = null;
        Save();
    }

    // ───────────────────────────── sign in ─────────────────────────────

    public async Task SignInAsync(CancellationToken ct)
    {
        if (!HasKeys) throw new ServiceException("SET UP APP KEYS FIRST");
        var (verifier, challenge, state) = OAuthLoopback.NewPkce();
        string url = $"{AuthBase}/authorize?client_id={Uri.EscapeDataString(ClientId)}&redirect_uri={Uri.EscapeDataString(OAuthLoopback.RedirectUri)}" +
                     $"&response_type=code&code_challenge={challenge}&code_challenge_method=S256&state={state}";
        string code = await OAuthLoopback.AuthorizeAsync(url, state, ct);

        await TokenRequestAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["redirect_uri"] = OAuthLoopback.RedirectUri,
            ["code_verifier"] = verifier,
            ["code"] = code,
        }, ct);

        var me = await GetAsync("/me", ct);
        Username = (string?)me["username"];
        Save();
    }

    // ───────────────────────────── tokens ─────────────────────────────

    async Task TokenRequestAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, AuthBase + "/oauth/token") { Content = new FormUrlEncodedContent(form) };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var res = await api.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new ServiceException($"LOGIN ERROR {(int)res.StatusCode}");
        var j = JsonNode.Parse(text)!;
        accessToken = (string?)j["access_token"];
        refreshToken = (string?)j["refresh_token"] ?? refreshToken;
        accessExpires = DateTime.UtcNow.AddSeconds(((int?)j["expires_in"] ?? 3600) - 60);
    }

    async Task<string> TokenAsync(CancellationToken ct)
    {
        if (accessToken != null && DateTime.UtcNow < accessExpires) return accessToken;
        if (refreshToken != null)
        {
            try
            {
                await TokenRequestAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = ClientId,
                    ["client_secret"] = ClientSecret,
                    ["refresh_token"] = refreshToken,
                }, ct);
                Save();
                return accessToken!;
            }
            catch (ServiceException)
            {
                // Refresh token expired or revoked: the user needs to sign in again.
                refreshToken = null;
                Username = null;
                Save();
                throw new ServiceException("PLEASE SIGN IN AGAIN");
            }
        }

        // Not signed in: an app-only token still allows searching and playing public tracks.
        if (!HasKeys) throw new ServiceException("SET UP APP KEYS FIRST");
        if (appToken != null && DateTime.UtcNow < appExpires) return appToken;
        using var req = new HttpRequestMessage(HttpMethod.Post, AuthBase + "/oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}")));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var res = await api.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new ServiceException($"APP KEYS REJECTED {(int)res.StatusCode}");
        var j = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))!;
        appToken = (string?)j["access_token"];
        appExpires = DateTime.UtcNow.AddSeconds(((int?)j["expires_in"] ?? 3600) - 60);
        return appToken!;
    }

    // ───────────────────────────── API ─────────────────────────────

    async Task<JsonNode> GetAsync(string pathOrUrl, CancellationToken ct)
    {
        string url = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? pathOrUrl : ApiBase + pathOrUrl;
        for (int attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", await TokenAsync(ct));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var res = await api.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                accessToken = appToken = null; // force a refresh and retry once
                continue;
            }
            if (!res.IsSuccessStatusCode) throw new ServiceException($"SOUNDCLOUD ERROR {(int)res.StatusCode}");
            return JsonNode.Parse(await res.Content.ReadAsStringAsync(ct)) ?? new JsonObject();
        }
    }

    static ScPage<T> ParsePage<T>(JsonNode root, Func<JsonNode, T?> parse) where T : class
    {
        var arr = root as JsonArray ?? root["collection"] as JsonArray ?? new JsonArray();
        var items = arr.Where(n => n != null).Select(n => parse(n!)).Where(x => x != null).Select(x => x!).ToList();
        return new ScPage<T>(items, root is JsonObject ? (string?)root["next_href"] : null);
    }

    static string IdOf(JsonNode n) => (string?)n["urn"] ?? n["id"]?.ToString() ?? "";

    static ScTrack? ParseTrack(JsonNode n)
    {
        // Likes can come wrapped as { "track": {...} }.
        if (n["track"] is JsonNode inner) n = inner;
        if (n["kind"] is JsonNode k && (string?)k is { } kind && kind != "track") return null;
        string id = IdOf(n);
        if (id.Length == 0) return null;
        // Cover art: the track's own, or the uploader's picture; ask for the big 500×500 version.
        string? art = ((string?)n["artwork_url"] ?? (string?)n["user"]?["avatar_url"])?.Replace("-large.", "-t500x500.");
        return new ScTrack(id, (string?)n["title"] ?? "?", (string?)n["user"]?["username"] ?? "", (int?)n["duration"] ?? 0, (string?)n["access"] ?? "playable", art);
    }

    static ScPlaylist? ParsePlaylist(JsonNode n)
    {
        if (n["playlist"] is JsonNode inner) n = inner;
        string id = IdOf(n);
        return id.Length == 0 ? null : new ScPlaylist(id, (string?)n["title"] ?? "?", (int?)n["track_count"] ?? 0);
    }

    static string Q(string s) => Uri.EscapeDataString(s);

    public async Task<ScPage<ScTrack>> SearchTracksAsync(string q, CancellationToken ct) =>
        ParsePage(await GetAsync($"/tracks?q={Q(q)}&limit=40&linked_partitioning=true", ct), ParseTrack);

    public async Task<ScPage<ScPlaylist>> SearchPlaylistsAsync(string q, CancellationToken ct) =>
        ParsePage(await GetAsync($"/playlists?q={Q(q)}&limit=40&linked_partitioning=true&show_tracks=false", ct), ParsePlaylist);

    public async Task<ScPage<ScPlaylist>> MyPlaylistsAsync(CancellationToken ct) =>
        ParsePage(await GetAsync("/me/playlists?limit=50&linked_partitioning=true&show_tracks=false", ct), ParsePlaylist);

    public async Task<ScPage<ScTrack>> MyLikesAsync(CancellationToken ct) =>
        ParsePage(await GetAsync("/me/likes/tracks?limit=50&linked_partitioning=true", ct), ParseTrack);

    public async Task<ScPage<ScTrack>> PlaylistTracksAsync(string playlistId, CancellationToken ct) =>
        ParsePage(await GetAsync($"/playlists/{playlistId}/tracks?limit=200&linked_partitioning=true", ct), ParseTrack);

    public async Task<ScPage<ScTrack>> MoreTracksAsync(string next, CancellationToken ct) => ParsePage(await GetAsync(next, ct), ParseTrack);
    public async Task<ScPage<ScPlaylist>> MorePlaylistsAsync(string next, CancellationToken ct) => ParsePage(await GetAsync(next, ct), ParsePlaylist);

    /// <summary>Finds a playable stream for a track. Returns the final media URL and whether it's HLS.</summary>
    public async Task<(string Url, bool Hls, bool Preview)> ResolveStreamAsync(string trackId, CancellationToken ct)
    {
        var j = await GetAsync($"/tracks/{trackId}/streams", ct);
        string? mp3 = (string?)j["http_mp3_128_url"];
        string? hls = (string?)j["hls_aac_160_url"] ?? (string?)j["hls_mp3_128_url"] ?? (string?)j["hls_opus_64_url"];
        string? preview = (string?)j["preview_mp3_128_url"];
        if (mp3 != null) return (await ResolveRedirectAsync(mp3, ct), false, false);
        if (hls != null) return (await ResolveRedirectAsync(hls, ct), true, false);
        if (preview != null) return (await ResolveRedirectAsync(preview, ct), false, true);
        throw new ServiceException("TRACK NOT PLAYABLE");
    }

    /// <summary>Stream links answer with a redirect to a signed CDN URL that needs no auth header.</summary>
    async Task<string> ResolveRedirectAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", await TokenAsync(ct));
        using var res = await noRedirect.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if ((int)res.StatusCode is >= 300 and < 400 && res.Headers.Location is Uri loc)
            return loc.IsAbsoluteUri ? loc.ToString() : new Uri(new Uri(url), loc).ToString();
        if (res.IsSuccessStatusCode && res.Content.Headers.ContentType?.MediaType?.Contains("json") == true)
        {
            var j = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
            if ((string?)j?["url"] is { } u) return u;
        }
        if (!res.IsSuccessStatusCode) throw new ServiceException($"STREAM ERROR {(int)res.StatusCode}");
        return url;
    }
}
