using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace TelegramDownloader.Services.Library
{
    /// <summary>
    /// Serves provider images (posters, backdrops, stills) from a local disk
    /// cache so clients only ever talk to this server. Images are fetched the
    /// first time they are requested, never during a scan.
    /// </summary>
    public class LibraryImageService
    {
        public static readonly string[] Sizes = { "w92", "w154", "w185", "w300", "w342", "w500", "w780", "w1280", "original" };

        private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "image.tmdb.org", "m.media-amazon.com", "ia.media-imdb.com", "images-na.ssl-images-amazon.com", "artworks.thetvdb.com"
        };

        public static string CacheFolder => Path.Combine(UserService.USERDATAFOLDER, "library", "images");

        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<LibraryImageService> _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        public LibraryImageService(IHttpClientFactory httpFactory, ILogger<LibraryImageService> logger)
        {
            _httpFactory = httpFactory;
            _logger = logger;
        }

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

        public static bool IsAllowed(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && AllowedHosts.Contains(uri.Host);

        public static string NormalizeSize(string? size) =>
            size != null && Sizes.Contains(size, StringComparer.OrdinalIgnoreCase) ? size.ToLowerInvariant() : "original";

        /// <summary>The source URL adjusted to the wanted size (only TMDB supports sizes).</summary>
        public static string SizedUrl(string url, string size)
        {
            const string marker = "/t/p/original/";
            if (url.Contains(marker, StringComparison.Ordinal) && size != "original")
                return url.Replace(marker, $"/t/p/{size}/", StringComparison.Ordinal);
            return url;
        }

        /// <summary>Local path of the cached image, downloading it when needed. Null when it cannot be fetched.</summary>
        public async Task<string?> GetLocalPathAsync(string sourceUrl, string size, CancellationToken ct)
        {
            if (!IsAllowed(sourceUrl)) return null;
            var sized = SizedUrl(sourceUrl, size);
            var ext = Path.GetExtension(new Uri(sized).AbsolutePath);
            if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = ".jpg";
            var name = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sized))).ToLowerInvariant() + ext;
            var folder = CacheFolder;
            var path = Path.Combine(folder, name);
            if (File.Exists(path)) return path;

            var gate = _locks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                if (File.Exists(path)) return path;
                Directory.CreateDirectory(folder);
                var http = _httpFactory.CreateClient("library-images");
                using var response = await http.GetAsync(sized, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Image {Url} answered {Status}", sized, (int)response.StatusCode);
                    return null;
                }
                var tmp = path + ".part";
                await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    await response.Content.CopyToAsync(fs, ct);
                File.Move(tmp, path, true);
                return path;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not cache image {Url}", sized);
                return null;
            }
            finally
            {
                gate.Release();
                _locks.TryRemove(name, out _);
            }
        }

        public static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
    }
}
