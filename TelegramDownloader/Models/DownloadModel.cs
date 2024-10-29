using System.Diagnostics.Tracing;
using System.Xml.Linq;
using System;
using TelegramDownloader.Data;
using TelegramDownloader.Services;
using TL;
using System.ComponentModel;

namespace TelegramDownloader.Models
{

    public class InfoDownloadTaksModel
    {
        public event EventHandler<InfoTaskEventArgs> EventChanged;
        public StateTask state { get; set; } = StateTask.Working;
        public string id {  get; set; }
        public bool isUpload {  get; set; }
        public string fromPath { get; set; }
        public string toPath { get; set; }
        public int total { get; set; }
        public int executed { get; set; }
        public long totalSize { get; set; }
        public long executedSize { get; set; }
        public Thread thread { get; set; }
        public Syncfusion.Blazor.FileManager.FileManagerDirectoryContent? file {  get; set; }

        public async void AddOne(long size)
        {
            executed += 1;
            executedSize += size;
            EventChanged?.Invoke(this, new InfoTaskEventArgs(TransactionInfoService.infoDownloadTaksModel));

        }

        public async void change()
        {
            EventChanged?.Invoke(this, new InfoTaskEventArgs(TransactionInfoService.infoDownloadTaksModel));

        }
    }
    public class DownloadModel
    {
        public event EventHandler<DownloadEventArgs> EventChanged;
        public string action { get; set; } = "Download";
        public StateTask state { get; set; } = StateTask.Working;
        public string idTask { get; set; }
        public ChatMessages m {  get; set; }
        public string name { get; set; }

        public long _size { get; set; }
        public long _transmitted {  get; set; }

        public string _sizeString { get; set; }
        public string _transmittedString { get; set; }

        public IPeerInfo channel { get; set; }
        public int progress { get; set; }

        public void ProgressCallback(long transmitted, long totalSize)
        {
            if (state == StateTask.Canceled)
                throw new Exception($"Canceled {name}");
            _transmitted = transmitted;
            _sizeString = HelperService.SizeSuffix(totalSize);
            _transmittedString = HelperService.SizeSuffix(transmitted);
            progress = Convert.ToInt32(transmitted * 100 / totalSize);
            EventChanged?.Invoke(this, new DownloadEventArgs(TransactionInfoService.downloadModels));
            if (transmitted == totalSize)
            {
                state = StateTask.Completed;
                NotificationModel nm = new NotificationModel();
                nm.sendEvent(new Notification($"Download {name} completed", "Download Completed", NotificationTypes.Success));
            }

        }
    }

    public class UploadModel
    {
        public event EventHandler<UploadEventArgs> EventChanged;
        public string action { get; set; } = "Upload";
        public StateTask state { get; set; } = StateTask.Working;
        public ChatMessages m { get; set; }
        public string name { get; set; }

        public long _size { get; set; }
        public long _transmitted { get; set; }

        public string _sizeString { get; set; }
        public string _transmittedString { get; set; }

        public IPeerInfo channel { get; set; }
        public int progress { get; set; }
        public Thread thread { get; set; }

        public virtual void ProgressCallback(long transmitted, long totalSize)
        {
            if (state == StateTask.Canceled)
                throw new Exception($"Canceled {name}");
            _transmitted = transmitted;
            _sizeString = HelperService.SizeSuffix(totalSize);
            _transmittedString = HelperService.SizeSuffix(transmitted);
            progress = Convert.ToInt32(transmitted * 100 / totalSize);
            EventChanged?.Invoke(this, new UploadEventArgs(TransactionInfoService.uploadModels));
            if (transmitted == totalSize)
            {
                state = StateTask.Completed;
                NotificationModel nm = new NotificationModel();
                nm.sendEvent(new Notification($"Upload {name} completed", "Upload Completed", NotificationTypes.Success));
            }

        }

        public void InvokeEvent(UploadEventArgs uploadEventArgs)
        {
            EventChanged?.Invoke(this, uploadEventArgs);
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
            _transmitted = transmitted;
            _sizeString = HelperService.SizeSuffix(totalSize);
            _transmittedString = HelperService.SizeSuffix(transmitted);
            progress = Convert.ToInt32(transmitted * 100 / totalSize);
            base.InvokeEvent(new UploadEventArgs(TransactionInfoService.uploadModels));
            if (transmitted == totalSize)
            {
                state = StateTask.Completed;
                NotificationModel nm = new NotificationModel();
                nm.sendEvent(new Notification($"Split {name} completed", "Split Completed", NotificationTypes.Success));
            }

        }
    }

    public class NewDownloadModel
    {
        public string newName { get; set; }
        public string folder { get; set; }
    }

    public class DownloadEventArgs : EventArgs
    {
        public List<DownloadModel> models { get; set; }
        public DownloadEventArgs(List<DownloadModel> downloadModel)
        {
            models = downloadModel;
        }
        
    }

    public class InfoTaskEventArgs : EventArgs
    {
        public List<InfoDownloadTaksModel> models { get; set; }
        public InfoTaskEventArgs(List<InfoDownloadTaksModel> infoDownloadTaksModel)
        {
            models = infoDownloadTaksModel;
        }

    }

    public class UploadEventArgs : EventArgs
    {
        public List<UploadModel> models { get; set; }
        public UploadEventArgs(List<UploadModel> uploadModel)
        {
            models = uploadModel;
        }

    }

    public enum StateTask
    {
        [Description("Error")]
        Error,
        [Description("Canceled")]
        Canceled,
        [Description("Completed")]
        Completed,
        [Description("Working")]
        Working
    }
}
