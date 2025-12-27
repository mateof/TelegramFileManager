using System.Text.Json;
using TFMAudioApp.Data;
using TFMAudioApp.Data.Entities;
using TFMAudioApp.Models;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.Services;

/// <summary>
/// Service for managing cached audio files for offline playback
/// </summary>
public class CacheService : ICacheService
{
    private readonly LocalDatabase _database;
    private readonly ISettingsService _settingsService;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _backgroundCacheLock = new(1, 1);
    private readonly HashSet<string> _backgroundCachingTracks = new();

    // Singleton HttpClient with optimized settings for fast downloads
    private static readonly Lazy<HttpClient> _downloadClient = new(() =>
    {
        var handler = new SocketsHttpHandler
        {
            // Connection pooling - keep connections alive for reuse
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 10,
            // Enable HTTP/2 for multiplexing
            EnableMultipleHttp2Connections = true,
            // Faster connection establishment
            ConnectTimeout = TimeSpan.FromSeconds(10),
            // Enable keep-alive
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            // Larger receive buffer
            InitialHttp2StreamWindowSize = 1024 * 1024, // 1MB
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30),
            DefaultRequestVersion = new Version(2, 0), // Prefer HTTP/2
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
    });

    public event EventHandler<string>? TrackCached;

    public CacheService(LocalDatabase database, ISettingsService settingsService)
    {
        _database = database;
        _settingsService = settingsService;
        _cacheDirectory = Path.Combine(FileSystem.AppDataDirectory, "cache", "audio");

        // Ensure cache directory exists
        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    #region Cache Status

    public async Task<bool> IsTrackCachedAsync(string trackId)
    {
        var cached = await _database.GetCachedTrackAsync(trackId);
        if (cached == null) return false;

        // Verify file still exists
        if (!File.Exists(cached.LocalFilePath))
        {
            await _database.DeleteCachedTrackAsync(trackId);
            return false;
        }

        // Verify file has valid content (not empty or too small)
        try
        {
            var fileInfo = new FileInfo(cached.LocalFilePath);
            if (fileInfo.Length < 1000) // Less than 1KB is definitely not a valid audio file
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Cached file too small ({fileInfo.Length} bytes), removing: {cached.FileName}");
                File.Delete(cached.LocalFilePath);
                await _database.DeleteCachedTrackAsync(trackId);
                return false;
            }

            // If we have the expected size, verify it matches (with tolerance)
            if (cached.FileSize > 0 && fileInfo.Length < cached.FileSize * 0.5)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Cached file incomplete ({fileInfo.Length}/{cached.FileSize} bytes), removing: {cached.FileName}");
                File.Delete(cached.LocalFilePath);
                await _database.DeleteCachedTrackAsync(trackId);
                return false;
            }
        }
        catch
        {
            // If we can't check the file, consider it invalid
            await _database.DeleteCachedTrackAsync(trackId);
            return false;
        }

