using TelegramDownloader.Models;
using TelegramDownloader.Services.Upload;

namespace TelegramDownloader.Services.Deletes
{
    /// <summary>
    /// What <see cref="ChannelDeleteRunner"/> needs from the channel index and
    /// from Telegram (the real one is <see cref="ChannelDeleteBackend"/>;
    /// tests use an in-memory channel).
    /// </summary>
    public interface IChannelDeleteBackend
    {
        /// <summary>Every entry (files and folders) whose <c>FilterPath</c> starts with one of the prefixes, in one query.</summary>
        Task<List<BsonFileManagerModel>> GetDescendantsAsync(IReadOnlyCollection<string> filterPathPrefixes, CancellationToken cancellationToken);

        /// <summary>Removes index entries by id (bulk).</summary>
        Task DeleteEntriesAsync(IReadOnlyCollection<string> ids);

        /// <summary>The ids among <paramref name="messageIds"/> that a remaining entry still references (one query).</summary>
        Task<HashSet<int>> GetReferencedMessageIdsAsync(IReadOnlyCollection<int> messageIds);

        /// <summary>Deletes up to <see cref="ChannelDeleteRunner.TelegramBatchSize"/> Telegram messages in one call.</summary>
        Task DeleteMessagesAsync(IReadOnlyList<int> messageIds, CancellationToken cancellationToken);

        /// <summary>Subtracts bytes from a folder and all its ancestors.</summary>
        Task SubtractFolderBytesAsync(string folderId, long bytes, string? filterIdHint);

        /// <summary>Recomputes the <c>HasChild</c> flag of a folder.</summary>
        Task RefreshHasChildAsync(string folderId);
    }

    /// <summary>Outcome of a channel delete.</summary>
    public sealed class ChannelDeleteResult
    {
        /// <summary>Ids of the selected entries that were deleted completely.</summary>
        public List<string> DeletedRootIds { get; } = new();

        /// <summary>Ids of the selected entries that were not (or only partly) deleted.</summary>
        public List<string> FailedRootIds { get; } = new();
    }

    /// <summary>
    /// The one delete implementation of a channel index, shared by the
    /// background delete operations, the synchronous v1 endpoint, WebDAV and
    /// the Blazor file manager:
    /// <list type="number">
    /// <item>counting: the selection plus every descendant of the selected folders (one query);</item>
    /// <item>files in chunks of <see cref="TelegramBatchSize"/>: index entries removed first (bulk),
    /// then the Telegram messages no remaining entry references are deleted in one call per
    /// chunk (only when the account may delete in the channel), honouring FLOOD_WAIT, and the
    /// bytes subtracted from the parent folders;</item>
    /// <item>the folder entries, and the <c>HasChild</c> flag of the parents.</item>
    /// </list>
    /// Deleting the entries before checking the references is what lets a
    /// message be freed: an entry still in the index references its own
    /// message, so checking first would always keep it.
    /// A cancel is honoured between chunks: what is deleted stays deleted and
    /// the index stays consistent (sizes are fixed per chunk).
    /// </summary>
    public sealed class ChannelDeleteRunner
    {
        /// <summary>Telegram accepts at most 100 ids per delete call.</summary>
        public const int TelegramBatchSize = 100;

        /// <summary>Folder entries removed per bulk delete.</summary>
        public const int FolderBatchSize = 500;

        /// <summary>FLOOD_WAIT retries of one batch before it is reported as failed.</summary>
        public const int MaxFloodWaits = 5;

        private readonly IChannelDeleteBackend _backend;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Func<DateTime> _clock;
        private readonly ILogger? _logger;

