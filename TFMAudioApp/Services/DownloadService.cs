using System.Collections.ObjectModel;
using TFMAudioApp.Data;
using TFMAudioApp.Data.Entities;
using TFMAudioApp.Models;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.Services;

/// <summary>
/// Service for managing file downloads
/// </summary>
public class DownloadService : IDownloadService
{
    private readonly LocalDatabase _database;
    private readonly ICacheService _cacheService;
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;

    private readonly ObservableCollection<DownloadItem> _queue = new();
    private CancellationTokenSource? _downloadCts;
    private bool _isPaused;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    public DownloadService(LocalDatabase database, ICacheService cacheService, ISettingsService settingsService)
    {
        _database = database;
        _cacheService = cacheService;
        _settingsService = settingsService;
        _httpClient = new HttpClient();

        // Load existing queue
        _ = LoadQueueAsync();
    }

    #region Properties

    public bool IsDownloading => CurrentDownload != null && CurrentDownload.Status == DownloadStatus.Downloading;
    public IReadOnlyList<DownloadItem> Queue => _queue;
    public DownloadItem? CurrentDownload { get; private set; }

    #endregion

    #region Events

    public event EventHandler<DownloadItem>? DownloadStarted;
    public event EventHandler<(DownloadItem item, double progress)>? DownloadProgress;
    public event EventHandler<DownloadItem>? DownloadCompleted;
    public event EventHandler<(DownloadItem item, string error)>? DownloadFailed;
    public event EventHandler? QueueChanged;

    #endregion

    #region Queue Management

    public async Task<DownloadItem> EnqueueAsync(Track track)
    {
        var streamUrl = await GetStreamUrlAsync(track);

        var entity = new DownloadQueueEntity
        {
            TrackId = track.FileId,
            StreamUrl = streamUrl,
            FileName = track.FileName,
            ChannelId = track.ChannelId,
            ChannelName = track.ChannelName,
            FileSize = track.FileSize,
            Status = DownloadStatus.Pending,
            Progress = 0,
            AddedAt = DateTime.UtcNow
        };

        await _database.AddToDownloadQueueAsync(entity);

        var item = MapToDownloadItem(entity);
        _queue.Add(item);
        QueueChanged?.Invoke(this, EventArgs.Empty);

        // Start processing if not already
        _ = ProcessQueueAsync();

        return item;
    }

    public async Task<List<DownloadItem>> EnqueueAsync(IEnumerable<Track> tracks)
    {
        var items = new List<DownloadItem>();

        foreach (var track in tracks)
        {
            // Skip if already in queue or cached
            if (_queue.Any(q => q.TrackId == track.FileId))
                continue;

            if (await _cacheService.IsTrackCachedAsync(track.FileId))
                continue;

            var item = await EnqueueAsync(track);
            items.Add(item);
        }

        return items;
    }

