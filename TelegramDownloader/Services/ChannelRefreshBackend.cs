using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services
{
    /// <summary>
    /// The per-filter high-water mark of a channel refresh, one document per
    /// filter in the channel's own database (collection
    /// <see cref="ChannelRefreshBackend.MarksCollection"/>).
    /// </summary>
    public class RefreshMarkDocument
    {
        /// <summary>The filter key: documents, audio, video or photos.</summary>
        [BsonId]
        public string Id { get; set; } = string.Empty;

        /// <summary>Every message of the filter with an id up to this one is in the index.</summary>
        public int LastMessageId { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>The real <see cref="IChannelRefreshBackend"/>: Telegram search plus the channel's Mongo database.</summary>
    public class ChannelRefreshBackend : IChannelRefreshBackend
    {
        public const string MarksCollection = "refresh_marks";

        private readonly string _channelId;
        private readonly ITelegramService _ts;
        private readonly IDbService _db;
        private BsonFileManagerModel? _rootFolder;

        public ChannelRefreshBackend(string channelId, ITelegramService ts, IDbService db)
        {
            _channelId = channelId;
            _ts = ts;
            _db = db;
        }

        public async Task<IReadOnlyCollection<int>> LoadIndexedIdsAsync() => await _db.getAllIdsFromChannel(_channelId);

        public async Task<IReadOnlyCollection<string>> LoadFileNamesAsync() => await _db.getAllFileNamesFromChannel(_channelId);

        public async Task<IReadOnlyDictionary<RefreshFilter, int>> LoadMarksAsync()
        {
            List<RefreshMarkDocument> docs = await Marks().Find(FilterDefinition<RefreshMarkDocument>.Empty).ToListAsync();
            Dictionary<RefreshFilter, int> marks = new Dictionary<RefreshFilter, int>();
            foreach (RefreshMarkDocument d in docs)
            {
                if (RefreshFilterNames.TryParse(d.Id, out RefreshFilter f))
                    marks[f] = d.LastMessageId;
            }
            return marks;
        }

        public Task<IReadOnlyList<TelegramChatDocuments>> SearchAsync(RefreshFilter filter, int floor, RefreshFilterProgress progress, CancellationToken cancellationToken) =>
            _ts.searchChannelFilter(Convert.ToInt64(_channelId), filter, floor, progress, cancellationToken);

        public async Task InsertAsync(IReadOnlyList<TelegramChatDocuments> batch)
        {
            _rootFolder ??= await _db.getRootFolder(_channelId);
            List<BsonFileManagerModel> models = batch.Select(tcd => ChannelRefreshPlanner.ToRootFileEntry(tcd, _rootFolder.Id)).ToList();
            await _db.createEntries(_channelId, models);
        }

        public async Task SaveMarksAsync(IReadOnlyDictionary<RefreshFilter, int> marks)
        {
            DateTime now = DateTime.UtcNow;
            List<WriteModel<RefreshMarkDocument>> writes = marks.Select(kv => (WriteModel<RefreshMarkDocument>)new ReplaceOneModel<RefreshMarkDocument>(
                Builders<RefreshMarkDocument>.Filter.Eq(d => d.Id, kv.Key.ToKey()),
                new RefreshMarkDocument { Id = kv.Key.ToKey(), LastMessageId = kv.Value, UpdatedAt = now })
            { IsUpsert = true }).ToList();
            if (writes.Count > 0)
                await Marks().BulkWriteAsync(writes);
        }

        private IMongoCollection<RefreshMarkDocument> Marks() =>
            _db.getDatabase(_channelId).GetCollection<RefreshMarkDocument>(MarksCollection);
    }
}
