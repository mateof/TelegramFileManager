using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace TelegramDownloader.Models.Library
{
    public static class LibraryKind
    {
        public const string Movie = "movie";
        public const string Series = "series";

        public static bool IsValid(string? kind) => kind == Movie || kind == Series;
    }

    public static class LibraryFileStatus
    {
        /// <summary>Confidently identified.</summary>
        public const string Matched = "matched";
        /// <summary>Identified, but with a score low enough to deserve a human look.</summary>
        public const string Review = "review";
        /// <summary>No candidate reached the minimum score.</summary>
        public const string Unmatched = "unmatched";
        /// <summary>Excluded from the library by the user (trailers, extras...).</summary>
        public const string Ignored = "ignored";
    }

    public static class LibraryMatchSource
    {
        public const string Auto = "auto";
        public const string Manual = "manual";
    }

    /// <summary>A movie or a series identified by a metadata provider.</summary>
    [BsonIgnoreExtraElements]
    public class LibraryItem
    {
        [BsonId]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string Kind { get; set; } = LibraryKind.Movie;
        /// <summary>Provider that identified the item (<c>tmdb</c>, <c>omdb</c>...).</summary>
        public string Provider { get; set; } = string.Empty;
        /// <summary>Id of the item inside <see cref="Provider"/>.</summary>
        public string ProviderId { get; set; } = string.Empty;
        /// <summary>Ids in other databases: <c>tmdb</c>, <c>imdb</c>, <c>tvdb</c>...</summary>
        public Dictionary<string, string> ExternalIds { get; set; } = new();
        public string Title { get; set; } = string.Empty;
        public string? OriginalTitle { get; set; }
        public string SortTitle { get; set; } = string.Empty;
        public int? Year { get; set; }
        public string? Overview { get; set; }
        public string? Tagline { get; set; }
        public string? Status { get; set; }
        public List<string> Genres { get; set; } = new();
        public double? Rating { get; set; }
        public int? VoteCount { get; set; }
        /// <summary>Runtime in minutes (movies) or typical episode runtime (series).</summary>
        public int? Runtime { get; set; }
        /// <summary>Absolute source URL of the poster at the provider.</summary>
        public string? PosterUrl { get; set; }
        public string? BackdropUrl { get; set; }
        public List<LibrarySeason> Seasons { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime MetadataFetchedAt { get; set; } = DateTime.UtcNow;
        /// <summary>Identified by hand: a rescan never reassigns it.</summary>
        public bool Locked { get; set; }
        /// <summary>Files linked to the item (ignored ones excluded). Kept by the scan.</summary>
        public int FileCount { get; set; }
        /// <summary>Series: distinct episodes that have a file.</summary>
        public int EpisodeCount { get; set; }
        /// <summary>Channels the files come from, for the hidden-channel filter.</summary>
        public List<long> ChannelIds { get; set; } = new();

        public string? ImdbId => ExternalIds.TryGetValue("imdb", out var v) ? v : null;
    }

    public class LibrarySeason
    {
        public int Number { get; set; }
        public string? Name { get; set; }
        public string? Overview { get; set; }
        public string? PosterUrl { get; set; }
        public int? EpisodeCount { get; set; }
        public DateTime? AirDate { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class LibraryEpisode
    {
        [BsonId]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string ItemId { get; set; } = string.Empty;
        public int Season { get; set; }
        public int Number { get; set; }
        public string? Title { get; set; }
        public string? Overview { get; set; }
        public string? StillUrl { get; set; }
        public DateTime? AirDate { get; set; }
        public int? Runtime { get; set; }
        public double? Rating { get; set; }
    }

    /// <summary>What the name parser understood from a file name and its folder.</summary>
    [BsonIgnoreExtraElements]
    public class ParsedName
    {
        public string Title { get; set; } = string.Empty;
        public int? Year { get; set; }
        public int? Season { get; set; }
        public int? Episode { get; set; }
        public int? EpisodeEnd { get; set; }
        public bool IsSeries { get; set; }
        /// <summary>Where the title came from: <c>file</c>, <c>folder</c> or <c>caption</c>.</summary>
        public string Source { get; set; } = "file";

        public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    }

    /// <summary>A video file of a channel index and how it was identified.</summary>
    [BsonIgnoreExtraElements]
    public class LibraryFile
    {
        [BsonId]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public long ChannelId { get; set; }
        /// <summary>Mongo id of the document in the channel database.</summary>
        public string FileId { get; set; } = string.Empty;
        public int? MessageId { get; set; }
        public bool IsSplit { get; set; }
        public string FileName { get; set; } = string.Empty;
        /// <summary>Extension including the dot.</summary>
        public string Type { get; set; } = string.Empty;
        public string FolderPath { get; set; } = "/";
        public long Size { get; set; }
        public DateTime FileDate { get; set; }
        public ParsedName Parsed { get; set; } = new();
        public string Status { get; set; } = LibraryFileStatus.Unmatched;
        public string? ItemId { get; set; }
        public string? Kind { get; set; }
        public int? Season { get; set; }
        public int? Episode { get; set; }
        public double Confidence { get; set; }
        public string MatchSource { get; set; } = LibraryMatchSource.Auto;
        /// <summary>Set by a manual action: rescans leave it alone.</summary>
        public bool Locked { get; set; }
        /// <summary>Folder rule or folder-name hint applied at scan time; a change triggers a rescan.</summary>
        public string? RuleKind { get; set; }
        public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
        public string? Error { get; set; }

        public static string KeyOf(long channelId, string fileId) => $"{channelId}:{fileId}";
        public string Key => KeyOf(ChannelId, FileId);
    }

    /// <summary>Playback progress of one file. One document per file, shared by every client.</summary>
    [BsonIgnoreExtraElements]
    public class WatchState
    {
        [BsonId]
        public string Id { get; set; } = string.Empty;   // "{channelId}:{fileId}"
        public long ChannelId { get; set; }
        public string FileId { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public string? ItemId { get; set; }
        public int? Season { get; set; }
        public int? Episode { get; set; }
        /// <summary>Reserved for per-user state; a single profile for now.</summary>
        public string Profile { get; set; } = "default";
        public long PositionMs { get; set; }
        public long DurationMs { get; set; }
        public bool Completed { get; set; }
        public int PlayCount { get; set; }
        public DateTime FirstPlayedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastPlayedAt { get; set; } = DateTime.UtcNow;

        public double Progress => DurationMs > 0 ? Math.Clamp((double)PositionMs / DurationMs, 0, 1) : 0;
        public bool InProgress => !Completed && PositionMs > 0;
    }

    /// <summary>Persisted state of the last (or current) library scan.</summary>
    [BsonIgnoreExtraElements]
    public class LibraryScanState
    {
        public const string SingletonId = "last";

        [BsonId]
        public string Id { get; set; } = SingletonId;
        public bool Running { get; set; }
        public bool Cancelled { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        /// <summary><c>all</c> or a channel id.</summary>
        public string Scope { get; set; } = "all";
        public bool Force { get; set; }
        public int ChannelsTotal { get; set; }
        public int ChannelsScanned { get; set; }
        public string? CurrentChannel { get; set; }
        public int FilesSeen { get; set; }
        public int FilesNew { get; set; }
        public int FilesRemoved { get; set; }
        public int Matched { get; set; }
        public int Review { get; set; }
        public int Unmatched { get; set; }
        public int Failed { get; set; }
        public string? Error { get; set; }

        public LibraryScanState Clone() => (LibraryScanState)MemberwiseClone();
    }

    /// <summary>Cached provider response so rescans don't repeat the same search.</summary>
    [BsonIgnoreExtraElements]
    public class ProviderCacheEntry
    {
        [BsonId]
        public string Id { get; set; } = string.Empty;
        public string Json { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }
}
