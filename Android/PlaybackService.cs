using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace RetroRadio;

/// <summary>
/// Keeps the radio playing with the screen off or another app in front. Android only lets a background app
/// keep running if it says so with a notification; this is that notification. POWER on the radio ends it.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class PlaybackService : Service
{
    const string Channel = "radio";
    const int NotificationId = 1;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(Channel, "Retro Radio", NotificationImportance.Low) { Description = "Shown while the radio is on" });

        var open = new Intent(this, typeof(MainActivity));
        open.SetFlags(ActivityFlags.SingleTop);
        var tap = PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var notification = new Notification.Builder(this, Channel)
            .SetContentTitle("Retro Radio")!
            .SetContentText("The radio is on. Press POWER on the radio to turn it off.")!
            .SetSmallIcon(Android.Resource.Drawable.IcMediaPlay)!
            .SetContentIntent(tap)!
            .SetOngoing(true)!
            .Build()!;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
        else StartForeground(NotificationId, notification);
        return StartCommandResult.NotSticky;
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        // Swiped away from the recent apps: save and switch off, like pulling the key.
        try { MainActivity.Radio?.SaveNow(); } catch (Exception) { }
        StopSelf();
        Process.KillProcess(Process.MyPid());
    }
}
