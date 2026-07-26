using Microsoft.AspNetCore.Mvc;
using Syncfusion.Blazor.FileManager;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Everything that moves bytes: pulling files out of Telegram onto the
    /// server, pushing server files into Telegram, and controlling the queue.
    ///
    /// These endpoints only enqueue work and return immediately. Progress is
    /// published on the <c>/hubs/transfers</c> SignalR hub; the snapshot
    /// endpoint below returns the very same payload for clients that prefer
    /// polling or need an initial state.
    /// </summary>
    [Route("api/v1/transfers")]
    [Tags("Transfers")]
    public class TransfersController : ApiV1ControllerBase
    {
        private readonly TransactionInfoService _tis;
        private readonly IFileService _files;
        private readonly IDbService _db;
        private readonly ITelegramService _telegram;
        private readonly ITaskPersistenceService _persistence;
        private readonly ILogger<TransfersController> _logger;

        public TransfersController(
            TransactionInfoService tis,
            IFileService files,
            IDbService db,
            ITelegramService telegram,
            ITaskPersistenceService persistence,
            ILogger<TransfersController> logger)
        {
            _tis = tis;
            _files = files;
            _db = db;
            _telegram = telegram;
            _persistence = persistence;
            _logger = logger;
        }

        /// <summary>Full snapshot of active and queued transfers.</summary>
        /// <remarks>
        /// Identical payload to the <c>TransfersSnapshot</c> hub message. Prefer
        /// the hub for live updates and use this once at startup, or when a
        /// client reconnects and wants to resynchronise.
        /// </remarks>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResult<TransfersSnapshotDto>), StatusCodes.Status200OK)]
        public IActionResult Snapshot() => OkResult(TransferSnapshotBuilder.BuildSnapshot(_tis));

        /// <summary>Counters and current transfer speeds.</summary>
        /// <remarks>Identical payload to the <c>TransferSummary</c> hub message.</remarks>
        [HttpGet("summary")]
        [ProducesResponseType(typeof(ApiResult<TransferSummaryDto>), StatusCodes.Status200OK)]
        public IActionResult Summary() => OkResult(TransferSnapshotBuilder.BuildSummary(_tis));

        /// <summary>Retained download/upload speed samples, for charts.</summary>
        [HttpGet("speed-history")]
        [ProducesResponseType(typeof(ApiResult<SpeedHistoryDto>), StatusCodes.Status200OK)]
        public IActionResult SpeedHistory() => OkResult(TransferSnapshotBuilder.BuildSpeedHistory(_tis));

        /// <summary>Lists downloads.</summary>
        /// <param name="queued">List the queue instead of the running downloads.</param>
        /// <param name="query">Paging.</param>
        [HttpGet("downloads")]
        [ProducesResponseType(typeof(ApiResult<List<TransferDto>>), StatusCodes.Status200OK)]
        public IActionResult Downloads([FromQuery] bool queued = false, [FromQuery] PagedQuery? query = null)
        {
            query ??= new PagedQuery();
            var source = (queued ? _tis.pendingDownloadModels : _tis.downloadModels)
                .ToList()
                .Select(d => TransferDto.FromDownload(d, queued))
                .ToList();
            var (items, page) = Paginate(source, query);
            return OkPaged(items, page);
        }

        /// <summary>Lists uploads.</summary>
        /// <param name="queued">List the queue instead of the running uploads.</param>
        /// <param name="query">Paging.</param>
        [HttpGet("uploads")]
        [ProducesResponseType(typeof(ApiResult<List<TransferDto>>), StatusCodes.Status200OK)]
        public IActionResult Uploads([FromQuery] bool queued = false, [FromQuery] PagedQuery? query = null)
        {
            query ??= new PagedQuery();
            var source = (queued ? _tis.pendingUploadModels : _tis.uploadModels)
                .ToList()
                .Select(u => TransferDto.FromUpload(u, queued))
                .ToList();
            var (items, page) = Paginate(source, query);
            return OkPaged(items, page);
        }

        /// <summary>Lists batch tasks (a folder download or upload as a whole).</summary>
        [HttpGet("tasks")]
        [ProducesResponseType(typeof(ApiResult<List<TransferDto>>), StatusCodes.Status200OK)]
        public IActionResult Tasks([FromQuery] PagedQuery? query = null)
        {
            query ??= new PagedQuery();
            var source = _tis.infoDownloadTaksModel
                .ToList()
                .OrderBy(t => t.creationDate)
                .Select(TransferDto.FromBatch)
                .ToList();
            var (items, page) = Paginate(source, query);
            return OkPaged(items, page);
        }

        /// <summary>Details of a single transfer, whatever its kind.</summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResult<TransferDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult Get(string id)
        {
            if (!TransferSnapshotBuilder.TryFind(_tis, id, out var download, out var upload, out var task))
                return NotFoundResult("Transfer not found", ApiErrorCodes.TaskNotFound);

            if (download != null)
                return OkResult(TransferDto.FromDownload(download, _tis.pendingDownloadModels.Contains(download)));
            if (upload != null)
                return OkResult(TransferDto.FromUpload(upload, _tis.pendingUploadModels.Contains(upload)));
            return OkResult(TransferDto.FromBatch(task!));
        }

        /// <summary>Downloads channel files onto the server.</summary>
        /// <remarks>
        /// Accepts file ids and folder ids; folders are pulled recursively. The
        /// call returns as soon as the work is queued, and each file then shows
        /// up as its own entry on the <c>transfers</c> hub.
        ///
        /// <c>targetPath</c> is relative to the server local root. When omitted,
        /// the channel folder structure is reproduced under it.
        /// </remarks>
        [HttpPost("downloads")]
        [RequireTelegramSession]
        [ProducesResponseType(typeof(ApiResult<TransferAcceptedDto>), StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> StartDownload([FromBody] StartDownloadRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ChannelId))
                return BadRequestResult("A channel id is required");
            if (request.FileIds == null || request.FileIds.Count == 0)
                return BadRequestResult("At least one file id is required");

            try
            {
                var dbName = string.IsNullOrEmpty(request.SharedCollectionId)
                    ? request.ChannelId
                    : DbService.SHARED_DB_NAME;

                var contents = new List<FileManagerDirectoryContent>();
                var skipped = new List<string>();

                foreach (var id in request.FileIds)
                {
                    var entry = string.IsNullOrEmpty(request.SharedCollectionId)
                        ? await _db.getFileById(request.ChannelId, id)
                        : await _db.getFileById(dbName, id, request.SharedCollectionId);

                    if (entry == null) skipped.Add(id);
                    else contents.Add(entry.toFileManagerContent());
                }

                if (contents.Count == 0)
                    return BadRequestResult("None of the supplied ids could be resolved", ApiErrorCodes.FileNotFound);

                var targetPath = string.IsNullOrWhiteSpace(request.TargetPath) ? null : request.TargetPath;

                // The download pipeline is long running; hand it off so the
                // client is not blocked while files stream in.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _files.downloadFile(
                            dbName,
                            contents,
                            targetPath,
                            request.SharedCollectionId,
                            string.IsNullOrEmpty(request.SharedCollectionId) ? null : request.ChannelId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background download from channel {ChannelId} failed", request.ChannelId);
                    }
                });

                return Accepted(ApiResult<TransferAcceptedDto>.Ok(
                    new TransferAcceptedDto { Accepted = contents.Count, Skipped = skipped },
                    "Download queued"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing a download from channel {ChannelId}", request.ChannelId);
                return ErrorResult("Could not queue the download", ex);
            }
        }

        /// <summary>Uploads server files into a channel.</summary>
        /// <remarks>
        /// <c>localPaths</c> are relative to the server local root; folders are
        /// pushed recursively. The whole request becomes one batch task, visible
        /// under <c>tasks</c> in the snapshot, which in turn spawns one upload
        /// entry per file.
        /// </remarks>
        [HttpPost("uploads")]
        [RequireTelegramSession]
        [ProducesResponseType(typeof(ApiResult<TransferAcceptedDto>), StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> StartUpload([FromBody] StartUploadRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ChannelId))
                return BadRequestResult("A channel id is required");
            if (request.LocalPaths == null || request.LocalPaths.Count == 0)
                return BadRequestResult("At least one local path is required");

            try
            {
                var contents = new List<FileManagerDirectoryContent>();
                var skipped = new List<string>();

                foreach (var relative in request.LocalPaths)
                {
                    var content = BuildLocalContent(relative);
                    if (content == null) skipped.Add(relative);
                    else contents.Add(content);
                }

                if (contents.Count == 0)
                    return BadRequestResult("None of the supplied paths exist under the local root", ApiErrorCodes.FileNotFound);

                var targetPath = ChannelFolderResolver.NormalizeFolderPath(request.TargetPath);
                await _files.AddUploadFileFromServer(request.ChannelId, targetPath, contents);

                var task = _tis.infoDownloadTaksModel.LastOrDefault(t => t.isUpload);
                return Accepted(ApiResult<TransferAcceptedDto>.Ok(
                    new TransferAcceptedDto
                    {
                        Accepted = contents.Count,
                        Skipped = skipped,
                        TaskId = task?._internalId
                    },
                    "Upload queued"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing an upload to channel {ChannelId}", request.ChannelId);
                return ErrorResult("Could not queue the upload", ex);
            }
        }

        /// <summary>Downloads the media attached to raw Telegram messages.</summary>
        /// <remarks>
        /// Works on any chat, indexed or not: this is how the web UI saves a
        /// file straight from the message list.
        /// </remarks>
        [HttpPost("messages")]
        [RequireTelegramSession]
        [ProducesResponseType(typeof(ApiResult<TransferAcceptedDto>), StatusCodes.Status202Accepted)]
        public async Task<IActionResult> DownloadMessages([FromBody] DownloadMessagesRequest request)
        {
            if (request == null || request.MessageIds == null || request.MessageIds.Count == 0)
                return BadRequestResult("At least one message id is required");

            var accepted = 0;
            var skipped = new List<string>();

            foreach (var messageId in request.MessageIds)
            {
                try
                {
                    var message = await _telegram.getMessageFile(request.ChatId.ToString(), messageId);
                    if (message == null)
                    {
                        skipped.Add(messageId.ToString());
                        continue;
                    }

                    var chatMessage = new ChatMessages { message = message, isDocument = true };
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _files.DownloadFileFromChat(chatMessage, null, request.TargetPath, null);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Download of message {MessageId} failed", messageId);
                        }
                    });
                    accepted++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not resolve message {MessageId} of chat {ChatId}", messageId, request.ChatId);
                    skipped.Add(messageId.ToString());
                }
            }

            return Accepted(ApiResult<TransferAcceptedDto>.Ok(
                new TransferAcceptedDto { Accepted = accepted, Skipped = skipped },
                "Message downloads queued"));
        }

        /// <summary>Pauses the whole download queue.</summary>
        /// <remarks>
        /// Running downloads are paused and pushed back to the front of the
        /// queue, so resuming continues where they stopped.
        /// </remarks>
        [HttpPost("downloads/pause")]
        [ProducesResponseType(typeof(ApiResult<TransferSummaryDto>), StatusCodes.Status200OK)]
        public IActionResult PauseDownloads()
        {
            _tis.PauseDownloads();
            return OkResult(TransferSnapshotBuilder.BuildSummary(_tis), "Downloads paused");
        }

        /// <summary>Resumes the download queue.</summary>
        [HttpPost("downloads/resume")]
        [ProducesResponseType(typeof(ApiResult<TransferSummaryDto>), StatusCodes.Status200OK)]
        public IActionResult ResumeDownloads()
        {
            _tis.PlayDownloads();
            return OkResult(TransferSnapshotBuilder.BuildSummary(_tis), "Downloads resumed");
        }

        /// <summary>Stops every download and empties the queue.</summary>
        [HttpPost("downloads/stop")]
        [ProducesResponseType(typeof(ApiResult<TransferSummaryDto>), StatusCodes.Status200OK)]
        public IActionResult StopDownloads()
        {
            _tis.StopDownloads();
            return OkResult(TransferSnapshotBuilder.BuildSummary(_tis), "Downloads stopped");
        }

        /// <summary>Cancels one transfer.</summary>
        /// <remarks>
        /// Cancelling a batch task also cancels the individual downloads and
        /// uploads it spawned.
        /// </remarks>
        [HttpPost("{id}/cancel")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult Cancel(string id)
        {
            if (!TransferSnapshotBuilder.TryFind(_tis, id, out var download, out var upload, out var task))
                return NotFoundResult("Transfer not found", ApiErrorCodes.TaskNotFound);

            download?.Cancel();
            upload?.Cancel();
            task?.cancelTask();
            return OkEmpty("Transfer cancelled");
        }

        /// <summary>Pauses one download.</summary>
        [HttpPost("{id}/pause")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult Pause(string id)
        {
            var download = _tis.downloadModels.FirstOrDefault(d => d._internalId == id);
            if (download == null)
                return NotFoundResult("No running download with that id", ApiErrorCodes.TaskNotFound);

            _tis.addToPendingDownloadList(download, atFirst: true, chekDownloads: false);
            download.Pause();
            return OkEmpty("Download paused");
        }

        /// <summary>Retries a paused, cancelled or failed transfer.</summary>
        [HttpPost("{id}/retry")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult Retry(string id)
        {
            if (!TransferSnapshotBuilder.TryFind(_tis, id, out var download, out _, out var task))
                return NotFoundResult("Transfer not found", ApiErrorCodes.TaskNotFound);

            if (task != null)
            {
                task.Retry();
                return OkEmpty("Task queued again");
            }

            if (download != null)
            {
                if (!_tis.pendingDownloadModels.Contains(download))
                    _tis.addToPendingDownloadList(download, atFirst: true);
                else
                    _ = _tis.CheckPendingDownloads();
                return OkEmpty("Download queued again");
            }

            return BadRequestResult("Only downloads and batch tasks can be retried", ApiErrorCodes.NotSupported);
        }

        /// <summary>Removes finished entries (completed, cancelled and failed) from a list.</summary>
        /// <param name="scope"><c>downloads</c>, <c>uploads</c>, <c>tasks</c> or <c>all</c>.</param>
        [HttpPost("clear")]
        [ProducesResponseType(typeof(ApiResult<TransfersSnapshotDto>), StatusCodes.Status200OK)]
        public IActionResult Clear([FromQuery] string scope = "all")
        {
            switch (scope?.ToLowerInvariant())
            {
                case "downloads":
                    _tis.clearDownloadCompleted();
                    break;
                case "uploads":
                    _tis.clearUploadCompleted();
                    break;
                case "tasks":
                    _tis.clearTasksCompleted();
                    break;
                case "all":
                case null:
                case "":
                    _tis.clearDownloadCompleted();
                    _tis.clearUploadCompleted();
                    _tis.clearTasksCompleted();
                    break;
                default:
                    return BadRequestResult("scope must be one of: downloads, uploads, tasks, all");
            }

            return OkResult(TransferSnapshotBuilder.BuildSnapshot(_tis), "Finished entries cleared");
        }

        /// <summary>Empties a queue without touching what is already running.</summary>
        /// <param name="scope"><c>downloads</c>, <c>uploads</c> or <c>all</c>.</param>
        [HttpPost("queue/clear")]
        [ProducesResponseType(typeof(ApiResult<TransfersSnapshotDto>), StatusCodes.Status200OK)]
        public IActionResult ClearQueue([FromQuery] string scope = "all")
        {
            switch (scope?.ToLowerInvariant())
            {
                case "downloads":
                    _tis.ClearPendingDownloads();
                    break;
                case "uploads":
                    _tis.ClearPendingUploads();
                    break;
                default:
                    _tis.ClearPendingDownloads();
                    _tis.ClearPendingUploads();
                    break;
            }

            return OkResult(TransferSnapshotBuilder.BuildSnapshot(_tis), "Queue cleared");
        }

        /// <summary>Lists the transfers persisted in MongoDB.</summary>
        /// <remarks>
        /// Persisted transfers survive an application restart: on startup the
        /// app reloads them and, when <c>autoResumeOnStartup</c> is enabled,
        /// resumes them from the last confirmed byte.
        /// </remarks>
        [HttpGet("persisted")]
        [ProducesResponseType(typeof(ApiResult<List<PersistedTaskDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Persisted([FromQuery] PagedQuery? query = null)
        {
            query ??= new PagedQuery();
            try
            {
                var tasks = await _persistence.LoadPendingTasks();
                var items = (tasks ?? new List<Models.Persistence.PersistedTaskModel>())
                    .Select(PersistedTaskDto.From)
                    .OrderByDescending(t => t.LastUpdated)
                    .ToList();
                var (page, info) = Paginate(items, query);
                return OkPaged(page, info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing persisted tasks");
                return ErrorResult("Could not list the persisted tasks", ex);
            }
        }

        /// <summary>Deletes one persisted transfer.</summary>
        [HttpDelete("persisted/{internalId}")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> DeletePersisted(string internalId)
        {
            try
            {
                await _db.DeleteTask(internalId);
                return OkEmpty("Persisted task deleted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting persisted task {InternalId}", internalId);
                return ErrorResult("Could not delete the persisted task", ex);
            }
        }

        /// <summary>Deletes every persisted transfer.</summary>
        [HttpDelete("persisted")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ClearPersisted()
        {
            try
            {
                await _db.ClearAllTasks();
                return OkEmpty("Persisted tasks cleared");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing persisted tasks");
                return ErrorResult("Could not clear the persisted tasks", ex);
            }
        }

        /// <summary>
        /// Builds the descriptor the upload pipeline expects for a path under
        /// the server local root. Returns null when the path escapes the root or
        /// does not exist.
        /// </summary>
        private static FileManagerDirectoryContent? BuildLocalContent(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;

            var normalized = relativePath.Replace("\\", "/").TrimStart('/');
            var absolute = Path.GetFullPath(Path.Combine(FileService.LOCALDIR, normalized));
            var root = Path.GetFullPath(FileService.LOCALDIR);

            if (!absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return null;

            var parent = Path.GetDirectoryName(normalized)?.Replace("\\", "/") ?? string.Empty;
            var filterPath = string.IsNullOrEmpty(parent) ? "/" : "/" + parent + "/";
            var name = Path.GetFileName(normalized);

            if (System.IO.File.Exists(absolute))
            {
                var info = new System.IO.FileInfo(absolute);
                return new FileManagerDirectoryContent
                {
                    Name = name,
                    IsFile = true,
                    Size = info.Length,
                    FilterPath = filterPath,
                    Type = info.Extension,
                    DateModified = info.LastWriteTime,
                    DateCreated = info.CreationTime
                };
            }

            if (Directory.Exists(absolute))
            {
                var info = new DirectoryInfo(absolute);
                return new FileManagerDirectoryContent
                {
                    Name = name,
                    IsFile = false,
                    Size = 0,
                    HasChild = info.EnumerateFileSystemInfos().Any(),
                    FilterPath = filterPath,
                    Type = "folder",
                    DateModified = info.LastWriteTime,
                    DateCreated = info.CreationTime
                };
            }

            return null;
        }
    }
}
