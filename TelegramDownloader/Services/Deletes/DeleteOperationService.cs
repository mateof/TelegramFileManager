using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services.Deletes
{
    /// <summary>
    /// Starts the background deletes of the v1 API on the
    /// <see cref="DeleteOperationRegistry"/>. Only singletons are captured, so
    /// the work outlives the HTTP request that started it.
    /// </summary>
    public sealed class DeleteOperationService
    {
        private readonly DeleteOperationRegistry _registry;
        private readonly IDbService _db;
        private readonly ITelegramService _telegram;
        private readonly ILogger<DeleteOperationService> _logger;

        public DeleteOperationService(DeleteOperationRegistry registry, IDbService db, ITelegramService telegram, ILogger<DeleteOperationService> logger)
        {
            _registry = registry;
            _db = db;
            _telegram = telegram;
            _logger = logger;
        }

        public DeleteOperationRegistry Registry => _registry;

        private string? ChannelName(string channelId)
        {
            try
            {
                return long.TryParse(channelId, out var id) ? _telegram.getChatName(id) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Deletes channel entries by id (folders recursively) in the background.</summary>
        public DeleteOperation StartChannel(string channelId, IReadOnlyList<string> ids)
        {
            var distinct = ids.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal).ToList();
            return _registry.Start(DeleteKinds.Channel, channelId, ChannelName(channelId), distinct.Count, async (operation, ct) =>
            {
                List<BsonFileManagerModel> entries = await _db.GetFilesByIds(channelId, distinct, ct);
                var found = entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var missing in distinct.Where(id => !found.Contains(id)))
                    operation.AddFailure(missing, "Not found");

                var runner = new ChannelDeleteRunner(new ChannelDeleteBackend(_db, _telegram, channelId), _logger);
                await runner.RunAsync(entries, ChannelDeleteBackend.CanDeleteMessages(_telegram, channelId), operation, ct);
            });
        }

        /// <summary>Deletes local files and folders (paths relative to <paramref name="root"/>) in the background.</summary>
        public DeleteOperation StartLocal(string root, IReadOnlyList<string> paths)
        {
            var list = paths.Where(p => p != null).Distinct(StringComparer.Ordinal).ToList();
            return _registry.Start(DeleteKinds.Local, null, null, list.Count, async (operation, ct) =>
            {
                await new LocalDeleteRunner(root, _logger).RunAsync(list, operation, ct);
            });
        }
    }
}
