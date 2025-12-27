using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TFMAudioApp.Controls;
using TFMAudioApp.Data.Entities;
using TFMAudioApp.Models;
using TFMAudioApp.Services;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.ViewModels;

[QueryProperty(nameof(PlaylistId), "id")]
[QueryProperty(nameof(PlaylistName), "name")]
public partial class PlaylistDetailViewModel : BaseViewModel
{
    private readonly ApiServiceFactory _apiFactory;
    private readonly IAudioPlayerService _playerService;
    private readonly IDownloadService _downloadService;
    private readonly ICacheService _cacheService;
    private readonly IDownloadNotificationService _downloadNotificationService;

    public ObservableCollection<Track> Tracks { get; } = new();

    [ObservableProperty]
    private string _playlistId = string.Empty;

    [ObservableProperty]
    private string _playlistName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private PlaylistDetail? _playlist;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadStatus = string.Empty;

    /// <summary>
    /// Status text for display in header (e.g., "3 of 10 tracks downloaded")
    /// </summary>
    public string DownloadStatusText => IsDownloading
        ? $"{_downloadedCount} of {_totalToDownload} tracks downloaded"
        : string.Empty;

    public bool IsNotDownloading => !IsDownloading;

    [ObservableProperty]
    private bool _isPlaylistCached;

    [ObservableProperty]
    private bool _isPlaylistMarkedForSync;

    [ObservableProperty]
    private bool _isOfflineMode;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// True if we can show the download button (not downloading and not already marked for sync)
    /// </summary>
    public bool CanDownload => !IsDownloading && !IsPlaylistMarkedForSync;

    /// <summary>
    /// True if we can show the remove offline button
    /// </summary>
    public bool CanRemoveOffline => IsPlaylistMarkedForSync && !IsDownloading;

    public PlaylistDetailViewModel(ApiServiceFactory apiFactory, IAudioPlayerService playerService, IDownloadService downloadService, ICacheService cacheService, IDownloadNotificationService downloadNotificationService)
    {
        _apiFactory = apiFactory;
        _playerService = playerService;
        _downloadService = downloadService;
        _cacheService = cacheService;
        _downloadNotificationService = downloadNotificationService;

        // Subscribe to download events
        _downloadService.DownloadProgress += OnDownloadProgress;
        _downloadService.DownloadCompleted += OnDownloadCompleted;
        _downloadService.DownloadFailed += OnDownloadFailed;

        // Subscribe to cache events for real-time updates
        _cacheService.TrackCached += OnTrackCached;
    }

