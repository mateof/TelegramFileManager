using TelegramDownloader.Models.Library;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Library;

namespace TelegramDownloader.Models.Api
{
    /// <summary>Playback progress of one file.</summary>
    public class ApiWatchStateDto
    {
        public long ChannelId { get; set; }
        public string FileId { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public string? ItemId { get; set; }
        public int? Season { get; set; }
        public int? Episode { get; set; }
        public long PositionMs { get; set; }
        public long DurationMs { get; set; }
        /// <summary>0-1 fraction of the duration already played.</summary>
        public double Progress { get; set; }
        public bool Completed { get; set; }
        public int PlayCount { get; set; }
        public DateTime FirstPlayedAt { get; set; }
        public DateTime LastPlayedAt { get; set; }

        public static ApiWatchStateDto From(WatchState w) => new()
        {
            ChannelId = w.ChannelId,
            FileId = w.FileId,
            FileName = w.FileName,
            ItemId = w.ItemId,
            Season = w.Season,
            Episode = w.Episode,
            PositionMs = w.PositionMs,
            DurationMs = w.DurationMs,
            Progress = Math.Round(w.Progress, 4),
            Completed = w.Completed,
            PlayCount = w.PlayCount,
            FirstPlayedAt = w.FirstPlayedAt,
            LastPlayedAt = w.LastPlayedAt
        };
    }

    /// <summary>Watch state of a whole item (a movie or a series).</summary>
    public class ApiWatchSummaryDto
    {
        /// <summary>Movie: watched. Series: every available episode watched.</summary>
        public bool Completed { get; set; }
        public bool InProgress { get; set; }
        /// <summary>Position of the most recently played file, when in progress.</summary>
        public long PositionMs { get; set; }
        public long DurationMs { get; set; }
        public double Progress { get; set; }
        public int EpisodesTotal { get; set; }
        public int EpisodesWatched { get; set; }
        public DateTime? LastPlayedAt { get; set; }
    }

    public class ApiParsedNameDto
    {
        public string Title { get; set; } = string.Empty;
        public int? Year { get; set; }
        public int? Season { get; set; }
        public int? Episode { get; set; }
        public bool IsSeries { get; set; }
        public string Source { get; set; } = "file";

        public static ApiParsedNameDto From(ParsedName p) => new()
        {
            Title = p.Title, Year = p.Year, Season = p.Season, Episode = p.Episode, IsSeries = p.IsSeries, Source = p.Source
        };
    }

    /// <summary>A channel video file as seen by the library.</summary>
    public class ApiLibraryFileDto
    {
        public long ChannelId { get; set; }
        public string FileId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = "/";
        public long Size { get; set; }
        public string SizeText { get; set; } = "0 B";
        /// <summary><c>matched</c>, <c>review</c>, <c>unmatched</c> or <c>ignored</c>.</summary>
        public string Status { get; set; } = LibraryFileStatus.Unmatched;
        public double Confidence { get; set; }
        /// <summary><c>auto</c> or <c>manual</c>.</summary>
        public string MatchSource { get; set; } = LibraryMatchSource.Auto;
        public bool Locked { get; set; }
        public ApiParsedNameDto Parsed { get; set; } = new();
        public string? ItemId { get; set; }
        public string? Kind { get; set; }
        public int? Season { get; set; }
        public int? Episode { get; set; }
        /// <summary>The regular file DTO, with its stream and download URLs.</summary>
        public ApiFileDto File { get; set; } = new();
        public ApiWatchStateDto? Watch { get; set; }
        /// <summary>The item the file belongs to. Present in flat lists (continue, review).</summary>
        public ApiLibraryItemDto? Item { get; set; }
        public DateTime ScannedAt { get; set; }
        public string? Error { get; set; }

