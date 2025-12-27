using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TFMAudioApp.Controls;
using TFMAudioApp.Helpers;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.ViewModels;

public partial class DownloadsViewModel : BaseViewModel
{
    private readonly IDownloadService _downloadService;
    private readonly ICacheService _cacheService;

    public ObservableCollection<DownloadItem> Downloads { get; } = new();
    public ObservableCollection<CachedTrack> CachedTracks { get; } = new();

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _cacheSize = "0 MB";

    [ObservableProperty]
    private int _cachedTrackCount;

    [ObservableProperty]
    private bool _hasActiveDownloads;

    [ObservableProperty]
    private DownloadItem? _currentDownload;

    public DownloadsViewModel(IDownloadService downloadService, ICacheService cacheService)
    {
        _downloadService = downloadService;
        _cacheService = cacheService;
        Title = "Downloads";

        // Subscribe to download events
        _downloadService.QueueChanged += OnQueueChanged;
        _downloadService.DownloadProgress += OnDownloadProgress;
    }

    public async Task InitializeAsync()
    {
        await LoadDownloadsAsync();
        await LoadCachedTracksAsync();
        await UpdateCacheStatsAsync();
    }

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await LoadDownloadsAsync();
            await UpdateCacheStatsAsync();
        });
    }

    private void OnDownloadProgress(object? sender, (DownloadItem item, double progress) e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var item = Downloads.FirstOrDefault(d => d.Id == e.item.Id);
            if (item != null)
            {
                item.Progress = e.progress;
            }
            CurrentDownload = _downloadService.CurrentDownload;
        });
    }

    [RelayCommand]
    private async Task LoadDownloadsAsync()
    {
        Downloads.Clear();
        foreach (var item in _downloadService.Queue)
        {
            Downloads.Add(item);
        }

        HasActiveDownloads = _downloadService.IsDownloading;
        CurrentDownload = _downloadService.CurrentDownload;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task LoadCachedTracksAsync()
    {
        var tracks = await _cacheService.GetCachedTracksAsync();
        CachedTracks.Clear();
        foreach (var track in tracks.OrderByDescending(t => t.CachedAt))
        {
            CachedTracks.Add(track);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadDownloadsAsync();
        await LoadCachedTracksAsync();
        await UpdateCacheStatsAsync();
        IsRefreshing = false;
    }

    private async Task UpdateCacheStatsAsync()
    {
        var size = await _cacheService.GetCacheSizeAsync();
        CacheSize = size.ToFileSizeString();
        CachedTrackCount = await _cacheService.GetCachedTrackCountAsync();
    }

    [RelayCommand]
    private async Task CancelDownloadAsync(DownloadItem item)
    {
        await _downloadService.CancelDownloadAsync(item.Id.ToString());
    }

    [RelayCommand]
    private async Task RetryDownloadAsync(DownloadItem item)
    {
        await _downloadService.RetryDownloadAsync(item.Id.ToString());
    }

    [RelayCommand]
    private async Task RemoveDownloadAsync(DownloadItem item)
    {
        await _downloadService.RemoveFromQueueAsync(item.Id.ToString());
    }

    [RelayCommand]
    private async Task DeleteCachedTrackAsync(CachedTrack track)
    {
        var confirm = await ConfirmationHelper.ShowConfirmAsync(
            "Delete",
            $"Delete '{track.DisplayName}' from cache?",
            "Delete",
            "Cancel",
            "",
            true);

        if (confirm)
        {
            await _cacheService.DeleteCachedTrackAsync(track.TrackId);
            CachedTracks.Remove(track);
            await UpdateCacheStatsAsync();
        }
    }

    [RelayCommand]
    private async Task ClearAllCacheAsync()
    {
        var confirm = await ConfirmationHelper.ShowConfirmAsync(
            "Clear Cache",
            $"Delete all {CachedTrackCount} cached tracks ({CacheSize})?",
            "Clear All",
            "Cancel",
            "",
            true);

        if (confirm)
        {
            await _cacheService.ClearCacheAsync();
            CachedTracks.Clear();
            await UpdateCacheStatsAsync();
        }
    }

    [RelayCommand]
    private async Task ClearCompletedDownloadsAsync()
    {
        await _downloadService.ClearHistoryAsync();
        await LoadDownloadsAsync();
    }

    [RelayCommand]
    private async Task PauseDownloadsAsync()
    {
        await _downloadService.PauseAsync();
        HasActiveDownloads = false;
    }

    [RelayCommand]
    private async Task ResumeDownloadsAsync()
    {
        await _downloadService.ResumeAsync();
        HasActiveDownloads = _downloadService.IsDownloading;
    }
}
