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
        /// <summary>
        /// Persists a batch task (insert or update by internal id) and attaches
        /// this service to it, so the task saves its own progress from then on.
        /// </summary>
        Task PersistBatchTask(InfoDownloadTaksModel idt);
        /// <summary>Inserts or replaces a task record, keyed by its internal id.</summary>
        Task PersistTask(PersistedTaskModel task);
        Task UpdateProgress(string internalId, long transmitted, int progress, StateTask state);
        /// <summary>
        /// Saves the counters, state and in-progress parts of a batch task.
        /// Debounced like <see cref="UpdateProgress"/> unless <paramref name="force"/>
        /// is set or the task is not running.
        /// </summary>
        Task SaveBatchProgress(InfoDownloadTaksModel idt, bool force = false);
        Task UpdateSplitProgress(string internalId, int currentPartIndex, List<int> completedParts, long completedBytes);
        /// <summary>Deletes the record of a finished task.</summary>
        Task MarkCompleted(string internalId);
        /// <summary>Deletes the record of a task the user canceled.</summary>
        Task Remove(string internalId);
        Task MarkError(string internalId, string error);
        Task<PersistedTaskModel> GetTask(string internalId);
        Task<List<PersistedTaskModel>> LoadPendingTasks();
        /// <summary>
        /// Records left in "Working" by a previous process (crash, restart) are
        /// set back to "Pending", except the ones running in this process.
        /// </summary>
        Task<int> ResetInterruptedTasks(Func<string, bool> isLive);
        Task FlushPendingUpdates();
        Task CleanupStaleTasks();
        bool IsEnabled { get; }
    }

    /// <summary>
    /// Stores transfer tasks in MongoDB so they survive a restart. All writes go
    /// through one async lock, which makes "read, modify, replace" updates
    /// atomic with respect to the delete of a finished task: a late progress
    /// write can never resurrect a record that was already completed.
    /// </summary>
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
                Math.Max(1, GeneralConfigStatic.config?.TaskPersistenceDebounceSeconds ?? 5));

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
                await UpsertAsync(task);
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
                await UpsertAsync(task);
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
                await UpsertAsync(task);
                idt.Persistence = this;
                _logger.LogDebug("Persisted batch task: {From} -> {To}", idt.fromPath, idt.toPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist batch task");
            }
        }

        public async Task PersistTask(PersistedTaskModel task)
        {
            if (!IsEnabled) return;

            try
            {
                await UpsertAsync(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist task {InternalId}", task.InternalId);
            }
        }

        private async Task UpsertAsync(PersistedTaskModel task)
        {
            await _persistLock.WaitAsync();
            try
            {
                // Insert-or-replace keyed by InternalId: persisting the same task
                // twice (a resume, a retry) must not create a duplicate record.
                var existing = await _db.GetTaskByInternalId(task.InternalId);
                if (existing != null)
                {
                    task.Id = existing.Id;
                    task.RetryCount = existing.RetryCount;
                    await _db.UpdateTask(task);
                }
                else
                {
                    await _db.SaveTask(task);
                }
                _lastProgressUpdate.Remove(task.InternalId);
                _pendingUpdates.Remove(task.InternalId);
            }
            finally
            {
                _persistLock.Release();
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
                if (!forceUpdate && IsDebounced(internalId))
                {
                    // Store pending update for later
                    _pendingUpdates[internalId] = (transmitted, progress, state);
                    return;
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

        // Caller holds _persistLock.
        private bool IsDebounced(string internalId)
        {
            return _lastProgressUpdate.TryGetValue(internalId, out var lastUpdate)
                && DateTime.Now - lastUpdate < _progressDebounceInterval;
        }

        public async Task SaveBatchProgress(InfoDownloadTaksModel idt, bool force = false)
        {
            if (!IsEnabled || idt == null) return;

            bool forceUpdate = force || idt.state != StateTask.Working;
            await _persistLock.WaitAsync();
            try
            {
                if (!forceUpdate && IsDebounced(idt._internalId))
                    return;

                var task = await _db.GetTaskByInternalId(idt._internalId);
                if (task == null)
                    return; // completed or deleted meanwhile: never recreate it

                task.State = idt.state;
                task.Total = idt.total;
                task.Executed = idt.executed;
                task.TotalBatchSize = idt.totalSize;
                task.ExecutedBatchSize = idt.executedSize;
                task.TotalSize = idt.totalSize;
                task.TransmittedBytes = idt.executedSize;
                task.Progress = idt.progress;
                task.CurrentFile = idt.GetUploadInProgress();
                if (idt.LastError != null)
                    task.LastError = idt.LastError;
                await _db.UpdateTask(task);
                _lastProgressUpdate[idt._internalId] = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save progress of batch task {InternalId}", idt._internalId);
            }
            finally
            {
                _persistLock.Release();
            }
        }

        /// <summary>
        /// Update split file progress
        /// </summary>
        public async Task UpdateSplitProgress(string internalId, int currentPartIndex, List<int> completedParts, long completedBytes)
        {
            if (!IsEnabled) return;

            await _persistLock.WaitAsync();
            try
            {
                var task = await _db.GetTaskByInternalId(internalId);
                if (task != null)
                {
                    task.CurrentPartIndex = currentPartIndex;
                    task.CompletedPartIndices = completedParts.ToList();
                    task.CompletedBytes = completedBytes;
                    task.TransmittedBytes = completedBytes;
                    task.State = StateTask.Working;
                    await _db.UpdateTask(task);
                    _lastProgressUpdate[internalId] = DateTime.Now;
                    _pendingUpdates.Remove(internalId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update split progress for task {InternalId}", internalId);
            }
            finally
            {
                _persistLock.Release();
            }
        }

        /// <summary>
        /// Mark task as completed and remove from persistence
        /// </summary>
        public async Task MarkCompleted(string internalId)
        {
            if (await DeleteAsync(internalId))
                _logger.LogInformation("Task {InternalId} completed and removed from persistence", internalId);
        }

        public async Task Remove(string internalId)
        {
            if (await DeleteAsync(internalId))
                _logger.LogInformation("Task {InternalId} canceled and removed from persistence", internalId);
        }

        private async Task<bool> DeleteAsync(string internalId)
        {
            if (!IsEnabled) return false;

            await _persistLock.WaitAsync();
            try
            {
                await _db.DeleteTask(internalId);
                _lastProgressUpdate.Remove(internalId);
                _pendingUpdates.Remove(internalId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove task {InternalId}", internalId);
                return false;
            }
            finally
            {
                _persistLock.Release();
            }
        }

        /// <summary>
        /// Mark task as error
        /// </summary>
        public async Task MarkError(string internalId, string error)
        {
            if (!IsEnabled) return;

            await _persistLock.WaitAsync();
            try
            {
                await _db.MarkTaskAsError(internalId, error);
                _pendingUpdates.Remove(internalId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark task {InternalId} as error", internalId);
            }
            finally
            {
                _persistLock.Release();
            }
        }

        public async Task<PersistedTaskModel> GetTask(string internalId)
        {
            try
            {
                return await _db.GetTaskByInternalId(internalId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load task {InternalId}", internalId);
                return null;
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

        public async Task<int> ResetInterruptedTasks(Func<string, bool> isLive)
        {
            if (!IsEnabled) return 0;

            int reset = 0;
            var tasks = await LoadPendingTasks();
            foreach (var task in tasks.Where(t => t.State == StateTask.Working && !(isLive?.Invoke(t.InternalId) ?? false)))
            {
                await _persistLock.WaitAsync();
                try
                {
                    await _db.UpdateTaskState(task.InternalId, StateTask.Pending);
                    reset++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to reset interrupted task {InternalId}", task.InternalId);
                }
                finally
                {
                    _persistLock.Release();
                }
            }
            if (reset > 0)
                _logger.LogInformation("{Count} task(s) interrupted by the previous shutdown set back to Pending", reset);
            return reset;
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
        /// Flush any pending updates (called on shutdown)
        /// </summary>
        public async Task FlushPendingUpdates()
        {
            if (!IsEnabled) return;

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
