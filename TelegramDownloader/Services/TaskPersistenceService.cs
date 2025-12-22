#nullable disable
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Persistence;

namespace TelegramDownloader.Services
{
    public interface ITaskPersistenceService
    {
        Task PersistDownload(DownloadModel dm, string channelId, int messageId, BsonFileManagerModel fileInfo = null);
        Task PersistUpload(UploadModel um, string channelId, string sourcePath, string dbFilePath = null, string parentId = null);
        Task PersistBatchTask(InfoDownloadTaksModel idt);
        Task UpdateProgress(string internalId, long transmitted, int progress, StateTask state);
        Task UpdateSplitProgress(string internalId, int currentPartIndex, List<int> completedParts);
        Task MarkCompleted(string internalId);
        Task MarkError(string internalId, string error);
        Task<List<PersistedTaskModel>> LoadPendingTasks();
        Task CleanupStaleTasks();
        bool IsEnabled { get; }
    }

    public class TaskPersistenceService : ITaskPersistenceService
    {
        private readonly IDbService _db;
        private readonly ILogger<TaskPersistenceService> _logger;
        private readonly SemaphoreSlim _persistLock = new SemaphoreSlim(1, 1);

        // Debounce progress updates to avoid excessive DB writes
        private readonly Dictionary<string, DateTime> _lastProgressUpdate = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, (long transmitted, int progress, StateTask state)> _pendingUpdates = new Dictionary<string, (long, int, StateTask)>();
        private readonly TimeSpan _progressDebounceInterval;

        public bool IsEnabled => GeneralConfigStatic.config?.EnableTaskPersistence ?? true;

        public TaskPersistenceService(IDbService db, ILogger<TaskPersistenceService> logger)
        {
            _db = db;
            _logger = logger;
            _progressDebounceInterval = TimeSpan.FromSeconds(
                GeneralConfigStatic.config?.TaskPersistenceDebounceSeconds ?? 5);

            _logger.LogInformation("TaskPersistenceService initialized with debounce interval of {Seconds}s",
                _progressDebounceInterval.TotalSeconds);
        }

        /// <summary>
        /// Persist a download task
        /// </summary>
        public async Task PersistDownload(DownloadModel dm, string channelId, int messageId, BsonFileManagerModel fileInfo = null)
        {
            if (!IsEnabled) return;

            try
            {
                var task = TaskPersistenceMapper.ToPersistedTask(dm, channelId, messageId, fileInfo);
                await _db.SaveTask(task);
                _logger.LogDebug("Persisted download task: {Name}", dm.name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist download task: {Name}", dm.name);
            }
        }

        /// <summary>
        /// Persist an upload task
        /// </summary>
        public async Task PersistUpload(UploadModel um, string channelId, string sourcePath, string dbFilePath = null, string parentId = null)
        {
            if (!IsEnabled) return;

            try
            {
                var task = TaskPersistenceMapper.ToPersistedTask(um, channelId, sourcePath, dbFilePath, parentId);
                await _db.SaveTask(task);
                _logger.LogDebug("Persisted upload task: {Name}", um.name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist upload task: {Name}", um.name);
            }
        }

        /// <summary>
        /// Persist a batch task
        /// </summary>
        public async Task PersistBatchTask(InfoDownloadTaksModel idt)
        {
            if (!IsEnabled) return;

            try
            {
                var task = TaskPersistenceMapper.ToPersistedTask(idt);
                await _db.SaveTask(task);
                _logger.LogDebug("Persisted batch task: {From} -> {To}", idt.fromPath, idt.toPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist batch task");
            }
        }

        /// <summary>
        /// Update task progress with debouncing
        /// </summary>
        public async Task UpdateProgress(string internalId, long transmitted, int progress, StateTask state)
        {
            if (!IsEnabled) return;

            // Always persist immediately on state change (not Working) or completion
            bool forceUpdate = state != StateTask.Working || progress >= 100;

            await _persistLock.WaitAsync();
            try
            {
                // Check debounce
                if (!forceUpdate && _lastProgressUpdate.TryGetValue(internalId, out var lastUpdate))
                {
                    if (DateTime.Now - lastUpdate < _progressDebounceInterval)
                    {
                        // Store pending update for later
                        _pendingUpdates[internalId] = (transmitted, progress, state);
                        return;
                    }
                }

                // Perform update
                await _db.UpdateTaskProgress(internalId, transmitted, progress, state);
                _lastProgressUpdate[internalId] = DateTime.Now;
                _pendingUpdates.Remove(internalId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update progress for task {InternalId}", internalId);
            }
            finally
            {
                _persistLock.Release();
            }
        }

        /// <summary>
        /// Update split file progress
        /// </summary>
        public async Task UpdateSplitProgress(string internalId, int currentPartIndex, List<int> completedParts)
        {
            if (!IsEnabled) return;

            try
            {
                var task = await _db.GetTaskByInternalId(internalId);
                if (task != null)
                {
                    task.CurrentPartIndex = currentPartIndex;
                    task.CompletedPartIndices = completedParts;
                    await _db.UpdateTask(task);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update split progress for task {InternalId}", internalId);
            }
        }

        /// <summary>
        /// Mark task as completed and remove from persistence
        /// </summary>
        public async Task MarkCompleted(string internalId)
        {
            if (!IsEnabled) return;

            try
            {
                await _db.DeleteTask(internalId);

                await _persistLock.WaitAsync();
                try
                {
                    _lastProgressUpdate.Remove(internalId);
                    _pendingUpdates.Remove(internalId);
                }
                finally
                {
                    _persistLock.Release();
                }

                _logger.LogInformation("Task {InternalId} completed and removed from persistence", internalId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark task {InternalId} as completed", internalId);
            }
        }

        /// <summary>
        /// Mark task as error
        /// </summary>
        public async Task MarkError(string internalId, string error)
        {
            if (!IsEnabled) return;

            try
            {
                await _db.MarkTaskAsError(internalId, error);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark task {InternalId} as error", internalId);
            }
        }

        /// <summary>
        /// Load all pending tasks from persistence
        /// </summary>
        public async Task<List<PersistedTaskModel>> LoadPendingTasks()
        {
            try
            {
                return await _db.GetAllPendingTasks();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load pending tasks");
                return new List<PersistedTaskModel>();
            }
        }

        /// <summary>
        /// Cleanup stale tasks
        /// </summary>
        public async Task CleanupStaleTasks()
        {
            try
            {
                var maxAgeDays = GeneralConfigStatic.config?.StaleTaskCleanupDays ?? 7;
                await _db.CleanupStaleTasks(maxAgeDays);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup stale tasks");
            }
        }

        /// <summary>
        /// Flush any pending updates (called before shutdown)
        /// </summary>
        public async Task FlushPendingUpdates()
        {
            await _persistLock.WaitAsync();
            try
            {
                foreach (var kvp in _pendingUpdates)
                {
                    try
                    {
                        await _db.UpdateTaskProgress(kvp.Key, kvp.Value.transmitted, kvp.Value.progress, kvp.Value.state);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to flush pending update for task {InternalId}", kvp.Key);
                    }
                }
                _pendingUpdates.Clear();
            }
            finally
            {
                _persistLock.Release();
            }
        }
    }
}
