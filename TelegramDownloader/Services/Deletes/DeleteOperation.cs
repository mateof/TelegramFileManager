namespace TelegramDownloader.Services.Deletes
{
    /// <summary>Values of <see cref="DeleteOperation.Kind"/>.</summary>
    public static class DeleteKinds
    {
        /// <summary>Entries of a channel index (and their Telegram messages).</summary>
        public const string Channel = "channel";

        /// <summary>Files and folders of the server's local storage.</summary>
        public const string Local = "local";
    }

    /// <summary>Values of <see cref="DeleteOperation.Phase"/>.</summary>
    public static class DeletePhases
    {
        /// <summary>Resolving the selection and counting what it contains.</summary>
        public const string Counting = "counting";

        /// <summary>Deleting; the totals are known.</summary>
        public const string Deleting = "deleting";

        /// <summary>Deleting, but paused because Telegram asked to wait (FLOOD_WAIT).</summary>
        public const string Waiting = "waiting";

        /// <summary>Finished (possibly with some failed items).</summary>
        public const string Done = "done";

        /// <summary>Aborted by an error that stopped the whole operation.</summary>
        public const string Failed = "failed";

        /// <summary>Stopped on request; what was deleted until then stays deleted.</summary>
        public const string Cancelled = "cancelled";

        public static bool IsFinal(string phase) => phase == Done || phase == Failed || phase == Cancelled;
    }

    /// <summary>An item that could not be deleted.</summary>
    public sealed record DeleteFailure(string Path, string Reason);

    /// <summary>Immutable copy of a <see cref="DeleteOperation"/> at one instant.</summary>
    public sealed record DeleteOperationSnapshot(
        string Id,
        string Kind,
        string? ChannelId,
        string? ChannelName,
        int ItemCount,
        string Phase,
        bool Counted,
        int TotalFiles,
        int TotalFolders,
        long TotalBytes,
        int ProcessedFiles,
        int ProcessedFolders,
        long ProcessedBytes,
        int MessagesDeleted,
        bool IndexOnly,
        string? CurrentItem,
        int FailedCount,
        IReadOnlyList<DeleteFailure> Failed,
        DateTime? WaitUntil,
        bool CancelRequested,
        string? Error,
        DateTime StartedAt,
        DateTime? FinishedAt,
        long Version);

    /// <summary>
    /// Live state of one background delete: what was selected, the totals once
    /// counted, what has been processed, the failures and the cancellation
    /// token. Every mutation bumps <see cref="Version"/> and raises
    /// <see cref="Changed"/>, which the events hub bridge throttles.
    /// Thread-safe: the runner writes while API requests read snapshots.
    /// </summary>
    public sealed class DeleteOperation
    {
        /// <summary>Failures kept in detail; the rest are only counted.</summary>
        public const int MaxFailuresKept = 100;

        private readonly object _lock = new object();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly List<DeleteFailure> _failed = new List<DeleteFailure>();
        private readonly Func<DateTime> _clock;

        private string _phase = DeletePhases.Counting;
        private bool _counted;
        private int _totalFiles, _totalFolders, _processedFiles, _processedFolders, _failedCount, _messagesDeleted;
        private long _totalBytes, _processedBytes, _version;
        private bool _indexOnly;
        private string? _current, _error;
        private DateTime? _waitUntil, _finishedAt;

        public DeleteOperation(string kind, string? channelId, string? channelName, int itemCount, Func<DateTime>? clock = null, string? id = null)
        {
            _clock = clock ?? (() => DateTime.UtcNow);
            Id = id ?? Guid.NewGuid().ToString("N");
            Kind = kind;
            ChannelId = channelId;
            ChannelName = channelName;
            ItemCount = itemCount;
            StartedAt = _clock();
        }

        public string Id { get; }
        public string Kind { get; }
        public string? ChannelId { get; }
        public string? ChannelName { get; }

        /// <summary>Items the client selected (top level).</summary>
        public int ItemCount { get; }
        public DateTime StartedAt { get; }

        /// <summary>Raised after every change (outside the lock).</summary>
        public event Action<DeleteOperation>? Changed;

        public CancellationToken Token => _cts.Token;
        public bool CancelRequested => _cts.IsCancellationRequested;

        public string Phase { get { lock (_lock) return _phase; } }
        public bool IsFinished { get { lock (_lock) return DeletePhases.IsFinal(_phase); } }
        public DateTime? FinishedAt { get { lock (_lock) return _finishedAt; } }
        public long Version { get { lock (_lock) return _version; } }

        /// <summary>Completes when the background work has ended (set by the registry).</summary>
        public Task Completion { get; internal set; } = Task.CompletedTask;

        /// <summary>Asks the runner to stop after the step in flight.</summary>
        public void Cancel()
        {
            if (IsFinished || _cts.IsCancellationRequested) return;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
            Mutate(() => { });
        }

        public void SetTotals(int files, int folders, long bytes) => Mutate(() =>
        {
            _totalFiles = files;
            _totalFolders = folders;
            _totalBytes = bytes;
            _counted = true;
            if (_phase == DeletePhases.Counting) _phase = DeletePhases.Deleting;
        });

        /// <summary>Only the index is cleaned: the account cannot delete messages in this channel.</summary>
        public void SetIndexOnly(bool indexOnly) => Mutate(() => _indexOnly = indexOnly);

        public void SetCurrent(string? item) => Mutate(() => _current = item);

        public void AddProcessed(int files, int folders, long bytes) => Mutate(() =>
        {
            _processedFiles += files;
            _processedFolders += folders;
            _processedBytes += bytes;
        });

        public void AddMessagesDeleted(int count) => Mutate(() => _messagesDeleted += count);

        public void AddFailure(string path, string reason) => Mutate(() =>
        {
            _failedCount++;
            if (_failed.Count < MaxFailuresKept) _failed.Add(new DeleteFailure(path, reason));
        });

        /// <summary>Telegram asked to wait until <paramref name="until"/> (null: the wait is over).</summary>
        public void SetWaiting(DateTime? until) => Mutate(() =>
        {
            _waitUntil = until;
            if (DeletePhases.IsFinal(_phase)) return;
            _phase = until.HasValue ? DeletePhases.Waiting : (_counted ? DeletePhases.Deleting : DeletePhases.Counting);
        });

        internal void Finish(string phase, string? error = null) => Mutate(() =>
        {
            if (DeletePhases.IsFinal(_phase)) return;
            _phase = phase;
            _error = error;
            _waitUntil = null;
            _current = null;
            _finishedAt = _clock();
        });

        public DeleteOperationSnapshot Snapshot()
        {
            lock (_lock)
            {
                return new DeleteOperationSnapshot(
                    Id, Kind, ChannelId, ChannelName, ItemCount, _phase, _counted,
                    _totalFiles, _totalFolders, _totalBytes,
                    _processedFiles, _processedFolders, _processedBytes,
                    _messagesDeleted, _indexOnly, _current,
                    _failedCount, _failed.ToList(), _waitUntil,
                    _cts.IsCancellationRequested, _error, StartedAt, _finishedAt, _version);
            }
        }

        private void Mutate(Action change)
        {
            lock (_lock)
            {
                change();
                _version++;
            }
            try
            {
                Changed?.Invoke(this);
            }
            catch
            {
                // A listener (the hub bridge) must never break the delete.
            }
        }
    }
}
