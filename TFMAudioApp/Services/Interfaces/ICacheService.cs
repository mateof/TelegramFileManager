using TFMAudioApp.Models;

namespace TFMAudioApp.Services.Interfaces;

/// <summary>
/// Service for managing cached audio files for offline playback
/// </summary>
public interface ICacheService
{
    #region Cache Status

    /// <summary>
    /// Check if a track is cached locally
    /// </summary>
    Task<bool> IsTrackCachedAsync(string trackId);

    /// <summary>
    /// Get the local file path for a cached track
    /// </summary>
    Task<string?> GetCachedFilePathAsync(string trackId);

    /// <summary>
    /// Get the playback URL - returns local path if cached, otherwise remote URL
    /// </summary>
    Task<string> GetPlaybackUrlAsync(Track track);

    /// <summary>
    /// Get cached track info including duration
    /// </summary>
    Task<CachedTrack?> GetCachedTrackInfoAsync(string trackId);

    /// <summary>
    /// Check cache status for multiple tracks and update their IsCached property
    /// </summary>
    Task UpdateTracksCacheStatusAsync(IEnumerable<Track> tracks);

    /// <summary>
    /// Check if a playlist is fully cached
    /// </summary>
    Task<(bool isFullyCached, bool isPartiallyCached, int cachedCount)> GetPlaylistCacheStatusAsync(string playlistId);

    /// <summary>
    /// Get IDs of all cached tracks
    /// </summary>
    Task<HashSet<string>> GetCachedTrackIdsAsync();

    #endregion

    #region Statistics

    /// <summary>
    /// Get total size of cached files in bytes
    /// </summary>
    Task<long> GetCacheSizeAsync();

    /// <summary>
    /// Get count of cached tracks
    /// </summary>
    Task<int> GetCachedTrackCountAsync();

    /// <summary>
    /// Get list of all cached tracks
    /// </summary>
    Task<List<CachedTrack>> GetCachedTracksAsync();

    #endregion

    #region Cache Management

    /// <summary>
    /// Cache a track for offline playback
    /// </summary>
    Task<bool> CacheTrackAsync(Track track, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache all tracks in a playlist
    /// </summary>
    Task<int> CachePlaylistAsync(string playlistId, IProgress<(int current, int total, string trackName)>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a cached track
    /// </summary>
    Task<bool> DeleteCachedTrackAsync(string trackId);

    /// <summary>
    /// Clear all cached files
    /// </summary>
    Task ClearCacheAsync();

    #endregion

    #region Offline Playlists

    /// <summary>
    /// Save playlist metadata for offline access
    /// </summary>
    Task SavePlaylistOfflineAsync(PlaylistDetail playlist);

    /// <summary>
    /// Get list of playlists available offline
    /// </summary>
    Task<List<Playlist>> GetOfflinePlaylistsAsync();

    /// <summary>
    /// Get full playlist details from offline storage
    /// </summary>
    Task<PlaylistDetail?> GetOfflinePlaylistAsync(string playlistId);

    /// <summary>
    /// Remove playlist from offline storage
    /// </summary>
    Task RemoveOfflinePlaylistAsync(string playlistId);

    /// <summary>
    /// Remove playlist from offline storage and delete all its cached tracks
    /// </summary>
    /// <param name="playlistId">Playlist ID to remove</param>
    /// <param name="deleteTracksExclusiveToPlaylist">If true, only deletes tracks not used by other offline playlists</param>
    /// <returns>Number of tracks deleted</returns>
    Task<int> RemoveOfflinePlaylistWithTracksAsync(string playlistId, bool deleteTracksExclusiveToPlaylist = true);

    /// <summary>
    /// Unmark a playlist from auto-sync
    /// </summary>
    Task UnmarkPlaylistForSyncAsync(string playlistId);

    #endregion

    #region Auto-Cache

    /// <summary>
    /// Cache a track in the background during playback (fire and forget)
    /// </summary>
    void CacheTrackInBackground(Track track);

    /// <summary>
    /// Event fired when a track is cached in background
    /// </summary>
    event EventHandler<string>? TrackCached;

    #endregion

    #region Playlist Sync

    /// <summary>
    /// Check for new tracks in cached playlists and download them
    /// </summary>
    Task SyncCachedPlaylistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark a playlist as requiring sync (when it's downloaded for offline)
    /// </summary>
    Task MarkPlaylistForSyncAsync(string playlistId);

    /// <summary>
    /// Check if a playlist is marked for sync
    /// </summary>
    Task<bool> IsPlaylistMarkedForSyncAsync(string playlistId);

    #endregion
}

/// <summary>
/// Cached track information
/// </summary>
public class CachedTrack
{
    public string Id { get; set; } = string.Empty;
    public string TrackId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public double? Duration { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public DateTime CachedAt { get; set; }
    public DateTime? LastPlayedAt { get; set; }

    public string DisplayName => !string.IsNullOrEmpty(Title) ? Title : Path.GetFileNameWithoutExtension(FileName);
    public string DisplayArtist => !string.IsNullOrEmpty(Artist) ? Artist : ChannelName;
}
