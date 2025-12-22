#nullable disable
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
        ToastService toastService,
        ITaskPersistenceService persistence
    ) : base(ts, db, logger, tis, toastService, persistence)
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
            _logger.LogInformation("Starting split file download V2 - FileName: {FileName}, Parts: {Parts}", itemFile.Name, file.ListMessageId.Count);
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
            _logger.LogInformation("Split file download V2 completed - FileName: {FileName}", itemFile.Name);
        }

        public async Task downloadFromTelegramV2(string dbName, int messageId, string destPath, BsonFileManagerModel file = null, bool shouldWait = false, string path = null, Int32 part = 0)
        {
            _logger.LogDebug("Queueing download V2 - DbName: {DbName}, MessageId: {MessageId}, Part: {Part}", dbName, messageId, part);
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
            catch
            {
                model.channelName = "Public or Shared";
            }

            // Setup persistence
            model.OnProgressPersist = async (transmitted, progress, state) =>
            {
                try
                {
                    await _persistence.UpdateProgress(model._internalId, transmitted, progress, state);
                }
                catch { }
            };

            // Persist download task
            try
            {
                await _persistence.PersistDownload(model, dbName, messageId, file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist download task - continuing without persistence");
            }

            var tcs = new TaskCompletionSource<bool>();

            EventHandler handler = null;
            handler = async (sender, args) =>
            {
                if (model.state == StateTask.Completed)
                {
                    model.EventStatechanged -= handler;
                    await _persistence.MarkCompleted(model._internalId);
                    tcs.SetResult(true);
                }
                else if (model.state == StateTask.Error || model.state == StateTask.Canceled)
                {
                    model.EventStatechanged -= handler;
                    await _persistence.MarkError(model._internalId, model.state.ToString());
                    tcs.TrySetResult(false);
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
            model.startDate = DateTime.Now;

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

        /// <summary>
        /// Download file with support for resuming from a specific byte offset
        /// </summary>
        public async Task DownloadFileNowV2WithOffset(string dbName, int messageId, string destPath, DownloadModel model, long resumeOffset = 0)
        {
            TL.Message m = await _ts.getMessageFile(dbName, messageId);
            ChatMessages cm = new ChatMessages();
            cm.message = m;
            model.startDate = DateTime.Now;

            cm.user = null;
            cm.isDocument = false;
            if (m.media is MessageMediaDocument { document: Document document })
            {
                cm.isDocument = true;
                model._size = document.size;
                model._sizeString = Services.HelperService.SizeSuffix(document.size);
            }

            // Determine file mode based on resume offset
            FileMode fileMode = resumeOffset > 0 ? FileMode.Append : FileMode.Create;

            _logger.LogInformation("DownloadFileNowV2WithOffset - File: {Name}, Offset: {Offset}, Mode: {Mode}",
                model.name, resumeOffset, fileMode);

            using (FileStream fs = new FileStream(destPath, fileMode, FileAccess.Write, FileShare.ReadWrite))
            {
                // If resuming, validate file size
                if (resumeOffset > 0 && fs.Length < resumeOffset)
                {
                    // File is smaller than expected offset - restart from current file size
                    resumeOffset = fs.Length;
                    model._transmitted = resumeOffset;
                    model._transmittedString = Services.HelperService.SizeSuffix(resumeOffset);
                }

                await _ts.DownloadFileAndReturnWithOffset(cm, ms: fs, model: model, offset: resumeOffset);
            }
        }

        public override async Task<int> PreloadFilesToTemp(string channelId, List<FileManagerDirectoryContent> items)
        {
            _logger.LogInformation("Starting preload files to temp V2 - ChannelId: {ChannelId}, ItemsCount: {Count}", channelId, items.Count);
            NotificationModel nm = new NotificationModel();
            int preloadedCount = 0;
            string tempPath = System.IO.Path.Combine(TEMPDIR, "_temp");
            Directory.CreateDirectory(tempPath);

            try
            {
                nm.sendEvent(new Notification($"Starting preload of {items.Count} items to cache", "Preload", NotificationTypes.Info));

                // Collect all files to preload (including files inside folders)
                var filesToPreload = new List<BsonFileManagerModel>();
                await CollectFilesForPreloadV2(channelId, items, filesToPreload);

                _logger.LogInformation("Collected {Count} files to preload", filesToPreload.Count);
                nm.sendEvent(new Notification($"Found {filesToPreload.Count} files to preload", "Preload", NotificationTypes.Info));

                foreach (var file in filesToPreload)
                {
                    try
                    {
                        // Build the temp file name: {channelId}-{MessageId}-{fileName}
                        string tempFileName = $"{channelId}-{file.MessageId}-{file.Name}";
                        string tempFilePath = System.IO.Path.Combine(tempPath, tempFileName);

                        // Check if file already exists and is complete
                        if (File.Exists(tempFilePath))
                        {
                            var existingFile = new System.IO.FileInfo(tempFilePath);
                            if (existingFile.Length >= file.Size)
                            {
                                _logger.LogInformation("File already preloaded: {FileName}", file.Name);
                                preloadedCount++;
                                continue;
                            }
                            // File exists but incomplete, delete and re-download
                            File.Delete(tempFilePath);
                        }

                        // Download file to temp
                        _logger.LogInformation("Preloading file: {FileName} (MessageId: {MessageId})", file.Name, file.MessageId);

                        if (file.isSplit)
                        {
                            // Handle split files
                            await PreloadSplitFilesToTempV2(channelId, file, tempFilePath);
                        }
                        else
                        {
                            await downloadFromTelegramV2(channelId, (int)file.MessageId, tempFilePath, file);
                        }

                        preloadedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error preloading file: {FileName}", file.Name);
                    }
                }

                nm.sendEvent(new Notification($"Preload completed: {preloadedCount} files cached", "Preload", NotificationTypes.Success));
                _logger.LogInformation("Preload completed - FilesPreloaded: {Count}", preloadedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during preload to temp");
                nm.sendEvent(new Notification($"Error during preload: {ex.Message}", "Preload Error", NotificationTypes.Error));
            }

            return preloadedCount;
        }

        private async Task CollectFilesForPreloadV2(string channelId, List<FileManagerDirectoryContent> items, List<BsonFileManagerModel> filesToPreload)
        {
            foreach (var item in items)
            {
                if (item.IsFile)
                {
                    // Get the file details from database
                    var dbFile = await _db.getFileById(channelId, item.Id);
                    if (dbFile != null && dbFile.MessageId != null)
                    {
                        filesToPreload.Add(dbFile);
                    }
                }
                else
                {
                    // It's a folder - get all files inside recursively
                    await CollectFilesInFolderRecursiveV2(channelId, item.Id, filesToPreload);
                }
            }
        }

        private async Task CollectFilesInFolderRecursiveV2(string channelId, string folderId, List<BsonFileManagerModel> filesToPreload)
        {
            // Get all items in this folder
            var children = await _db.getFilesByParentId(channelId, folderId);

            foreach (var child in children)
            {
                if (child.IsFile)
                {
                    if (child.MessageId != null)
                    {
                        filesToPreload.Add(child);
                    }
                }
                else
                {
                    // Recurse into subfolder
                    await CollectFilesInFolderRecursiveV2(channelId, child.Id, filesToPreload);
                }
            }
        }

        private async Task PreloadSplitFilesToTempV2(string channelId, BsonFileManagerModel file, string destPath)
        {
            _logger.LogInformation("Preloading split file V2 - FileName: {FileName}, Parts: {Parts}", file.Name, file.ListMessageId?.Count ?? 0);

            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            int i = 1;
            foreach (int messageId in file.ListMessageId)
            {
                await downloadFromTelegramV2(channelId, messageId, destPath, file, true, destPath, i);
                i++;
            }
        }

    }
}
