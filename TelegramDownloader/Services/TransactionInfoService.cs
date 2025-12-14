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


        public bool isPauseDownloads = false;
        public event EventHandler<EventArgs> EventChanged;
        public event EventHandler TaskEventChanged;
        public event EventHandler HistorykEventChanged;
        public event EventHandler<SpeedHistoryEventArgs> NewSpeedHistoryPoint;
        public List<DownloadModel> downloadModels = new List<DownloadModel>();
        public List<DownloadModel> pendingDownloadModels = new List<DownloadModel>();
        public List<UploadModel> uploadModels = new List<UploadModel>();
        public List<UploadModel> pendingUploadModels = new List<UploadModel>();
        public List<InfoDownloadTaksModel> infoDownloadTaksModel = new List<InfoDownloadTaksModel>();
        public String downloadSpeed = "0 KB/s";
        public String uploadSpeed = "0 KB/s";
        public long bytesUploaded = 0;
        public long bytesDownloaded = 0;
        public List<SpeedHistory> downloadSpeedsHistory { get; set; } = new List<SpeedHistory>();
        public List<SpeedHistory> uploadSpeedsHistory { get; set; } = new List<SpeedHistory>();
        private readonly object _speedHistoryLock = new object();
        private int speedHistoryCount = 0;

        private static Mutex PendingDownloadMutex = new Mutex();
        private static Mutex PendingUploadMutex = new Mutex();
        private static Mutex PendingUploadInfoTaskMutex = new Mutex();
        private static Mutex DownloadBytesMutex = new Mutex();
        private static Mutex UploadBytesMutex = new Mutex();

        public static int MAX_SPEED_HISTORY_SECONDS = 600; // 10 minutes
        public static int INTERVAL_SPEED_HISTORY_SECONDS = 3;

        private Timer aTimer;
        private readonly ILogger<IFileService> _logger;

        public TransactionInfoService(ILogger<IFileService> logger)
        {
            _logger = logger;
        }
        public bool isWorking()
        {
            if (!(isDownloading() || isUploading()))
            {
                //if (!isDownloading())
                //{
                //    resetDownloadBytes();
                //}
                //if (!isUploading())
                //{
                //    resetUploadBytes();
                //}
                stopTimer();
                return false;
            }
            startTimer();
            return true;
        }

        public bool isFileDownloaded(String path)
        {
            return downloadModels.Any(x => x.path == path && x.state == StateTask.Working || x.state == StateTask.Pending || x.state == StateTask.Paused);
        }

        public bool isUploading()
        {
            return uploadModels.Any(x => x.state == StateTask.Working);
        }

        public bool isDownloading()
        {
            return downloadModels.Any(x => x.state == StateTask.Working);
        }

        public void startTimer()
        {
            if (aTimer == null || !aTimer.Enabled)
            {
                if (aTimer != null)
                {
                    aTimer.Start();
                    return;
                }
                aTimer = new Timer(1000);
                aTimer.Elapsed += OnTimedEvent;
                aTimer.AutoReset = true;
                aTimer.Enabled = true;
            }
        }

        public void stopTimer()
        {
            if (aTimer != null && aTimer.Enabled && canStopTimer())
            {
                aTimer.Stop();
                resetUploadBytes();
                resetDownloadBytes();
            }
        }

        private void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            //_logger.LogInformation("El evento Elapsed se disparó a las {0:HH:mm:ss.fff}", e.SignalTime);
            //_logger.LogInformation("Upload: {0}, download: {1}", bytesUploaded, bytesDownloaded);
            if (bytesDownloaded > 0)
                setDownloadSpeed(HelperService.SizeSuffixPerTime(bytesDownloaded));
            if (bytesUploaded > 0)
                setUploadSpeed(HelperService.SizeSuffixPerTime(bytesUploaded));
            if (speedHistoryCount++ >= INTERVAL_SPEED_HISTORY_SECONDS)
            {
                speedHistoryCount = 0;
                setHistorySpeed();
            }
            resetDownloadBytes();
            resetUploadBytes();
            if (!aTimer.Enabled)
                stopTimer();
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
            var activeDownloads = downloadModels.Where(x => x.state == StateTask.Working).Select(x => x.name).ToList();
            var activeUploads = uploadModels.Where(x => x.state == StateTask.Working).Select(x => x.name).ToList();

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

        public async Task addDownloadBytes(long bytes)
        {
            DownloadBytesMutex.WaitOne();
            bytesDownloaded += bytes;
            DownloadBytesMutex.ReleaseMutex();
        }

        public async Task addUploadBytes(long bytes)
        {
            UploadBytesMutex.WaitOne();
            bytesUploaded += bytes;
            UploadBytesMutex.ReleaseMutex();
        }

        public async Task resetDownloadBytes()
        {
            DownloadBytesMutex.WaitOne();
            bytesDownloaded = 0;
            if (!isDownloading())
                setDownloadSpeed("0 KB/s");
            DownloadBytesMutex.ReleaseMutex();
        }

        public async Task resetUploadBytes()
        {
            UploadBytesMutex.WaitOne();
            bytesUploaded = 0;
            if (!isUploading())
                setUploadSpeed("0 KB/s");
            UploadBytesMutex.ReleaseMutex();
        }

        public void setDownloadSpeed(String speed)
        {
            downloadSpeed = speed;
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public void setUploadSpeed(String speed)
        {
            //_logger.LogInformation("Set bytes speed upload {0}", speed);
            uploadSpeed = speed;
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public void addToDownloadList(DownloadModel downloadModel)
        {
            _logger.LogInformation("Adding to download list - Name: {Name}, Size: {SizeMB:F2}MB",
                downloadModel.name, downloadModel._size / (1024.0 * 1024.0));
            PendingDownloadMutex.WaitOne(10000);
            downloadModels.Insert(0, downloadModel);
            PendingDownloadMutex.ReleaseMutex();
            EventChanged?.Invoke(this, new EventArgs());
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public void addToPendingDownloadList(DownloadModel downloadModel, bool atFirst = false, bool chekDownloads = true)
        {
            _logger.LogDebug("Adding to pending download list - Name: {Name}, AtFirst: {AtFirst}, PendingCount: {Count}",
                downloadModel.name, atFirst, pendingDownloadModels.Count + 1);
            PendingDownloadMutex.WaitOne(10000);
            if (atFirst)
                pendingDownloadModels.Insert(0, downloadModel);
            else
                pendingDownloadModels.Add(downloadModel);
            PendingDownloadMutex.ReleaseMutex();
            if (chekDownloads)
                CheckPendingDownloads();
        }

        public void PauseDownloads()
        {
            _logger.LogInformation("Pausing all downloads - ActiveCount: {Count}", downloadModels.Where(x => x.state == StateTask.Working).Count());
            PendingDownloadMutex.WaitOne(10000);
            isPauseDownloads = true;
            foreach (DownloadModel dm in downloadModels.Where(x => x.state == StateTask.Working).ToList())
            {
                pendingDownloadModels.Insert(0, dm);
                dm.Pause();
            }
            PendingDownloadMutex.ReleaseMutex();
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public void PlayDownloads()
        {
            _logger.LogInformation("Resuming downloads - PendingCount: {Count}", pendingDownloadModels.Count);
            PendingDownloadMutex.WaitOne(10000);
            isPauseDownloads = false;
            PendingDownloadMutex.ReleaseMutex();
            CheckPendingDownloads();
        }

        public void StopDownloads()
        {
            _logger.LogInformation("Stopping all downloads - ActiveCount: {Active}, PendingCount: {Pending}",
                downloadModels.Where(x => x.state == StateTask.Working).Count(), pendingDownloadModels.Count);
            PendingDownloadMutex.WaitOne(10000);
            isPauseDownloads = true;
            foreach (DownloadModel dm in downloadModels.Where(x => x.state == StateTask.Working).ToList())
            {
                dm.Pause();
            }
            pendingDownloadModels.Clear();
            PendingDownloadMutex.ReleaseMutex();
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public async Task CheckPendingDownloads()
        {
            Thread.Sleep(500);
            PendingDownloadMutex.WaitOne();
            while (downloadModels.Where(x => x.state == StateTask.Working).Count() < GeneralConfigStatic.config.MaxSimultaneousDownloads
                && pendingDownloadModels.Count() > 0
                && !isPauseDownloads)
            {
                DownloadModel dm = pendingDownloadModels.FirstOrDefault();
                downloadModels.Insert(0, dm);
                pendingDownloadModels.Remove(dm);
                startTimer();
                dm.RetryCallback();
            }
            PendingDownloadMutex.ReleaseMutex();
            EventChanged?.Invoke(this, new EventArgs());
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public async Task CheckPendingUploadInfoTasks()
        {
            PendingUploadInfoTaskMutex.WaitOne();
            while (infoDownloadTaksModel.Where(x => x.state == StateTask.Working).Count() < 1
                && infoDownloadTaksModel.Where(x => x.state == StateTask.Pending).Count() > 0)
            {
                InfoDownloadTaksModel idt = infoDownloadTaksModel.Where(x => x.state == StateTask.Pending).OrderBy(x => x.creationDate).FirstOrDefault();
                idt.state = StateTask.Working;
                startTimer();
                idt.RetryCallback();
            }
            PendingUploadInfoTaskMutex.ReleaseMutex();
            EventChanged?.Invoke(this, new EventArgs());
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public void addToUploadList(UploadModel uploadModel)
        {
            _logger.LogInformation("Adding to upload list - Name: {Name}, Size: {SizeMB:F2}MB",
                uploadModel.name, uploadModel._size / (1024.0 * 1024.0));
            uploadModels.Insert(0, uploadModel);
            EventChanged?.Invoke(this, new EventArgs());
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public void deleteUploadInList(UploadModel uploadModel)
        {
            uploadModels.Remove(uploadModel);
            EventChanged?.Invoke(this, new EventArgs());
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public void addToInfoDownloadTaskList(InfoDownloadTaksModel infoDownloadModel)
        {
            PendingUploadInfoTaskMutex.WaitOne();
            infoDownloadTaksModel.Add(infoDownloadModel);
            PendingUploadInfoTaskMutex.ReleaseMutex();
            CheckPendingUploadInfoTasks();
        }


        public List<DownloadModel> GetDownloadModels(int pageNumber, int pageSize, bool isPending = false)
        {
            if (isPending)
            {
                if (pendingDownloadModels.Count() == 0 || pendingDownloadModels.Count() < (pageNumber) * pageSize)
                    return pendingDownloadModels;
                return pendingDownloadModels.Skip(pageNumber * pageSize).Take(pageSize).ToList();
            }
            if (downloadModels.Count() == 0 || downloadModels.Count() < (pageNumber) * pageSize)
                return downloadModels;
            return downloadModels.Skip(pageNumber * pageSize).Take(pageSize).ToList(); //.GetRange(pageNumber * pageSize, pageSize);
        }

        public int getTotalDownloads(bool isPending = false)
        {
            return isPending ? pendingDownloadModels.Count() : downloadModels.Count();
        }

        public List<UploadModel> GetUploadModels(int pageNumber, int pageSize, bool isPending = false)
        {
            var sourceList = isPending ? pendingUploadModels : uploadModels;
            if (sourceList.Count() == 0 || sourceList.Count() < (pageNumber) * pageSize)
                return sourceList;
            return sourceList.Skip(pageNumber * pageSize).Take(pageSize).ToList();
        }

        public int getTotalUploads(bool isPending = false)
        {
            return isPending ? pendingUploadModels.Count() : uploadModels.Count();
        }

        public void addToPendingUploadList(UploadModel uploadModel)
        {
            PendingUploadMutex.WaitOne();
            pendingUploadModels.Add(uploadModel);
            PendingUploadMutex.ReleaseMutex();
            EventChanged?.Invoke(this, new EventArgs());
            TaskEventChanged?.Invoke(null, EventArgs.Empty);
        }

        public void deletePendingUploadInList(UploadModel uploadModel)
        {
            PendingUploadMutex.WaitOne();
            pendingUploadModels.Remove(uploadModel);
            PendingUploadMutex.ReleaseMutex();
            EventChanged?.Invoke(this, new EventArgs());
            TaskEventChanged?.Invoke(null, EventArgs.Empty);
        }

        public void deleteDownloadInList(DownloadModel downloadModel)
        {
            PendingDownloadMutex.WaitOne();
            downloadModels.Remove(downloadModel);
            PendingDownloadMutex.ReleaseMutex();
            EventChanged?.Invoke(this, new EventArgs());
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public void deletePendingDownloadInList(DownloadModel downloadModel)
        {
            PendingDownloadMutex.WaitOne();
            pendingDownloadModels.Remove(downloadModel);
            PendingDownloadMutex.ReleaseMutex();
            EventChanged?.Invoke(this, new EventArgs());
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public void deleteInfoDownloadTaskFromList(InfoDownloadTaksModel idt)
        {
            PendingUploadInfoTaskMutex.WaitOne();
            infoDownloadTaksModel.Remove(idt);
            PendingUploadInfoTaskMutex.ReleaseMutex();
            EventChanged?.Invoke(this, new EventArgs());
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public List<InfoDownloadTaksModel> getInfoDownloadTaksModel(int pageNumber, int pageSize)
        {
            if (infoDownloadTaksModel.Count() == 0 || infoDownloadTaksModel.Count() < (pageNumber) * pageSize)
                return infoDownloadTaksModel.OrderBy(x => x.creationDate).ToList();
            return infoDownloadTaksModel.OrderBy(x => x.creationDate).Skip(pageNumber * pageSize).Take(pageSize).ToList();
        }

        public int getTotalTasks()
        {
            return infoDownloadTaksModel.Count();
        }

        public void clearUploadCompleted()
        {
            uploadModels.RemoveAll(x => x.state != StateTask.Working);
            EventChanged?.Invoke(this, new EventArgs());
        }

        public void clearDownloadCompleted()
        {
            PendingDownloadMutex.WaitOne();
            downloadModels.RemoveAll(x => x.state != StateTask.Working);
            PendingDownloadMutex.ReleaseMutex();
            EventChanged?.Invoke(this, new EventArgs());
        }

        public void clearTasksCompleted()
        {
            infoDownloadTaksModel.RemoveAll(x => x.state != StateTask.Working);
            EventChanged?.Invoke(this, new EventArgs());
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
