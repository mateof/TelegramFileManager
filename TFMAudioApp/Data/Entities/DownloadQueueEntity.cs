using SQLite;

namespace TFMAudioApp.Data.Entities;

/// <summary>
/// Download queue item
/// </summary>
[Table("DownloadQueue")]
public class DownloadQueueEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string TrackId { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Status { get; set; } = "pending"; // pending, downloading, completed, failed, cancelled
    public double Progress { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public static class DownloadStatus
{
    public const string Pending = "pending";
    public const string Downloading = "downloading";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
