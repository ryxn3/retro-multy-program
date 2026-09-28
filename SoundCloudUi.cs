namespace RetroRadio;

/// <summary>Online music menus (SoundCloud or Spotify), shown on the radio display like the settings menu.</summary>
sealed partial class RadioForm
{
    readonly SoundCloudClient sc = new();
    readonly SpotifyClient spotifyClient = new();
    SpotifyRemote spotify = null!;
    int serviceIdx;
    IMusicService svc => serviceIdx == 1 ? spotifyClient : sc;
    static readonly string[] ServiceNames = ["SOUNDCLOUD", "SPOTIFY"];

    void InitServices()
    {
        spotify = new SpotifyRemote(spotifyClient);
        spotify.Error += msg => Flash(msg, 3);
        spotify.TrackChanged += (uri, name) =>
        {
            // Spotify moved on by itself: follow it in the queue.
            int idx = tracks.FindIndex(t => IsSp(t) && ParseSc(t).Id == uri);
            if (idx >= 0) current = idx;
            title = name.Length > 0 ? name : idx >= 0 ? TitleOf(tracks[idx]) : title;
            marqueeStart = now;
        };
    }

    /// <summary>The deck currently in charge: the local engine, or Spotify on remote control.</summary>
    IPlayback player => spotify.Active ? spotify : engine;

    /// <summary>Stops remote-controlling Spotify (pausing it) before the radio plays something itself.</summary>
    void LeaveSpotify()
    {
        if (!spotify.Active) return;
        spotify.Pause();
        spotify.Active = false;
        engine.Loopback = false;
    }
    bool scMode, scTyping, scBusy;
    string scInput = "", scSearchKind = "TRACKS";
    string scTitle = "SOUNDCLOUD";
    List<ScRow> scRows = new();
    int scCursor, scTop;
    readonly Stack<(string Title, List<ScRow> Rows, int Cursor)> scBack = new();
    CancellationTokenSource? scCts;
    int playToken;

    sealed record ScRow(string Text, string Right, Func<Task>? Pick);

    // Queue entries for online tracks look like: sc://<id>/<duration ms>/<escaped "artist - title"> (sp:// for Spotify).
    static bool IsSc(string entry) => entry.StartsWith("sc://", StringComparison.Ordinal);
    static bool IsSp(string entry) => entry.StartsWith("sp://", StringComparison.Ordinal);
    static bool IsOnline(string entry) => IsSc(entry) || IsSp(entry) || RadioBrowser.IsEntry(entry);

    /// <summary>True while Spotify plays a whole playlist by itself; skips go to Spotify.</summary>
    bool InSpotifyPlaylist => spotify.Active && current >= 0 && current < tracks.Count && IsSp(tracks[current])
        && ParseSc(tracks[current]).Id.StartsWith("spotify:playlist:", StringComparison.Ordinal);

    string ScEntry(ScTrack t)
    {
        if (t.ArtUrl != null) TrackMeta.OnlineArt[t.Id] = t.ArtUrl; // for the modern screens' cover art
        return $"{svc.EntryPrefix}{t.Id}/{t.DurationMs}/{Uri.EscapeDataString(t.Artist.Length > 0 ? $"{t.Artist} - {t.Title}" : t.Title)}";
    }

    static (string Id, int DurationMs, string Title) ParseSc(string entry)
    {
        var parts = entry["sc://".Length..].Split('/', 3);
        return (parts[0], parts.Length > 1 && int.TryParse(parts[1], out var d) ? d : 0, parts.Length > 2 ? Uri.UnescapeDataString(parts[2]) : "ONLINE");
    }

    static string Dur(int ms) => $"{ms / 60000}:{ms / 1000 % 60:00}";

    // ───────────────────────────── navigation ─────────────────────────────

    void OpenSoundCloud()
    {
        if (BlockedOffline()) return;
        scMode = true;
        listMode = settingsMode = false;
        scTyping = false;
        scBack.Clear();
        ShowScMenu();
    }

