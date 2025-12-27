using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TFMAudioApp.Helpers;
using TFMAudioApp.Models;
using TFMAudioApp.Services;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.ViewModels;

public partial class PlayerViewModel : BaseViewModel
{
    private readonly IAudioPlayerService _playerService;
    private readonly ApiServiceFactory _apiFactory;

    // Events for popup requests (handled by the Page)
    public event EventHandler? ShowQueueRequested;
    public event EventHandler? ShowAddToPlaylistRequested;

    [ObservableProperty]
    private string _trackName = "Not Playing";

    [ObservableProperty]
    private string _artistName = "";

    [ObservableProperty]
    private string _channelName = "";

    [ObservableProperty]
    private string _playPauseIcon = ">";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _positionText = "0:00";

    [ObservableProperty]
    private string _durationText = "0:00";

    [ObservableProperty]
    private double _volume = 1.0;

    [ObservableProperty]
    private string _repeatIcon = "";

    [ObservableProperty]
    private Color _shuffleColor = Colors.White;

    [ObservableProperty]
    private Color _repeatColor = Colors.White;

    [ObservableProperty]
    private bool _isOptionsVisible;

    [ObservableProperty]
    private bool _isShowingTrackInfo;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _canSeek = true;

    [ObservableProperty]
    private string _fileSizeText = "";

    [ObservableProperty]
    private string _fileTypeText = "";

    [ObservableProperty]
    private string _dateAddedText = "";

    [ObservableProperty]
    private string _bitrateText = "";

    private bool _isSeeking;

    public PlayerViewModel(IAudioPlayerService playerService, ApiServiceFactory apiFactory)
    {
        _playerService = playerService;
        _apiFactory = apiFactory;
        Title = "Now Playing";

        // Initialize with current state
        Volume = _playerService.Volume;
        UpdateShuffleUI();
        UpdateRepeatUI();
    }

    // Public accessors for the Page to show popups
    public IList<Track> GetQueue() => _playerService.Queue;
    public Track? GetCurrentTrack() => _playerService.CurrentTrack;
    public int GetCurrentIndex() => _playerService.CurrentIndex;

    public async Task<List<Playlist>> GetPlaylistsAsync()
    {
        var service = await _apiFactory.GetApiServiceAsync();
        if (service == null) return new List<Playlist>();

        try
        {
            var result = await service.GetAllPlaylistsAsync();
            return result.Data ?? new List<Playlist>();
        }
        catch
        {
            return new List<Playlist>();
        }
    }