    private void OnTrackCached(object? sender, string trackId)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var track = Tracks.FirstOrDefault(t => t.FileId == trackId);
            if (track != null)
            {
                track.IsCached = true;

                // Update duration from cache
                var cachedInfo = await _cacheService.GetCachedTrackInfoAsync(trackId);
                if (cachedInfo?.Duration.HasValue == true && cachedInfo.Duration > 0)
                {
                    track.Duration = cachedInfo.Duration;
                }

                // Check if playlist is now fully cached
                await UpdatePlaylistCacheStatusAsync();
            }
        });
    }

    private async Task UpdatePlaylistCacheStatusAsync()
    {
        var allCached = Tracks.Count > 0 && Tracks.All(t => t.IsCached);
        IsPlaylistCached = allCached;

        // Check if playlist is marked for sync (offline mode)
        var markedForSync = await _cacheService.IsPlaylistMarkedForSyncAsync(PlaylistId);

        // Force update even if value hasn't changed to ensure UI reflects current state
        if (_isPlaylistMarkedForSync != markedForSync)
        {
            IsPlaylistMarkedForSync = markedForSync;
        }
        else
        {
            // Explicitly notify even if unchanged (for initial load)
            OnPropertyChanged(nameof(IsPlaylistMarkedForSync));
            OnPropertyChanged(nameof(CanDownload));
            OnPropertyChanged(nameof(CanRemoveOffline));
        }

        if (Playlist != null)
        {
            Playlist.IsFullyCached = allCached;
            Playlist.IsPartiallyCached = Tracks.Any(t => t.IsCached) && !allCached;
            Playlist.CachedTrackCount = Tracks.Count(t => t.IsCached);
        }
    }

    partial void OnIsDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotDownloading));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanRemoveOffline));
    }

    partial void OnIsPlaylistMarkedForSyncChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanRemoveOffline));
    }

    partial void OnPlaylistIdChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            LoadPlaylistCommand.ExecuteAsync(null);
        }
    }

    partial void OnPlaylistNameChanged(string value)
    {
        Title = value;
    }

    [RelayCommand]
    private async Task LoadPlaylistAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(PlaylistId)) return;

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            // Step 1: Try to load from offline cache first (instant UI)
            var loadedFromCache = await LoadFromOfflineCacheAsync();

            // Step 2: Try to sync with server
            await SyncPlaylistFromServerAsync(loadedFromCache);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> LoadFromOfflineCacheAsync()
    {
        try
        {
            var offlinePlaylist = await _cacheService.GetOfflinePlaylistAsync(PlaylistId);

            if (offlinePlaylist != null)
            {
                Playlist = offlinePlaylist;
                Title = offlinePlaylist.Name;
                Description = offlinePlaylist.Description ?? string.Empty;

                Tracks.Clear();
                foreach (var track in offlinePlaylist.Tracks)
                    Tracks.Add(track);

                // Update cache status for all tracks
                await _cacheService.UpdateTracksCacheStatusAsync(Tracks);
                await UpdatePlaylistCacheStatusAsync();

                StatusMessage = "Loaded from cache";
                return true;
            }
        }
        catch
        {
            // Ignore cache loading errors
        }

        return false;
    }

    private async Task SyncPlaylistFromServerAsync(bool hasOfflineData)
    {
        try
        {
            StatusMessage = hasOfflineData ? "Syncing..." : "";

            var api = await _apiFactory.GetApiServiceAsync();
            if (api == null)
            {
                IsOfflineMode = true;
                if (!hasOfflineData)
                {
                    ErrorMessage = "Cannot connect to server";
                }
                else
                {
                    StatusMessage = "Offline mode";
                }
                return;
            }

            var result = await api.GetPlaylistAsync(PlaylistId);

            if (result.Success && result.Data != null)
            {
                IsOfflineMode = false;
                StatusMessage = string.Empty;

                Playlist = result.Data;
                Title = result.Data.Name;
                Description = result.Data.Description ?? string.Empty;

                Tracks.Clear();
                foreach (var track in result.Data.Tracks)
                    Tracks.Add(track);

                // Check cache status for all tracks
                await _cacheService.UpdateTracksCacheStatusAsync(Tracks);
                await UpdatePlaylistCacheStatusAsync();

                // If playlist is marked for sync, update offline data and sync new tracks
                if (await _cacheService.IsPlaylistMarkedForSyncAsync(PlaylistId))
                {
                    await _cacheService.SavePlaylistOfflineAsync(result.Data);
                    // Sync any new tracks in background
                    _ = _cacheService.SyncCachedPlaylistsAsync();
                }
            }
            else
            {
                // Server error - use cached data if available
                if (hasOfflineData)
                {
                    IsOfflineMode = true;
                    StatusMessage = "Using cached data";
                }
                else
                {
                    ErrorMessage = result.Error ?? "Failed to load playlist";
                }
            }
        }
        catch (Exception)
        {
            // Network error - fall back to offline mode
            IsOfflineMode = true;
            if (hasOfflineData)
            {
                StatusMessage = "Offline mode";
            }
            else
            {
                ErrorMessage = "Cannot connect to server";
            }
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadPlaylistAsync();
        IsRefreshing = false;
    }

    [RelayCommand]
    private async Task PlayTrackAsync(Track track)
    {
        var index = Tracks.IndexOf(track);
        if (index >= 0)
        {
            // Disable shuffle when user explicitly selects a specific track
            _playerService.ShuffleEnabled = false;
            await _playerService.PlayAsync(Tracks, index);
            await Shell.Current.GoToAsync("player");
        }
    }

    [RelayCommand]
    private async Task RemoveTrackAsync(Track track)
    {
        var confirmed = await ConfirmationHelper.ShowConfirmAsync(
            "Remove Track",
            $"Remove '{track.DisplayName}' from playlist?",
            "Remove",
            "Cancel",
            "",
            true);

        if (!confirmed) return;

        var api = await _apiFactory.GetApiServiceAsync();
        if (api == null) return;

        try
        {
            await api.RemoveTrackFromPlaylistAsync(PlaylistId, track.FileId);
            Tracks.Remove(track);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to remove track: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PlayAllAsync()
    {
        if (Tracks.Count == 0) return;
        await _playerService.PlayAsync(Tracks, 0);
        await Shell.Current.GoToAsync("player");
    }

    [RelayCommand]
    private async Task ShufflePlayAsync()
    {
        if (Tracks.Count == 0) return;
        _playerService.ShuffleEnabled = true;
        await _playerService.PlayAsync(Tracks, 0);
        await Shell.Current.GoToAsync("player");
    }

    [RelayCommand]
    private async Task DownloadPlaylistAsync()
    {
        if (Tracks.Count == 0)
        {
            await ConfirmationHelper.ShowAlertAsync("No Tracks", "This playlist has no tracks to download.");
            return;
        }

        var confirmed = await ConfirmationHelper.ShowConfirmAsync(
            "Download Playlist",
            $"Download {Tracks.Count} tracks for offline listening?\n\nNew tracks added to this playlist will be downloaded automatically.",
            "Download",
            "Cancel");

        if (!confirmed) return;

        try
        {
            IsDownloading = true;
            DownloadProgress = 0;
            _downloadedCount = 0;
            _totalToDownload = Tracks.Count;
            DownloadStatus = $"0 / {Tracks.Count}";

            // Save playlist for offline access and mark for auto-sync
            if (Playlist != null)
            {
                await _cacheService.SavePlaylistOfflineAsync(Playlist);
                await _cacheService.MarkPlaylistForSyncAsync(PlaylistId);
            }

            var items = await _downloadService.EnqueueAsync(Tracks);

            if (items.Count == 0)
            {
                IsDownloading = false;
                IsPlaylistCached = true;
                await ConfirmationHelper.ShowSuccessAsync("Already Downloaded", "All tracks are already available offline.");
                return;
            }

            _totalToDownload = items.Count;
            DownloadStatus = $"Downloading {items.Count} tracks...";

            // Update status
            IsPlaylistMarkedForSync = true;
        }
        catch (Exception ex)
        {
            IsDownloading = false;
            await ConfirmationHelper.ShowErrorAsync("Error", $"Failed to start download: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RemoveFromOfflineAsync()
    {
        var cachedCount = Tracks.Count(t => t.IsCached);
        var message = cachedCount > 0
            ? $"Remove this playlist from offline mode?\n\nThis will delete {cachedCount} cached track(s) from your device."
            : "Remove this playlist from offline mode?";

        var confirmed = await ConfirmationHelper.ShowConfirmAsync(
            "Remove from Offline",
            message,
            "Remove",
            "Cancel",
            "",
            true);

        if (!confirmed) return;

        try
        {
            IsBusy = true;
            var deletedTracks = await _cacheService.RemoveOfflinePlaylistWithTracksAsync(PlaylistId);

            // Update UI
            IsPlaylistMarkedForSync = false;
            IsPlaylistCached = false;

            // Update track cache status
            foreach (var track in Tracks)
            {
                track.IsCached = await _cacheService.IsTrackCachedAsync(track.FileId);
            }

            await ConfirmationHelper.ShowSuccessAsync(
                "Removed from Offline",
                deletedTracks > 0
                    ? $"Playlist removed from offline mode. {deletedTracks} track(s) deleted."
                    : "Playlist removed from offline mode.");
        }
        catch (Exception ex)
        {
            await ConfirmationHelper.ShowErrorAsync("Error", $"Failed to remove from offline: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CancelDownloadAsync()
    {
        var confirmed = await ConfirmationHelper.ShowConfirmAsync(
            "Cancel Download",
            "Cancel the download and remove already downloaded tracks from this playlist?",
            "Yes, Cancel",
            "No, Continue",
            "",
            true);

        if (!confirmed) return;

        try
        {
            // Cancel all pending downloads for this playlist
            var trackIds = Tracks.Select(t => t.FileId).ToHashSet();
            await _downloadService.CancelDownloadsAsync(trackIds);

            // Reset download state for all tracks
            foreach (var track in Tracks)
            {
                track.IsDownloading = false;
                track.DownloadProgress = 0;
            }

            // Delete already downloaded tracks from this playlist
            var deletedCount = 0;
            foreach (var track in Tracks.Where(t => t.IsCached))
            {
                if (await _cacheService.DeleteCachedTrackAsync(track.FileId))
                {
                    track.IsCached = false;
                    deletedCount++;
                }
            }

            // Remove playlist from offline marking
            await _cacheService.UnmarkPlaylistForSyncAsync(PlaylistId);
            await _cacheService.RemoveOfflinePlaylistAsync(PlaylistId);

            // Reset UI state
            IsDownloading = false;
            IsPlaylistMarkedForSync = false;
            IsPlaylistCached = false;
            DownloadProgress = 0;
            DownloadStatus = "";
            _downloadedCount = 0;
            _totalToDownload = 0;
            OnPropertyChanged(nameof(DownloadStatusText));
            OnPropertyChanged(nameof(CanDownload));
            OnPropertyChanged(nameof(CanRemoveOffline));

            _downloadNotificationService.CancelNotification();

            await ConfirmationHelper.ShowSuccessAsync(
                "Download Cancelled",
                deletedCount > 0
                    ? $"Download cancelled. {deletedCount} track(s) removed."
                    : "Download cancelled.");
        }
        catch (Exception ex)
        {
            await ConfirmationHelper.ShowErrorAsync("Error", $"Failed to cancel: {ex.Message}");
        }
    }

    private int _downloadedCount;
    private int _totalToDownload;

    private void OnDownloadProgress(object? sender, (DownloadItem item, double progress) args)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Find the track in our playlist
            var track = Tracks.FirstOrDefault(t => t.FileId == args.item.TrackId);
            if (track != null)
            {
                // Update individual track progress
                track.IsDownloading = true;
                track.DownloadProgress = args.progress;

                // Update overall playlist progress
                DownloadProgress = (_downloadedCount + args.progress) / _totalToDownload;
                OnPropertyChanged(nameof(DownloadStatusText));

                // Update notification
                _downloadNotificationService.ShowDownloadProgress(
                    PlaylistName,
                    _downloadedCount + 1,
                    _totalToDownload,
                    DownloadProgress);
            }
        });
    }

    private void OnDownloadCompleted(object? sender, DownloadItem item)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var track = Tracks.FirstOrDefault(t => t.FileId == item.TrackId);
            if (track != null)
            {
                // Mark track as completed
                track.IsDownloading = false;
                track.DownloadProgress = 0;
                track.IsCached = true;

                _downloadedCount++;
                DownloadStatus = $"{_downloadedCount} / {_totalToDownload}";
                DownloadProgress = (double)_downloadedCount / _totalToDownload;
                OnPropertyChanged(nameof(DownloadStatusText));

                // Update notification
                _downloadNotificationService.ShowDownloadProgress(
                    PlaylistName,
                    _downloadedCount,
                    _totalToDownload,
                    DownloadProgress);

                if (_downloadedCount >= _totalToDownload)
                {
                    IsDownloading = false;
                    DownloadStatus = "Download complete!";
                    OnPropertyChanged(nameof(DownloadStatusText));
                    _downloadNotificationService.ShowDownloadComplete(PlaylistName, _downloadedCount);
                }
            }
        });
    }

    private void OnDownloadFailed(object? sender, (DownloadItem item, string error) args)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var track = Tracks.FirstOrDefault(t => t.FileId == args.item.TrackId);
            if (track != null)
            {
                // Mark track as failed
                track.IsDownloading = false;
                track.DownloadProgress = 0;

                _downloadedCount++;
                DownloadStatus = $"{_downloadedCount} / {_totalToDownload} (some failed)";
                DownloadProgress = (double)_downloadedCount / _totalToDownload;
                OnPropertyChanged(nameof(DownloadStatusText));

                if (_downloadedCount >= _totalToDownload)
                {
                    IsDownloading = false;
                    OnPropertyChanged(nameof(DownloadStatusText));
                    _downloadNotificationService.ShowDownloadError(PlaylistName, "Some tracks failed to download");
                }
            }
        });
    }
}
