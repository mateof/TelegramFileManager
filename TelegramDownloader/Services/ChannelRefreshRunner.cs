using Microsoft.Extensions.Logging;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services
{
    /// <summary>
    /// What a channel refresh needs from Telegram and from the channel index.
    /// Kept narrow so <see cref="ChannelRefreshRunner"/> can be tested without
    /// either.
    /// </summary>
    public interface IChannelRefreshBackend
    {
        /// <summary>Message ids of every file already in the index.</summary>
        Task<IReadOnlyCollection<int>> LoadIndexedIdsAsync();

        /// <summary>Names of every file already in the index (for de-duplication).</summary>
        Task<IReadOnlyCollection<string>> LoadFileNamesAsync();

        /// <summary>The stored high-water mark of each filter that has one.</summary>
        Task<IReadOnlyDictionary<RefreshFilter, int>> LoadMarksAsync();

        /// <summary>
        /// Every message of <paramref name="filter"/> newer than
        /// <paramref name="floor"/> (all of them when 0), newest first. Reports
        /// each page to <paramref name="progress"/>, checks
        /// <paramref name="cancellationToken"/> between pages and throws
        /// <see cref="OperationCanceledException"/> when it fires.
        /// </summary>
        Task<IReadOnlyList<TelegramChatDocuments>> SearchAsync(RefreshFilter filter, int floor, RefreshFilterProgress progress, CancellationToken cancellationToken);

        /// <summary>Writes one batch of new files to the index (all or nothing).</summary>
        Task InsertAsync(IReadOnlyList<TelegramChatDocuments> batch);

        /// <summary>Stores the given marks (only the filters present are touched).</summary>
        Task SaveMarksAsync(IReadOnlyDictionary<RefreshFilter, int> marks);
    }

    /// <summary>
    /// One channel refresh: search the selected filters on Telegram, index what
    /// is new in batches, then advance each filter's high-water mark as far as
    /// the index is now known to be complete. The filters are searched at once,
    /// or one after another (<c>oneAtATime</c>): then each one is indexed and its
    /// mark stored before the next one starts. Never throws: failures and
    /// cancellation end up in the returned <see cref="ChannelRefreshResult"/>.
    /// </summary>
    public static class ChannelRefreshRunner
    {
        private enum SearchState { Completed, Canceled, Failed }

        private sealed class FilterOutcome
        {
            public SearchState State;
            public int Floor;
            public IReadOnlyList<TelegramChatDocuments> Docs = Array.Empty<TelegramChatDocuments>();
        }

        /// <summary>What is known of the channel along a run, shared by its groups of filters.</summary>
        private sealed class Run
        {
            public required string ChannelId;
            public required IChannelRefreshBackend Backend;
            public required ChannelRefreshProgress Progress;
            public required ChannelRefreshResult Result;
            public required ILogger Logger;
            public required int BatchSize;
            public required bool Force;
            public required IReadOnlyDictionary<RefreshFilter, int> Marks;
            public required Task<IReadOnlyCollection<int>> IndexedTask;
            public required Task<IReadOnlyCollection<string>> NamesTask;
            public HashSet<int>? Indexed;
            public HashSet<string>? Names;
        }

        public static async Task<ChannelRefreshResult> RunAsync(
            string channelId,
            IChannelRefreshBackend backend,
            IReadOnlyList<RefreshFilter> filters,
            bool force,
            ChannelRefreshProgress progress,
            ILogger logger,
            int batchSize = ChannelRefreshPlanner.InsertBatchSize,
            bool oneAtATime = false)
        {
            CancellationToken ct = progress.Token;
            ChannelRefreshResult result = new ChannelRefreshResult
            {
                ChannelId = channelId,
                StartedAt = progress.StartedAt
            };
            // Counters exist from the start, so a poller sees every selected filter at 0.
            foreach (RefreshFilter f in filters) progress.Filter(f);
            progress.Phase = RefreshPhase.Searching;

            try
            {
                // Load what is already indexed once, in parallel with the search.
                Task<IReadOnlyCollection<int>> indexedTask = backend.LoadIndexedIdsAsync();
                Task<IReadOnlyCollection<string>> namesTask = backend.LoadFileNamesAsync();
                IReadOnlyDictionary<RefreshFilter, int> marks = await backend.LoadMarksAsync();

                List<RefreshFilter> fullScans = ChannelRefreshPlanner.FiltersNeedingFullScan(filters, marks, force);
                if (fullScans.Count > 0)
                    logger.LogInformation("Refresh {ChannelId}: full scan of {Filters} (forced or no high-water mark yet)",
                        channelId, string.Join(", ", fullScans.Select(f => f.ToKey())));

                Run run = new Run
                {
                    ChannelId = channelId, Backend = backend, Progress = progress, Result = result, Logger = logger, BatchSize = batchSize,
                    Force = force, Marks = marks, IndexedTask = indexedTask, NamesTask = namesTask
                };
                IEnumerable<IReadOnlyList<RefreshFilter>> groups = oneAtATime
                    ? filters.Select(f => (IReadOnlyList<RefreshFilter>)new[] { f })
                    : new[] { filters };
                foreach (IReadOnlyList<RefreshFilter> group in groups)
                {
                    if (ct.IsCancellationRequested) result.Canceled = true;
                    if (result.Canceled) break;
                    await RunGroupAsync(run, group, ct);
                }
            }
            catch (Exception ex)
            {
                result.Fail(ex.Message);
                logger.LogError(ex, "Refresh of channel {ChannelId} failed", channelId);
            }
            finally
            {
                progress.Phase = RefreshPhase.Done;
                result.FinishedAt = DateTime.Now;
            }
            return result;
        }

        /// <summary>Searches <paramref name="filters"/> at once, indexes what is new and stores their marks.</summary>
        private static async Task RunGroupAsync(Run run, IReadOnlyList<RefreshFilter> filters, CancellationToken ct)
        {
            (string channelId, IChannelRefreshBackend backend, ChannelRefreshProgress progress, ChannelRefreshResult result, ILogger logger) =
                (run.ChannelId, run.Backend, run.Progress, run.Result, run.Logger);
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            progress.Phase = RefreshPhase.Searching;

            // Each filter is an independent paged search, so they run concurrently.
            Dictionary<RefreshFilter, FilterOutcome> outcomes = filters.ToDictionary(f => f, f => new FilterOutcome
            {
                Floor = ChannelRefreshPlanner.ScanFloor(f, run.Marks, run.Force)
            });
            await Task.WhenAll(filters.Select(async f =>
            {
                FilterOutcome outcome = outcomes[f];
                RefreshFilterProgress fp = progress.Filter(f);
                fp.MarkStarted();
                try
                {
                    outcome.Docs = await backend.SearchAsync(f, outcome.Floor, fp, ct);
                    outcome.State = SearchState.Completed;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    outcome.State = SearchState.Canceled;
                }
                catch (Exception ex)
                {
                    outcome.State = SearchState.Failed;
                    lock (result) result.Fail($"{f.ToKey()}: {ex.Message}");
                    logger.LogError(ex, "Refresh {ChannelId}: search of {Filter} failed", channelId, f.ToKey());
                }
                finally
                {
                    fp.MarkDone();
                }
            }));
            long searchMs = sw.ElapsedMilliseconds;

            HashSet<int> indexed = run.Indexed ??= (await run.IndexedTask).ToHashSet();
            HashSet<string> names = run.Names ??= new HashSet<string>(await run.NamesTask, StringComparer.OrdinalIgnoreCase);

            if (ct.IsCancellationRequested)
            {
                // Nothing of these filters was written yet.
                result.Canceled = true;
                logger.LogInformation("Refresh {ChannelId} canceled while searching Telegram ({Filters})",
                    channelId, string.Join(", ", filters.Select(f => f.ToKey())));
                return;
            }

            // Merge in filter order (earlier filter wins a duplicate id). A
            // filter that failed contributes nothing and keeps its mark.
            List<TelegramChatDocuments> merged = ChannelRefreshPlanner.MergePhaseResults(
                filters.Select(f => outcomes[f].State == SearchState.Completed ? outcomes[f].Docs : Array.Empty<TelegramChatDocuments>()));
            // Nameless entries are never indexed, by design; they must not hold a mark back forever.
            HashSet<int> skippedByDesign = merged.Where(d => d.name == null).Select(d => d.id).ToHashSet();
            merged = merged.Where(d => d.name != null).ToList();

            ChannelRefreshPlanner.AssignUniqueNames(names, merged);
            List<TelegramChatDocuments> newDocs = ChannelRefreshPlanner.SelectNew(merged, indexed);
            progress.NewFiles += newDocs.Count;
            progress.Phase = RefreshPhase.Indexing;

            // One InsertMany per batch; counted per batch so a cancel or a
            // failure reports exactly what got in.
            int inserted = 0;
            foreach (TelegramChatDocuments[] batch in newDocs.Chunk(run.BatchSize))
            {
                if (ct.IsCancellationRequested)
                {
                    result.Canceled = true;
                    break;
                }
                try
                {
                    await backend.InsertAsync(batch);
                }
                catch (Exception ex)
                {
                    result.Fail(ex.Message);
                    logger.LogError(ex, "Refresh {ChannelId}: inserting a batch of {Count} files failed", channelId, batch.Length);
                    break;
                }
                foreach (TelegramChatDocuments tcd in batch)
                {
                    indexed.Add(tcd.id);
                    names.Add(tcd.name);
                    result.Count(tcd.documentType);
                }
                inserted += batch.Length;
                progress.AddIndexed(batch.Length);
            }
            long indexMs = sw.ElapsedMilliseconds - searchMs;

            // Advance the marks of the filters whose search finished, never
            // past a message that is not in the index now.
            Dictionary<RefreshFilter, int> newMarks = new Dictionary<RefreshFilter, int>();
            foreach (RefreshFilter f in filters)
            {
                FilterOutcome o = outcomes[f];
                if (o.State != SearchState.Completed) continue;
                int mark = ChannelRefreshPlanner.AdvanceMark(o.Floor, o.Docs.Select(d => d.id),
                    id => indexed.Contains(id) || skippedByDesign.Contains(id));
                bool hadMark = run.Marks.TryGetValue(f, out int oldMark);
                // A filter without a mark that cannot advance stays without one (same meaning: full scan).
                if (hadMark ? mark != oldMark : mark > 0)
                    newMarks[f] = mark;
            }
            if (newMarks.Count > 0)
            {
                try
                {
                    await backend.SaveMarksAsync(newMarks);
                }
                catch (Exception ex)
                {
                    // The index is fine; the next refresh just scans more than it needs to.
                    logger.LogWarning(ex, "Refresh {ChannelId}: could not store the high-water marks", channelId);
                }
            }

            logger.LogDebug("Refresh {ChannelId} timings of {Filters}: search {SearchMs} ms, index {IndexMs} ms ({New} new of {Scanned} scanned, {Inserted} inserted, canceled {Canceled})",
                channelId, string.Join(", ", filters.Select(f => f.ToKey())), searchMs, indexMs, newDocs.Count, merged.Count, inserted, result.Canceled);
        }
    }
}
