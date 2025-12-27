namespace TFMAudioApp.Services.Interfaces;

public interface IMediaNotificationService
{
    /// <summary>
    /// Updates the media notification with current playback info
    /// </summary>
    /// <param name="title">Track title</param>
    /// <param name="artist">Artist name</param>
    /// <param name="isPlaying">Whether audio is currently playing</param>
    /// <param name="position">Current playback position in milliseconds</param>
    /// <param name="duration">Total track duration in milliseconds</param>
    void UpdateNotification(string title, string artist, bool isPlaying, long position = 0, long duration = 0);

    /// <summary>
    /// Stops and removes the media notification
    /// </summary>
    void StopNotification();
}
