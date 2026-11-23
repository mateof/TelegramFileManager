#nullable disable
using System.Timers;
using TelegramDownloader.Data;
using TelegramDownloader.Models;
using TelegramDownloader.Pages.Modals;
using TL;
using Timer = System.Timers.Timer;

namespace TelegramDownloader.Services
{
    public class TransactionInfoService
    {


        // Locking: each group of lists has its own monitor. Sections guarded by
        // them never await and never call back into a model (callbacks that
        // start transfers run after the lock is released), so a plain lock is
        // enough. Readers - the UI, the API - only ever see copies taken under
        // the lock, so enumerating them can never hit "collection was modified".
        private readonly object _downloadLock = new object();
        private readonly object _uploadLock = new object();
        private readonly object _taskLock = new object();

        private volatile bool _isPauseDownloads = false;
        public bool isPauseDownloads { get => _isPauseDownloads; set => _isPauseDownloads = value; }
        public event EventHandler<EventArgs> EventChanged;
        /// <summary>
        /// Aggregated, throttled event raised whenever any transaction changes:
        /// list membership (add/remove/clear) or per-model progress/state.
        /// UI components should subscribe to this single event instead of
        /// subscribing to each model individually.
        /// </summary>
        public event EventHandler TransactionsChanged;
        public event EventHandler TaskEventChanged;
        public event EventHandler HistorykEventChanged;
        public event EventHandler<SpeedHistoryEventArgs> NewSpeedHistoryPoint;

        private readonly List<DownloadModel> _downloadModels = new List<DownloadModel>();
        private readonly List<DownloadModel> _pendingDownloadModels = new List<DownloadModel>();
        private readonly List<UploadModel> _uploadModels = new List<UploadModel>();
        private readonly List<UploadModel> _pendingUploadModels = new List<UploadModel>();
        private readonly List<InfoDownloadTaksModel> _infoDownloadTaksModel = new List<InfoDownloadTaksModel>();

        /// <summary>Snapshot of the active (and finished) downloads.</summary>
        public List<DownloadModel> downloadModels { get { lock (_downloadLock) return _downloadModels.ToList(); } }
        /// <summary>Snapshot of the queued downloads.</summary>
        public List<DownloadModel> pendingDownloadModels { get { lock (_downloadLock) return _pendingDownloadModels.ToList(); } }
        /// <summary>Snapshot of the active (and finished) uploads.</summary>
        public List<UploadModel> uploadModels { get { lock (_uploadLock) return _uploadModels.ToList(); } }
        /// <summary>Snapshot of the queued uploads.</summary>
        public List<UploadModel> pendingUploadModels { get { lock (_uploadLock) return _pendingUploadModels.ToList(); } }
        /// <summary>Snapshot of the batch tasks.</summary>
        public List<InfoDownloadTaksModel> infoDownloadTaksModel { get { lock (_taskLock) return _infoDownloadTaksModel.ToList(); } }

        public String downloadSpeed = "0 KB/s";
        public String uploadSpeed = "0 KB/s";
        private long _bytesUploaded = 0;
        private long _bytesDownloaded = 0;
        public long bytesUploaded => Interlocked.Read(ref _bytesUploaded);
        public long bytesDownloaded => Interlocked.Read(ref _bytesDownloaded);
        public List<SpeedHistory> downloadSpeedsHistory { get; set; } = new List<SpeedHistory>();
        public List<SpeedHistory> uploadSpeedsHistory { get; set; } = new List<SpeedHistory>();
        private readonly object _speedHistoryLock = new object();
        private int speedHistoryCount = 0;

        public static int MAX_SPEED_HISTORY_SECONDS = 600; // 10 minutes
        public static int INTERVAL_SPEED_HISTORY_SECONDS = 3;

        private Timer aTimer;
        private readonly object _timerLock = new object();
        private readonly ILogger<IFileService> _logger;

        /// <summary>Logger shared with the transfer models, which have no DI.</summary>
        public ILogger Logger => _logger;

        // Throttling for TransactionsChanged: progress callbacks fire per network
        // chunk, so raise at most once per interval with a guaranteed trailing raise.
        private static readonly TimeSpan NotifyThrottle = TimeSpan.FromMilliseconds(250);
        private readonly object _notifyLock = new object();
        private DateTime _lastNotifyUtc = DateTime.MinValue;
        private bool _trailingNotifyScheduled = false;
        private System.Threading.Timer _trailingNotifyTimer;

