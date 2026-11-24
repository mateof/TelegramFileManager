using System.Collections.Concurrent;

namespace TelegramDownloader.Services.Deletes
{
    /// <summary>Outcome of <see cref="DeleteOperationRegistry.TryCancel"/>.</summary>
    public enum DeleteCancelResult
    {
        NotFound,
        AlreadyFinished,
        Cancelling
    }

    /// <summary>
    /// The background deletes of the server (channel index + Telegram messages,
    /// or local files), each with its live progress and cancellation token.
    /// Finished operations are kept for <see cref="Retention"/> so a client
    /// that polls late still reads the outcome.
    /// </summary>
    public sealed class DeleteOperationRegistry
    {
        /// <summary>How long a finished operation stays readable.</summary>
        public static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);

        private readonly ConcurrentDictionary<string, DeleteOperation> _operations = new(StringComparer.Ordinal);
        private readonly Func<DateTime> _clock;
        private readonly ILogger<DeleteOperationRegistry>? _logger;

        public DeleteOperationRegistry(ILogger<DeleteOperationRegistry>? logger = null) : this(null, logger) { }

        public DeleteOperationRegistry(Func<DateTime>? clock, ILogger<DeleteOperationRegistry>? logger = null)
        {
            _clock = clock ?? (() => DateTime.UtcNow);
            _logger = logger;
        }

        /// <summary>Raised on every change of any operation (progress, end).</summary>
        public event Action<DeleteOperation>? Changed;

        /// <summary>
        /// Registers an operation and runs <paramref name="work"/> in the
        /// background. The work reports into the operation; it ends as
        /// <c>done</c>, <c>cancelled</c> (cancellation requested and an
        /// <see cref="OperationCanceledException"/>) or <c>failed</c>.
        /// </summary>
        public DeleteOperation Start(string kind, string? channelId, string? channelName, int itemCount,
            Func<DeleteOperation, CancellationToken, Task> work)
        {
            Prune();
            var operation = new DeleteOperation(kind, channelId, channelName, itemCount, _clock);
            operation.Changed += OnChanged;
            _operations[operation.Id] = operation;
            operation.Completion = Task.Run(() => RunAsync(operation, work));
            return operation;
        }

        private async Task RunAsync(DeleteOperation operation, Func<DeleteOperation, CancellationToken, Task> work)
        {
            try
            {
                await work(operation, operation.Token);
                // Completed every step: done, even if a cancel arrived after the last one.
                operation.Finish(DeletePhases.Done);
            }
            catch (OperationCanceledException) when (operation.CancelRequested)
            {
                operation.Finish(DeletePhases.Cancelled);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Delete operation {Id} ({Kind}) failed", operation.Id, operation.Kind);
                operation.Finish(DeletePhases.Failed, ex.Message);
            }
        }

        private void OnChanged(DeleteOperation operation)
        {
            try
            {
                Changed?.Invoke(operation);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Delete operation listener failed");
            }
        }

        public DeleteOperation? Get(string id)
        {
            Prune();
            return _operations.TryGetValue(id, out var operation) ? operation : null;
        }

        /// <summary>Running operations first, then the recent ones, newest first.</summary>
        public IReadOnlyList<DeleteOperationSnapshot> Snapshots()
        {
            Prune();
            return _operations.Values
                .Select(o => o.Snapshot())
                .OrderBy(s => DeletePhases.IsFinal(s.Phase))
                .ThenByDescending(s => s.StartedAt)
                .ToList();
        }

        /// <summary>The operations still running.</summary>
        public IReadOnlyList<DeleteOperation> Running() =>
            _operations.Values.Where(o => !o.IsFinished).ToList();

        public DeleteCancelResult TryCancel(string id)
        {
            var operation = Get(id);
            if (operation == null) return DeleteCancelResult.NotFound;
            if (operation.IsFinished) return DeleteCancelResult.AlreadyFinished;
            operation.Cancel();
            return DeleteCancelResult.Cancelling;
        }

        /// <summary>Forgets the operations that finished more than <see cref="Retention"/> ago.</summary>
        public void Prune()
        {
            var limit = _clock() - Retention;
            foreach (var (id, operation) in _operations)
            {
                var finished = operation.FinishedAt;
                if (finished.HasValue && finished.Value < limit && _operations.TryRemove(id, out var removed))
                    removed.Changed -= OnChanged;
            }
        }
    }
}
