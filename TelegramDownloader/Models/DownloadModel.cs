#nullable disable
using System.Diagnostics.Tracing;
using System.Xml.Linq;
using System;
using TelegramDownloader.Data;
using TelegramDownloader.Services;
using TL;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using MongoDB.Bson.Serialization.Attributes;
using TelegramDownloader.Models.Persistence;
using TelegramDownloader.Services.Transfers;
using TelegramDownloader.Models.FileSystem;

namespace TelegramDownloader.Models
{

    /// <summary>
    /// A batch transfer (today: uploading a selection of server files/folders
    /// to a channel). Runs one at a time through
    /// <see cref="TransactionInfoService.CheckPendingUploadInfoTasks"/>.
    /// </summary>
    public class InfoDownloadTaksModel
    {
        // Guards the counters, the child lists and the in-progress upload. Never
        // held across an await or while raising events.
        private readonly object _sync = new object();
        private int _running;
        public event EventHandler<InfoTaskEventArgs> EventChanged;
        public StateTask state { get; set; } = StateTask.Pending;
        public DateTime creationDate { get; set; } = DateTime.Now;
        public DateTime endnDate { get; set; } = DateTime.Now;
        public string _internalId { get; set; } = Guid.NewGuid().ToString();
        public string id {  get; set; }
        public string channelId { get; set; }
        public bool isUpload {  get; set; }
        public List<FsEntry> files { get; set; }
        public Callbacks callbacks { get; set; }
        public string fromPath { get; set; }
        public string toPath { get; set; }
        public int total { get; set; }
        public int executed { get; set; }
        public int currentUpload { get; set; } = 0;
        public long totalSize { get; set; }
        public long executedSize { get; set; }
        public Thread thread { get; set; }
        public FsEntry? file {  get; set; }
        public int progress { get; set; }
        public  TransactionInfoService tis { get; set; }

        /// <summary>Message of the failure that stopped the task, if any.</summary>
        public string LastError { get; set; }

        /// <summary>
        /// Set by <see cref="ITaskPersistenceService.PersistBatchTask"/> when the
        /// task is persisted; null when persistence is disabled.
        /// </summary>
        public ITaskPersistenceService Persistence { get; set; }

        /// <summary>True while the upload callback started by <see cref="RetryCallback"/> runs.</summary>
        public bool IsRunning => Volatile.Read(ref _running) == 1;

        // Parts already sent for the file being uploaded (see PersistedUploadProgress).
        private PersistedUploadProgress _uploadInProgress;

        private readonly List<UploadModel> _currentUploads = new List<UploadModel>();
        private readonly List<DownloadModel> _currentDownloads = new List<DownloadModel>();

        /// <summary>Snapshot of the uploads that belong to this task.</summary>
        public List<UploadModel> currentUploads { get { lock (_sync) return _currentUploads.ToList(); } }
        /// <summary>Snapshot of the downloads that belong to this task.</summary>
        public List<DownloadModel> currentDownloads { get { lock (_sync) return _currentDownloads.ToList(); } }

        /// <summary>Short label for lists: the selected item, or how many there are.</summary>
        public string DisplayName
        {
            get
            {
                var selected = files;
                if (selected == null || selected.Count == 0)
                    return toPath;
                return selected.Count == 1 ? selected[0].Name : $"{selected[0].Name} (+{selected.Count - 1})";
            }
        }

        private ILogger logger => tis?.Logger;

        public void AddOne(long size)
        {
            if (state == StateTask.Canceled)
                cancelTask();
            bool hadParts;
            lock (_sync)
            {
                executed += 1;
                progress = total > 0 ? (int)Math.Min(100, executed * 100L / total) : 100;
                executedSize += size;
                // The file is done (sent and indexed, or skipped): its parts are
                // no longer needed for a resume.
                hadParts = _uploadInProgress != null;
                _uploadInProgress = null;
            }
            EventChanged?.Invoke(this, new InfoTaskEventArgs());
            Persistence?.SaveBatchProgress(this, force: hadParts).Observe(logger, $"save progress of task {_internalId}");
        }

