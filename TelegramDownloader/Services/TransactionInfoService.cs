using TelegramDownloader.Models;
using TelegramDownloader.Pages.Modals;
using TL;

namespace TelegramDownloader.Services
{
    public class TransactionInfoService
    {
        public static bool isPauseDownloads = false;
        public static event EventHandler<EventArgs> EventChanged;
        public static event EventHandler TaskEventChanged;
        public static List<DownloadModel> downloadModels = new List<DownloadModel>();
        public static List<DownloadModel> pendingDownloadModels = new List<DownloadModel>();
        public static List<UploadModel> uploadModels = new List<UploadModel>();
        public static List<InfoDownloadTaksModel> infoDownloadTaksModel = new List<InfoDownloadTaksModel>();

        private static Mutex PendingDownloadMutex = new Mutex();
        private static Mutex PendingUploadInfoTaskMutex = new Mutex();

        public bool isWorking()
        {
            return downloadModels.Where(x => x.state == StateTask.Working).Count() > 0
                || uploadModels.Where(x => x.state == StateTask.Working).Count() > 0;
        }

        public void addToDownloadList(DownloadModel downloadModel)
        {
            PendingDownloadMutex.WaitOne(10000);
            downloadModels.Insert(0, downloadModel);
            PendingDownloadMutex.ReleaseMutex();
            EventChanged?.Invoke(this, new EventArgs());
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public void addToPendingDownloadList(DownloadModel downloadModel, bool atFirst = false, bool chekDownloads = true)
        {
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
            PendingDownloadMutex.WaitOne(10000);
            isPauseDownloads = false;
            PendingDownloadMutex.ReleaseMutex();
            CheckPendingDownloads();
        }

        public void StopDownloads()
        {
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
            PendingDownloadMutex.WaitOne();
            while (downloadModels.Where(x => x.state ==  StateTask.Working).Count() < GeneralConfigStatic.config.MaxSimultaneousDownloads
                && pendingDownloadModels.Count() > 0
                && !isPauseDownloads)
            {
                DownloadModel dm = pendingDownloadModels.FirstOrDefault();
                downloadModels.Insert(0, dm);
                pendingDownloadModels.Remove(dm);
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
                idt.RetryCallback();
            }
            PendingUploadInfoTaskMutex.ReleaseMutex();
            EventChanged?.Invoke(this, new EventArgs());
            TaskEventChanged.Invoke(null, EventArgs.Empty);
        }

        public void addToUploadList(UploadModel uploadModel)
        {
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

        public List<UploadModel> GetUploadModels(int pageNumber, int pageSize)
        {
            if (uploadModels.Count() == 0 || uploadModels.Count() < (pageNumber) * pageSize)
                return uploadModels;
            return uploadModels.Skip(pageNumber * pageSize).Take(pageSize).ToList(); //.GetRange(pageNumber * pageSize, pageSize);
        }

        public int getTotalUploads()
        {
            return uploadModels.Count();
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
}
