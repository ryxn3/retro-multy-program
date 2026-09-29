using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using RetroRadio.Droid;
using Uri = Android.Net.Uri;

namespace RetroRadio;

/// <summary>The radio's one screen. The radio itself (RadioForm) lives as long as the app, not the activity.</summary>
[Activity(
    Label = "Retro Radio",
    MainLauncher = true,
    Exported = true,
    Icon = "@mipmap/icon",
    Theme = "@style/RetroTheme",
    LaunchMode = LaunchMode.SingleTask,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateAlwaysHidden,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation | ConfigChanges.UiMode | ConfigChanges.Density)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataMimeType = "audio/*")]
public sealed class MainActivity : Activity
{
    const int PermissionRequest = 1;

    internal static RadioForm? Radio;
    RadioView? view;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var host = AndroidHost.Instance;
        host.Activity = this;
        Host.Current = host;

        view = new RadioView(this);
        SetContentView(view);
        host.View = view;
        GoFullScreen();

        if (Radio == null || Radio.IsDisposed)
        {
            Radio = new RadioForm([]);
            System.Windows.Forms.Application.MainForm = Radio;
        }
        view.Form = Radio;

        StartPlaybackService();
        AskPermissions();
        HandleIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleIntent(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        AndroidHost.Instance.Activity = this;
        GoFullScreen();
        view?.RequestFocus();
    }

    protected override void OnStop()
    {
        base.OnStop();
        try { Radio?.SaveNow(); } catch (Exception) { }
    }

    protected override void OnDestroy()
    {
        if (AndroidHost.Instance.Activity == this)
        {
            AndroidHost.Instance.Activity = null;
            AndroidHost.Instance.View = null;
        }
        base.OnDestroy();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) GoFullScreen();
    }

#pragma warning disable CS0672, CS0618 // OnBackPressed: the back key is the radio's Esc (back out of menus, then to the background)
    public override void OnBackPressed() => view?.SendKey(System.Windows.Forms.Keys.Escape);
#pragma warning restore CS0672, CS0618

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        AndroidHost.Instance.PickerResult(requestCode, resultCode, data);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == PermissionRequest) AddMusicFolder();
    }

    /// <summary>A song opened with Retro Radio from a file manager or another app.</summary>
    void HandleIntent(Intent? intent)
    {
        if (intent?.Action != Intent.ActionView || intent.Data is not { } uri) return;
        intent.SetData(null); // only once, not again when the activity comes back
        AndroidHost.Instance.ImportAsync([uri], folder: false, files =>
        {
            if (files.Length > 0 && Radio != null)
                Radio.RaiseFilesDropped(files, new System.Drawing.Point(Radio.ClientSize.Width / 2, Radio.ClientSize.Height / 2));
        });
    }

    void AskPermissions()
    {
        var wanted = new List<string>();
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            wanted.Add(Manifest.Permission.ReadMediaAudio);
            wanted.Add(Manifest.Permission.PostNotifications);
        }
        else
        {
            wanted.Add(Manifest.Permission.ReadExternalStorage);
        }
        var missing = wanted.Where(p => CheckSelfPermission(p) != Permission.Granted).ToArray();
        if (missing.Length > 0) RequestPermissions(missing, PermissionRequest);
        else AddMusicFolder();
    }

    bool CanReadMusic() => CheckSelfPermission(Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
        ? Manifest.Permission.ReadMediaAudio : Manifest.Permission.ReadExternalStorage) == Permission.Granted;

    /// <summary>The first time the radio starts, the phone's Music folder goes into its list.</summary>
    void AddMusicFolder()
    {
        if (!CanReadMusic() || Radio == null) return;
        var music = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryMusic)?.AbsolutePath;
        if (music == null) return;
        try { Radio.AddLibraryIfEmpty(music); } catch (Exception) { }
    }

    void StartPlaybackService()
    {
        try
        {
            StartForegroundService(new Intent(this, typeof(PlaybackService)));
        }
        catch (Exception)
        {
            // Without it the music still plays; Android may just stop it sooner in the background.
        }
    }

    /// <summary>The radio fills the whole screen, like a head unit: no status or navigation bars.</summary>
    void GoFullScreen()
    {
        if (Window == null) return;
        Window.AddFlags(WindowManagerFlags.Fullscreen);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            Window.SetDecorFitsSystemWindows(false);
            if (Window.InsetsController is { } c)
            {
                c.Hide(WindowInsets.Type.StatusBars() | WindowInsets.Type.NavigationBars());
                c.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
            }
        }
        else
        {
#pragma warning disable CS0618
            Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(SystemUiFlags.ImmersiveSticky | SystemUiFlags.Fullscreen
                | SystemUiFlags.HideNavigation | SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutStable);
#pragma warning restore CS0618
        }
    }
}
