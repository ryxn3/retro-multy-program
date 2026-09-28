using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace RetroRadio;

/// <summary>Error from an online music service, worded for the radio's display.</summary>
sealed class ServiceException(string message) : Exception(message);

/// <summary>
/// Browser sign-in (OAuth 2 authorization code + PKCE) with a one-off listener on 127.0.0.1,
/// so the user types their password only on the service's own web page.
/// </summary>
static class OAuthLoopback
{
    public const int Port = 53682;
    public const string RedirectUri = "http://127.0.0.1:53682/callback";

    public static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static (string Verifier, string Challenge, string State) NewPkce()
    {
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge, Base64Url(RandomNumberGenerator.GetBytes(16)));
    }

    /// <summary>Opens the login page in the browser and waits for the redirect. Returns the authorization code.</summary>
    public static async Task<string> AuthorizeAsync(string authorizeUrl, string state, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, Port);
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            throw new ServiceException("PORT 53682 BUSY");
        }
        try
        {
            Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });
            return await WaitForCallbackAsync(listener, state, ct);
        }
        finally
        {
            listener.Stop();
        }
    }

    static async Task<string> WaitForCallbackAsync(TcpListener listener, string expectedState, CancellationToken ct)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            string? requestLine = await reader.ReadLineAsync(ct);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(ct))) { } // skip headers

            var target = requestLine?.Split(' ') is { Length: >= 2 } parts ? parts[1] : "/";
            if (!target.StartsWith("/callback", StringComparison.Ordinal))
            {
                await RespondAsync(stream, 404, "Not found", ct);
                continue;
            }

            var query = System.Web.HttpUtility.ParseQueryString(new Uri("http://127.0.0.1" + target).Query);
            string? error = query["error"], code = query["code"], state = query["state"];
            if (error != null)
            {
                await RespondAsync(stream, 200, "Sign-in was cancelled. You can close this tab.", ct);
                throw new ServiceException("SIGN-IN CANCELLED");
            }
            if (code == null || state != expectedState)
            {
                await RespondAsync(stream, 400, "That sign-in response didn't match. Please try again from the radio.", ct);
                throw new ServiceException("SIGN-IN FAILED");
            }
            await RespondAsync(stream, 200, "Signed in! You can close this tab and go back to the radio.", ct);
            return code;
        }
    }

    static async Task RespondAsync(Stream stream, int status, string message, CancellationToken ct)
    {
        string body = "<!doctype html><meta charset=utf-8><title>Retro Radio</title>" +
                      "<body style=\"background:#0b0d12;color:#6febff;font:20px monospace;display:grid;place-items:center;height:90vh\">" +
                      WebUtility.HtmlEncode(message) + "</body>";
        var bytes = Encoding.UTF8.GetBytes(body);
        string head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.WriteAsync(bytes, ct);
    }
}

/// <summary>What the radio's online-music menus need from a service.</summary>
interface IMusicService
{
    string Name { get; }
    string EntryPrefix { get; }
    bool NeedsSecret { get; }
    string AppsUrl { get; }
    string SetupHelp { get; }
    string ClientId { get; }
    bool HasKeys { get; }
    bool SignedIn { get; }
    bool CanBrowseSignedOut { get; }
    string? Username { get; }

    void SetKeys(string clientId, string clientSecret);
    void SignOut();
    Task SignInAsync(CancellationToken ct);

    Task<ScPage<ScTrack>> SearchTracksAsync(string q, CancellationToken ct);
    Task<ScPage<ScPlaylist>> SearchPlaylistsAsync(string q, CancellationToken ct);
    Task<ScPage<ScPlaylist>> MyPlaylistsAsync(CancellationToken ct);
    Task<ScPage<ScTrack>> MyLikesAsync(CancellationToken ct);
    Task<ScPage<ScTrack>> PlaylistTracksAsync(string playlistId, CancellationToken ct);
    Task<ScPage<ScTrack>> MoreTracksAsync(string next, CancellationToken ct);
    Task<ScPage<ScPlaylist>> MorePlaylistsAsync(string next, CancellationToken ct);
}

/// <summary>Transport controls shared by the local audio engine and the Spotify remote.</summary>
interface IPlayback
{
    bool IsLoaded { get; }
    bool IsPlaying { get; }
    bool IsPaused { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    void Play();
    void Pause();
    void TogglePause();
    void Stop();
    void Seek(TimeSpan t);
}
