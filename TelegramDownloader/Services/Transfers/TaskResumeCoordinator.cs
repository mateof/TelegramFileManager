#nullable disable
using TelegramDownloader.Data;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Persistence;

namespace TelegramDownloader.Services.Transfers
{
    public sealed class ResumeResult
    {
        public string InternalId { get; init; }
        public string Name { get; init; }
        public ResumeActionKind Action { get; init; }
        /// <summary>True when the task was queued again.</summary>
        public bool Resumed { get; init; }
        public string Message { get; init; }

        public override string ToString() => $"{Name ?? InternalId}: {Action} {Message}";
    }

    /// <summary>
    /// The single code path that puts a persisted transfer back to work: the
    /// automatic resume on startup and the "Resume"/"Retry" buttons (web UI and
    /// API) all end here. Resumes are serialized, never run before the Telegram
    /// session is ready, and never start a transfer that is already queued or
    /// running in this process.
    /// </summary>
    public class TaskResumeCoordinator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ITaskPersistenceService _persistence;
        private readonly TransactionInfoService _tis;
        private readonly ITelegramService _telegram;
        private readonly ILogger<TaskResumeCoordinator> _logger;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly object _scopeLock = new object();
        private IServiceScope _scope;
        private int _startupDone;

        public TaskResumeCoordinator(
            IServiceProvider serviceProvider,
            ITaskPersistenceService persistence,
            TransactionInfoService tis,
            ITelegramService telegram,
            ILogger<TaskResumeCoordinator> logger)
        {
            _serviceProvider = serviceProvider;
            _persistence = persistence;
            _tis = tis;
            _telegram = telegram;
            _logger = logger;
        }

        /// <summary>True when the record belongs to a transfer queued or running now.</summary>
        public bool IsLive(string internalId) => _tis.IsLive(internalId);

        // The file service is transient and depends on scoped UI services; the
        // resumed transfers outlive any request, so they share one long-lived
        // scope owned by this singleton.
        private FileServiceV2 GetFileService()
        {
            lock (_scopeLock)
            {
                _scope ??= _serviceProvider.CreateScope();
                return (FileServiceV2)_scope.ServiceProvider.GetRequiredService<IFileService>();
            }
        }

