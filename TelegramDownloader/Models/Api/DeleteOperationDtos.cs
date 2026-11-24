using TelegramDownloader.Services.Deletes;

namespace TelegramDownloader.Models.Api
{
    /// <summary>Answer of the endpoints that start a background delete.</summary>
    public class DeleteOperationStartedDto
    {
        /// <summary>Read it with <c>GET /api/v1/delete-operations/{operationId}</c>; correlates the <c>DeleteProgress</c> messages.</summary>
        public string OperationId { get; set; } = string.Empty;
    }

    /// <summary>An item a delete operation could not remove.</summary>
    public class ApiDeleteFailureDto
    {
        /// <summary>Channel path (<c>/music/a.mp3</c>) or local path relative to the root.</summary>
        public string Path { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// A background delete (<c>GET /api/v1/delete-operations/{id}</c> and the
    /// <c>DeleteProgress</c> message of <c>/hubs/events</c>).
    /// </summary>
    public class ApiDeleteOperationDto
    {
        public string Id { get; set; } = string.Empty;

        /// <summary><c>channel</c> or <c>local</c>.</summary>
        public string Kind { get; set; } = DeleteKinds.Channel;

        /// <summary>Channel id (channel deletes only).</summary>
        public string? ChannelId { get; set; }

        /// <summary>Channel name, when known.</summary>
        public string? ChannelName { get; set; }

        /// <summary>Number of items the client selected.</summary>
        public int ItemCount { get; set; }

        /// <summary>
        /// <c>counting</c> → <c>deleting</c> (<c>waiting</c> while Telegram asks
        /// to wait, see <see cref="WaitUntil"/>) → <c>done</c> | <c>failed</c> | <c>cancelled</c>.
        /// </summary>
        public string Phase { get; set; } = DeletePhases.Counting;

        /// <summary>True once the totals are known (after <c>counting</c>).</summary>
        public bool Counted { get; set; }

        /// <summary>True when <see cref="Phase"/> is final.</summary>
        public bool Finished { get; set; }

        public int TotalFiles { get; set; }
        public int TotalFolders { get; set; }
        public long TotalBytes { get; set; }
        public int ProcessedFiles { get; set; }
        public int ProcessedFolders { get; set; }
        public long ProcessedBytes { get; set; }

        /// <summary>Telegram messages deleted so far (channel deletes).</summary>
        public int MessagesDeleted { get; set; }

        /// <summary>
        /// True in channels where the account cannot delete messages: only the
        /// index entries are removed, the messages stay in the channel.
        /// </summary>
        public bool IndexOnly { get; set; }

        /// <summary>Item being deleted.</summary>
        public string? CurrentItem { get; set; }

        /// <summary>Items that could not be deleted (may exceed <see cref="Failed"/>, which is capped at 100).</summary>
        public int FailedCount { get; set; }
        public List<ApiDeleteFailureDto> Failed { get; set; } = new();

        /// <summary>While <c>waiting</c>: when Telegram allows the next call (UTC).</summary>
        public DateTime? WaitUntil { get; set; }

        public bool CancelRequested { get; set; }

        /// <summary>Why the operation failed (phase <c>failed</c>).</summary>
        public string? Error { get; set; }

        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }

        public static ApiDeleteOperationDto From(DeleteOperationSnapshot s) => new ApiDeleteOperationDto
        {
            Id = s.Id,
            Kind = s.Kind,
            ChannelId = s.ChannelId,
            ChannelName = s.ChannelName,
            ItemCount = s.ItemCount,
            Phase = s.Phase,
            Counted = s.Counted,
            Finished = DeletePhases.IsFinal(s.Phase),
            TotalFiles = s.TotalFiles,
            TotalFolders = s.TotalFolders,
            TotalBytes = s.TotalBytes,
            ProcessedFiles = s.ProcessedFiles,
            ProcessedFolders = s.ProcessedFolders,
            ProcessedBytes = s.ProcessedBytes,
            MessagesDeleted = s.MessagesDeleted,
            IndexOnly = s.IndexOnly,
            CurrentItem = s.CurrentItem,
            FailedCount = s.FailedCount,
            Failed = s.Failed.Select(f => new ApiDeleteFailureDto { Path = f.Path, Reason = f.Reason }).ToList(),
            WaitUntil = s.WaitUntil,
            CancelRequested = s.CancelRequested,
            Error = s.Error,
            StartedAt = s.StartedAt,
            FinishedAt = s.FinishedAt
        };
    }
}
