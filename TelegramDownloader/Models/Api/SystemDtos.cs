using TelegramDownloader.Services;

namespace TelegramDownloader.Models.Api
{
    /// <summary>Server identity and health, returned by <c>GET /api/v1/system/info</c>.</summary>
    public class ServerInfoDto
    {
        public string Product { get; set; } = "TelegramFileManager";
        public string Version { get; set; } = string.Empty;

        /// <summary>Highest API version this server implements.</summary>
        public string ApiVersion { get; set; } = "1.0";

        public DateTime ServerTimeUtc { get; set; } = DateTime.UtcNow;
        public bool MongoConnected { get; set; }
        public bool TelegramConfigured { get; set; }
        public bool TelegramAuthenticated { get; set; }
        public bool SetupComplete { get; set; }
        public bool WebDavRunning { get; set; }

        /// <summary>Relative path of the SignalR hub streaming transfer updates.</summary>
        public string TransfersHubPath { get; set; } = "/hubs/transfers";

        /// <summary>True when the server requires an <c>X-Api-Key</c> header.</summary>
        public bool RequiresApiKey { get; set; }
    }

    /// <summary>Machine resource usage, returned by <c>GET /api/v1/system/metrics</c>.</summary>
    public class SystemMetricsDto
    {
        public double SystemCpuUsage { get; set; }
        public double AppCpuUsage { get; set; }
        public int ProcessorCount { get; set; }

        public long TotalMemoryBytes { get; set; }
        public long UsedMemoryBytes { get; set; }
        public long AvailableMemoryBytes { get; set; }
        public double MemoryUsagePercent { get; set; }
        public long AppMemoryBytes { get; set; }

        public string? TempFolderPath { get; set; }
        public long TempFolderSizeBytes { get; set; }
        public long DiskTotalBytes { get; set; }
        public long DiskUsedBytes { get; set; }
        public long DiskFreeBytes { get; set; }
        public double DiskUsagePercent { get; set; }

        public static SystemMetricsDto From(SystemMetrics m) => new()
        {
            SystemCpuUsage = m.SystemCpuUsage,
            AppCpuUsage = m.AppCpuUsage,
            ProcessorCount = m.ProcessorCount,
            TotalMemoryBytes = m.TotalMemoryBytes,
            UsedMemoryBytes = m.UsedMemoryBytes,
            AvailableMemoryBytes = m.AvailableMemoryBytes,
            MemoryUsagePercent = m.MemoryUsagePercent,
            AppMemoryBytes = m.AppMemoryBytes,
            TempFolderPath = m.TempFolderPath,
            TempFolderSizeBytes = m.TempFolderSizeBytes,
            DiskTotalBytes = m.DiskTotalBytes,
            DiskUsedBytes = m.DiskUsedBytes,
            DiskFreeBytes = m.DiskFreeBytes,
            DiskUsagePercent = m.DiskUsagePercent
        };
    }

    /// <summary>Progress of the first-run wizard.</summary>
    public class SetupStatusDto
    {
        /// <summary><c>Complete</c>, <c>MongoDbRequired</c> or <c>TelegramRequired</c>.</summary>
        public string CurrentStep { get; set; } = string.Empty;
        public bool MongoDbConfigured { get; set; }
        public bool MongoDbConnected { get; set; }
        public bool TelegramConfigured { get; set; }
        public string? MongoDbError { get; set; }
    }

    /// <summary>Statistics of one indexed channel database.</summary>
    public class DatabaseStatsDto
    {
        public string ChannelId { get; set; } = string.Empty;
        public string? ChannelName { get; set; }
        public long SizeInBytes { get; set; }
        public string SizeText { get; set; } = "0 B";
        public long DocumentCount { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? LastModified { get; set; }
    }

