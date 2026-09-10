using MongoDB.Driver;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Data.db
{
    public class LibraryDbService : ILibraryDbService
    {
        public const string DB_NAME = "TFM-LIBRARY";
        private const string ITEMS = "items";
        private const string EPISODES = "episodes";
        private const string FILES = "files";
        private const string WATCH = "watch";
        private const string SCAN = "scan";
        private const string CACHE = "provider_cache";

        private readonly IDbService _db;
        private readonly ILogger<LibraryDbService> _logger;

        public LibraryDbService(IDbService db, ILogger<LibraryDbService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // The IDbService client can be re-created after setup, so the database
        // handle is resolved on every call instead of being cached.
        private IMongoDatabase Db => _db.getDatabase(DB_NAME);
        private IMongoCollection<LibraryItem> Items => Db.GetCollection<LibraryItem>(ITEMS);
        private IMongoCollection<LibraryEpisode> Episodes => Db.GetCollection<LibraryEpisode>(EPISODES);
        private IMongoCollection<LibraryFile> Files => Db.GetCollection<LibraryFile>(FILES);
        private IMongoCollection<WatchState> Watch => Db.GetCollection<WatchState>(WATCH);
        private IMongoCollection<LibraryScanState> Scan => Db.GetCollection<LibraryScanState>(SCAN);
        private IMongoCollection<ProviderCacheEntry> Cache => Db.GetCollection<ProviderCacheEntry>(CACHE);

        public async Task EnsureIndexes()
        {
            try
            {
                await Items.Indexes.CreateManyAsync(new[]
                {
                    new CreateIndexModel<LibraryItem>(Builders<LibraryItem>.IndexKeys.Ascending(x => x.Provider).Ascending(x => x.ProviderId), new CreateIndexOptions { Name = "provider" }),
                    new CreateIndexModel<LibraryItem>(Builders<LibraryItem>.IndexKeys.Ascending(x => x.Kind).Ascending(x => x.SortTitle), new CreateIndexOptions { Name = "kind_title" })
                });
                await Episodes.Indexes.CreateOneAsync(new CreateIndexModel<LibraryEpisode>(
                    Builders<LibraryEpisode>.IndexKeys.Ascending(x => x.ItemId).Ascending(x => x.Season).Ascending(x => x.Number),
                    new CreateIndexOptions { Name = "item_season_number", Unique = true }));
                await Files.Indexes.CreateManyAsync(new[]
                {
                    new CreateIndexModel<LibraryFile>(Builders<LibraryFile>.IndexKeys.Ascending(x => x.ChannelId).Ascending(x => x.FileId), new CreateIndexOptions { Name = "channel_file", Unique = true }),
                    new CreateIndexModel<LibraryFile>(Builders<LibraryFile>.IndexKeys.Ascending(x => x.ItemId), new CreateIndexOptions { Name = "item" }),
                    new CreateIndexModel<LibraryFile>(Builders<LibraryFile>.IndexKeys.Ascending(x => x.Status), new CreateIndexOptions { Name = "status" })
                });
                await Watch.Indexes.CreateManyAsync(new[]
                {
                    new CreateIndexModel<WatchState>(Builders<WatchState>.IndexKeys.Ascending(x => x.ItemId), new CreateIndexOptions { Name = "item" }),
                    new CreateIndexModel<WatchState>(Builders<WatchState>.IndexKeys.Descending(x => x.LastPlayedAt), new CreateIndexOptions { Name = "last_played" })
                });
                await Cache.Indexes.CreateOneAsync(new CreateIndexModel<ProviderCacheEntry>(
                    Builders<ProviderCacheEntry>.IndexKeys.Ascending(x => x.ExpiresAt),
                    new CreateIndexOptions { Name = "ttl", ExpireAfter = TimeSpan.Zero }));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create the library indexes");
            }
        }

        #region Channel indexes

        public Task<List<string>> GetChannelDatabaseNames() => _db.GetAllChannelDatabaseNames();

        public async Task<List<BsonFileManagerModel>> GetChannelFiles(string channelId)
        {
            var col = _db.getDatabase(channelId).GetCollection<BsonFileManagerModel>("directory");
            return await col.Find(Builders<BsonFileManagerModel>.Filter.Eq(x => x.IsFile, true)).ToListAsync();
        }

        public async Task<List<string>> GetChannelFolders(string channelId)
        {
            var col = _db.getDatabase(channelId).GetCollection<BsonFileManagerModel>("directory");
            var folders = await col.Find(Builders<BsonFileManagerModel>.Filter.Eq(x => x.IsFile, false)).ToListAsync();
            return folders
                .Where(f => !string.IsNullOrEmpty(f.Name) && !(string.IsNullOrEmpty(f.FilterPath) && string.IsNullOrEmpty(f.FilePath)))
                .Select(f => Services.Library.LibraryFolderRules.Normalize((f.FilterPath ?? "/") + f.Name))
                .Distinct()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<BsonFileManagerModel?> GetChannelFile(string channelId, string fileId)
        {
            try
            {
                return await _db.getFileById(channelId, fileId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "File {FileId} of channel {ChannelId} could not be read", fileId, channelId);
                return null;
            }
        }

        #endregion

        #region Items

        public Task<LibraryItem?> GetItem(string id) =>
            Items.Find(x => x.Id == id).FirstOrDefaultAsync()!;

        public async Task<List<LibraryItem>> GetItems(IEnumerable<string> ids)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return new List<LibraryItem>();
            return await Items.Find(Builders<LibraryItem>.Filter.In(x => x.Id, list)).ToListAsync();
        }

        public Task<List<LibraryItem>> GetAllItems() => Items.Find(FilterDefinition<LibraryItem>.Empty).ToListAsync();

        public Task<LibraryItem?> FindItemByProvider(string provider, string providerId) =>
            Items.Find(x => x.Provider == provider && x.ProviderId == providerId).FirstOrDefaultAsync()!;

        public Task<LibraryItem?> FindItemByExternalId(string key, string value) =>
            Items.Find(Builders<LibraryItem>.Filter.Eq($"ExternalIds.{key}", value)).FirstOrDefaultAsync()!;

        public Task UpsertItem(LibraryItem item) =>
            Items.ReplaceOneAsync(x => x.Id == item.Id, item, new ReplaceOptions { IsUpsert = true });

        public async Task DeleteItem(string id)
        {
            await Items.DeleteOneAsync(x => x.Id == id);
            await Episodes.DeleteManyAsync(x => x.ItemId == id);
        }

        #endregion

        #region Episodes

        public Task<List<LibraryEpisode>> GetEpisodes(string itemId) =>
            Episodes.Find(x => x.ItemId == itemId).SortBy(x => x.Season).ThenBy(x => x.Number).ToListAsync();

        public async Task UpsertEpisodes(IEnumerable<LibraryEpisode> episodes)
        {
            var writes = new List<WriteModel<LibraryEpisode>>();
            foreach (var e in episodes)
            {
                var filter = Builders<LibraryEpisode>.Filter.Where(x => x.ItemId == e.ItemId && x.Season == e.Season && x.Number == e.Number);
                var update = Builders<LibraryEpisode>.Update
                    .Set(x => x.Title, e.Title)
                    .Set(x => x.Overview, e.Overview)
                    .Set(x => x.StillUrl, e.StillUrl)
                    .Set(x => x.AirDate, e.AirDate)
                    .Set(x => x.Runtime, e.Runtime)
                    .Set(x => x.Rating, e.Rating)
                    .SetOnInsert(x => x.Id, e.Id)
                    .SetOnInsert(x => x.ItemId, e.ItemId)
                    .SetOnInsert(x => x.Season, e.Season)
                    .SetOnInsert(x => x.Number, e.Number);
                writes.Add(new UpdateOneModel<LibraryEpisode>(filter, update) { IsUpsert = true });
            }
            if (writes.Count > 0)
                await Episodes.BulkWriteAsync(writes);
        }

        public Task DeleteEpisodes(string itemId) => Episodes.DeleteManyAsync(x => x.ItemId == itemId);

        #endregion

        #region Files

        public Task<LibraryFile?> GetFile(long channelId, string fileId) =>
            Files.Find(x => x.ChannelId == channelId && x.FileId == fileId).FirstOrDefaultAsync()!;

        public Task<List<LibraryFile>> GetFilesByChannel(long channelId) =>
            Files.Find(x => x.ChannelId == channelId).ToListAsync();

        public Task<List<LibraryFile>> GetFilesByItem(string itemId) =>
            Files.Find(x => x.ItemId == itemId).ToListAsync();

        public async Task<List<LibraryFile>> GetFilesByItems(IEnumerable<string> itemIds)
        {
            var list = itemIds.Distinct().ToList();
            if (list.Count == 0) return new List<LibraryFile>();
            return await Files.Find(Builders<LibraryFile>.Filter.In(x => x.ItemId, list)).ToListAsync();
        }

        public Task<List<LibraryFile>> GetAllFiles() => Files.Find(FilterDefinition<LibraryFile>.Empty).ToListAsync();

        public Task<List<LibraryFile>> GetFilesByStatus(string status) =>
            Files.Find(x => x.Status == status).ToListAsync();

        public async Task<Dictionary<string, int>> CountFilesByStatus()
        {
            var result = new Dictionary<string, int>();
            var groups = await Files.Aggregate()
                .Group(x => x.Status, g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            foreach (var g in groups)
                result[g.Status ?? string.Empty] = g.Count;
            return result;
        }

        public Task UpsertFile(LibraryFile file) =>
            Files.ReplaceOneAsync(x => x.ChannelId == file.ChannelId && x.FileId == file.FileId, file, new ReplaceOptions { IsUpsert = true });

        public Task DeleteFile(long channelId, string fileId) =>
            Files.DeleteOneAsync(x => x.ChannelId == channelId && x.FileId == fileId);

        public async Task<long> DeleteFilesByChannel(long channelId)
        {
            var r = await Files.DeleteManyAsync(x => x.ChannelId == channelId);
            return r.DeletedCount;
        }

        #endregion

        #region Watch

        public Task<WatchState?> GetWatch(long channelId, string fileId) =>
            Watch.Find(x => x.Id == LibraryFile.KeyOf(channelId, fileId)).FirstOrDefaultAsync()!;

        public async Task<List<WatchState>> GetWatchMany(IEnumerable<string> keys)
        {
            var list = keys.Distinct().ToList();
            if (list.Count == 0) return new List<WatchState>();
            return await Watch.Find(Builders<WatchState>.Filter.In(x => x.Id, list)).ToListAsync();
        }

        public Task<List<WatchState>> GetWatchByItem(string itemId) =>
            Watch.Find(x => x.ItemId == itemId).ToListAsync();

        public async Task<List<WatchState>> GetWatchByItems(IEnumerable<string> itemIds)
        {
            var list = itemIds.Distinct().ToList();
            if (list.Count == 0) return new List<WatchState>();
            return await Watch.Find(Builders<WatchState>.Filter.In(x => x.ItemId, list)).ToListAsync();
        }

        public Task<List<WatchState>> GetWatchInProgress() =>
            Watch.Find(x => !x.Completed && x.PositionMs > 0).SortByDescending(x => x.LastPlayedAt).ToListAsync();

        public Task<List<WatchState>> GetWatchCompleted() =>
            Watch.Find(x => x.Completed).SortByDescending(x => x.LastPlayedAt).ToListAsync();

        public Task UpsertWatch(WatchState state) =>
            Watch.ReplaceOneAsync(x => x.Id == state.Id, state, new ReplaceOptions { IsUpsert = true });

        public Task DeleteWatch(long channelId, string fileId) =>
            Watch.DeleteOneAsync(x => x.Id == LibraryFile.KeyOf(channelId, fileId));

        #endregion

        #region Scan state and cache

        public Task<LibraryScanState?> GetScanState() =>
            Scan.Find(x => x.Id == LibraryScanState.SingletonId).FirstOrDefaultAsync()!;

        public Task SaveScanState(LibraryScanState state) =>
            Scan.ReplaceOneAsync(x => x.Id == state.Id, state, new ReplaceOptions { IsUpsert = true });

        public async Task<string?> GetCached(string key)
        {
            var entry = await Cache.Find(x => x.Id == key).FirstOrDefaultAsync();
            if (entry == null || entry.ExpiresAt < DateTime.UtcNow) return null;
            return entry.Json;
        }

        public Task SetCached(string key, string json, TimeSpan ttl) =>
            Cache.ReplaceOneAsync(x => x.Id == key,
                new ProviderCacheEntry { Id = key, Json = json, ExpiresAt = DateTime.UtcNow.Add(ttl) },
                new ReplaceOptions { IsUpsert = true });

        #endregion
    }
}