        public ChannelDeleteRunner(IChannelDeleteBackend backend, ILogger? logger = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTime>? clock = null)
        {
            _backend = backend;
            _logger = logger;
            _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>The prefix of the <c>FilterPath</c> of everything under a folder entry.</summary>
        public static string ChildPrefix(BsonFileManagerModel folder) => (folder.FilterPath ?? string.Empty) + folder.Name + "/";

        /// <summary>Message ids that back a file entry.</summary>
        public static IEnumerable<int> MessageIdsOf(BsonFileManagerModel entry)
        {
            if (!entry.IsFile) return Enumerable.Empty<int>();
            if (entry.isSplit) return entry.ListMessageId ?? Enumerable.Empty<int>();
            return entry.MessageId.HasValue ? new[] { entry.MessageId.Value } : Enumerable.Empty<int>();
        }

        private static string PathOf(BsonFileManagerModel entry) =>
            !string.IsNullOrEmpty(entry.FilePath) ? entry.FilePath : (entry.FilterPath ?? "/") + entry.Name;

        /// <summary>
        /// Deletes <paramref name="selected"/> (files and folders, recursively).
        /// Throws <see cref="OperationCanceledException"/> when cancelled.
        /// </summary>
        /// <param name="selected">Entries the user selected.</param>
        /// <param name="canDeleteMessages">False in channels where the account cannot delete messages: only the index is cleaned.</param>
        /// <param name="progress">Receives the counters (a throwaway one for synchronous callers).</param>
        /// <param name="cancellationToken">Honoured between chunks and during a FLOOD_WAIT.</param>
        public async Task<ChannelDeleteResult> RunAsync(IReadOnlyList<BsonFileManagerModel> selected, bool canDeleteMessages,
            DeleteOperation progress, CancellationToken cancellationToken = default)
        {
            var result = new ChannelDeleteResult();
            progress.SetIndexOnly(!canDeleteMessages);

            // ---- counting ----
            var roots = selected.Where(e => e != null && !string.IsNullOrEmpty(e.Id))
                .GroupBy(e => e.Id).Select(g => g.First()).ToList();
            var folderRoots = roots.Where(e => !e.IsFile).ToList();
            var all = new Dictionary<string, BsonFileManagerModel>(StringComparer.Ordinal);
            foreach (var root in roots) all[root.Id] = root;
            if (folderRoots.Count > 0)
            {
                var prefixes = folderRoots.Select(ChildPrefix).Distinct(StringComparer.Ordinal).ToList();
                foreach (var d in await _backend.GetDescendantsAsync(prefixes, cancellationToken))
                    if (!string.IsNullOrEmpty(d.Id)) all.TryAdd(d.Id, d);
            }

            // Which selected item each entry belongs to (to report failures per selected item).
            string RootOf(BsonFileManagerModel e)
            {
                if (roots.Any(r => r.Id == e.Id)) return e.Id;
                var path = e.FilterPath ?? string.Empty;
                var owner = folderRoots
                    .Where(f => path.StartsWith(ChildPrefix(f), StringComparison.Ordinal))
                    .OrderBy(f => ChildPrefix(f).Length)
                    .FirstOrDefault();
                return owner?.Id ?? e.Id;
            }

            var files = all.Values.Where(e => e.IsFile).OrderBy(e => e.FilterPath, StringComparer.Ordinal).ThenBy(e => e.Name, StringComparer.Ordinal).ToList();
            // Deepest folders first, so a cancelled run never leaves an orphan child folder.
            var folders = all.Values.Where(e => !e.IsFile).OrderByDescending(e => (e.FilterPath ?? string.Empty).Length).ToList();
            progress.SetTotals(files.Count, folders.Count, files.Sum(f => Math.Max(0, f.Size)));

            var failedRoots = new HashSet<string>(StringComparer.Ordinal);
            var touchedParents = new HashSet<string>(StringComparer.Ordinal);
            var deletedIds = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                // ---- files ----
                foreach (var chunk in files.Chunk(TelegramBatchSize))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress.SetCurrent(PathOf(chunk[^1]));
                    try
                    {
                        await _backend.DeleteEntriesAsync(chunk.Select(f => f.Id).ToList());
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Could not delete {Count} index entries", chunk.Length);
                        foreach (var f in chunk)
                        {
                            progress.AddFailure(PathOf(f), ex.Message);
                            failedRoots.Add(RootOf(f));
                        }
                        continue;
                    }
                    foreach (var f in chunk) deletedIds.Add(f.Id);

                    if (canDeleteMessages)
                        await DeleteMessagesOfAsync(chunk, progress, failedRoots, RootOf, cancellationToken);

                    await SubtractSizesAsync(chunk);
                    foreach (var f in chunk)
                        if (!string.IsNullOrEmpty(f.ParentId)) touchedParents.Add(f.ParentId);
                    progress.AddProcessed(chunk.Length, 0, chunk.Sum(f => Math.Max(0, f.Size)));
                }

                // ---- folders ----
                // A folder whose content could not be deleted is kept (with its ancestors).
                var keep = new HashSet<string>(StringComparer.Ordinal);
                foreach (var f in files.Where(f => !deletedIds.Contains(f.Id)))
                    foreach (var folder in folders)
                        if ((f.FilterPath ?? string.Empty).StartsWith(ChildPrefix(folder), StringComparison.Ordinal))
                            keep.Add(folder.Id);

                foreach (var chunk in folders.Where(f => !keep.Contains(f.Id)).Chunk(FolderBatchSize))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress.SetCurrent(PathOf(chunk[0]));
                    try
                    {
                        await _backend.DeleteEntriesAsync(chunk.Select(f => f.Id).ToList());
                        foreach (var f in chunk)
                        {
                            deletedIds.Add(f.Id);
                            if (!string.IsNullOrEmpty(f.ParentId)) touchedParents.Add(f.ParentId);
                        }
                        progress.AddProcessed(0, chunk.Length, 0);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Could not delete {Count} folder entries", chunk.Length);
                        foreach (var f in chunk)
                        {
                            progress.AddFailure(PathOf(f), ex.Message);
                            failedRoots.Add(RootOf(f));
                        }
                    }
                }
                foreach (var f in folders.Where(f => keep.Contains(f.Id)))
                    failedRoots.Add(RootOf(f));
            }
            finally
            {
                // Also after a cancel: the folders that lost children must not claim to have some.
                foreach (var parent in touchedParents.Where(p => !deletedIds.Contains(p)))
                {
                    try
                    {
                        await _backend.RefreshHasChildAsync(parent);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Could not refresh HasChild of folder {Id}", parent);
                    }
                }
                progress.SetCurrent(null);
            }