    void ShowScMenu()
    {
        var rows = new List<ScRow>();
        if (!svc.HasKeys)
        {
            rows.Add(new(svc.NeedsSecret ? "SET UP APP KEYS" : "SET UP APP KEY", "START HERE", SetUpKeysAsync));
        }
        else
        {
            if (svc.SignedIn || svc.CanBrowseSignedOut)
            {
                rows.Add(new("SEARCH TRACKS", "", () => BeginTyping("TRACKS")));
                rows.Add(new("SEARCH PLAYLISTS", "", () => BeginTyping("PLAYLISTS")));
            }
            if (svc.SignedIn)
            {
                rows.Add(new("MY PLAYLISTS", "", LoadMyPlaylistsAsync));
                rows.Add(new("MY LIKES", "", LoadMyLikesAsync));
                rows.Add(new("SIGN OUT", svc.Username ?? "", () =>
                {
                    svc.SignOut();
                    ShowScMenu();
                    Flash("SIGNED OUT");
                    return Task.CompletedTask;
                }));
            }
            else rows.Add(new("SIGN IN", "OPENS BROWSER", SignInAsync));
            rows.Add(new("APP KEYS", "CHANGE", SetUpKeysAsync));
        }
        rows.Insert(0, new("INTERNET RADIO", "", () => { OpenInternetRadio(); return Task.CompletedTask; }));
        rows.Add(new("SWITCH SERVICE", ServiceNames[1 - serviceIdx], () =>
        {
            serviceIdx = 1 - serviceIdx;
            ShowScMenu();
            return Task.CompletedTask;
        }));
        rows.Add(new("EXIT", "", () =>
        {
            scMode = false;
            return Task.CompletedTask;
        }));
        scTitle = svc.SignedIn ? $"{svc.Name} - {svc.Username}" : svc.Name;
        scRows = rows;
        scCursor = Math.Clamp(scCursor, 0, rows.Count - 1);
        scTop = 0;
    }

    void PushView(string title, List<ScRow> rows)
    {
        scBack.Push((scTitle, scRows, scCursor));
        scTitle = title;
        scRows = rows;
        scCursor = scTop = 0;
    }

    void ScGoBack()
    {
        if (scTyping)
        {
            scTyping = false;
            return;
        }
        if (scBack.Count > 0)
        {
            (scTitle, scRows, scCursor) = scBack.Pop();
            scTop = 0;
        }
        else scMode = false;
    }