        public static ApiLibraryFileDto From(LibraryFile f, string baseUrl, WatchState? watch = null, ApiLibraryItemDto? item = null)
        {
            var bson = new BsonFileManagerModel
            {
                Id = f.FileId,
                Name = f.FileName,
                FilterPath = f.FolderPath,
                IsFile = true,
                Size = f.Size,
                Type = f.Type,
                MessageId = f.MessageId,
                isSplit = f.IsSplit,
                DateCreated = f.FileDate,
                DateModified = f.FileDate
            };
            return new ApiLibraryFileDto
            {
                ChannelId = f.ChannelId,
                FileId = f.FileId,
                FileName = f.FileName,
                FolderPath = f.FolderPath,
                Size = f.Size,
                SizeText = HelperService.SizeSuffix(f.Size),
                Status = f.Status,
                Confidence = Math.Round(f.Confidence, 3),
                MatchSource = f.MatchSource,
                Locked = f.Locked,
                Parsed = ApiParsedNameDto.From(f.Parsed),
                ItemId = f.ItemId,
                Kind = f.Kind,
                Season = f.Season,
                Episode = f.Episode,
                File = ApiFileDto.FromBson(bson, f.ChannelId.ToString(), baseUrl),
                Watch = watch == null ? null : ApiWatchStateDto.From(watch),
                Item = item,
                ScannedAt = f.ScannedAt,
                Error = f.Error
            };
        }
    }

    /// <summary>A movie or a series of the library.</summary>
    public class ApiLibraryItemDto
    {
        public string Id { get; set; } = string.Empty;
        /// <summary><c>movie</c> or <c>series</c>.</summary>
        public string Kind { get; set; } = LibraryKind.Movie;
        public string Title { get; set; } = string.Empty;
        public string? OriginalTitle { get; set; }
        public int? Year { get; set; }
        public string? Overview { get; set; }
        public List<string> Genres { get; set; } = new();
        public double? Rating { get; set; }
        public int? VoteCount { get; set; }
        /// <summary>Minutes.</summary>
        public int? Runtime { get; set; }
        /// <summary>Absolute URL served by this server (cached copy of the provider image).</summary>
        public string? PosterUrl { get; set; }
        public string? BackdropUrl { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string ProviderId { get; set; } = string.Empty;
        public string? ImdbId { get; set; }
        public Dictionary<string, string> ExternalIds { get; set; } = new();
        public bool Locked { get; set; }
        public int FileCount { get; set; }
        public List<long> ChannelIds { get; set; } = new();
        public int SeasonCount { get; set; }
        public DateTime AddedAt { get; set; }
        public ApiWatchSummaryDto Watch { get; set; } = new();

        public static ApiLibraryItemDto From(LibraryItem item, string baseUrl, ApiWatchSummaryDto? watch = null)
        {
            var dto = new ApiLibraryItemDto();
            Fill(dto, item, baseUrl, watch);
            return dto;
        }

        protected static void Fill(ApiLibraryItemDto dto, LibraryItem item, string baseUrl, ApiWatchSummaryDto? watch)
        {
            dto.Id = item.Id;
            dto.Kind = item.Kind;
            dto.Title = item.Title;
            dto.OriginalTitle = item.OriginalTitle;
            dto.Year = item.Year;
            dto.Overview = item.Overview;
            dto.Genres = item.Genres;
            dto.Rating = item.Rating;
            dto.VoteCount = item.VoteCount;
            dto.Runtime = item.Runtime;
            dto.PosterUrl = LibraryImageService.ProxyUrl(baseUrl, item.PosterUrl, "w342");
            dto.BackdropUrl = LibraryImageService.ProxyUrl(baseUrl, item.BackdropUrl, "w1280");
            dto.Provider = item.Provider;
            dto.ProviderId = item.ProviderId;
            dto.ImdbId = item.ImdbId;
            dto.ExternalIds = item.ExternalIds;
            dto.Locked = item.Locked;
            dto.FileCount = item.FileCount;
            dto.ChannelIds = item.ChannelIds;
            dto.SeasonCount = item.Seasons.Count(s => s.Number > 0);
            dto.AddedAt = item.CreatedAt;
            dto.Watch = watch ?? new ApiWatchSummaryDto();
        }
    }

