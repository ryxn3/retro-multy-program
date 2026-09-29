using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Text;
using Android.Widget;
using NAudio.Wave;
using RetroRadio.Droid;
using Uri = Android.Net.Uri;

namespace RetroRadio;

/// <summary>Connects the shared radio code to Android: UI thread, screen, pickers, sound and decoders.</summary>
sealed class AndroidHost : IAndroidHost
{
    public static readonly AndroidHost Instance = new();

    const int PickRequestCode = 4100;
    readonly Handler main = new(Looper.MainLooper!);
    PickRequest? pending;

    public MainActivity? Activity { get; set; }
    public RadioView? View { get; set; }

    Context Ctx => (Context?)Activity ?? Android.App.Application.Context;

    public bool IsUiThread => Looper.MyLooper() == Looper.MainLooper;
    public void Post(Action action) => main.Post(() => Safely(action));
    public void PostDelayed(Action action, int milliseconds) => main.PostDelayed(() => Safely(action), milliseconds);

    void Safely(Action action)
    {
        try { action(); }
        catch (Exception ex) { Report(ex); }
    }

    // ───────────────────────────── errors ─────────────────────────────

    /// <summary>Where the last crash is written, to be shown (and copied) the next time the radio starts.</summary>
    public static string CrashFile => Path.Combine(Android.App.Application.Context.FilesDir!.AbsolutePath, "last-crash.txt");

    long lastToast;

    /// <summary>
    /// An error inside the radio: it's written down and shown briefly, and the radio carries on
    /// (on Windows a WinForms error would show a dialog rather than close the program).
    /// </summary>
    public void Report(Exception ex)
    {
        Android.Util.Log.Error("RetroRadio", ex.ToString());
        try { File.AppendAllText(Path.Combine(Android.App.Application.Context.FilesDir!.AbsolutePath, "errors.txt"), $"{DateTime.Now:u}\n{ex}\n\n"); }
        catch (Exception) { }
        long now = SystemClock.UptimeMillis();
        if (now - lastToast < 8000 || Activity == null) return;
        lastToast = now;
        try { Toast.MakeText(Activity, $"Retro Radio error: {ex.GetType().Name}: {ex.Message}", ToastLength.Long)?.Show(); }
        catch (Exception) { }
    }

    public static void WriteCrash(Exception? ex)
    {
        try { File.WriteAllText(CrashFile, $"{DateTime.Now:u}\n{ex}"); }
        catch (Exception) { }
    }
    public void RequestPaint() => View?.PostInvalidateOnAnimation();
    public void FormResized() => Post(() => View?.FitForm());

    public void OpenUrl(string url)
    {
        var intent = new Intent(Intent.ActionView, Uri.Parse(url));
        intent.AddFlags(ActivityFlags.NewTask);
        Ctx.StartActivity(intent);
    }

    public void Minimize() => Activity?.MoveTaskToBack(true);

    /// <summary>POWER: the radio has played its goodbye; the app closes for real (the next start is a fresh boot).</summary>
    public void Quit()
    {
        Ctx.StopService(new Intent(Ctx, typeof(PlaybackService)));
        Activity?.FinishAndRemoveTask();
        MainActivity.Radio = null;
        PostDelayed(() => Process.KillProcess(Process.MyPid()), 300);
    }

    public IAudioSink OpenAudio(int sampleRate) => new AudioTrackSink(sampleRate);

    public WaveStream OpenDecoder(string pathOrUrl)
    {
        // Opening a web address talks to the network, which Android doesn't allow on the UI thread.
        if (IsUiThread && pathOrUrl.Contains("://"))
            return Task.Run(() => new MediaCodecReader(pathOrUrl)).GetAwaiter().GetResult();
        return new MediaCodecReader(pathOrUrl);
    }

    // ───────────────────────────── pickers ─────────────────────────────

    public void Pick(PickRequest request)
    {
        if (Activity == null) return;
        pending = request;
        Intent intent;
        if (request.Folder)
        {
            intent = new Intent(Intent.ActionOpenDocumentTree);
        }
        else
        {
            intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            bool audio = request.Extensions.Length > 0 && request.Extensions.All(IsAudioExt);
            intent.SetType(audio ? "audio/*" : "*/*");
            intent.PutExtra(Intent.ExtraAllowMultiple, request.Multiple);
        }
        try
        {
            Activity.StartActivityForResult(intent, PickRequestCode);
        }
        catch (ActivityNotFoundException)
        {
            pending = null;
            Toast.MakeText(Activity, "No file picker on this phone.", ToastLength.Long)?.Show();
        }
    }

    static bool IsAudioExt(string e) => e is ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" or ".ogg" or ".opus" or ".wma" or ".aif" or ".aiff" or ".mp2";