        public TransactionInfoService(ILogger<IFileService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Raises <see cref="TransactionsChanged"/>, coalescing bursts so
        /// subscribers refresh at most once per <see cref="NotifyThrottle"/>.
        /// The last change in a burst is always delivered (trailing raise).
        /// </summary>
        public void NotifyTransactionsChanged()
        {
            bool raiseNow = false;
            lock (_notifyLock)
            {
                DateTime now = DateTime.UtcNow;
                if (now - _lastNotifyUtc >= NotifyThrottle)
                {
                    _lastNotifyUtc = now;
                    raiseNow = true;
                }
                else if (!_trailingNotifyScheduled)
                {
                    _trailingNotifyScheduled = true;
                    TimeSpan delay = NotifyThrottle - (now - _lastNotifyUtc);
                    if (delay < TimeSpan.Zero)
                        delay = TimeSpan.Zero;
                    if (_trailingNotifyTimer == null)
                        _trailingNotifyTimer = new System.Threading.Timer(_ => RaiseTrailingNotify(), null, delay, Timeout.InfiniteTimeSpan);
                    else
                        _trailingNotifyTimer.Change(delay, Timeout.InfiniteTimeSpan);
                }
            }
            if (raiseNow)
                SafeRaiseTransactionsChanged();
        }

        private void RaiseTrailingNotify()
        {
            lock (_notifyLock)
            {
                _trailingNotifyScheduled = false;
                _lastNotifyUtc = DateTime.UtcNow;
            }
            SafeRaiseTransactionsChanged();
        }

        private void SafeRaiseTransactionsChanged()
        {
            try
            {
                TransactionsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error notifying TransactionsChanged subscribers");
            }
        }

        // The service owns the per-model subscriptions so UI components do not
        // have to track which models are wired. Hooking is idempotent.
        private void hookModel(DownloadModel dm)
        {
            dm.EventChanged -= onDownloadModelChanged;
            dm.EventChanged += onDownloadModelChanged;
        }

        private void unhookModel(DownloadModel dm)
        {
            dm.EventChanged -= onDownloadModelChanged;
        }

        private void hookModel(UploadModel um)
        {
            um.EventChanged -= onUploadModelChanged;
            um.EventChanged += onUploadModelChanged;
        }

        private void unhookModel(UploadModel um)
        {
            um.EventChanged -= onUploadModelChanged;
        }

        private void hookModel(InfoDownloadTaksModel idt)
        {
            idt.EventChanged -= onInfoTaskModelChanged;
            idt.EventChanged += onInfoTaskModelChanged;
        }

        private void unhookModel(InfoDownloadTaksModel idt)
        {
            idt.EventChanged -= onInfoTaskModelChanged;
        }

        private void onDownloadModelChanged(object sender, DownloadEventArgs e) => NotifyTransactionsChanged();
        private void onUploadModelChanged(object sender, UploadEventArgs e) => NotifyTransactionsChanged();
        private void onInfoTaskModelChanged(object sender, InfoTaskEventArgs e) => NotifyTransactionsChanged();
        public bool isWorking()
        {
            if (!(isDownloading() || isUploading()))
            {
                stopTimer();
                return false;
            }
            startTimer();
            return true;
        }

        public bool isFileDownloaded(String path)
        {
            lock (_downloadLock)
                return _downloadModels.Any(x => x.path == path && x.state == StateTask.Working || x.state == StateTask.Pending || x.state == StateTask.Paused);
        }

        public bool isUploading()
        {
            lock (_uploadLock)
                return _uploadModels.Any(x => x.state == StateTask.Working);
        }

        public bool isSplitting()
        {
            lock (_uploadLock)
                return _uploadModels.Any(x => x.state == StateTask.Working && (x is SplitModel || x.action == "Splitting"));
        }

        public bool isCalculatingHash()
        {
            lock (_uploadLock)
                return _uploadModels.Any(x => x.state == StateTask.Working && (x is Md5Model || x is XxHashModel || x.action == "MD5 Calc" || x.action == "XxHash Calc"));
        }

        public bool isRealUploading()
        {
            lock (_uploadLock)
                return _uploadModels.Any(x => x.state == StateTask.Working && x.action == "Upload");
        }

        public bool isDownloading()
        {
            lock (_downloadLock)
                return _downloadModels.Any(x => x.state == StateTask.Working);
        }

        /// <summary>
        /// True when a transfer with this persistence id is in memory and still
        /// queued or running, i.e. resuming it again would run it twice.
        /// </summary>
        public bool IsLive(string internalId)
        {
            if (string.IsNullOrEmpty(internalId))
                return false;
            if (_liveJobs.ContainsKey(internalId))
                return true;
            static bool active(StateTask s) => s == StateTask.Working || s == StateTask.Pending;
            lock (_downloadLock)
            {
                if (_pendingDownloadModels.Any(d => d._internalId == internalId))
                    return true;
                if (_downloadModels.Any(d => d._internalId == internalId && (active(d.state) || d.IsRunning)))
                    return true;
            }
            lock (_uploadLock)
            {
                if (_uploadModels.Any(u => u._internalId == internalId && active(u.state)))
                    return true;
            }
            lock (_taskLock)
            {
                if (_infoDownloadTaksModel.Any(t => t._internalId == internalId && (active(t.state) || t.IsRunning)))
                    return true;
            }
            return false;
        }

        // Persisted jobs that run as several in-memory models (a split download
        // runs one model per part), registered while their loop is active.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _liveJobs = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();

        /// <summary>Marks a multi-model job as running; false when it already is.</summary>
        public bool TryRegisterJob(string internalId) => _liveJobs.TryAdd(internalId, 0);

        public void UnregisterJob(string internalId) => _liveJobs.TryRemove(internalId, out _);

        /// <summary>
        /// True when a queued or running download writes to <paramref name="path"/>:
        /// two downloads into one file would corrupt it.
        /// </summary>
        public bool IsDestinationBusy(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            string target = normalizePath(path);
            lock (_downloadLock)
            {
                return _pendingDownloadModels.Any(d => normalizePath(d.path) == target)
                    || _downloadModels.Any(d => (d.state == StateTask.Working || d.IsRunning) && normalizePath(d.path) == target);
            }
        }

        private static string normalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
            }
            path = path.Replace('\\', '/');
            return OperatingSystem.IsWindows() ? path.ToLowerInvariant() : path;
        }