        /// <summary>
        /// Runs once per process after the session is ready: interrupted records
        /// are set back to Pending and, when <see cref="GeneralConfig.AutoResumeOnStartup"/>
        /// is on, every pending/interrupted task is queued again.
        /// </summary>
        public async Task<IReadOnlyList<ResumeResult>> ResumeOnStartupAsync(CancellationToken cancellationToken)
        {
            var results = new List<ResumeResult>();
            if (Interlocked.Exchange(ref _startupDone, 1) == 1)
                return results;
            if (!_persistence.IsEnabled)
            {
                _logger.LogInformation("Task persistence is disabled - nothing to resume");
                return results;
            }

            await _persistence.CleanupStaleTasks();
            // Nothing from the previous process is running any more: "Working"
            // only means it was interrupted. Done even when auto-resume is off, so
            // the tasks show up as resumable instead of stuck.
            await _persistence.ResetInterruptedTasks(_tis.IsLive);

            if (!(GeneralConfigStatic.config?.AutoResumeOnStartup ?? true))
            {
                _logger.LogInformation("Auto-resume on startup is disabled - pending tasks wait for a manual resume");
                return results;
            }

            var tasks = TransferResumePlanner.SelectForStartup(await _persistence.LoadPendingTasks(), _tis.IsLive);
            _logger.LogInformation("Resuming {Count} pending task(s) after startup", tasks.Count);
            foreach (var task in tasks)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                var result = await ResumeCoreAsync(task, ResumeTrigger.Startup);
                _logger.LogInformation("Startup resume - {Result}", result);
                results.Add(result);
            }
            return results;
        }

        /// <summary>Manual resume / retry of one persisted task.</summary>
        public async Task<ResumeResult> ResumeAsync(string internalId)
        {
            var task = await _persistence.GetTask(internalId);
            if (task == null)
                return new ResumeResult { InternalId = internalId, Action = ResumeActionKind.Skip, Message = "Task not found" };
            return await ResumeCoreAsync(task, ResumeTrigger.Manual);
        }

        private async Task<ResumeResult> ResumeCoreAsync(PersistedTaskModel task, ResumeTrigger trigger)
        {
            await _gate.WaitAsync();
            try
            {
                if (!_telegram.IsConfigured || !_telegram.checkUserLogin())
                    return Result(task, ResumeActionKind.Skip, false, "The Telegram session is not ready yet");

                // Failed earlier in this same process: retry the model in place.
                if (trigger == ResumeTrigger.Manual && !_tis.IsLive(task.InternalId))
                {
                    var retried = RetryInMemory(task);
                    if (retried != null)
                        return retried;
                }

                var plan = TransferResumePlanner.Plan(task, trigger, _tis.IsLive(task.InternalId), PhysicalResumeFileSystem.Instance, FileService.LOCALDIR);
                if ((plan.Kind == ResumeActionKind.ResumeDownload || plan.Kind == ResumeActionKind.ResumeSplitDownload)
                    && _tis.IsDestinationBusy(task.DestinationPath))
                {
                    plan = ResumePlan.Skip("Another download is already writing this file");
                }

                switch (plan.Kind)
                {
                    case ResumeActionKind.Skip:
                        return Result(task, plan.Kind, false, plan.Reason);

                    case ResumeActionKind.Fail:
                        await _persistence.MarkError(task.InternalId, plan.Reason);
                        return Result(task, plan.Kind, false, plan.Reason);

                    case ResumeActionKind.ResumeDownload:
                    {
                        var fs = GetFileService();
                        fs.QueueResumedDownload(task, atFirst: trigger == ResumeTrigger.Manual);
                        await _persistence.UpdateProgress(task.InternalId, task.TransmittedBytes, task.Progress, StateTask.Pending);
                        string msg = plan.ResumeOffset > 0
                            ? $"Queued, continuing from {HelperService.SizeSuffix(plan.ResumeOffset)}"
                            : "Queued from the start";
                        return Result(task, plan.Kind, true, msg);
                    }

                    case ResumeActionKind.ResumeSplitDownload:
                    {
                        var fs = GetFileService();
                        await _persistence.UpdateProgress(task.InternalId, plan.CompletedBytes + plan.TransmittedInPart, task.Progress, StateTask.Pending);
                        // Registers itself as a live job before its first await.
                        fs.RunSplitDownloadAsync(task, plan.StartPart, plan.CompletedBytes, plan.TransmittedInPart, plan.RestartFromScratch)
                            .Observe(_logger, $"resumed split download {task.Name}");
                        string msg = plan.Reason ?? $"Queued from part {plan.StartPart + 1} of {task.MessageIds.Count}";
                        return Result(task, plan.Kind, true, msg);
                    }

                    case ResumeActionKind.ResumeBatchUpload:
                        await ResumeBatchAsync(task, plan);
                        return Result(task, plan.Kind, true, plan.Reason ?? "Queued; files already in the destination are skipped");
                }
                return Result(task, plan.Kind, false, plan.Reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not resume task {InternalId} ({Name})", task.InternalId, task.Name);
                await _persistence.MarkError(task.InternalId, ex.Message);
                return Result(task, ResumeActionKind.Fail, false, ex.Message);
            }
            finally
            {
                _gate.Release();
            }
        }

        private ResumeResult RetryInMemory(PersistedTaskModel task)
        {
            var download = _tis.FindDownload(task.InternalId);
            if (download != null && (download.state == StateTask.Error || download.state == StateTask.Paused))
            {
                _tis.addToPendingDownloadList(download, atFirst: true);
                return Result(task, ResumeActionKind.ResumeDownload, true, "Queued again");
            }
            var batch = _tis.FindTask(task.InternalId);
            if (batch != null && batch.state == StateTask.Error)
            {
                batch.Retry();
                return Result(task, ResumeActionKind.ResumeBatchUpload, true, "Queued again; files already uploaded are skipped");
            }
            return null;
        }

        private async Task ResumeBatchAsync(PersistedTaskModel task, ResumePlan plan)
        {
            var idt = new InfoDownloadTaksModel
            {
                _internalId = task.InternalId,
                creationDate = task.CreationDate,
                channelId = task.ChannelId
            };
            idt.RestoreUploadInProgress(await ValidateUploadedPartsAsync(task));
            if (plan.MissingItems.Count > 0)
                _logger.LogWarning("Batch upload {InternalId}: {Reason}", task.InternalId, plan.Reason);

            var fs = GetFileService();
            // Same entry point as a new upload: totals are recomputed, the record
            // is updated in place (same id) and the queue runs it in order. Files
            // already in the destination are skipped by the destination check.
            await fs.AddUploadFileFromServer(task.ChannelId, task.ToPath, TaskPersistenceMapper.ToSourceContents(plan.Items), idt);
        }

        /// <summary>
        /// Parts recorded for the file in progress are only reused while they
        /// still exist in the channel and the local file is unchanged.
        /// </summary>
        private async Task<PersistedUploadProgress> ValidateUploadedPartsAsync(PersistedTaskModel task)
        {
            var progress = task.CurrentFile;
            if (progress == null || progress.Parts == null || progress.Parts.Count == 0)
                return null;
            try
            {
                var info = new FileInfo(progress.FilePath);
                if (!info.Exists || !progress.Matches(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks, progress.Mode, progress.ChunkSize))
                {
                    _logger.LogWarning("Batch upload {InternalId}: {File} changed since its parts were sent; it is uploaded again (orphan messages: {Ids})",
                        task.InternalId, progress.FilePath, string.Join(",", progress.Parts.Select(p => p.MessageId)));
                    return null;
                }
                foreach (var part in progress.Parts)
                {
                    if (await _telegram.getMessageFile(task.ChannelId, part.MessageId) == null)
                    {
                        _logger.LogWarning("Batch upload {InternalId}: part {Index} of {File} no longer exists in the channel; the file is uploaded again",
                            task.InternalId, part.Index, progress.FilePath);
                        return null;
                    }
                }
                _logger.LogInformation("Batch upload {InternalId}: reusing {Count} part(s) already sent for {File}",
                    task.InternalId, progress.Parts.Count, progress.FilePath);
                return progress;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch upload {InternalId}: could not check the parts already sent; the file is uploaded again", task.InternalId);
                return null;
            }
        }

        private static ResumeResult Result(PersistedTaskModel task, ResumeActionKind action, bool resumed, string message)
        {
            return new ResumeResult
            {
                InternalId = task.InternalId,
                Name = task.Name,
                Action = action,
                Resumed = resumed,
                Message = message
            };
        }
    }
}
