﻿using System.Diagnostics.Tracing;
using System.Xml.Linq;
using System;
using TelegramDownloader.Data;
using TelegramDownloader.Services;
using TL;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TelegramDownloader.Models
{

    public class InfoDownloadTaksModel
    {
        public event EventHandler<InfoTaskEventArgs> EventChanged;
        public StateTask state { get; set; } = StateTask.Working;
        public string id {  get; set; }
        public bool isUpload {  get; set; }
        public List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> files { get; set; }
        public Callbacks callbacks { get; set; }
        public string fromPath { get; set; }
        public string toPath { get; set; }
        public int total { get; set; }
        public int executed { get; set; }
        public long totalSize { get; set; }
        public long executedSize { get; set; }
        public Thread thread { get; set; }
        public Syncfusion.Blazor.FileManager.FileManagerDirectoryContent? file {  get; set; }
        public int progress { get; set; }
        public List<UploadModel> currentUploads { get; set; } = new List<UploadModel>();
        public List<DownloadModel> currentDownloads { get; set; } = new List<DownloadModel>();

        public async void AddOne(long size)
        {
            if (state == StateTask.Canceled)
                cancelTask();
            
            executed += 1;
            progress = Convert.ToInt32(executed * 100 / total);
            executedSize += size;
            EventChanged?.Invoke(this, new InfoTaskEventArgs());

        }

        public async void change()
        {
            if (state == StateTask.Canceled)
                throw new Exception($"Canceled {id}");
            EventChanged?.Invoke(this, new InfoTaskEventArgs());

        }

        public void addUpload(UploadModel um)
        {
            if (state == StateTask.Canceled)
                return;
            deleteNotWorkingTasks();
            currentUploads.Add(um);
        }

        public void addDownloads(DownloadModel dm)
        {
            if (state == StateTask.Canceled)
                return;
            deleteNotWorkingTasks();
            currentDownloads.Add(dm);
        }

        public void RetryCallback()
        {
            callbacks.callback.Invoke();
        }

        public void cancelTask()
        {
            if (state == StateTask.Canceled)
            {
                foreach(DownloadModel dm in currentDownloads)
                {
                    dm.state = StateTask.Canceled;
                }
                foreach(UploadModel um in currentUploads)
                {
                    um.state = StateTask.Canceled;
                }
            }
                

        }

        private void deleteNotWorkingTasks()
        {
            currentUploads.RemoveAll(um => um.state != StateTask.Working);
            currentDownloads.RemoveAll(dm => dm.state != StateTask.Working);
        }
    }
    public class DownloadModel
    {
        public event EventHandler<DownloadEventArgs> EventChanged;
        public Guid id = Guid.NewGuid();
        public string action { get; set; } = "Download";
        public StateTask state { get; set; } = StateTask.Working;
        public string idTask { get; set; }
        public ChatMessages m {  get; set; }
        public string name { get; set; }
        public Callbacks callbacks { get; set; }

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

        public void RetryCallback()
        {
            callbacks.callback.Invoke();
        }
    }

    public class UploadModel
    {
        public event EventHandler<UploadEventArgs> EventChanged;
        public Guid id = Guid.NewGuid();
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
            EventChanged?.Invoke(this, new UploadEventArgs());
            if (transmitted == totalSize)
            {
                state = StateTask.Completed;
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
            state = newState;
            EventChanged?.Invoke(this, new UploadEventArgs());
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
            base.InvokeEvent(new UploadEventArgs());
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
        [Description("Canceled")]
        Canceled,
        [Description("Completed")]
        Completed,
        [Description("Working")]
        Working
    }
}