            foreach (var root in roots)
            {
                if (deletedIds.Contains(root.Id) && !failedRoots.Contains(root.Id)) result.DeletedRootIds.Add(root.Id);
                else result.FailedRootIds.Add(root.Id);
            }
            return result;
        }

        private async Task DeleteMessagesOfAsync(BsonFileManagerModel[] chunk, DeleteOperation progress,
            HashSet<string> failedRoots, Func<BsonFileManagerModel, string> rootOf, CancellationToken cancellationToken)
        {
            var ids = chunk.SelectMany(MessageIdsOf).Distinct().ToList();
            if (ids.Count == 0) return;

            // The chunk's entries are gone: what is still referenced belongs to another entry (a copy).
            HashSet<int> referenced = await _backend.GetReferencedMessageIdsAsync(ids);
            var free = ids.Where(id => !referenced.Contains(id)).ToList();

            foreach (var batch in free.Chunk(TelegramBatchSize))
            {
                try
                {
                    await DeleteBatchAsync(batch, progress, cancellationToken);
                    progress.AddMessagesDeleted(batch.Length);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Cancelled during a FLOOD_WAIT: the entries of this chunk are already gone.
                    ReportMessageFailure(chunk, batch, "Cancelled before the Telegram messages were deleted", progress, failedRoots, rootOf);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Could not delete {Count} Telegram messages", batch.Length);
                    ReportMessageFailure(chunk, batch, "Telegram: " + ex.Message, progress, failedRoots, rootOf);
                }
            }
        }

        private static void ReportMessageFailure(BsonFileManagerModel[] chunk, int[] batch, string reason, DeleteOperation progress,
            HashSet<string> failedRoots, Func<BsonFileManagerModel, string> rootOf)
        {
            var set = batch.ToHashSet();
            foreach (var f in chunk.Where(f => MessageIdsOf(f).Any(set.Contains)))
            {
                progress.AddFailure(PathOf(f), reason);
                failedRoots.Add(rootOf(f));
            }
        }

        private async Task DeleteBatchAsync(int[] batch, DeleteOperation progress, CancellationToken cancellationToken)
        {
            for (int waits = 0; ; waits++)
            {
                try
                {
                    await _backend.DeleteMessagesAsync(batch, cancellationToken);
                    return;
                }
                catch (Exception ex) when (waits < MaxFloodWaits && UploadRetryPolicy.GetTelegramWait(ex) is TimeSpan wait)
                {
                    _logger?.LogInformation("Telegram FLOOD_WAIT of {Seconds}s while deleting messages", wait.TotalSeconds);
                    progress.SetWaiting(_clock() + wait);
                    try
                    {
                        await _delay(wait, cancellationToken);
                    }
                    finally
                    {
                        progress.SetWaiting(null);
                    }
                }
            }
        }

        private async Task SubtractSizesAsync(BsonFileManagerModel[] chunk)
        {
            foreach (var group in chunk.Where(f => !string.IsNullOrEmpty(f.ParentId) && f.Size != 0).GroupBy(f => f.ParentId))
            {
                try
                {
                    await _backend.SubtractFolderBytesAsync(group.Key, group.Sum(f => f.Size), group.First().FilterId);
                }
                catch (Exception ex)
                {
                    // A missing ancestor must not stop the delete: sizes are informative.
                    _logger?.LogDebug(ex, "Could not update the size of folder {Id}", group.Key);
                }
            }
        }
    }
}
