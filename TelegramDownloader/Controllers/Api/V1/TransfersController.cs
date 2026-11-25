using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;
using TelegramDownloader.Services.Upload;
using TelegramDownloader.Models.FileSystem;

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

                var contents = new List<FsEntry>();
                var skipped = new List<string>();

                foreach (var id in request.FileIds)
                {
                    var entry = string.IsNullOrEmpty(request.SharedCollectionId)
                        ? await _db.getFileById(request.ChannelId, id)
                        : await _db.getFileById(dbName, id, request.SharedCollectionId);

                    if (entry == null) skipped.Add(id);
                    else contents.Add(entry.ToFsEntry());
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
                var (contents, skipped) = BuildLocalContents(request.LocalPaths);

                if (contents.Count == 0)
                    return BadRequestResult("None of the supplied paths exist under the local root", ApiErrorCodes.FileNotFound);

                var targetPath = ChannelFolderResolver.NormalizeFolderPath(request.TargetPath);
                // The id of the batch this request created; looking up the last
                // upload task could return another request's task under concurrency.
                var taskId = await _files.AddUploadFileFromServer(request.ChannelId, targetPath, contents);

                return Accepted(ApiResult<TransferAcceptedDto>.Ok(
                    new TransferAcceptedDto
                    {
                        Accepted = contents.Count,
                        Skipped = skipped,
                        TaskId = taskId
                    },
                    "Upload queued"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing an upload to channel {ChannelId}", request.ChannelId);
                return ErrorResult("Could not queue the upload", ex);
            }
        }

        /// <summary>Dry run of an upload: what <c>POST /transfers/uploads</c> would do.</summary>
        /// <remarks>
        /// Takes the same body as the upload and applies the very same decisions
        /// (shared planner) without sending anything: files that are new, files
        /// that replace an older indexed copy, files skipped (empty, or a newer
        /// copy already indexed), bytes to send and folders to create. With
        /// <c>findDuplicates</c> it also reports files to upload whose content
        /// (same stored XXHash and size) already exists elsewhere in the channel;
        /// that option reads the matching local files from disk. Nothing is
        /// queued and no Telegram session is needed.
        /// </remarks>
        [HttpPost("uploads/preview")]
        [ProducesResponseType(typeof(ApiResult<UploadPreviewResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PreviewUpload([FromBody] UploadPreviewRequest request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ChannelId))
                return BadRequestResult("A channel id is required");
            if (request.LocalPaths == null || request.LocalPaths.Count == 0)
                return BadRequestResult("At least one local path is required");

            try
            {
                var (contents, missing) = BuildLocalContents(request.LocalPaths);
                if (contents.Count == 0)
                    return BadRequestResult("None of the supplied paths exist under the local root", ApiErrorCodes.FileNotFound);

                var targetPath = ChannelFolderResolver.NormalizeFolderPath(request.TargetPath);
                int maxItems = Math.Clamp(request.MaxItems ?? UploadPreviewPlanner.DefaultMaxItems, 0, 5000);
                var preview = await _files.PreviewUploadFromServer(request.ChannelId, targetPath, contents, request.FindDuplicates, maxItems, cancellationToken);
                preview.MissingLocalPaths = missing;
                return OkResult(preview);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error previewing an upload to channel {ChannelId}", request.ChannelId);
                return ErrorResult("Could not preview the upload", ex);
            }
        }

        /// <summary>Downloads the media attached to raw Telegram messages.</summary>
        /// <remarks>
        /// Works on any chat, indexed or not: this is how the web UI saves
        /// files straight from the message list. <c>targetPath</c> is a folder
        /// relative to the server local root (created if needed; <c>temp</c>
        /// when omitted). Names already used in it get a number, so messages that
        /// share a file name do not overwrite each other.
        /// </remarks>
        [HttpPost("messages")]
        [RequireTelegramSession]
        [ProducesResponseType(typeof(ApiResult<TransferAcceptedDto>), StatusCodes.Status202Accepted)]
        public async Task<IActionResult> DownloadMessages([FromBody] DownloadMessagesRequest request)
        {
            if (request == null || request.MessageIds == null || request.MessageIds.Count == 0)
                return BadRequestResult("At least one message id is required");

            string folder;
            if (string.IsNullOrWhiteSpace(request.TargetPath))
                folder = FileService.TEMPDIR;
            else if (!LocalPathGuard.TryResolve(FileService.LOCALDIR, request.TargetPath, out folder, out _, out var error))
                return BadRequestResult(error ?? "Invalid target path");

            Dictionary<int, TL.Message> messages;
            string chatName;
            try
            {
                messages = (await _telegram.getMessagesById(request.ChatId, request.MessageIds)).ToDictionary(m => m.id);
                chatName = _telegram.getChatName(request.ChatId);
                Directory.CreateDirectory(folder);
            }
            catch (KeyNotFoundException)
            {
                return NotFoundResult("Channel not found", ApiErrorCodes.ChannelNotFound);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not read {Count} messages of chat {ChatId}", request.MessageIds.Count, request.ChatId);
                return ErrorResult("Could not read the messages from Telegram", ex);
            }

            var accepted = 0;
            var skipped = new List<string>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var messageId in request.MessageIds.Distinct())
            {
                var attachment = messages.TryGetValue(messageId, out var message) ? MessageAttachment.From(message) : null;
                if (attachment == null)
                {
                    skipped.Add(messageId.ToString());
                    continue;
                }
                try
                {
                    var name = MessageDownloads.UniqueName(attachment.Name, names, n => System.IO.File.Exists(Path.Combine(folder, n)));
                    var model = new DownloadModel { tis = _tis, channelName = chatName };
                    // Only queues it: the download runs when a slot is free.
                    await _files.DownloadFileFromChat(new ChatMessages { message = message, isDocument = !attachment.IsPhoto }, name, folder, model);
                    accepted++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not queue the download of message {MessageId}", messageId);
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

        /// <summary>Pauses one download or upload.</summary>
        /// <remarks>
        /// A download, running or queued, stays paused in the queue until it is
        /// resumed (<c>retry</c>, or resuming all the downloads). An upload stops
        /// sending data. Transfers with <c>canPause: false</c> answer <c>400</c>.
        /// </remarks>
        [HttpPost("{id}/pause")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult Pause(string id)
        {
            if (!TransferSnapshotBuilder.TryFind(_tis, id, out var download, out var upload, out _) || (download == null && upload == null))
                return NotFoundResult("No download or upload with that id", ApiErrorCodes.TaskNotFound);

            if (download != null ? _tis.PauseDownload(download) : upload!.Pause())
                return OkEmpty("Transfer paused");
            return BadRequestResult("This transfer cannot be paused now", ApiErrorCodes.NotSupported);
        }

        /// <summary>Resumes a paused transfer, or retries a cancelled or failed one.</summary>
        [HttpPost("{id}/retry")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult Retry(string id)
        {
            if (!TransferSnapshotBuilder.TryFind(_tis, id, out var download, out var upload, out var task))
                return NotFoundResult("Transfer not found", ApiErrorCodes.TaskNotFound);

            if (upload != null && upload.Resume())
                return OkEmpty("Upload resumed");

            if (task != null)
            {
                task.Retry();
                return OkEmpty("Task queued again");
            }

            if (download != null)
            {
                if (!download.CanRestart)
                    return BadRequestResult("This download cannot be started again", ApiErrorCodes.NotSupported);
                if (_tis.pendingDownloadModels.Contains(download))
                    _tis.ResumeDownload(download);
                else if (!_tis.RetryDownload(download))
                    return ConflictResult("The download is still running");
                return OkEmpty("Download queued again");
            }

            return BadRequestResult("Only downloads and batch tasks can be retried", ApiErrorCodes.NotSupported);
        }

        /// <summary>Removes one entry from the transfer lists ("remove from list").</summary>
        /// <remarks>
        /// For finished entries (completed, failed, cancelled), paused ones and
        /// queued ones (which leave the queue without starting). A running
        /// transfer answers <c>409</c>: cancel it first. Nothing is deleted
        /// from disk or from Telegram, and persisted copies are not touched.
        /// </remarks>
        [HttpDelete("{id}")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status409Conflict)]
        public IActionResult Remove(string id)
        {
            if (!TransferSnapshotBuilder.TryFind(_tis, id, out var download, out var upload, out var task))
                return NotFoundResult("Transfer not found", ApiErrorCodes.TaskNotFound);

            var state = download?.state ?? upload?.state ?? task!.state;
            if (!TransferSnapshotBuilder.CanRemove(state) || upload?.state == StateTask.Paused)
                return ConflictResult("The transfer is running; cancel it before removing it");

            if (download != null)
            {
                if (_tis.pendingDownloadModels.Contains(download)) _tis.deletePendingDownloadInList(download);
                if (_tis.downloadModels.Contains(download)) _tis.deleteDownloadInList(download);
            }
            else if (upload != null)
            {
                if (_tis.pendingUploadModels.Contains(upload)) _tis.deletePendingUploadInList(upload);
                if (_tis.uploadModels.Contains(upload)) _tis.deleteUploadInList(upload);
            }
            else
            {
                _tis.deleteInfoDownloadTaskFromList(task!);
            }

            return OkEmpty("Transfer removed from the list");
        }

        /// <summary>Removes finished entries (completed, cancelled and failed) from a list.</summary>
        /// <param name="scope"><c>downloads</c>, <c>uploads</c>, <c>tasks</c> or <c>all</c>.</param>
        /// <param name="states">
        /// Comma-separated states to restrict the cleanup to: <c>completed</c>,
        /// <c>error</c>, <c>canceled</c>, <c>paused</c> or <c>pending</c>. Omit it
        /// to remove every finished entry, as before. <c>working</c> is rejected:
        /// a running transfer is cancelled, not cleared.
        /// </param>
        [HttpPost("clear")]
        [ProducesResponseType(typeof(ApiResult<TransfersSnapshotDto>), StatusCodes.Status200OK)]
        public IActionResult Clear([FromQuery] string scope = "all", [FromQuery] string? states = null)
        {
            HashSet<StateTask>? wanted;
            if (!TryParseStates(states, out wanted, out string? stateError))
                return BadRequestResult(stateError!);

            switch (scope?.ToLowerInvariant())
            {
                case "downloads":
                    _tis.clearDownloadCompleted(wanted);
                    break;
                case "uploads":
                    _tis.clearUploadCompleted(wanted);
                    break;
                case "tasks":
                    _tis.clearTasksCompleted(wanted);
                    break;
                case "all":
                case null:
                case "":
                    _tis.clearDownloadCompleted(wanted);
                    _tis.clearUploadCompleted(wanted);
                    _tis.clearTasksCompleted(wanted);
                    break;
                default:
                    return BadRequestResult("scope must be one of: downloads, uploads, tasks, all");
            }

            return OkResult(TransferSnapshotBuilder.BuildSnapshot(_tis), "Finished entries cleared");
        }

        /// <summary>
        /// Reads the optional <c>states</c> filter. Null means "every finished
        /// state", which is what the endpoint did before the filter existed.
        /// </summary>
        private static bool TryParseStates(string? states, out HashSet<StateTask>? parsed, out string? error)
        {
            parsed = null;
            error = null;
            if (string.IsNullOrWhiteSpace(states))
                return true;

            HashSet<StateTask> result = new HashSet<StateTask>();
            foreach (string raw in states.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // "failed" reads better than the enum's "error" from a client.
                string name = raw.ToLowerInvariant() switch
                {
                    "failed" => "error",
                    "cancelled" => "canceled",
                    var other => other
                };

                if (!Enum.TryParse(name, ignoreCase: true, out StateTask state) || state == StateTask.Working)
                {
                    error = $"Unknown state '{raw}'. Valid states: completed, error, canceled, paused, pending";
                    return false;
                }
                result.Add(state);
            }

            parsed = result;
            return true;
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

        /// <summary>Resumes or retries one persisted transfer.</summary>
        /// <remarks>
        /// Same code path as the automatic resume on startup: downloads continue
        /// from the last confirmed byte, batch uploads skip the files already in
        /// the destination, and a task whose source no longer exists is marked
        /// as failed with the reason. A task that is already queued or running
        /// is left alone.
        /// </remarks>
        [HttpPost("persisted/{internalId}/resume")]
        [ProducesResponseType(typeof(ApiResult<TaskResumeResultDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ResumePersisted(string internalId,
            [FromServices] TelegramDownloader.Services.Transfers.TaskResumeCoordinator coordinator)
        {
            try
            {
                var result = await coordinator.ResumeAsync(internalId);
                if (result.Action == TelegramDownloader.Services.Transfers.ResumeActionKind.Skip && result.Message == "Task not found")
                    return NotFoundResult("No persisted task with that id", ApiErrorCodes.TaskNotFound);
                return OkResult(new TaskResumeResultDto
                {
                    InternalId = result.InternalId,
                    Resumed = result.Resumed,
                    Action = result.Action.ToString(),
                    Message = result.Message
                }, result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resuming persisted task {InternalId}", internalId);
                return ErrorResult("Could not resume the persisted task", ex);
            }
        }

        /// <summary>Result of resuming a persisted transfer.</summary>
        public class TaskResumeResultDto
        {
            public string InternalId { get; set; } = "";
            /// <summary>True when the transfer was queued again.</summary>
            public bool Resumed { get; set; }
            /// <summary>Skip, Fail, ResumeDownload, ResumeSplitDownload or ResumeBatchUpload.</summary>
            public string Action { get; set; } = "";
            public string? Message { get; set; }
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
        /// <summary>
        /// Resolves the requested local paths the way the upload does; shared by
        /// the upload and its preview so both see the same items.
        /// </summary>
        private static (List<FsEntry> Contents, List<string> Missing) BuildLocalContents(IEnumerable<string> relativePaths)
        {
            var contents = new List<FsEntry>();
            var missing = new List<string>();
            foreach (var relative in relativePaths)
            {
                var content = BuildLocalContent(relative);
                if (content == null) missing.Add(relative);
                else contents.Add(content);
            }
            return (contents, missing);
        }

        private static FsEntry? BuildLocalContent(string relativePath)
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
                return new FsEntry
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
                return new FsEntry
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
