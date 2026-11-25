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
    /// <para>
    /// A scan first compares every channel index with the library and groups the
    /// files to identify by title, whatever their channel; then it identifies
    /// several titles at once. The request rate is limited by each provider.
    /// </para>
    /// </summary>
    public class LibraryScanService : IHostedService
    {
        public static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts", ".mpg", ".mpeg", ".m2ts", ".3gp", ".ogv", ".divx", ".vob"
        };

        /// <summary>Titles identified at the same time.</summary>
        public const int TitleParallelism = 8;
        /// <summary>Channel indexes read at the same time.</summary>
        public const int ChannelParallelism = 4;
        private static readonly TimeSpan SaveStateInterval = TimeSpan.FromSeconds(2);
        /// <summary>How often the counts of the items found so far are updated, so they show up before the scan ends.</summary>
        private static readonly TimeSpan RecountInterval = TimeSpan.FromSeconds(20);

        private readonly ILibraryDbService _lib;
        private readonly IMetadataProviderSource _providers;
        private readonly ILogger<LibraryScanService> _logger;
        private readonly object _sync = new();
        private readonly ConcurrentQueue<(string Channel, string? Folder)> _pending = new();
        // Episodes already fetched during the current scan, per "itemId:season"
        private readonly ConcurrentDictionary<string, List<LibraryEpisode>> _seasonCache = new();
        // Two titles can lead to the same item or season at once: one creates it, the other waits
        private readonly StripedLock _itemLocks = new();
        private readonly StripedLock _seasonLocks = new();
        private readonly SemaphoreSlim _itemWriteLock = new(1, 1);
        private CancellationTokenSource? _cts;

        public LibraryScanService(ILibraryDbService lib, IMetadataProviderSource providers, ILogger<LibraryScanService> logger)
        {
            _lib = lib;
            _providers = providers;
            _logger = logger;
        }

        public LibraryScanState? Current { get; private set; }

        /// <summary>
        /// Raised when a scan ends (also when cancelled or failed) with the ids
        /// of the items it created or linked files to. Used to prefetch their
        /// posters; handlers must not block.
        /// </summary>
        public event Action<IReadOnlyCollection<string>>? ScanFinished;
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
            StartOrQueue(channelId);
        }

        /// <summary>
        /// Scans a channel, or one of its folders, now or after the running
        /// scan. False when it cannot be scanned: no provider is enabled.
        /// </summary>
        public bool StartOrQueue(string channelId, string? folder = null)
        {
            lock (_sync)
            {
                if (Start(channelId, false, out _, folder)) return true;
                if (!IsRunning) return false;
                if (!_pending.Contains((channelId, folder))) _pending.Enqueue((channelId, folder));
                return true;
            }
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
        public bool Start(string? channelId, bool force, out string? error, string? folder = null)
        {
            var scope = string.IsNullOrWhiteSpace(channelId) ? "all" : channelId.Trim();
            var folders = folder != null && long.TryParse(scope, out var id)
                ? new List<LibraryScanFolder> { new() { ChannelId = id, Path = LibraryFolderRules.Normalize(folder) } }
                : null;
            return Start(scope, folders, force, out error);
        }

        /// <summary>Like <see cref="Start(string?, bool, out string?, string?)"/>, limited to some folders of one or several channels.</summary>
        public bool Start(IEnumerable<LibraryScanFolder> folders, bool force, out string? error)
        {
            var outermost = LibraryFolderRules.Outermost(folders);
            if (outermost.Count == 0)
            {
                // Never taken for "no limit"
                error = "No folder to scan";
                return false;
            }
            var channels = outermost.Select(f => f.ChannelId).Distinct().ToList();
            return Start(channels.Count == 1 ? channels[0].ToString() : "folders", outermost, force, out error);
        }

        private bool Start(string scope, List<LibraryScanFolder>? folders, bool force, out string? error)
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
                    Scope = scope,
                    Folders = folders,
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
                if (state.Folders != null)
                    channels = state.Folders.Select(f => f.ChannelId.ToString()).Distinct().ToList();
                else if (state.Scope == "all")
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
                    StartOrQueue(next.Channel, next.Folder);
            }
        }

        #endregion

        #region Scan

        /// <summary>Async locks by key, with a fixed number of semaphores: two keys may share one.</summary>
        private sealed class StripedLock
        {
            private readonly SemaphoreSlim[] _stripes = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

            public async Task<IDisposable> LockAsync(string key, CancellationToken ct)
            {
                var stripe = _stripes[(key.GetHashCode() & int.MaxValue) % _stripes.Length];
                await stripe.WaitAsync(ct);
                return new Releaser(stripe);
            }

            private sealed class Releaser : IDisposable
            {
                private readonly SemaphoreSlim _stripe;
                public Releaser(SemaphoreSlim stripe) => _stripe = stripe;
                public void Dispose() => _stripe.Release();
            }
        }

        private sealed record ScanEntry(long ChannelId, BsonFileManagerModel Doc, ParsedName Parsed, LibraryFile? Old, string? Hint, PinnedTitle? Pin);

        /// <summary>The series assigned to the folder of a file.</summary>
        private sealed record PinnedTitle(LibraryFolderRule Rule, string Key, bool Continuous);

        /// <summary>Everything the parallel parts of one scan share.</summary>
        private sealed class ScanRun
        {
            public required LibraryScanState State { get; init; }
            /// <summary>Guards the counters of <see cref="State"/> and the sets below.</summary>
            public object Gate { get; } = new();
            public HashSet<string> Touched { get; } = new();
            /// <summary>Touched since the last periodic recount.</summary>
            public HashSet<string> ToRecount { get; } = new();
            /// <summary>Files left per channel; a channel counts as scanned when it gets to zero.</summary>
            public Dictionary<long, int> Remaining { get; } = new();
            /// <summary>Secondary providers that rejected the key or ran out of quota during this scan.</summary>
            public ConcurrentDictionary<string, string> Excluded { get; } = new(StringComparer.OrdinalIgnoreCase);
            public SemaphoreSlim Housekeeping { get; } = new(1, 1);
            public DateTime LastSave { get; set; } = DateTime.UtcNow;
            public DateTime LastRecount { get; set; } = DateTime.UtcNow;
            public Exception? Fatal { get; set; }

            public void Touch(string itemId)
            {
                lock (Gate)
                {
                    Touched.Add(itemId);
                    ToRecount.Add(itemId);
                }
            }

            public void Update(Action<LibraryScanState> change)
            {
                lock (Gate) change(State);
            }

            public void FilesDone(IEnumerable<ScanEntry> files)
            {
                lock (Gate)
                {
                    foreach (var file in files)
                    {
                        State.FilesProcessed++;
                        if (--Remaining[file.ChannelId] == 0) State.ChannelsScanned++;
                    }
                }
            }
        }

        public async Task ScanAsync(LibraryScanState state, List<string> channels, bool force, CancellationToken ct)
        {
            _seasonCache.Clear();
            var run = new ScanRun { State = state };
            state.ChannelsTotal = channels.Count;
            await _lib.SaveScanState(state);
            try
            {
                var entries = await FindChangesAsync(channels, force, run, ct);
                var groups = GroupByTitle(entries);
                state.FilesToProcess = entries.Count;
                await IdentifyAsync(groups, run, ct);
                if (!run.Excluded.IsEmpty)
                    state.Error = string.Join(" ", run.Excluded.Values);
            }
            catch (OperationCanceledException)
            {
                state.Cancelled = true;
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
                // Also after an abort: an incremental scan never comes back to
                // what this one already matched
                try
                {
                    await RecountAsync(run.Touched);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not update the counts of the scanned items");
                    state.Error ??= ex.Message;
                }
                state.Running = false;
                state.CurrentChannel = null;
                state.FinishedAt = DateTime.UtcNow;
                _seasonCache.Clear();
                await _lib.SaveScanState(state);
                _logger.LogInformation("Library scan finished in {Seconds:F1}s: {Matched} matched, {Review} to review, {Unmatched} unmatched, {Failed} failed",
                    (state.FinishedAt - state.StartedAt)?.TotalSeconds ?? 0, state.Matched, state.Review, state.Unmatched, state.Failed);
                if (run.Touched.Count > 0)
                {
                    try
                    {
                        ScanFinished?.Invoke(run.Touched.ToList());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "A scan-finished handler failed");
                    }
                }
            }
        }

        /// <summary>
        /// Compares the channel indexes with the library: drops the files that
        /// are gone, stores the ignored ones and returns those to identify, in
        /// channel order.
        /// </summary>
        private async Task<List<ScanEntry>> FindChangesAsync(List<string> channels, bool force, ScanRun run, CancellationToken ct)
        {
            var perChannel = new List<ScanEntry>[channels.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, channels.Count), new ParallelOptions { MaxDegreeOfParallelism = ChannelParallelism, CancellationToken = ct }, async (i, token) =>
            {
                perChannel[i] = new List<ScanEntry>();
                run.Update(st => st.CurrentChannel = channels[i]);
                try
                {
                    perChannel[i] = await FindChangesAsync(channels[i], force, run, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Library scan of channel {Channel} failed", channels[i]);
                    run.Update(st => st.Failed++);
                }
                if (perChannel[i].Count == 0)
                    run.Update(st => st.ChannelsScanned++);
            });
            return perChannel.SelectMany(e => e).ToList();
        }

        private async Task<List<ScanEntry>> FindChangesAsync(string channelId, bool force, ScanRun run, CancellationToken ct)
        {
            var entries = new List<ScanEntry>();
            if (!long.TryParse(channelId, out var chId)) return entries;

            var docs = (await _lib.GetChannelVideos(channelId, VideoExtensions)).Where(d => d.IsFile && IsVideo(d.Type)).ToList();
            var existing = (await _lib.GetFilesByChannel(chId)).ToDictionary(f => f.FileId);
            var limits = run.State.Folders?.Where(f => f.ChannelId == chId).Select(f => f.Path).ToList();
            bool InScope(BsonFileManagerModel doc) => limits == null || limits.Any(path => LibraryFolderRules.IsWithin(doc.FilterPath, path));
            run.Update(st => st.FilesSeen += docs.Count(InScope));

            var present = docs.Select(d => d.Id).ToHashSet();
            var gone = existing.Values.Where(f => !present.Contains(f.FileId)).ToList();
            if (gone.Count > 0)
            {
                await _lib.DeleteFiles(chId, gone.Select(f => f.FileId));
                foreach (var file in gone)
                {
                    if (file.ItemId != null) run.Touch(file.ItemId);
                    existing.Remove(file.FileId);
                }
                run.Update(st => st.FilesRemoved += gone.Count);
            }

            var config = GeneralConfigStatic.config;
            var ignored = new List<LibraryFile>();
            var pins = new Dictionary<LibraryFolderRule, PinnedTitle>();
            foreach (var doc in docs)
            {
                ct.ThrowIfCancellationRequested();
                if (!InScope(doc)) continue;
                existing.TryGetValue(doc.Id, out var old);
                var rule = LibraryFolderRules.ResolveRule(config, chId, doc.FilterPath);
                var hint = rule?.Kind ?? LibraryFolderRules.HintFromPath(doc.FilterPath);
                if (old != null && !ShouldRescan(old, force, hint, rule?.TitleKey)) continue;

                PinnedTitle? pin = null;
                if (rule?.TitleKey is string title && !pins.TryGetValue(rule, out pin))
                    pins[rule] = pin = new PinnedTitle(rule, title, IsContinuous(config, rule, chId, docs));

                if (hint == LibraryRuleKind.Ignore)
                {
                    // Folder rule: not library material (extras, trailers...)
                    var file = NewFile(chId, doc, old, MediaNameParser.Parse(doc.Name, doc.FilterPath), null, hint, null);
                    file.Status = LibraryFileStatus.Ignored;
                    ignored.Add(file);
                    if (old?.ItemId != null) run.Touch(old.ItemId);
                    continue;
                }
                entries.Add(new ScanEntry(chId, doc, MediaNameParser.Parse(doc.Name, doc.FilterPath, null, hint), old, hint, pin));
            }
            if (ignored.Count > 0)
                await _lib.UpsertFiles(ignored);
            lock (run.Gate) run.Remaining[chId] = entries.Count;
            return entries;
        }

        /// <summary>
        /// Whether the files of a folder assigned to a series are numbered from
        /// 1 on without seasons, so 102 is the episode 102 and not 1x02. It looks
        /// at every file of the folder: the new ones alone do not tell.
        /// </summary>
        private static bool IsContinuous(GeneralConfig config, LibraryFolderRule rule, long channelId, List<BsonFileManagerModel> docs)
        {
            var titles = new[] { rule.Title };
            return docs.Any(d => ReferenceEquals(LibraryFolderRules.ResolveRule(config, channelId, d.FilterPath), rule)
                && SeriesFolderParser.Read(d.Name, d.FilterPath, rule.Path, titles) is { IsLoose: true, Number: < 100 });
        }

        /// <summary>The files of one title, from any channel: identified with a single search, or none when the folder says the title.</summary>
        private static List<List<ScanEntry>> GroupByTitle(List<ScanEntry> entries) =>
            entries.GroupBy(e => e.Pin != null ? $"={e.Pin.Key}" : $"{(e.Parsed.IsSeries ? LibraryKind.Series : LibraryKind.Movie)}|{MediaNameParser.NormalizeForMatch(e.Parsed.Title)}|{e.Parsed.Year}")
                .Select(g => g.ToList())
                .ToList();

        private async Task IdentifyAsync(List<List<ScanEntry>> groups, ScanRun run, CancellationToken ct)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                await Parallel.ForEachAsync(groups, new ParallelOptions { MaxDegreeOfParallelism = TitleParallelism, CancellationToken = stop.Token }, async (group, token) =>
                {
                    run.Update(st => st.CurrentChannel = group[0].ChannelId.ToString());
                    try
                    {
                        await ProcessGroupAsync(group, run, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (ProviderException ex) when (IsRefusal(ex))
                    {
                        // No point continuing with a bad key or an exhausted quota
                        lock (run.Gate) run.Fatal ??= ex;
                        stop.Cancel();
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Nothing stored: the next scan tries these files again
                        _logger.LogError(ex, "Library scan of '{Title}' failed", group[0].Parsed.Title);
                        run.Update(st => st.Failed += group.Count);
                    }
                    run.FilesDone(group);
                    await HousekeepingAsync(run);
                });
            }
            catch (OperationCanceledException) when (run.Fatal != null)
            {
                // Stopped by the failure rethrown below
            }
            if (run.Fatal != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(run.Fatal);
            ct.ThrowIfCancellationRequested();
        }

        /// <summary>Saves the progress and updates the counts of the items found so far, every so often.</summary>
        private async Task HousekeepingAsync(ScanRun run)
        {
            var now = DateTime.UtcNow;
            if (now - run.LastSave < SaveStateInterval || !await run.Housekeeping.WaitAsync(0)) return;
            try
            {
                run.LastSave = now;
                LibraryScanState state;
                List<string> items = new();
                lock (run.Gate)
                {
                    state = run.State.Clone();
                    if (now - run.LastRecount >= RecountInterval)
                    {
                        run.LastRecount = now;
                        items = run.ToRecount.ToList();
                        run.ToRecount.Clear();
                    }
                }
                await _lib.SaveScanState(state);
                // An item left without files may be about to get some: only the end of the scan removes it
                if (items.Count > 0)
                    await RecountAsync(items, removeEmpty: false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not save the progress of the library scan");
            }
            finally
            {
                run.Housekeeping.Release();
            }
        }

        private static bool IsRefusal(Exception ex) => ex is ProviderException { StatusCode: 401 or 403 or 429 };

        /// <summary>
        /// Whether the scan can go on without <paramref name="provider"/>, which
        /// refused a request: yes for a secondary one, which is left out of the
        /// rest of the scan; no for the first, or the others would take over the
        /// identification of everything.
        /// </summary>
        private bool TryExclude(ScanRun run, IMetadataProvider provider, Exception refusal)
        {
            if (_providers.GetEnabled().FirstOrDefault()?.Id == provider.Id) return false;
            if (run.Excluded.TryAdd(provider.Id, $"{refusal.Message}. The scan went on without {provider.Descriptor.Name}."))
                _logger.LogWarning("Library scan: {Provider} is left out: {Error}", provider.Id, refusal.Message);
            return true;
        }

        private static bool ShouldRescan(LibraryFile old, bool force, string? hint, string? title)
        {
            if (old.Locked) return false;
            // A folder rule that changed since the file was scanned re-decides it
            if (!string.Equals(old.RuleKind, hint, StringComparison.Ordinal)) return true;
            if (!string.Equals(old.RuleTitle, title, StringComparison.Ordinal)) return true;
            if (old.Status == LibraryFileStatus.Ignored) return false;
            if (force) return true;
            // Unmatched files get another chance every scan (a new provider or a
            // cache refresh may know them now); matched ones are left alone.
            return old.Status == LibraryFileStatus.Unmatched;
        }

        private static LibraryFile NewFile(long channelId, BsonFileManagerModel doc, LibraryFile? old, ParsedName parsed, string? kind, string? hint, string? title) => new()
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
            RuleTitle = title,
            ScannedAt = DateTime.UtcNow
        };

        private async Task ProcessGroupAsync(List<ScanEntry> files, ScanRun run, CancellationToken ct)
        {
            var sample = files[0].Parsed;
            var pin = files[0].Pin;
            var kind = sample.IsSeries ? LibraryKind.Series : LibraryKind.Movie;
            LibraryItem? item = null;
            MatchResult? best = null;
            string? error = null;

            if (pin != null)
            {
                try
                {
                    item = await ResolvePinnedAsync(pin.Rule, run, ct);
                    best = new MatchResult { Score = 1 };
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !IsRefusal(ex))
                {
                    _logger.LogWarning(ex, "Could not get the title assigned to the folder {Folder}: {Title}", pin.Rule.Path, pin.Rule.Title);
                    error = ex.Message;
                    run.Update(st => st.Failed += files.Count);
                }
            }
            else if (sample.HasTitle && !MediaNameParser.IsJunk(sample.Title))
            {
                try
                {
                    var (provider, result) = await FindBestAsync(kind, sample, run, ct);
                    if (provider != null && result != null && result.Status != LibraryFileStatus.Unmatched)
                    {
                        try
                        {
                            item = await ResolveItemAsync(provider, kind, result.Candidate, run, ct);
                            best = result;
                        }
                        catch (ProviderException ex) when (IsRefusal(ex) && TryExclude(run, provider, ex))
                        {
                            error = ex.Message;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !IsRefusal(ex))
                {
                    _logger.LogWarning(ex, "Could not identify '{Title}'", sample.Title);
                    error = ex.Message;
                    run.Update(st => st.Failed += files.Count);
                }
            }

            var results = new List<LibraryFile>();
            var stored = new List<LibraryFile>();
            foreach (var entry in files)
            {
                var (parsed, old) = (entry.Parsed, entry.Old);
                if (entry.Pin != null)
                    ReadEpisode(entry, item);
                var file = NewFile(entry.ChannelId, entry.Doc, old, parsed, kind, entry.Hint, entry.Pin?.Key);
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
                            LibraryEpisode? episode;
                            try
                            {
                                episode = await EnsureEpisodeAsync(item, file.Season.Value, parsed.Episode.Value, ct);
                            }
                            catch (ProviderException ex) when (IsRefusal(ex) && ProviderFor(item) is { } source && TryExclude(run, source.Provider, ex))
                            {
                                // Without status: these files fail, the scan goes on
                                throw new ProviderException(ex.Message, null, ex);
                            }
                            if (episode == null && file.Status == LibraryFileStatus.Matched)
                                file.Status = LibraryFileStatus.Review;
                        }
                    }
                }
                results.Add(file);
                // Still unknown and nothing else changed: not worth a write
                if (old == null || !SameOutcome(old, file))
                    stored.Add(file);
            }

            if (stored.Count > 0)
            {
                await _lib.UpsertFiles(stored);
                await LinkWatchAsync(stored);
            }
            foreach (var entry in files)
                if (entry.Old?.ItemId != null && entry.Old.ItemId != item?.Id) run.Touch(entry.Old.ItemId);
            if (item != null) run.Touch(item.Id);

            run.Update(st =>
            {
                st.FilesNew += files.Count(f => f.Old == null);
                st.Matched += results.Count(f => f.Status == LibraryFileStatus.Matched);
                st.Review += results.Count(f => f.Status == LibraryFileStatus.Review);
                st.Unmatched += results.Count(f => f.Status == LibraryFileStatus.Unmatched);
            });
        }

        private static bool SameOutcome(LibraryFile old, LibraryFile now) =>
            old.Status == LibraryFileStatus.Unmatched && now.Status == LibraryFileStatus.Unmatched
            && old.ItemId == null && now.ItemId == null
            && old.FileName == now.FileName && old.FolderPath == now.FolderPath && old.Type == now.Type
            && old.Size == now.Size && old.MessageId == now.MessageId && old.IsSplit == now.IsSplit
            && old.Kind == now.Kind && old.RuleKind == now.RuleKind && old.RuleTitle == now.RuleTitle && old.Error == now.Error
            && old.Parsed.Title == now.Parsed.Title && old.Parsed.Year == now.Parsed.Year
            && old.Parsed.Season == now.Parsed.Season && old.Parsed.Episode == now.Parsed.Episode
            && old.Parsed.EpisodeEnd == now.Parsed.EpisodeEnd && old.Parsed.IsSeries == now.Parsed.IsSeries;

        /// <summary>Title, season and episode of a file whose folder is assigned to a series.</summary>
        private static void ReadEpisode(ScanEntry entry, LibraryItem? item)
        {
            var (rule, parsed) = (entry.Pin!.Rule, entry.Parsed);
            var read = SeriesFolderParser.Read(entry.Doc.Name, entry.Doc.FilterPath, rule.Path, new[] { rule.Title, item?.Title, item?.OriginalTitle });
            var (season, episode) = SeriesFolderParser.Map(read, item?.Seasons ?? new List<LibrarySeason>(), entry.Pin.Continuous);
            parsed.IsSeries = true;
            parsed.Title = item?.Title ?? rule.Title ?? parsed.Title;
            parsed.Year = item?.Year ?? rule.Year;
            parsed.Source = "rule";
            parsed.Season = season;
            parsed.Episode = episode;
            parsed.EpisodeEnd = episode + (read.End - read.Number);
        }

        /// <summary>The series a folder rule names. Only asks its provider when the library does not have it yet.</summary>
        private async Task<LibraryItem> ResolvePinnedAsync(LibraryFolderRule rule, ScanRun run, CancellationToken ct)
        {
            var existing = await FindExistingAsync(rule.Provider!, LibraryKind.Series, rule.ProviderId!, null);
            if (existing != null) return existing;
            if (run.Excluded.TryGetValue(rule.Provider!, out var reason))
                throw new ProviderException(reason);
            var provider = _providers.Get(rule.Provider!) ?? throw new ProviderException($"Provider '{rule.Provider}' is not enabled");
            try
            {
                return await ResolveItemAsync(provider, LibraryKind.Series, CandidateFor(provider, LibraryKind.Series, rule.ProviderId!), run, ct);
            }
            catch (ProviderException ex) when (IsRefusal(ex) && TryExclude(run, provider, ex))
            {
                throw new ProviderException(ex.Message, null, ex);
            }
        }

        /// <summary>A candidate known only by its id, picked by the user.</summary>
        public static ProviderCandidate CandidateFor(IMetadataProvider provider, string kind, string providerId) => new()
        {
            Provider = provider.Id,
            ProviderId = providerId,
            Kind = kind,
            ImdbId = provider.Id == OmdbProvider.ProviderId ? providerId : null
        };

        private async Task<(IMetadataProvider? Provider, MatchResult? Result)> FindBestAsync(string kind, ParsedName parsed, ScanRun run, CancellationToken ct)
        {
            IMetadataProvider? bestProvider = null;
            MatchResult? best = null;
            foreach (var provider in _providers.GetEnabled())
            {
                if (run.Excluded.ContainsKey(provider.Id)) continue;
                try
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
                }
                catch (ProviderException ex) when (IsRefusal(ex) && TryExclude(run, provider, ex))
                {
                    continue;
                }
                if (best != null && best.Score >= LibraryMatcher.MatchedThreshold)
                    break;
            }
            return (bestProvider, best);
        }

        /// <summary>Keeps a file's watch state pointing at the item it is now linked to.</summary>
        public Task LinkWatchAsync(LibraryFile file) => LinkWatchAsync(new[] { file });

        private async Task LinkWatchAsync(IReadOnlyCollection<LibraryFile> files)
        {
            var watches = (await _lib.GetWatchMany(files.Select(f => f.Key))).ToDictionary(w => w.Id);
            if (watches.Count == 0) return;
            foreach (var file in files)
            {
                if (!watches.TryGetValue(file.Key, out var watch)) continue;
                if (watch.ItemId == file.ItemId && watch.Season == file.Season && watch.Episode == file.Episode && watch.FileName == file.FileName) continue;
                watch.ItemId = file.ItemId;
                watch.Season = file.Season;
                watch.Episode = file.Episode;
                watch.FileName ??= file.FileName;
                await _lib.UpsertWatch(watch);
            }
        }

        #endregion

        #region Items and episodes (shared with the manual actions)

        /// <summary>
        /// The library item for a provider hit: an existing one (matched by
        /// provider id or IMDb id) or a freshly fetched one enriched by the
        /// other providers. With <paramref name="refresh"/> the metadata is
        /// fetched again even when the item exists.
        /// </summary>
        public Task<LibraryItem> ResolveItemAsync(IMetadataProvider provider, string kind, ProviderCandidate candidate, CancellationToken ct, bool refresh = false) =>
            ResolveItemAsync(provider, kind, candidate, null, ct, refresh);

        private async Task<LibraryItem> ResolveItemAsync(IMetadataProvider provider, string kind, ProviderCandidate candidate, ScanRun? run, CancellationToken ct, bool refresh = false)
        {
            using var _ = await _itemLocks.LockAsync($"{provider.Id}:{kind}:{candidate.ProviderId}", ct);

            var existing = await FindExistingAsync(provider.Id, kind, candidate.ProviderId, candidate.ImdbId);
            if (existing != null && !refresh)
                return existing;

            var fetched = await provider.GetItemAsync(kind, candidate.ProviderId, ct)
                ?? throw new ProviderException($"{provider.Descriptor.Name} has no {kind} with id {candidate.ProviderId}");
            fetched.ExternalIds.TryAdd(ExternalKeyFor(provider.Id), candidate.ProviderId);

            foreach (var other in _providers.GetEnabled().Where(p => p.Id != provider.Id))
            {
                if (run?.Excluded.ContainsKey(other.Id) == true) continue;
                try
                {
                    await other.EnrichAsync(fetched, ct);
                }
                catch (ProviderException ex) when (IsRefusal(ex))
                {
                    if (run == null || !TryExclude(run, other, ex)) throw;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "{Provider} could not enrich '{Title}'", other.Id, fetched.Title);
                }
            }

            // The same title may already exist through an id we only learnt now,
            // or be created right now through another provider
            await _itemWriteLock.WaitAsync(ct);
            try
            {
                existing ??= await FindExistingAsync(provider.Id, kind, candidate.ProviderId, fetched.ImdbId);
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
            }
            finally
            {
                _itemWriteLock.Release();
            }

            if (refresh)
            {
                await _lib.DeleteEpisodes(fetched.Id);
                foreach (var key in _seasonCache.Keys.Where(k => k.StartsWith(fetched.Id + ":")).ToList())
                    _seasonCache.TryRemove(key, out var _removed);
            }
            return fetched;
        }

        private async Task<LibraryItem?> FindExistingAsync(string provider, string kind, string providerId, string? imdbId)
        {
            // TMDB numbers movies and series separately: the same id is two titles
            var key = ExternalKeyFor(provider);
            var scope = key == "imdb" ? null : kind;
            var existing = await _lib.FindItemByProvider(provider, providerId, scope)
                ?? await _lib.FindItemByExternalId(key, providerId, scope);
            if (existing == null && !string.IsNullOrEmpty(imdbId))
                existing = await _lib.FindItemByExternalId("imdb", imdbId);
            return existing;
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
            using var _ = await _seasonLocks.LockAsync(cacheKey, ct);
            if (_seasonCache.TryGetValue(cacheKey, out cached)) return cached;

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

        /// <summary>
        /// Recomputes file counts and channel lists; items left without files
        /// are removed, unless <paramref name="removeEmpty"/> is false.
        /// </summary>
        public async Task RecountAsync(IEnumerable<string> itemIds, bool removeEmpty = true)
        {
            foreach (var ids in itemIds.Distinct().ToList().Chunk(200))
            {
                var files = (await _lib.GetFilesByItems(ids)).Where(f => f.Status != LibraryFileStatus.Ignored).ToLookup(f => f.ItemId!);
                var changed = new List<LibraryItem>();
                foreach (var item in await _lib.GetItems(ids))
                {
                    var own = files[item.Id].ToList();
                    if (own.Count == 0)
                    {
                        if (removeEmpty) await _lib.DeleteItem(item.Id);
                        continue;
                    }
                    item.FileCount = own.Count;
                    item.ChannelIds = own.Select(f => f.ChannelId).Distinct().OrderBy(c => c).ToList();
                    item.EpisodeCount = item.Kind == LibraryKind.Series
                        ? own.Where(f => f.Episode.HasValue).Select(f => (f.Season ?? 1, f.Episode!.Value)).Distinct().Count()
                        : 0;
                    changed.Add(item);
                }
                if (changed.Count > 0)
                    await _lib.UpdateItemCounts(changed);
            }
        }

        #endregion
    }
}
