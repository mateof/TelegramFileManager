using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Services.Api
{
    /// <summary>
    /// Picks the Telegram photo size used as a thumbnail. Telegram names its
    /// sizes with one letter: <c>s</c> 100px, <c>m</c> 320px, <c>x</c> 800px,
    /// <c>y</c> 1280px, <c>w</c> 2560px, <c>a</c>-<c>d</c> square crops, and
    /// <c>i</c> (stripped) / <c>j</c> (outline) which are not real images.
    /// </summary>
    public static class ThumbnailSizePicker
    {
        /// <summary>Preference order: big enough for a grid tile, small enough to be quick.</summary>
        public static readonly IReadOnlyList<string> Preference = new[] { "m", "x", "s", "d", "c", "b", "a", "y", "w" };

        public static T? Pick<T>(IEnumerable<T>? sizes, Func<T, string?> typeOf) where T : class
        {
            if (sizes == null) return null;
            var list = sizes.Where(s => s != null).ToList();
            foreach (var wanted in Preference)
            {
                var hit = list.FirstOrDefault(s => string.Equals(typeOf(s), wanted, StringComparison.Ordinal));
                if (hit != null) return hit;
            }
            // Unknown letters: anything that is not a stripped/outline preview.
            return list.FirstOrDefault(s => typeOf(s) is string t && t != "i" && t != "j");
        }
    }

    /// <summary>A single byte range resolved against a known length.</summary>
    public readonly record struct ByteRange(long From, long To)
    {
        public long Length => To - From + 1;
    }

    /// <summary>Outcome of parsing a <c>Range</c> header.</summary>
    public enum RangeKind
    {
        /// <summary>No (usable) range: send the whole entity with 200.</summary>
        None,
        /// <summary>One satisfiable range: send 206.</summary>
        Partial,
        /// <summary>Outside the entity: send 416.</summary>
        Unsatisfiable
    }

    /// <summary>
    /// RFC 7233 single-range parsing (<c>bytes=a-b</c>, <c>bytes=a-</c>,
    /// <c>bytes=-n</c>). Multi-range and malformed headers are ignored (whole
    /// entity), as servers are allowed to do.
    /// </summary>
    public static class HttpRangeParser
    {
        public static RangeKind Parse(string? header, long totalLength, out ByteRange range)
        {
            range = new ByteRange(0, Math.Max(0, totalLength - 1));
            if (string.IsNullOrWhiteSpace(header)) return RangeKind.None;

            var value = header.Trim();
            if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return RangeKind.None;
            value = value["bytes=".Length..].Trim();
            if (value.Contains(',')) return RangeKind.None;

            var dash = value.IndexOf('-');
            if (dash < 0) return RangeKind.None;
            var startText = value[..dash].Trim();
            var endText = value[(dash + 1)..].Trim();

            if (totalLength <= 0) return RangeKind.Unsatisfiable;

            if (startText.Length == 0)
            {
                // Suffix: the last N bytes.
                if (!long.TryParse(endText, out var suffix) || suffix < 0) return RangeKind.None;
                if (suffix == 0) return RangeKind.Unsatisfiable;
                range = new ByteRange(Math.Max(0, totalLength - suffix), totalLength - 1);
                return RangeKind.Partial;
            }

            if (!long.TryParse(startText, out var from) || from < 0) return RangeKind.None;
            long to = totalLength - 1;
            if (endText.Length > 0)
            {
                if (!long.TryParse(endText, out to) || to < from) return RangeKind.None;
                to = Math.Min(to, totalLength - 1);
            }
            if (from >= totalLength) return RangeKind.Unsatisfiable;

            range = new ByteRange(from, to);
            return RangeKind.Partial;
        }
    }

    /// <summary>
    /// Where a byte of a file split into several Telegram messages lives. The
    /// splitter cuts fixed-size parts, so every part but the last has the size
    /// of the first one.
    /// </summary>
    public static class SplitFileLayout
    {
        /// <summary>Telegram serves file bytes in 512 KB aligned blocks.</summary>
        public const long BlockSize = 524288;

        /// <summary>Index of the part holding <paramref name="position"/> and the offset where that part starts.</summary>
        public static (int PartIndex, long PartStart) Locate(long position, long firstPartSize, int partCount)
        {
            if (partCount <= 1 || firstPartSize <= 0) return (0, 0);
            var index = (int)Math.Min(position / firstPartSize, partCount - 1);
            return (index, index * firstPartSize);
        }

        /// <summary>Rounds a part-local offset down to a Telegram block, returning the bytes to skip.</summary>
        public static (long AlignedOffset, long Skip) Align(long localOffset)
        {
            var aligned = localOffset / BlockSize * BlockSize;
            return (aligned, localOffset - aligned);
        }
    }

    /// <summary>
    /// Builds the stable v1 media URLs placed in <see cref="ApiFileDto"/>. All
    /// of them go through <c>/api/v1</c>, so they share its protection.
    /// </summary>
    public static class MediaUrlBuilder
    {
        public static string Content(string baseUrl, string channelId, string fileId, bool attachment = false, string? collectionId = null)
        {
            var url = $"{baseUrl}/api/v1/channels/{Uri.EscapeDataString(channelId)}/files/{Uri.EscapeDataString(fileId)}/content";
            var query = new List<string>();
            if (attachment) query.Add("disposition=attachment");
            if (!string.IsNullOrEmpty(collectionId)) query.Add("collectionId=" + Uri.EscapeDataString(collectionId));
            return query.Count == 0 ? url : url + "?" + string.Join("&", query);
        }

        public static string Stream(string baseUrl, string channelId, string fileId) =>
            $"{baseUrl}/api/v1/channels/{Uri.EscapeDataString(channelId)}/files/{Uri.EscapeDataString(fileId)}/stream";

        public static string Thumbnail(string baseUrl, string channelId, int messageId) =>
            $"{baseUrl}/api/v1/channels/{Uri.EscapeDataString(channelId)}/messages/{messageId}/thumbnail";

        public static string LocalContent(string baseUrl, string relativePath, bool attachment) =>
            $"{baseUrl}/api/v1/local/download?path={Uri.EscapeDataString(relativePath.Replace("\\", "/"))}" +
            (attachment ? string.Empty : "&disposition=inline");

        /// <summary>Formats that browsers cannot play natively and that the server can transcode.</summary>
        public static bool NeedsTranscoding(string? fileName) =>
            TranscodedExtensions.Contains(System.IO.Path.GetExtension(fileName ?? string.Empty));

        /// <summary>Kept in sync with <c>VideoStreamController</c>.</summary>
        public static readonly HashSet<string> TranscodedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".avi", ".wmv", ".flv", ".mov", ".m4v", ".3gp", ".ts", ".mts", ".m2ts", ".vob", ".divx", ".xvid"
        };

        /// <summary>
        /// Fills the v1 media URLs of a channel (or shared collection) entry.
        /// Thumbnails exist for photos and videos backed by a single message.
        /// </summary>
        public static void Apply(ApiFileDto dto, string baseUrl, string channelId, string? collectionId = null)
        {
            if (!dto.IsFile) return;
            dto.ContentUrl = Content(baseUrl, channelId, dto.Id, attachment: false, collectionId);
            dto.ContentDownloadUrl = Content(baseUrl, channelId, dto.Id, attachment: true, collectionId);

            if (dto.Category == "Video")
                dto.PlaybackUrl = string.IsNullOrEmpty(collectionId) && NeedsTranscoding(dto.Name)
                    ? Stream(baseUrl, channelId, dto.Id)
                    : dto.ContentUrl;
            else if (dto.Category == "Audio")
                dto.PlaybackUrl = dto.ContentUrl;

            if ((dto.Category == "Photo" || dto.Category == "Video") && dto.MessageId.HasValue && !dto.IsSplit)
                dto.ThumbnailUrl = Thumbnail(baseUrl, channelId, dto.MessageId.Value);
        }
    }

    /// <summary>
    /// Path containment for the local file endpoints: every client path must
    /// resolve inside the local root.
    /// </summary>
    public static class LocalPathGuard
    {
        /// <summary>Resolves a client path (relative to <paramref name="root"/>), refusing anything that escapes it.</summary>
        public static bool TryResolve(string root, string? path, out string absolute, out string relative, out string? error)
        {
            absolute = string.Empty;
            relative = string.Empty;
            error = null;

            var candidate = (path ?? string.Empty).Replace("\\", "/").Trim().TrimStart('/');
            if (System.IO.Path.IsPathRooted(candidate) || (OperatingSystem.IsWindows() && candidate.Contains(':')))
            {
                error = "Only paths relative to the local root are accepted";
                return false;
            }

            var rootFull = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root));
            var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(System.IO.Path.Combine(rootFull, candidate)));

            if (!IsSameOrInside(rootFull, full))
            {
                error = "The path escapes the local root";
                return false;
            }

            absolute = full;
            relative = System.IO.Path.GetRelativePath(rootFull, full).Replace("\\", "/");
            if (relative == ".") relative = string.Empty;
            return true;
        }

        /// <summary>True when <paramref name="candidate"/> is <paramref name="folder"/> or lies below it (segment-aware).</summary>
        public static bool IsSameOrInside(string folder, string candidate)
        {
            var f = System.IO.Path.TrimEndingDirectorySeparator(folder);
            var c = System.IO.Path.TrimEndingDirectorySeparator(candidate);
            if (string.Equals(f, c, StringComparison.OrdinalIgnoreCase)) return true;
            return c.StartsWith(f + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || c.StartsWith(f + System.IO.Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Rules of the local copy/move endpoints.</summary>
    public static class LocalCopyMovePlanner
    {
        /// <summary>
        /// False when the entry would land on itself, or a folder would be put
        /// inside itself or one of its sub-folders.
        /// </summary>
        public static bool CanPlace(string source, string destination, bool sourceIsDirectory)
        {
            var s = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(source));
            var d = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(destination));
            if (string.Equals(s, d, StringComparison.OrdinalIgnoreCase)) return false;
            return !(sourceIsDirectory && LocalPathGuard.IsSameOrInside(s, d));
        }
    }

    /// <summary>Cache headers for the SPA served at <c>/app</c>.</summary>
    public static class SpaCachePolicy
    {
        public const string NoCache = "no-cache";
        public const string Immutable = "public, max-age=31536000, immutable";
        public const string Short = "public, max-age=3600";

        /// <summary>
        /// Vite emits content-hashed files under <c>/app/assets/</c>: cache them
        /// forever. <c>index.html</c>, the service worker and the manifest must
        /// be revalidated so a new build is picked up; anything else gets an hour.
        /// </summary>
        public static string For(string? requestPath)
        {
            var path = (requestPath ?? string.Empty).Replace('\\', '/');
            if (path.StartsWith("/app/assets/", StringComparison.OrdinalIgnoreCase)) return Immutable;

            var name = path[(path.LastIndexOf('/') + 1)..];
            if (name.Length == 0 || !name.Contains('.')) return NoCache;
            if (name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                || name.Equals("sw.js", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("workbox-", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("registerSW", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".webmanifest", StringComparison.OrdinalIgnoreCase)
                || name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                return NoCache;
            return Short;
        }
    }
}
