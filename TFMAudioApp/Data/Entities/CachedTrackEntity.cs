using SQLite;

namespace TFMAudioApp.Data.Entities;

/// <summary>
/// Cached track stored locally for offline playback
/// </summary>
[Table("CachedTracks")]
public class CachedTrackEntity
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty; // FileId

    public string? ChannelId { get; set; }
    public string? ChannelName { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string LocalFilePath { get; set; } = string.Empty; // Local path to cached file
    public long FileSize { get; set; }
    public double? Duration { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? StreamUrl { get; set; } // Original stream URL
    public DateTime CachedAt { get; set; }
    public DateTime? LastPlayedAt { get; set; }
}
