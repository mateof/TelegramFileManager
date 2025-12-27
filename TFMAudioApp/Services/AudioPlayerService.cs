using System.Collections.ObjectModel;
using LibVLCSharp.Shared;
using TFMAudioApp.Models;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.Services;

/// <summary>
/// Audio player service using LibVLC for universal audio format support
/// Supports: MP3, FLAC, OGG, OPUS, AAC, WAV, M4A, APE, WMA, and many more
/// </summary>
public class AudioPlayerService : IAudioPlayerService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ICacheService _cacheService;
    private readonly IMediaNotificationService _mediaNotificationService;
    private readonly Random _random = new();

    private LibVLC? _libVLC;
    private LibVLCSharp.Shared.MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private System.Timers.Timer? _positionTimer;
    private List<Track> _originalQueue = new();
    private bool _isDisposed;
    private bool _isInitialized;
    private int _notificationUpdateCounter; // Update notification every N position updates
    private DateTime _playbackStartTime; // Track when playback started to avoid early notification updates
    private const int NotificationDelayMs = 3000; // Wait 3 seconds before updating notifications after playback starts

    public AudioPlayerService(ISettingsService settingsService, ICacheService cacheService, IMediaNotificationService mediaNotificationService)
    {
        _settingsService = settingsService;
        _cacheService = cacheService;
        _mediaNotificationService = mediaNotificationService;

        // Subscribe to cache events to update track status dynamically
        _cacheService.TrackCached += OnTrackCached;

        // Load saved settings
        ShuffleEnabled = _settingsService.ShuffleEnabled;
        RepeatMode = (RepeatMode)_settingsService.RepeatMode;

#if ANDROID
        // On Android, always use max volume (system volume is used)
        Volume = 1.0;
#else
        Volume = _settingsService.Volume;
#endif
    }

    private void OnTrackCached(object? sender, string trackId)
    {
        // If the current track just got cached, update its status
        if (CurrentTrack != null && CurrentTrack.FileId == trackId)
        {
            CurrentTrack.IsCached = true;
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Current track now cached: {CurrentTrack.FileName}");
            // Fire TrackChanged to notify UI to update CanSeek
            TrackChanged?.Invoke(this, CurrentTrack);
        }

        // Also update any tracks in the queue
        foreach (var track in Queue.Where(t => t.FileId == trackId))
        {
            track.IsCached = true;
        }
    }

    #region Initialization

    /// <summary>
    /// Initialize LibVLC - must be called before playing
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;

        try
        {
            // Initialize LibVLC core
            Core.Initialize();

            // Create LibVLC instance with audio-only optimizations and network settings
            // Increased timeouts to handle server-side file downloads from Telegram
            _libVLC = new LibVLC(
                "--no-video",               // Disable video for audio-only playback
                "--verbose=2",              // Enable verbose logging for debugging
                "--no-lua",                 // Disable Lua scripting
                "--no-snapshot-preview",    // No snapshot previews
                "--network-caching=30000",  // 30 seconds of network buffer (server may need to download from Telegram)
                "--live-caching=30000",     // 30 seconds for live streams
                "--file-caching=10000",     // 10 seconds file caching
                "--http-reconnect",         // Auto-reconnect on connection drops
                "--http-continuous",        // Enable continuous stream reading
                "--sout-mux-caching=5000",  // Output muxer caching
                "--tcp-caching=30000",      // TCP caching for slow connections
                "--clock-jitter=0",         // Reduce jitter sensitivity
                "--clock-synchro=0"         // Disable strict clock sync
            );

            // Log LibVLC messages for debugging
            _libVLC.Log += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[LibVLC] {e.Level}: {e.Message}");
            };

            // Create media player
            _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
            _mediaPlayer.Volume = (int)(_volume * 100);

            // Setup event handlers
            _mediaPlayer.Playing += OnPlaying;
            _mediaPlayer.Paused += OnPaused;
            _mediaPlayer.Stopped += OnStopped;
            _mediaPlayer.EndReached += OnEndReached;
            _mediaPlayer.EncounteredError += OnError;
            _mediaPlayer.Opening += OnOpening;
            _mediaPlayer.Buffering += OnBuffering;
            _mediaPlayer.LengthChanged += OnLengthChanged;

            // Setup position timer (LibVLC doesn't have position changed events)
            _positionTimer = new System.Timers.Timer(250); // Update every 250ms
            _positionTimer.Elapsed += OnPositionTimerElapsed;

            _isInitialized = true;
            System.Diagnostics.Debug.WriteLine("[AudioPlayer] LibVLC initialized successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Failed to initialize LibVLC: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"Failed to initialize audio player: {ex.Message}");
        }
    }

    #endregion

    #region State Properties

    public Track? CurrentTrack { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    /// <summary>
    /// Returns true when audio is playing (including during buffering while streaming)
    /// </summary>
    public bool IsPlaying => State == PlaybackState.Playing || State == PlaybackState.Buffering;

    /// <summary>
    /// Returns true only when state is exactly Playing (not buffering)
    /// </summary>
    public bool IsPlayingExact => State == PlaybackState.Playing;

    public TimeSpan Position { get; private set; }
    public TimeSpan Duration { get; private set; }

    private double _volume = 1.0;
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0, 1.0);
            _settingsService.Volume = _volume;
            if (_mediaPlayer != null)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _mediaPlayer.Volume = (int)(_volume * 100);
                });
            }
        }
    }

    private bool _shuffleEnabled;
    public bool ShuffleEnabled
    {
        get => _shuffleEnabled;
        set
        {
            _shuffleEnabled = value;
            _settingsService.ShuffleEnabled = value;
        }
    }

    private RepeatMode _repeatMode;
    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            _repeatMode = value;
            _settingsService.RepeatMode = (int)value;
        }
    }

    #endregion

    #region Queue Properties

    public ObservableCollection<Track> Queue { get; } = new();
    public int CurrentIndex { get; private set; } = -1;
    public bool HasNext => Queue.Count > 0 && (CurrentIndex < Queue.Count - 1 || RepeatMode == RepeatMode.All);
    public bool HasPrevious => Queue.Count > 0 && (CurrentIndex > 0 || RepeatMode == RepeatMode.All);

    #endregion

    #region Events

    public event EventHandler<Track?>? TrackChanged;
    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<TimeSpan>? DurationChanged;
    public event EventHandler<string>? ErrorOccurred;

    #endregion

    #region LibVLC Event Handlers

    private void OnOpening(object? sender, EventArgs e)
    {
        SetState(PlaybackState.Loading);
    }

    private void OnBuffering(object? sender, MediaPlayerBufferingEventArgs e)
    {
        if (e.Cache < 100)
        {
            SetState(PlaybackState.Buffering);
        }
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        _retryCount = 0; // Reset retry count on successful playback
        _playbackStartTime = DateTime.UtcNow; // Mark when playback actually started
        SetState(PlaybackState.Playing, skipNotification: true); // Skip notification to avoid audio glitch
        _positionTimer?.Start();

        // Delay the first notification update to avoid audio interruption
        Task.Delay(NotificationDelayMs).ContinueWith(_ =>
        {
            if (State == PlaybackState.Playing && CurrentTrack != null)
            {
                UpdateMediaNotification();
            }
        });
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        SetState(PlaybackState.Paused);
        _positionTimer?.Stop();
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        SetState(PlaybackState.Stopped);
        _positionTimer?.Stop();
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        _positionTimer?.Stop();

        // Handle end of track on main thread
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (RepeatMode == RepeatMode.One)
            {
                await SeekAsync(TimeSpan.Zero);
                await ResumeAsync();
            }
            else
            {
                await NextAsync();
            }
        });
    }

    private int _retryCount = 0;
    private const int MaxRetries = 2;

    private void OnError(object? sender, EventArgs e)
    {
        var errorMsg = "Playback error occurred";

        // Try to get more details about the error
        if (_currentMedia != null)
        {
            var state = _currentMedia.State;
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Media state: {state}");
        }

        if (_mediaPlayer != null)
        {
            var state = _mediaPlayer.State;
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Player state: {state}");
        }

        System.Diagnostics.Debug.WriteLine($"[AudioPlayer] LibVLC error occurred for track: {CurrentTrack?.FileName ?? "null"}");
        System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Error: {errorMsg}, retry count: {_retryCount}");

        // Retry playback if under max retries (server might have been downloading)
        if (_retryCount < MaxRetries && CurrentIndex >= 0 && CurrentIndex < Queue.Count)
        {
            _retryCount++;
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Retrying playback (attempt {_retryCount}/{MaxRetries})...");

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(2000); // Wait 2 seconds before retry
                await PlayAtIndexAsync(CurrentIndex);
            });
            return;
        }

        _retryCount = 0;
        SetState(PlaybackState.Error);
        ErrorOccurred?.Invoke(this, errorMsg);
        _positionTimer?.Stop();
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        Duration = TimeSpan.FromMilliseconds(e.Length);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DurationChanged?.Invoke(this, Duration);
        });
    }

    private void OnPositionTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_mediaPlayer == null || !_mediaPlayer.IsPlaying) return;

        var length = _mediaPlayer.Length;
        if (length > 0)
        {
            var positionMs = _mediaPlayer.Position * length;
            Position = TimeSpan.FromMilliseconds(positionMs);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                PositionChanged?.Invoke(this, Position);
            });

            // Update notification every ~1 second (4 cycles of 250ms)
            // But skip during initial playback period to avoid audio glitches
            _notificationUpdateCounter++;
            if (_notificationUpdateCounter >= 4)
            {
                _notificationUpdateCounter = 0;
                var timeSincePlaybackStart = (DateTime.UtcNow - _playbackStartTime).TotalMilliseconds;
                if (timeSincePlaybackStart > NotificationDelayMs)
                {
                    UpdateMediaNotification();
                }
            }
        }
    }

    private void UpdateMediaNotification()
    {
        if (CurrentTrack == null)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] UpdateMediaNotification: CurrentTrack is null, skipping");
            return;
        }

        var positionMs = (long)Position.TotalMilliseconds;
        var durationMs = (long)Duration.TotalMilliseconds;
        // Include Loading and Buffering states as "playing" since audio will play shortly
        // This ensures the notification shows Pause button from the start
        var isPlaying = State == PlaybackState.Playing ||
                       State == PlaybackState.Buffering ||
                       State == PlaybackState.Loading;

        System.Diagnostics.Debug.WriteLine($"[AudioPlayer] UpdateMediaNotification: State={State}, IsPlaying={isPlaying}");

        _mediaNotificationService.UpdateNotification(
            CurrentTrack.DisplayName,
            CurrentTrack.DisplayArtist,
            isPlaying,
            positionMs,
            durationMs);
    }

    #endregion

    #region Playback Control

    public async Task PlayAsync(Track track)
    {
        await PlayAsync(new[] { track }, 0);
    }

    public async Task PlayAsync(IEnumerable<Track> tracks, int startIndex = 0)
    {
        var trackList = tracks.ToList();
        if (trackList.Count == 0) return;

        // Store original order for shuffle toggle
        _originalQueue = new List<Track>(trackList);

        Queue.Clear();
        foreach (var track in trackList)
        {
            Queue.Add(track);
        }

        if (ShuffleEnabled && trackList.Count > 1)
        {
            ShuffleQueue();
            // Find the selected track and move it to front
            var selectedTrack = trackList[startIndex];
            var shuffledIndex = Queue.IndexOf(selectedTrack);
            if (shuffledIndex > 0)
            {
                MoveInQueue(shuffledIndex, 0);
            }
            startIndex = 0;
        }

        await PlayAtIndexAsync(startIndex);
    }

    public async Task PauseAsync()
    {
        if (_mediaPlayer == null) return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _mediaPlayer.Pause();
        });
    }

    public async Task ResumeAsync()
    {
        if (_mediaPlayer == null) return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _mediaPlayer.Play();
        });
    }

    public async Task TogglePlayPauseAsync()
    {
        // Handle Loading state - stop the loading process if user wants to pause
        if (State == PlaybackState.Loading)
        {
            System.Diagnostics.Debug.WriteLine("[AudioPlayer] TogglePlayPause during Loading - stopping");
            await StopAsync();
            return;
        }

        if (IsPlaying)
        {
            await PauseAsync();
        }
        else if (State == PlaybackState.Paused)
        {
            // Resume from pause
            await ResumeAsync();
        }
        else if (State == PlaybackState.Stopped && CurrentIndex >= 0 && CurrentIndex < Queue.Count)
        {
            // Stopped but have a track - replay it
            await PlayAtIndexAsync(CurrentIndex);
        }
        else if (Queue.Count > 0)
        {
            // No current track but have queue - play first
            await PlayAtIndexAsync(0);
        }
    }

    public async Task StopAsync()
    {
        if (_mediaPlayer == null) return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _mediaPlayer.Stop();
        });

        _currentMedia?.Dispose();
        _currentMedia = null;

        // Don't clear CurrentTrack - keep it so user can resume
        // CurrentTrack = null;
        // CurrentIndex = -1;
        Position = TimeSpan.Zero;
        // Keep Duration so UI still shows track info

        // Don't invoke TrackChanged with null - track is still "selected", just stopped
        // TrackChanged?.Invoke(this, null);
        SetState(PlaybackState.Stopped);
    }

    public async Task NextAsync()
    {
        if (Queue.Count == 0) return;

        var nextIndex = CurrentIndex + 1;

        if (nextIndex >= Queue.Count)
        {
            if (RepeatMode == RepeatMode.All)
            {
                nextIndex = 0;
            }
            else
            {
                await StopAsync();
                return;
            }
        }

        await PlayAtIndexAsync(nextIndex);
    }

    public async Task PreviousAsync()
    {
        if (Queue.Count == 0) return;

        // If more than 3 seconds into track, restart it
        if (Position.TotalSeconds > 3)
        {
            await SeekAsync(TimeSpan.Zero);
            return;
        }

        var prevIndex = CurrentIndex - 1;

        if (prevIndex < 0)
        {
            if (RepeatMode == RepeatMode.All)
            {
                prevIndex = Queue.Count - 1;
            }
            else
            {
                await SeekAsync(TimeSpan.Zero);
                return;
            }
        }

        await PlayAtIndexAsync(prevIndex);
    }

    public async Task SeekAsync(TimeSpan position)
    {
        if (_mediaPlayer == null || _mediaPlayer.Length <= 0) return;

        // Seeking is now allowed for all tracks (server supports progressive streaming with range requests)
        System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Seeking to {position}");

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var positionRatio = (float)(position.TotalMilliseconds / _mediaPlayer.Length);
                _mediaPlayer.Position = Math.Clamp(positionRatio, 0f, 1f);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Seek failed: {ex.Message}");
            // Don't propagate error - just ignore failed seek
        }
    }

    public async Task PlayAtIndexAsync(int index)
    {
        if (!_isInitialized)
        {
            Initialize();
        }

        if (index < 0 || index >= Queue.Count) return;
        if (_mediaPlayer == null || _libVLC == null)
        {
            ErrorOccurred?.Invoke(this, "Audio player not initialized");
            return;
        }

        var track = Queue[index];
        CurrentIndex = index;
        CurrentTrack = track;

        // Note: We no longer download tracks to device during playback.
        // The server handles progressive streaming with server-side caching.
        // Device caching is only for explicit offline playlist downloads.

        var streamUrl = await GetStreamUrlAsync(track);
        System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Track: {track.FileName}");
        System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Stream URL: {streamUrl}");

        if (string.IsNullOrEmpty(streamUrl))
        {
            var errorMsg = "Could not get stream URL";
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Error: {errorMsg}");
            ErrorOccurred?.Invoke(this, errorMsg);
            return;
        }

        bool isRemoteUrl = streamUrl.StartsWith("http://") || streamUrl.StartsWith("https://");

        TrackChanged?.Invoke(this, track);
        SetState(PlaybackState.Loading);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            try
            {
                // Dispose previous media
                _currentMedia?.Dispose();

                // Create new media from URL or file path
                if (isRemoteUrl)
                {
                    System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Creating media from URL");
                    _currentMedia = new Media(_libVLC, new Uri(streamUrl));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Creating media from file: {streamUrl}");
                    _currentMedia = new Media(_libVLC, streamUrl, FromType.FromPath);
                }

                // Play the media
                _mediaPlayer.Play(_currentMedia);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Exception playing media: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Failed to play: {ex.Message}");
            }
        });
    }

    private async Task<string?> GetStreamUrlAsync(Track track)
    {
        try
        {
            // Use cache service to get the best playback URL
            // This returns local path if cached, otherwise remote URL
            return await _cacheService.GetPlaybackUrlAsync(track);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get stream URL: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Queue Management

    public void AddToQueue(Track track)
    {
        Queue.Add(track);
    }

    public void AddToQueue(IEnumerable<Track> tracks)
    {
        foreach (var track in tracks)
        {
            Queue.Add(track);
        }
    }

    public void InsertInQueue(int index, Track track)
    {
        if (index < 0) index = 0;
        if (index > Queue.Count) index = Queue.Count;

        Queue.Insert(index, track);

        // Adjust current index if needed
        if (index <= CurrentIndex)
        {
            CurrentIndex++;
        }
    }

    public void RemoveFromQueue(int index)
    {
        if (index < 0 || index >= Queue.Count) return;

        Queue.RemoveAt(index);

        // Adjust current index if needed
        if (index < CurrentIndex)
        {
            CurrentIndex--;
        }
        else if (index == CurrentIndex)
        {
            // Current track was removed
            if (Queue.Count == 0)
            {
                CurrentIndex = -1;
                CurrentTrack = null;
            }
            else if (CurrentIndex >= Queue.Count)
            {
                CurrentIndex = Queue.Count - 1;
            }
        }
    }

    public void ClearQueue()
    {
        Queue.Clear();
        _originalQueue.Clear();
        CurrentIndex = -1;
    }

    public void MoveInQueue(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Queue.Count) return;
        if (toIndex < 0 || toIndex >= Queue.Count) return;
        if (fromIndex == toIndex) return;

        var item = Queue[fromIndex];
        Queue.RemoveAt(fromIndex);
        Queue.Insert(toIndex, item);

        // Adjust current index if needed
        if (CurrentIndex == fromIndex)
        {
            CurrentIndex = toIndex;
        }
        else if (fromIndex < CurrentIndex && toIndex >= CurrentIndex)
        {
            CurrentIndex--;
        }
        else if (fromIndex > CurrentIndex && toIndex <= CurrentIndex)
        {
            CurrentIndex++;
        }
    }

    public void ShuffleQueue()
    {
        if (Queue.Count <= 1) return;

        // Fisher-Yates shuffle, but keep current track in place
        var currentTrack = CurrentIndex >= 0 && CurrentIndex < Queue.Count ? Queue[CurrentIndex] : null;
        var shuffled = Queue.OrderBy(_ => _random.Next()).ToList();

        Queue.Clear();
        foreach (var track in shuffled)
        {
            Queue.Add(track);
        }

        // Update current index to match moved track
        if (currentTrack != null)
        {
            CurrentIndex = Queue.IndexOf(currentTrack);
        }
    }

    #endregion

    #region Helpers

    private void SetState(PlaybackState state, bool skipNotification = false)
    {
        System.Diagnostics.Debug.WriteLine($"[AudioPlayer] SetState called: {State} -> {state}");

        if (State == state)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] State unchanged, skipping");
            return;
        }

        State = state;
        System.Diagnostics.Debug.WriteLine($"[AudioPlayer] State updated to: {state}, IsPlaying: {IsPlaying}");

        // Update media notification for Android (skip during initial playback to avoid audio glitch)
        if (!skipNotification)
        {
            if (CurrentTrack != null)
            {
                // Check if we're in the initial playback period - skip notification updates to avoid glitches
                var timeSincePlaybackStart = (DateTime.UtcNow - _playbackStartTime).TotalMilliseconds;
                if (timeSincePlaybackStart > NotificationDelayMs || state == PlaybackState.Stopped || state == PlaybackState.Paused)
                {
                    UpdateMediaNotification();
                }
            }
            else if (state == PlaybackState.Stopped)
            {
                _mediaNotificationService.StopNotification();
            }
        }

        var hasSubscribers = StateChanged != null;
        System.Diagnostics.Debug.WriteLine($"[AudioPlayer] StateChanged has subscribers: {hasSubscribers}");

        MainThread.BeginInvokeOnMainThread(() =>
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Firing StateChanged event for state: {state}");
            StateChanged?.Invoke(this, state);
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        // Stop media notification
        _mediaNotificationService.StopNotification();

        _positionTimer?.Stop();
        _positionTimer?.Dispose();

        if (_mediaPlayer != null)
        {
            _mediaPlayer.Playing -= OnPlaying;
            _mediaPlayer.Paused -= OnPaused;
            _mediaPlayer.Stopped -= OnStopped;
            _mediaPlayer.EndReached -= OnEndReached;
            _mediaPlayer.EncounteredError -= OnError;
            _mediaPlayer.Opening -= OnOpening;
            _mediaPlayer.Buffering -= OnBuffering;
            _mediaPlayer.LengthChanged -= OnLengthChanged;
            _mediaPlayer.Dispose();
        }

        _currentMedia?.Dispose();
        _libVLC?.Dispose();

        _isDisposed = true;
    }

    #endregion
}
