using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.Controls;

public partial class MiniPlayerControl : ContentView
{
    private IAudioPlayerService? _playerService;

    #region Bindable Properties

    public static readonly BindableProperty TrackNameProperty =
        BindableProperty.Create(nameof(TrackName), typeof(string), typeof(MiniPlayerControl), "Not Playing");

    public static readonly BindableProperty ArtistNameProperty =
        BindableProperty.Create(nameof(ArtistName), typeof(string), typeof(MiniPlayerControl), "");

    public static readonly BindableProperty PlayPauseIconProperty =
        BindableProperty.Create(nameof(PlayPauseIcon), typeof(string), typeof(MiniPlayerControl), ">");

    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(MiniPlayerControl), 0.0);

    public static readonly BindableProperty CanSeekProperty =
        BindableProperty.Create(nameof(CanSeek), typeof(bool), typeof(MiniPlayerControl), true);

    public static readonly BindableProperty IsPlayerVisibleProperty =
        BindableProperty.Create(nameof(IsPlayerVisible), typeof(bool), typeof(MiniPlayerControl), false,
            propertyChanged: OnIsPlayerVisibleChanged);

    public string TrackName
    {
        get => (string)GetValue(TrackNameProperty);
        set => SetValue(TrackNameProperty, value);
    }

    public string ArtistName
    {
        get => (string)GetValue(ArtistNameProperty);
        set => SetValue(ArtistNameProperty, value);
    }

    public string PlayPauseIcon
    {
        get => (string)GetValue(PlayPauseIconProperty);
        set => SetValue(PlayPauseIconProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool CanSeek
    {
        get => (bool)GetValue(CanSeekProperty);
        set => SetValue(CanSeekProperty, value);
    }

    public bool IsPlayerVisible
    {
        get => (bool)GetValue(IsPlayerVisibleProperty);
        set => SetValue(IsPlayerVisibleProperty, value);
    }

    private static void OnIsPlayerVisibleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is MiniPlayerControl control)
        {
            control.IsVisible = (bool)newValue;
        }
    }

    #endregion

    public MiniPlayerControl()
    {
        InitializeComponent();
        IsVisible = false;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler != null)
        {
            // Get service after control is loaded
            InitializePlayerService();
        }
    }

    private void InitializePlayerService()
    {
        System.Diagnostics.Debug.WriteLine($"[MiniPlayer] InitializePlayerService called");
        _playerService = Application.Current?.Handler?.MauiContext?.Services.GetService<IAudioPlayerService>();

        if (_playerService != null)
        {
            System.Diagnostics.Debug.WriteLine($"[MiniPlayer] Got player service, subscribing to events");
            // Subscribe to events
            _playerService.TrackChanged += OnTrackChanged;
            _playerService.StateChanged += OnStateChanged;
            _playerService.PositionChanged += OnPositionChanged;
            _playerService.DurationChanged += OnDurationChanged;

            // Update UI with current state
            UpdateUI();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[MiniPlayer] Player service is NULL!");
        }
    }

    private void OnTrackChanged(object? sender, Models.Track? track)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (track != null)
            {
                TrackName = track.DisplayName;
                ArtistName = track.DisplayArtist;
                CanSeek = true; // Always allow seeking (server supports progressive streaming)
                IsPlayerVisible = true;
            }
            // Don't hide when track is null - user might have stopped playback
            // but still wants to see the mini player with last track info
        });
    }

    private void OnStateChanged(object? sender, PlaybackState state)
    {
        System.Diagnostics.Debug.WriteLine($"[MiniPlayer] OnStateChanged received: {state}");
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Show pause icon for Playing, Buffering, and Loading states
            // Loading = waiting for server to download from Telegram, but user can cancel
            var isPlaying = state == PlaybackState.Playing ||
                           state == PlaybackState.Buffering ||
                           state == PlaybackState.Loading;
            System.Diagnostics.Debug.WriteLine($"[MiniPlayer] Setting PlayPause icons, isPlaying={isPlaying}");
            UpdatePlayPauseIcon(isPlaying);
        });
    }

    private void UpdatePlayPauseIcon(bool isPlaying)
    {
        // Toggle between play and pause icons
        PlayIcon.IsVisible = !isPlaying;
        PauseIcon.IsVisible = isPlaying;
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        // Don't update progress while user is seeking
        if (_isSeeking) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_playerService?.Duration.TotalSeconds > 0)
            {
                Progress = position.TotalSeconds / _playerService.Duration.TotalSeconds;
            }
        });
    }

    private void OnDurationChanged(object? sender, TimeSpan duration)
    {
        // Duration updated, progress calculation will use this
    }

    private void UpdateUI()
    {
        System.Diagnostics.Debug.WriteLine($"[MiniPlayer] UpdateUI called");
        if (_playerService == null)
        {
            System.Diagnostics.Debug.WriteLine($"[MiniPlayer] _playerService is null!");
            return;
        }

        var track = _playerService.CurrentTrack;
        if (track != null)
        {
            TrackName = track.DisplayName;
            ArtistName = track.DisplayArtist;
            CanSeek = true; // Always allow seeking (server supports progressive streaming)
            IsPlayerVisible = true;

            // Update progress
            if (_playerService.Duration.TotalSeconds > 0)
            {
                Progress = _playerService.Position.TotalSeconds / _playerService.Duration.TotalSeconds;
            }
        }

        // Always update play/pause icon based on current state
        var isPlaying = _playerService.IsPlaying;
        System.Diagnostics.Debug.WriteLine($"[MiniPlayer] UpdateUI: IsPlaying={isPlaying}");
        UpdatePlayPauseIcon(isPlaying);

        // Show mini player if there's a track or if there are items in queue
        if (track != null || _playerService.Queue.Count > 0)
        {
            IsPlayerVisible = true;
        }
    }

    private async void OnPlayPauseClicked(object? sender, EventArgs e)
    {
        if (_playerService == null) return;
        await _playerService.TogglePlayPauseAsync();
    }

    private async void OnPreviousClicked(object? sender, EventArgs e)
    {
        if (_playerService == null) return;
        await _playerService.PreviousAsync();
    }

    private async void OnNextClicked(object? sender, EventArgs e)
    {
        if (_playerService == null) return;
        await _playerService.NextAsync();
    }

    private async void OnPlayerTapped(object? sender, TappedEventArgs e)
    {
        // Navigate to full player page
        await Shell.Current.GoToAsync("player");
    }

    private bool _isSeeking;

    private void OnSeekStarted(object? sender, EventArgs e)
    {
        _isSeeking = true;
    }

    private void OnSeekCompleted(object? sender, EventArgs e)
    {
        _isSeeking = false;

        if (_playerService == null) return;

        // Seeking is always allowed (server supports progressive streaming)

        var duration = _playerService.Duration;
        if (duration.TotalSeconds > 0)
        {
            var targetPosition = TimeSpan.FromSeconds(duration.TotalSeconds * ProgressSlider.Value);
            System.Diagnostics.Debug.WriteLine($"[MiniPlayer] Seeking to {targetPosition}");
            _ = _playerService.SeekAsync(targetPosition);
        }
    }
}
