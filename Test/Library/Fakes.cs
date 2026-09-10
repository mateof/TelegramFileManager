using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace Test.Library
{
    /// <summary>In-memory library storage so the scan can run without MongoDB.</summary>
    public class FakeLibraryDb : ILibraryDbService
    {
        public Dictionary<string, List<BsonFileManagerModel>> Channels { get; } = new();
        public Dictionary<string, LibraryItem> Items { get; } = new();
        public List<LibraryEpisode> Episodes { get; } = new();
        public Dictionary<string, LibraryFile> Files { get; } = new();
        public Dictionary<string, WatchState> Watch { get; } = new();
        public LibraryScanState? ScanState { get; set; }
        public Dictionary<string, (string Json, DateTime Expires)> Cache { get; } = new();

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
        public Task<List<BsonFileManagerModel>> GetChannelFiles(string channelId) =>
            Task.FromResult(Channels.TryGetValue(channelId, out var l) ? l.ToList() : new List<BsonFileManagerModel>());
        public Task<List<string>> GetChannelFolders(string channelId) =>
            Task.FromResult(Channels.TryGetValue(channelId, out var l) ? l.Select(d => d.FilterPath).Where(p => p != "/").Distinct().ToList() : new List<string>());
        public Task<BsonFileManagerModel?> GetChannelFile(string channelId, string fileId) =>
            Task.FromResult(Channels.TryGetValue(channelId, out var l) ? l.FirstOrDefault(d => d.Id == fileId) : null);

        public Task<LibraryItem?> GetItem(string id) => Task.FromResult(Items.GetValueOrDefault(id));
        public Task<List<LibraryItem>> GetItems(IEnumerable<string> ids) => Task.FromResult(ids.Distinct().Select(i => Items.GetValueOrDefault(i)).Where(i => i != null).ToList()!);
        public Task<List<LibraryItem>> GetAllItems() => Task.FromResult(Items.Values.ToList());
        public Task<LibraryItem?> FindItemByProvider(string provider, string providerId) =>
            Task.FromResult(Items.Values.FirstOrDefault(i => i.Provider == provider && i.ProviderId == providerId));
        public Task<LibraryItem?> FindItemByExternalId(string key, string value) =>
            Task.FromResult(Items.Values.FirstOrDefault(i => i.ExternalIds.TryGetValue(key, out var v) && v == value));
        public Task UpsertItem(LibraryItem item) { Items[item.Id] = item; return Task.CompletedTask; }
        public Task DeleteItem(string id) { Items.Remove(id); Episodes.RemoveAll(e => e.ItemId == id); return Task.CompletedTask; }

        public Task<List<LibraryEpisode>> GetEpisodes(string itemId) => Task.FromResult(Episodes.Where(e => e.ItemId == itemId).OrderBy(e => e.Season).ThenBy(e => e.Number).ToList());
        public Task UpsertEpisodes(IEnumerable<LibraryEpisode> episodes)
        {
            foreach (var e in episodes)
            {
                Episodes.RemoveAll(x => x.ItemId == e.ItemId && x.Season == e.Season && x.Number == e.Number);
                Episodes.Add(e);
            }
            return Task.CompletedTask;
        }
        public Task DeleteEpisodes(string itemId) { Episodes.RemoveAll(e => e.ItemId == itemId); return Task.CompletedTask; }

        public Task<LibraryFile?> GetFile(long channelId, string fileId) => Task.FromResult(Files.GetValueOrDefault(LibraryFile.KeyOf(channelId, fileId)));
        public Task<List<LibraryFile>> GetFilesByChannel(long channelId) => Task.FromResult(Files.Values.Where(f => f.ChannelId == channelId).ToList());
        public Task<List<LibraryFile>> GetFilesByItem(string itemId) => Task.FromResult(Files.Values.Where(f => f.ItemId == itemId).ToList());
        public Task<List<LibraryFile>> GetFilesByItems(IEnumerable<string> itemIds) { var set = itemIds.ToHashSet(); return Task.FromResult(Files.Values.Where(f => f.ItemId != null && set.Contains(f.ItemId)).ToList()); }
        public Task<List<LibraryFile>> GetAllFiles() => Task.FromResult(Files.Values.ToList());
        public Task<List<LibraryFile>> GetFilesByStatus(string status) => Task.FromResult(Files.Values.Where(f => f.Status == status).ToList());
        public Task<Dictionary<string, int>> CountFilesByStatus() => Task.FromResult(Files.Values.GroupBy(f => f.Status).ToDictionary(g => g.Key, g => g.Count()));
        public Task UpsertFile(LibraryFile file) { Files[file.Key] = file; return Task.CompletedTask; }
        public Task DeleteFile(long channelId, string fileId) { Files.Remove(LibraryFile.KeyOf(channelId, fileId)); return Task.CompletedTask; }
        public Task<long> DeleteFilesByChannel(long channelId)
        {
            var keys = Files.Values.Where(f => f.ChannelId == channelId).Select(f => f.Key).ToList();
            foreach (var k in keys) Files.Remove(k);
            return Task.FromResult((long)keys.Count);
        }

        public Task<WatchState?> GetWatch(long channelId, string fileId) => Task.FromResult(Watch.GetValueOrDefault(LibraryFile.KeyOf(channelId, fileId)));
        public Task<List<WatchState>> GetWatchMany(IEnumerable<string> keys) => Task.FromResult(keys.Select(k => Watch.GetValueOrDefault(k)).Where(w => w != null).ToList()!);
        public Task<List<WatchState>> GetWatchByItem(string itemId) => Task.FromResult(Watch.Values.Where(w => w.ItemId == itemId).ToList());
        public Task<List<WatchState>> GetWatchByItems(IEnumerable<string> itemIds) { var set = itemIds.ToHashSet(); return Task.FromResult(Watch.Values.Where(w => w.ItemId != null && set.Contains(w.ItemId)).ToList()); }
        public Task<List<WatchState>> GetWatchInProgress() => Task.FromResult(Watch.Values.Where(w => w.InProgress).OrderByDescending(w => w.LastPlayedAt).ToList());
        public Task<List<WatchState>> GetWatchCompleted() => Task.FromResult(Watch.Values.Where(w => w.Completed).OrderByDescending(w => w.LastPlayedAt).ToList());
        public Task UpsertWatch(WatchState state) { Watch[state.Id] = state; return Task.CompletedTask; }
        public Task DeleteWatch(long channelId, string fileId) { Watch.Remove(LibraryFile.KeyOf(channelId, fileId)); return Task.CompletedTask; }

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
        public int SearchCalls { get; private set; }
        public int ItemCalls { get; private set; }
        public int SeasonCalls { get; private set; }
        public int EnrichCalls { get; private set; }
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

        public Task<IReadOnlyList<ProviderCandidate>> SearchAsync(string kind, string query, int? year, CancellationToken ct)
        {
            if (Throw != null) throw Throw;
            SearchCalls++;
            var q = MediaNameParser.NormalizeForMatch(query);
            var hits = Catalogue.Where(c => c.Kind == kind && MediaNameParser.NormalizeForMatch(c.Title).Contains(q.Split(' ')[0]))
                .Where(c => year == null || c.Year == null || Math.Abs(c.Year.Value - year.Value) <= 1)
                .ToList();
            return Task.FromResult<IReadOnlyList<ProviderCandidate>>(hits);
        }

        public Task<LibraryItem?> GetItemAsync(string kind, string providerId, CancellationToken ct)
        {
            ItemCalls++;
            var c = Catalogue.FirstOrDefault(x => x.ProviderId == providerId && x.Kind == kind);
            if (c == null) return Task.FromResult<LibraryItem?>(null);
            var item = new LibraryItem
            {
                Kind = kind, Provider = Id, ProviderId = providerId, Title = c.Title, OriginalTitle = c.OriginalTitle, Year = c.Year,
                Overview = "overview", PosterUrl = "https://image.tmdb.org/t/p/original/" + providerId + ".jpg"
            };
            if (c.ImdbId != null) item.ExternalIds["imdb"] = c.ImdbId;
            if (kind == LibraryKind.Series)
                foreach (var s in Seasons.Where(s => s.Key.StartsWith(providerId + ":")))
                    item.Seasons.Add(new LibrarySeason { Number = int.Parse(s.Key.Split(':')[1]), EpisodeCount = s.Value.Count });
            return Task.FromResult<LibraryItem?>(item);
        }

        public Task<IReadOnlyList<LibraryEpisode>> GetSeasonAsync(string providerId, int season, CancellationToken ct)
        {
            SeasonCalls++;
            var list = Seasons.TryGetValue($"{providerId}:{season}", out var eps)
                ? eps.Select(e => new LibraryEpisode { Season = e.Season, Number = e.Number, Title = e.Title }).ToList()
                : new List<LibraryEpisode>();
            return Task.FromResult<IReadOnlyList<LibraryEpisode>>(list);
        }

        public Task<ProviderCandidate?> FindByImdbAsync(string imdbId, string? kind, CancellationToken ct) =>
            Task.FromResult(Catalogue.FirstOrDefault(c => c.ImdbId == imdbId && (kind == null || c.Kind == kind)));

        public Task EnrichAsync(LibraryItem item, CancellationToken ct)
        {
            EnrichCalls++;
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
