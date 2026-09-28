using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RetroLauncher;

sealed record ReleaseAsset(long Id, string Name, long Size, DateTimeOffset Updated, string Url);
sealed record Release(string Tag, string Title, string Notes, DateTimeOffset Published, List<ReleaseAsset> Assets)
{
    public ReleaseAsset? Asset(string name) => Assets.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Reads the latest release of the GitHub repo that hosts the programs.</summary>
static class GitHub
{
    public const string Owner = "ryxn3";
    public const string Repo = "retro-multy-program";
    public static string RepoUrl => $"https://github.com/{Owner}/{Repo}";

    public static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RetroLauncher", Program.Version.ToString()));
        return c;
    }

    /// <summary>The newest published release, or null if the repo has none yet.</summary>
    public static async Task<Release?> LatestAsync(CancellationToken ct)
    {
        // RMP_RELEASE_URL points the launcher at a stand-in release (used for testing before publishing).
        string url = Environment.GetEnvironmentVariable("RMP_RELEASE_URL") ?? $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var res = await Http.SendAsync(req, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if ((int)res.StatusCode == 403) throw new InvalidOperationException("GitHub is limiting requests right now — try again in a few minutes.");
        res.EnsureSuccessStatusCode();
        var j = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))!;
        var assets = (j["assets"] as JsonArray ?? []).Where(a => a != null).Select(a => new ReleaseAsset(
            (long?)a!["id"] ?? 0,
            (string?)a["name"] ?? "",
            (long?)a["size"] ?? 0,
            DateTimeOffset.TryParse((string?)a["updated_at"], out var u) ? u : DateTimeOffset.MinValue,
            (string?)a["browser_download_url"] ?? "")).ToList();
        return new Release(
            (string?)j["tag_name"] ?? "",
            (string?)j["name"] ?? "",
            (string?)j["body"] ?? "",
            DateTimeOffset.TryParse((string?)j["published_at"], out var p) ? p : DateTimeOffset.MinValue,
            assets);
    }

    /// <summary>"v1.2.3" → 1.2.3 (unparseable tags count as 0.0).</summary>
    public static Version ParseTag(string tag) =>
        Version.TryParse(tag.TrimStart('v', 'V').Split('-')[0], out var v) ? v : new Version(0, 0);

    /// <summary>Downloads a file, reporting (bytes so far, total).</summary>
    public static async Task DownloadAsync(string url, string dest, IProgress<(long Done, long Total)> progress, CancellationToken ct)
    {
        using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        long total = res.Content.Headers.ContentLength ?? 0;
        await using var src = await res.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        var buf = new byte[1 << 16];
        long done = 0;
        var sw = Stopwatch.StartNew();
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            done += n;
            if (sw.ElapsedMilliseconds > 80)
            {
                progress.Report((done, total));
                sw.Restart();
            }
        }
        progress.Report((done, total));
    }
}

/// <summary>One program the launcher can install.</summary>
sealed record AppInfo(string Id, string Name, string Tagline, string Description, string AssetName, string Exe, string Image);