    public class ApiLibraryItemDetailDto : ApiLibraryItemDto
    {
        public string? Tagline { get; set; }
        public string? Status { get; set; }
        /// <summary>Movies: the files (versions) of the movie.</summary>
        public List<ApiLibraryFileDto> Files { get; set; } = new();
        /// <summary>Series: seasons that have at least one file, with their episodes.</summary>
        public List<ApiLibrarySeasonDto> Seasons { get; set; } = new();
        /// <summary>Series: the episode to play next (the one in progress, or the first unwatched).</summary>
        public ApiLibraryEpisodeDto? NextUp { get; set; }
        /// <summary>The file to resume, when something is in progress.</summary>
        public ApiLibraryFileDto? Resume { get; set; }

        public static ApiLibraryItemDetailDto From(LibraryItem item, string baseUrl, ApiWatchSummaryDto watch)
        {
            var dto = new ApiLibraryItemDetailDto { Tagline = item.Tagline, Status = item.Status };
            Fill(dto, item, baseUrl, watch);
            dto.PosterUrl = LibraryImageService.ProxyUrl(baseUrl, item.PosterUrl, "w500");
            return dto;
        }
    }

    public class ApiLibrarySeasonDto
    {
        public int Number { get; set; }
        public string? Name { get; set; }
        public string? Overview { get; set; }
        public string? PosterUrl { get; set; }
        public int? EpisodeCount { get; set; }
        /// <summary>Episodes that have files, in order.</summary>
        public List<ApiLibraryEpisodeDto> Episodes { get; set; } = new();
        public int EpisodesWatched { get; set; }
    }

    public class ApiLibraryEpisodeDto
    {
        public int Season { get; set; }
        public int Number { get; set; }
        public string? Title { get; set; }
        public string? Overview { get; set; }
        public string? StillUrl { get; set; }
        public DateTime? AirDate { get; set; }
        public int? Runtime { get; set; }
        public double? Rating { get; set; }
        public List<ApiLibraryFileDto> Files { get; set; } = new();
        /// <summary>Progress of the most recently played file of the episode.</summary>
        public ApiWatchStateDto? Watch { get; set; }
        public bool Completed { get; set; }
    }

    public class ApiProviderCandidateDto
    {
        public string Provider { get; set; } = string.Empty;
        public string ProviderId { get; set; } = string.Empty;
        public string Kind { get; set; } = LibraryKind.Movie;
        public string Title { get; set; } = string.Empty;
        public string? OriginalTitle { get; set; }
        public int? Year { get; set; }
        public string? Overview { get; set; }
        public string? PosterUrl { get; set; }
        public string? ImdbId { get; set; }

        public static ApiProviderCandidateDto From(ProviderCandidate c, string baseUrl) => new()
        {
            Provider = c.Provider,
            ProviderId = c.ProviderId,
            Kind = c.Kind,
            Title = c.Title,
            OriginalTitle = c.OriginalTitle,
            Year = c.Year,
            Overview = c.Overview,
            PosterUrl = LibraryImageService.ProxyUrl(baseUrl, c.PosterUrl, "w185"),
            ImdbId = c.ImdbId
        };
    }

    /// <summary>A metadata provider and how it is configured.</summary>
    public class ApiLibraryProviderDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;
        public string KeyUrl { get; set; } = string.Empty;
        public bool RequiresKey { get; set; }
        public bool SupportsEpisodes { get; set; }
        public bool SupportsImages { get; set; }
        public bool SupportsLanguage { get; set; }
        public bool Enabled { get; set; }
        public bool HasKey { get; set; }
        /// <summary>Last characters of the key, for display.</summary>
        public string? ApiKeyMasked { get; set; }
        public int Priority { get; set; }

