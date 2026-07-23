using TelegramDownloader.Models.Persistence;
using TelegramDownloader.Services;

namespace TelegramDownloader.Models.Api
{
    /// <summary>Kind of transfer reported by the API and the SignalR hub.</summary>
    public static class TransferKind
    {
        public const string Download = "download";
        public const string Upload = "upload";
        /// <summary>A batch job that spawns individual downloads/uploads.</summary>
        public const string Task = "task";
    }

    /// <summary>
    /// A single running/queued/finished transfer. Shape is shared by the REST
    /// endpoints and by the <c>transfers</c> SignalR hub, so a client can render
    /// the same view from a snapshot or from a live event.
    /// </summary>
    public class TransferDto
    {
        /// <summary>Stable id of the transfer. Use it to pause/resume/cancel.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>One of <see cref="TransferKind"/>.</summary>
        public string Kind { get; set; } = TransferKind.Download;

        /// <summary>Operation label: <c>Download</c>, <c>Upload</c>, <c>Splitting</c>, <c>MD5 Calc</c>, <c>XxHash Calc</c>.</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary><c>Error</c>, <c>Pending</c>, <c>Canceled</c>, <c>Paused</c>, <c>Completed</c> or <c>Working</c>.</summary>
        public string State { get; set; } = nameof(StateTask.Pending);