        public void markAsCompleted()
        {
            state = StateTask.Completed;
            endnDate = DateTime.Now;
            Persistence?.MarkCompleted(_internalId).Observe(logger, $"complete task {_internalId}");
            tis.CheckPendingUploadInfoTasks();
            EventChanged?.Invoke(this, new InfoTaskEventArgs());
        }

        public void change()
        {
            if (state == StateTask.Canceled)
                throw new Exception($"Canceled {id}");
            EventChanged?.Invoke(this, new InfoTaskEventArgs());
        }

        public void addUpload(UploadModel um)
        {
            if (state == StateTask.Canceled)
                return;
            lock (_sync)
            {
                deleteNotWorkingTasks();
                _currentUploads.Add(um);
            }
        }

        public void addDownloads(DownloadModel dm)
        {
            if (state == StateTask.Canceled)
                return;
            lock (_sync)
            {
                deleteNotWorkingTasks();
                _currentDownloads.Add(dm);
            }
        }

        /// <summary>Queues the task again (in-memory retry after a failure).</summary>
        public void Retry()
        {
            if (IsRunning)
                return;
            state = StateTask.Pending;
            currentUpload = 0;
            LastError = null;
            Persistence?.SaveBatchProgress(this, force: true).Observe(logger, $"save state of task {_internalId}");
            tis.CheckPendingUploadInfoTasks();
        }