    public async Task RemoveFromQueueAsync(string downloadId)
    {
        if (!int.TryParse(downloadId, out var id)) return;

        var item = _queue.FirstOrDefault(q => q.Id == id);
        if (item == null) return;

        // Cancel if currently downloading
        if (CurrentDownload?.Id == id)
        {
            _downloadCts?.Cancel();
        }

        await _database.RemoveFromDownloadQueueAsync(id);
        _queue.Remove(item);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearQueueAsync()
    {
        _downloadCts?.Cancel();

        var db = await GetPendingItemsAsync();
        foreach (var item in db)
        {
            await _database.RemoveFromDownloadQueueAsync(item.Id);
        }

        _queue.Clear();
        CurrentDownload = null;
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task CancelDownloadAsync(string downloadId)
    {
        if (!int.TryParse(downloadId, out var id)) return;

        var item = _queue.FirstOrDefault(q => q.Id == id);
        if (item == null) return;

        if (CurrentDownload?.Id == id)
        {
            _downloadCts?.Cancel();
        }

        item.Status = DownloadStatus.Cancelled;
        await UpdateItemStatusAsync(item);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RetryDownloadAsync(string downloadId)
    {
        if (!int.TryParse(downloadId, out var id)) return;

        var item = _queue.FirstOrDefault(q => q.Id == id);
        if (item == null) return;

        item.Status = DownloadStatus.Pending;
        item.Progress = 0;
        item.ErrorMessage = null;

        await UpdateItemStatusAsync(item);
        QueueChanged?.Invoke(this, EventArgs.Empty);

        _ = ProcessQueueAsync();
    }

    public async Task CancelDownloadsAsync(IEnumerable<string> trackIds)
    {
        var trackIdSet = trackIds.ToHashSet();

        // Cancel current download if it matches
        if (CurrentDownload != null && trackIdSet.Contains(CurrentDownload.TrackId))
        {
            _downloadCts?.Cancel();
        }

        // Find all matching items in queue
        var itemsToCancel = _queue.Where(q => trackIdSet.Contains(q.TrackId)).ToList();

        foreach (var item in itemsToCancel)
        {
            item.Status = DownloadStatus.Cancelled;
            await UpdateItemStatusAsync(item);
            await _database.RemoveFromDownloadQueueAsync(item.Id);
            _queue.Remove(item);
        }

        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Download Control

    public Task StartAsync()
    {
        _isPaused = false;
        return ProcessQueueAsync();
    }

    public Task PauseAsync()
    {
        _isPaused = true;
        _downloadCts?.Cancel();
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        _isPaused = false;
        return ProcessQueueAsync();
    }

    #endregion

    #region History

    public async Task<List<DownloadItem>> GetCompletedDownloadsAsync()
    {
        var entities = await _database.GetDownloadQueueAsync();
        return entities
            .Where(e => e.Status == DownloadStatus.Completed)
            .Select(MapToDownloadItem)
            .ToList();
    }

    public async Task<List<DownloadItem>> GetFailedDownloadsAsync()
    {
        var entities = await _database.GetDownloadQueueAsync();
        return entities
            .Where(e => e.Status == DownloadStatus.Failed)
            .Select(MapToDownloadItem)
            .ToList();
    }

    public async Task ClearHistoryAsync()
    {
        await _database.ClearCompletedDownloadsAsync();
        var completedItems = _queue.Where(q => q.IsCompleted || q.IsFailed).ToList();
        foreach (var item in completedItems)
        {
            _queue.Remove(item);
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Private Methods

    private async Task LoadQueueAsync()
    {
        var entities = await _database.GetDownloadQueueAsync();
        foreach (var entity in entities)
        {
            _queue.Add(MapToDownloadItem(entity));
        }

        if (_queue.Any(q => q.IsPending))
        {
            _ = ProcessQueueAsync();
        }
    }

    private async Task ProcessQueueAsync()
    {
        if (_isPaused) return;

        await _downloadLock.WaitAsync();
        try
        {
            while (!_isPaused)
            {
                var nextItem = _queue.FirstOrDefault(q => q.IsPending);
                if (nextItem == null) break;

                await DownloadItemAsync(nextItem);
            }
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    private async Task DownloadItemAsync(DownloadItem item)
    {
        CurrentDownload = item;
        _downloadCts = new CancellationTokenSource();

        try
        {
            item.Status = DownloadStatus.Downloading;
            await UpdateItemStatusAsync(item);
            DownloadStarted?.Invoke(this, item);

            // Create track from download item
            var track = new Track
            {
                FileId = item.TrackId,
                ChannelId = item.ChannelId,
                ChannelName = item.ChannelName,
                FileName = item.FileName,
                FileSize = item.FileSize,
                StreamUrl = item.StreamUrl
            };

            var progress = new Progress<double>(p =>
            {
                item.Progress = p;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DownloadProgress?.Invoke(this, (item, p));
                });
            });

            var success = await _cacheService.CacheTrackAsync(track, progress, _downloadCts.Token);

            if (success)
            {
                item.Status = DownloadStatus.Completed;
                item.CompletedAt = DateTime.UtcNow;
                item.Progress = 1.0;
                await UpdateItemStatusAsync(item);
                DownloadCompleted?.Invoke(this, item);
            }
            else if (_downloadCts.Token.IsCancellationRequested)
            {
                item.Status = DownloadStatus.Cancelled;
                await UpdateItemStatusAsync(item);
            }
            else
            {
                item.Status = DownloadStatus.Failed;
                item.ErrorMessage = "Download failed";
                await UpdateItemStatusAsync(item);
                DownloadFailed?.Invoke(this, (item, "Download failed"));
            }
        }
        catch (OperationCanceledException)
        {
            item.Status = DownloadStatus.Cancelled;
            await UpdateItemStatusAsync(item);
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = ex.Message;
            await UpdateItemStatusAsync(item);
            DownloadFailed?.Invoke(this, (item, ex.Message));
        }
        finally
        {
            CurrentDownload = null;
            _downloadCts?.Dispose();
            _downloadCts = null;
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task UpdateItemStatusAsync(DownloadItem item)
    {
        var entity = new DownloadQueueEntity
        {
            Id = item.Id,
            TrackId = item.TrackId,
            StreamUrl = item.StreamUrl,
            FileName = item.FileName,
            ChannelId = item.ChannelId,
            ChannelName = item.ChannelName,
            FileSize = item.FileSize,
            Status = item.Status,
            Progress = item.Progress,
            ErrorMessage = item.ErrorMessage,
            AddedAt = item.AddedAt,
            CompletedAt = item.CompletedAt
        };

        await _database.UpdateDownloadQueueItemAsync(entity);
    }

    private async Task<List<DownloadQueueEntity>> GetPendingItemsAsync()
    {
        var all = await _database.GetDownloadQueueAsync();
        return all.Where(e => e.Status == DownloadStatus.Pending).ToList();
    }

    private async Task<string> GetStreamUrlAsync(Track track)
    {
        if (!string.IsNullOrEmpty(track.StreamUrl))
            return track.StreamUrl;

        if (!string.IsNullOrEmpty(track.DirectUrl))
            return track.DirectUrl;

        var config = await _settingsService.GetServerConfigAsync();
        if (config == null)
            throw new InvalidOperationException("Server not configured");

        var protocol = config.UseHttps ? "https" : "http";
        var baseUrl = $"{protocol}://{config.Host}:{config.Port}";

        if (track.IsLocalFile)
        {
            var encodedPath = Uri.EscapeDataString(track.FilePath);
            return $"{baseUrl}/api/mobile/stream/local?path={encodedPath}";
        }

        return $"{baseUrl}/api/mobile/stream/{track.ChannelId}/{track.FileId}";
    }

    private static DownloadItem MapToDownloadItem(DownloadQueueEntity entity)
    {
        return new DownloadItem
        {
            Id = entity.Id,
            TrackId = entity.TrackId,
            FileName = entity.FileName,
            ChannelId = entity.ChannelId,
            ChannelName = entity.ChannelName,
            StreamUrl = entity.StreamUrl,
            FileSize = entity.FileSize,
            Status = entity.Status,
            Progress = entity.Progress,
            ErrorMessage = entity.ErrorMessage,
            AddedAt = entity.AddedAt,
            CompletedAt = entity.CompletedAt
        };
    }

    #endregion
}