    async void ScPick()
    {
        if (scBusy || scCursor < 0 || scCursor >= scRows.Count) return;
        var pick = scRows[scCursor].Pick;
        if (pick == null) return;
        scBusy = true;
        scCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await pick();
        }
        catch (OperationCanceledException)
        {
            Flash("TIMED OUT");
        }
        catch (Exception ex)
        {
            Flash(ScError(ex), 3);
        }
        finally
        {
            scBusy = false;
        }
    }

    static string ScError(Exception ex) => ex switch
    {
        ServiceException s => s.Message,
        HttpRequestException => "NO CONNECTION",
        _ => "SOUNDCLOUD ERROR",
    };

    CancellationToken ScToken => scCts?.Token ?? CancellationToken.None;

    Task BeginTyping(string kind)
    {
        scSearchKind = kind;
        scInput = "";
        scTyping = true;
        return Task.CompletedTask;
    }

    async void SubmitSearch()
    {
        string q = scInput.Trim();
        scTyping = false;
        if (q.Length == 0 || scBusy) return;
        scBusy = true;
        scCts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try
        {
            if (scSearchKind == "STATIONS")
            {
                scBusy = false;
                await LoadStationsAsync($"{Lang.T("SEARCH")}: {q}", "search", q);
            }
            else if (scSearchKind == "TRACKS")
            {
                var page = await svc.SearchTracksAsync(q, ScToken);
                PushView($"SEARCH: {q}", TrackRows(page.Items, page.Next));
            }
            else
            {
                var page = await svc.SearchPlaylistsAsync(q, ScToken);
                PushView($"PLAYLISTS: {q}", PlaylistRows(page.Items, page.Next));
            }
            if (scRows.Count == 0) Flash("NOTHING FOUND");
        }
        catch (Exception ex)
        {
            Flash(ScError(ex), 3);
        }
        finally
        {
            scBusy = false;
        }
    }

    // ───────────────────────────── actions ─────────────────────────────

    Task SetUpKeysAsync()
    {
        using var dlg = new SoundCloudKeysDialog(svc);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            svc.SetKeys(dlg.ClientId, dlg.ClientSecret);
            Flash("KEYS SAVED");
        }
        ShowScMenu();
        return Task.CompletedTask;
    }

    async Task SignInAsync()
    {
        Flash("CHECK YOUR BROWSER", 180);
        await svc.SignInAsync(ScToken);
        ShowScMenu();
        Flash($"{Lang.Hello} {svc.Username}", 2.5);
    }

    async Task LoadMyPlaylistsAsync()
    {
        var page = await svc.MyPlaylistsAsync(ScToken);
        PushView("MY PLAYLISTS", PlaylistRows(page.Items, page.Next));
    }

    async Task LoadMyLikesAsync()
    {
        var page = await svc.MyLikesAsync(ScToken);
        PushView("MY LIKES", TrackRows(page.Items, page.Next));
    }

    List<ScRow> TrackRows(List<ScTrack> items, string? next)
    {
        var rows = new List<ScRow>();
        if (items.Count > 0) rows.Add(new($"{DotFont.Play} PLAY ALL", $"{items.Count} TRK", () => PlayScTracks(items, 0)));
        for (int i = 0; i < items.Count; i++)
        {
            var t = items[i];
            int index = i;
            string right = t.Access switch { "blocked" => "BLOCKED", "preview" => "PREVIEW", _ => Dur(t.DurationMs) };
            string name = t.Artist.Length > 0 ? $"{t.Artist} - {t.Title}" : t.Title;
            rows.Add(new(name, right, () => PlayScTracks(items, index)));
        }
        if (next != null)
        {
            rows.Add(new("LOAD MORE...", "", async () =>
            {
                var more = await svc.MoreTracksAsync(next, ScToken);
                int keep = scCursor;
                scRows = TrackRows([.. items, .. more.Items], more.Next);
                scCursor = keep;
            }));
        }
        return rows;
    }

    List<ScRow> PlaylistRows(List<ScPlaylist> items, string? next)
    {
        var rows = items.Select(p => new ScRow(p.Title, $"{p.TrackCount} TRK", async () =>
        {
            ScPage<ScTrack> page;
            try
            {
                page = await svc.PlaylistTracksAsync(p.Id, ScToken);
            }
            catch (PlaylistLockedException)
            {
                // Spotify hides this playlist's song list from the app, but the Spotify app can play it whole.
                tracks.Clear();
                tracks.Add($"sp://spotify:playlist:{p.Id}/0/{Uri.EscapeDataString(p.Title)}");
                scMode = false;
                PlayIndex(0);
                return;
            }
            PushView(p.Title, TrackRows(page.Items, page.Next));
        })).ToList();
        if (next != null)
        {
            rows.Add(new("LOAD MORE...", "", async () =>
            {
                var more = await svc.MorePlaylistsAsync(next, ScToken);
                int keep = scCursor;
                scRows = PlaylistRows([.. items, .. more.Items], more.Next);
                scCursor = keep;
            }));
        }
        return rows;
    }

    /// <summary>Replaces the queue with these SoundCloud tracks and starts at the chosen one.</summary>
    Task PlayScTracks(List<ScTrack> items, int start)
    {
        var playable = items.Where(t => t.Access != "blocked").ToList();
        if (playable.Count == 0)
        {
            Flash("NOT PLAYABLE");
            return Task.CompletedTask;
        }
        int first = Math.Max(0, playable.IndexOf(items[start]));
        tracks.Clear();
        tracks.AddRange(playable.Select(ScEntry));
        scMode = false;
        PlayIndex(first);
        return Task.CompletedTask;
    }

    /// <summary>Resolves and starts a SoundCloud stream; later requests win over earlier ones.</summary>
    async void PlayRemote(int index)
    {
        LeaveSpotify();
        int token = ++playToken;
        var (id, durationMs, _) = ParseSc(tracks[index]);
        engine.Eject();
        stopped = false;
        Flash("CONNECTING...", 30);
        try
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var (url, hls, preview) = await sc.ResolveStreamAsync(id, cts.Token);
            var length = TimeSpan.FromMilliseconds(durationMs);
            string? ffmpeg = hls ? FfmpegLocator.Find() ?? throw new ServiceException("NEEDS FFMPEG FOR THIS TRACK") : null;
            ITrackSource src = await Task.Run(() => hls
                ? new FfmpegTrackSource(ffmpeg!, url, length)
                : (ITrackSource)new UrlTrackSource(url, preview ? TimeSpan.FromSeconds(30) : length));
            if (token != playToken)
            {
                src.Dispose();
                return;
            }
            engine.Load(src);
            player.Play();
            Flash(preview ? "30 SEC PREVIEW ONLY" : "PLAYING", 1.2);
        }
        catch (Exception ex)
        {
            if (token != playToken) return;
            stopped = true;
            Flash(ScError(ex), 3);
        }
    }

    /// <summary>Starts the queue's Spotify tracks on the Spotify app, beginning at this one.</summary>
    async void PlaySpotify(int index)
    {
        int token = ++playToken;
        engine.Eject();
        stopped = false;
        Flash("CONNECTING...", 30);
        if (ParseSc(tracks[index]).Id.StartsWith("spotify:playlist:", StringComparison.Ordinal))
        {
            try
            {
                await spotify.StartContextAsync(ParseSc(tracks[index]).Id);
                if (token != playToken) return;
                spotify.SetShuffle(shuffle);
                spotify.SetRepeat(repeat switch { RepeatMode.One => "track", RepeatMode.All => "context", _ => "off" });
                spotify.SetVolume(engine.Muted ? 0 : engine.Volume);
                Flash("WHOLE PLAYLIST ON SPOTIFY", 2);
            }
            catch (Exception ex)
            {
                if (token != playToken) return;
                spotify.Active = false;
                stopped = true;
                Flash(ScError(ex), 4);
            }
            return;
        }
        // Hand Spotify the Spotify tracks around this one (it plays on through them by itself).
        var spIdx = Enumerable.Range(0, tracks.Count).Where(i => IsSp(tracks[i])).ToList();
        int at = spIdx.IndexOf(index);
        int from = Math.Max(0, at - 50);
        var window = spIdx.Skip(from).Take(150).Select(i => ParseSc(tracks[i]).Id).ToList();
        try
        {
            await spotify.StartAsync(window, at - from, ParseSc(tracks[index]).DurationMs);
            if (token != playToken) return;
            spotify.SetShuffle(false); // the radio picks shuffled tracks itself
            spotify.SetRepeat(repeat switch { RepeatMode.One => "track", RepeatMode.All => "context", _ => "off" });
            spotify.SetVolume(engine.Muted ? 0 : engine.Volume);
            Flash("PLAYING ON SPOTIFY", 1.5);
        }
        catch (Exception ex)
        {
            if (token != playToken) return;
            spotify.Active = false;
            stopped = true;
            Flash(ScError(ex), 3);
        }
    }

    // ───────────────────────────── input ─────────────────────────────

    /// <summary>Keys while the SoundCloud menu is open. Returns true when handled.</summary>
    bool ScKey(Keys key)
    {
        if (scTyping)
        {
            switch (key)
            {
                case Keys.Enter: SubmitSearch(); return true;
                case Keys.Escape: scTyping = false; return true;
                case Keys.Back:
                    if (scInput.Length > 0) scInput = scInput[..^1];
                    return true;
            }
            return false; // let the character arrive through OnKeyPress
        }
        switch (key)
        {
            case Keys.Up: scCursor = Math.Max(0, scCursor - 1); return true;
            case Keys.Down: scCursor = Math.Min(scRows.Count - 1, scCursor + 1); return true;
            case Keys.Enter: case Keys.Right: ScPick(); return true;
            case Keys.Escape: case Keys.Left: case Keys.Back: ScGoBack(); return true;
        }
        return true; // swallow other shortcuts while browsing
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (searching && !char.IsControl(e.KeyChar) && listFilter.Length < 40)
        {
            listFilter += e.KeyChar;
            listCursor = listTop = 0;
            e.Handled = true;
            return;
        }
        if (scMode && scTyping && !char.IsControl(e.KeyChar) && scInput.Length < 48)
        {
            scInput += e.KeyChar;
            e.Handled = true;
        }
    }

    bool ScClick(PointF p, bool right)
    {
        if (right)
        {
            ScGoBack();
            return true;
        }
        if (scTyping)
        {
            SubmitSearch();
            return true;
        }
        if (MenuRowAt(p.Y) < 0) return false;
        int idx = scTop + MenuRowAt(p.Y);
        if (idx >= scRows.Count) return true;
        // First click selects, clicking the selected row opens it.
        if (idx == scCursor) ScPick();
        else scCursor = idx;
        return true;
    }

    // ───────────────────────────── drawing ─────────────────────────────

    string ScMarquee() => scBusy ? "LOADING" + new string('.', (int)(now * 3) % 4)
        : scTyping ? $"TYPE TO SEARCH {scSearchKind} - ENTER TO GO"
        : scTitle;

    void DrawSoundCloud(Graphics g, Inks ink)
    {
        const float top = 64, rowH = 17, p = 1.8f;
        if (scTyping)
        {
            string label = $"SEARCH {scSearchKind}:";
            DotText(g, ink.Accent, label, 20, 80, 2f);
            string text = DotFont.Normalize(scInput);
            const float tp = 2.6f;
            int maxChars = (int)((GW - 60) / (6 * tp));
            if (text.Length > maxChars) text = text[^maxChars..];
            bool caret = (now % 1.0) < 0.55;
            g.DrawRectangle(ink.MainPen, 16, 106, GW - 32, 7 * tp + 14);
            DotText(g, ink.Main, text + (caret ? "_" : " "), 24, 113, tp);
            DotText(g, ink.Main, "ENTER = SEARCH   ESC = BACK", 20, 160, 1.6f);
            return;
        }

        scCursor = Math.Clamp(scCursor, 0, Math.Max(0, scRows.Count - 1));
        if (scCursor < scTop) scTop = scCursor;
        if (scCursor >= scTop + ListRows) scTop = scCursor - ListRows + 1;

        for (int row = 0; row < ListRows; row++)
        {
            int idx = scTop + row;
            if (idx >= scRows.Count) break;
            var item = scRows[idx];
            float y = top + row * rowH;
            bool sel = idx == scCursor;
            string right = DotFont.Normalize(Lang.Value(item.Right));
            string name = DotFont.Normalize(Lang.T(item.Text));
            name = DotFont.Fit(name, GW - 66 - DotFont.Width(right, p), p);
            if (sel) g.FillRectangle(ink.Main, 16, y - 2, GW - 44, 7 * p + 4);
            DotText(g, sel ? ink.Ink : ink.Main, name, 20, y, p);
            DotText(g, sel ? ink.Ink : ink.Accent, right, GW - 32 - DotFont.Width(right, p), y, p);
        }

        float trackH = ListRows * rowH;
        g.FillRectangle(ink.Ghost, GW - 12, top - 2, 3, trackH);
        if (scRows.Count > ListRows)
        {
            float h = trackH * ListRows / scRows.Count;
            float sy = top - 2 + (trackH - h) * scTop / (scRows.Count - ListRows);
            g.FillRectangle(ink.Main, GW - 12, sy, 3, h);
        }
    }
}

