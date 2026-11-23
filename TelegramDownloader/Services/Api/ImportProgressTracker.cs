using System.Collections.Concurrent;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Services.Api
{
    /// <summary>
    /// Gives every background import started through the API an id and turns
    /// the ticks of its <see cref="GenericNotificationProgressModel"/> into
    /// <see cref="ImportProgressEventDto"/> messages, which the events hub
    /// broadcasts as <c>ImportProgress</c>.
    /// </summary>
    public sealed class ImportProgressTracker
    {
        private readonly ConcurrentDictionary<string, string> _active = new(StringComparer.Ordinal);

        /// <summary>Raised for every progress tick and for the final message.</summary>
        public event Action<ImportProgressEventDto>? Progress;

        /// <summary>Ids of the imports still running.</summary>
        public IReadOnlyCollection<string> ActiveIds => _active.Keys.ToList();

        /// <summary>
        /// Registers a new import of <paramref name="kind"/> and returns its id
        /// plus the progress model to hand to the import routine.
        /// </summary>
        public (string Id, GenericNotificationProgressModel Model) Begin(string kind)
        {
            var id = Guid.NewGuid().ToString("N");
            _active[id] = kind;
            var model = new GenericNotificationProgressModel();
            model.EventNotification += (_, e) =>
            {
                if (!_active.ContainsKey(id)) return;
                var dto = EventMapping.ToImportProgress(id, kind, e);
                if (dto.Done) _active.TryRemove(id, out string? _);
                Raise(dto);
            };
            Raise(new ImportProgressEventDto { Id = id, Kind = kind, Message = "Starting" });
            return (id, model);
        }

        /// <summary>Sends the final message when the routine returned without a final tick.</summary>
        public void Complete(string id)
        {
            if (!_active.TryRemove(id, out var kind)) return;
            Raise(new ImportProgressEventDto { Id = id, Kind = kind, Percent = 100, Done = true, Message = "Completed" });
        }

        /// <summary>Sends the final message of a failed import.</summary>
        public void Fail(string id, string error)
        {
            if (!_active.TryRemove(id, out var kind)) kind = ImportKinds.Index;
            Raise(new ImportProgressEventDto { Id = id, Kind = kind, Done = true, Message = "Failed", Error = error });
        }

        /// <summary>
        /// Runs <paramref name="work"/> in the background and reports its end
        /// (completion or failure) through <see cref="Progress"/>.
        /// </summary>
        public string Run(string kind, Func<GenericNotificationProgressModel, Task> work, ILogger logger, Action? cleanup = null)
        {
            var (id, model) = Begin(kind);
            _ = Task.Run(async () =>
            {
                try
                {
                    await work(model);
                    Complete(id);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Background {Kind} import {Id} failed", kind, id);
                    Fail(id, ex.Message);
                }
                finally
                {
                    try { cleanup?.Invoke(); } catch { }
                }
            });
            return id;
        }

        private void Raise(ImportProgressEventDto dto)
        {
            try { Progress?.Invoke(dto); }
            catch { /* a listener must never break an import */ }
        }
    }
}
