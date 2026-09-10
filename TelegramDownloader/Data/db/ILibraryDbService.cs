using TelegramDownloader.Models;
using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Data.db
{
    /// <summary>
    /// Storage of the media library (identified movies/series, their files and
    /// the playback progress). Lives in its own MongoDB database so the
    /// per-channel indexes are never touched.
    /// </summary>
    public interface ILibraryDbService
    {
        Task EnsureIndexes();

        // Channel indexes (read only)
        Task<List<string>> GetChannelDatabaseNames();
        Task<List<BsonFileManagerModel>> GetChannelFiles(string channelId);
        Task<BsonFileManagerModel?> GetChannelFile(string channelId, string fileId);

        // Items
        Task<LibraryItem?> GetItem(string id);
        Task<List<LibraryItem>> GetItems(IEnumerable<string> ids);
        Task<List<LibraryItem>> GetAllItems();
        Task<LibraryItem?> FindItemByProvider(string provider, string providerId);
        Task<LibraryItem?> FindItemByExternalId(string key, string value);
        Task UpsertItem(LibraryItem item);
        Task DeleteItem(string id);

        // Episodes
        Task<List<LibraryEpisode>> GetEpisodes(string itemId);
        Task UpsertEpisodes(IEnumerable<LibraryEpisode> episodes);
        Task DeleteEpisodes(string itemId);

        // Files
        Task<LibraryFile?> GetFile(long channelId, string fileId);
        Task<List<LibraryFile>> GetFilesByChannel(long channelId);
        Task<List<LibraryFile>> GetFilesByItem(string itemId);
        Task<List<LibraryFile>> GetFilesByItems(IEnumerable<string> itemIds);
        Task<List<LibraryFile>> GetAllFiles();
        Task<List<LibraryFile>> GetFilesByStatus(string status);
        Task<Dictionary<string, int>> CountFilesByStatus();
        Task UpsertFile(LibraryFile file);
        Task DeleteFile(long channelId, string fileId);
        Task<long> DeleteFilesByChannel(long channelId);

        // Watch state
        Task<WatchState?> GetWatch(long channelId, string fileId);
        Task<List<WatchState>> GetWatchMany(IEnumerable<string> keys);
        Task<List<WatchState>> GetWatchByItem(string itemId);
        Task<List<WatchState>> GetWatchByItems(IEnumerable<string> itemIds);
        Task<List<WatchState>> GetWatchInProgress();
        Task<List<WatchState>> GetWatchCompleted();
        Task UpsertWatch(WatchState state);
        Task DeleteWatch(long channelId, string fileId);

        // Scan state
        Task<LibraryScanState?> GetScanState();
        Task SaveScanState(LibraryScanState state);

        // Provider cache
        Task<string?> GetCached(string key);
        Task SetCached(string key, string json, TimeSpan ttl);
    }
}
