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
    /// the index is now known to be complete. Never throws: failures and
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

        public static async Task<ChannelRefreshResult> RunAsync(
            string channelId,
            IChannelRefreshBackend backend,
            IReadOnlyList<RefreshFilter> filters,
            bool force,
            ChannelRefreshProgress progress,
            ILogger logger,
            int batchSize = ChannelRefreshPlanner.InsertBatchSize)
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
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                // Load what is already indexed once, in parallel with the search.
                Task<IReadOnlyCollection<int>> indexedTask = backend.LoadIndexedIdsAsync();
                Task<IReadOnlyCollection<string>> namesTask = backend.LoadFileNamesAsync();
                IReadOnlyDictionary<RefreshFilter, int> marks = await backend.LoadMarksAsync();

                List<RefreshFilter> fullScans = ChannelRefreshPlanner.FiltersNeedingFullScan(filters, marks, force);
                if (fullScans.Count > 0)
                    logger.LogInformation("Refresh {ChannelId}: full scan of {Filters} (forced or no high-water mark yet)",
                        channelId, string.Join(", ", fullScans.Select(f => f.ToKey())));

                // Each filter is an independent paged search, so they run concurrently.
                Dictionary<RefreshFilter, FilterOutcome> outcomes = filters.ToDictionary(f => f, f => new FilterOutcome
                {
                    Floor = ChannelRefreshPlanner.ScanFloor(f, marks, force)
                });
                await Task.WhenAll(filters.Select(async f =>
                {
                    FilterOutcome outcome = outcomes[f];
                    RefreshFilterProgress fp = progress.Filter(f);
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
                        logger.LogError(ex, "Refresh {ChannelId}: search of {Filter} failed", channelId, f.ToKey());
                    }
                    finally
                    {
                        fp.MarkDone();
                    }
                }));
                long searchMs = sw.ElapsedMilliseconds;

                HashSet<int> indexed = (await indexedTask).ToHashSet();

                if (ct.IsCancellationRequested)
                {
                    // Nothing was written yet, so the index is exactly as before.
                    result.Canceled = true;
                    logger.LogInformation("Refresh {ChannelId} canceled while searching Telegram; nothing indexed", channelId);
                    return result;
                }
                if (outcomes.Values.Any(o => o.State == SearchState.Failed))
                    result.Failed = true;

                // Merge in filter order (earlier filter wins a duplicate id). A
                // filter that failed contributes nothing and keeps its mark.
                List<TelegramChatDocuments> merged = ChannelRefreshPlanner.MergePhaseResults(
                    filters.Select(f => outcomes[f].State == SearchState.Completed ? outcomes[f].Docs : Array.Empty<TelegramChatDocuments>()));
                // Nameless entries are never indexed, by design; they must not hold a mark back forever.
                HashSet<int> skippedByDesign = merged.Where(d => d.name == null).Select(d => d.id).ToHashSet();
                merged = merged.Where(d => d.name != null).ToList();

                ChannelRefreshPlanner.AssignUniqueNames(await namesTask, merged);
                List<TelegramChatDocuments> newDocs = ChannelRefreshPlanner.SelectNew(merged, indexed);
                progress.NewFiles = newDocs.Count;
                progress.Phase = RefreshPhase.Indexing;

                // One InsertMany per batch; counted per batch so a cancel or a
                // failure reports exactly what got in.
                foreach (TelegramChatDocuments[] batch in newDocs.Chunk(batchSize))
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
                        result.Failed = true;
                        logger.LogError(ex, "Refresh {ChannelId}: inserting a batch of {Count} files failed", channelId, batch.Length);
                        break;
                    }
                    foreach (TelegramChatDocuments tcd in batch)
                    {
                        indexed.Add(tcd.id);
                        result.Count(tcd.documentType);
                    }
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
                    bool hadMark = marks.TryGetValue(f, out int oldMark);
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

                logger.LogDebug("Refresh {ChannelId} timings: search {SearchMs} ms, index {IndexMs} ms ({New} new of {Scanned} scanned, {Inserted} inserted, canceled {Canceled})",
                    channelId, searchMs, indexMs, newDocs.Count, merged.Count, result.Total, result.Canceled);
            }
            catch (Exception ex)
            {
                result.Failed = true;
                logger.LogError(ex, "Refresh of channel {ChannelId} failed", channelId);
            }
            finally
            {
                progress.Phase = RefreshPhase.Done;
                result.FinishedAt = DateTime.Now;
            }
            return result;
        }
    }
}