/// <summary>What's installed where. Programs live under %LocalAppData%\RetroMultyProgram\apps\&lt;id&gt;.</summary>
sealed class Installer
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroMultyProgram");
    static string AppsDir => Path.Combine(Root, "apps");

    public static string DirOf(AppInfo app) => Path.Combine(AppsDir, app.Id);
    static string InfoFile(AppInfo app) => Path.Combine(DirOf(app), "installed.json");

    public sealed record InstalledInfo(string Tag, long AssetId, DateTimeOffset Updated, string ExePath);

    public static InstalledInfo? Installed(AppInfo app)
    {
        try
        {
            if (!File.Exists(InfoFile(app))) return null;
            var info = JsonSerializer.Deserialize<InstalledInfo>(File.ReadAllText(InfoFile(app)));
            return info != null && File.Exists(info.ExePath) ? info : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool IsRunning(AppInfo app) =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(app.Exe)).Any(p =>
        {
            try { return p.MainModule?.FileName?.StartsWith(DirOf(app), StringComparison.OrdinalIgnoreCase) == true; }
            catch (Exception) { return false; }
        });

    public static void CloseRunning(AppInfo app)
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(app.Exe)))
        {
            try
            {
                if (p.MainModule?.FileName?.StartsWith(DirOf(app), StringComparison.OrdinalIgnoreCase) != true) continue;
                p.CloseMainWindow();
                if (!p.WaitForExit(8000)) p.Kill();
            }
            catch (Exception) { }
        }
    }

    /// <summary>Downloads the program's zip from the release and swaps it in.</summary>
    public static async Task InstallAsync(AppInfo app, Release release, IProgress<(long, long)> progress, CancellationToken ct)
    {
        var asset = release.Asset(app.AssetName) ?? throw new InvalidOperationException($"The release has no {app.AssetName}.");
        Directory.CreateDirectory(AppsDir);
        string tmpZip = Path.Combine(Path.GetTempPath(), $"rmp-{app.Id}-{Guid.NewGuid():N}.zip");
        string staging = DirOf(app) + ".new";
        try
        {
            await GitHub.DownloadAsync(asset.Url, tmpZip, progress, ct);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            await Task.Run(() => ZipFile.ExtractToDirectory(tmpZip, staging), ct);

            string? exe = Directory.EnumerateFiles(staging, app.Exe, SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidOperationException($"{app.Exe} wasn't in the download.");

            if (Directory.Exists(DirOf(app))) Directory.Delete(DirOf(app), true);
            Directory.Move(staging, DirOf(app));
            exe = Path.Combine(DirOf(app), Path.GetRelativePath(staging, exe));
            File.WriteAllText(InfoFile(app), JsonSerializer.Serialize(new InstalledInfo(release.Tag, asset.Id, asset.Updated, exe)));
        }
        finally
        {
            try { File.Delete(tmpZip); } catch (IOException) { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { }
        }
    }

    public static void Uninstall(AppInfo app)
    {
        if (Directory.Exists(DirOf(app))) Directory.Delete(DirOf(app), true);
    }

    public static void Launch(AppInfo app)
    {
        var info = Installed(app) ?? throw new InvalidOperationException("Not installed.");
        Process.Start(new ProcessStartInfo(info.ExePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(info.ExePath)! });
    }

    /// <summary>Puts a shortcut on the desktop.</summary>
    public static void DesktopShortcut(AppInfo app)
    {
        var info = Installed(app) ?? throw new InvalidOperationException("Not installed.");
        string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), app.Name + ".lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Shortcuts aren't available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic sc = shell.CreateShortcut(lnk);
        sc.TargetPath = info.ExePath;
        sc.WorkingDirectory = Path.GetDirectoryName(info.ExePath);
        sc.Description = app.Tagline;
        sc.Save();
    }

    /// <summary>
    /// Replaces the running launcher with a newer one: downloads it, then a tiny script swaps the
    /// exe once this process has exited and starts it again.
    /// </summary>
    public static async Task SelfUpdateAsync(Release release, string assetName, IProgress<(long, long)> progress, CancellationToken ct)
    {
        var asset = release.Asset(assetName) ?? throw new InvalidOperationException("No launcher in this release.");
        string me = Environment.ProcessPath ?? throw new InvalidOperationException("Can't find the launcher's own file.");
        string work = Path.Combine(Path.GetTempPath(), "rmp-launcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string zip = Path.Combine(work, "launcher.zip");
        await GitHub.DownloadAsync(asset.Url, zip, progress, ct);
        ZipFile.ExtractToDirectory(zip, Path.Combine(work, "x"));
        string newExe = Directory.EnumerateFiles(Path.Combine(work, "x"), "RetroLauncher.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("The download had no RetroLauncher.exe.");

        string script = Path.Combine(work, "update.cmd");
        File.WriteAllText(script,
            "@echo off\r\n" +
            $":wait\r\ntasklist /FI \"PID eq {Environment.ProcessId}\" | find \"{Environment.ProcessId}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
            $"copy /y \"{newExe}\" \"{me}\" >nul\r\n" +
            $"start \"\" \"{me}\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false });
    }
}
