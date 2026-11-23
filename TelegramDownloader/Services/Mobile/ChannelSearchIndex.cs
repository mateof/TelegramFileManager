using System.Collections.Concurrent;
using MongoDB.Bson;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Mobile;

namespace TelegramDownloader.Services.Mobile
{
    /// <summary>
    /// Matches of one channel: ids in name order (case-insensitive) and whether
    /// more matches existed beyond the requested limit.
    /// </summary>
    public sealed record ChannelSearchMatch(IReadOnlyList<string> Ids, bool HasMore)
    {
        public static readonly ChannelSearchMatch Empty = new(Array.Empty<string>(), false);
    }

    /// <summary>
    /// In-memory index of file names per channel database, used by the mobile
    /// global search to do substring ("contains") matching without scanning
    /// Mongo on every request.
    ///
    /// Each channel is loaded lazily on its first search with a projection of
    /// id, name and type of files only, and kept as a compact array sorted by
    /// name. An entry is dropped when the channel is refreshed
    /// (<see cref="FileService.ChannelRefreshed"/>) and expires after a TTL so
    /// uploads and deletes made elsewhere show up. Only one load per channel
    /// runs at a time; concurrent searches share it.
    /// </summary>
    public sealed class ChannelSearchIndex : IDisposable
    {
        public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

        /// <summary>One indexed file: 12-byte ObjectId, lowercased name and audio flag.</summary>
        private readonly struct Entry
        {
            public readonly ObjectId Id;
            public readonly string LowerName;
            public readonly bool IsAudio;

            public Entry(ObjectId id, string lowerName, bool isAudio)
            {
                Id = id;
                LowerName = lowerName;
                IsAudio = isAudio;
            }
        }

        private sealed class Snapshot
        {
            public required Entry[] Entries { get; init; }
            public required DateTimeOffset BuiltAt { get; init; }
        }

        private readonly Func<string, CancellationToken, Task<List<ChannelFileNameEntry>>> _loader;
        private readonly TimeProvider _clock;
        private readonly TimeSpan _ttl;
        private readonly ILogger? _logger;
        private readonly ConcurrentDictionary<string, Lazy<Task<Snapshot>>> _entries = new(StringComparer.Ordinal);
        private readonly ITimer? _sweeper;
        private int _disposed;

        public ChannelSearchIndex(IDbService db, ILogger<ChannelSearchIndex> logger)
            : this((dbName, ct) => db.GetFileNameEntries(dbName, ct), TimeProvider.System, DefaultTtl, logger, subscribeToRefresh: true)
        {
        }

        /// <param name="loader">Loads the file entries of a channel database.</param>
        /// <param name="clock">Time source for the TTL.</param>
        /// <param name="ttl">How long a loaded channel stays valid.</param>
        /// <param name="logger">Optional logger.</param>
        /// <param name="subscribeToRefresh">Hook <see cref="FileService.ChannelRefreshed"/> and run a periodic sweep of expired channels.</param>
        public ChannelSearchIndex(
            Func<string, CancellationToken, Task<List<ChannelFileNameEntry>>> loader,
            TimeProvider clock,
            TimeSpan ttl,
            ILogger? logger = null,
            bool subscribeToRefresh = false)
        {
            if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _clock = clock ?? TimeProvider.System;
            _ttl = ttl;
            _logger = logger;

            if (subscribeToRefresh)
            {
                FileService.ChannelRefreshed += OnChannelRefreshed;
                // Frees memory of channels nobody searched again after the TTL.
                _sweeper = _clock.CreateTimer(_ => EvictExpired(), null, ttl, ttl);
            }
        }

        /// <summary>Number of channels currently held (loaded or loading).</summary>
        public int CachedChannelCount => _entries.Count;

        /// <summary>
        /// Files of <paramref name="dbName"/> whose name contains <paramref name="query"/>
        /// (case-insensitive, '*' ignored), in name order, at most <paramref name="limit"/>.
        /// With <paramref name="audioOnly"/>, non-audio files are skipped before counting.
        /// </summary>
        public async Task<ChannelSearchMatch> SearchAsync(
            string dbName, string query, bool audioOnly, int limit, CancellationToken cancellationToken = default)
        {
            if (limit < 1) return ChannelSearchMatch.Empty;
            var snapshot = await GetSnapshot(dbName, cancellationToken);
            return Match(snapshot.Entries, NormalizeQuery(query), audioOnly, limit, cancellationToken);
        }

