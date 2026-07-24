using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Application settings: transfer tuning, streaming behaviour, task
    /// persistence and the WebDAV bridge.
    ///
    /// Settings are global and shared with the web UI: changing them here
    /// changes them everywhere.
    /// </summary>
    [Route("api/v1/config")]
    [Tags("Configuration")]
    public class ConfigController : ApiV1ControllerBase
    {
        private readonly IDbService _db;
        private readonly ILogger<ConfigController> _logger;

        public ConfigController(IDbService db, ILogger<ConfigController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>Reads the current configuration.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResult<AppConfigDto>), StatusCodes.Status200OK)]
        public IActionResult Get() => OkResult(AppConfigDto.From(GeneralConfigStatic.config));

        /// <summary>
        /// Updates the configuration. Only the fields present in the body are
        /// applied, so a client can change one setting without reading the rest.
        /// </summary>
        /// <remarks>
        /// A few values are clamped server-side: <c>memorySplitSizeGB</c> is
        /// capped by the Telegram file-size limit of the account (4 GB for
        /// Premium, 2 GB otherwise) and by <c>splitSize</c>. The response always
        /// returns the effective configuration after clamping.
        /// </remarks>
        [HttpPatch]
        [ProducesResponseType(typeof(ApiResult<AppConfigDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Update([FromBody] UpdateConfigRequest request)
        {
            if (request == null)
                return BadRequestResult("A configuration body is required");

            try
            {
                var c = GeneralConfigStatic.config;

                if (request.ShouldNotify.HasValue) c.ShouldNotify = request.ShouldNotify.Value;
                if (request.TimeSleepBetweenTransactions.HasValue) c.TimeSleepBetweenTransactions = request.TimeSleepBetweenTransactions.Value;
                if (request.SplitSize.HasValue) c.SplitSize = request.SplitSize.Value;
                if (request.MaxSimultaneousDownloads.HasValue) c.MaxSimultaneousDownloads = Math.Max(1, request.MaxSimultaneousDownloads.Value);
                if (request.CheckHash.HasValue) c.CheckHash = request.CheckHash.Value;
                if (request.MaxImageUploadSizeInMb.HasValue) c.MaxImageUploadSizeInMb = request.MaxImageUploadSizeInMb.Value;
                if (request.MaxPreloadFileSizeInMb.HasValue) c.MaxPreloadFileSizeInMb = request.MaxPreloadFileSizeInMb.Value;
                if (request.ShouldShowCaptionPath.HasValue) c.ShouldShowCaptionPath = request.ShouldShowCaptionPath.Value;
                if (request.ShouldShowLogInTerminal.HasValue) c.ShouldShowLogInTerminal = request.ShouldShowLogInTerminal.Value;

                if (!string.IsNullOrWhiteSpace(request.StrmStreamingMode))
                {
                    if (!Enum.TryParse<StreamingMode>(request.StrmStreamingMode, true, out var mode))
                        return BadRequestResult("strmStreamingMode must be DirectStream, ProgressiveCache or Preload");
                    c.StrmStreamingMode = mode;
                }

                if (request.ShouldShowPaginatedFileChannel.HasValue) c.ShouldShowPaginatedFileChannel = request.ShouldShowPaginatedFileChannel.Value;
                if (request.ShowChannelImages.HasValue) c.ShowChannelImages = request.ShowChannelImages.Value;

                if (request.EnableTaskPersistence.HasValue) c.EnableTaskPersistence = request.EnableTaskPersistence.Value;
                if (request.TaskPersistenceDebounceSeconds.HasValue) c.TaskPersistenceDebounceSeconds = request.TaskPersistenceDebounceSeconds.Value;
                if (request.StaleTaskCleanupDays.HasValue) c.StaleTaskCleanupDays = request.StaleTaskCleanupDays.Value;
                if (request.AutoResumeOnStartup.HasValue) c.AutoResumeOnStartup = request.AutoResumeOnStartup.Value;

                if (request.EnableVideoTranscoding.HasValue) c.EnableVideoTranscoding = request.EnableVideoTranscoding.Value;
                if (request.EnableRefreshOwnChannels.HasValue) c.EnableRefreshOwnChannels = request.EnableRefreshOwnChannels.Value;

                if (request.EnableMemorySplitUpload.HasValue) c.EnableMemorySplitUpload = request.EnableMemorySplitUpload.Value;
                if (request.MemorySplitSizeGB.HasValue) c.MemorySplitSizeGB = request.MemorySplitSizeGB.Value;
                if (request.ParallelTransfers.HasValue) c.ParallelTransfers = Math.Clamp(request.ParallelTransfers.Value, 1, 16);

                if (request.EnableMultiConnectionDownloads.HasValue) c.EnableMultiConnectionDownloads = request.EnableMultiConnectionDownloads.Value;
                if (request.DownloadConnections.HasValue) c.DownloadConnections = Math.Clamp(request.DownloadConnections.Value, 2, 8);
                if (request.MultiConnectionPartSizeKB.HasValue) c.MultiConnectionPartSizeKB = request.MultiConnectionPartSizeKB.Value;
                if (request.MultiConnectionBlockSizeMB.HasValue) c.MultiConnectionBlockSizeMB = Math.Clamp(request.MultiConnectionBlockSizeMB.Value, 1, 16);
                if (request.MultiConnectionMinFileSizeMB.HasValue) c.MultiConnectionMinFileSizeMB = request.MultiConnectionMinFileSizeMB.Value;

                await GeneralConfigStatic.SaveChanges(_db, c);

                return OkResult(AppConfigDto.From(GeneralConfigStatic.config), "Configuration saved");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating the configuration");
                return ErrorResult("Could not update the configuration", ex);
            }
        }

    }
}
