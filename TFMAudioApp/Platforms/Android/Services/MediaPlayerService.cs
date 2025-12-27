#if ANDROID
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Android.Support.V4.Media;
using Android.Support.V4.Media.Session;
using AndroidX.Core.App;
using TFMAudioApp.Services.Interfaces;
using Application = Android.App.Application;

namespace TFMAudioApp.Platforms.Android.Services;

[Service(Exported = true, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeMediaPlayback)]
public class MediaPlayerService : Service
{
    private const string CHANNEL_ID = "TFMAudioPlayerChannel";
    private const int NOTIFICATION_ID = 1001;

    private MediaSessionCompat? _mediaSession;
    private IAudioPlayerService? _playerService;
    private bool _isPlaying;
    private long _position;
    private long _duration;
    private AudioBecomingNoisyReceiver? _noisyReceiver;

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
        InitializeMediaSession();
        RegisterAudioNoisyReceiver();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var action = intent?.Action;

        switch (action)
        {
            case "ACTION_PLAY":
                HandlePlay();
                break;
            case "ACTION_PAUSE":
                HandlePause();
                break;
            case "ACTION_NEXT":
                HandleNext();
                break;
            case "ACTION_PREVIOUS":
                HandlePrevious();
                break;
            case "ACTION_STOP":
                HandleStop();
                break;
            case "ACTION_UPDATE":
                UpdateNotification(
                    intent?.GetStringExtra("title") ?? "Unknown",
                    intent?.GetStringExtra("artist") ?? "",
                    intent?.GetBooleanExtra("isPlaying", false) ?? false,
                    intent?.GetLongExtra("position", 0) ?? 0,
                    intent?.GetLongExtra("duration", 0) ?? 0);
                break;
        }

