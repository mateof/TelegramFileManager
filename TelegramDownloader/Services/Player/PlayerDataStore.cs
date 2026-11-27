using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Services.Player
{
    /// <summary>A favourite track (one document per key).</summary>
    [BsonIgnoreExtraElements]
    public class PlayerFavoriteDoc
    {
        [BsonId]
        public string Key { get; set; } = string.Empty;
        public ApiPlayerTrackDto Track { get; set; } = new();
        public DateTime AddedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>One play of the history.</summary>
    [BsonIgnoreExtraElements]
    public class PlayerHistoryDoc
    {
        [BsonId]
        public ObjectId Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public ApiPlayerTrackDto Track { get; set; } = new();
        public DateTime PlayedAt { get; set; }
        public double? PositionSeconds { get; set; }
        public bool Completed { get; set; }
    }

    /// <summary>A resume position (one document per key).</summary>
    [BsonIgnoreExtraElements]
    public class PlayerResumeDoc
    {
        [BsonId]
        public string Key { get; set; } = string.Empty;
        public ApiPlayerTrackDto Track { get; set; } = new();
        public double PositionSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>Persistence of the shared player data (Mongo in the app, in memory in tests).</summary>
    public interface IPlayerDataStore
    {
        /// <summary>Adds or refreshes a favourite; the first <c>AddedAt</c> is kept. Returns the stored document.</summary>
        Task<PlayerFavoriteDoc> UpsertFavorite(ApiPlayerTrackDto track, DateTime now);
        Task<(List<PlayerFavoriteDoc> Items, long Total)> ListFavorites(int skip, int take);
        Task<List<string>> FavoriteKeys();
        Task DeleteFavorite(string key);

        Task InsertHistory(PlayerHistoryDoc entry);
        Task<long> CountHistory();
        /// <summary>Deletes the <paramref name="count"/> oldest plays (by <c>PlayedAt</c>).</summary>
        Task DeleteOldestHistory(int count);
        /// <summary>Newest first, strictly before <paramref name="before"/> when given.</summary>
        Task<List<PlayerHistoryDoc>> ListHistory(DateTime? before, int take);
        /// <summary>Every play since <paramref name="since"/> (all when null): key, track and date only.</summary>
        Task<List<PlayerHistoryDoc>> HistorySince(DateTime? since);
        Task ClearHistory();

        Task UpsertResume(PlayerResumeDoc entry);
        Task<PlayerResumeDoc?> GetResume(string key);
        Task<List<PlayerResumeDoc>> ListResume(int take);
        Task DeleteResume(string key);
    }

    /// <summary>
    /// Mongo store of the player data, in the configuration database
    /// (<c>TCCONFIG</c>, next to the playlists): collections
    /// <c>player_favorites</c> and <c>player_resume</c> (keyed by the track key)
    /// and <c>player_history</c> (indexed by key and play date).
    /// </summary>
    public sealed class MongoPlayerDataStore : IPlayerDataStore
    {
        public const string FavoritesCollection = "player_favorites";
        public const string HistoryCollection = "player_history";
        public const string ResumeCollection = "player_resume";

        private readonly IDbService _db;
        private readonly ILogger<MongoPlayerDataStore> _logger;
        private readonly SemaphoreSlim _indexGate = new(1, 1);
        private volatile bool _indexesReady;

        public MongoPlayerDataStore(IDbService db, ILogger<MongoPlayerDataStore> logger)
        {
            _db = db;
            _logger = logger;
        }

        // The IDbService client can be re-created after setup: resolve on every call.
        private IMongoDatabase Db => _db.getDatabase(DbService.CONFIG_DB_NAME);
        private IMongoCollection<PlayerFavoriteDoc> Favorites => Db.GetCollection<PlayerFavoriteDoc>(FavoritesCollection);
        private IMongoCollection<PlayerHistoryDoc> History => Db.GetCollection<PlayerHistoryDoc>(HistoryCollection);
        private IMongoCollection<PlayerResumeDoc> Resume => Db.GetCollection<PlayerResumeDoc>(ResumeCollection);

        /// <summary>Creates the indexes once per process (retried on the next call if it failed).</summary>
        private async Task Ready()
        {
            if (_indexesReady) return;
            await _indexGate.WaitAsync();
            try
            {
                if (_indexesReady) return;
                await Favorites.Indexes.CreateOneAsync(new CreateIndexModel<PlayerFavoriteDoc>(
                    Builders<PlayerFavoriteDoc>.IndexKeys.Descending(x => x.AddedAt), new CreateIndexOptions { Name = "added" }));
                await History.Indexes.CreateManyAsync(new[]
                {
                    new CreateIndexModel<PlayerHistoryDoc>(Builders<PlayerHistoryDoc>.IndexKeys.Ascending(x => x.Key), new CreateIndexOptions { Name = "key" }),
                    new CreateIndexModel<PlayerHistoryDoc>(Builders<PlayerHistoryDoc>.IndexKeys.Descending(x => x.PlayedAt), new CreateIndexOptions { Name = "played" })
                });
                await Resume.Indexes.CreateOneAsync(new CreateIndexModel<PlayerResumeDoc>(
                    Builders<PlayerResumeDoc>.IndexKeys.Descending(x => x.UpdatedAt), new CreateIndexOptions { Name = "updated" }));
                _indexesReady = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create the player data indexes");
            }
            finally
            {
                _indexGate.Release();
            }
        }

        public async Task<PlayerFavoriteDoc> UpsertFavorite(ApiPlayerTrackDto track, DateTime now)
        {
            await Ready();
            var update = Builders<PlayerFavoriteDoc>.Update
                .Set(x => x.Track, track)
                .Set(x => x.UpdatedAt, now)
                .SetOnInsert(x => x.AddedAt, now);
            return await Favorites.FindOneAndUpdateAsync(
                Builders<PlayerFavoriteDoc>.Filter.Eq(x => x.Key, track.Key),
                update,
                new FindOneAndUpdateOptions<PlayerFavoriteDoc> { IsUpsert = true, ReturnDocument = ReturnDocument.After });
        }

        public async Task<(List<PlayerFavoriteDoc> Items, long Total)> ListFavorites(int skip, int take)
        {
            await Ready();
            var all = Builders<PlayerFavoriteDoc>.Filter.Empty;
            var total = await Favorites.CountDocumentsAsync(all);
            var items = await Favorites.Find(all)
                .Sort(Builders<PlayerFavoriteDoc>.Sort.Descending(x => x.AddedAt).Ascending(x => x.Key))
                .Skip(skip).Limit(take).ToListAsync();
            return (items, total);
        }

        public async Task<List<string>> FavoriteKeys()
        {
            await Ready();
            return await Favorites.Find(Builders<PlayerFavoriteDoc>.Filter.Empty)
                .Project(x => x.Key).ToListAsync();
        }

        public async Task DeleteFavorite(string key)
        {
            await Ready();
            await Favorites.DeleteOneAsync(Builders<PlayerFavoriteDoc>.Filter.Eq(x => x.Key, key));
        }

        public async Task InsertHistory(PlayerHistoryDoc entry)
        {
            await Ready();
            if (entry.Id == ObjectId.Empty) entry.Id = ObjectId.GenerateNewId();
            await History.InsertOneAsync(entry);
        }

        public async Task<long> CountHistory()
        {
            await Ready();
            return await History.CountDocumentsAsync(Builders<PlayerHistoryDoc>.Filter.Empty);
        }

        public async Task DeleteOldestHistory(int count)
        {
            if (count <= 0) return;
            await Ready();
            var ids = await History.Find(Builders<PlayerHistoryDoc>.Filter.Empty)
                .Sort(Builders<PlayerHistoryDoc>.Sort.Ascending(x => x.PlayedAt).Ascending(x => x.Id))
                .Limit(count).Project(x => x.Id).ToListAsync();
            if (ids.Count > 0)
                await History.DeleteManyAsync(Builders<PlayerHistoryDoc>.Filter.In(x => x.Id, ids));
        }

        public async Task<List<PlayerHistoryDoc>> ListHistory(DateTime? before, int take)
        {
            await Ready();
            var filter = before.HasValue
                ? Builders<PlayerHistoryDoc>.Filter.Lt(x => x.PlayedAt, before.Value)
                : Builders<PlayerHistoryDoc>.Filter.Empty;
            return await History.Find(filter)
                .Sort(Builders<PlayerHistoryDoc>.Sort.Descending(x => x.PlayedAt).Descending(x => x.Id))
                .Limit(take).ToListAsync();
        }

        public async Task<List<PlayerHistoryDoc>> HistorySince(DateTime? since)
        {
            await Ready();
            var filter = since.HasValue
                ? Builders<PlayerHistoryDoc>.Filter.Gte(x => x.PlayedAt, since.Value)
                : Builders<PlayerHistoryDoc>.Filter.Empty;
            return await History.Find(filter).ToListAsync();
        }

        public async Task ClearHistory()
        {
            await Ready();
            await History.DeleteManyAsync(Builders<PlayerHistoryDoc>.Filter.Empty);
        }

        public async Task UpsertResume(PlayerResumeDoc entry)
        {
            await Ready();
            await Resume.ReplaceOneAsync(Builders<PlayerResumeDoc>.Filter.Eq(x => x.Key, entry.Key), entry, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<PlayerResumeDoc?> GetResume(string key)
        {
            await Ready();
            return await Resume.Find(Builders<PlayerResumeDoc>.Filter.Eq(x => x.Key, key)).FirstOrDefaultAsync();
        }

        public async Task<List<PlayerResumeDoc>> ListResume(int take)
        {
            await Ready();
            return await Resume.Find(Builders<PlayerResumeDoc>.Filter.Empty)
                .Sort(Builders<PlayerResumeDoc>.Sort.Descending(x => x.UpdatedAt))
                .Limit(take).ToListAsync();
        }

        public async Task DeleteResume(string key)
        {
            await Ready();
            await Resume.DeleteOneAsync(Builders<PlayerResumeDoc>.Filter.Eq(x => x.Key, key));
        }
    }
}
