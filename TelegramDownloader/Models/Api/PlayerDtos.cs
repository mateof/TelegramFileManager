using MongoDB.Bson.Serialization.Attributes;

namespace TelegramDownloader.Models.Api
{
    /// <summary>
    /// A track of the shared player data (favourites, history, resume positions).
    /// It is identified by <see cref="Key"/>: <c>channel:{channelId}:{fileId}</c>
    /// for a channel file, <c>local:{path}</c> for a file of the local storage
    /// (path relative to the local root, with <c>/</c> separators and no leading
    /// slash). The server computes the key from the ids/path; the other fields
    /// are a snapshot for display, so lists render without looking files up.
    /// Stored as it is inside the player documents.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class ApiPlayerTrackDto
    {
        /// <summary>
        /// Track key (computed by the server; clients may send it). Always set in
        /// answers. Nullable only so a body without it is not refused by the
        /// model validation (MVC treats non-nullable strings as required).
        /// </summary>
        public string? Key { get; set; } = string.Empty;

        /// <summary>Channel id, for channel tracks; null for local files.</summary>
        public string? ChannelId { get; set; }

        /// <summary>Entry id in the channel index, for channel tracks; null for local files.</summary>
        public string? FileId { get; set; }

        /// <summary>True for a file of the server's local storage.</summary>
        public bool IsLocalFile { get; set; }

        /// <summary>Local files: path relative to the local root. Channel tracks: optional, as sent.</summary>
        public string? FilePath { get; set; }

        /// <summary>File name; always set in answers (derived from the path when not sent).</summary>
        public string? FileName { get; set; } = string.Empty;
        public string? ChannelName { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }

        /// <summary>Length in seconds, when known.</summary>
        public double? Duration { get; set; }

        /// <summary>Size in bytes, when known.</summary>
        public long? FileSize { get; set; }

        public ApiPlayerTrackDto Clone() => (ApiPlayerTrackDto)MemberwiseClone();
    }

    /// <summary>A favourite track: the track fields plus when it was added.</summary>
    public class ApiPlayerFavoriteDto : ApiPlayerTrackDto
    {
        /// <summary>When the track was first marked as favourite (UTC). Re-adding keeps it.</summary>
        public DateTime AddedAt { get; set; }

        public static ApiPlayerFavoriteDto From(ApiPlayerTrackDto track, DateTime addedAt)
        {
            var dto = new ApiPlayerFavoriteDto
            {
                Key = track.Key,
                ChannelId = track.ChannelId,
                FileId = track.FileId,
                IsLocalFile = track.IsLocalFile,
                FilePath = track.FilePath,
                FileName = track.FileName,
                ChannelName = track.ChannelName,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.Album,
                Duration = track.Duration,
                FileSize = track.FileSize,
                AddedAt = addedAt
            };
            return dto;
        }
    }

    /// <summary>A page of favourites, newest first.</summary>
    public class ApiPlayerFavoritesPageDto
    {
        public List<ApiPlayerFavoriteDto> Items { get; set; } = new();
        public long Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/player/history</c>.</summary>
    public class ApiPlayerHistoryWriteRequest
    {
        public ApiPlayerTrackDto? Track { get; set; }

        /// <summary>When it was played; default now. Send it when syncing plays recorded offline.</summary>
        public DateTimeOffset? PlayedAt { get; set; }

        /// <summary>Where playback was when the play was recorded (seconds).</summary>
        public double? PositionSeconds { get; set; }

        /// <summary>The track was played to the end.</summary>
        public bool? Completed { get; set; }
    }

    /// <summary>One play of the history.</summary>
    public class ApiPlayerHistoryEntryDto
    {
        public string Id { get; set; } = string.Empty;
        public ApiPlayerTrackDto Track { get; set; } = new();
        public DateTime PlayedAt { get; set; }
        public double? PositionSeconds { get; set; }
        public bool Completed { get; set; }
    }

    /// <summary>A page of the history, newest first. Ask the next page with <c>before</c> = the last <c>playedAt</c>.</summary>
    public class ApiPlayerHistoryPageDto
    {
        public List<ApiPlayerHistoryEntryDto> Items { get; set; } = new();
        public bool HasMore { get; set; }
    }

    /// <summary>A most played track.</summary>
    public class ApiPlayerTopTrackDto
    {
        /// <summary>The snapshot of its latest play.</summary>
        public ApiPlayerTrackDto Track { get; set; } = new();
        public int PlayCount { get; set; }
        public DateTime LastPlayedAt { get; set; }
    }

    /// <summary>Body of <c>PUT /api/v1/player/resume</c>.</summary>
    public class ApiPlayerResumeWriteRequest
    {
        public ApiPlayerTrackDto? Track { get; set; }
        public double PositionSeconds { get; set; }
        public double DurationSeconds { get; set; }
    }

    /// <summary>A "continue where I left off" position.</summary>
    public class ApiPlayerResumeDto
    {
        public ApiPlayerTrackDto Track { get; set; } = new();
        public double PositionSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Only meaningful in the answer of <c>PUT /resume</c>: true when the
        /// position was in the last 2% or the last 15 s, so the track counts as
        /// finished and its entry was deleted instead of saved. Always false in
        /// stored entries.
        /// </summary>
        public bool Finished { get; set; }
    }

    /// <summary>
    /// <c>PlayerDataChanged</c> message of <c>/hubs/events</c>: the shared player
    /// data changed (from any device); refresh what is shown.
    /// </summary>
    public class PlayerDataChangedEventDto
    {
        /// <summary><c>favorites</c>, <c>history</c> or <c>resume</c>.</summary>
        public string Kind { get; set; } = PlayerDataKinds.Favorites;

        /// <summary>Key of the track that changed; null when the whole list changed (history cleared).</summary>
        public string? Key { get; set; }
    }

    /// <summary>Values of <see cref="PlayerDataChangedEventDto.Kind"/>.</summary>
    public static class PlayerDataKinds
    {
        public const string Favorites = "favorites";
        public const string History = "history";
        public const string Resume = "resume";
    }
}