        /// <summary>True when the transfer sits in the queue instead of running.</summary>
        public bool IsQueued { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Destination path for downloads, source path for uploads.</summary>
        public string? Path { get; set; }

        public string? ChannelId { get; set; }
        public string? ChannelName { get; set; }

        public long Size { get; set; }
        public long Transmitted { get; set; }
        public string SizeText { get; set; } = "0 B";
        public string TransmittedText { get; set; } = "0 B";

        /// <summary>Completion percentage, 0-100.</summary>
        public int Progress { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        // Batch-only fields
        /// <summary>Number of files in the batch (batch tasks only).</summary>
        public int? TotalItems { get; set; }
        /// <summary>Number of files already processed (batch tasks only).</summary>
        public int? ExecutedItems { get; set; }
        /// <summary>True when a batch task uploads, false when it downloads.</summary>
        public bool? IsUpload { get; set; }
        public string? FromPath { get; set; }
        public string? ToPath { get; set; }

        public static TransferDto FromDownload(DownloadModel m, bool isQueued = false) => new()
        {
            Id = m._internalId,
            Kind = TransferKind.Download,
            Action = m.action,
            State = m.state.ToString(),
            IsQueued = isQueued,
            Name = m.name ?? string.Empty,
            Path = m.path,
            ChannelId = m.PersistenceChannelId,
            ChannelName = m.channelName,
            Size = m._size,
            Transmitted = m._transmitted,
            SizeText = m._sizeString ?? HelperService.SizeSuffix(m._size),
            TransmittedText = m._transmittedString ?? HelperService.SizeSuffix(m._transmitted),
            Progress = m.progress,
            CreatedAt = m.creationDate,
            StartedAt = m.startDate == default ? null : m.startDate,
            EndedAt = m.endnDate == default ? null : m.endnDate
        };

        public static TransferDto FromUpload(UploadModel m, bool isQueued = false) => new()
        {
            Id = m._internalId,
            Kind = TransferKind.Upload,
            Action = m.action,
            State = m.state.ToString(),
            IsQueued = isQueued,
            Name = m.name ?? string.Empty,
            Path = m.path,
            ChannelId = m.PersistenceChannelId,
            ChannelName = m.chatName,
            Size = m._size,
            Transmitted = m._transmitted,
            SizeText = m._sizeString ?? HelperService.SizeSuffix(m._size),
            TransmittedText = m._transmittedString ?? HelperService.SizeSuffix(m._transmitted),
            Progress = m.progress,
            CreatedAt = m.creationDate,
            StartedAt = m.startDate == default ? null : m.startDate,
            EndedAt = m.endnDate == default ? null : m.endnDate
        };

        public static TransferDto FromBatch(InfoDownloadTaksModel m) => new()
        {
            Id = m._internalId,
            Kind = TransferKind.Task,
            Action = m.isUpload ? "Upload batch" : "Download batch",
            State = m.state.ToString(),
            IsQueued = m.state == StateTask.Pending,
            Name = m.isUpload ? (m.toPath ?? "batch") : (m.fromPath ?? "batch"),
            ChannelId = m.channelId,
            Size = m.totalSize,
            Transmitted = m.executedSize,
            SizeText = HelperService.SizeSuffix(m.totalSize),
            TransmittedText = HelperService.SizeSuffix(m.executedSize),
            Progress = m.progress,
            CreatedAt = m.creationDate,
            EndedAt = m.endnDate == default ? null : m.endnDate,
            TotalItems = m.total,
            ExecutedItems = m.executed,
            IsUpload = m.isUpload,
            FromPath = m.fromPath,
            ToPath = m.toPath
        };
    }

    /// <summary>
    /// Aggregate view of everything in flight. This is the payload of the
    /// <c>TransfersSnapshot</c> hub message and of <c>GET /api/v1/transfers</c>.
    /// </summary>
    public class TransfersSnapshotDto
    {
        public List<TransferDto> Downloads { get; set; } = new();
        public List<TransferDto> QueuedDownloads { get; set; } = new();
        public List<TransferDto> Uploads { get; set; } = new();
        public List<TransferDto> QueuedUploads { get; set; } = new();
        public List<TransferDto> Tasks { get; set; } = new();
        public TransferSummaryDto Summary { get; set; } = new();
    }

    /// <summary>
    /// Lightweight counters and current speeds. Pushed on its own as
    /// <c>TransferSummary</c> so clients can render a status bar cheaply.
    /// </summary>
    public class TransferSummaryDto
    {
        public int ActiveDownloads { get; set; }
        public int QueuedDownloads { get; set; }
        public int ActiveUploads { get; set; }
        public int QueuedUploads { get; set; }
        public int ActiveTasks { get; set; }
        public int TotalTasks { get; set; }

        /// <summary>Human readable download speed, e.g. <c>4.2 MB/s</c>.</summary>
        public string DownloadSpeed { get; set; } = "0 KB/s";

        /// <summary>Human readable upload speed.</summary>
        public string UploadSpeed { get; set; } = "0 KB/s";

        /// <summary>Bytes transferred during the current sampling second.</summary>
        public long DownloadBytesPerSecond { get; set; }
        public long UploadBytesPerSecond { get; set; }

        /// <summary>True when the download queue has been paused globally.</summary>
        public bool DownloadsPaused { get; set; }

        public bool IsWorking => ActiveDownloads > 0 || ActiveUploads > 0 || ActiveTasks > 0;
    }

    /// <summary>One sample of the speed history chart.</summary>
    public class SpeedPointDto
    {
        public DateTime Time { get; set; }
        public long BytesPerSecond { get; set; }
        public string SpeedText { get; set; } = "0 KB/s";
        public List<string> ActiveFiles { get; set; } = new();

        public static SpeedPointDto From(SpeedHistory h) => new()
        {
            Time = h.time,
            BytesPerSecond = h.speed,
            SpeedText = h.speedString ?? "0 KB/s",
            ActiveFiles = h.activeFiles ?? new List<string>()
        };
    }

    /// <summary>Download and upload speed history, used to draw charts.</summary>
    public class SpeedHistoryDto
    {
        public List<SpeedPointDto> Download { get; set; } = new();
        public List<SpeedPointDto> Upload { get; set; } = new();

        /// <summary>Seconds between samples.</summary>
        public int IntervalSeconds { get; set; } = TransactionInfoService.INTERVAL_SPEED_HISTORY_SECONDS;

        /// <summary>How long samples are retained, in seconds.</summary>
        public int WindowSeconds { get; set; } = TransactionInfoService.MAX_SPEED_HISTORY_SECONDS;
    }

    /// <summary>Body of <c>POST /api/v1/transfers/downloads</c>.</summary>
    public class StartDownloadRequest
    {
        /// <summary>Channel whose indexed files should be downloaded.</summary>
        public string ChannelId { get; set; } = string.Empty;

        /// <summary>Ids of the files/folders to download. Folders are pulled recursively.</summary>
        public List<string> FileIds { get; set; } = new();

        /// <summary>
        /// Destination folder relative to the server local root. Null keeps the
        /// original channel folder structure.
        /// </summary>
        public string? TargetPath { get; set; }

        /// <summary>Set when downloading from a shared collection instead of an owned channel.</summary>
        public string? SharedCollectionId { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/transfers/uploads</c>.</summary>
    public class StartUploadRequest
    {
        /// <summary>Destination channel.</summary>
        public string ChannelId { get; set; } = string.Empty;

        /// <summary>Paths relative to the server local root. Folders are pushed recursively.</summary>
        public List<string> LocalPaths { get; set; } = new();

        /// <summary>Destination folder inside the channel, e.g. <c>/backup/</c>. Defaults to the root.</summary>
        public string? TargetPath { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/transfers/messages</c>.</summary>
    public class DownloadMessagesRequest
    {
        /// <summary>Chat the messages belong to.</summary>
        public long ChatId { get; set; }

        /// <summary>Telegram message ids carrying the media to download.</summary>
        public List<int> MessageIds { get; set; } = new();

        /// <summary>Destination folder relative to the server local root.</summary>
        public string? TargetPath { get; set; }
    }

    /// <summary>Result returned when a transfer batch has been queued.</summary>
    public class TransferAcceptedDto
    {
        /// <summary>Number of items accepted for transfer.</summary>
        public int Accepted { get; set; }

        /// <summary>Ids that could not be resolved and were skipped.</summary>
        public List<string> Skipped { get; set; } = new();

        /// <summary>Id of the batch task, when the operation created one.</summary>
        public string? TaskId { get; set; }
    }

    /// <summary>A transfer restored from MongoDB after an application restart.</summary>
    public class PersistedTaskDto
    {
        public string Id { get; set; } = string.Empty;
        public string InternalId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? ChannelId { get; set; }
        public string? ChannelName { get; set; }
        public long TotalSize { get; set; }
        public long TransmittedBytes { get; set; }
        public int Progress { get; set; }
        public string? SourcePath { get; set; }
        public string? DestinationPath { get; set; }
        public DateTime CreationDate { get; set; }
        public DateTime LastUpdated { get; set; }
        public int RetryCount { get; set; }
        public string? LastError { get; set; }

        public static PersistedTaskDto From(PersistedTaskModel m) => new()
        {
            Id = m.Id,
            InternalId = m.InternalId,
            Type = m.Type.ToString(),
            State = m.State.ToString(),
            Name = m.Name,
            ChannelId = m.ChannelId,
            ChannelName = m.ChannelName,
            TotalSize = m.TotalSize,
            TransmittedBytes = m.TransmittedBytes,
            Progress = m.Progress,
            SourcePath = m.SourcePath,
            DestinationPath = m.DestinationPath,
            CreationDate = m.CreationDate,
            LastUpdated = m.LastUpdated,
            RetryCount = m.RetryCount,
            LastError = m.LastError
        };
    }
}
