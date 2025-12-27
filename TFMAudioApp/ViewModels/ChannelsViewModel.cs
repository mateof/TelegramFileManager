using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text.Json;
using TFMAudioApp.Models;
using TFMAudioApp.Services;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.ViewModels;

public partial class ChannelsViewModel : BaseViewModel
{
    private readonly ApiServiceFactory _apiFactory;
    private readonly IConnectivityService _connectivityService;

    // Source collections (unfiltered)
    private List<Channel> _allChannelsSource = new();
    private List<Channel> _favoriteChannelsSource = new();
    private List<Channel> _ownedChannelsSource = new();

    // Displayed collections (filtered)
    public ObservableCollection<Channel> AllChannels { get; } = new();
    public ObservableCollection<Channel> FavoriteChannels { get; } = new();
    public ObservableCollection<Channel> OwnedChannels { get; } = new();
    public ObservableCollection<ChannelFolder> Folders { get; } = new();

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Static cache to persist across navigations
    private static List<Channel>? _cachedAllChannels;
    private static List<Channel>? _cachedFavoriteChannels;
    private static List<ChannelFolder>? _cachedFolders;
    private static DateTime _lastSyncTime = DateTime.MinValue;
    private static readonly TimeSpan SyncCooldown = TimeSpan.FromMinutes(2); // Don't sync more than once per 2 minutes

    public ChannelsViewModel(ApiServiceFactory apiFactory, IConnectivityService connectivityService)
    {
        _apiFactory = apiFactory;
        _connectivityService = connectivityService;
        Title = "Channels";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        // Step 1: Load from cache immediately (no loading indicator)
        var hasCache = _cachedAllChannels != null || _cachedFavoriteChannels != null || _cachedFolders != null;

        if (hasCache)
        {
            LoadFromCache();

            // Skip server sync if we synced recently (improves navigation speed)
            if (DateTime.UtcNow - _lastSyncTime < SyncCooldown)
            {
                return;
            }
        }
        else
        {
            // First load ever - show loading indicator
            IsBusy = true;
        }

        // Step 2: Sync with server in background (don't await to not block navigation)
        _ = SyncWithServerAsync().ContinueWith(_ => IsBusy = false, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void LoadFromCache()
    {
        if (_cachedAllChannels != null)
        {
            _allChannelsSource = _cachedAllChannels;
            _ownedChannelsSource = _cachedAllChannels.Where(c => c.IsOwner).ToList();
        }

        if (_cachedFavoriteChannels != null)
        {
            _favoriteChannelsSource = _cachedFavoriteChannels;
        }

        if (_cachedFolders != null)
        {
            Folders.Clear();
            foreach (var folder in _cachedFolders)
                Folders.Add(folder);
        }

        ApplySearchFilter();
    }

    private async Task SyncWithServerAsync()
    {
        if (!_connectivityService.IsConnected)
        {
            if (_cachedAllChannels == null)
            {
                StatusMessage = "Offline mode - No cached data";
            }
            return;
        }

        IsSyncing = true;
        StatusMessage = "Syncing...";

        try
        {
            var api = await _apiFactory.GetApiServiceAsync();
            if (api == null)
            {
                StatusMessage = _cachedAllChannels != null ? "" : "Not connected to server";
                return;
            }

            // Load all data in parallel
            var allTask = api.GetAllChannelsAsync();
            var favTask = api.GetFavoriteChannelsAsync();
            var foldersTask = api.GetChannelsWithFoldersAsync();

            await Task.WhenAll(allTask, favTask, foldersTask);

            // Update source collections and cache
            var allResult = await allTask;
            if (allResult.Success && allResult.Data != null)
            {
                _allChannelsSource = allResult.Data;
                _ownedChannelsSource = allResult.Data.Where(c => c.IsOwner).ToList();
                _cachedAllChannels = allResult.Data;
            }

            var favResult = await favTask;
            if (favResult.Success && favResult.Data != null)
            {
                _favoriteChannelsSource = favResult.Data;
                _cachedFavoriteChannels = favResult.Data;
            }

            var foldersResult = await foldersTask;
            if (foldersResult.Success && foldersResult.Data != null)
            {
                Folders.Clear();
                foreach (var folder in foldersResult.Data.Folders)
                    Folders.Add(folder);
                _cachedFolders = foldersResult.Data.Folders.ToList();
            }

            // Apply current search filter
            ApplySearchFilter();
            StatusMessage = "";
            _lastSyncTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            StatusMessage = _cachedAllChannels != null ? "" : $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private void ApplySearchFilter()
    {
        var query = SearchQuery?.Trim().ToLowerInvariant() ?? string.Empty;

        // Filter and update AllChannels
        AllChannels.Clear();
        var filteredAll = string.IsNullOrEmpty(query)
            ? _allChannelsSource
            : _allChannelsSource.Where(c => c.Name.ToLowerInvariant().Contains(query));
        foreach (var channel in filteredAll)
            AllChannels.Add(channel);

        // Filter and update FavoriteChannels
        FavoriteChannels.Clear();
        var filteredFav = string.IsNullOrEmpty(query)
            ? _favoriteChannelsSource
            : _favoriteChannelsSource.Where(c => c.Name.ToLowerInvariant().Contains(query));
        foreach (var channel in filteredFav)
            FavoriteChannels.Add(channel);

        // Filter and update OwnedChannels
        OwnedChannels.Clear();
        var filteredOwned = string.IsNullOrEmpty(query)
            ? _ownedChannelsSource
            : _ownedChannelsSource.Where(c => c.Name.ToLowerInvariant().Contains(query));
        foreach (var channel in filteredOwned)
            OwnedChannels.Add(channel);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadDataAsync();
        IsRefreshing = false;
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(Channel channel)
    {
        var api = await _apiFactory.GetApiServiceAsync();
        if (api == null) return;

        try
        {
            if (channel.IsFavorite)
            {
                await api.RemoveFromFavoritesAsync(channel.Id);
                channel.IsFavorite = false;
                FavoriteChannels.Remove(channel);
            }
            else
            {
                await api.AddToFavoritesAsync(channel.Id);
                channel.IsFavorite = true;
                if (!FavoriteChannels.Contains(channel))
                    FavoriteChannels.Add(channel);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to update favorite: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task NavigateToChannelAsync(Channel channel)
    {
        await Shell.Current.GoToAsync($"channeldetail?id={channel.Id}&name={Uri.EscapeDataString(channel.Name)}");
    }

    [RelayCommand]
    private async Task SelectFolderAsync(ChannelFolder folder)
    {
        if (folder == null) return;

        // Navigate to folder detail page
        var folderJson = System.Text.Json.JsonSerializer.Serialize(folder);
        await Shell.Current.GoToAsync($"folderdetail?folder={Uri.EscapeDataString(folderJson)}");
    }

    partial void OnSearchQueryChanged(string value)
    {
        // Apply filter when search query changes
        ApplySearchFilter();
    }
}
