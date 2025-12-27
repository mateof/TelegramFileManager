using SQLite;

namespace TFMAudioApp.Data.Entities;

/// <summary>
/// Playlist saved for offline access
/// </summary>
[Table("OfflinePlaylists")]
public class OfflinePlaylistEntity
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int TrackCount { get; set; }
    public string TracksJson { get; set; } = string.Empty; // JSON serialized tracks
    public DateTime SavedAt { get; set; }

    /// <summary>
    /// If true, this playlist will be synced automatically when new tracks are added
    /// </summary>
    public bool AutoSync { get; set; }

    /// <summary>
    /// Last time this playlist was synced with the server
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }
}
