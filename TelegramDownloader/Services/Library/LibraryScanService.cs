using System.Collections.Concurrent;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Services.Library
{
    /// <summary>
    /// Walks the channel indexes, identifies every video file through the
    /// configured providers and keeps the library collections in sync. One
    /// scan at a time; incremental unless forced; only MongoDB and the
    /// providers are touched, never Telegram.
    /// </summary>
    public class LibraryScanService : IHostedService
    {
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts", ".mpg", ".mpeg", ".m2ts", ".3gp", ".ogv", ".divx", ".vob"
        };

        private readonly ILibraryDbService _lib;
        private readonly IMetadataProviderSource _providers;
        private readonly ILogger<LibraryScanService> _logger;
        private readonly object _sync = new();
        private readonly ConcurrentQueue<string> _pending = new();
        // Episodes already fetched during the current scan, per "itemId:season"
        private readonly ConcurrentDictionary<string, List<LibraryEpisode>> _seasonCache = new();
        private CancellationTokenSource? _cts;

        public LibraryScanService(ILibraryDbService lib, IMetadataProviderSource providers, ILogger<LibraryScanService> logger)
        {
            _lib = lib;
            _providers = providers;
            _logger = logger;
        }

        public LibraryScanState? Current { get; private set; }
        public bool IsRunning => Current?.Running == true;

        public static bool IsVideo(string? extension) =>
            !string.IsNullOrEmpty(extension) && VideoExtensions.Contains(extension.StartsWith('.') ? extension : "." + extension);

        #region Hosted service

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _lib.EnsureIndexes();
                var state = await _lib.GetScanState();
                if (state != null && state.Running)
                {
                    // A scan that was running when the process died
                    state.Running = false;
                    state.FinishedAt = DateTime.UtcNow;
                    state.Error ??= "Interrupted by a server restart";
                    await _lib.SaveScanState(state);
                }
                Current = state;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Library storage is not available yet");
            }
            FileService.ChannelRefreshed += OnChannelRefreshed;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            FileService.ChannelRefreshed -= OnChannelRefreshed;
            Cancel();
            return Task.CompletedTask;
        }

        private void OnChannelRefreshed(string channelId)
        {
            var config = GeneralConfigStatic.config;
            if (!config.LibraryEnabled || !config.LibraryAutoScan) return;
            if (!IsChannelSelected(config, channelId)) return;
            if (!Start(channelId, false, out _))
                _pending.Enqueue(channelId);
        }

        /// <summary>
        /// The channels a scan covers: the included list when one is set (else
        /// every indexed channel), minus the excluded ones.
        /// </summary>
        public static List<string> SelectChannels(GeneralConfig config, IEnumerable<string> indexedChannels) =>
            indexedChannels.Where(c => IsChannelSelected(config, c)).ToList();

        public static bool IsChannelSelected(GeneralConfig config, string channelId)
        {
            if (!long.TryParse(channelId, out var id)) return false;
            var included = config.LibraryIncludedChannels ?? new List<long>();
            var excluded = config.LibraryExcludedChannels ?? new List<long>();
            if (excluded.Contains(id)) return false;
            return included.Count == 0 || included.Contains(id);
        }

        #endregion

        #region Start / cancel

        /// <summary>Starts a scan in the background. False when one is already running or nothing is configured.</summary>
        public bool Start(string? channelId, bool force, out string? error)
        {
            error = null;
            lock (_sync)
            {
                if (IsRunning)
                {
                    error = "A library scan is already running";
                    return false;
                }
                if (_providers.GetEnabled().Count == 0)
                {
                    error = "No metadata provider is enabled. Add an API key in the settings.";
                    return false;
                }
                var state = new LibraryScanState
                {
                    Running = true,
                    StartedAt = DateTime.UtcNow,
                    Scope = string.IsNullOrWhiteSpace(channelId) ? "all" : channelId.Trim(),
                    Force = force
                };
                Current = state;
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;
                _ = Task.Run(() => RunAsync(state, ct), CancellationToken.None);
                return true;
            }
        }

        public void Cancel()
        {
            lock (_sync)
            {
                _cts?.Cancel();
            }
        }

        public async Task<LibraryScanState?> GetStateAsync() => Current ?? await _lib.GetScanState();

        private async Task RunAsync(LibraryScanState state, CancellationToken ct)
        {
            try
            {
                var config = GeneralConfigStatic.config;
                List<string> channels;
                if (state.Scope == "all")
                    channels = SelectChannels(config, await _lib.GetChannelDatabaseNames());
                else
                    channels = new List<string> { state.Scope };
                await ScanAsync(state, channels, state.Force, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Library scan failed");
                state.Error ??= ex.Message;
                state.Running = false;
                state.FinishedAt = DateTime.UtcNow;
                try { await _lib.SaveScanState(state); } catch { }
            }
            finally
            {
                lock (_sync)
                {
                    _cts?.Dispose();
                    _cts = null;
                }
                // Channels refreshed while this scan was running
                if (_pending.TryDequeue(out var next))
                    Start(next, false, out _);
            }
        }

        #endregion

        #region Scan

        public async Task ScanAsync(LibraryScanState state, List<string> channels, bool force, CancellationToken ct)
        {
            _seasonCache.Clear();
            var touched = new HashSet<string>();
            state.ChannelsTotal = channels.Count;
            await _lib.SaveScanState(state);
            try
            {
                foreach (var channel in channels)
                {
                    ct.ThrowIfCancellationRequested();
                    state.CurrentChannel = channel;
                    try
                    {
                        await ScanChannelAsync(channel, force, state, touched, ct);
                    }
                    catch (ProviderException ex) when (ex.StatusCode is 401 or 403 or 429)
                    {
                        throw;   // no point continuing with a bad key or an exhausted quota
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Library scan of channel {Channel} failed", channel);
                        state.Failed++;
                    }
                    state.ChannelsScanned++;
                    await _lib.SaveScanState(state);
                }
                await RecountAsync(touched);
            }
            catch (OperationCanceledException)
            {
                state.Cancelled = true;
                await RecountAsync(touched);
            }
            catch (ProviderException ex)
            {
                state.Error = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Library scan failed");
                state.Error = ex.Message;
            }
            finally
            {
                state.Running = false;
                state.CurrentChannel = null;
                state.FinishedAt = DateTime.UtcNow;
                _seasonCache.Clear();
                await _lib.SaveScanState(state);
                _logger.LogInformation("Library scan finished: {Matched} matched, {Review} to review, {Unmatched} unmatched, {Failed} failed",
                    state.Matched, state.Review, state.Unmatched, state.Failed);
            }
        }

        public async Task ScanChannelAsync(string channelId, bool force, LibraryScanState state, HashSet<string> touched, CancellationToken ct)
        {
            if (!long.TryParse(channelId, out var chId)) return;

            var docs = (await _lib.GetChannelFiles(channelId)).Where(d => d.IsFile && IsVideo(d.Type)).ToList();
            var existing = (await _lib.GetFilesByChannel(chId)).ToDictionary(f => f.FileId);
            state.FilesSeen += docs.Count;

            var present = docs.Select(d => d.Id).ToHashSet();
            foreach (var gone in existing.Values.Where(f => !present.Contains(f.FileId)).ToList())
            {
                await _lib.DeleteFile(chId, gone.FileId);
                if (gone.ItemId != null) touched.Add(gone.ItemId);
                existing.Remove(gone.FileId);
                state.FilesRemoved++;
            }

            var config = GeneralConfigStatic.config;
            var groups = new Dictionary<string, List<(BsonFileManagerModel Doc, ParsedName Parsed, LibraryFile? Old, string? Hint)>>();
            foreach (var doc in docs)
            {
                existing.TryGetValue(doc.Id, out var old);
                var hint = LibraryFolderRules.Effective(config, chId, doc.FilterPath);
                if (old != null && !ShouldRescan(old, force, hint)) continue;

                if (hint == LibraryRuleKind.Ignore)
                {
                    // Folder rule: not library material (extras, trailers...)
                    var ignored = NewFile(chId, doc, old, MediaNameParser.Parse(doc.Name, doc.FilterPath), null, hint);
                    ignored.Status = LibraryFileStatus.Ignored;
                    await _lib.UpsertFile(ignored);
                    if (old?.ItemId != null) touched.Add(old.ItemId);
                    continue;
                }

                var parsed = MediaNameParser.Parse(doc.Name, doc.FilterPath, null, hint);
                var kind = parsed.IsSeries ? LibraryKind.Series : LibraryKind.Movie;
                var key = $"{kind}|{MediaNameParser.NormalizeForMatch(parsed.Title)}|{parsed.Year}";
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new();
                list.Add((doc, parsed, old, hint));
            }

            foreach (var group in groups.Values)
            {
                ct.ThrowIfCancellationRequested();
                await ProcessGroupAsync(chId, group, state, touched, ct);
            }
        }

        private static bool ShouldRescan(LibraryFile old, bool force, string? hint)
        {
            if (old.Locked) return false;
            // A folder rule that changed since the file was scanned re-decides it
            if (!string.Equals(old.RuleKind, hint, StringComparison.Ordinal)) return true;
            if (old.Status == LibraryFileStatus.Ignored) return false;
            if (force) return true;
            // Unmatched files get another chance every scan (a new provider or a
            // cache refresh may know them now); matched ones are left alone.
            return old.Status == LibraryFileStatus.Unmatched;
        }

        private static LibraryFile NewFile(long channelId, BsonFileManagerModel doc, LibraryFile? old, ParsedName parsed, string? kind, string? hint) => new()
        {
            Id = old?.Id ?? MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            ChannelId = channelId,
            FileId = doc.Id,
            MessageId = doc.MessageId,
            IsSplit = doc.isSplit,
            FileName = doc.Name ?? string.Empty,
            Type = doc.Type ?? string.Empty,
            FolderPath = string.IsNullOrEmpty(doc.FilterPath) ? "/" : doc.FilterPath,
            Size = doc.Size,
            FileDate = doc.DateCreated,
            Parsed = parsed,
            Kind = kind,
            Status = LibraryFileStatus.Unmatched,
            MatchSource = LibraryMatchSource.Auto,
            RuleKind = hint,
            ScannedAt = DateTime.UtcNow
        };

        private async Task ProcessGroupAsync(long channelId, List<(BsonFileManagerModel Doc, ParsedName Parsed, LibraryFile? Old, string? Hint)> files,
            LibraryScanState state, HashSet<string> touched, CancellationToken ct)
        {
            var sample = files[0].Parsed;
            var kind = sample.IsSeries ? LibraryKind.Series : LibraryKind.Movie;
            LibraryItem? item = null;
            MatchResult? best = null;
            string? error = null;

            if (sample.HasTitle && !MediaNameParser.IsJunk(sample.Title))
            {
                try
                {
                    var (provider, result) = await FindBestAsync(kind, sample, ct);
                    if (provider != null && result != null && result.Status != LibraryFileStatus.Unmatched)
                    {
                        item = await ResolveItemAsync(provider, kind, result.Candidate, ct);
                        best = result;
                    }
                }
                catch (ProviderException ex) when (ex.StatusCode is 401 or 403 or 429)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not identify '{Title}'", sample.Title);
                    error = ex.Message;
                    state.Failed += files.Count;
                }
            }

            foreach (var (doc, parsed, old, hint) in files)
            {
                var file = NewFile(channelId, doc, old, parsed, kind, hint);
                file.Error = error;

                if (item != null && best != null)
                {
                    file.ItemId = item.Id;
                    file.Status = best.Status;
                    file.Confidence = best.Score;
                    if (kind == LibraryKind.Series)
                    {
                        file.Season = parsed.Season ?? 1;
                        file.Episode = parsed.Episode;
                        if (parsed.Episode == null)
                            file.Status = LibraryFileStatus.Review;
                        else
                        {
                            var episode = await EnsureEpisodeAsync(item, file.Season.Value, parsed.Episode.Value, ct);
                            if (episode == null && file.Status == LibraryFileStatus.Matched)
                                file.Status = LibraryFileStatus.Review;
                        }
                    }
                    touched.Add(item.Id);
                }
                if (old?.ItemId != null && old.ItemId != file.ItemId) touched.Add(old.ItemId);

                await _lib.UpsertFile(file);
                await LinkWatchAsync(file);

                if (old == null) state.FilesNew++;
                switch (file.Status)
                {
                    case LibraryFileStatus.Matched: state.Matched++; break;
                    case LibraryFileStatus.Review: state.Review++; break;
                    default: state.Unmatched++; break;
                }
            }
        }

        private async Task<(IMetadataProvider? Provider, MatchResult? Result)> FindBestAsync(string kind, ParsedName parsed, CancellationToken ct)
        {
            IMetadataProvider? bestProvider = null;
            MatchResult? best = null;
            foreach (var provider in _providers.GetEnabled())
            {
                var candidates = await provider.SearchAsync(kind, parsed.Title, parsed.Year, ct);
                if (candidates.Count == 0 && parsed.Year.HasValue)
                    candidates = await provider.SearchAsync(kind, parsed.Title, null, ct);
                var result = LibraryMatcher.Best(parsed, candidates);
                if (result != null && (best == null || result.Score > best.Score))
                {
                    best = result;
                    bestProvider = provider;
                }
                if (best != null && best.Score >= LibraryMatcher.MatchedThreshold)
                    break;
            }
            return (bestProvider, best);
        }

        /// <summary>Keeps a file's watch state pointing at the item it is now linked to.</summary>
        public async Task LinkWatchAsync(LibraryFile file)
        {
            var watch = await _lib.GetWatch(file.ChannelId, file.FileId);
            if (watch == null) return;
            if (watch.ItemId == file.ItemId && watch.Season == file.Season && watch.Episode == file.Episode && watch.FileName == file.FileName) return;
            watch.ItemId = file.ItemId;
            watch.Season = file.Season;
            watch.Episode = file.Episode;
            watch.FileName ??= file.FileName;
            await _lib.UpsertWatch(watch);
        }

        #endregion

        #region Items and episodes (shared with the manual actions)

        /// <summary>
        /// The library item for a provider hit: an existing one (matched by
        /// provider id or IMDb id) or a freshly fetched one enriched by the
        /// other providers. With <paramref name="refresh"/> the metadata is
        /// fetched again even when the item exists.
        /// </summary>
        public async Task<LibraryItem> ResolveItemAsync(IMetadataProvider provider, string kind, ProviderCandidate candidate, CancellationToken ct, bool refresh = false)
        {
            var existing = await _lib.FindItemByProvider(provider.Id, candidate.ProviderId)
                ?? await _lib.FindItemByExternalId(ExternalKeyFor(provider.Id), candidate.ProviderId);
            if (existing == null && !string.IsNullOrEmpty(candidate.ImdbId))
                existing = await _lib.FindItemByExternalId("imdb", candidate.ImdbId);
            if (existing != null && !refresh)
                return existing;

            var fetched = await provider.GetItemAsync(kind, candidate.ProviderId, ct)
                ?? throw new ProviderException($"{provider.Descriptor.Name} has no {kind} with id {candidate.ProviderId}");
            fetched.ExternalIds.TryAdd(ExternalKeyFor(provider.Id), candidate.ProviderId);

            foreach (var other in _providers.GetEnabled().Where(p => p.Id != provider.Id))
            {
                try
                {
                    await other.EnrichAsync(fetched, ct);
                }
                catch (ProviderException ex) when (ex.StatusCode is 401 or 403 or 429)
                {
                    throw;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "{Provider} could not enrich '{Title}'", other.Id, fetched.Title);
                }
            }

            // The same title may already exist through an id we only learnt now
            existing ??= fetched.ImdbId != null ? await _lib.FindItemByExternalId("imdb", fetched.ImdbId) : null;
            if (existing != null)
            {
                fetched.Id = existing.Id;
                fetched.CreatedAt = existing.CreatedAt;
                fetched.Locked = existing.Locked;
                fetched.FileCount = existing.FileCount;
                fetched.EpisodeCount = existing.EpisodeCount;
                fetched.ChannelIds = existing.ChannelIds;
                foreach (var kv in existing.ExternalIds)
                    fetched.ExternalIds.TryAdd(kv.Key, kv.Value);
                if (!refresh)
                {
                    // Keep the identity the item already had
                    fetched.Provider = existing.Provider;
                    fetched.ProviderId = existing.ProviderId;
                }
            }
            fetched.SortTitle = MediaNameParser.SortTitle(fetched.Title);
            fetched.MetadataFetchedAt = DateTime.UtcNow;
            await _lib.UpsertItem(fetched);

            if (refresh)
            {
                await _lib.DeleteEpisodes(fetched.Id);
                foreach (var key in _seasonCache.Keys.Where(k => k.StartsWith(fetched.Id + ":")).ToList())
                    _seasonCache.TryRemove(key, out _);
            }
            return fetched;
        }

        public static string ExternalKeyFor(string providerId) =>
            providerId.Equals(OmdbProvider.ProviderId, StringComparison.OrdinalIgnoreCase) ? "imdb" : providerId.ToLowerInvariant();

        /// <summary>An enabled provider that knows this item, with the id to use there.</summary>
        public (IMetadataProvider Provider, string ProviderId)? ProviderFor(LibraryItem item)
        {
            var enabled = _providers.GetEnabled();
            var own = enabled.FirstOrDefault(p => p.Id.Equals(item.Provider, StringComparison.OrdinalIgnoreCase));
            if (own != null) return (own, item.ProviderId);
            foreach (var p in enabled)
                if (item.ExternalIds.TryGetValue(ExternalKeyFor(p.Id), out var id))
                    return (p, id);
            return null;
        }

        public async Task<List<LibraryEpisode>> GetSeasonEpisodesAsync(LibraryItem item, int season, CancellationToken ct)
        {
            var cacheKey = $"{item.Id}:{season}";
            if (_seasonCache.TryGetValue(cacheKey, out var cached)) return cached;

            var known = (await _lib.GetEpisodes(item.Id)).Where(e => e.Season == season).ToList();
            if (known.Count == 0)
            {
                var source = ProviderFor(item);
                if (source != null)
                {
                    var fetched = await source.Value.Provider.GetSeasonAsync(source.Value.ProviderId, season, ct);
                    foreach (var e in fetched) e.ItemId = item.Id;
                    if (fetched.Count > 0)
                    {
                        await _lib.UpsertEpisodes(fetched);
                        known = (await _lib.GetEpisodes(item.Id)).Where(e => e.Season == season).ToList();
                    }
                }
            }
            _seasonCache[cacheKey] = known;
            return known;
        }

        public async Task<LibraryEpisode?> EnsureEpisodeAsync(LibraryItem item, int season, int episode, CancellationToken ct)
        {
            var episodes = await GetSeasonEpisodesAsync(item, season, ct);
            return episodes.FirstOrDefault(e => e.Number == episode);
        }

        /// <summary>Recomputes file counts and channel lists; items left without files are removed.</summary>
        public async Task RecountAsync(IEnumerable<string> itemIds)
        {
            foreach (var id in itemIds.Distinct().ToList())
            {
                var item = await _lib.GetItem(id);
                if (item == null) continue;
                var files = (await _lib.GetFilesByItem(id)).Where(f => f.Status != LibraryFileStatus.Ignored).ToList();
                if (files.Count == 0)
                {
                    await _lib.DeleteItem(id);
                    continue;
                }
                item.FileCount = files.Count;
                item.ChannelIds = files.Select(f => f.ChannelId).Distinct().OrderBy(c => c).ToList();
                item.EpisodeCount = item.Kind == LibraryKind.Series
                    ? files.Where(f => f.Episode.HasValue).Select(f => (f.Season ?? 1, f.Episode!.Value)).Distinct().Count()
                    : 0;
                await _lib.UpsertItem(item);
            }
        }

        #endregion
    }
}
