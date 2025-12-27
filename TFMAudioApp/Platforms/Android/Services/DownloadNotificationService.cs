#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.Platforms.Android.Services;

public class DownloadNotificationService : IDownloadNotificationService
{
    private const string CHANNEL_ID = "TFMDownloadChannel";
    private const int NOTIFICATION_ID = 2001;
    private NotificationManager? _notificationManager;
    private bool _channelCreated;

    public void ShowDownloadProgress(string title, int current, int total, double progress)
    {
        EnsureChannelCreated();

        var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
        var progressPercent = (int)(progress * 100);

        var notification = new NotificationCompat.Builder(context, CHANNEL_ID)
            .SetContentTitle($"Downloading: {title}")
            .SetContentText($"{current} of {total} tracks ({progressPercent}%)")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetProgress(100, progressPercent, false)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetPriority(NotificationCompat.PriorityLow)
            .SetCategory(NotificationCompat.CategoryProgress)
            .Build();

        _notificationManager?.Notify(NOTIFICATION_ID, notification);
    }

    public void ShowDownloadComplete(string title, int totalDownloaded)
    {
        EnsureChannelCreated();

        var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;

        var notification = new NotificationCompat.Builder(context, CHANNEL_ID)
            .SetContentTitle("Download Complete")
            .SetContentText($"{title}: {totalDownloaded} tracks downloaded")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetOngoing(false)
            .SetAutoCancel(true)
            .SetPriority(NotificationCompat.PriorityDefault)
            .Build();

        _notificationManager?.Notify(NOTIFICATION_ID, notification);
    }

    public void ShowDownloadError(string title, string error)
    {
        EnsureChannelCreated();

        var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;

        var notification = new NotificationCompat.Builder(context, CHANNEL_ID)
            .SetContentTitle("Download Failed")
            .SetContentText($"{title}: {error}")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetOngoing(false)
            .SetAutoCancel(true)
            .SetPriority(NotificationCompat.PriorityDefault)
            .Build();

        _notificationManager?.Notify(NOTIFICATION_ID, notification);
    }

    public void CancelNotification()
    {
        _notificationManager?.Cancel(NOTIFICATION_ID);
    }

    private void EnsureChannelCreated()
    {
        if (_channelCreated) return;

        var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
        _notificationManager = (NotificationManager?)context.GetSystemService(Context.NotificationService);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                CHANNEL_ID,
                "Downloads",
                NotificationImportance.Low)
            {
                Description = "Download progress notifications"
            };
            channel.SetShowBadge(false);
            _notificationManager?.CreateNotificationChannel(channel);
        }

        _channelCreated = true;
    }
}
#endif