    public void PickerResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != PickRequestCode) return;
        var req = pending;
        pending = null;
        if (req == null || resultCode != Result.Ok || data == null) return;
        var uris = new List<Uri>();
        if (data.ClipData is { } clip)
            for (int i = 0; i < clip.ItemCount; i++)
                if (clip.GetItemAt(i)?.Uri is { } u) uris.Add(u);
        if (uris.Count == 0 && data.Data is { } one) uris.Add(one);
        if (uris.Count == 0) return;
        if (req.Folder)
        {
            try { Ctx.ContentResolver?.TakePersistableUriPermission(uris[0], ActivityFlags.GrantReadUriPermission); } catch (Exception) { }
        }
        ImportAsync(uris, req.Folder, req.Done);
    }

    /// <summary>
    /// Turns picked documents into paths the radio can open: the real file when the app may read it
    /// (it may, for music, once the phone allowed it), otherwise a copy in the app's own storage.
    /// </summary>
    public void ImportAsync(IReadOnlyList<Uri> uris, bool folder, Action<string[]> done)
    {
        var ctx = Ctx;
        Task.Run(() =>
        {
            var paths = new List<string>();
            foreach (var uri in uris)
            {
                try
                {
                    if (folder)
                    {
                        if (FolderPath(uri) is { } dir) paths.Add(dir);
                    }
                    else if (ReadablePath(ctx, uri) is { } p)
                    {
                        paths.Add(p);
                    }
                    else if (CopyIn(ctx, uri) is { } copy)
                    {
                        paths.Add(copy);
                    }
                }
                catch (Exception)
                {
                    // Skip what can't be read.
                }
            }
            Post(() => done([.. paths]));
        });
    }

    static string? FolderPath(Uri uri)
    {
        string? id = DocumentsContract.GetTreeDocumentId(uri);
        return id == null ? null : VolumePath(id) is { } p && Directory.Exists(p) ? p : null;
    }

    /// <summary>"primary:Music/song.mp3" → /storage/emulated/0/Music/song.mp3, "1234-ABCD:x" → /storage/1234-ABCD/x.</summary>
    static string? VolumePath(string docId)
    {
        int colon = docId.IndexOf(':');
        if (colon < 0) return null;
        string volume = docId[..colon], rel = docId[(colon + 1)..];
        if (volume == "raw") return rel;
        string root = volume == "primary" ? Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath ?? "/storage/emulated/0" : "/storage/" + volume;
        return rel.Length == 0 ? root : Path.Combine(root, rel);
    }

    static string? ReadablePath(Context ctx, Uri uri)
    {
        string? path = null;
        if (uri.Scheme == "file") path = uri.Path;
        else if (DocumentsContract.IsDocumentUri(ctx, uri) && DocumentsContract.GetDocumentId(uri) is { } id)
        {
            if (uri.Authority == "com.android.externalstorage.documents" || id.StartsWith("raw:", StringComparison.Ordinal)) path = VolumePath(id);
        }
        if (path == null || !File.Exists(path)) return null;
        try
        {
            using var _ = File.OpenRead(path);
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Copies a document into the app's own storage, keeping its name (the radio goes by the extension).</summary>
    static string? CopyIn(Context ctx, Uri uri)
    {
        var resolver = ctx.ContentResolver;
        if (resolver == null) return null;
        string name = DisplayName(resolver, uri) ?? "track";
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        string dir = Path.Combine(ctx.FilesDir!.AbsolutePath, "Imported");
        Directory.CreateDirectory(dir);
        string dest = Path.Combine(dir, name);
        if (File.Exists(dest)) return dest;
        using var input = resolver.OpenInputStream(uri);
        if (input == null) return null;
        string temp = dest + ".part";
        using (var output = File.Create(temp))
            input.CopyTo(output);
        File.Move(temp, dest, overwrite: true);
        return dest;
    }

    static string? DisplayName(ContentResolver resolver, Uri uri)
    {
        using var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
        if (cursor == null || !cursor.MoveToFirst()) return uri.LastPathSegment;
        int col = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
        return col >= 0 ? cursor.GetString(col) : uri.LastPathSegment;
    }

    // ───────────────────────────── text prompt ─────────────────────────────

    public void AskText(string title, string message, string[] fields, string[] values, Action<string[]> done)
    {
        if (Activity == null) return;
        var layout = new LinearLayout(Activity) { Orientation = Orientation.Vertical };
        int pad = (int)(20 * Activity.Resources!.DisplayMetrics!.Density);
        layout.SetPadding(pad, pad / 2, pad, 0);
        var boxes = new List<EditText>();
        for (int i = 0; i < fields.Length; i++)
        {
            var box = new EditText(Activity) { Hint = fields[i] };
            box.SetSingleLine(true);
            box.Text = i < values.Length ? values[i] : "";
            if (fields[i].Contains("Secret", StringComparison.OrdinalIgnoreCase))
                box.InputType = InputTypes.ClassText | InputTypes.TextVariationPassword;
            layout.AddView(box);
            boxes.Add(box);
        }
        var scroll = new ScrollView(Activity);
        scroll.AddView(layout);
        new AlertDialog.Builder(Activity)
            .SetTitle(title)!
            .SetMessage(message)!
            .SetView(scroll)!
            .SetPositiveButton("Save", (_, _) => done([.. boxes.Select(b => b.Text ?? "")]))!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }
}