    public async Task<bool> AddTrackToPlaylistAsync(Playlist playlist, Track track)
    {
        var service = await _apiFactory.GetApiServiceAsync();
        if (service == null) return false;

        try
        {
            var request = new AddTrackRequest
            {
                FileId = track.FileId,
                ChannelId = track.ChannelId,
                ChannelName = track.ChannelName,
                FileName = track.FileName,
                FilePath = track.FilePath ?? "",
                FileSize = track.FileSize,
                DirectUrl = track.DirectUrl
            };
            await service.AddTrackToPlaylistAsync(playlist.Id, request);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void ClearQueue() => _playerService.ClearQueue();
    public void ShuffleQueue() => _playerService.ShuffleQueue();

    public void OnAppearing()
    {
        // Subscribe to events
        _playerService.TrackChanged += OnTrackChanged;
        _playerService.StateChanged += OnStateChanged;
        _playerService.PositionChanged += OnPositionChanged;
        _playerService.DurationChanged += OnDurationChanged;

        // Update UI with current state
        UpdateTrackInfo();
        UpdatePlayPauseIcon();
    }

    public void OnDisappearing()
    {
        // Unsubscribe from events
        _playerService.TrackChanged -= OnTrackChanged;
        _playerService.StateChanged -= OnStateChanged;
        _playerService.PositionChanged -= OnPositionChanged;
        _playerService.DurationChanged -= OnDurationChanged;
    }

    private void OnTrackChanged(object? sender, Models.Track? track)
    {
        MainThread.BeginInvokeOnMainThread(UpdateTrackInfo);
    }

    private void OnStateChanged(object? sender, PlaybackState state)
    {
        System.Diagnostics.Debug.WriteLine($"[PlayerVM] OnStateChanged received: {state}");
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdatePlayPauseIcon();
            // Only show loading animation for initial Loading state, not during playback buffering
            IsLoading = state == PlaybackState.Loading;
        });
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (_isSeeking) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            PositionText = FormatTime(position);
            if (_playerService.Duration.TotalSeconds > 0)
            {
                Progress = position.TotalSeconds / _playerService.Duration.TotalSeconds;
            }
        });
    }

    private void OnDurationChanged(object? sender, TimeSpan duration)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DurationText = FormatTime(duration);
        });
    }

    private void UpdateTrackInfo()
    {
        var track = _playerService.CurrentTrack;
        if (track != null)
        {
            TrackName = track.DisplayName;
            ArtistName = track.DisplayArtist;
            ChannelName = track.ChannelName;
            DurationText = track.Duration.HasValue
                ? FormatTime(TimeSpan.FromSeconds(track.Duration.Value))
                : "0:00";

            // Update file info for the flip view
            FileSizeText = FormatFileSize(track.FileSize);
            FileTypeText = !string.IsNullOrEmpty(track.FileType) ? track.FileType.ToUpperInvariant() : "AUDIO";
            DateAddedText = track.DateAdded != default ? track.DateAdded.ToString("MMM dd, yyyy") : "Unknown";
            BitrateText = track.Duration.HasValue && track.FileSize > 0
                ? $"{(int)(track.FileSize * 8 / track.Duration.Value / 1000)} kbps"
                : "Unknown";

            // Seeking is now always available (server supports progressive streaming)
            CanSeek = true;
        }
        else
        {
            TrackName = "Not Playing";
            ArtistName = "";
            ChannelName = "";
            Progress = 0;
            PositionText = "0:00";
            DurationText = "0:00";
            FileSizeText = "";
            FileTypeText = "";
            DateAddedText = "";
            BitrateText = "";
            IsShowingTrackInfo = false;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "Unknown";
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    private void UpdatePlayPauseIcon()
    {
        var isPlaying = _playerService.IsPlaying;
        var newIcon = isPlaying ? "II" : ">";
        System.Diagnostics.Debug.WriteLine($"[PlayerVM] UpdatePlayPauseIcon: IsPlaying={isPlaying}, Icon={newIcon}");
        PlayPauseIcon = newIcon;
    }

    private void UpdateShuffleUI()
    {
        ShuffleColor = _playerService.ShuffleEnabled
            ? Application.Current?.Resources["Primary"] as Color ?? Colors.Green
            : Colors.White;
    }

    private void UpdateRepeatUI()
    {
        switch (_playerService.RepeatMode)
        {
            case RepeatMode.None:
                RepeatIcon = "";
                RepeatColor = Colors.White;
                break;
            case RepeatMode.All:
                RepeatIcon = "";
                RepeatColor = Application.Current?.Resources["Primary"] as Color ?? Colors.Green;
                break;
            case RepeatMode.One:
                RepeatIcon = "1";
                RepeatColor = Application.Current?.Resources["Primary"] as Color ?? Colors.Green;
                break;
        }
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");
    }

    public void SeekToPosition(double progress)
    {
        var duration = _playerService.Duration;
        if (duration.TotalSeconds > 0)
        {
            var targetPosition = TimeSpan.FromSeconds(duration.TotalSeconds * progress);
            _ = _playerService.SeekAsync(targetPosition);
        }
    }

    partial void OnVolumeChanged(double value)
    {
        _playerService.Volume = value;
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task PlayPauseAsync()
    {
        await _playerService.TogglePlayPauseAsync();
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        await _playerService.NextAsync();
    }

    [RelayCommand]
    private async Task PreviousAsync()
    {
        await _playerService.PreviousAsync();
    }

    [RelayCommand]
    private void ToggleShuffle()
    {
        _playerService.ShuffleEnabled = !_playerService.ShuffleEnabled;
        UpdateShuffleUI();
    }

    [RelayCommand]
    private void ToggleRepeat()
    {
        _playerService.RepeatMode = _playerService.RepeatMode switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.None,
            _ => RepeatMode.None
        };
        UpdateRepeatUI();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _playerService.StopAsync();
        // Don't call UpdateTrackInfo() - keep showing current track
        // Just reset progress and update play/pause icon
        Progress = 0;
        PositionText = "0:00";
        UpdatePlayPauseIcon();
    }

    [RelayCommand]
    private void ShowOptions()
    {
        IsOptionsVisible = true;
    }

    [RelayCommand]
    private void HideOptions()
    {
        IsOptionsVisible = false;
    }

    [RelayCommand]
    private void ToggleTrackInfo()
    {
        if (_playerService.CurrentTrack != null)
        {
            IsShowingTrackInfo = !IsShowingTrackInfo;
        }
    }

    [RelayCommand]
    private void AddToPlaylist()
    {
        IsOptionsVisible = false;
        ShowAddToPlaylistRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ViewQueue()
    {
        IsOptionsVisible = false;
        ShowQueueRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ShareTrackAsync()
    {
        IsOptionsVisible = false;
        var track = _playerService.CurrentTrack;
        if (track != null)
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "Share Track",
                Text = $"Check out this track: {track.DisplayName}"
            });
        }
    }

    [RelayCommand]
    private async Task StopFromOptionsAsync()
    {
        IsOptionsVisible = false;
        await StopAsync();
    }
}
