using CommunityToolkit.Maui.Views;
using TFMAudioApp.Controls;
using TFMAudioApp.Helpers;
using TFMAudioApp.Models;
using TFMAudioApp.Services.Interfaces;
using TFMAudioApp.ViewModels;

namespace TFMAudioApp.Views;

public partial class PlayerPage : ContentPage
{
    private readonly PlayerViewModel _viewModel;
    private IAudioPlayerService? _playerService;
    private CancellationTokenSource? _loadingAnimationCts;
    private bool _isAnimating;
    private DateTime _loadingStartTime;
    private const int MinLoadingDurationMs = 600; // Minimum time to show loading animation

    public PlayerPage(PlayerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;

        // Subscribe to popup request events
        _viewModel.ShowQueueRequested += OnShowQueueRequested;
        _viewModel.ShowAddToPlaylistRequested += OnShowAddToPlaylistRequested;

        // Subscribe to loading state changes for animation
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsLoading))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_viewModel.IsLoading)
                {
                    StartLoadingAnimation();
                }
                else
                {
                    StopLoadingAnimation();
                }
            });
        }
    }

    private async void StartLoadingAnimation()
    {
        if (_isAnimating) return;
        _isAnimating = true;
        _loadingStartTime = DateTime.UtcNow;

        // Make loading container visible immediately
        LoadingBorderContainer.IsVisible = true;

        _loadingAnimationCts?.Cancel();
        _loadingAnimationCts = new CancellationTokenSource();
        var token = _loadingAnimationCts.Token;

        try
        {
            // Run animations concurrently
            var ray1Animation = AnimateRayRotation(LoadingRay1, token);
            var ray2Animation = AnimateRayRotation(LoadingRay2, token);
            var glowAnimation = AnimateGlowPulse(LoadingGlow, token);

            await Task.WhenAll(ray1Animation, ray2Animation, glowAnimation);
        }
        catch (TaskCanceledException)
        {
            // Animation was cancelled, which is expected
        }
    }

    private async Task AnimateRayRotation(Border ray, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await ray.RotateTo(ray.Rotation + 360, 1500, Easing.Linear);
            if (token.IsCancellationRequested) break;
        }
    }

    private async Task AnimateGlowPulse(Border glow, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await glow.FadeTo(1, 400, Easing.SinInOut);
            if (token.IsCancellationRequested) break;
            await glow.FadeTo(0.3, 400, Easing.SinInOut);
            if (token.IsCancellationRequested) break;
        }
    }

    private void StopLoadingAnimation()
    {
        if (!_isAnimating) return;

        _loadingAnimationCts?.Cancel();
        _isAnimating = false;

        // Stop animation immediately without blocking
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                LoadingBorderContainer.IsVisible = false;
                LoadingBorderContainer.Opacity = 1;

                // Reset to initial state
                LoadingRay1.Rotation = 0;
                LoadingRay2.Rotation = 180;
                LoadingGlow.Opacity = 0.6;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlayerPage] Error stopping animation: {ex.Message}");
            }
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Reset the MainGrid position in case it was moved by swipe down animation
        // This fixes the issue where the player appears empty after returning from minimized state
        MainGrid.TranslationY = 0;
        MainGrid.Opacity = 1;

        _viewModel.OnAppearing();

        // Also subscribe directly to update the label
        _playerService = Application.Current?.Handler?.MauiContext?.Services.GetService<IAudioPlayerService>();
        if (_playerService != null)
        {
            _playerService.StateChanged += OnStateChanged;
            _playerService.TrackChanged += OnTrackChanged;
            // Update immediately with current state
            UpdatePlayPauseLabel(_playerService.IsPlaying);

            // Check current loading state and start animation if needed
            var isCurrentlyLoading = _playerService.State == PlaybackState.Loading;
            System.Diagnostics.Debug.WriteLine($"[PlayerPage] OnAppearing: State={_playerService.State}, IsLoading={isCurrentlyLoading}");

            if (isCurrentlyLoading)
            {
                StartLoadingAnimation();
            }
        }
    }

    private void OnTrackChanged(object? sender, Track? track)
    {
        // When a new track starts playing, start the loading animation
        if (track != null)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                System.Diagnostics.Debug.WriteLine($"[PlayerPage] OnTrackChanged: Starting loading animation for {track.FileName}");
                StartLoadingAnimation();
            });
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();

        if (_playerService != null)
        {
            _playerService.StateChanged -= OnStateChanged;
            _playerService.TrackChanged -= OnTrackChanged;
        }

        // Stop loading animation when leaving the page
        _loadingAnimationCts?.Cancel();
        _isAnimating = false;
    }

    private async void OnShowQueueRequested(object? sender, EventArgs e)
    {
        var queue = _viewModel.GetQueue();
        var currentTrack = _viewModel.GetCurrentTrack();
        var currentIndex = _viewModel.GetCurrentIndex();

        var popup = new QueuePopup(queue, currentTrack, currentIndex);
        var result = await this.ShowPopupAsync(popup);

        if (result is string action)
        {
            if (action == "clear")
            {
                _viewModel.ClearQueue();
                await ConfirmationHelper.ShowAlertAsync("Queue", "Queue cleared");
            }
            else if (action == "shuffle")
            {
                _viewModel.ShuffleQueue();
                await ConfirmationHelper.ShowAlertAsync("Queue", "Queue shuffled");
            }
        }
    }

    private async void OnShowAddToPlaylistRequested(object? sender, EventArgs e)
    {
        var currentTrack = _viewModel.GetCurrentTrack();
        if (currentTrack == null)
        {
            await ConfirmationHelper.ShowAlertAsync("Add to Playlist", "No track is currently playing");
            return;
        }

        var playlists = await _viewModel.GetPlaylistsAsync();
        var popup = new PlaylistPickerPopup(playlists, currentTrack.DisplayName);
        var result = await this.ShowPopupAsync(popup) as PlaylistPickerResult;

        if (result != null)
        {
            if (result.CreateNew)
            {
                // Navigate to create playlist page
                await Shell.Current.GoToAsync("//playlists");
                await ConfirmationHelper.ShowAlertAsync("Create Playlist", "Create a new playlist first, then add the track");
            }
            else if (result.SelectedPlaylist != null)
            {
                var success = await _viewModel.AddTrackToPlaylistAsync(result.SelectedPlaylist, currentTrack);
                if (success)
                {
                    await ConfirmationHelper.ShowAlertAsync("Success", $"Added to {result.SelectedPlaylist.Name}");
                }
                else
                {
                    await ConfirmationHelper.ShowAlertAsync("Error", "Failed to add track to playlist");
                }
            }
        }
    }

    private void OnStateChanged(object? sender, PlaybackState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                // Show pause icon for Playing, Buffering, and Loading states
                // Loading = waiting for server to download from Telegram, but user can cancel
                var isPlaying = state == PlaybackState.Playing ||
                               state == PlaybackState.Buffering ||
                               state == PlaybackState.Loading;
                System.Diagnostics.Debug.WriteLine($"[PlayerPage] OnStateChanged: state={state}, isPlaying={isPlaying}");
                UpdatePlayPauseLabel(isPlaying);

                // Handle loading animation directly based on state
                if (state == PlaybackState.Loading)
                {
                    StartLoadingAnimation();
                }
                else
                {
                    // Stop loading animation for all other states (Playing, Paused, Stopped, Error)
                    StopLoadingAnimation();
                }

                // If error state, ensure page is visible and usable
                if (state == PlaybackState.Error || state == PlaybackState.Stopped)
                {
                    // Reset any problematic UI state
                    LoadingBorderContainer.IsVisible = false;
                    LoadingBorderContainer.Opacity = 1;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlayerPage] Error in OnStateChanged: {ex.Message}");
            }
        });
    }

    private void UpdatePlayPauseLabel(bool isPlaying)
    {
        System.Diagnostics.Debug.WriteLine($"[PlayerPage] Updating PlayPause icon, isPlaying: {isPlaying}");
        // Toggle between play and pause icons
        PlayIcon.IsVisible = !isPlaying;
        PauseIcon.IsVisible = isPlaying;
    }

    private void OnSeekCompleted(object? sender, EventArgs e)
    {
        if (sender is Slider slider)
        {
            _viewModel.SeekToPosition(slider.Value);
        }
    }

    private async void OnSwipedDown(object? sender, SwipedEventArgs e)
    {
        // Animate the page sliding down before navigating back
        await MainGrid.TranslateTo(0, Height, 250, Easing.CubicIn);
        await Shell.Current.GoToAsync("..");
    }
}
