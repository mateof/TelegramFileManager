using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Services.Player
{
    /// <summary>
    /// The pure rules of the shared player data: track keys, validation, the
    /// "finished" rule of resume positions, history trimming and the most
    /// played ranking. No Mongo, no clock: the tests exercise them directly.
    /// </summary>
    public static class PlayerDataRules
    {
        public const string ChannelPrefix = "channel:";
        public const string LocalPrefix = "local:";

        /// <summary>History entries kept; older plays are deleted.</summary>
        public const int MaxHistoryEntries = 2000;

        /// <summary>Range of every <c>limit</c> / <c>pageSize</c> parameter.</summary>
        public const int MinLimit = 1;
        public const int MaxLimit = 500;

        /// <summary>A position this close to the end (seconds) counts as finished.</summary>
        public const double FinishedTailSeconds = 15;

        /// <summary>A position past this fraction of the duration counts as finished.</summary>
        public const double FinishedFraction = 0.98;

        public static string ChannelKey(string channelId, string fileId) => $"{ChannelPrefix}{channelId.Trim()}:{fileId.Trim()}";

        public static string LocalKey(string path) => $"{LocalPrefix}{NormalizeLocalPath(path)}";

        /// <summary>
        /// The canonical form of a local path in keys: <c>/</c> separators, no
        /// surrounding blanks and no leading slash (the form every
        /// <c>/api/v1/local</c> endpoint accepts). The case is kept.
        /// </summary>
        public static string NormalizeLocalPath(string path) => path.Replace('\\', '/').Trim().TrimStart('/');

        /// <summary>
        /// Splits a key into its parts. False for anything that is not
        /// <c>channel:{channelId}:{fileId}</c> or <c>local:{path}</c> with non-empty parts.
        /// </summary>
        public static bool TryParseKey(string? key, out string? channelId, out string? fileId, out string? localPath)
        {
            channelId = fileId = localPath = null;
            if (string.IsNullOrWhiteSpace(key)) return false;
            key = key.Trim();

            if (key.StartsWith(ChannelPrefix, StringComparison.Ordinal))
            {
                var rest = key.Substring(ChannelPrefix.Length);
                var sep = rest.IndexOf(':');
                if (sep <= 0 || sep == rest.Length - 1) return false;
                channelId = rest.Substring(0, sep).Trim();
                fileId = rest.Substring(sep + 1).Trim();
                if (channelId.Length == 0 || fileId.Length == 0)
                {
                    channelId = fileId = null;
                    return false;
                }
                return true;
            }

            if (key.StartsWith(LocalPrefix, StringComparison.Ordinal))
            {
                var path = NormalizeLocalPath(key.Substring(LocalPrefix.Length));
                if (path.Length == 0) return false;
                localPath = path;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The canonical form of a key sent by a client (e.g. in <c>?key=</c>):
        /// parsed and rebuilt, so <c>local:/a\b.mp3</c> matches <c>local:a/b.mp3</c>.
        /// Null when it is not a valid key.
        /// </summary>
        public static string? NormalizeKey(string? key)
        {
            if (!TryParseKey(key, out var channelId, out var fileId, out var localPath)) return null;
            return localPath != null ? LocalKey(localPath) : ChannelKey(channelId!, fileId!);
        }

        /// <summary>
        /// Validates a track of a request body and returns a clean copy with its
        /// key computed. Channel tracks need <c>channelId</c> + <c>fileId</c>,
        /// local files need <c>filePath</c> (<c>isLocalFile</c> forces the local
        /// form). When neither is given a valid <c>key</c> is used instead.
        /// </summary>
        public static bool TryNormalizeTrack(ApiPlayerTrackDto? track, out ApiPlayerTrackDto normalized, out string? error)
        {
            normalized = new ApiPlayerTrackDto();
            error = null;
            if (track == null)
            {
                error = "A track is required";
                return false;
            }

            var channelId = Blank(track.ChannelId);
            var fileId = Blank(track.FileId);
            var filePath = Blank(track.FilePath);
            var local = track.IsLocalFile || (filePath != null && (channelId == null || fileId == null));

            if (local && filePath == null && TryParseKey(track.Key, out _, out _, out var keyPath) && keyPath != null)
                filePath = keyPath;
            if (!local && (channelId == null || fileId == null) && TryParseKey(track.Key, out var keyChannel, out var keyFile, out var keyLocal))
            {
                if (keyLocal != null)
                {
                    local = true;
                    filePath = keyLocal;
                }
                else
                {
                    channelId = keyChannel;
                    fileId = keyFile;
                }
            }

            if (local)
            {
                if (filePath == null || NormalizeLocalPath(filePath).Length == 0)
                {
                    error = "A local track needs a filePath";
                    return false;
                }
                filePath = NormalizeLocalPath(filePath);
                normalized.Key = LocalKey(filePath);
                normalized.IsLocalFile = true;
                normalized.FilePath = filePath;
            }
            else
            {
                if (channelId == null || fileId == null)
                {
                    error = "A track needs channelId and fileId, or a local filePath";
                    return false;
                }
                normalized.Key = ChannelKey(channelId, fileId);
                normalized.ChannelId = channelId;
                normalized.FileId = fileId;
                normalized.FilePath = filePath;
            }

            if (track.Duration is < 0 || track.FileSize is < 0)
            {
                error = "duration and fileSize cannot be negative";
                return false;
            }

            normalized.FileName = Blank(track.FileName) ?? FileNameOf(filePath) ?? string.Empty;
            normalized.ChannelName = Blank(track.ChannelName);
            normalized.Title = Blank(track.Title);
            normalized.Artist = Blank(track.Artist);
            normalized.Album = Blank(track.Album);
            normalized.Duration = track.Duration;
            normalized.FileSize = track.FileSize;
            return true;
        }

        /// <summary>
        /// True when a resume position means the track was finished: in the last
        /// 2% or the last 15 seconds. Unknown durations (0) are never finished.
        /// </summary>
        public static bool IsFinished(double positionSeconds, double durationSeconds)
        {
            if (durationSeconds <= 0) return false;
            return positionSeconds >= durationSeconds * FinishedFraction
                || positionSeconds >= durationSeconds - FinishedTailSeconds;
        }

        /// <summary>How many of the oldest entries to delete so at most <paramref name="max"/> remain.</summary>
        public static int HistoryExcess(long count, int max = MaxHistoryEntries) => count > max ? (int)Math.Min(int.MaxValue, count - max) : 0;

        public static bool IsValidLimit(int value) => value >= MinLimit && value <= MaxLimit;

        /// <summary>
        /// Most played tracks: plays grouped by key, sorted by play count then
        /// by the latest play, each with the snapshot of its latest play.
        /// </summary>
        public static List<ApiPlayerTopTrackDto> TopPlayed(IEnumerable<(ApiPlayerTrackDto Track, DateTime PlayedAt)> plays, int limit)
        {
            return plays
                .Where(p => !string.IsNullOrEmpty(p.Track?.Key))
                .GroupBy(p => p.Track.Key, StringComparer.Ordinal)
                .Select(g =>
                {
                    var latest = g.OrderByDescending(p => p.PlayedAt).First();
                    return new ApiPlayerTopTrackDto { Track = latest.Track, PlayCount = g.Count(), LastPlayedAt = latest.PlayedAt };
                })
                .OrderByDescending(t => t.PlayCount)
                .ThenByDescending(t => t.LastPlayedAt)
                .ThenBy(t => t.Track.Key, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
        }

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? FileNameOf(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var name = path.Replace('\\', '/').TrimEnd('/');
            var slash = name.LastIndexOf('/');
            name = slash >= 0 ? name.Substring(slash + 1) : name;
            return name.Length == 0 ? null : name;
        }
    }
}