        /// <summary>
        /// Runs the task callback. The returned task is observed: a failure marks
        /// the task as failed (and persists the reason) and lets the next queued
        /// task start, instead of leaving this one "Working" forever.
        /// </summary>
        public void RetryCallback()
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                logger?.LogWarning("Task {Id} is already running - start request ignored", _internalId);
                return;
            }
            state = StateTask.Working;
            currentUpload = 0;
            Persistence?.SaveBatchProgress(this, force: true).Observe(logger, $"save state of task {_internalId}");
            Task run;
            try
            {
                run = callbacks?.callback?.Invoke() ?? Task.CompletedTask;
            }
            catch (Exception ex)
            {
                run = Task.FromException(ex);
            }
            run.ContinueWith(OnRunEnded, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        private void OnRunEnded(Task run)
        {
            try
            {
                if (run.IsFaulted || run.IsCanceled)
                {
                    Exception ex = run.Exception?.GetBaseException();
                    if (state != StateTask.Canceled)
                    {
                        state = StateTask.Error;
                        endnDate = DateTime.Now;
                        LastError = ex?.Message ?? "The task was interrupted";
                        logger?.LogError(ex, "Task {Id} failed: {Message}", _internalId, LastError);
                        Persistence?.MarkError(_internalId, LastError).Observe(logger, $"mark task {_internalId} as failed");
                    }
                }
                else if (state == StateTask.Working)
                {
                    // The upload returned without completing: one of its files was
                    // canceled, which stops the whole batch.
                    state = StateTask.Canceled;
                    endnDate = DateTime.Now;
                }
                if (state == StateTask.Canceled)
                    Persistence?.Remove(_internalId).Observe(logger, $"remove canceled task {_internalId}");
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
            EventChanged?.Invoke(this, new InfoTaskEventArgs());
            tis?.CheckPendingUploadInfoTasks();
        }

        public void cancelTask()
        {
            state = StateTask.Canceled;
            foreach (DownloadModel dm in currentDownloads.Where(x => x.state == StateTask.Working || x.UserPaused))
            {
                dm.Cancel();
            }
            foreach (UploadModel um in currentUploads.Where(x => x.state == StateTask.Working || x.state == StateTask.Paused))
            {
                um.Cancel();
            }
            Persistence?.Remove(_internalId).Observe(logger, $"remove canceled task {_internalId}");
            tis.CheckPendingUploadInfoTasks();
        }

        private void deleteNotWorkingTasks()
        {
            // Paused ones still belong to the task: cancelling it must reach them
            _currentUploads.RemoveAll(um => um.state != StateTask.Working && um.state != StateTask.Paused);
            _currentDownloads.RemoveAll(dm => dm.state != StateTask.Working && dm.state != StateTask.Paused && !dm.UserPaused);
        }

        /// <summary>Copy of the parts already sent for the file in progress.</summary>
        public PersistedUploadProgress GetUploadInProgress()
        {
            lock (_sync)
                return _uploadInProgress?.Clone();
        }

        /// <summary>Restores the parts sent before a restart (resume only).</summary>
        public void RestoreUploadInProgress(PersistedUploadProgress progress)
        {
            lock (_sync)
                _uploadInProgress = progress?.Clone();
        }

        /// <summary>
        /// Message id of part <paramref name="index"/> of <paramref name="file"/>
        /// when it was already sent by a previous run of this task and the file
        /// has not changed since; null when it has to be uploaded.
        /// </summary>
        public int? TryGetUploadedPart(FileInfo file, string mode, long chunkSize, int index)
        {
            lock (_sync)
            {
                if (_uploadInProgress == null || !_uploadInProgress.Matches(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks, mode, chunkSize))
                    return null;
                return _uploadInProgress.Parts.FirstOrDefault(p => p.Index == index)?.MessageId;
            }
        }

        /// <summary>
        /// Records a part that reached Telegram and persists it right away, so a
        /// restart before the file is indexed does not send it a second time.
        /// </summary>
        public Task RecordUploadedPartAsync(FileInfo file, string mode, long chunkSize, int index, int messageId)
        {
            lock (_sync)
            {
                if (_uploadInProgress == null || !_uploadInProgress.Matches(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks, mode, chunkSize))
                {
                    _uploadInProgress = new PersistedUploadProgress
                    {
                        FilePath = file.FullName,
                        FileSize = file.Length,
                        LastWriteTicksUtc = file.LastWriteTimeUtc.Ticks,
                        Mode = mode,
                        ChunkSize = chunkSize
                    };
                }
                _uploadInProgress.Parts.RemoveAll(p => p.Index == index);
                _uploadInProgress.Parts.Add(new PersistedUploadedPart { Index = index, MessageId = messageId });
            }
            return Persistence?.SaveBatchProgress(this, force: true) ?? Task.CompletedTask;
        }
    }

    /// <summary>
    /// Thrown from a progress callback to stop a transfer the user canceled or
    /// paused. Not a failure: the run ends without marking the task as failed.
    /// </summary>
    public class TransferStoppedException : Exception
    {
        public TransferStoppedException(string message) : base(message) { }
    }

    public class DownloadModel
    {
        // Guards the progress fields and state transitions. Never held while
        // raising events or calling into TransactionInfoService.
        private readonly object _sync = new object();
        private int _running;
        private volatile bool _pausedRun;
        private volatile bool _restartRequested;
        public event EventHandler<DownloadEventArgs> EventChanged;
        public event EventHandler EventStatechanged;
        /// <summary>
        /// Raised when a run started by <see cref="RetryCallback"/> has ended and
        /// its file stream is closed (completed, failed, canceled or paused).
        /// </summary>
        public event EventHandler RunFinished;
        public string _internalId { get; set; } = Guid.NewGuid().ToString();
        public string id = Guid.NewGuid().ToString();
        public string action { get; set; } = "Download";
        public StateTask state { get; set; } = StateTask.Working;
        public string idTask { get; set; }
        public ChatMessages m {  get; set; }
        public string name { get; set; }
        public string path { get; set; }
        public Callbacks callbacks { get; set; }

        public long _size { get; set; }
        public long _transmitted { get; set; } = 0;

        public string _sizeString { get; set; }
        public string _transmittedString { get; set; }

        public IPeerInfo channel { get; set; }
        public string channelName { get; set; }
        public int progress { get; set; }
        public DateTime creationDate { get; set; } = DateTime.Now;
        public DateTime startDate {  get; set; }
        public DateTime endnDate { get; set; }
        public TransactionInfoService tis { get; set; }

        /// <summary>Message of the failure that stopped the download, if any.</summary>
        [BsonIgnore]
        public string LastError { get; set; }

        /// <summary>
        /// Split downloads write every part into the same file: number of bytes
        /// of that file that belong to the previous parts.
        /// </summary>
        [BsonIgnore]
        public long PartStartOffset { get; set; }

        /// <summary>True while a run started by <see cref="RetryCallback"/> is in progress.</summary>
        [BsonIgnore]
        public bool IsRunning => Volatile.Read(ref _running) == 1;

        /// <summary>
        /// Paused on its own by the user: it waits in the queue and nothing but
        /// the user (resume, resume all) starts it again.
        /// </summary>
        [BsonIgnore]
        public bool UserPaused { get; set; }

        /// <summary>
        /// Whether it can run again after being paused, cancelled or failed. Not
        /// the downloads tied to one HTTP request.
        /// </summary>
        [BsonIgnore]
        public bool CanRestart => callbacks?.callback != null;

        // Persistence context - not serialized to MongoDB
        [BsonIgnore]
        public string PersistenceChannelId { get; set; }
        [BsonIgnore]
        public int? PersistenceMessageId { get; set; }
        [BsonIgnore]
        public bool PersistenceIsSplit { get; set; }
        [BsonIgnore]
        public List<int> PersistenceMessageIds { get; set; }
        [BsonIgnore]
        public int PersistenceCurrentPartIndex { get; set; }
        [BsonIgnore]
        public List<int> PersistenceCompletedPartIndices { get; set; } = new List<int>();
        [BsonIgnore]
        public Action<long, int, StateTask> OnProgressPersist { get; set; }

        public DownloadModel(string? name = null)
        {
            if (!string.IsNullOrEmpty(name))
                id = Guid.NewGuid().ToString() + ":" + name;
        }

        private ILogger logger => tis?.Logger;

        /// <summary>Throws when the user canceled or paused the download, to end the current run.</summary>
        public void throwIfStopped()
        {
            if (state == StateTask.Canceled)
                throw new TransferStoppedException($"Canceled {name}");
            // Parallel parts report after the first one noticed the pause: the run
            // is over for all of them
            if (_pausedRun)
                throw new TransferStoppedException($"Paused {name}");
            if (state == StateTask.Paused)
            {
                state = StateTask.Working;
                _pausedRun = true;
                tis.deleteDownloadInList(this);
                throw new TransferStoppedException($"Paused {name}");
            }
        }

        /// <summary>Applies a progress report; true when it completed the download.</summary>
        private bool applyProgress(long transmitted, long totalSize)
        {
            lock (_sync)
            {
                _transmitted = transmitted;
                _sizeString = HelperService.SizeSuffix(totalSize);
                _transmittedString = HelperService.SizeSuffix(transmitted);
                progress = totalSize > 0 ? Convert.ToInt32(transmitted * 100 / totalSize) : 100;
                if (transmitted == totalSize && state != StateTask.Completed)
                {
                    endnDate = DateTime.Now;
                    state = StateTask.Completed;
                    return true;
                }
                return false;
            }
        }

        private void afterProgress(bool completed)
        {
            EventChanged?.Invoke(this, new DownloadEventArgs());
            // Notify persistence service of progress (debounced there)
            OnProgressPersist?.Invoke(_transmitted, progress, state);
            if (completed)
            {
                EventStatechanged?.Invoke(this, EventArgs.Empty);
                NotificationModel nm = new NotificationModel();
                nm.sendEvent(new Notification($"Download {name} completed", "Download Completed", NotificationTypes.Success));
                tis.CheckPendingDownloads();
            }
        }

        public void ProgressCallback(long transmitted, long totalSize)
        {
            throwIfStopped();
            tis.addDownloadBytes(transmitted - _transmitted);
            afterProgress(applyProgress(transmitted, totalSize));
        }

        /// <summary>
        /// Progress reporting for multi-connection downloads, where parts arrive
        /// out of order. <paramref name="confirmedPrefix"/> is the contiguous
        /// number of bytes completed from the start of the file (the only safe
        /// resume offset for persistence), while <paramref name="chunkBytes"/> is
        /// the size of the part just received, used for live speed accounting.
        /// Throws like ProgressCallback when the task is canceled or paused.
        /// </summary>
        public void ReportParallelProgress(long confirmedPrefix, int chunkBytes, long totalSize)
        {
            throwIfStopped();
            tis.addDownloadBytes(chunkBytes);
            afterProgress(applyProgress(confirmedPrefix, totalSize));
        }

        public void Cancel()
        {
            UserPaused = false;
            lock (_sync)
                state = StateTask.Canceled;
            tis.CheckPendingDownloads();
            EventChanged?.Invoke(this, new DownloadEventArgs());
            // Lets the owner drop the persisted record: a canceled download must
            // not come back after a restart.
            EventStatechanged?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            lock (_sync)
                state = StateTask.Paused;
            EventChanged?.Invoke(this, new DownloadEventArgs());
        }

        /// <summary>
        /// Starts (or restarts) the download callback. The run is observed: a
        /// failure marks the download as failed with its reason instead of
        /// leaving it "Working" and holding a download slot forever. Only one run
        /// at a time; a start request that arrives while a paused run is still
        /// unwinding is replayed when it ends.
        /// </summary>
        public void RetryCallback()
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                _restartRequested = true;
                return;
            }
            _pausedRun = false;
            UserPaused = false;
            lock (_sync)
            {
                state = StateTask.Working;
                startDate = DateTime.Now;
                LastError = null;
            }
            EventChanged?.Invoke(this, new DownloadEventArgs());
            Task run;
            try
            {
                // Without callback nothing would be downloaded: it must not end as completed
                run = CanRestart
                    ? callbacks.callback.Invoke() ?? Task.CompletedTask
                    : Task.FromException(new InvalidOperationException("This download cannot be started again"));
            }
            catch (Exception ex)
            {
                run = Task.FromException(ex);
            }
            run.ContinueWith(OnRunEnded, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        private void OnRunEnded(Task run)
        {
            bool stateChanged = false;
            bool paused = false;
            bool restart;
            try
            {
                restart = _restartRequested;
                _restartRequested = false;
                if (run.IsFaulted || run.IsCanceled)
                {
                    Exception ex = run.Exception?.GetBaseException();
                    if (_pausedRun)
                    {
                        // Paused: the model is back in the pending queue and resumes
                        // from the confirmed bytes when it is started again.
                        _pausedRun = false;
                        paused = true;
                    }
                    else if (state != StateTask.Canceled && state != StateTask.Completed)
                    {
                        lock (_sync)
                        {
                            state = StateTask.Error;
                            endnDate = DateTime.Now;
                            LastError = ex?.Message ?? "The download was interrupted";
                        }
                        logger?.LogError(ex, "Download {Name} failed: {Message}", name, LastError);
                        stateChanged = true;
                    }
                }
                else if (state == StateTask.Working)
                {
                    // Finished without a final progress report (for instance the
                    // file was already complete on disk).
                    lock (_sync)
                    {
                        state = StateTask.Completed;
                        endnDate = DateTime.Now;
                        progress = 100;
                    }
                    stateChanged = true;
                }
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
            if (stateChanged)
            {
                EventChanged?.Invoke(this, new DownloadEventArgs());
                EventStatechanged?.Invoke(this, EventArgs.Empty);
            }
            RunFinished?.Invoke(this, EventArgs.Empty);
            // A paused download sits first in the queue: kicking the queue here
            // would restart it right away, unless it is the only one paused: then
            // it leaves its place to the next one.
            if (!paused || (UserPaused && !restart))
                tis?.CheckPendingDownloads();
            if (restart && state == StateTask.Working)
            {
                // Resumed while the paused run was ending, which took it off the list
                tis?.addToDownloadList(this);
                RetryCallback();
            }
        }
    }

    public class UploadModel
    {
        // Guards the progress fields and state transitions.
        protected readonly object _sync = new object();
        public event EventHandler<UploadEventArgs> EventChanged;
        public event EventHandler EventStatechanged;
        public string _internalId { get; set; } = Guid.NewGuid().ToString();
        public Guid id = Guid.NewGuid();
        public string action { get; set; } = "Upload";
        public StateTask state { get; set; } = StateTask.Working;
        public ChatMessages m { get; set; }
        public string name { get; set; }
        public string path { get; set; }

        public long _size { get; set; }
        public long _transmitted { get; set; } = 0;

        public string _sizeString { get; set; }
        public string _transmittedString { get; set; }
        public string chatName { get; set; }
        public IPeerInfo channel { get; set; }
        public int progress { get; set; }
        public DateTime creationDate { get; set; } = DateTime.Now;
        public DateTime startDate { get; set; }
        public DateTime endnDate { get; set; }
        public TransactionInfoService tis { get; set; }

        /// <summary>Set by the transfers that send a file to Telegram: their source stream waits while paused.</summary>
        [BsonIgnore]
        public bool CanPause { get; set; }
        // Completed when a paused upload is resumed or stopped
        private TaskCompletionSource _unpaused;

        // Persistence context - not serialized to MongoDB
        [BsonIgnore]
        public string PersistenceChannelId { get; set; }
        [BsonIgnore]
        public string PersistenceSourcePath { get; set; }
        [BsonIgnore]
        public string PersistenceDbFilePath { get; set; }
        [BsonIgnore]
        public string PersistenceParentId { get; set; }
        [BsonIgnore]
        public Action<long, int, StateTask> OnProgressPersist { get; set; }

        public virtual void ProgressCallback(long transmitted, long totalSize)
        {
            if (state == StateTask.Canceled)
                throw new TransferStoppedException($"Canceled {name}");
            tis.addUploadBytes(transmitted - _transmitted);
            bool completed = false;
            lock (_sync)
            {
                _transmitted = transmitted;
                _sizeString = HelperService.SizeSuffix(totalSize);
                _transmittedString = HelperService.SizeSuffix(transmitted);
                progress = totalSize > 0 ? Convert.ToInt32(transmitted * 100 / totalSize) : 100;
                if (transmitted == totalSize && state != StateTask.Completed)
                {
                    endnDate = DateTime.Now;
                    state = StateTask.Completed;
                    completed = true;
                }
            }
            EventChanged?.Invoke(this, new UploadEventArgs());

            // Notify persistence service of progress
            OnProgressPersist?.Invoke(_transmitted, progress, state);

            if (completed)
            {
                EventStatechanged?.Invoke(this, EventArgs.Empty);
                NotificationModel nm = new NotificationModel();
                nm.sendEvent(new Notification($"Upload {name} completed", "Upload Completed", NotificationTypes.Success));
            }
        }

        public void SendNotification()
        {
            EventChanged?.Invoke(this, new UploadEventArgs());
        }

        public void SetState(StateTask newState)
        {
            if (newState == StateTask.Working)
                startDate = DateTime.Now;
            changeState(newState);
            EventChanged?.Invoke(this, new UploadEventArgs());
        }

        public void Cancel()
        {
            changeState(StateTask.Canceled);
            EventChanged?.Invoke(this, new UploadEventArgs());
        }

        private void changeState(StateTask newState)
        {
            TaskCompletionSource waiting = null;
            lock (_sync)
            {
                state = newState;
                if (newState != StateTask.Paused)
                {
                    waiting = _unpaused;
                    _unpaused = null;
                }
            }
            waiting?.TrySetResult();
        }

        /// <summary>
        /// Pauses a running upload: the parts in flight end and no more are
        /// sent. False when it is not running or cannot be paused.
        /// </summary>
        public bool Pause()
        {
            lock (_sync)
            {
                if (!CanPause || state != StateTask.Working)
                    return false;
                state = StateTask.Paused;
                _unpaused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            EventChanged?.Invoke(this, new UploadEventArgs());
            return true;
        }

        /// <summary>Resumes a paused upload where it was. False when it is not paused.</summary>
        public bool Resume()
        {
            lock (_sync)
            {
                if (state != StateTask.Paused)
                    return false;
            }
            changeState(StateTask.Working);
            EventChanged?.Invoke(this, new UploadEventArgs());
            return true;
        }

        /// <summary>Waits while the upload is paused; throws when it is canceled.</summary>
        public async Task WaitWhilePausedAsync(CancellationToken ct = default)
        {
            while (true)
            {
                Task wait;
                lock (_sync)
                {
                    if (state == StateTask.Canceled)
                        throw new TransferStoppedException($"Canceled {name}");
                    if (state != StateTask.Paused || _unpaused == null)
                        return;
                    wait = _unpaused.Task;
                }
                await wait.WaitAsync(ct);
            }
        }

        public void InvokeEvent(UploadEventArgs uploadEventArgs)
        {
            EventChanged?.Invoke(this, uploadEventArgs);
        }

        protected void InvokeStateChanged()
        {
            EventStatechanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class SplitModel : UploadModel
    {
        public SplitModel(): base()
        {
            action = "Splitting";
        }
        public override void ProgressCallback(long transmitted, long totalSize)
        {
            bool completed = false;
            lock (_sync)
            {
                _transmitted = transmitted;
                _sizeString = HelperService.SizeSuffix(totalSize);
                _transmittedString = HelperService.SizeSuffix(transmitted);
                progress = totalSize > 0 ? Convert.ToInt32(transmitted * 100 / totalSize) : 100;
                if (transmitted == totalSize && state != StateTask.Completed)
                {
                    state = StateTask.Completed;
                    completed = true;
                }
            }
            base.InvokeEvent(new UploadEventArgs());
            if (completed)
            {
                InvokeStateChanged();
                NotificationModel nm = new NotificationModel();
                nm.sendEvent(new Notification($"Split {name} completed", "Split Completed", NotificationTypes.Success));
            }
        }
    }

    public class Md5Model : UploadModel
    {
        public Md5Model() : base()
        {
            action = "MD5 Calc";
        }
        public virtual void Init(long size, string filename)
        {
            lock (_sync)
            {
                _sizeString = HelperService.SizeSuffix(size);
                name = filename;
            }
            base.InvokeEvent(new UploadEventArgs());
        }

        public virtual void Finish()
        {
            lock (_sync)
            {
                state = StateTask.Completed;
                progress = 100;
            }
            InvokeStateChanged();
            base.InvokeEvent(new UploadEventArgs());
        }
    }
    public class XxHashModel : Md5Model
    {
        public XxHashModel() : base()
        {
            action = "XxHash Calc";
        }
    }

    public class NewDownloadModel
    {
        public string newName { get; set; }
        public string folder { get; set; }
    }

    public class DownloadEventArgs : EventArgs
    {
        //public List<DownloadModel> models { get; set; }
        //public DownloadEventArgs(List<DownloadModel> downloadModel)
        //{
        //    models = downloadModel;
        //}
        
    }

    public class InfoTaskEventArgs : EventArgs
    {
        //public List<InfoDownloadTaksModel> models { get; set; }
        //public InfoTaskEventArgs(List<InfoDownloadTaksModel> infoDownloadTaksModel)
        //{
        //    models = infoDownloadTaksModel;
        //}

    }

    public class UploadEventArgs : EventArgs
    {
        //public List<UploadModel> models { get; set; }
        //public UploadEventArgs(List<UploadModel> uploadModel)
        //{
        //    models = uploadModel;
        //}

    }

    public class Callbacks
    {
        public GenericDelegateCallback callback { get; set; }
    }

    public class DownloadRetryParams
    {
        public string dbName { get; set; }
        public int messageId {  get; set; }
        public string destPath { get; set; }

        public DownloadRetryParams(string dbName, int messageId, string destPath)
        {
            this.dbName = dbName;
            this.messageId = messageId;
            this.destPath = destPath;
        }
    }

    public class GenericFuncDelegate
    {
        public async Task CallGenericFuncDelegate(GenericDelegateCallback del)
        {
            await del();
        }
    }

    public delegate Task GenericDelegateCallback();

    public enum StateTask
    {
        [Description("Error")]
        Error,
        [Description("pending")]
        Pending,
        [Description("Canceled")]
        Canceled,
        [Description("Paused")]
        Paused,
        [Description("Completed")]
        Completed,
        [Description("Working")]
        Working
    }
}
