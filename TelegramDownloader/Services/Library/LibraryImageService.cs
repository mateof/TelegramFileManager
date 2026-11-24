using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace TelegramDownloader.Services.Library
{
    /// <summary>Tuning of <see cref="LibraryImageService"/> (the defaults suit the server; tests shrink them).</summary>
    public class LibraryImageOptions
    {
        /// <summary>Where the images are stored. Defaults to <c>datauser/library/images</c> (the persisted volume).</summary>
        public string CacheFolder { get; set; } = LibraryImageService.CacheFolder;
        /// <summary>Downloads from the providers running at the same time, whatever the number of clients.</summary>
        public int MaxConcurrentDownloads { get; set; } = 6;
        /// <summary>Disk budget: past it the least recently served images are deleted.</summary>
        public long MaxCacheBytes { get; set; } = 2L * 1024 * 1024 * 1024;
        /// <summary>Larger responses are refused (a poster is well under 1 MB, an original backdrop a few MB).</summary>
        public long MaxImageBytes { get; set; } = 25L * 1024 * 1024;
        /// <summary>A failed image is not asked to the provider again before this.</summary>
        public TimeSpan FailureTtl { get; set; } = TimeSpan.FromMinutes(10);
        public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromSeconds(60);
    }

    /// <summary>An image ready to be served from disk.</summary>
    public sealed record CachedImage(string Path, string ContentType, long Length, DateTimeOffset LastModified, string ETag);

    /// <summary>
    /// Serves provider images (posters, backdrops, stills) from a local disk
    /// cache so clients only ever talk to this server.
    ///
    /// An image is downloaded once: concurrent requests for the same image
    /// share one download, which is not cancelled when the client that started
    /// it goes away (a lazy image scrolled out of view), so the next request
    /// finds it on disk. Downloads to the providers are bounded, failures are
    /// remembered for a while instead of hammering the provider, and the folder
    /// is trimmed to <see cref="LibraryImageOptions.MaxCacheBytes"/>.
    /// </summary>
    public class LibraryImageService : IDisposable
    {
        public static readonly string[] Sizes = { "w92", "w154", "w185", "w300", "w342", "w500", "w780", "w1280", "original" };

        private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "image.tmdb.org", "m.media-amazon.com", "ia.media-imdb.com", "images-na.ssl-images-amazon.com", "artworks.thetvdb.com"
        };

        /// <summary>Extensions an image may be stored with, the usual one first.</summary>
        private static readonly string[] StoredExtensions = { ".jpg", ".png", ".webp", ".gif", ".avif" };

        private const string PartSuffix = ".part";

        public static string CacheFolder => Path.Combine(UserService.USERDATAFOLDER, "library", "images");

        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<LibraryImageService> _logger;
        private readonly LibraryImageOptions _options;
        private readonly SemaphoreSlim _downloadSlots;
        private readonly CancellationTokenSource _stopping = new();
        private readonly ConcurrentDictionary<string, Lazy<Task<CachedImage?>>> _inFlight = new();
        private readonly ConcurrentDictionary<string, DateTime> _failures = new();
        private long _bytesSinceTrim;
        private int _trimming;

        public LibraryImageService(IHttpClientFactory httpFactory, ILogger<LibraryImageService> logger, LibraryImageOptions? options = null)
        {
            _httpFactory = httpFactory;
            _logger = logger;
            _options = options ?? new LibraryImageOptions();
            _downloadSlots = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentDownloads));
            // Start "over budget" so the first download after a restart checks the folder size
            _bytesSinceTrim = TrimThreshold;
        }

        public string Folder => _options.CacheFolder;

        private long TrimThreshold => Math.Max(1, _options.MaxCacheBytes / 20);

        #region URLs

        /// <summary>URL of this server that serves a cached copy of <paramref name="sourceUrl"/>.</summary>
        public static string? ProxyUrl(string baseUrl, string? sourceUrl, string size)
        {
            if (string.IsNullOrWhiteSpace(sourceUrl)) return null;
            return $"{baseUrl}/api/v1/library/images/{Encode(sourceUrl)}?size={size}";
        }

        public static string Encode(string url) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(url)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static string? Decode(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 2048) return null;
            try
            {
                var s = key.Replace('-', '+').Replace('_', '/');
                s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
                return Encoding.UTF8.GetString(Convert.FromBase64String(s));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Only HTTPS URLs of the known image hosts, on the default port and without credentials.</summary>
        public static bool IsAllowed(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && AllowedHosts.Contains(uri.Host)
            && uri.UserInfo.Length == 0 && uri.IsDefaultPort;

        /// <summary>The size as stored (<c>original</c> when missing). False for a size not in <see cref="Sizes"/>.</summary>
        public static bool TryNormalizeSize(string? size, out string normalized)
        {
            if (string.IsNullOrWhiteSpace(size))
            {
                normalized = "original";
                return true;
            }
            var match = Sizes.FirstOrDefault(s => s.Equals(size.Trim(), StringComparison.OrdinalIgnoreCase));
            normalized = match ?? "original";
            return match != null;
        }

        public static string NormalizeSize(string? size) => TryNormalizeSize(size, out var s) ? s : "original";

        /// <summary>The source URL adjusted to the wanted size (only TMDB supports sizes).</summary>
        public static string SizedUrl(string url, string size)
        {
            const string marker = "/t/p/original/";
            if (url.Contains(marker, StringComparison.Ordinal) && size != "original")
                return url.Replace(marker, $"/t/p/{size}/", StringComparison.Ordinal);
            return url;
        }

        /// <summary>File name (without extension) of a sized source URL: a hash, so nothing of the URL reaches the file system.</summary>
        public static string CacheName(string sizedUrl) =>
            Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sizedUrl))).ToLowerInvariant();

        public static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".avif" => "image/avif",
            _ => "image/jpeg"
        };

        private static string? ExtensionOf(string? contentType) => contentType?.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" or "image/pjpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/avif" => ".avif",
            _ => null
        };

        #endregion

        #region Cache

        /// <summary>
        /// The image, from disk or downloaded now. Null when the source is not
        /// allowed or cannot be fetched (a failure is remembered for
        /// <see cref="LibraryImageOptions.FailureTtl"/>).
        /// <paramref name="ct"/> only stops waiting: a download in progress
        /// carries on for the next request.
        /// </summary>
        public async Task<CachedImage?> GetAsync(string sourceUrl, string size, CancellationToken ct)
        {
            if (!IsAllowed(sourceUrl)) return null;
            var sized = SizedUrl(sourceUrl, size);
            var name = CacheName(sized);

            var cached = FindCached(name, touch: true);
            if (cached != null) return cached;

            if (_failures.TryGetValue(name, out var until))
            {
                if (until > DateTime.UtcNow) return null;
                _failures.TryRemove(name, out _);
            }

            var lazy = new Lazy<Task<CachedImage?>>(() => DownloadAsync(sized, name), LazyThreadSafetyMode.ExecutionAndPublication);
            var shared = _inFlight.GetOrAdd(name, lazy);
            if (ReferenceEquals(shared, lazy))
                _ = lazy.Value.ContinueWith(_ => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<CachedImage?>>>(name, lazy)),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return await shared.Value.WaitAsync(ct);
        }

        /// <summary>Whether the image is already on disk (no download).</summary>
        public bool IsCached(string sourceUrl, string size) =>
            IsAllowed(sourceUrl) && FindCached(CacheName(SizedUrl(sourceUrl, size)), touch: false) != null;

        /// <summary>
        /// Downloads the images not cached yet, a few at a time (the global
        /// download limit applies too). Failures are skipped. Returns how many
        /// were stored.
        /// </summary>
        public async Task<int> PrefetchAsync(IEnumerable<(string Url, string Size)> images, int parallelism, CancellationToken ct)
        {
            var todo = images.Where(i => !string.IsNullOrWhiteSpace(i.Url) && !IsCached(i.Url, i.Size))
                .DistinctBy(i => SizedUrl(i.Url, i.Size)).ToList();
            var fetched = 0;
            await Parallel.ForEachAsync(todo, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism), CancellationToken = ct },
                async (image, token) =>
                {
                    if (await GetAsync(image.Url, image.Size, token) != null) Interlocked.Increment(ref fetched);
                });
            return fetched;
        }

        private CachedImage? FindCached(string name, bool touch)
        {
            foreach (var ext in StoredExtensions)
            {
                var path = Path.Combine(_options.CacheFolder, name + ext);
                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                    if (!info.Exists || info.Length == 0) continue;
                }
                catch
                {
                    continue;
                }
                if (touch) Touch(info);
                return new CachedImage(info.FullName, ContentTypeOf(path), info.Length,
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), $"\"{name}\"");
            }
            return null;
        }

        /// <summary>Marks the file as recently served (at most once a day), for the eviction order.</summary>
        private static void Touch(FileInfo info)
        {
            try
            {
                var now = DateTime.UtcNow;
                if (info.LastAccessTimeUtc < now.AddDays(-1))
                    File.SetLastAccessTimeUtc(info.FullName, now);
            }
            catch
            {
                // Read-only volume, file being deleted...: not worth failing the request
            }
        }

        private async Task<CachedImage?> DownloadAsync(string sizedUrl, string name)
        {
            // Off the caller's stack: requests only wait for this task
            await Task.Yield();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
            timeout.CancelAfter(_options.DownloadTimeout);
            var ct = timeout.Token;
            string? tmp = null;
            var slot = false;
            try
            {
                await _downloadSlots.WaitAsync(ct);
                slot = true;

                // A previous download may have stored it while this one waited
                var existing = FindCached(name, touch: false);
                if (existing != null) return existing;

                var http = _httpFactory.CreateClient("library-images");
                using var response = await http.GetAsync(sizedUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                {
                    // 404 is a genuinely missing image; anything else (429, 5xx, 403...) is worth seeing in the logs
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        _logger.LogDebug("Image {Url} answered {Status}", sizedUrl, (int)response.StatusCode);
                    else
                        _logger.LogWarning("Image {Url} answered {Status}", sizedUrl, (int)response.StatusCode);
                    return Fail(name);
                }
                var ext = ExtensionOf(response.Content.Headers.ContentType?.MediaType);
                if (ext == null)
                {
                    _logger.LogDebug("Image {Url} is not an image ({Type})", sizedUrl, response.Content.Headers.ContentType?.MediaType);
                    return Fail(name);
                }
                if (response.Content.Headers.ContentLength > _options.MaxImageBytes)
                    return Fail(name);

                Directory.CreateDirectory(_options.CacheFolder);
                tmp = Path.Combine(_options.CacheFolder, $"{name}.{Guid.NewGuid():N}{PartSuffix}");
                long written = 0;
                await using (var source = await response.Content.ReadAsStreamAsync(ct))
                await using (var target = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        written += read;
                        if (written > _options.MaxImageBytes)
                        {
                            _logger.LogDebug("Image {Url} is larger than {Max} bytes", sizedUrl, _options.MaxImageBytes);
                            return Fail(name);
                        }
                        await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                }
                if (written == 0) return Fail(name);

                File.Move(tmp, Path.Combine(_options.CacheFolder, name + ext), true);
                tmp = null;
                if (Interlocked.Add(ref _bytesSinceTrim, written) >= TrimThreshold)
                    StartTrim();
                return FindCached(name, touch: false);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                // Timeouts and network errors: the provider is not asked again for a while
                _logger.LogWarning("Could not cache image {Url}: {Error}", sizedUrl, ex.Message);
                return Fail(name);
            }
            finally
            {
                if (slot) _downloadSlots.Release();
                if (tmp != null)
                {
                    try { File.Delete(tmp); } catch { }
                }
            }
        }

        private CachedImage? Fail(string name)
        {
            if (_failures.Count > 10_000) _failures.Clear();
            _failures[name] = DateTime.UtcNow + _options.FailureTtl;
            return null;
        }

        /// <summary>Forgets the remembered failures (after clearing the cache, so everything is asked again).</summary>
        public void ResetFailures() => _failures.Clear();

        private void StartTrim()
        {
            if (Interlocked.Exchange(ref _trimming, 1) == 1) return;
            Interlocked.Exchange(ref _bytesSinceTrim, 0);
            _ = Task.Run(() =>
            {
                try
                {
                    Trim();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not trim the library image cache");
                }
                finally
                {
                    Interlocked.Exchange(ref _trimming, 0);
                }
            });
        }

        /// <summary>
        /// Deletes stale partial downloads and, when the folder is over budget,
        /// the least recently served images until it is at 90% of the budget.
        /// Returns the number of images deleted.
        /// </summary>
        public int Trim()
        {
            var folder = _options.CacheFolder;
            if (!Directory.Exists(folder)) return 0;
            var files = new List<FileInfo>();
            long total = 0;
            foreach (var info in new DirectoryInfo(folder).EnumerateFiles())
            {
                if (info.Name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (info.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1))
                        try { info.Delete(); } catch { }
                    continue;
                }
                files.Add(info);
                total += info.Length;
            }
            if (total <= _options.MaxCacheBytes) return 0;

            var target = _options.MaxCacheBytes * 9 / 10;
            var deleted = 0;
            foreach (var info in files.OrderBy(f => f.LastAccessTimeUtc > f.LastWriteTimeUtc ? f.LastAccessTimeUtc : f.LastWriteTimeUtc))
            {
                if (total <= target) break;
                try
                {
                    var length = info.Length;
                    info.Delete();
                    total -= length;
                    deleted++;
                }
                catch
                {
                    // In use: try the next one
                }
            }
            _logger.LogInformation("Library image cache trimmed: {Deleted} images deleted", deleted);
            return deleted;
        }

        /// <summary>Images stored in a cache folder (default: the server's) and their size, for the image-cache maintenance.</summary>
        public static (int Count, long Bytes) Usage(string? folder = null)
        {
            folder ??= CacheFolder;
            if (!Directory.Exists(folder)) return (0, 0);
            var count = 0;
            long bytes = 0;
            foreach (var info in new DirectoryInfo(folder).EnumerateFiles())
            {
                if (info.Name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                count++;
                bytes += info.Length;
            }
            return (count, bytes);
        }

        #endregion

        /// <summary>Stops the downloads in progress (server shutdown).</summary>
        public void Dispose()
        {
            if (!_stopping.IsCancellationRequested) _stopping.Cancel();
            GC.SuppressFinalize(this);
        }
    }
}
