#if ANDROID
using Android.Content;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.Platforms.Android.Services;

public class MediaNotificationService : IMediaNotificationService
{
    public void UpdateNotification(string title, string artist, bool isPlaying, long position = 0, long duration = 0)
    {
        var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(MediaPlayerService));
        intent.SetAction("ACTION_UPDATE");
        intent.PutExtra("title", title);
        intent.PutExtra("artist", artist);
        intent.PutExtra("isPlaying", isPlaying);
        intent.PutExtra("position", position);
        intent.PutExtra("duration", duration);

        // Always use StartForegroundService to keep notification visible in background
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    public void StopNotification()
    {
        var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(MediaPlayerService));
        intent.SetAction("ACTION_STOP");
        context.StartService(intent);
    }
}
#endif
