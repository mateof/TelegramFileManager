using System.Collections.Concurrent;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace Test.Library
{
    /// <summary>In-memory library storage so the scan can run without MongoDB. Thread-safe: the scan works in parallel.</summary>
    public class FakeLibraryDb : ILibraryDbService
    {
        public Dictionary<string, List<BsonFileManagerModel>> Channels { get; } = new();
        public ConcurrentDictionary<string, LibraryItem> Items { get; } = new();
        public List<LibraryEpisode> Episodes { get; } = new();
        public ConcurrentDictionary<string, LibraryFile> Files { get; } = new();
        public ConcurrentDictionary<string, WatchState> Watch { get; } = new();
        public int FileWrites;
        /// <summary>Wait of every item lookup, to make concurrent scans overlap.</summary>
        public TimeSpan LookupDelay { get; set; }
        public LibraryScanState? ScanState { get; set; }
        public ConcurrentDictionary<string, (string Json, DateTime Expires)> Cache { get; } = new();

        public BsonFileManagerModel AddChannelFile(string channelId, string name, string folder = "/", int? messageId = null)
        {
            var doc = new BsonFileManagerModel
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                Name = name,
                IsFile = true,
                Type = Path.GetExtension(name),
                FilterPath = folder,
                Size = 1000,
                MessageId = messageId ?? Random.Shared.Next(1, 100000)
            };
            if (!Channels.TryGetValue(channelId, out var list)) Channels[channelId] = list = new();
            list.Add(doc);
            return doc;
        }

        public Task EnsureIndexes() => Task.CompletedTask;
        public Task<List<string>> GetChannelDatabaseNames() => Task.FromResult(Channels.Keys.ToList());
        public Task<List<BsonFileManagerModel>> GetChannelVideos(string channelId, IEnumerable<string> extensions) =>
            Task.FromResult(Channels.TryGetValue(channelId, out var l) ? l.ToList() : new List<BsonFileManagerModel>());
        public Task<List<string>> GetChannelFolders(string channelId) =>
            Task.FromResult(Channels.TryGetValue(channelId, out var l) ? l.Select(d => d.FilterPath).Where(p => p != "/").Distinct().ToList() : new List<string>());
        public Task<BsonFileManagerModel?> GetChannelFile(string channelId, string fileId) =>
            Task.FromResult(Channels.TryGetValue(channelId, out var l) ? l.FirstOrDefault(d => d.Id == fileId) : null);

        public Task<LibraryItem?> GetItem(string id) => Task.FromResult(Items.GetValueOrDefault(id));
        public Task<List<LibraryItem>> GetItems(IEnumerable<string> ids) => Task.FromResult(ids.Distinct().Select(i => Items.GetValueOrDefault(i)).Where(i => i != null).ToList()!);
        public Task<List<LibraryItem>> GetAllItems() => Task.FromResult(Items.Values.ToList());
        public async Task<LibraryItem?> FindItemByProvider(string provider, string providerId, string? kind = null)
        {
            if (LookupDelay > TimeSpan.Zero) await Task.Delay(LookupDelay);
            return Items.Values.FirstOrDefault(i => i.Provider == provider && i.ProviderId == providerId && (kind == null || i.Kind == kind));
        }
        public Task<LibraryItem?> FindItemByExternalId(string key, string value, string? kind = null) =>
            Task.FromResult(Items.Values.FirstOrDefault(i => i.ExternalIds.TryGetValue(key, out var v) && v == value && (kind == null || i.Kind == kind)));
        public Task UpsertItem(LibraryItem item) { Items[item.Id] = item; return Task.CompletedTask; }
        public Task DeleteItem(string id) { Items.TryRemove(id, out _); lock (Episodes) Episodes.RemoveAll(e => e.ItemId == id); return Task.CompletedTask; }
        public Task UpdateItemCounts(IEnumerable<LibraryItem> items)
        {
            foreach (var i in items)
                if (Items.TryGetValue(i.Id, out var own)) (own.FileCount, own.EpisodeCount, own.ChannelIds) = (i.FileCount, i.EpisodeCount, i.ChannelIds);
            return Task.CompletedTask;
        }

        public Task<List<LibraryEpisode>> GetEpisodes(string itemId) => Task.FromResult(Locked(() => Episodes.Where(e => e.ItemId == itemId).OrderBy(e => e.Season).ThenBy(e => e.Number).ToList()));
        private T Locked<T>(Func<T> read) { lock (Episodes) return read(); }
        public Task UpsertEpisodes(IEnumerable<LibraryEpisode> episodes)
        {
            lock (Episodes)
                foreach (var e in episodes)
                {
                    Episodes.RemoveAll(x => x.ItemId == e.ItemId && x.Season == e.Season && x.Number == e.Number);
                    Episodes.Add(e);
                }
            return Task.CompletedTask;
        }
        public Task DeleteEpisodes(string itemId) { lock (Episodes) Episodes.RemoveAll(e => e.ItemId == itemId); return Task.CompletedTask; }

        public Task<LibraryFile?> GetFile(long channelId, string fileId) => Task.FromResult(Files.GetValueOrDefault(LibraryFile.KeyOf(channelId, fileId)));
        public Task<List<LibraryFile>> GetFilesByChannel(long channelId) => Task.FromResult(Files.Values.Where(f => f.ChannelId == channelId).ToList());
        public Task<List<LibraryFile>> GetFilesByItem(string itemId) => Task.FromResult(Files.Values.Where(f => f.ItemId == itemId).ToList());
        public Task<List<LibraryFile>> GetFilesByItems(IEnumerable<string> itemIds) { var set = itemIds.ToHashSet(); return Task.FromResult(Files.Values.Where(f => f.ItemId != null && set.Contains(f.ItemId)).ToList()); }
        public Task<List<LibraryFile>> GetAllFiles() => Task.FromResult(Files.Values.ToList());
        public Task<List<LibraryFile>> GetFilesByStatus(string status) => Task.FromResult(Files.Values.Where(f => f.Status == status).ToList());
        public Task<Dictionary<string, int>> CountFilesByStatus() => Task.FromResult(Files.Values.GroupBy(f => f.Status).ToDictionary(g => g.Key, g => g.Count()));
        public Task UpsertFile(LibraryFile file) { Interlocked.Increment(ref FileWrites); Files[file.Key] = file; return Task.CompletedTask; }
        public async Task UpsertFiles(IEnumerable<LibraryFile> files) { foreach (var f in files) await UpsertFile(f); }
        public async Task DeleteFiles(long channelId, IEnumerable<string> fileIds) { foreach (var id in fileIds.ToList()) await DeleteFile(channelId, id); }
        public Task DeleteFile(long channelId, string fileId) { Files.TryRemove(LibraryFile.KeyOf(channelId, fileId), out _); return Task.CompletedTask; }
        public Task<long> DeleteFilesByChannel(long channelId)
        {
            var keys = Files.Values.Where(f => f.ChannelId == channelId).Select(f => f.Key).ToList();
            foreach (var k in keys) Files.TryRemove(k, out _);
            return Task.FromResult((long)keys.Count);
        }

        public Task<WatchState?> GetWatch(long channelId, string fileId) => Task.FromResult(Watch.GetValueOrDefault(LibraryFile.KeyOf(channelId, fileId)));
        public Task<List<WatchState>> GetWatchMany(IEnumerable<string> keys) => Task.FromResult(keys.Select(k => Watch.GetValueOrDefault(k)).Where(w => w != null).ToList()!);
        public Task<List<WatchState>> GetWatchByItem(string itemId) => Task.FromResult(Watch.Values.Where(w => w.ItemId == itemId).ToList());
        public Task<List<WatchState>> GetWatchByItems(IEnumerable<string> itemIds) { var set = itemIds.ToHashSet(); return Task.FromResult(Watch.Values.Where(w => w.ItemId != null && set.Contains(w.ItemId)).ToList()); }
        public Task<List<WatchState>> GetWatchInProgress() => Task.FromResult(Watch.Values.Where(w => w.InProgress).OrderByDescending(w => w.LastPlayedAt).ToList());
        public Task<List<WatchState>> GetWatchCompleted() => Task.FromResult(Watch.Values.Where(w => w.Completed).OrderByDescending(w => w.LastPlayedAt).ToList());
        public Task UpsertWatch(WatchState state) { Watch[state.Id] = state; return Task.CompletedTask; }
        public Task DeleteWatch(long channelId, string fileId) { Watch.TryRemove(LibraryFile.KeyOf(channelId, fileId), out _); return Task.CompletedTask; }

        public Task<LibraryScanState?> GetScanState() => Task.FromResult(ScanState);
        public Task SaveScanState(LibraryScanState state) { ScanState = state.Clone(); return Task.CompletedTask; }

        public Task<string?> GetCached(string key) =>
            Task.FromResult(Cache.TryGetValue(key, out var e) && e.Expires > DateTime.UtcNow ? e.Json : null);
        public Task SetCached(string key, string json, TimeSpan ttl) { Cache[key] = (json, DateTime.UtcNow + ttl); return Task.CompletedTask; }
    }

    /// <summary>A provider with a hand-written catalogue and call counters.</summary>
    public class FakeProvider : IMetadataProvider
    {
        public string Id { get; }
        public ProviderDescriptor Descriptor { get; }
        public List<ProviderCandidate> Catalogue { get; } = new();
        public Dictionary<string, List<LibraryEpisode>> Seasons { get; } = new();
        public int SearchCalls;
        public int ItemCalls;
        public int SeasonCalls;
        public int EnrichCalls;
        /// <summary>Latency of every call.</summary>
        public TimeSpan Delay { get; set; }
        /// <summary>Makes <see cref="EnrichAsync"/> fail.</summary>
        public Exception? ThrowOnEnrich { get; set; }
        /// <summary>Makes the searches fail once this many have been answered.</summary>
        public int? SearchesBeforeThrow { get; set; }
        public double? EnrichRating { get; set; }
        public Exception? Throw { get; set; }

        public FakeProvider(string id = "fake")
        {
            Id = id;
            Descriptor = new ProviderDescriptor { Id = id, Name = id, SupportsEpisodes = true };
        }

        public ProviderCandidate Add(string kind, string id, string title, int? year, string? imdb = null, string? original = null)
        {
            var c = new ProviderCandidate { Provider = Id, ProviderId = id, Kind = kind, Title = title, OriginalTitle = original, Year = year, ImdbId = imdb, Popularity = 10 };
            Catalogue.Add(c);
            return c;
        }

        public async Task<IReadOnlyList<ProviderCandidate>> SearchAsync(string kind, string query, int? year, CancellationToken ct)
        {
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            var calls = Interlocked.Increment(ref SearchCalls);
            if (Throw != null && calls > (SearchesBeforeThrow ?? 0)) throw Throw;
            var q = MediaNameParser.NormalizeForMatch(query);
            var hits = Catalogue.Where(c => c.Kind == kind && MediaNameParser.NormalizeForMatch(c.Title).Contains(q.Split(' ')[0]))
                .Where(c => year == null || c.Year == null || Math.Abs(c.Year.Value - year.Value) <= 1)
                .ToList();
            return hits;
        }

        public async Task<LibraryItem?> GetItemAsync(string kind, string providerId, CancellationToken ct)
        {
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            Interlocked.Increment(ref ItemCalls);
            var c = Catalogue.FirstOrDefault(x => x.ProviderId == providerId && x.Kind == kind);
            if (c == null) return null;
            var item = new LibraryItem
            {
                Kind = kind, Provider = Id, ProviderId = providerId, Title = c.Title, OriginalTitle = c.OriginalTitle, Year = c.Year,
                Overview = "overview", PosterUrl = "https://image.tmdb.org/t/p/original/" + providerId + ".jpg"
            };
            if (c.ImdbId != null) item.ExternalIds["imdb"] = c.ImdbId;
            if (kind == LibraryKind.Series)
                foreach (var s in Seasons.Where(s => s.Key.StartsWith(providerId + ":")))
                    item.Seasons.Add(new LibrarySeason { Number = int.Parse(s.Key.Split(':')[1]), EpisodeCount = s.Value.Count });
            return item;
        }

        public async Task<IReadOnlyList<LibraryEpisode>> GetSeasonAsync(string providerId, int season, CancellationToken ct)
        {
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            Interlocked.Increment(ref SeasonCalls);
            var list = Seasons.TryGetValue($"{providerId}:{season}", out var eps)
                ? eps.Select(e => new LibraryEpisode { Season = e.Season, Number = e.Number, Title = e.Title }).ToList()
                : new List<LibraryEpisode>();
            return list;
        }

        public Task<ProviderCandidate?> FindByImdbAsync(string imdbId, string? kind, CancellationToken ct) =>
            Task.FromResult(Catalogue.FirstOrDefault(c => c.ImdbId == imdbId && (kind == null || c.Kind == kind)));

        public Task EnrichAsync(LibraryItem item, CancellationToken ct)
        {
            Interlocked.Increment(ref EnrichCalls);
            if (ThrowOnEnrich != null) throw ThrowOnEnrich;
            if (EnrichRating.HasValue) item.Rating ??= EnrichRating;
            return Task.CompletedTask;
        }
    }

    public class FakeProviderSource : IMetadataProviderSource
    {
        public List<IMetadataProvider> Providers { get; } = new();
        public IReadOnlyList<IMetadataProvider> GetEnabled() => Providers;
        public IMetadataProvider? Get(string id) => Providers.FirstOrDefault(p => p.Id == id);
    }
}
