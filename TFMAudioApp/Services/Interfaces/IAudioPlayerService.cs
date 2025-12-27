using System.Collections.ObjectModel;
using TFMAudioApp.Models;

namespace TFMAudioApp.Services.Interfaces;

/// <summary>
/// Repeat mode options
/// </summary>
public enum RepeatMode
{
    None = 0,
    All = 1,
    One = 2
}

/// <summary>
/// Playback state
/// </summary>
public enum PlaybackState
{
    Stopped,
    Loading,
    Playing,
    Paused,
    Buffering,
    Error
}

/// <summary>
/// Service for controlling audio playback
/// </summary>
public interface IAudioPlayerService
{
    #region State Properties

    /// <summary>
    /// Currently playing track
    /// </summary>
    Track? CurrentTrack { get; }

    /// <summary>
    /// Current playback state
    /// </summary>
    PlaybackState State { get; }

    /// <summary>
    /// Whether audio is currently playing
    /// </summary>
    bool IsPlaying { get; }

    /// <summary>
    /// Current playback position
    /// </summary>
    TimeSpan Position { get; }

    /// <summary>
    /// Total duration of current track
    /// </summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Volume level (0.0 - 1.0)
    /// </summary>
    double Volume { get; set; }

    /// <summary>
    /// Whether shuffle mode is enabled
    /// </summary>
    bool ShuffleEnabled { get; set; }

    /// <summary>
    /// Current repeat mode
    /// </summary>
    RepeatMode RepeatMode { get; set; }

    #endregion

    #region Queue Properties

    /// <summary>
    /// Current playback queue
    /// </summary>
    ObservableCollection<Track> Queue { get; }

    /// <summary>
    /// Index of current track in queue
    /// </summary>
    int CurrentIndex { get; }

    /// <summary>
    /// Whether there is a next track available
    /// </summary>
    bool HasNext { get; }

    /// <summary>
    /// Whether there is a previous track available
    /// </summary>
    bool HasPrevious { get; }

    #endregion

    #region Events

    /// <summary>
    /// Fired when the current track changes
    /// </summary>
    event EventHandler<Track?> TrackChanged;

    /// <summary>
    /// Fired when playback state changes
    /// </summary>
    event EventHandler<PlaybackState> StateChanged;

    /// <summary>
    /// Fired when playback position changes
    /// </summary>
    event EventHandler<TimeSpan> PositionChanged;

    /// <summary>
    /// Fired when duration is known/changes
    /// </summary>
    event EventHandler<TimeSpan> DurationChanged;

    /// <summary>
    /// Fired when an error occurs
    /// </summary>
    event EventHandler<string> ErrorOccurred;

    #endregion

    #region Playback Control

    /// <summary>
    /// Play a single track
    /// </summary>
    Task PlayAsync(Track track);

    /// <summary>
    /// Play a list of tracks starting at specified index
    /// </summary>
    Task PlayAsync(IEnumerable<Track> tracks, int startIndex = 0);

    /// <summary>
    /// Pause playback
    /// </summary>
    Task PauseAsync();

    /// <summary>
    /// Resume playback
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    /// Toggle play/pause
    /// </summary>
    Task TogglePlayPauseAsync();

    /// <summary>
    /// Stop playback and clear current track
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Skip to next track
    /// </summary>
    Task NextAsync();

    /// <summary>
    /// Go to previous track
    /// </summary>
    Task PreviousAsync();

    /// <summary>
    /// Seek to specific position
    /// </summary>
    Task SeekAsync(TimeSpan position);

    /// <summary>
    /// Play track at specific index in queue
    /// </summary>
    Task PlayAtIndexAsync(int index);

    #endregion

    #region Queue Management

    /// <summary>
    /// Add track to end of queue
    /// </summary>
    void AddToQueue(Track track);

    /// <summary>
    /// Add multiple tracks to end of queue
    /// </summary>
    void AddToQueue(IEnumerable<Track> tracks);

    /// <summary>
    /// Insert track at specific position in queue
    /// </summary>
    void InsertInQueue(int index, Track track);

    /// <summary>
    /// Remove track at specific position from queue
    /// </summary>
    void RemoveFromQueue(int index);

    /// <summary>
    /// Clear the entire queue
    /// </summary>
    void ClearQueue();

    /// <summary>
    /// Move track within queue
    /// </summary>
    void MoveInQueue(int fromIndex, int toIndex);

    /// <summary>
    /// Shuffle the queue (keeps current track at position 0)
    /// </summary>
    void ShuffleQueue();

    #endregion
}
