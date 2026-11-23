namespace TelegramDownloader.Models
{
    /// <summary>One of the Telegram search filters a channel refresh runs.</summary>
    /// <remarks>The order is the merge order: a message found by two filters belongs to the earlier one.</remarks>
    public enum RefreshFilter
    {
        Documents,
        Audio,
        Video,
        Photos
    }

    /// <summary>Where a running refresh is.</summary>
    public enum RefreshPhase
    {
        /// <summary>Paging through Telegram's search results.</summary>
        Searching,
        /// <summary>Writing the new files to the channel index.</summary>
        Indexing,
        /// <summary>Finished (or canceled); about to be released.</summary>
        Done
    }

    public static class RefreshFilterNames
    {
        /// <summary>The stable lowercase name used in the API and as the high-water mark key.</summary>
        public static string ToKey(this RefreshFilter filter) => filter switch
        {
            RefreshFilter.Documents => "documents",
            RefreshFilter.Audio => "audio",
            RefreshFilter.Video => "video",
            RefreshFilter.Photos => "photos",
            _ => filter.ToString().ToLowerInvariant()
        };

        public static bool TryParse(string? key, out RefreshFilter filter)
        {
            foreach (RefreshFilter f in Enum.GetValues<RefreshFilter>())
            {
                if (string.Equals(f.ToKey(), key, StringComparison.OrdinalIgnoreCase))
                {
                    filter = f;
                    return true;
                }
            }
            filter = default;
            return false;
        }

        public static string ToKey(this RefreshPhase phase) => phase switch
        {
            RefreshPhase.Searching => "searching",
            RefreshPhase.Indexing => "indexing",
            _ => "done"
        };

        /// <summary>The selected filters, in merge order.</summary>
        public static List<RefreshFilter> SelectedFilters(this RefreshChannelOptions options)
        {
            List<RefreshFilter> filters = new List<RefreshFilter>();
            if (options.IncludeDocuments) filters.Add(RefreshFilter.Documents);
            if (options.IncludeAudio) filters.Add(RefreshFilter.Audio);
            if (options.IncludeVideo) filters.Add(RefreshFilter.Video);
            if (options.IncludePhotos) filters.Add(RefreshFilter.Photos);
            return filters;
        }
    }

    /// <summary>Live counters of one filter's Telegram search. Written by one search, read by any poller.</summary>
    public sealed class RefreshFilterProgress
    {
        private int _fetched;
        private int _total = -1;
        private int _done;

        public RefreshFilterProgress(RefreshFilter filter) => Filter = filter;

        public RefreshFilter Filter { get; }

        /// <summary>Messages read from Telegram so far.</summary>
        public int Fetched => Volatile.Read(ref _fetched);

        /// <summary>
        /// How many messages match the filter in the whole channel, as Telegram
        /// reports it; null until the first page arrives. An incremental scan
        /// stops at the high-water mark, so it usually ends well below this.
        /// </summary>
        public int? Total
        {
            get
            {
                int t = Volatile.Read(ref _total);
                return t < 0 ? null : t;
            }
        }

        /// <summary>The search of this filter is over (complete, canceled or failed).</summary>
        public bool Done => Volatile.Read(ref _done) == 1;

        /// <summary>Records one page: <paramref name="fetchedInPage"/> messages and the reported total.</summary>
        public void ReportPage(int fetchedInPage, int? total)
        {
            if (total.HasValue)
                Interlocked.CompareExchange(ref _total, total.Value, -1);
            Interlocked.Add(ref _fetched, fetchedInPage);
        }

        public void MarkDone() => Volatile.Write(ref _done, 1);
    }

    /// <summary>
    /// The live state of one running channel refresh, plus the token that
    /// cancels it. Updated by the refresh from several threads (the filters
    /// search concurrently) and read by pollers; every member is thread-safe.
    /// </summary>
    public sealed class ChannelRefreshProgress
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _filtersLock = new object();
        private readonly List<RefreshFilterProgress> _filters = new List<RefreshFilterProgress>();
        private int _phase = (int)RefreshPhase.Searching;
        private int _newFiles;
        private int _indexed;

        public ChannelRefreshProgress(string channelId, DateTime? startedAt = null)
        {
            ChannelId = channelId;
            StartedAt = startedAt ?? DateTime.Now;
        }

        public string ChannelId { get; }
        public DateTime StartedAt { get; }

        /// <summary>Canceled by <see cref="Cancel"/>; the refresh checks it between pages and between insert batches.</summary>
        public CancellationToken Token => _cts.Token;

        public bool CancelRequested => _cts.IsCancellationRequested;

        public RefreshPhase Phase
        {
            get => (RefreshPhase)Volatile.Read(ref _phase);
            set => Volatile.Write(ref _phase, (int)value);
        }

        /// <summary>Files found that were not indexed yet (known once the search is over).</summary>
        public int NewFiles
        {
            get => Volatile.Read(ref _newFiles);
            set => Volatile.Write(ref _newFiles, value);
        }

        /// <summary>New files written to the index so far.</summary>
        public int Indexed => Volatile.Read(ref _indexed);

        public void AddIndexed(int count) => Interlocked.Add(ref _indexed, count);

        /// <summary>Registers (or returns) the counters of a filter.</summary>
        public RefreshFilterProgress Filter(RefreshFilter filter)
        {
            lock (_filtersLock)
            {
                RefreshFilterProgress? existing = _filters.FirstOrDefault(f => f.Filter == filter);
                if (existing != null) return existing;
                RefreshFilterProgress created = new RefreshFilterProgress(filter);
                _filters.Add(created);
                return created;
            }
        }

        /// <summary>Requests cancellation. Safe to call any number of times, from any thread.</summary>
        public void Cancel()
        {
            try { _cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        /// <summary>A consistent-enough copy for a poller (each counter is read atomically).</summary>
        public ChannelRefreshProgressSnapshot Snapshot()
        {
            List<RefreshFilterProgressSnapshot> filters;
            lock (_filtersLock)
            {
                filters = _filters
                    .Select(f => new RefreshFilterProgressSnapshot(f.Filter, f.Fetched, f.Total, f.Done))
                    .ToList();
            }
            return new ChannelRefreshProgressSnapshot(ChannelId, Phase, StartedAt, filters, NewFiles, Indexed, CancelRequested);
        }
    }

    public sealed record RefreshFilterProgressSnapshot(RefreshFilter Filter, int Fetched, int? Total, bool Done);

    public sealed record ChannelRefreshProgressSnapshot(
        string ChannelId,
        RefreshPhase Phase,
        DateTime StartedAt,
        IReadOnlyList<RefreshFilterProgressSnapshot> Filters,
        int NewFiles,
        int Indexed,
        bool CancelRequested);
}
