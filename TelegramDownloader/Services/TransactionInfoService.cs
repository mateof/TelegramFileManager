using TelegramDownloader.Models;
using TelegramDownloader.Pages.Modals;
using TL;

namespace TelegramDownloader.Services
{
    public class TransactionInfoService
    {
        public static event EventHandler<EventArgs> EventChanged;
        public static List<DownloadModel> downloadModels = new List<DownloadModel>();
        public static List<UploadModel> uploadModels = new List<UploadModel>();
        public static List<InfoDownloadTaksModel> infoDownloadTaksModel = new List<InfoDownloadTaksModel>();

        public void addToDownloadList(DownloadModel downloadModel)
        {
            downloadModels.Insert(0, downloadModel);
            EventChanged?.Invoke(this, new EventArgs());
        }

        public void addToUploadList(UploadModel uploadModel)
        {
            uploadModels.Insert(0, uploadModel);
            EventChanged?.Invoke(this, new EventArgs());
        }

        public void deleteUploadInList(UploadModel uploadModel)
        {
            uploadModels.Remove(uploadModel);
            EventChanged?.Invoke(this, new EventArgs());
        }

        public void addToInfoDownloadTaskList(InfoDownloadTaksModel infoDownloadModel)
        {
            infoDownloadTaksModel.Insert(0, infoDownloadModel);
            EventChanged?.Invoke(this, new EventArgs());
        }


        public List<DownloadModel> GetDownloadModels(int pageNumber, int pageSize)
        {
            if (downloadModels.Count() == 0 || downloadModels.Count() < (pageNumber) * pageSize)
                return downloadModels;
            return downloadModels.Skip(pageNumber * pageSize).Take(pageSize).ToList(); //.GetRange(pageNumber * pageSize, pageSize);
        }

        public int getTotalDownloads()
        {
            return downloadModels.Count();
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
            downloadModels.Remove(downloadModel);
            EventChanged?.Invoke(this, new EventArgs());
        }

        public List<InfoDownloadTaksModel> getInfoDownloadTaksModel(int pageNumber, int pageSize)
        {
            if (infoDownloadTaksModel.Count() == 0 || infoDownloadTaksModel.Count() < (pageNumber) * pageSize)
                return infoDownloadTaksModel;
            return infoDownloadTaksModel.Skip(pageNumber * pageSize).Take(pageSize).ToList();
        }

        public int getTotalTasks()
        {
            return infoDownloadTaksModel.Count();
        }

        public static void clearUploadCompleted()
        {
            uploadModels.RemoveAll(x => x.state != StateTask.Working);
        }

        public static void clearDownloadCompleted()
        {
            downloadModels.RemoveAll(x => x.state != StateTask.Working);
        }

        public static void clearTasksCompleted()
        {
            infoDownloadTaksModel.RemoveAll(x => x.state != StateTask.Working);
        }


    }
}
