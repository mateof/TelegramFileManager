using BlazorBootstrap;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.StaticFiles;
using MongoDB.Driver;
using Newtonsoft.Json;
using Syncfusion.Blazor.FileManager;
using Syncfusion.Blazor.Inputs;

using Syncfusion.EJ2.FileManager.PhysicalFileProvider;
using Syncfusion.EJ2.Linq;
using System.Dynamic;
using System.IO.Compression;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Services;
using TL;

namespace TelegramDownloader.Data
{
    public class FileServiceV2 : FileService
    {
        public FileServiceV2(
        ITelegramService ts,
        IDbService db,
        ILogger<IFileService> logger,
        TransactionInfoService tis,
        ToastService toastService
    ) : base(ts, db, logger, tis, toastService)
        {
           
        }

        public override async Task downloadFile(string dbName, List<FileManagerDirectoryContent> files, string targetPath, string? collectionId = null, string? channelId = null)
        {
            NotificationModel nm = new NotificationModel();
            try
            {
                foreach (FileManagerDirectoryContent file in files)
                {
                    _logger.LogInformation($"Download files to {targetPath} :: {file.Name}");
                }

                nm.sendEvent(new Notification("Download Start", "Download", NotificationTypes.Info));
                string currentTargetPath = targetPath == null ? "/" : targetPath;
                // if (targetPath == null) targetPath = "/"; //path.Replace("\\", "/");
                foreach (var itemFile in files)
                {
                    var filterPath = itemFile.FilterPath == "Files/" ? "/" : itemFile.FilterPath;
                    BsonFileManagerModel file = null;
                    if (itemFile.Id != null)
                        file = collectionId == null ? await _db.getFileById(dbName, itemFile.Id) : await _db.getFileById(dbName, itemFile.Id, collectionId);
                    else
                        file = collectionId == null ? _db.getFileByPathSync(dbName, filterPath.Replace("\\", "/") + itemFile.Name) : _db.getFileByPathSync(dbName, filterPath.Replace("\\", "/") + itemFile.Name, collectionId);
                    string currentFilePath = currentTargetPath;
                    if (!itemFile.IsFile)
                    {
                        Directory.CreateDirectory(System.IO.Path.Combine(currentFilePath, itemFile.Name));
                        var filesInDir = collectionId == null ? await _db.getAllFilesInDirectoryPath(dbName, itemFile.FilterPath == "" ? "/" : itemFile.FilterPath + itemFile.Name + "/") : await _db.getAllFilesInDirectoryPath(dbName, itemFile.FilterPath == "" ? "/" : itemFile.FilterPath + itemFile.Name + "/", collectionId);
                        if (filesInDir.Count() > 0)
                        {
                            await downloadFile(dbName, filesInDir.Select(x => x.toFileManagerContent()).ToList(), System.IO.Path.Combine(currentTargetPath, itemFile.Name).Replace("\\", "/"), collectionId, channelId);
                        }

                    }
                    else
                    {
                        Directory.CreateDirectory(currentFilePath);
                        if (file == null)
                            throw new Exception($"{currentTargetPath} does not exist");
                        if (file.isSplit)
                        {
                            downloadSplitFilesV2(itemFile, file, currentFilePath, channelId == null ? dbName : channelId);
                        }
                        else
                        {
                            await downloadFromTelegramV2(channelId == null ? dbName : channelId, (int)file.MessageId, System.IO.Path.Combine(currentFilePath, itemFile.Name), file);
                        }
                    }


                }

                nm.sendEvent(new Notification("downloads have been completed", "Download completed", NotificationTypes.Success));

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on downloadFile");
                nm.sendEvent(new Notification("Error at download.", "Download Error", NotificationTypes.Error));
                throw ex;
            }

        }

        public async Task downloadSplitFilesV2(FileManagerDirectoryContent itemFile, BsonFileManagerModel file, string currentFilePath, string dbName)
        {
            int i = 1;
            string filePathPart = Path.Combine(currentFilePath, itemFile.Name);
            if (File.Exists(filePathPart))
            {
                File.Delete(filePathPart);
            }
            foreach (int messageId in file.ListMessageId)
            {
                await downloadFromTelegramV2(dbName, messageId, filePathPart, file, true, Path.Combine(currentFilePath, itemFile.Name), i);
                i++;
            }
        }

        public async Task downloadFromTelegramV2(string dbName, int messageId, string destPath, BsonFileManagerModel file = null, bool shouldWait = false, string path = null, Int32 part = 0)
        {
            DownloadModel model = new DownloadModel();
            model.path = path ?? destPath;
            model.tis = _tis;
            if (file != null)
            {
                model.name = (part == 0 ? "" : $"({part.ToString()}) - ") + file.Name;
                model._size = file.Size;
            }
            model._transmitted = 0;
            model.callbacks = new Callbacks();
            try
            {
                model.channelName = _ts.getChatName(Convert.ToInt64(dbName));
            }
            catch (Exception ex)
            {
                model.channelName = "Public or Shared";
            }
            var tcs = new TaskCompletionSource<bool>();

            EventHandler handler = null;
            handler = (sender, args) =>
            {
                if (model.state == StateTask.Completed)
                {
                    model.EventStatechanged -= handler;
                    tcs.SetResult(true);
                }
            };

            model.EventStatechanged += handler;
            model.callbacks.callback = async () => await DownloadFileNowV2(dbName, messageId, destPath, model);
            _tis.addToPendingDownloadList(model, atFirst: shouldWait);
            // Espera hasta que el estado sea "Completed"
            if (shouldWait)
                await tcs.Task;
        }

        public async Task DownloadFileNowV2(string dbName, int messageId, string destPath, DownloadModel model)
        {
            TL.Message m = await _ts.getMessageFile(dbName, messageId);
            ChatMessages cm = new ChatMessages();
            cm.message = m;

            cm.user = null;
            cm.isDocument = false;
            if (m.media is MessageMediaDocument { document: Document document })
            {
                cm.isDocument = true;
            }
            using (FileStream fs = new FileStream(destPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                await _ts.DownloadFileAndReturn(cm, ms: fs, model: model);
            }
                
        }

    }
}