        /// <summary>Drops the cached names of a channel; the next search reloads them.</summary>
        public void Invalidate(string dbName)
        {
            if (dbName != null && _entries.TryRemove(dbName, out _))
                _logger?.LogDebug("Search index invalidated for channel {Db}", dbName);
        }

        /// <summary>Drops every channel whose names were loaded longer than the TTL ago.</summary>
        public void EvictExpired()
        {
            var now = _clock.GetUtcNow();
            foreach (var kv in _entries)
            {
                var task = kv.Value.IsValueCreated ? kv.Value.Value : null;
                if (task == null || !task.IsCompleted) continue;
                if (!task.IsCompletedSuccessfully || IsExpired(task.Result, now))
                    _entries.TryRemove(kv);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            FileService.ChannelRefreshed -= OnChannelRefreshed;
            _sweeper?.Dispose();
            _entries.Clear();
        }

        private void OnChannelRefreshed(string channelId) => Invalidate(channelId);

        internal static string NormalizeQuery(string query) =>
            (query ?? string.Empty).Replace("*", string.Empty).ToLowerInvariant();

        private bool IsExpired(Snapshot snapshot, DateTimeOffset now) => now - snapshot.BuiltAt >= _ttl;

        private async Task<Snapshot> GetSnapshot(string dbName, CancellationToken ct)
        {
            // Two rounds at most: an expired snapshot is replaced once by a fresh one.
            for (var attempt = 0; ; attempt++)
            {
                var lazy = _entries.GetOrAdd(dbName,
                    key => new Lazy<Task<Snapshot>>(() => Build(key), LazyThreadSafetyMode.ExecutionAndPublication));

                Snapshot snapshot;
                try
                {
                    // The load is shared, so it never takes a caller's token; a caller
                    // that gives up only stops waiting.
                    snapshot = await lazy.Value.WaitAsync(ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Failed load: forget it so the next search retries.
                    _entries.TryRemove(new KeyValuePair<string, Lazy<Task<Snapshot>>>(dbName, lazy));
                    throw;
                }

                if (attempt == 0 && IsExpired(snapshot, _clock.GetUtcNow()))
                {
                    _entries.TryRemove(new KeyValuePair<string, Lazy<Task<Snapshot>>>(dbName, lazy));
                    continue;
                }
                return snapshot;
            }
        }

        private async Task<Snapshot> Build(string dbName)
        {
            var builtAt = _clock.GetUtcNow();
            var raw = await _loader(dbName, CancellationToken.None).ConfigureAwait(false);

            var entries = new List<Entry>(raw.Count);
            foreach (var f in raw)
            {
                if (f?.Name == null || !ObjectId.TryParse(f.Id, out var id)) continue;
                entries.Add(new Entry(id, f.Name.ToLowerInvariant(), ChannelFileDto.IsAudioFile(f.Type)));
            }
            raw.Clear();

            // Same order the search returns (name, case-insensitive), so the first
            // `limit` matches of a scan are the first `limit` results.
            entries.Sort(static (a, b) =>
            {
                var c = StringComparer.OrdinalIgnoreCase.Compare(a.LowerName, b.LowerName);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });

            _logger?.LogDebug("Search index loaded {Count} files for channel {Db}", entries.Count, dbName);
            return new Snapshot { Entries = entries.ToArray(), BuiltAt = builtAt };
        }

        private static ChannelSearchMatch Match(Entry[] entries, string needle, bool audioOnly, int limit, CancellationToken ct)
        {
            var ids = new List<string>(Math.Min(limit, 64));
            var hasMore = false;
            for (var i = 0; i < entries.Length; i++)
            {
                if ((i & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();

                ref readonly var e = ref entries[i];
                if (audioOnly && !e.IsAudio) continue;
                if (needle.Length > 0 && !e.LowerName.Contains(needle, StringComparison.Ordinal)) continue;

                if (ids.Count >= limit)
                {
                    hasMore = true;
                    break;
                }
                ids.Add(e.Id.ToString());
            }
            return ids.Count == 0 ? ChannelSearchMatch.Empty : new ChannelSearchMatch(ids, hasMore);
        }
    }
}
