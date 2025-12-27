using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TFMAudioApp.Controls;
using TFMAudioApp.Models;
using TFMAudioApp.Services;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.ViewModels;

[QueryProperty(nameof(ChannelId), "id")]
[QueryProperty(nameof(ChannelName), "name")]
public partial class ChannelDetailViewModel : BaseViewModel
{
    private readonly ApiServiceFactory _apiFactory;
    private readonly IAudioPlayerService _playerService;
    private readonly IConnectivityService _connectivityService;
    private readonly ICacheService _cacheService;

    // All files from server (unfiltered)
    private List<ChannelFile> _allFiles = new();

    // Filtered files shown in UI
    public ObservableCollection<ChannelFile> Files { get; } = new();

    // Static cache for channel files (key: channelId_folderId)
    private static readonly Dictionary<string, List<ChannelFile>> _fileCache = new();

    [ObservableProperty]
    private long _channelId;

    [ObservableProperty]
    private string _channelName = string.Empty;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _currentPath = "/";

    [ObservableProperty]
    private string? _currentFolderId;

    // Pagination
    private int _currentPage = 1;
    private const int PageSize = 50;

    [ObservableProperty]
    private bool _hasMorePages = true;

    [ObservableProperty]
    private bool _isLoadingMore;

    // Sorting
    [ObservableProperty]
    private string _sortBy = "name";

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private int _selectedSortIndex;

    public List<string> SortOptions { get; } = new() { "Name", "Date", "Size", "Type" };

    // New Filter System
    [ObservableProperty]
    private bool _showFolders = true;

    [ObservableProperty]
    private List<string>? _selectedExtensions;

    [ObservableProperty]
    private bool _hasActiveFilters;

    [ObservableProperty]
    private int _activeFilterCount;

    private static readonly List<string> AllAudioExtensions = new()
    {
        ".mp3", ".flac", ".ogg", ".opus", ".aac", ".wav", ".m4a", ".wma", ".ape"
    };

    // Search
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchVisible;

    // Stats
    [ObservableProperty]
    private int _totalItems;

    [ObservableProperty]
    private string _statsText = string.Empty;

    // Sync status
    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ChannelDetailViewModel(ApiServiceFactory apiFactory, IAudioPlayerService playerService, IConnectivityService connectivityService, ICacheService cacheService)
    {
        _apiFactory = apiFactory;
        _playerService = playerService;
        _connectivityService = connectivityService;
        _cacheService = cacheService;
        _selectedExtensions = new List<string>(AllAudioExtensions); // All selected by default
    }

    private string GetCacheKey() => $"{ChannelId}_{CurrentFolderId ?? "root"}";

    partial void OnChannelIdChanged(long value)
    {
        if (value > 0)
        {
            LoadFilesCommand.ExecuteAsync(null);
        }
    }

