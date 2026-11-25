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
        /// <summary>The files of a channel index with one of these extensions (with the dot), and only the fields a scan needs.</summary>
        Task<List<BsonFileManagerModel>> GetChannelVideos(string channelId, IEnumerable<string> extensions);
        /// <summary>Folder paths of a channel index (<c>/Series/Breaking Bad/</c>), root excluded.</summary>
        Task<List<string>> GetChannelFolders(string channelId);
        Task<BsonFileManagerModel?> GetChannelFile(string channelId, string fileId);

        // Items
        Task<LibraryItem?> GetItem(string id);
        Task<List<LibraryItem>> GetItems(IEnumerable<string> ids);
        Task<List<LibraryItem>> GetAllItems();
        Task<LibraryItem?> FindItemByProvider(string provider, string providerId, string? kind = null);
        Task<LibraryItem?> FindItemByExternalId(string key, string value, string? kind = null);
        Task UpsertItem(LibraryItem item);
        /// <summary>Stores only the file count, episode count and channels of these items.</summary>
        Task UpdateItemCounts(IEnumerable<LibraryItem> items);
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
        Task UpsertFiles(IEnumerable<LibraryFile> files);
        Task DeleteFile(long channelId, string fileId);
        Task DeleteFiles(long channelId, IEnumerable<string> fileIds);
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
