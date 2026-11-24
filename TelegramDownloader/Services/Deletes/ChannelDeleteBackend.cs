using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services.Deletes
{
    /// <summary>The real <see cref="IChannelDeleteBackend"/>: the channel's Mongo index and Telegram.</summary>
    public sealed class ChannelDeleteBackend : IChannelDeleteBackend
    {
        private const string Collection = "directory";

        private readonly IDbService _db;
        private readonly ITelegramService _telegram;
        private readonly string _dbName;

        public ChannelDeleteBackend(IDbService db, ITelegramService telegram, string dbName)
        {
            _db = db;
            _telegram = telegram;
            _dbName = dbName;
        }

        private IMongoCollection<BsonFileManagerModel> Entries => _db.getDatabase(_dbName).GetCollection<BsonFileManagerModel>(Collection);

        /// <summary>
        /// Whether the account may delete messages in the chat (same rule as
        /// the Blazor file manager): false for channels where it is not admin,
        /// and when the chat is unknown.
        /// </summary>
        public static bool CanDeleteMessages(ITelegramService telegram, string chatId)
        {
            try
            {
                return long.TryParse(chatId, out var id) && telegram.isMyChat(id);
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<BsonFileManagerModel>> GetDescendantsAsync(IReadOnlyCollection<string> filterPathPrefixes, CancellationToken cancellationToken)
        {
            if (filterPathPrefixes.Count == 0) return new List<BsonFileManagerModel>();
            var b = Builders<BsonFileManagerModel>.Filter;
            var filter = b.Or(filterPathPrefixes.Select(p => b.Regex(x => x.FilterPath, new BsonRegularExpression("^" + Regex.Escape(p)))));
            return await Entries.Find(filter).ToListAsync(cancellationToken);
        }

        public Task DeleteEntriesAsync(IReadOnlyCollection<string> ids) =>
            ids.Count == 0 ? Task.CompletedTask : Entries.DeleteManyAsync(Builders<BsonFileManagerModel>.Filter.In(x => x.Id, ids));

        public async Task<HashSet<int>> GetReferencedMessageIdsAsync(IReadOnlyCollection<int> messageIds)
        {
            var result = new HashSet<int>();
            if (messageIds.Count == 0) return result;
            var b = Builders<BsonFileManagerModel>.Filter;
            var filter = b.In(x => x.MessageId, messageIds.Select(i => (int?)i)) | b.AnyIn(x => x.ListMessageId, messageIds);
            var hits = await Entries.Find(filter).Project(x => new { x.MessageId, x.ListMessageId }).ToListAsync();
            var wanted = messageIds.ToHashSet();
            foreach (var h in hits)
            {
                if (h.MessageId.HasValue && wanted.Contains(h.MessageId.Value)) result.Add(h.MessageId.Value);
                if (h.ListMessageId != null)
                    foreach (var id in h.ListMessageId)
                        if (wanted.Contains(id)) result.Add(id);
            }
            return result;
        }

        public Task DeleteMessagesAsync(IReadOnlyList<int> messageIds, CancellationToken cancellationToken) =>
            _telegram.deleteFiles(_dbName, messageIds);

        public Task SubtractFolderBytesAsync(string folderId, long bytes, string? filterIdHint) =>
            _db.subBytesToFolder(_dbName, folderId, bytes, filterIdHint: filterIdHint);

        public Task RefreshHasChildAsync(string folderId) => _db.checkAndSetDirectoryHasChild(_dbName, folderId);
    }
}
