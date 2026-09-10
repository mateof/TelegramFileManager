using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Services.Library
{
    /// <summary>
    /// Read side of the library (catalogue, detail, continue watching) plus the
    /// user actions: playback progress and manual identification.
    /// </summary>
    public class LibraryService
    {
        private readonly ILibraryDbService _lib;
        private readonly LibraryScanService _scan;
        private readonly IMetadataProviderSource _providers;
        private readonly ILogger<LibraryService> _logger;

        public LibraryService(ILibraryDbService lib, LibraryScanService scan, IMetadataProviderSource providers, ILogger<LibraryService> logger)
        {
            _lib = lib;
            _scan = scan;
            _providers = providers;
            _logger = logger;
        }

        private static GeneralConfig Config => GeneralConfigStatic.config;

        #region Catalogue

        public async Task<List<ApiLibraryItemDto>> ListItemsAsync(string? kind, string? search, string? genre, string? status,
            bool includeHidden, string? sortBy, bool sortDescending, string baseUrl)
        {
            var items = await _lib.GetAllItems();
            if (LibraryKind.IsValid(kind)) items = items.Where(i => i.Kind == kind).ToList();
            if (!includeHidden && !Config.ShowHiddenChannels) items = items.Where(i => !IsHidden(i)).ToList();
            if (!string.IsNullOrWhiteSpace(genre))
                items = items.Where(i => i.Genres.Any(g => g.Equals(genre, StringComparison.OrdinalIgnoreCase))).ToList();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = MediaNameParser.NormalizeForMatch(search);
                items = items.Where(i =>
                    MediaNameParser.NormalizeForMatch(i.Title).Contains(q) ||
                    (i.OriginalTitle != null && MediaNameParser.NormalizeForMatch(i.OriginalTitle).Contains(q))).ToList();
            }
            if (status == LibraryFileStatus.Review)
            {
                var reviewItems = (await _lib.GetFilesByStatus(LibraryFileStatus.Review)).Select(f => f.ItemId).ToHashSet();
                items = items.Where(i => reviewItems.Contains(i.Id)).ToList();
            }

            var watches = (await _lib.GetWatchByItems(items.Select(i => i.Id))).GroupBy(w => w.ItemId!).ToDictionary(g => g.Key, g => g.ToList());
            var dtos = items.Select(i => ApiLibraryItemDto.From(i, baseUrl, Summarize(i, watches.GetValueOrDefault(i.Id) ?? new()))).ToList();

            IEnumerable<ApiLibraryItemDto> sorted = (sortBy ?? "title").ToLowerInvariant() switch
            {
                "year" => dtos.OrderBy(d => d.Year ?? 0).ThenBy(d => d.Title),
                "added" => dtos.OrderBy(d => d.AddedAt),
                "rating" => dtos.OrderBy(d => d.Rating ?? 0).ThenBy(d => d.Title),
                "lastplayed" => dtos.OrderBy(d => d.Watch.LastPlayedAt ?? DateTime.MinValue),
                _ => dtos.OrderBy(d => MediaNameParser.SortTitle(d.Title)).ThenBy(d => d.Year)
            };
            return (sortDescending ? sorted.Reverse() : sorted).ToList();
        }

        public async Task<ApiLibraryItemDetailDto?> GetItemDetailAsync(string id, string baseUrl)
        {
            var item = await _lib.GetItem(id);
            if (item == null) return null;
            var files = (await _lib.GetFilesByItem(id)).Where(f => f.Status != LibraryFileStatus.Ignored).ToList();
            var watches = await _lib.GetWatchByItem(id);
            var watchByKey = watches.ToDictionary(w => w.Id);
            var summary = Summarize(item, watches);
            var dto = ApiLibraryItemDetailDto.From(item, baseUrl, summary);

            if (item.Kind == LibraryKind.Movie)
            {
                dto.Files = files.OrderByDescending(f => f.Size)
                    .Select(f => ApiLibraryFileDto.From(f, baseUrl, watchByKey.GetValueOrDefault(f.Key))).ToList();
                dto.Resume = dto.Files.Where(f => f.Watch is { Completed: false, PositionMs: > 0 })
                    .OrderByDescending(f => f.Watch!.LastPlayedAt).FirstOrDefault();
                return dto;
            }

            var episodesMeta = (await _lib.GetEpisodes(id)).ToDictionary(e => (e.Season, e.Number));
            var bySeason = files.GroupBy(f => f.Season ?? 1).OrderBy(g => g.Key == 0 ? int.MaxValue : g.Key);
            var allEpisodes = new List<ApiLibraryEpisodeDto>();
            foreach (var seasonFiles in bySeason)
            {
                var meta = item.Seasons.FirstOrDefault(s => s.Number == seasonFiles.Key);
                var seasonDto = new ApiLibrarySeasonDto
                {
                    Number = seasonFiles.Key,
                    Name = meta?.Name ?? (seasonFiles.Key == 0 ? "Especiales" : $"Temporada {seasonFiles.Key}"),
                    Overview = meta?.Overview,
                    PosterUrl = LibraryImageService.ProxyUrl(baseUrl, meta?.PosterUrl ?? item.PosterUrl, "w342"),
                    EpisodeCount = meta?.EpisodeCount
                };
                foreach (var group in seasonFiles.GroupBy(f => f.Episode ?? 0).OrderBy(g => g.Key))
                {
                    episodesMeta.TryGetValue((seasonFiles.Key, group.Key), out var em);
                    var ep = new ApiLibraryEpisodeDto
                    {
                        Season = seasonFiles.Key,
                        Number = group.Key,
                        Title = em?.Title ?? (group.Key == 0 ? "Sin número de episodio" : $"Episodio {group.Key}"),
                        Overview = em?.Overview,
                        StillUrl = LibraryImageService.ProxyUrl(baseUrl, em?.StillUrl, "w300"),
                        AirDate = em?.AirDate,
                        Runtime = em?.Runtime ?? item.Runtime,
                        Rating = em?.Rating,
                        Files = group.OrderByDescending(f => f.Size).Select(f => ApiLibraryFileDto.From(f, baseUrl, watchByKey.GetValueOrDefault(f.Key))).ToList()
                    };
                    var epWatches = ep.Files.Where(f => f.Watch != null).Select(f => f.Watch!).ToList();
                    ep.Completed = epWatches.Any(w => w.Completed);
                    ep.Watch = epWatches.Where(w => !w.Completed && w.PositionMs > 0).OrderByDescending(w => w.LastPlayedAt).FirstOrDefault()
                        ?? epWatches.OrderByDescending(w => w.LastPlayedAt).FirstOrDefault();
                    seasonDto.Episodes.Add(ep);
                    allEpisodes.Add(ep);
                }
                seasonDto.EpisodesWatched = seasonDto.Episodes.Count(e => e.Completed);
                dto.Seasons.Add(seasonDto);
            }

            dto.NextUp = NextUp(allEpisodes);
            if (dto.NextUp?.Watch is { Completed: false, PositionMs: > 0 } w2)
                dto.Resume = dto.NextUp.Files.FirstOrDefault(f => f.FileId == w2.FileId && f.ChannelId == w2.ChannelId);
            return dto;
        }

        private static ApiLibraryEpisodeDto? NextUp(List<ApiLibraryEpisodeDto> episodes)
        {
            if (episodes.Count == 0) return null;
            var inProgress = episodes.Where(e => e.Watch is { Completed: false, PositionMs: > 0 })
                .OrderByDescending(e => e.Watch!.LastPlayedAt).FirstOrDefault();
            if (inProgress != null) return inProgress;
            var lastCompleted = -1;
            for (int i = 0; i < episodes.Count; i++)
                if (episodes[i].Completed) lastCompleted = i;
            for (int i = lastCompleted + 1; i < episodes.Count; i++)
                if (!episodes[i].Completed) return episodes[i];
            return null;
        }

        public async Task<List<ApiLibraryFileDto>> ContinueAsync(string baseUrl, int limit, bool includeHidden)
        {
            var watches = await _lib.GetWatchInProgress();
            var hidden = includeHidden || Config.ShowHiddenChannels ? new List<long>() : Config.HiddenChannels ?? new List<long>();
            watches = watches.Where(w => !hidden.Contains(w.ChannelId)).Take(limit).ToList();
            return await FilesForWatchesAsync(watches, baseUrl);
        }

        public async Task<List<ApiLibraryFileDto>> HistoryAsync(string baseUrl, int limit, bool includeHidden)
        {
            var watches = await _lib.GetWatchCompleted();
            var hidden = includeHidden || Config.ShowHiddenChannels ? new List<long>() : Config.HiddenChannels ?? new List<long>();
            watches = watches.Where(w => !hidden.Contains(w.ChannelId)).Take(limit).ToList();
            return await FilesForWatchesAsync(watches, baseUrl);
        }

        private async Task<List<ApiLibraryFileDto>> FilesForWatchesAsync(List<WatchState> watches, string baseUrl)
        {
            var result = new List<ApiLibraryFileDto>();
            var items = (await _lib.GetItems(watches.Where(w => w.ItemId != null).Select(w => w.ItemId!))).ToDictionary(i => i.Id);
            foreach (var w in watches)
            {
                var file = await _lib.GetFile(w.ChannelId, w.FileId) ?? await FileFromChannelAsync(w.ChannelId, w.FileId, w.FileName);
                if (file == null) continue;
                var item = file.ItemId != null ? items.GetValueOrDefault(file.ItemId) : null;
                result.Add(ApiLibraryFileDto.From(file, baseUrl, w, item == null ? null : ApiLibraryItemDto.From(item, baseUrl)));
            }
            return result;
        }

        public async Task<List<ApiLibraryItemDto>> RecentAsync(string? kind, int limit, string baseUrl, bool includeHidden)
        {
            var items = await _lib.GetAllItems();
            if (LibraryKind.IsValid(kind)) items = items.Where(i => i.Kind == kind).ToList();
            if (!includeHidden && !Config.ShowHiddenChannels) items = items.Where(i => !IsHidden(i)).ToList();
            items = items.OrderByDescending(i => i.CreatedAt).Take(limit).ToList();
            var watches = (await _lib.GetWatchByItems(items.Select(i => i.Id))).GroupBy(w => w.ItemId!).ToDictionary(g => g.Key, g => g.ToList());
            return items.Select(i => ApiLibraryItemDto.From(i, baseUrl, Summarize(i, watches.GetValueOrDefault(i.Id) ?? new()))).ToList();
        }

        public async Task<List<ApiLibraryGenreDto>> GenresAsync(string? kind)
        {
            var items = await _lib.GetAllItems();
            if (LibraryKind.IsValid(kind)) items = items.Where(i => i.Kind == kind).ToList();
            return items.SelectMany(i => i.Genres).GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ApiLibraryGenreDto { Name = g.Key, Count = g.Count() })
                .OrderBy(g => g.Name).ToList();
        }

        public async Task<List<ApiLibraryFileDto>> ListFilesAsync(string? status, long? channelId, string? search, string baseUrl)
        {
            List<LibraryFile> files;
            if (channelId.HasValue) files = await _lib.GetFilesByChannel(channelId.Value);
            else if (!string.IsNullOrWhiteSpace(status)) files = await _lib.GetFilesByStatus(status);
            else files = await _lib.GetAllFiles();
            if (!string.IsNullOrWhiteSpace(status)) files = files.Where(f => f.Status == status).ToList();
            if (!string.IsNullOrWhiteSpace(search))
                files = files.Where(f => f.FileName.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || f.Parsed.Title.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            files = files.OrderBy(f => f.FileName).ToList();

            var items = (await _lib.GetItems(files.Where(f => f.ItemId != null).Select(f => f.ItemId!))).ToDictionary(i => i.Id);
            var watches = (await _lib.GetWatchMany(files.Select(f => f.Key))).ToDictionary(w => w.Id);
            return files.Select(f => ApiLibraryFileDto.From(f, baseUrl, watches.GetValueOrDefault(f.Key),
                f.ItemId != null && items.TryGetValue(f.ItemId, out var it) ? ApiLibraryItemDto.From(it, baseUrl) : null)).ToList();
        }

        public async Task<ApiLibraryFileDto?> GetFileAsync(long channelId, string fileId, string baseUrl)
        {
            var file = await _lib.GetFile(channelId, fileId);
            if (file == null) return null;
            var item = file.ItemId != null ? await _lib.GetItem(file.ItemId) : null;
            var watch = await _lib.GetWatch(channelId, fileId);
            return ApiLibraryFileDto.From(file, baseUrl, watch, item == null ? null : ApiLibraryItemDto.From(item, baseUrl));
        }

        public async Task<ApiLibraryStatsDto> StatsAsync()
        {
            var config = Config;
            var providers = MetadataProviderRegistry.EffectiveConfigs(config)
                .Select(c => ApiLibraryProviderDto.From(MetadataProviderRegistry.Describe(c.Id)!, c)).ToList();
            var stats = new ApiLibraryStatsDto
            {
                Enabled = config.LibraryEnabled,
                Language = config.LibraryLanguage,
                AutoScan = config.LibraryAutoScan,
                WatchedThreshold = config.LibraryWatchedThreshold,
                Providers = providers,
                Ready = config.LibraryEnabled && _providers.GetEnabled().Count > 0
            };
            try
            {
                var items = await _lib.GetAllItems();
                stats.Movies = items.Count(i => i.Kind == LibraryKind.Movie);
                stats.Series = items.Count(i => i.Kind == LibraryKind.Series);
                stats.Files = await _lib.CountFilesByStatus();
                stats.InProgress = (await _lib.GetWatchInProgress()).Count;
                stats.Scan = ApiLibraryScanStateDto.From(await _scan.GetStateAsync());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Library statistics are not available");
            }
            return stats;
        }

        private static bool IsHidden(LibraryItem item)
        {
            var hidden = Config.HiddenChannels;
            if (hidden == null || hidden.Count == 0 || item.ChannelIds.Count == 0) return false;
            return item.ChannelIds.All(hidden.Contains);
        }

        public static ApiWatchSummaryDto Summarize(LibraryItem item, List<WatchState> watches)
        {
            var summary = new ApiWatchSummaryDto();
            if (watches.Count == 0)
            {
                summary.EpisodesTotal = item.EpisodeCount;
                return summary;
            }
            var latest = watches.OrderByDescending(w => w.LastPlayedAt).First();
            var inProgress = watches.Where(w => w.InProgress).OrderByDescending(w => w.LastPlayedAt).FirstOrDefault();
            summary.LastPlayedAt = latest.LastPlayedAt;
            summary.InProgress = inProgress != null;
            if (inProgress != null)
            {
                summary.PositionMs = inProgress.PositionMs;
                summary.DurationMs = inProgress.DurationMs;
                summary.Progress = Math.Round(inProgress.Progress, 4);
            }
            if (item.Kind == LibraryKind.Movie)
            {
                summary.Completed = watches.Any(w => w.Completed);
            }
            else
            {
                summary.EpisodesTotal = item.EpisodeCount;
                summary.EpisodesWatched = watches.Where(w => w.Completed && w.Episode.HasValue)
                    .Select(w => (w.Season ?? 1, w.Episode!.Value)).Distinct().Count();
                summary.Completed = item.EpisodeCount > 0 && summary.EpisodesWatched >= item.EpisodeCount;
            }
            return summary;
        }

        #endregion

        #region Watch state

        public async Task<WatchState?> UpdateWatchAsync(long channelId, string fileId, LibraryWatchUpdateRequest request)
        {
            var state = await GetOrCreateWatchAsync(channelId, fileId);
            if (state == null) return null;
            var wasCompleted = state.Completed;
            state.PositionMs = Math.Max(0, request.PositionMs);
            if (request.DurationMs > 0) state.DurationMs = request.DurationMs;
            var threshold = Math.Clamp(Config.LibraryWatchedThreshold <= 0 ? 0.92 : Config.LibraryWatchedThreshold, 0.5, 1.0);
            var completed = request.Completed ?? (state.DurationMs > 0 && (double)state.PositionMs / state.DurationMs >= threshold);
            if (request.Completed == null && wasCompleted && !completed)
            {
                // A rewatch from the start: keep the watched flag until the client says otherwise
                completed = state.PositionMs < 30_000 || completed;
            }
            state.Completed = completed;
            if (completed && !wasCompleted) state.PlayCount++;
            state.LastPlayedAt = DateTime.UtcNow;
            await _lib.UpsertWatch(state);
            return state;
        }

        public async Task<WatchState?> SetWatchedAsync(long channelId, string fileId, bool watched)
        {
            var state = await GetOrCreateWatchAsync(channelId, fileId);
            if (state == null) return null;
            Apply(state, watched);
            await _lib.UpsertWatch(state);
            return state;
        }

        public async Task<int> SetItemWatchedAsync(LibraryItem item, int? season, bool watched)
        {
            var files = (await _lib.GetFilesByItem(item.Id)).Where(f => f.Status != LibraryFileStatus.Ignored);
            if (season.HasValue) files = files.Where(f => (f.Season ?? 1) == season.Value);
            int count = 0;
            foreach (var file in files)
            {
                var state = await _lib.GetWatch(file.ChannelId, file.FileId);
                if (state == null)
                {
                    if (!watched) continue;
                    state = NewWatch(file.ChannelId, file.FileId, file);
                }
                Apply(state, watched);
                await _lib.UpsertWatch(state);
                count++;
            }
            return count;
        }

        public Task DeleteWatchAsync(long channelId, string fileId) => _lib.DeleteWatch(channelId, fileId);

        private static void Apply(WatchState state, bool watched)
        {
            if (watched)
            {
                if (!state.Completed) state.PlayCount++;
                state.Completed = true;
                if (state.DurationMs > 0) state.PositionMs = state.DurationMs;
            }
            else
            {
                state.Completed = false;
                state.PositionMs = 0;
            }
            state.LastPlayedAt = DateTime.UtcNow;
        }

        private async Task<WatchState?> GetOrCreateWatchAsync(long channelId, string fileId)
        {
            var state = await _lib.GetWatch(channelId, fileId);
            if (state != null) return state;
            var file = await _lib.GetFile(channelId, fileId);
            if (file == null)
            {
                // Not (yet) in the library: still worth remembering, if the file exists
                var doc = await _lib.GetChannelFile(channelId.ToString(), fileId);
                if (doc == null) return null;
                return new WatchState { Id = LibraryFile.KeyOf(channelId, fileId), ChannelId = channelId, FileId = fileId, FileName = doc.Name };
            }
            return NewWatch(channelId, fileId, file);
        }

        private static WatchState NewWatch(long channelId, string fileId, LibraryFile file) => new()
        {
            Id = LibraryFile.KeyOf(channelId, fileId),
            ChannelId = channelId,
            FileId = fileId,
            FileName = file.FileName,
            ItemId = file.ItemId,
            Season = file.Season,
            Episode = file.Episode
        };

        #endregion

        #region Manual identification

        public async Task<List<ApiProviderCandidateDto>> SearchProvidersAsync(string? q, string? kind, int? year, string? imdbId, string? providerId, string baseUrl, CancellationToken ct)
        {
            var providers = _providers.GetEnabled();
            if (!string.IsNullOrWhiteSpace(providerId))
                providers = providers.Where(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase)).ToList();
            var result = new List<ApiProviderCandidateDto>();
            var kinds = LibraryKind.IsValid(kind) ? new[] { kind! } : new[] { LibraryKind.Movie, LibraryKind.Series };
            foreach (var provider in providers)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(imdbId))
                    {
                        var hit = await provider.FindByImdbAsync(imdbId.Trim(), LibraryKind.IsValid(kind) ? kind : null, ct);
                        if (hit != null) result.Add(ApiProviderCandidateDto.From(hit, baseUrl));
                        continue;
                    }
                    foreach (var k in kinds)
                        foreach (var c in await provider.SearchAsync(k, q!, year, ct))
                            result.Add(ApiProviderCandidateDto.From(c, baseUrl));
                }
                catch (ProviderException ex)
                {
                    _logger.LogWarning("{Provider} search failed: {Message}", provider.Id, ex.Message);
                    if (providers.Count == 1) throw;
                }
            }
            return result;
        }

        public async Task<(LibraryFile? File, string? Error)> MatchFileAsync(long channelId, string fileId, LibraryMatchFileRequest request, CancellationToken ct)
        {
            if (!LibraryKind.IsValid(request.Kind)) return (null, "kind must be movie or series");
            if (request.Kind == LibraryKind.Series && (!request.Season.HasValue || !request.Episode.HasValue))
                return (null, "season and episode are required for a series file");
            var provider = _providers.Get(request.Provider);
            if (provider == null) return (null, $"Provider '{request.Provider}' is not enabled");

            var file = await _lib.GetFile(channelId, fileId) ?? await FileFromChannelAsync(channelId, fileId, null);
            if (file == null) return (null, "file not found");

            var item = await _scan.ResolveItemAsync(provider, request.Kind, new ProviderCandidate
            {
                Provider = provider.Id,
                ProviderId = request.ProviderId,
                Kind = request.Kind,
                ImdbId = provider.Id == OmdbProvider.ProviderId ? request.ProviderId : null
            }, ct);

            var oldItem = file.ItemId;
            file.ItemId = item.Id;
            file.Kind = request.Kind;
            file.Season = request.Kind == LibraryKind.Series ? request.Season : null;
            file.Episode = request.Kind == LibraryKind.Series ? request.Episode : null;
            file.Status = LibraryFileStatus.Matched;
            file.Confidence = 1;
            file.MatchSource = LibraryMatchSource.Manual;
            file.Locked = true;
            file.Error = null;
            file.ScannedAt = DateTime.UtcNow;
            await _lib.UpsertFile(file);
            await _scan.LinkWatchAsync(file);
            if (file.Season.HasValue && file.Episode.HasValue)
                await _scan.EnsureEpisodeAsync(item, file.Season.Value, file.Episode.Value, ct);

            var touched = new List<string> { item.Id };
            if (oldItem != null && oldItem != item.Id) touched.Add(oldItem);
            await _scan.RecountAsync(touched);
            return (file, null);
        }

        public async Task<LibraryFile?> UnmatchFileAsync(long channelId, string fileId)
        {
            var file = await _lib.GetFile(channelId, fileId);
            if (file == null) return null;
            var oldItem = file.ItemId;
            file.ItemId = null;
            file.Season = null;
            file.Episode = null;
            file.Status = LibraryFileStatus.Unmatched;
            file.Confidence = 0;
            file.MatchSource = LibraryMatchSource.Manual;
            file.Locked = true;
            await _lib.UpsertFile(file);
            await _scan.LinkWatchAsync(file);
            if (oldItem != null) await _scan.RecountAsync(new[] { oldItem });
            return file;
        }

        public async Task<LibraryFile?> SetIgnoredAsync(long channelId, string fileId, bool ignored)
        {
            var file = await _lib.GetFile(channelId, fileId) ?? await FileFromChannelAsync(channelId, fileId, null);
            if (file == null) return null;
            var oldItem = file.ItemId;
            if (ignored)
            {
                file.Status = LibraryFileStatus.Ignored;
                file.Locked = true;
                file.MatchSource = LibraryMatchSource.Manual;
            }
            else
            {
                file.Status = LibraryFileStatus.Unmatched;
                file.ItemId = null;
                file.Locked = false;
                file.MatchSource = LibraryMatchSource.Auto;
            }
            await _lib.UpsertFile(file);
            if (oldItem != null) await _scan.RecountAsync(new[] { oldItem });
            return file;
        }

        public async Task<(LibraryItem? Item, string? Error)> IdentifyItemAsync(string itemId, LibraryIdentifyItemRequest request, CancellationToken ct)
        {
            var item = await _lib.GetItem(itemId);
            if (item == null) return (null, "item not found");
            var provider = _providers.Get(request.Provider);
            if (provider == null) return (null, $"Provider '{request.Provider}' is not enabled");
            var kind = LibraryKind.IsValid(request.Kind) ? request.Kind! : item.Kind;

            var target = await _scan.ResolveItemAsync(provider, kind, new ProviderCandidate
            {
                Provider = provider.Id,
                ProviderId = request.ProviderId,
                Kind = kind,
                ImdbId = provider.Id == OmdbProvider.ProviderId ? request.ProviderId : null
            }, ct);

            var files = await _lib.GetFilesByItem(itemId);
            foreach (var file in files)
            {
                file.ItemId = target.Id;
                file.Kind = kind;
                if (kind == LibraryKind.Movie) { file.Season = null; file.Episode = null; }
                else { file.Season ??= file.Parsed.Season ?? 1; file.Episode ??= file.Parsed.Episode; }
                if (file.Status != LibraryFileStatus.Ignored)
                {
                    file.Status = LibraryFileStatus.Matched;
                    file.Confidence = 1;
                }
                file.MatchSource = LibraryMatchSource.Manual;
                file.Locked = true;
                file.Error = null;
                await _lib.UpsertFile(file);
                await _scan.LinkWatchAsync(file);
                if (file.Season.HasValue && file.Episode.HasValue)
                    await _scan.EnsureEpisodeAsync(target, file.Season.Value, file.Episode.Value, ct);
            }

            target.Locked = true;
            await _lib.UpsertItem(target);
            await _scan.RecountAsync(target.Id == itemId ? new[] { itemId } : new[] { itemId, target.Id });
            return (await _lib.GetItem(target.Id), null);
        }

        public async Task<(LibraryItem? Item, string? Error)> RefreshItemAsync(string itemId, CancellationToken ct)
        {
            var item = await _lib.GetItem(itemId);
            if (item == null) return (null, "item not found");
            var source = _scan.ProviderFor(item);
            if (source == null) return (null, "No enabled provider knows this item");
            var (provider, providerId) = source.Value;
            var refreshed = await _scan.ResolveItemAsync(provider, item.Kind, new ProviderCandidate
            {
                Provider = provider.Id, ProviderId = providerId, Kind = item.Kind, ImdbId = item.ImdbId
            }, ct, refresh: true);
            if (refreshed.Kind == LibraryKind.Series)
            {
                var seasons = (await _lib.GetFilesByItem(refreshed.Id)).Select(f => f.Season ?? 1).Distinct();
                foreach (var season in seasons)
                    await _scan.GetSeasonEpisodesAsync(refreshed, season, ct);
            }
            await _scan.RecountAsync(new[] { refreshed.Id });
            return (await _lib.GetItem(refreshed.Id), null);
        }

        /// <summary>A library record for a channel file the scan has not visited yet (or that is not a video).</summary>
        private async Task<LibraryFile?> FileFromChannelAsync(long channelId, string fileId, string? knownName)
        {
            var doc = await _lib.GetChannelFile(channelId.ToString(), fileId);
            if (doc == null)
            {
                if (knownName == null) return null;
                return new LibraryFile { ChannelId = channelId, FileId = fileId, FileName = knownName, Parsed = MediaNameParser.Parse(knownName) };
            }
            return new LibraryFile
            {
                ChannelId = channelId,
                FileId = fileId,
                MessageId = doc.MessageId,
                IsSplit = doc.isSplit,
                FileName = doc.Name ?? knownName ?? string.Empty,
                Type = doc.Type ?? string.Empty,
                FolderPath = string.IsNullOrEmpty(doc.FilterPath) ? "/" : doc.FilterPath,
                Size = doc.Size,
                FileDate = doc.DateCreated,
                Parsed = MediaNameParser.Parse(doc.Name ?? string.Empty, doc.FilterPath)
            };
        }

        #endregion
    }
}