/// <summary>One-time setup: the user's own app credentials for SoundCloud or Spotify.</summary>
sealed class SoundCloudKeysDialog : Form
{
    readonly TextBox txtId = new(), txtSecret = new();

    public string ClientId => txtId.Text;
    public string ClientSecret => txtSecret.Text;

    public SoundCloudKeysDialog(IMusicService service)
    {
        string currentId = service.ClientId;
        Text = service.NeedsSecret ? "SoundCloud app keys" : "Spotify app key";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 350);
        BackColor = Color.FromArgb(18, 18, 22);
        ForeColor = Color.FromArgb(220, 225, 235);
        Font = new Font("Segoe UI", 9f);

        var info = new Label
        {
            Location = new Point(14, 12),
            Size = new Size(492, 138),
            Text = service.SetupHelp,
        };
        var link = new LinkLabel
        {
            Text = service.AppsUrl,
            Location = new Point(14, 106),
            AutoSize = true,
            LinkColor = Color.FromArgb(110, 235, 255),
        };
        link.LinkClicked += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link.Text) { UseShellExecute = true });

        var lblRedirect = new Label { Text = "Redirect URI", Location = new Point(14, 138), AutoSize = true };
        var txtRedirect = new TextBox { Text = OAuthLoopback.RedirectUri, ReadOnly = true, Location = new Point(120, 135), Width = 386 };
        var lblId = new Label { Text = "Client ID", Location = new Point(14, 172), AutoSize = true };
        txtId.Location = new Point(120, 169);
        txtId.Width = 386;
        txtId.Text = currentId;
        var lblSecret = new Label { Text = "Client Secret", Location = new Point(14, 206), AutoSize = true };
        txtSecret.Location = new Point(120, 203);
        txtSecret.Width = 386;
        txtSecret.UseSystemPasswordChar = true;

        var note = new Label
        {
            Text = OperatingSystem.IsWindows() ? "Stored encrypted for your Windows account only." : "Stored encrypted on this Mac, for your user only.",
            Location = new Point(14, 236),
            AutoSize = true,
            ForeColor = Color.FromArgb(140, 146, 158),
        };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(334, 260), Size = new Size(84, 28) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(422, 260), Size = new Size(84, 28) };
        foreach (var t in new[] { txtRedirect, txtId, txtSecret })
        {
            t.BackColor = Color.FromArgb(30, 31, 37);
            t.ForeColor = ForeColor;
            t.BorderStyle = BorderStyle.FixedSingle;
        }
        foreach (var b in new[] { ok, cancel })
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(30, 31, 37);
        }
        ok.Click += (_, e) =>
        {
            if (ClientId.Trim().Length == 0 || (service.NeedsSecret && ClientSecret.Trim().Length == 0))
            {
                MessageBox.Show(this, service.NeedsSecret ? "Please fill in both the Client ID and the Client Secret." : "Please fill in the Client ID.", Text);
                DialogResult = DialogResult.None;
            }
        };
        AcceptButton = ok;
        CancelButton = cancel;
        // Room for the longer help text, and Spotify needs no secret.
        foreach (Control c in new Control[] { link, lblRedirect, txtRedirect, lblId, txtId, lblSecret, txtSecret, note, ok, cancel }) c.Top += 48;
        lblSecret.Visible = txtSecret.Visible = service.NeedsSecret;
        Controls.AddRange([info, link, lblRedirect, txtRedirect, lblId, txtId, lblSecret, txtSecret, note, ok, cancel]);
        ClassicFrame.Apply(this);
    }
}