        public static ApiLibraryProviderDto From(ProviderDescriptor d, LibraryProviderConfig c)
        {
            var key = c.ApiKey?.Trim() ?? string.Empty;
            return new ApiLibraryProviderDto
            {
                Id = d.Id,
                Name = d.Name,
                Website = d.Website,
                KeyUrl = d.KeyUrl,
                RequiresKey = d.RequiresKey,
                SupportsEpisodes = d.SupportsEpisodes,
                SupportsImages = d.SupportsImages,
                SupportsLanguage = d.SupportsLanguage,
                Enabled = c.Enabled,
                HasKey = key.Length > 0,
                ApiKeyMasked = key.Length == 0 ? null : key.Length <= 4 ? "****" : "****" + key[^4..],
                Priority = c.Priority
            };
        }
    }

    /// <summary>Provider settings as sent by a client (the key is optional: omit it to keep the current one).</summary>
    public class LibraryProviderConfigDto
    {
        public string Id { get; set; } = string.Empty;
        public bool? Enabled { get; set; }
        public string? ApiKey { get; set; }
        public int? Priority { get; set; }
    }

    public class ApiLibraryScanStateDto
    {
        public bool Running { get; set; }
        public bool Cancelled { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
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

        public static ApiLibraryScanStateDto? From(LibraryScanState? s) => s == null ? null : new()
        {
            Running = s.Running, Cancelled = s.Cancelled, StartedAt = s.StartedAt, FinishedAt = s.FinishedAt, Scope = s.Scope, Force = s.Force,
            ChannelsTotal = s.ChannelsTotal, ChannelsScanned = s.ChannelsScanned, CurrentChannel = s.CurrentChannel,
            FilesSeen = s.FilesSeen, FilesNew = s.FilesNew, FilesRemoved = s.FilesRemoved,
            Matched = s.Matched, Review = s.Review, Unmatched = s.Unmatched, Failed = s.Failed, Error = s.Error
        };
    }

    public class ApiLibraryStatsDto
    {
        public bool Enabled { get; set; }
        public string Language { get; set; } = string.Empty;
        public bool AutoScan { get; set; }
        public double WatchedThreshold { get; set; }
        public List<ApiLibraryProviderDto> Providers { get; set; } = new();
        /// <summary>True when at least one enabled provider has its key.</summary>
        public bool Ready { get; set; }
        public int Movies { get; set; }
        public int Series { get; set; }
        /// <summary>Files by status: matched, review, unmatched, ignored.</summary>
        public Dictionary<string, int> Files { get; set; } = new();
        public int InProgress { get; set; }
        public ApiLibraryScanStateDto? Scan { get; set; }
    }

    public class ApiLibraryGenreDto
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    // ----- request bodies -----

    public class LibraryScanRequest
    {
        /// <summary>Scan only this channel. Omit for every channel with an index.</summary>
        public string? ChannelId { get; set; }
        /// <summary>Re-identify files that were already matched automatically. Manual matches are never touched.</summary>
        public bool Force { get; set; }
    }

    public class LibraryMatchFileRequest
    {
        public string Provider { get; set; } = string.Empty;
        public string ProviderId { get; set; } = string.Empty;
        /// <summary><c>movie</c> or <c>series</c>.</summary>
        public string Kind { get; set; } = LibraryKind.Movie;
        public int? Season { get; set; }
        public int? Episode { get; set; }
    }

    public class LibraryIdentifyItemRequest
    {
        public string Provider { get; set; } = string.Empty;
        public string ProviderId { get; set; } = string.Empty;
        /// <summary>Defaults to the item's current kind.</summary>
        public string? Kind { get; set; }
    }

    public class LibraryWatchUpdateRequest
    {
        public long PositionMs { get; set; }
        public long DurationMs { get; set; }
        /// <summary>Force the watched flag (e.g. the player reached the end).</summary>
        public bool? Completed { get; set; }
    }

    public class LibraryItemWatchedRequest
    {
        /// <summary>Series: limit to one season.</summary>
        public int? Season { get; set; }
    }
}
