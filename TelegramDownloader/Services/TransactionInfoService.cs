using TelegramDownloader.Models;

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
            downloadModels.Add(downloadModel);
            EventChanged?.Invoke(this, new EventArgs());
        }

        public void addToUploadList(UploadModel uploadModel)
        {
            uploadModels.Add(uploadModel);
            EventChanged?.Invoke(this, new EventArgs());
        }

        public void addToInfoDownloadTaskList(InfoDownloadTaksModel infoDownloadModel)
        {
            infoDownloadTaksModel.Add(infoDownloadModel);
            EventChanged?.Invoke(this, new EventArgs());
        }

        public List<DownloadModel> GetDownloadModels()
        {
            return downloadModels;
        }

        public List<UploadModel> GetUploadModels()
        {
            return uploadModels;
        }

        public List<InfoDownloadTaksModel> getInfoDownloadTaksModel()
        {
            return infoDownloadTaksModel;
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
