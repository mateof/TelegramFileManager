using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.Services;

// Default implementation for non-Android platforms
public class MediaNotificationService : IMediaNotificationService
{
    public void UpdateNotification(string title, string artist, bool isPlaying, long position = 0, long duration = 0)
    {
        // No-op on Windows and other platforms
        // TODO: Could implement Windows SystemMediaTransportControls in the future
    }

    public void StopNotification()
    {
        // No-op on Windows and other platforms
    }
}
