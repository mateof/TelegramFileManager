using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TFMAudioApp.Controls;
using TFMAudioApp.Models;
using TFMAudioApp.Services;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.ViewModels;

public partial class PlaylistsViewModel : BaseViewModel
{
    private readonly ApiServiceFactory _apiFactory;
    private readonly ICacheService _cacheService;
    private readonly IConnectivityService _connectivityService;

    public ObservableCollection<Playlist> Playlists { get; } = new();

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _newPlaylistName = string.Empty;

    [ObservableProperty]
    private bool _isCreatingPlaylist;

    [ObservableProperty]
    private bool _isOfflineMode;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Static cache to improve navigation performance
    private static List<Playlist>? _cachedPlaylists;
    private static DateTime _lastSyncTime = DateTime.MinValue;
    private static readonly TimeSpan SyncCooldown = TimeSpan.FromMinutes(2);

    public PlaylistsViewModel(ApiServiceFactory apiFactory, ICacheService cacheService, IConnectivityService connectivityService)
    {
        _apiFactory = apiFactory;
        _cacheService = cacheService;
        _connectivityService = connectivityService;
        Title = "Playlists";
    }

    [RelayCommand]
    private async Task LoadPlaylistsAsync()
    {
        if (IsBusy) return;

        ErrorMessage = string.Empty;

        // Step 1: Load from cache immediately if available
        if (_cachedPlaylists != null && _cachedPlaylists.Count > 0)
        {
            Playlists.Clear();
            foreach (var playlist in _cachedPlaylists)
                Playlists.Add(playlist);

            // Skip server sync if we synced recently
            if (DateTime.UtcNow - _lastSyncTime < SyncCooldown)
            {
                return;
            }

            // Sync in background without blocking
            _ = SyncWithServerAsync();
            return;
        }

        // First load - show loading and wait
        IsBusy = true;

        try
        {
            // Load offline playlists immediately
            await LoadOfflinePlaylistsAsync();

            // Sync with server
            await SyncWithServerAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadOfflinePlaylistsAsync()
    {
        try
        {
            var offlinePlaylists = await _cacheService.GetOfflinePlaylistsAsync();

            if (offlinePlaylists.Count > 0)
            {
                Playlists.Clear();
                foreach (var playlist in offlinePlaylists)
                {
                    playlist.IsFullyCached = true; // Offline playlists are cached
                    Playlists.Add(playlist);
                }

                StatusMessage = $"{offlinePlaylists.Count} offline playlist(s)";
            }
        }
        catch
        {
            // Ignore errors loading offline playlists
        }
    }

    private async Task SyncWithServerAsync()
    {
        // Check connectivity first
        if (!_connectivityService.IsConnected)
        {
            IsOfflineMode = true;
            StatusMessage = Playlists.Count > 0
                ? $"Offline mode • {Playlists.Count} playlist(s) available"
                : "Offline mode • No cached playlists";
            return;
        }

        try
        {
            IsSyncing = true;
            StatusMessage = "Syncing...";

            var api = await _apiFactory.GetApiServiceAsync();
            if (api == null)
            {
                IsOfflineMode = true;
                StatusMessage = Playlists.Count > 0
                    ? $"Offline mode • {Playlists.Count} playlist(s) available"
                    : "Cannot connect to server";
                return;
            }

            var result = await api.GetAllPlaylistsAsync();

            if (result.Success && result.Data != null)
            {
                IsOfflineMode = false;

                // Update cache
                _cachedPlaylists = result.Data.ToList();
                _lastSyncTime = DateTime.UtcNow;

                Playlists.Clear();
                foreach (var playlist in result.Data)
                {
                    Playlists.Add(playlist);
                }

                // Update cache status in background (don't block UI)
                _ = UpdatePlaylistsCacheStatusAsync();

                StatusMessage = string.Empty;
            }
            else
            {
                // Server returned error but we might have offline data
                if (Playlists.Count > 0)
                {
                    IsOfflineMode = true;
                    StatusMessage = "Using cached data";
                }
                else
                {
                    ErrorMessage = result.Error ?? "Failed to load playlists";
                }
            }
        }
        catch (Exception ex)
        {
            // Network error - fall back to offline mode
            IsOfflineMode = true;
            if (Playlists.Count > 0)
            {
                StatusMessage = "Offline mode • Using cached data";
            }
            else
            {
                ErrorMessage = $"Cannot connect: {ex.Message}";
            }
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private async Task UpdatePlaylistsCacheStatusAsync()
    {
        foreach (var playlist in Playlists)
        {
            try
            {
                var (isFullyCached, isPartiallyCached, cachedCount) = await _cacheService.GetPlaylistCacheStatusAsync(playlist.Id);
                playlist.IsFullyCached = isFullyCached;
                playlist.IsPartiallyCached = isPartiallyCached;
                playlist.CachedTrackCount = cachedCount;
            }
            catch
            {
                // Ignore cache check errors
            }
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadPlaylistsAsync();
        IsRefreshing = false;
    }

    [RelayCommand]
    private async Task CreatePlaylistAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPlaylistName)) return;

        var api = await _apiFactory.GetApiServiceAsync();
        if (api == null) return;

        try
        {
            IsCreatingPlaylist = true;
            var request = new CreatePlaylistRequest
            {
                Name = NewPlaylistName.Trim(),
                Description = ""
            };

            var result = await api.CreatePlaylistAsync(request);
            if (result.Success && result.Data != null)
            {
                Playlists.Insert(0, result.Data);
                NewPlaylistName = string.Empty;
            }
            else
            {
                ErrorMessage = result.Error ?? "Failed to create playlist";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create playlist: {ex.Message}";
        }
        finally
        {
            IsCreatingPlaylist = false;
        }
    }

    [RelayCommand]
    private async Task DeletePlaylistAsync(Playlist playlist)
    {
        var confirmed = await ConfirmationHelper.ShowConfirmAsync(
            "Delete Playlist",
            $"Are you sure you want to delete '{playlist.Name}'?",
            "Delete",
            "Cancel",
            "",
            true);

        if (!confirmed) return;

        var api = await _apiFactory.GetApiServiceAsync();
        if (api == null) return;

        try
        {
            await api.DeletePlaylistAsync(playlist.Id);
            Playlists.Remove(playlist);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete playlist: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task NavigateToPlaylistAsync(Playlist playlist)
    {
        await Shell.Current.GoToAsync($"playlistdetail?id={playlist.Id}&name={playlist.Name}");
    }
}