        return StartCommandResult.Sticky;
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                CHANNEL_ID,
                "Audio Player",
                NotificationImportance.Low)
            {
                Description = "Audio playback controls"
            };
            channel.SetShowBadge(false);
            channel.LockscreenVisibility = NotificationVisibility.Public;

            var notificationManager = (NotificationManager?)GetSystemService(NotificationService);
            notificationManager?.CreateNotificationChannel(channel);
        }
    }

    private void InitializeMediaSession()
    {
        _mediaSession = new MediaSessionCompat(this, "TFMAudioPlayer");
        _mediaSession.SetFlags(MediaSessionCompat.FlagHandlesMediaButtons | MediaSessionCompat.FlagHandlesTransportControls);
        _mediaSession.SetCallback(new MediaSessionCallback(this));
        _mediaSession.Active = true;
    }

    public void UpdateNotification(string title, string artist, bool isPlaying, long position = 0, long duration = 0)
    {
        System.Diagnostics.Debug.WriteLine($"[MediaPlayerService] UpdateNotification: title={title}, isPlaying={isPlaying}, pos={position}, dur={duration}");

        _isPlaying = isPlaying;
        _position = position;
        _duration = duration;

        var playPauseAction = isPlaying
            ? CreateAction("ACTION_PAUSE", "Pause", global::Android.Resource.Drawable.IcMediaPause)
            : CreateAction("ACTION_PLAY", "Play", global::Android.Resource.Drawable.IcMediaPlay);

        System.Diagnostics.Debug.WriteLine($"[MediaPlayerService] PlayPause action: {(isPlaying ? "PAUSE" : "PLAY")}");

        var style = new AndroidX.Media.App.NotificationCompat.MediaStyle()
            .SetMediaSession(_mediaSession?.SessionToken)
            .SetShowActionsInCompactView(0, 1, 2);

        var notification = new NotificationCompat.Builder(this, CHANNEL_ID)
            .SetContentTitle(title)
            .SetContentText(artist)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetLargeIcon(BitmapFactory.DecodeResource(Resources, Resource.Mipmap.appicon))
            .SetStyle(style)
            .SetVisibility((int)NotificationVisibility.Public)
            .SetOngoing(true) // Always ongoing to keep notification visible in background
            .AddAction(CreateAction("ACTION_PREVIOUS", "Previous", global::Android.Resource.Drawable.IcMediaPrevious))
            .AddAction(playPauseAction)
            .AddAction(CreateAction("ACTION_NEXT", "Next", global::Android.Resource.Drawable.IcMediaNext))
            .SetContentIntent(CreateContentIntent())
            .Build();

        // Always use StartForeground to keep the service alive in background
        StartForeground(NOTIFICATION_ID, notification);

        UpdateMediaSessionPlaybackState(isPlaying, position, duration);
        UpdateMediaSessionMetadata(title, artist, duration);
    }

    private NotificationCompat.Action CreateAction(string action, string title, int icon)
    {
        var intent = new Intent(this, typeof(MediaPlayerService));
        intent.SetAction(action);
        var pendingIntent = PendingIntent.GetService(
            this, 0, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        return new NotificationCompat.Action.Builder(icon, title, pendingIntent).Build();
    }

    private PendingIntent? CreateContentIntent()
    {
        var intent = Platform.CurrentActivity?.PackageManager?.GetLaunchIntentForPackage(Platform.CurrentActivity.PackageName!);
        if (intent == null) return null;
        return PendingIntent.GetActivity(
            this, 0, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    private void UpdateMediaSessionPlaybackState(bool isPlaying, long position, long duration)
    {
        var state = new PlaybackStateCompat.Builder()
            .SetActions(
                PlaybackStateCompat.ActionPlay |
                PlaybackStateCompat.ActionPause |
                PlaybackStateCompat.ActionSkipToNext |
                PlaybackStateCompat.ActionSkipToPrevious |
                PlaybackStateCompat.ActionStop |
                PlaybackStateCompat.ActionSeekTo)
            .SetState(
                isPlaying ? PlaybackStateCompat.StatePlaying : PlaybackStateCompat.StatePaused,
                position, // Current position in ms
                isPlaying ? 1.0f : 0f) // Playback speed (0 when paused)
            .Build();

        _mediaSession?.SetPlaybackState(state);
    }

    private void UpdateMediaSessionMetadata(string title, string artist, long duration)
    {
        var metadata = new MediaMetadataCompat.Builder()
            .PutString(MediaMetadataCompat.MetadataKeyTitle, title)
            .PutString(MediaMetadataCompat.MetadataKeyArtist, artist)
            .PutLong(MediaMetadataCompat.MetadataKeyDuration, duration) // Duration in ms
            .Build();

        _mediaSession?.SetMetadata(metadata);
    }

    private void HandlePlay()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var playerService = IPlatformApplication.Current?.Services.GetService<IAudioPlayerService>();
            await playerService?.TogglePlayPauseAsync()!;
        });
    }

    private void HandlePause()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var playerService = IPlatformApplication.Current?.Services.GetService<IAudioPlayerService>();
            await playerService?.PauseAsync()!;
        });
    }

    private void HandleNext()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var playerService = IPlatformApplication.Current?.Services.GetService<IAudioPlayerService>();
            await playerService?.NextAsync()!;
        });
    }

    private void HandlePrevious()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var playerService = IPlatformApplication.Current?.Services.GetService<IAudioPlayerService>();
            await playerService?.PreviousAsync()!;
        });
    }

    private void HandleStop()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var playerService = IPlatformApplication.Current?.Services.GetService<IAudioPlayerService>();
            await playerService?.StopAsync()!;
        });
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        // Called when app is swiped away from recent apps
        System.Diagnostics.Debug.WriteLine("[MediaPlayerService] OnTaskRemoved - stopping playback");
        HandleStop();
        base.OnTaskRemoved(rootIntent);
    }

    public override void OnDestroy()
    {
        UnregisterAudioNoisyReceiver();
        _mediaSession?.Release();
        base.OnDestroy();
    }

    private void RegisterAudioNoisyReceiver()
    {
        _noisyReceiver = new AudioBecomingNoisyReceiver(this);
        var filter = new IntentFilter(AudioManager.ActionAudioBecomingNoisy);
        RegisterReceiver(_noisyReceiver, filter);
        System.Diagnostics.Debug.WriteLine("[MediaPlayerService] Registered audio noisy receiver");
    }

    private void UnregisterAudioNoisyReceiver()
    {
        if (_noisyReceiver != null)
        {
            try
            {
                UnregisterReceiver(_noisyReceiver);
                System.Diagnostics.Debug.WriteLine("[MediaPlayerService] Unregistered audio noisy receiver");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaPlayerService] Error unregistering receiver: {ex.Message}");
            }
            _noisyReceiver = null;
        }
    }

    /// <summary>
    /// Handle audio becoming noisy (headphones unplugged, Bluetooth disconnected)
    /// </summary>
    public void OnAudioBecomingNoisy()
    {
        System.Diagnostics.Debug.WriteLine("[MediaPlayerService] Audio becoming noisy - pausing playback");
        HandlePause();
    }

    private class MediaSessionCallback : MediaSessionCompat.Callback
    {
        private readonly MediaPlayerService _service;

        public MediaSessionCallback(MediaPlayerService service)
        {
            _service = service;
        }

        public override void OnPlay() => _service.HandlePlay();
        public override void OnPause() => _service.HandlePause();
        public override void OnSkipToNext() => _service.HandleNext();
        public override void OnSkipToPrevious() => _service.HandlePrevious();
        public override void OnStop() => _service.HandleStop();

        public override void OnSeekTo(long pos)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                var playerService = IPlatformApplication.Current?.Services.GetService<IAudioPlayerService>();
                if (playerService != null)
                {
                    await playerService.SeekAsync(TimeSpan.FromMilliseconds(pos));
                }
            });
        }
    }
}

/// <summary>
/// BroadcastReceiver to handle audio becoming noisy events
/// (headphones unplugged, Bluetooth disconnected, etc.)
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class AudioBecomingNoisyReceiver : BroadcastReceiver
{
    private readonly MediaPlayerService? _service;

    public AudioBecomingNoisyReceiver()
    {
    }

    public AudioBecomingNoisyReceiver(MediaPlayerService service)
    {
        _service = service;
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action == AudioManager.ActionAudioBecomingNoisy)
        {
            System.Diagnostics.Debug.WriteLine("[AudioBecomingNoisyReceiver] Audio becoming noisy detected");
            _service?.OnAudioBecomingNoisy();
        }
    }
}
#endif
