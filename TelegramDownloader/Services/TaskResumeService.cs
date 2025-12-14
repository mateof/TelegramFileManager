using TelegramDownloader.Data;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Persistence;

namespace TelegramDownloader.Services
{
    /// <summary>
    /// Background service that resumes pending tasks on application startup
    /// </summary>
    public class TaskResumeService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TaskResumeService> _logger;

        public TaskResumeService(IServiceProvider serviceProvider, ILogger<TaskResumeService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        private CancellationToken _cancellationToken;
        private bool _hasResumedTasks = false;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;

            _logger.LogInformation("========== TaskResumeService STARTING ==========");
            _logger.LogInformation("AutoResumeOnStartup config value: {Value}", GeneralConfigStatic.config?.AutoResumeOnStartup);
            _logger.LogInformation("EnableTaskPersistence config value: {Value}", GeneralConfigStatic.config?.EnableTaskPersistence);

            // Check if auto-resume is enabled
            if (!(GeneralConfigStatic.config?.AutoResumeOnStartup ?? true))
            {
                _logger.LogWarning("Auto-resume on startup is DISABLED in config - skipping task resume");
                return;
            }

            _logger.LogInformation("Auto-resume is ENABLED - subscribing to OnUserLoggedIn event");

            // Subscribe to the login event
            TelegramService.OnUserLoggedIn += OnUserLoggedIn;

            // Also try to resume immediately in case user is already logged in
            _ = Task.Run(async () =>
            {
                try
                {
                    // Wait a bit for services to initialize
                    await Task.Delay(3000, cancellationToken);
                    await TryResumeTasksAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during initial task resume attempt: {Message}", ex.Message);
                }
            }, cancellationToken);
        }

        private async void OnUserLoggedIn(object sender, EventArgs e)
        {
            _logger.LogInformation("========== OnUserLoggedIn event received ==========");

            if (_hasResumedTasks)
            {
                _logger.LogInformation("Tasks already resumed - skipping");
                return;
            }

            try
            {
                await TryResumeTasksAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resuming tasks after login: {Message}", ex.Message);
            }
        }

        private async Task TryResumeTasksAsync()
        {
            if (_hasResumedTasks)
            {
                _logger.LogInformation("Tasks already resumed - skipping");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var telegramService = scope.ServiceProvider.GetRequiredService<ITelegramService>();

            if (!telegramService.checkUserLogin())
            {
                _logger.LogInformation("Telegram session not ready yet - waiting for user login");
                return;
            }

            _logger.LogInformation("Telegram session is ready - proceeding to resume tasks");
            _hasResumedTasks = true;

            await ResumeTasksAsync(_cancellationToken);
        }

        private async Task ResumeTasksAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("========== ResumeTasksAsync - Starting ==========");

            using var scope = _serviceProvider.CreateScope();
            var persistence = scope.ServiceProvider.GetRequiredService<ITaskPersistenceService>();
            var fileService = scope.ServiceProvider.GetRequiredService<IFileService>();
            var transactionInfo = scope.ServiceProvider.GetRequiredService<TransactionInfoService>();

            _logger.LogInformation("ResumeTasksAsync - Services resolved. Persistence enabled: {Enabled}, FileService type: {Type}",
                persistence.IsEnabled, fileService.GetType().Name);

            _logger.LogInformation("========== Loading pending tasks from MongoDB ==========");

            // Cleanup stale tasks first
            await persistence.CleanupStaleTasks();

            // Load pending tasks
            _logger.LogInformation("Calling LoadPendingTasks...");
            var pendingTasks = await persistence.LoadPendingTasks();
            _logger.LogInformation("LoadPendingTasks returned {Count} tasks", pendingTasks?.Count ?? 0);

            if (pendingTasks == null || pendingTasks.Count == 0)
            {
                _logger.LogInformation("========== No pending tasks found in MongoDB - nothing to resume ==========");
                return;
            }

            _logger.LogInformation("========== Found {Count} pending tasks to resume ==========", pendingTasks.Count);
            foreach (var t in pendingTasks)
            {
                _logger.LogInformation("  - Task: {Name}, Type: {Type}, State: {State}, Channel: {Channel}, Progress: {Progress}%",
                    t.Name, t.Type, t.State, t.ChannelId, t.Progress);
            }

            // Group tasks by type for better processing
            var downloads = pendingTasks.Where(t => t.Type == TaskType.Download).ToList();
            var uploads = pendingTasks.Where(t => t.Type == TaskType.Upload).ToList();
            var batchTasks = pendingTasks.Where(t => t.Type == TaskType.BatchDownload || t.Type == TaskType.BatchUpload).ToList();

            _logger.LogInformation("Task breakdown - Downloads: {Downloads}, Uploads: {Uploads}, Batch: {Batch}",
                downloads.Count, uploads.Count, batchTasks.Count);

            // Resume downloads
            foreach (var task in downloads)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                try
                {
                    await ResumeDownload(task, fileService, persistence, transactionInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to resume download task {InternalId} - {Name}",
                        task.InternalId, task.Name);
                    await persistence.MarkError(task.InternalId, ex.Message);
                }
            }

