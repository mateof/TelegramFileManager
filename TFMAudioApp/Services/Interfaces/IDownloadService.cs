using TFMAudioApp.Data.Entities;
using TFMAudioApp.Models;

namespace TFMAudioApp.Services.Interfaces;

/// <summary>
/// Download queue item (view model for UI)
/// </summary>
public class DownloadItem
{
    public int Id { get; set; }
    public string TrackId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Status { get; set; } = DownloadStatus.Pending;
    public double Progress { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public string DisplayName => Path.GetFileNameWithoutExtension(FileName);

    public bool IsPending => Status == DownloadStatus.Pending;
    public bool IsDownloading => Status == DownloadStatus.Downloading;
    public bool IsCompleted => Status == DownloadStatus.Completed;
    public bool IsFailed => Status == DownloadStatus.Failed;
}

/// <summary>
/// Service for managing file downloads
/// </summary>
public interface IDownloadService
{
    #region Properties

    /// <summary>
    /// Whether a download is currently in progress
    /// </summary>
    bool IsDownloading { get; }

    /// <summary>
    /// Current download queue
    /// </summary>
    IReadOnlyList<DownloadItem> Queue { get; }

    /// <summary>
    /// Currently downloading item
    /// </summary>
    DownloadItem? CurrentDownload { get; }

    #endregion

    #region Events

    /// <summary>
    /// Fired when a download starts
    /// </summary>
    event EventHandler<DownloadItem> DownloadStarted;

    /// <summary>
    /// Fired when download progress updates
    /// </summary>
    event EventHandler<(DownloadItem item, double progress)> DownloadProgress;

    /// <summary>
    /// Fired when a download completes
    /// </summary>
    event EventHandler<DownloadItem> DownloadCompleted;

    /// <summary>
    /// Fired when a download fails
    /// </summary>
    event EventHandler<(DownloadItem item, string error)> DownloadFailed;

    /// <summary>
    /// Fired when the queue changes
    /// </summary>
    event EventHandler QueueChanged;

    #endregion

    #region Queue Management

    /// <summary>
    /// Add a track to the download queue
    /// </summary>
    Task<DownloadItem> EnqueueAsync(Track track);

    /// <summary>
    /// Add multiple tracks to the download queue
    /// </summary>
    Task<List<DownloadItem>> EnqueueAsync(IEnumerable<Track> tracks);

    /// <summary>
    /// Remove an item from the queue
    /// </summary>
    Task RemoveFromQueueAsync(string downloadId);

    /// <summary>
    /// Clear the entire queue (cancels active downloads)
    /// </summary>
    Task ClearQueueAsync();

    /// <summary>
    /// Cancel a specific download
    /// </summary>
    Task CancelDownloadAsync(string downloadId);

    /// <summary>
    /// Cancel downloads for specific track IDs
    /// </summary>
    Task CancelDownloadsAsync(IEnumerable<string> trackIds);

    /// <summary>
    /// Retry a failed download
    /// </summary>
    Task RetryDownloadAsync(string downloadId);

    #endregion

    #region Download Control

    /// <summary>
    /// Start processing the download queue
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Pause all downloads
    /// </summary>
    Task PauseAsync();

    /// <summary>
    /// Resume paused downloads
    /// </summary>
    Task ResumeAsync();

    #endregion

    #region History

    /// <summary>
    /// Get completed downloads
    /// </summary>
    Task<List<DownloadItem>> GetCompletedDownloadsAsync();

    /// <summary>
    /// Get failed downloads
    /// </summary>
    Task<List<DownloadItem>> GetFailedDownloadsAsync();

    /// <summary>
    /// Clear download history
    /// </summary>
    Task ClearHistoryAsync();

    #endregion
}