        /// <summary>The in-memory download with this persistence id, if any.</summary>
        public DownloadModel FindDownload(string internalId)
        {
            lock (_downloadLock)
                return _pendingDownloadModels.FirstOrDefault(d => d._internalId == internalId)
                    ?? _downloadModels.FirstOrDefault(d => d._internalId == internalId);
        }

        /// <summary>The in-memory batch task with this persistence id, if any.</summary>
        public InfoDownloadTaksModel FindTask(string internalId)
        {
            lock (_taskLock)
                return _infoDownloadTaksModel.FirstOrDefault(t => t._internalId == internalId);
        }

        public void startTimer()
        {
            lock (_timerLock)
            {
                if (aTimer == null)
                {
                    aTimer = new Timer(1000);
                    aTimer.Elapsed += OnTimedEvent;
                    aTimer.AutoReset = true;
                }
                if (!aTimer.Enabled)
                    aTimer.Start();
            }
        }

        public void stopTimer()
        {
            lock (_timerLock)
            {
                if (aTimer == null || !aTimer.Enabled || !canStopTimer())
                    return;
                aTimer.Stop();
            }
            resetUploadBytes();
            resetDownloadBytes();
        }

        private void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            try
            {
                long downloaded = bytesDownloaded;
                long uploaded = bytesUploaded;
                if (downloaded > 0)
                    setDownloadSpeed(HelperService.SizeSuffixPerTime(downloaded));
                if (uploaded > 0)
                    setUploadSpeed(HelperService.SizeSuffixPerTime(uploaded));
                if (speedHistoryCount++ >= INTERVAL_SPEED_HISTORY_SECONDS)
                {
                    speedHistoryCount = 0;
                    setHistorySpeed();
                }
                resetDownloadBytes();
                resetUploadBytes();
            }
            catch (Exception ex)
            {
                // A timer callback that throws would take the process down.
                _logger.LogWarning(ex, "Error updating the transfer speed");
            }
        }

        private bool canStopTimer()
        {
            lock (_speedHistoryLock)
            {
                return uploadSpeedsHistory.Sum(x => x.speed) + downloadSpeedsHistory.Sum(x => x.speed) == 0;
            }
        }

        private void setHistorySpeed()
        {
            DateTime now = DateTime.Now;
            List<string> activeDownloads;
            List<string> activeUploads;
            lock (_downloadLock)
                activeDownloads = _downloadModels.Where(x => x.state == StateTask.Working).Select(x => x.name).ToList();
            lock (_uploadLock)
                activeUploads = _uploadModels.Where(x => x.state == StateTask.Working).Select(x => x.name).ToList();

            var newDownloadPoint = new SpeedHistory() { time = now, speed = bytesDownloaded, speedString = downloadSpeed, activeFiles = activeDownloads };
            var newUploadPoint = new SpeedHistory() { time = now, speed = bytesUploaded, speedString = uploadSpeed, activeFiles = activeUploads };

            lock (_speedHistoryLock)
            {
                downloadSpeedsHistory.Add(newDownloadPoint);
                uploadSpeedsHistory.Add(newUploadPoint);
                downloadSpeedsHistory.RemoveAll(x => (now - x.time).TotalSeconds > MAX_SPEED_HISTORY_SECONDS);
                uploadSpeedsHistory.RemoveAll(x => (now - x.time).TotalSeconds > MAX_SPEED_HISTORY_SECONDS);
            }

            // Invoke new event with individual points
            NewSpeedHistoryPoint?.Invoke(this, new SpeedHistoryEventArgs(newDownloadPoint, newUploadPoint));

            // Keep legacy event for backwards compatibility
            HistorykEventChanged?.Invoke(this, EventArgs.Empty);
        }

        public List<SpeedHistory> GetDownloadSpeedsHistoryCopy()
        {
            lock (_speedHistoryLock)
            {
                return downloadSpeedsHistory.ToList();
            }
        }

        public List<SpeedHistory> GetUploadSpeedsHistoryCopy()
        {
            lock (_speedHistoryLock)
            {
                return uploadSpeedsHistory.ToList();
            }
        }

        public Task addDownloadBytes(long bytes)
        {
            Interlocked.Add(ref _bytesDownloaded, bytes);
            return Task.CompletedTask;
        }

        public Task addUploadBytes(long bytes)
        {
            Interlocked.Add(ref _bytesUploaded, bytes);
            return Task.CompletedTask;
        }

        public Task resetDownloadBytes()
        {
            Interlocked.Exchange(ref _bytesDownloaded, 0);
            if (!isDownloading())
                setDownloadSpeed("0 KB/s");
            return Task.CompletedTask;
        }

        public Task resetUploadBytes()
        {
            Interlocked.Exchange(ref _bytesUploaded, 0);
            if (!isUploading())
                setUploadSpeed("0 KB/s");
            return Task.CompletedTask;
        }

        public void setDownloadSpeed(String speed)
        {
            downloadSpeed = speed;
            TaskEventChanged?.Invoke(null, EventArgs.Empty);
        }

        public void setUploadSpeed(String speed)
        {
            uploadSpeed = speed;
            TaskEventChanged?.Invoke(null, EventArgs.Empty);
        }

        private void raiseListChanged()
        {
            EventChanged?.Invoke(this, new EventArgs());
            TaskEventChanged?.Invoke(null, EventArgs.Empty);
            NotifyTransactionsChanged();
        }

        public void addToDownloadList(DownloadModel downloadModel)
        {
            lock (_downloadLock)
            {
                // Check if already exists to avoid duplicate key errors in UI
                if (_downloadModels.Any(d => d._internalId == downloadModel._internalId))
                {
                    _logger.LogDebug("Download already in list, skipping - Name: {Name}, InternalId: {Id}",
                        downloadModel.name, downloadModel._internalId);
                    return;
                }
                _downloadModels.Insert(0, downloadModel);
            }
            _logger.LogInformation("Adding to download list - Name: {Name}, Size: {SizeMB:F2}MB",
                downloadModel.name, downloadModel._size / (1024.0 * 1024.0));
            hookModel(downloadModel);
            raiseListChanged();
        }

        public void addToPendingDownloadList(DownloadModel downloadModel, bool atFirst = false, bool chekDownloads = true)
        {
            bool added = false;
            int count;
            lock (_downloadLock)
            {
                // Check if already exists to avoid duplicate key errors in UI
                if (!_pendingDownloadModels.Any(d => d._internalId == downloadModel._internalId))
                {
                    if (atFirst)
                        _pendingDownloadModels.Insert(0, downloadModel);
                    else
                        _pendingDownloadModels.Add(downloadModel);
                    added = true;
                }
                count = _pendingDownloadModels.Count;
            }
            if (added)
            {
                _logger.LogDebug("Adding to pending download list - Name: {Name}, AtFirst: {AtFirst}, PendingCount: {Count}",
                    downloadModel.name, atFirst, count);
                hookModel(downloadModel);
                NotifyTransactionsChanged();
            }
            else
            {
                _logger.LogDebug("Download already in pending list, skipping - Name: {Name}, InternalId: {Id}",
                    downloadModel.name, downloadModel._internalId);
            }
            if (chekDownloads)
                CheckPendingDownloads();
        }

        public void PauseDownloads()
        {
            List<DownloadModel> paused;
            lock (_downloadLock)
            {
                isPauseDownloads = true;
                paused = _downloadModels.Where(x => x.state == StateTask.Working).ToList();
                foreach (DownloadModel dm in paused)
                {
                    if (!_pendingDownloadModels.Contains(dm))
                        _pendingDownloadModels.Insert(0, dm);
                }
            }
            _logger.LogInformation("Pausing all downloads - ActiveCount: {Count}", paused.Count);
            foreach (DownloadModel dm in paused)
                dm.Pause();
            TaskEventChanged?.Invoke(null, EventArgs.Empty);
            NotifyTransactionsChanged();
        }

        public void PlayDownloads()
        {
            _logger.LogInformation("Resuming downloads - PendingCount: {Count}", getTotalDownloads(true));
            isPauseDownloads = false;
            CheckPendingDownloads();
        }

        public void StopDownloads()
        {
            List<DownloadModel> working;
            int pending;
            lock (_downloadLock)
            {
                isPauseDownloads = true;
                working = _downloadModels.Where(x => x.state == StateTask.Working).ToList();
                pending = _pendingDownloadModels.Count;
                _pendingDownloadModels.Clear();
            }
            _logger.LogInformation("Stopping all downloads - ActiveCount: {Active}, PendingCount: {Pending}", working.Count, pending);
            foreach (DownloadModel dm in working)
                dm.Pause();
            TaskEventChanged?.Invoke(null, EventArgs.Empty);
            NotifyTransactionsChanged();
        }

        /// <summary>
        /// Starts queued downloads while there are free slots. The models are
        /// moved under the lock but started after releasing it, so a transfer
        /// callback never runs while the list lock is held. (This used to block
        /// the calling thread - often a Telegram progress callback - for 500 ms.)
        /// </summary>
        public Task CheckPendingDownloads()
        {
            List<DownloadModel> toStart = new List<DownloadModel>();
            lock (_downloadLock)
            {
                int max = GeneralConfigStatic.config?.MaxSimultaneousDownloads ?? 1;
                int working = _downloadModels.Count(x => x.state == StateTask.Working);
                while (working < max && _pendingDownloadModels.Count > 0 && !isPauseDownloads)
                {
                    DownloadModel dm = _pendingDownloadModels[0];
                    _pendingDownloadModels.RemoveAt(0);
                    _downloadModels.Remove(dm);
                    _downloadModels.Insert(0, dm);
                    // Canceled while queued: listed as canceled, never started.
                    if (dm.state == StateTask.Canceled)
                        continue;
                    // Counted as working right away: RetryCallback sets the state.
                    working++;
                    toStart.Add(dm);
                }
            }
            foreach (DownloadModel dm in toStart)
            {
                hookModel(dm);
                startTimer();
                try
                {
                    dm.RetryCallback();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not start download {Name}", dm.name);
                }
            }
            raiseListChanged();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Starts the oldest pending batch task when no batch is running (batch
        /// tasks run one at a time).
        /// </summary>
        public Task CheckPendingUploadInfoTasks()
        {
            InfoDownloadTaksModel toStart = null;
            lock (_taskLock)
            {
                if (!_infoDownloadTaksModel.Any(x => x.state == StateTask.Working))
                {
                    toStart = _infoDownloadTaksModel.Where(x => x.state == StateTask.Pending).OrderBy(x => x.creationDate).FirstOrDefault();
                    if (toStart != null)
                        toStart.state = StateTask.Working;
                }
            }
            if (toStart != null)
            {
                hookModel(toStart);
                startTimer();
                try
                {
                    toStart.RetryCallback();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not start task {Id}", toStart._internalId);
                }
            }
            raiseListChanged();
            return Task.CompletedTask;
        }

        public void addToUploadList(UploadModel uploadModel)
        {
            lock (_uploadLock)
            {
                // Check if already exists to avoid duplicate key errors in UI
                if (_uploadModels.Any(u => u._internalId == uploadModel._internalId))
                {
                    _logger.LogDebug("Upload already in list, skipping - Name: {Name}, InternalId: {Id}",
                        uploadModel.name, uploadModel._internalId);
                    return;
                }
                _uploadModels.Insert(0, uploadModel);
            }
            _logger.LogInformation("Adding to upload list - Name: {Name}, Size: {SizeMB:F2}MB",
                uploadModel.name, uploadModel._size / (1024.0 * 1024.0));
            hookModel(uploadModel);
            raiseListChanged();
        }

        public void deleteUploadInList(UploadModel uploadModel)
        {
            lock (_uploadLock)
                _uploadModels.Remove(uploadModel);
            unhookModel(uploadModel);
            raiseListChanged();
        }

        public void addToInfoDownloadTaskList(InfoDownloadTaksModel infoDownloadModel)
        {
            lock (_taskLock)
            {
                if (!_infoDownloadTaksModel.Contains(infoDownloadModel))
                    _infoDownloadTaksModel.Add(infoDownloadModel);
            }
            hookModel(infoDownloadModel);
            NotifyTransactionsChanged();
            CheckPendingUploadInfoTasks();
        }

        private static List<T> page<T>(List<T> source, int pageNumber, int pageSize)
        {
            if (source.Count == 0 || source.Count < pageNumber * pageSize)
                return source;
            return source.Skip(pageNumber * pageSize).Take(pageSize).ToList();
        }

        public List<DownloadModel> GetDownloadModels(int pageNumber, int pageSize, bool isPending = false)
        {
            return page(isPending ? pendingDownloadModels : downloadModels, pageNumber, pageSize);
        }

        public int getTotalDownloads(bool isPending = false)
        {
            lock (_downloadLock)
                return isPending ? _pendingDownloadModels.Count : _downloadModels.Count;
        }

        public List<UploadModel> GetUploadModels(int pageNumber, int pageSize, bool isPending = false)
        {
            return page(isPending ? pendingUploadModels : uploadModels, pageNumber, pageSize);
        }

        public int getTotalUploads(bool isPending = false)
        {
            lock (_uploadLock)
                return isPending ? _pendingUploadModels.Count : _uploadModels.Count;
        }

        public void addToPendingUploadList(UploadModel uploadModel)
        {
            lock (_uploadLock)
            {
                // Check if already exists to avoid duplicate key errors in UI
                if (_pendingUploadModels.Any(u => u._internalId == uploadModel._internalId))
                {
                    _logger.LogDebug("Upload already in pending list, skipping - Name: {Name}, InternalId: {Id}",
                        uploadModel.name, uploadModel._internalId);
                    return;
                }
                _pendingUploadModels.Add(uploadModel);
            }
            hookModel(uploadModel);
            raiseListChanged();
        }

        public void deletePendingUploadInList(UploadModel uploadModel)
        {
            bool stillListed;
            lock (_uploadLock)
            {
                _pendingUploadModels.Remove(uploadModel);
                stillListed = _uploadModels.Contains(uploadModel);
            }
            if (!stillListed)
                unhookModel(uploadModel);
            raiseListChanged();
        }

        public void deleteDownloadInList(DownloadModel downloadModel)
        {
            bool stillPending;
            lock (_downloadLock)
            {
                _downloadModels.Remove(downloadModel);
                stillPending = _pendingDownloadModels.Contains(downloadModel);
            }
            // A paused download is removed from the active list but stays in the
            // pending list, so keep it hooked in that case.
            if (!stillPending)
                unhookModel(downloadModel);
            raiseListChanged();
        }

        public void deletePendingDownloadInList(DownloadModel downloadModel)
        {
            bool stillListed;
            lock (_downloadLock)
            {
                _pendingDownloadModels.Remove(downloadModel);
                stillListed = _downloadModels.Contains(downloadModel);
            }
            if (!stillListed)
                unhookModel(downloadModel);
            raiseListChanged();
        }

        public void ClearPendingDownloads()
        {
            List<DownloadModel> removed;
            List<DownloadModel> active;
            lock (_downloadLock)
            {
                removed = _pendingDownloadModels.ToList();
                _pendingDownloadModels.Clear();
                active = _downloadModels.ToList();
            }
            _logger.LogInformation("Clearing all pending downloads - Count: {Count}", removed.Count);
            foreach (DownloadModel dm in removed.Where(x => !active.Contains(x)))
                unhookModel(dm);
            raiseListChanged();
        }

        public void ClearPendingUploads()
        {
            List<UploadModel> removed;
            List<UploadModel> active;
            lock (_uploadLock)
            {
                removed = _pendingUploadModels.ToList();
                _pendingUploadModels.Clear();
                active = _uploadModels.ToList();
            }
            _logger.LogInformation("Clearing all pending uploads - Count: {Count}", removed.Count);
            foreach (UploadModel um in removed.Where(x => !active.Contains(x)))
                unhookModel(um);
            raiseListChanged();
        }

        public void deleteInfoDownloadTaskFromList(InfoDownloadTaksModel idt)
        {
            lock (_taskLock)
                _infoDownloadTaksModel.Remove(idt);
            unhookModel(idt);
            raiseListChanged();
        }

        public List<InfoDownloadTaksModel> getInfoDownloadTaksModel(int pageNumber, int pageSize)
        {
            return page(infoDownloadTaksModel.OrderBy(x => x.creationDate).ToList(), pageNumber, pageSize);
        }

        public int getTotalTasks()
        {
            lock (_taskLock)
                return _infoDownloadTaksModel.Count;
        }

        /// <summary>
        /// A finished entry can be cleared away. When <paramref name="states"/>
        /// is given, only those states qualify, so a caller can drop just the
        /// completed ones and keep the failures on screen.
        /// </summary>
        private static bool isClearable(StateTask state, IReadOnlyCollection<StateTask>? states)
        {
            return state != StateTask.Working && (states == null || states.Contains(state));
        }

        public void clearUploadCompleted(IReadOnlyCollection<StateTask>? states = null)
        {
            List<UploadModel> removed;
            lock (_uploadLock)
            {
                removed = _uploadModels.Where(x => isClearable(x.state, states)).ToList();
                _uploadModels.RemoveAll(x => removed.Contains(x));
            }
            foreach (UploadModel um in removed)
                unhookModel(um);
            EventChanged?.Invoke(this, new EventArgs());
            NotifyTransactionsChanged();
        }

        public void clearDownloadCompleted(IReadOnlyCollection<StateTask>? states = null)
        {
            List<DownloadModel> removed;
            List<DownloadModel> pending;
            lock (_downloadLock)
            {
                removed = _downloadModels.Where(x => isClearable(x.state, states) && !x.IsRunning).ToList();
                _downloadModels.RemoveAll(x => removed.Contains(x));
                pending = _pendingDownloadModels.ToList();
            }
            foreach (DownloadModel dm in removed.Where(x => !pending.Contains(x)))
                unhookModel(dm);
            EventChanged?.Invoke(this, new EventArgs());
            NotifyTransactionsChanged();
        }

        public void clearTasksCompleted(IReadOnlyCollection<StateTask>? states = null)
        {
            List<InfoDownloadTaksModel> removed;
            lock (_taskLock)
            {
                removed = _infoDownloadTaksModel.Where(x => isClearable(x.state, states) && !x.IsRunning).ToList();
                _infoDownloadTaksModel.RemoveAll(x => removed.Contains(x));
            }
            foreach (InfoDownloadTaksModel idt in removed)
                unhookModel(idt);
            EventChanged?.Invoke(this, new EventArgs());
            NotifyTransactionsChanged();
        }
    }

    public class SpeedHistory
    {
        public DateTime time { get; set; }
        public long speed { get; set; }
        public String speedString { get; set; }
        public List<string> activeFiles { get; set; } = new List<string>();
    }

    public class SpeedHistoryEventArgs : EventArgs
    {
        public SpeedHistory DownloadPoint { get; }
        public SpeedHistory UploadPoint { get; }

        public SpeedHistoryEventArgs(SpeedHistory downloadPoint, SpeedHistory uploadPoint)
        {
            DownloadPoint = downloadPoint;
            UploadPoint = uploadPoint;
        }
    }
}