            // Resume uploads
            foreach (var task in uploads)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                try
                {
                    await ResumeUpload(task, fileService, persistence, transactionInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to resume upload task {InternalId} - {Name}",
                        task.InternalId, task.Name);
                    await persistence.MarkError(task.InternalId, ex.Message);
                }
            }

            // Resume batch tasks
            foreach (var task in batchTasks)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                try
                {
                    await ResumeBatchTask(task, fileService, persistence, transactionInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to resume batch task {InternalId}",
                        task.InternalId);
                    await persistence.MarkError(task.InternalId, ex.Message);
                }
            }

            _logger.LogInformation("Task resume completed");
        }

        private async Task ResumeDownload(
            PersistedTaskModel task,
            IFileService fileService,
            ITaskPersistenceService persistence,
            TransactionInfoService transactionInfo)
        {
            _logger.LogInformation("Resuming download: {Name} at {Progress}% ({Transmitted}/{Total} bytes)",
                task.Name, task.Progress, task.TransmittedBytes, task.TotalSize);

            // Validate the destination path
            var destDir = Path.GetDirectoryName(task.DestinationPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Check if partial file exists and validate its size
            long resumeOffset = 0;
            if (File.Exists(task.DestinationPath))
            {
                var fileInfo = new FileInfo(task.DestinationPath);
                if (fileInfo.Length <= task.TransmittedBytes && fileInfo.Length > 0)
                {
                    // File exists and is smaller or equal to what we transmitted - resume from file size
                    resumeOffset = fileInfo.Length;
                    _logger.LogInformation("Found partial file ({FileSize} bytes), will resume from offset {Offset}",
                        fileInfo.Length, resumeOffset);
                }
                else if (fileInfo.Length > task.TransmittedBytes)
                {
                    // File is larger than what we recorded - something is wrong, restart
                    _logger.LogWarning("Partial file is larger than recorded ({FileSize} > {Recorded}), restarting download",
                        fileInfo.Length, task.TransmittedBytes);
                    File.Delete(task.DestinationPath);
                    resumeOffset = 0;
                }
            }

            // Create the download model from persisted task
            var downloadModel = TaskPersistenceMapper.ToDownloadModel(task);
            downloadModel.tis = transactionInfo;
            downloadModel._transmitted = resumeOffset;
            downloadModel._transmittedString = HelperService.SizeSuffix(resumeOffset);
            downloadModel.progress = task.TotalSize > 0 ? (int)(resumeOffset * 100 / task.TotalSize) : 0;

            // Set up persistence callback
            downloadModel.OnProgressPersist = async (transmitted, progress, state) =>
            {
                await persistence.UpdateProgress(task.InternalId, transmitted, progress, state);
            };

            // Handle completion
            downloadModel.EventStatechanged += async (sender, args) =>
            {
                if (downloadModel.state == StateTask.Completed)
                {
                    await persistence.MarkCompleted(task.InternalId);
                }
            };

            // Queue the download using the existing FileService
            // Note: FileServiceV2.downloadFromTelegramV2 will need modification to accept resumeOffset
            if (fileService is FileServiceV2 fsv2)
            {
                _logger.LogInformation("FileService is FileServiceV2 - setting up download callback");

                // Add to pending list with the existing internal ID
                downloadModel.callbacks = new Callbacks();
                downloadModel.callbacks.callback = async () =>
                {
                    _logger.LogInformation("Download callback executing for: {Name}", task.Name);
                    await fsv2.DownloadFileNowV2WithOffset(
                        task.ChannelId,
                        task.MessageId ?? (task.MessageIds?.FirstOrDefault() ?? 0),
                        task.DestinationPath,
                        downloadModel,
                        resumeOffset);
                };

                _logger.LogInformation("Adding download to pending list: {Name}, InternalId: {Id}",
                    downloadModel.name, downloadModel._internalId);

                transactionInfo.addToPendingDownloadList(downloadModel, atFirst: false, chekDownloads: true);

                _logger.LogInformation("Download added to pending list successfully: {Name}", task.Name);
            }
            else
            {
                _logger.LogWarning("FileService is NOT FileServiceV2 (actual type: {Type}) - cannot resume with offset",
                    fileService.GetType().Name);
                // Fallback: restart download from beginning
                // This would require implementing resume in FileService as well
            }
        }

        private async Task ResumeUpload(
            PersistedTaskModel task,
            IFileService fileService,
            ITaskPersistenceService persistence,
            TransactionInfoService transactionInfo)
        {
            _logger.LogInformation("Resuming upload: {Name} (uploads restart from beginning due to Telegram API limitation)",
                task.Name);

            // Check if source file still exists
            if (!File.Exists(task.SourcePath))
            {
                _logger.LogWarning("Source file not found: {Path} - marking task as error", task.SourcePath);
                await persistence.MarkError(task.InternalId, $"Source file not found: {task.SourcePath}");
                return;
            }

            // Uploads cannot resume mid-stream with Telegram API
            // They must restart from the beginning
            var uploadModel = TaskPersistenceMapper.ToUploadModel(task);
            uploadModel.tis = transactionInfo;
            uploadModel._transmitted = 0; // Reset to 0
            uploadModel.progress = 0;

            // Set up persistence callback
            uploadModel.OnProgressPersist = async (transmitted, progress, state) =>
            {
                await persistence.UpdateProgress(task.InternalId, transmitted, progress, state);
            };

            // For uploads, we need to re-queue them
            // This is a simplified version - full implementation would integrate with FileService
            _logger.LogInformation("Upload task {Name} will be restarted (Telegram doesn't support upload resume)",
                task.Name);

            // Mark as pending to be picked up by user action or re-queue
            await persistence.UpdateProgress(task.InternalId, 0, 0, StateTask.Pending);
        }

        private async Task ResumeBatchTask(
            PersistedTaskModel task,
            IFileService fileService,
            ITaskPersistenceService persistence,
            TransactionInfoService transactionInfo)
        {
            _logger.LogInformation("Resuming batch task: {From} -> {To} ({Executed}/{Total} completed)",
                task.FromPath, task.ToPath, task.Executed, task.Total);

            // Batch tasks are more complex - they contain child tasks
            // For now, mark them for manual resume
            _logger.LogWarning("Batch task resume requires manual intervention - task will be marked as pending");
            await persistence.UpdateProgress(task.InternalId, task.ExecutedBatchSize, task.Progress, StateTask.Pending);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("TaskResumeService stopping - unsubscribing from events");
            TelegramService.OnUserLoggedIn -= OnUserLoggedIn;
            return Task.CompletedTask;
        }
    }
}