    /// <summary>Result of a filter-path integrity analysis on a channel database.</summary>
    public class PathAnalysisDto
    {
        public string DatabaseName { get; set; } = string.Empty;
        public int TotalItems { get; set; }
        public int ItemsWithIssues { get; set; }
        public int FilterPathIssues { get; set; }
        public int FilterIdIssues { get; set; }
        public int FilePathIssues { get; set; }
        public bool HasIssues { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>Application configuration exposed for reading and updating.</summary>
    public class AppConfigDto
    {
        public bool ShouldNotify { get; set; }
        public int TimeSleepBetweenTransactions { get; set; }
        public int SplitSize { get; set; }
        public int MaxSimultaneousDownloads { get; set; }
        public bool CheckHash { get; set; }
        public int MaxImageUploadSizeInMb { get; set; }
        public int MaxPreloadFileSizeInMb { get; set; }
        public bool ShouldShowCaptionPath { get; set; }
        public bool ShouldShowLogInTerminal { get; set; }

        /// <summary><c>DirectStream</c>, <c>ProgressiveCache</c> or <c>Preload</c>.</summary>
        public string StrmStreamingMode { get; set; } = nameof(StreamingMode.DirectStream);

        public bool ShouldShowPaginatedFileChannel { get; set; }
        public bool ShowChannelImages { get; set; }
        public List<long> FavouriteChannels { get; set; } = new();

        public bool EnableTaskPersistence { get; set; }
        public int TaskPersistenceDebounceSeconds { get; set; }
        public int StaleTaskCleanupDays { get; set; }
        public bool AutoResumeOnStartup { get; set; }

        public bool EnableVideoTranscoding { get; set; }
        public bool EnableRefreshOwnChannels { get; set; }

        public bool EnableMemorySplitUpload { get; set; }
        public int MemorySplitSizeGB { get; set; }
        public int ParallelTransfers { get; set; }

        public bool EnableMultiConnectionDownloads { get; set; }
        public int DownloadConnections { get; set; }
        public int MultiConnectionPartSizeKB { get; set; }
        public int MultiConnectionBlockSizeMB { get; set; }
        public int MultiConnectionMinFileSizeMB { get; set; }

        public WebDavConfigDto WebDav { get; set; } = new();

        public static AppConfigDto From(GeneralConfig c) => new()
        {
            ShouldNotify = c.ShouldNotify,
            TimeSleepBetweenTransactions = c.TimeSleepBetweenTransactions,
            SplitSize = c.SplitSize,
            MaxSimultaneousDownloads = c.MaxSimultaneousDownloads,
            CheckHash = c.CheckHash,
            MaxImageUploadSizeInMb = c.MaxImageUploadSizeInMb,
            MaxPreloadFileSizeInMb = c.MaxPreloadFileSizeInMb,
            ShouldShowCaptionPath = c.ShouldShowCaptionPath,
            ShouldShowLogInTerminal = c.ShouldShowLogInTerminal,
            StrmStreamingMode = c.GetEffectiveStreamingMode().ToString(),
            ShouldShowPaginatedFileChannel = c.ShouldShowPaginatedFileChannel,
            ShowChannelImages = c.ShowChannelImages,
            FavouriteChannels = c.FavouriteChannels ?? new List<long>(),
            EnableTaskPersistence = c.EnableTaskPersistence,
            TaskPersistenceDebounceSeconds = c.TaskPersistenceDebounceSeconds,
            StaleTaskCleanupDays = c.StaleTaskCleanupDays,
            AutoResumeOnStartup = c.AutoResumeOnStartup,
            EnableVideoTranscoding = c.EnableVideoTranscoding,
            EnableRefreshOwnChannels = c.EnableRefreshOwnChannels,
            EnableMemorySplitUpload = c.EnableMemorySplitUpload,
            MemorySplitSizeGB = c.MemorySplitSizeGB,
            ParallelTransfers = c.ParallelTransfers,
            EnableMultiConnectionDownloads = c.EnableMultiConnectionDownloads,
            DownloadConnections = c.DownloadConnections,
            MultiConnectionPartSizeKB = c.MultiConnectionPartSizeKB,
            MultiConnectionBlockSizeMB = c.MultiConnectionBlockSizeMB,
            MultiConnectionMinFileSizeMB = c.MultiConnectionMinFileSizeMB,
            WebDav = new WebDavConfigDto
            {
                Host = c.webDav?.Host ?? "127.0.0.1",
                InternalPort = c.webDav?.PuertoEntrada ?? 0,
                ExternalPort = c.webDav?.PuertoSalida ?? 0,
                IsRunning = c.webDav?.webDavService?.IsRunning ?? false
            }
        };
    }

    /// <summary>WebDAV bridge settings and state.</summary>
    public class WebDavConfigDto
    {
        public string Host { get; set; } = "127.0.0.1";
        public int InternalPort { get; set; }
        public int ExternalPort { get; set; }
        public bool IsRunning { get; set; }
    }

    /// <summary>
    /// Partial configuration update. Only the properties present in the request
    /// body are applied; everything else keeps its current value.
    /// </summary>
    public class UpdateConfigRequest
    {
        public bool? ShouldNotify { get; set; }
        public int? TimeSleepBetweenTransactions { get; set; }
        public int? SplitSize { get; set; }
        public int? MaxSimultaneousDownloads { get; set; }
        public bool? CheckHash { get; set; }
        public int? MaxImageUploadSizeInMb { get; set; }
        public int? MaxPreloadFileSizeInMb { get; set; }
        public bool? ShouldShowCaptionPath { get; set; }
        public bool? ShouldShowLogInTerminal { get; set; }
        public string? StrmStreamingMode { get; set; }
        public bool? ShouldShowPaginatedFileChannel { get; set; }
        public bool? ShowChannelImages { get; set; }
        public bool? EnableTaskPersistence { get; set; }
        public int? TaskPersistenceDebounceSeconds { get; set; }
        public int? StaleTaskCleanupDays { get; set; }
        public bool? AutoResumeOnStartup { get; set; }
        public bool? EnableVideoTranscoding { get; set; }
        public bool? EnableRefreshOwnChannels { get; set; }
        public bool? EnableMemorySplitUpload { get; set; }
        public int? MemorySplitSizeGB { get; set; }
        public int? ParallelTransfers { get; set; }
        public bool? EnableMultiConnectionDownloads { get; set; }
        public int? DownloadConnections { get; set; }
        public int? MultiConnectionPartSizeKB { get; set; }
        public int? MultiConnectionBlockSizeMB { get; set; }
        public int? MultiConnectionMinFileSizeMB { get; set; }
        public string? WebDavHost { get; set; }
        public int? WebDavInternalPort { get; set; }
        public int? WebDavExternalPort { get; set; }
    }

    /// <summary>One application log record.</summary>
    public class LogEntryDto
    {
        public string Id { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string? Level { get; set; }
        public string? Message { get; set; }
        public string? Logger { get; set; }
        public string? Exception { get; set; }
        public string? Version { get; set; }
    }

    /// <summary>Query string for <c>GET /api/v1/system/logs</c>.</summary>
    public class LogQuery : PagedQuery
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }

        /// <summary><c>Verbose</c>, <c>Debug</c>, <c>Information</c>, <c>Warning</c>, <c>Error</c>, <c>Fatal</c>.</summary>
        public string? Level { get; set; }

        public string? Logger { get; set; }
        public string? Version { get; set; }
        public string? Search { get; set; }
    }

    /// <summary>A shared file collection published by another user.</summary>
    public class SharedCollectionDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? ChannelId { get; set; }
        public string? CollectionId { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/shares/import</c>.</summary>
    public class ImportSharedRequest
    {
        /// <summary>Share payload, normally obtained from <c>GET /api/file/share/{id}</c>.</summary>
        public ShareFilesModel Share { get; set; } = new();
    }

    /// <summary>Body of <c>POST /api/v1/channels/{id}/strm</c>.</summary>
    public class CreateStrmRequest
    {
        /// <summary>Channel folder to export, e.g. <c>/movies/</c>.</summary>
        public string Path { get; set; } = "/";

        /// <summary>Base URL written inside the .strm files. Defaults to the request host.</summary>
        public string? Host { get; set; }

        /// <summary>
        /// When set, .strm files are written to this folder under the server local
        /// root instead of being returned as a zip download link.
        /// </summary>
        public string? DestinationFolder { get; set; }
    }
}
