using SQLite;
using TFMAudioApp.Data.Entities;
using TFMAudioApp.Models;

namespace TFMAudioApp.Data;

/// <summary>
/// Local SQLite database for caching and offline data
/// </summary>
public class LocalDatabase
{
    private SQLiteAsyncConnection? _database;
    private readonly string _dbPath;

    public LocalDatabase()
    {
        _dbPath = Path.Combine(FileSystem.AppDataDirectory, "tfmaudio.db");
    }

    private async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_database != null)
            return _database;

        _database = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

        // Create tables
        await _database.CreateTableAsync<ServerConfig>();
        await _database.CreateTableAsync<CachedTrackEntity>();
        await _database.CreateTableAsync<OfflinePlaylistEntity>();
        await _database.CreateTableAsync<DownloadQueueEntity>();
        await _database.CreateTableAsync<PlayHistoryEntity>();

        return _database;
    }

    #region Server Config

    public async Task<ServerConfig?> GetServerConfigAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<ServerConfig>().FirstOrDefaultAsync();
    }

    public async Task SaveServerConfigAsync(ServerConfig config)
    {
        var db = await GetConnectionAsync();
        var existing = await db.Table<ServerConfig>().FirstOrDefaultAsync();

        if (existing != null)
        {
            config.Id = existing.Id;
            await db.UpdateAsync(config);
        }
        else
        {
            await db.InsertAsync(config);
        }
    }

    public async Task ClearServerConfigAsync()
    {
        var db = await GetConnectionAsync();
        await db.DeleteAllAsync<ServerConfig>();
    }

    #endregion

    #region Cached Tracks

    public async Task<List<CachedTrackEntity>> GetAllCachedTracksAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<CachedTrackEntity>().ToListAsync();
    }

    public async Task<CachedTrackEntity?> GetCachedTrackAsync(string trackId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<CachedTrackEntity>().FirstOrDefaultAsync(t => t.Id == trackId);
    }

    public async Task SaveCachedTrackAsync(CachedTrackEntity track)
    {
        var db = await GetConnectionAsync();
        var existing = await GetCachedTrackAsync(track.Id);

        if (existing != null)
        {
            await db.UpdateAsync(track);
        }
        else
        {
            await db.InsertAsync(track);
        }
    }

    public async Task DeleteCachedTrackAsync(string trackId)
    {
        var db = await GetConnectionAsync();
        await db.DeleteAsync<CachedTrackEntity>(trackId);
    }

    public async Task<int> GetCachedTrackCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<CachedTrackEntity>().CountAsync();
    }

    public async Task<long> GetTotalCacheSizeAsync()
    {
        var tracks = await GetAllCachedTracksAsync();
        return tracks.Sum(t => t.FileSize);
    }

    public async Task ClearAllCachedTracksAsync()
    {
        var db = await GetConnectionAsync();
        await db.DeleteAllAsync<CachedTrackEntity>();
    }

    #endregion

    #region Offline Playlists

    public async Task<List<OfflinePlaylistEntity>> GetAllOfflinePlaylistsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<OfflinePlaylistEntity>().ToListAsync();
    }

    public async Task<OfflinePlaylistEntity?> GetOfflinePlaylistAsync(string playlistId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<OfflinePlaylistEntity>().FirstOrDefaultAsync(p => p.Id == playlistId);
    }

    public async Task SaveOfflinePlaylistAsync(OfflinePlaylistEntity playlist)
    {
        var db = await GetConnectionAsync();
        var existing = await GetOfflinePlaylistAsync(playlist.Id);

        if (existing != null)
        {
            // Preserve AutoSync and LastSyncedAt when updating
            playlist.AutoSync = existing.AutoSync;
            playlist.LastSyncedAt = existing.LastSyncedAt;
            await db.UpdateAsync(playlist);
        }
        else
        {
            await db.InsertAsync(playlist);
        }
    }

    public async Task DeleteOfflinePlaylistAsync(string playlistId)
    {
        var db = await GetConnectionAsync();
        await db.DeleteAsync<OfflinePlaylistEntity>(playlistId);
    }

    public async Task ClearAllOfflinePlaylistsAsync()
    {
        var db = await GetConnectionAsync();
        await db.DeleteAllAsync<OfflinePlaylistEntity>();
    }

    public async Task<List<OfflinePlaylistEntity>> GetAutoSyncPlaylistsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<OfflinePlaylistEntity>()
            .Where(p => p.AutoSync)
            .ToListAsync();
    }

    public async Task SetPlaylistAutoSyncAsync(string playlistId, bool autoSync)
    {
        var db = await GetConnectionAsync();
        var playlist = await GetOfflinePlaylistAsync(playlistId);
        if (playlist != null)
        {
            playlist.AutoSync = autoSync;
            await db.UpdateAsync(playlist);
        }
    }

    public async Task UpdatePlaylistLastSyncAsync(string playlistId)
    {
        var db = await GetConnectionAsync();
        var playlist = await GetOfflinePlaylistAsync(playlistId);
        if (playlist != null)
        {
            playlist.LastSyncedAt = DateTime.UtcNow;
            await db.UpdateAsync(playlist);
        }
    }

    public async Task<HashSet<string>> GetAllCachedTrackIdsAsync()
    {
        var db = await GetConnectionAsync();
        var tracks = await db.Table<CachedTrackEntity>().ToListAsync();
        return tracks.Select(t => t.Id).ToHashSet();
    }

    #endregion

    #region Download Queue

    public async Task<List<DownloadQueueEntity>> GetDownloadQueueAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<DownloadQueueEntity>()
            .Where(d => d.Status != DownloadStatus.Completed)
            .OrderBy(d => d.AddedAt)
            .ToListAsync();
    }

    public async Task<DownloadQueueEntity?> GetNextPendingDownloadAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<DownloadQueueEntity>()
            .Where(d => d.Status == DownloadStatus.Pending)
            .OrderBy(d => d.AddedAt)
            .FirstOrDefaultAsync();
    }

    public async Task AddToDownloadQueueAsync(DownloadQueueEntity item)
    {
        var db = await GetConnectionAsync();
        item.AddedAt = DateTime.UtcNow;
        await db.InsertAsync(item);
    }

    public async Task UpdateDownloadQueueItemAsync(DownloadQueueEntity item)
    {
        var db = await GetConnectionAsync();
        await db.UpdateAsync(item);
    }

    public async Task RemoveFromDownloadQueueAsync(int id)
    {
        var db = await GetConnectionAsync();
        await db.DeleteAsync<DownloadQueueEntity>(id);
    }

    public async Task ClearCompletedDownloadsAsync()
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM DownloadQueue WHERE Status = ?", DownloadStatus.Completed);
    }

    #endregion

    #region Play History

    public async Task<List<PlayHistoryEntity>> GetRecentPlayHistoryAsync(int limit = 20)
    {
        var db = await GetConnectionAsync();
        return await db.Table<PlayHistoryEntity>()
            .OrderByDescending(h => h.PlayedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task AddToPlayHistoryAsync(PlayHistoryEntity entry)
    {
        var db = await GetConnectionAsync();
        entry.PlayedAt = DateTime.UtcNow;
        await db.InsertAsync(entry);

        // Keep only last 100 entries
        await db.ExecuteAsync(
            "DELETE FROM PlayHistory WHERE Id NOT IN (SELECT Id FROM PlayHistory ORDER BY PlayedAt DESC LIMIT 100)");
    }

    public async Task ClearPlayHistoryAsync()
    {
        var db = await GetConnectionAsync();
        await db.DeleteAllAsync<PlayHistoryEntity>();
    }

    #endregion
}