    partial void OnChannelNameChanged(string value)
    {
        Title = value;
    }

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
        if (!IsSearchVisible && !string.IsNullOrEmpty(SearchText))
        {
            SearchText = string.Empty;
            ApplyLocalFilters();
        }
    }

    [RelayCommand]
    private void Search()
    {
        ApplyLocalFilters();
    }

    public async Task ApplyFiltersAsync(FilterResult result)
    {
        ShowFolders = result.ShowFolders;
        SelectedExtensions = result.SelectedExtensions;
        SortBy = result.SortBy;
        SortDescending = result.SortDescending;
        SelectedSortIndex = result.SortBy switch
        {
            "name" => 0,
            "date" => 1,
            "size" => 2,
            "type" => 3,
            _ => 0
        };

        UpdateActiveFilterCount();
        ApplyLocalFilters();
    }

    private void UpdateActiveFilterCount()
    {
        var count = 0;

        // Count non-default filters
        if (!ShowFolders) count++;
        if (SelectedExtensions != null && SelectedExtensions.Count < AllAudioExtensions.Count) count++;
        if (SortBy != "name") count++;
        if (SortDescending) count++;

        ActiveFilterCount = count;
        HasActiveFilters = count > 0;
    }

    private void ApplyLocalFilters(bool isLoadingMore = false)
    {
        var filtered = _allFiles.AsEnumerable();

        // Filter by folders
        if (!ShowFolders)
        {
            filtered = filtered.Where(f => f.IsFile);
        }

        // Filter by extensions (only for files)
        if (SelectedExtensions != null && SelectedExtensions.Count > 0)
        {
            filtered = filtered.Where(f =>
                !f.IsFile || // Keep folders
                SelectedExtensions.Contains(f.Type?.ToLowerInvariant() ?? ""));
        }

        // Filter by search text
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var searchLower = SearchText.ToLowerInvariant();
            filtered = filtered.Where(f => f.Name.ToLowerInvariant().Contains(searchLower));
        }

        // Apply sorting (folders first)
        filtered = (SortBy, SortDescending) switch
        {
            ("name", false) => filtered.OrderBy(f => f.IsFile).ThenBy(f => f.Name),
            ("name", true) => filtered.OrderBy(f => f.IsFile).ThenByDescending(f => f.Name),
            ("date", false) => filtered.OrderBy(f => f.IsFile).ThenBy(f => f.DateModified),
            ("date", true) => filtered.OrderBy(f => f.IsFile).ThenByDescending(f => f.DateModified),
            ("size", false) => filtered.OrderBy(f => f.IsFile).ThenBy(f => f.Size),
            ("size", true) => filtered.OrderBy(f => f.IsFile).ThenByDescending(f => f.Size),
            ("type", false) => filtered.OrderBy(f => f.IsFile).ThenBy(f => f.Type),
            ("type", true) => filtered.OrderBy(f => f.IsFile).ThenByDescending(f => f.Type),
            _ => filtered.OrderBy(f => f.IsFile).ThenBy(f => f.Name)
        };

        var filteredList = filtered.ToList();

        if (isLoadingMore)
        {
            // Only add new items that are not already in the list
            var existingIds = Files.Select(f => f.Id).ToHashSet();
            foreach (var file in filteredList.Where(f => !existingIds.Contains(f.Id)))
            {
                Files.Add(file);
            }
        }
        else
        {
            // Full refresh - clear and add all
            Files.Clear();
            foreach (var file in filteredList)
            {
                Files.Add(file);
            }
        }

        StatsText = $"{Files.Count} of {_allFiles.Count} items";

        // Update cache status for audio files in background
        _ = UpdateCacheStatusAsync();
    }

    private async Task UpdateCacheStatusAsync()
    {
        // Get all audio files to check
        var audioFiles = Files.Where(f => f.IsAudioFile).ToList();
        if (audioFiles.Count == 0) return;

        try
        {
            // Check all cache statuses in parallel for better performance
            var tasks = audioFiles.Select(async f => new
            {
                File = f,
                IsCached = await _cacheService.IsTrackCachedAsync(f.Id)
            });

            var results = await Task.WhenAll(tasks);

            // Update on main thread
            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var result in results)
                {
                    result.File.IsCached = result.IsCached;
                }
            });
        }
        catch
        {
            // Ignore cache check errors
        }
    }

    private async Task ReloadFilesAsync()
    {
        _currentPage = 1;
        HasMorePages = true;
        _allFiles.Clear();
        Files.Clear();
        await LoadFilesAsync();
    }

    [RelayCommand]
    private async Task LoadFilesAsync()
    {
        if (ChannelId == 0) return;

        var cacheKey = GetCacheKey();

        // Always start fresh to prevent duplications
        _allFiles.Clear();
        Files.Clear();

        // Step 1: Load from cache immediately (no loading indicator)
        if (_fileCache.TryGetValue(cacheKey, out var cachedFiles) && cachedFiles.Count > 0)
        {
            // Create new list from cache (defensive copy)
            _allFiles = new List<ChannelFile>(cachedFiles);
            ApplyLocalFilters();
        }
        else
        {
            // First load - show loading indicator
            IsBusy = true;
        }

        // Step 2: Sync with server in background
        await SyncFilesWithServerAsync();

        IsBusy = false;
    }

    private async Task SyncFilesWithServerAsync()
    {
        if (!_connectivityService.IsConnected)
        {
            var cacheKey = GetCacheKey();
            if (!_fileCache.ContainsKey(cacheKey))
            {
                StatusMessage = "Offline - No cached data";
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
                StatusMessage = _allFiles.Count > 0 ? "" : "Not connected to server";
                return;
            }

            // Always request audio_folders from server (folders + audio files only)
            var request = new ChannelFilesRequest
            {
                FolderId = CurrentFolderId,
                Page = _currentPage,
                PageSize = PageSize,
                Filter = "audio_folders", // Always get audio files and folders
                SortBy = "name", // Default sort, we'll sort locally
                SortDescending = false
            };

            var result = await api.GetChannelFilesAsync(ChannelId, request);

            if (result.Success && result.Data != null)
            {
                if (_currentPage == 1)
                {
                    // Clear both _allFiles and Files to prevent duplication
                    _allFiles.Clear();
                    Files.Clear();
                }

                // Add files, avoiding duplicates by checking ID
                var existingIds = _allFiles.Select(f => f.Id).ToHashSet();
                foreach (var file in result.Data)
                {
                    if (!existingIds.Contains(file.Id))
                    {
                        _allFiles.Add(file);
                        existingIds.Add(file.Id);
                    }
                }

                // Update cache
                var cacheKey = GetCacheKey();
                _fileCache[cacheKey] = new List<ChannelFile>(_allFiles);

                // Update pagination info
                if (result.Pagination != null)
                {
                    TotalItems = result.Pagination.TotalItems;
                    HasMorePages = result.Pagination.HasNext;
                }
                else
                {
                    HasMorePages = result.Data.Count >= PageSize;
                }

                // Apply local filters
                ApplyLocalFilters();
                StatusMessage = "";
            }
            else
            {
                StatusMessage = _allFiles.Count > 0 ? "" : (result.Error ?? "Failed to load files");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = _allFiles.Count > 0 ? "" : $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsLoadingMore || !HasMorePages || IsBusy) return;

        IsLoadingMore = true;
        try
        {
            _currentPage++;

            var api = await _apiFactory.GetApiServiceAsync();
            if (api == null) return;

            var request = new ChannelFilesRequest
            {
                FolderId = CurrentFolderId,
                Page = _currentPage,
                PageSize = PageSize,
                Filter = "audio_folders",
                SortBy = "name",
                SortDescending = false
            };

            var result = await api.GetChannelFilesAsync(ChannelId, request);

            if (result.Success && result.Data != null)
            {
                foreach (var file in result.Data)
                    _allFiles.Add(file);

                if (result.Pagination != null)
                {
                    HasMorePages = result.Pagination.HasNext;
                }
                else
                {
                    HasMorePages = result.Data.Count >= PageSize;
                }

                // Re-apply local filters to include new items (keep scroll position)
                ApplyLocalFilters(isLoadingMore: true);
            }
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        _currentPage = 1;
        HasMorePages = true;
        await LoadFilesAsync();
        IsRefreshing = false;
    }

    [RelayCommand]
    private async Task OpenFileAsync(ChannelFile file)
    {
        if (!file.IsFile)
        {
            // Navigate into folder
            CurrentFolderId = file.Id;
            CurrentPath = file.Path;
            _currentPage = 1;
            HasMorePages = true;
            _allFiles.Clear();
            Files.Clear();
            await LoadFilesAsync();
        }
        else if (IsAudioFile(file))
        {
            // Convert ChannelFile to Track and play
            var track = ConvertToTrack(file);

            // Get all audio files in current view for queue
            var audioFiles = Files.Where(f => f.IsFile && IsAudioFile(f)).ToList();
            var tracks = audioFiles.Select(ConvertToTrack).ToList();
            var index = tracks.FindIndex(t => t.FileId == track.FileId);

            if (index >= 0)
            {
                // Disable shuffle when user explicitly selects a specific track
                _playerService.ShuffleEnabled = false;
                await _playerService.PlayAsync(tracks, index);
                await Shell.Current.GoToAsync("player");
            }
        }
    }

    /// <summary>
    /// Check if a file is an audio file by category or extension
    /// </summary>
    private static bool IsAudioFile(ChannelFile file)
    {
        // Check by category first
        if (file.Category == "Audio")
            return true;

        // Fallback: check by extension
        var ext = Path.GetExtension(file.Name)?.ToLowerInvariant();
        return !string.IsNullOrEmpty(ext) && Helpers.Constants.AudioExtensions.Contains(ext);
    }

    private Track ConvertToTrack(ChannelFile file)
    {
        return new Track
        {
            FileId = file.Id,
            MessageId = file.MessageId,
            ChannelId = ChannelId.ToString(),
            ChannelName = ChannelName,
            FileName = file.Name,
            FilePath = file.Path,
            FileType = file.Type ?? "audio/mpeg",
            FileSize = file.Size,
            IsLocalFile = false,
            StreamUrl = file.StreamUrl ?? string.Empty
        };
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (string.IsNullOrEmpty(CurrentFolderId))
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        // Go up one level - for now just reload root
        CurrentFolderId = null;
        CurrentPath = "/";
        _currentPage = 1;
        HasMorePages = true;
        _allFiles.Clear();
        Files.Clear();
        await LoadFilesAsync();
    }

    [RelayCommand]
    private async Task AddToPlaylistAsync(ChannelFile file)
    {
        var api = await _apiFactory.GetApiServiceAsync();
        if (api == null)
        {
            await ConfirmationHelper.ShowErrorAsync("Error", "Not connected to server");
            return;
        }

        // Get all playlists
        var playlistsResult = await api.GetAllPlaylistsAsync();
        if (!playlistsResult.Success || playlistsResult.Data == null)
        {
            await ConfirmationHelper.ShowErrorAsync("Error", "Failed to load playlists");
            return;
        }

        var playlists = playlistsResult.Data;

        // Show custom playlist picker popup
        var popup = new PlaylistPickerPopup(playlists, file.Name);
        var currentPage = Shell.Current.CurrentPage;
        var result = await currentPage.ShowPopupAsync(popup) as PlaylistPickerResult;

        if (result == null) return;

        if (result.CreateNew)
        {
            await Shell.Current.GoToAsync("playlists");
            return;
        }

        if (result.SelectedPlaylist == null) return;

        // Add track to playlist
        try
        {
            var request = new AddTrackRequest
            {
                FileId = file.Id,
                ChannelId = ChannelId.ToString(),
                ChannelName = ChannelName,
                FileName = file.Name,
                FilePath = file.Path,
                FileType = file.Type ?? "audio/mpeg",
                FileSize = file.Size
            };

            var addResult = await api.AddTrackToPlaylistAsync(result.SelectedPlaylist.Id, request);
            if (addResult.Success)
            {
                await ConfirmationHelper.ShowSuccessAsync("Success", $"Added to '{result.SelectedPlaylist.Name}'");
            }
            else
            {
                await ConfirmationHelper.ShowErrorAsync("Error", addResult.Error ?? "Failed to add track");
            }
        }
        catch (Exception ex)
        {
            await ConfirmationHelper.ShowErrorAsync("Error", $"Failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DownloadFileAsync(ChannelFile file)
    {
        if (!IsAudioFile(file)) return;

        var track = ConvertToTrack(file);
        var downloadService = Application.Current?.Handler?.MauiContext?.Services.GetService<IDownloadService>();

        if (downloadService == null)
        {
            await ConfirmationHelper.ShowErrorAsync("Error", "Download service not available");
            return;
        }

        await downloadService.EnqueueAsync(track);
        await ConfirmationHelper.ShowSuccessAsync("Download Started", $"'{file.Name}' added to download queue");
    }

    [RelayCommand]
    private async Task PlayAllAudioAsync()
    {
        var audioFiles = Files.Where(f => f.IsFile && IsAudioFile(f)).ToList();
        if (audioFiles.Count == 0)
        {
            await ConfirmationHelper.ShowAlertAsync("No Audio", "No audio files in this folder");
            return;
        }

        var tracks = audioFiles.Select(ConvertToTrack).ToList();
        await _playerService.PlayAsync(tracks, 0);
        await Shell.Current.GoToAsync("player");
    }
}