        return true;
    }

    public async Task<string?> GetCachedFilePathAsync(string trackId)
    {
        var cached = await _database.GetCachedTrackAsync(trackId);
        if (cached == null) return null;

        if (!File.Exists(cached.LocalFilePath))
        {
            await _database.DeleteCachedTrackAsync(trackId);
            return null;
        }

        // Verify file has valid content
        try
        {
            var fileInfo = new FileInfo(cached.LocalFilePath);
            if (fileInfo.Length < 1000)
            {
                File.Delete(cached.LocalFilePath);
                await _database.DeleteCachedTrackAsync(trackId);
                return null;
            }
        }
        catch
        {
            await _database.DeleteCachedTrackAsync(trackId);
            return null;
        }

        return cached.LocalFilePath;
    }

    public async Task<string> GetPlaybackUrlAsync(Track track)
    {
        // Check if cached locally
        var localPath = await GetCachedFilePathAsync(track.FileId);
        if (localPath != null)
        {
            return localPath;
        }

        // Build URL from config with API key as query param
        // (LibVLC doesn't support custom HTTP headers, so we pass API key in URL for streaming)
        var config = await _settingsService.GetServerConfigAsync();
        if (config == null)
        {
            throw new InvalidOperationException("Server not configured");
        }

        var protocol = config.UseHttps ? "https" : "http";
        var baseUrl = $"{protocol}://{config.Host}:{config.Port}";
        var apiKeyParam = $"apiKey={Uri.EscapeDataString(config.ApiKey)}";

        // If track has a StreamUrl, append API key to it
        if (!string.IsNullOrEmpty(track.StreamUrl))
        {
            var url = track.StreamUrl.StartsWith("http") ? track.StreamUrl : $"{baseUrl}{track.StreamUrl}";
            var separator = url.Contains('?') ? "&" : "?";
            return $"{url}{separator}{apiKeyParam}";
        }

        if (track.IsLocalFile)
        {
            var encodedPath = Uri.EscapeDataString(track.FilePath);
            return $"{baseUrl}/api/mobile/stream/local?path={encodedPath}&{apiKeyParam}";
        }

        // Use the /tfm/ endpoint for progressive streaming
        // This endpoint streams immediately while caching on the server in background
        // Supports full seeking via HTTP Range requests
        var encodedFileName = Uri.EscapeDataString(track.FileName);
        return $"{baseUrl}/api/mobile/stream/tfm/{track.ChannelId}/{track.FileId}?fileName={encodedFileName}&{apiKeyParam}";
    }

    public async Task<CachedTrack?> GetCachedTrackInfoAsync(string trackId)
    {
        var entity = await _database.GetCachedTrackAsync(trackId);
        if (entity == null) return null;

        return new CachedTrack
        {
            Id = entity.Id,
            TrackId = entity.Id,
            ChannelId = entity.ChannelId ?? string.Empty,
            ChannelName = entity.ChannelName ?? string.Empty,
            FileName = entity.FileName,
            LocalPath = entity.LocalFilePath,
            FileSize = entity.FileSize,
            Duration = entity.Duration,
            Title = entity.Title,
            Artist = entity.Artist,
            CachedAt = entity.CachedAt,
            LastPlayedAt = entity.LastPlayedAt
        };
    }

    public async Task UpdateTracksCacheStatusAsync(IEnumerable<Track> tracks)
    {
        var cachedIds = await _database.GetAllCachedTrackIdsAsync();

        foreach (var track in tracks)
        {
            track.IsCached = cachedIds.Contains(track.FileId);

            // If cached, try to get duration from cache
            if (track.IsCached && (!track.Duration.HasValue || track.Duration <= 0))
            {
                var cachedInfo = await _database.GetCachedTrackAsync(track.FileId);
                if (cachedInfo?.Duration.HasValue == true && cachedInfo.Duration > 0)
                {
                    track.Duration = cachedInfo.Duration;
                }
            }
        }
    }

    public async Task<(bool isFullyCached, bool isPartiallyCached, int cachedCount)> GetPlaylistCacheStatusAsync(string playlistId)
    {
        var playlist = await GetOfflinePlaylistAsync(playlistId);
        if (playlist == null || playlist.Tracks.Count == 0)
        {
            return (false, false, 0);
        }

        var cachedIds = await _database.GetAllCachedTrackIdsAsync();
        var cachedCount = playlist.Tracks.Count(t => cachedIds.Contains(t.FileId));

        var isFullyCached = cachedCount == playlist.Tracks.Count;
        var isPartiallyCached = cachedCount > 0 && cachedCount < playlist.Tracks.Count;

        return (isFullyCached, isPartiallyCached, cachedCount);
    }

    public async Task<HashSet<string>> GetCachedTrackIdsAsync()
    {
        return await _database.GetAllCachedTrackIdsAsync();
    }

    #endregion

    #region Statistics

    public async Task<long> GetCacheSizeAsync()
    {
        return await _database.GetTotalCacheSizeAsync();
    }

    public async Task<int> GetCachedTrackCountAsync()
    {
        return await _database.GetCachedTrackCountAsync();
    }

    public async Task<List<CachedTrack>> GetCachedTracksAsync()
    {
        var entities = await _database.GetAllCachedTracksAsync();
        return entities.Select(e => new CachedTrack
        {
            Id = e.Id,
            TrackId = e.Id,
            ChannelId = e.ChannelId ?? string.Empty,
            ChannelName = e.ChannelName ?? string.Empty,
            FileName = e.FileName,
            LocalPath = e.LocalFilePath,
            FileSize = e.FileSize,
            Duration = e.Duration,
            Title = e.Title,
            Artist = e.Artist,
            CachedAt = e.CachedAt,
            LastPlayedAt = e.LastPlayedAt
        }).ToList();
    }

    #endregion

    #region Cache Management

    public async Task<bool> CacheTrackAsync(Track track, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get download URL (uses endpoint that downloads file completely before returning)
            var downloadUrl = await GetDownloadUrlAsync(track);
            if (string.IsNullOrEmpty(downloadUrl))
            {
                return false;
            }

            // Prepare local file path
            var safeFileName = GetSafeFileName(track.FileName);
            var localPath = Path.Combine(_cacheDirectory, $"{track.FileId}_{safeFileName}");

            // Use singleton HttpClient for connection pooling (much faster for multiple downloads)
            var client = _downloadClient.Value;

            // Download the file (server will wait until file is completely downloaded from Telegram)
            System.Diagnostics.Debug.WriteLine($"[CacheService] Starting download: {track.FileName}");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            System.Diagnostics.Debug.WriteLine($"[CacheService] Response received in {stopwatch.ElapsedMilliseconds}ms, status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                System.Diagnostics.Debug.WriteLine($"[CacheService] Download failed: {response.StatusCode} - {errorContent}");
                return false;
            }

            var totalBytes = response.Content.Headers.ContentLength ?? track.FileSize;

            // Use 1MB buffer for maximum throughput (optimal for most network conditions)
            const int bufferSize = 1024 * 1024; // 1MB buffer
            var buffer = new byte[bufferSize];
            var bytesRead = 0L;
            var lastProgressReport = 0L;
            const long progressReportThreshold = 1024 * 1024; // Report progress every 1MB

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

            // Pre-allocate file to reduce fragmentation and improve write performance
            using var fileStream = new FileStream(
                localPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            // Pre-allocate file space if we know the size (reduces fragmentation)
            if (totalBytes > 0)
            {
                fileStream.SetLength(totalBytes);
            }

            int read;
            while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesRead += read;

                // Report progress less frequently to reduce overhead
                if (totalBytes > 0 && bytesRead - lastProgressReport >= progressReportThreshold)
                {
                    progress?.Report((double)bytesRead / totalBytes);
                    lastProgressReport = bytesRead;
                }
            }

            // Trim file if we allocated more than needed
            if (bytesRead < totalBytes)
            {
                fileStream.SetLength(bytesRead);
            }

            stopwatch.Stop();
            var speedMbps = (bytesRead / 1024.0 / 1024.0) / (stopwatch.ElapsedMilliseconds / 1000.0);
            System.Diagnostics.Debug.WriteLine($"[CacheService] Download complete in {stopwatch.ElapsedMilliseconds}ms ({speedMbps:F2} MB/s)");

            // Verify file was downloaded completely
            var downloadedSize = new FileInfo(localPath).Length;

            // Check minimum file size (audio files should be at least 1KB)
            if (downloadedSize < 1000)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Downloaded file too small ({downloadedSize} bytes), likely failed: {track.FileName}");
                File.Delete(localPath);
                return false;
            }

            // Check against expected size if known
            if (track.FileSize > 0 && downloadedSize < track.FileSize * 0.95) // Allow 5% tolerance
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Download incomplete: {downloadedSize} / {track.FileSize} bytes");
                File.Delete(localPath);
                return false;
            }

            // Save to database
            var entity = new CachedTrackEntity
            {
                Id = track.FileId,
                ChannelId = track.ChannelId,
                ChannelName = track.ChannelName,
                FileName = track.FileName,
                LocalFilePath = localPath,
                FileSize = downloadedSize,
                Duration = track.Duration,
                Title = track.Title,
                Artist = track.Artist,
                StreamUrl = await GetStreamUrlAsync(track), // Store streaming URL for playback
                CachedAt = DateTime.UtcNow
            };

            await _database.SaveCachedTrackAsync(entity);
            progress?.Report(1.0);
            System.Diagnostics.Debug.WriteLine($"[CacheService] Download complete: {track.FileName} ({downloadedSize} bytes)");

            // Fire event to notify that track is now cached
            TrackCached?.Invoke(this, track.FileId);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cache failed for {track.FileName}: {ex.Message}");
            return false;
        }
    }

    public async Task<int> CachePlaylistAsync(string playlistId, IProgress<(int current, int total, string trackName)>? progress = null, CancellationToken cancellationToken = default)
    {
        // Get playlist from offline storage or we need the API
        var playlist = await GetOfflinePlaylistAsync(playlistId);
        if (playlist == null)
        {
            return 0;
        }

        var cachedCount = 0;
        var total = playlist.Tracks.Count;

        for (int i = 0; i < playlist.Tracks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var track = playlist.Tracks[i];
            progress?.Report((i + 1, total, track.DisplayName));

            // Skip if already cached
            if (await IsTrackCachedAsync(track.FileId))
            {
                cachedCount++;
                continue;
            }

            if (await CacheTrackAsync(track, null, cancellationToken))
            {
                cachedCount++;
            }
        }

        return cachedCount;
    }

    public async Task<bool> DeleteCachedTrackAsync(string trackId)
    {
        var cached = await _database.GetCachedTrackAsync(trackId);
        if (cached == null) return false;

        // Delete file
        if (File.Exists(cached.LocalFilePath))
        {
            try
            {
                File.Delete(cached.LocalFilePath);
            }
            catch
            {
                // Ignore file deletion errors
            }
        }

        // Delete from database
        await _database.DeleteCachedTrackAsync(trackId);
        return true;
    }

    public async Task ClearCacheAsync()
    {
        // Get all cached tracks
        var tracks = await _database.GetAllCachedTracksAsync();

        // Delete all files
        foreach (var track in tracks)
        {
            if (File.Exists(track.LocalFilePath))
            {
                try
                {
                    File.Delete(track.LocalFilePath);
                }
                catch
                {
                    // Ignore errors
                }
            }
        }

        // Clear database
        await _database.ClearAllCachedTracksAsync();

        // Clear all offline playlist markers (so they don't auto-download again)
        await _database.ClearAllOfflinePlaylistsAsync();

        // Clean up any orphaned files in cache directory
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                foreach (var file in Directory.GetFiles(_cacheDirectory))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Ignore errors
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }
    }

    #endregion

    #region Offline Playlists

    public async Task SavePlaylistOfflineAsync(PlaylistDetail playlist)
    {
        var entity = new OfflinePlaylistEntity
        {
            Id = playlist.Id,
            Name = playlist.Name,
            Description = playlist.Description,
            TrackCount = playlist.Tracks.Count,
            TracksJson = JsonSerializer.Serialize(playlist.Tracks),
            SavedAt = DateTime.UtcNow
        };

        await _database.SaveOfflinePlaylistAsync(entity);
    }

    public async Task<List<Playlist>> GetOfflinePlaylistsAsync()
    {
        var entities = await _database.GetAllOfflinePlaylistsAsync();
        return entities.Select(e => new Playlist
        {
            Id = e.Id,
            Name = e.Name,
            Description = e.Description,
            TrackCount = e.TrackCount,
            DateCreated = e.SavedAt,
            DateModified = e.SavedAt
        }).ToList();
    }

    public async Task<PlaylistDetail?> GetOfflinePlaylistAsync(string playlistId)
    {
        var entity = await _database.GetOfflinePlaylistAsync(playlistId);
        if (entity == null) return null;

        var tracks = new List<Track>();
        if (!string.IsNullOrEmpty(entity.TracksJson))
        {
            try
            {
                tracks = JsonSerializer.Deserialize<List<Track>>(entity.TracksJson) ?? new List<Track>();
            }
            catch
            {
                // Ignore deserialization errors
            }
        }

        return new PlaylistDetail
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            TrackCount = entity.TrackCount,
            Tracks = tracks
        };
    }

    public async Task RemoveOfflinePlaylistAsync(string playlistId)
    {
        await _database.DeleteOfflinePlaylistAsync(playlistId);
    }

    public async Task<int> RemoveOfflinePlaylistWithTracksAsync(string playlistId, bool deleteTracksExclusiveToPlaylist = true)
    {
        // Get the playlist to remove
        var playlistToRemove = await GetOfflinePlaylistAsync(playlistId);
        if (playlistToRemove == null)
        {
            return 0;
        }

        var tracksToDelete = new HashSet<string>(playlistToRemove.Tracks.Select(t => t.FileId));

        if (deleteTracksExclusiveToPlaylist)
        {
            // Get all other offline playlists to check for shared tracks
            var allPlaylists = await _database.GetAllOfflinePlaylistsAsync();
            foreach (var otherPlaylist in allPlaylists.Where(p => p.Id != playlistId))
            {
                if (!string.IsNullOrEmpty(otherPlaylist.TracksJson))
                {
                    try
                    {
                        var otherTracks = JsonSerializer.Deserialize<List<Track>>(otherPlaylist.TracksJson);
                        if (otherTracks != null)
                        {
                            foreach (var track in otherTracks)
                            {
                                // Remove from deletion list if used by another playlist
                                tracksToDelete.Remove(track.FileId);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore deserialization errors
                    }
                }
            }
        }

        // Delete exclusive tracks
        var deletedCount = 0;
        foreach (var trackId in tracksToDelete)
        {
            if (await DeleteCachedTrackAsync(trackId))
            {
                deletedCount++;
            }
        }

        // Remove playlist from offline storage and unmark for sync
        await _database.SetPlaylistAutoSyncAsync(playlistId, false);
        await _database.DeleteOfflinePlaylistAsync(playlistId);

        System.Diagnostics.Debug.WriteLine($"[CacheService] Removed offline playlist {playlistId}, deleted {deletedCount} tracks");

        return deletedCount;
    }

    public async Task UnmarkPlaylistForSyncAsync(string playlistId)
    {
        await _database.SetPlaylistAutoSyncAsync(playlistId, false);
    }

    #endregion

    #region Auto-Cache

    public void CacheTrackInBackground(Track track)
    {
        // Don't cache if already cached or currently caching
        if (track.IsCached) return;

        lock (_backgroundCachingTracks)
        {
            if (_backgroundCachingTracks.Contains(track.FileId))
                return;
            _backgroundCachingTracks.Add(track.FileId);
        }

        // Fire and forget background caching
        _ = Task.Run(async () =>
        {
            try
            {
                // Check again if already cached
                if (await IsTrackCachedAsync(track.FileId))
                {
                    track.IsCached = true;
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[CacheService] Background caching: {track.FileName}");

                var success = await CacheTrackAsync(track);

                if (success)
                {
                    track.IsCached = true;

                    // Update duration from cached file
                    var cachedInfo = await _database.GetCachedTrackAsync(track.FileId);
                    if (cachedInfo?.Duration.HasValue == true && cachedInfo.Duration > 0)
                    {
                        track.Duration = cachedInfo.Duration;
                    }

                    System.Diagnostics.Debug.WriteLine($"[CacheService] Background cache complete: {track.FileName}");
                    TrackCached?.Invoke(this, track.FileId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Background cache failed: {ex.Message}");
            }
            finally
            {
                lock (_backgroundCachingTracks)
                {
                    _backgroundCachingTracks.Remove(track.FileId);
                }
            }
        });
    }

    #endregion

    #region Playlist Sync

    public async Task MarkPlaylistForSyncAsync(string playlistId)
    {
        await _database.SetPlaylistAutoSyncAsync(playlistId, true);
    }

    public async Task<bool> IsPlaylistMarkedForSyncAsync(string playlistId)
    {
        var playlist = await _database.GetOfflinePlaylistAsync(playlistId);
        return playlist?.AutoSync ?? false;
    }

    public async Task SyncCachedPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        var syncPlaylists = await _database.GetAutoSyncPlaylistsAsync();
        if (syncPlaylists.Count == 0) return;

        System.Diagnostics.Debug.WriteLine($"[CacheService] Syncing {syncPlaylists.Count} cached playlists");

        foreach (var playlistEntity in syncPlaylists)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                await SyncPlaylistAsync(playlistEntity, cancellationToken);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Sync failed for {playlistEntity.Name}: {ex.Message}");
            }
        }
    }

    private async Task SyncPlaylistAsync(OfflinePlaylistEntity offlinePlaylist, CancellationToken cancellationToken)
    {
        // Get current tracks from offline storage
        var currentTracks = new List<Track>();
        if (!string.IsNullOrEmpty(offlinePlaylist.TracksJson))
        {
            try
            {
                currentTracks = JsonSerializer.Deserialize<List<Track>>(offlinePlaylist.TracksJson) ?? new List<Track>();
            }
            catch
            {
                return;
            }
        }

        var cachedIds = await _database.GetAllCachedTrackIdsAsync();

        // Find tracks that are not cached yet
        var uncachedTracks = currentTracks.Where(t => !cachedIds.Contains(t.FileId)).ToList();

        if (uncachedTracks.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[CacheService] Playlist '{offlinePlaylist.Name}' is fully cached");
            await _database.UpdatePlaylistLastSyncAsync(offlinePlaylist.Id);
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[CacheService] Caching {uncachedTracks.Count} new tracks for '{offlinePlaylist.Name}'");

        foreach (var track in uncachedTracks)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var success = await CacheTrackAsync(track, null, cancellationToken);
                if (success)
                {
                    track.IsCached = true;
                    TrackCached?.Invoke(this, track.FileId);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Failed to cache {track.FileName}: {ex.Message}");
            }
        }

        await _database.UpdatePlaylistLastSyncAsync(offlinePlaylist.Id);
    }

    #endregion

    #region Helpers

    private async Task<string?> GetStreamUrlAsync(Track track)
    {
        if (!string.IsNullOrEmpty(track.StreamUrl))
        {
            return track.StreamUrl;
        }

        if (!string.IsNullOrEmpty(track.DirectUrl))
        {
            return track.DirectUrl;
        }

        var config = await _settingsService.GetServerConfigAsync();
        if (config == null) return null;

        var protocol = config.UseHttps ? "https" : "http";
        var baseUrl = $"{protocol}://{config.Host}:{config.Port}";

        if (track.IsLocalFile)
        {
            var encodedPath = Uri.EscapeDataString(track.FilePath);
            return $"{baseUrl}/api/mobile/stream/local?path={encodedPath}";
        }

        return $"{baseUrl}/api/mobile/stream/{track.ChannelId}/{track.FileId}";
    }

    /// <summary>
    /// Gets the download URL that ensures complete file download before returning.
    /// This endpoint waits for the file to be fully downloaded from Telegram before serving it.
    /// </summary>
    private async Task<string?> GetDownloadUrlAsync(Track track)
    {
        var config = await _settingsService.GetServerConfigAsync();
        if (config == null) return null;

        var protocol = config.UseHttps ? "https" : "http";
        var baseUrl = $"{protocol}://{config.Host}:{config.Port}";
        var apiKeyParam = $"apiKey={Uri.EscapeDataString(config.ApiKey)}";

        // For local files, use the local streaming endpoint (already complete files)
        if (track.IsLocalFile)
        {
            var encodedPath = Uri.EscapeDataString(track.FilePath);
            return $"{baseUrl}/api/mobile/stream/local?path={encodedPath}&{apiKeyParam}";
        }

        // Use the download endpoint that ensures complete file download from Telegram
        var encodedFileName = Uri.EscapeDataString(track.FileName);
        return $"{baseUrl}/api/mobile/stream/download/{track.ChannelId}/{track.FileId}?fileName={encodedFileName}&{apiKeyParam}";
    }

    private static string GetSafeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return safeName.Length > 100 ? safeName[..100] : safeName;
    }

    #endregion
}
