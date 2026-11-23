namespace TelegramDownloader.Models.Api
{
    /// <summary>
    /// <c>Notification</c> message of the <c>/hubs/events</c> hub: the toasts the
    /// web UI shows (download started, upload completed, preload finished...).
    /// </summary>
    public class NotificationEventDto
    {
        /// <summary><c>info</c>, <c>success</c>, <c>warning</c> or <c>error</c>.</summary>
        public string Level { get; set; } = EventLevels.Info;

        /// <summary>Short heading, when the server gave one.</summary>
        public string? Title { get; set; }

        /// <summary>Plain text body (any HTML markup is stripped).</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>When the server raised it (UTC).</summary>
        public DateTime At { get; set; }
    }

    /// <summary>Values of <see cref="NotificationEventDto.Level"/>.</summary>
    public static class EventLevels
    {
        public const string Info = "info";
        public const string Success = "success";
        public const string Warning = "warning";
        public const string Error = "error";
    }

    /// <summary><c>ChannelRefreshed</c>: a channel refresh ended (success, cancel or failure).</summary>
    public class ChannelRefreshedEventDto
    {
        public string ChannelId { get; set; } = string.Empty;

        /// <summary>Same shape as <c>GET /api/v1/channels/{id}/refresh/result</c>.</summary>
        public ApiChannelRefreshResultDto Result { get; set; } = new();
    }

    /// <summary><c>ChannelRefreshProgress</c>: live counters of a running refresh, at most once per second.</summary>
    public class ChannelRefreshProgressEventDto
    {
        public string ChannelId { get; set; } = string.Empty;

        /// <summary>Same shape as <c>GET /api/v1/channels/{id}/refresh/progress</c>.</summary>
        public ApiChannelRefreshProgressDto Progress { get; set; } = new();
    }

    /// <summary><c>ImportProgress</c>: progress of a channel-index or shared-collection import.</summary>
    public class ImportProgressEventDto
    {
        /// <summary>Import id, as returned by the endpoint that started it.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary><c>index</c> (channel index import) or <c>share</c> (shared collection import).</summary>
        public string Kind { get; set; } = ImportKinds.Index;

        /// <summary>0-100.</summary>
        public int Percent { get; set; }

        /// <summary>Human readable status, e.g. <c>120/480 items</c>.</summary>
        public string Message { get; set; } = string.Empty;

        public int Total { get; set; }
        public int Completed { get; set; }

        /// <summary>True on the last message of the import (success or failure).</summary>
        public bool Done { get; set; }

        /// <summary>Failure reason; null on success.</summary>
        public string? Error { get; set; }
    }

    /// <summary>Values of <see cref="ImportProgressEventDto.Kind"/>.</summary>
    public static class ImportKinds
    {
        public const string Index = "index";
        public const string Share = "share";
    }

    /// <summary><c>AuthState</c>: whether a Telegram session is active. Sent on connect and on every change.</summary>
    public class AuthStateEventDto
    {
        public bool TelegramLoggedIn { get; set; }
    }

    /// <summary><c>LibraryScan</c>: media library scan state, every second while it runs and once when it ends.</summary>
    public class LibraryScanEventDto
    {
        public bool Running { get; set; }

        /// <summary>Same shape as <c>GET /api/v1/library/scan</c>.</summary>
        public ApiLibraryScanStateDto? Progress { get; set; }
    }

    /// <summary>Returned by the endpoints that start an import (<c>202 Accepted</c>).</summary>
    public class ImportStartedDto
    {
        /// <summary>Correlates the <c>ImportProgress</c> messages of <c>/hubs/events</c>.</summary>
        public string ImportId { get; set; } = string.Empty;

        public string Kind { get; set; } = ImportKinds.Index;
    }
}
