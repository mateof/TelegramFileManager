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
using TelegramDownloader.Models.Persistence;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Transfers;
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
                            // Runs on its own (one part after the other); failures are logged.
                            downloadSplitFilesV2(itemFile, file, currentFilePath, channelId == null ? dbName : channelId)
                                .Observe(_logger, $"split download of {itemFile.Name}");
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
            await DownloadSplitFileAsync(dbName, file, Path.Combine(currentFilePath, itemFile.Name));
        }

        /// <summary>
        /// Downloads a split file (one Telegram message per part, appended into
        /// one destination) as a single persisted task, so a restart continues
        /// from the first part that was not finished instead of leaving a file
        /// with missing parts.
        /// </summary>
        private async Task<StateTask> DownloadSplitFileAsync(string dbName, BsonFileManagerModel file, string destPath)
        {
            _logger.LogInformation("Starting split file download V2 - FileName: {FileName}, Parts: {Parts}", file.Name, file.ListMessageId.Count);
            var task = new PersistedTaskModel
            {
                InternalId = Guid.NewGuid().ToString(),
                Type = TaskType.Download,
                State = StateTask.Pending,
                ChannelId = dbName,
                ChannelName = GetChannelName(dbName),
                MessageId = file.ListMessageId.First(),
                MessageIds = file.ListMessageId.ToList(),
                IsSplit = true,
                Name = file.Name,
                DestinationPath = destPath,
                TotalSize = file.Size,
                CreationDate = DateTime.Now,
                StartDate = DateTime.Now,
                LastUpdated = DateTime.Now
            };
            await _persistence.PersistTask(task);
            StateTask result = await RunSplitDownloadAsync(task, startPart: 0, completedBytes: 0, transmittedInPart: 0, restart: true);
            _logger.LogInformation("Split file download V2 finished - FileName: {FileName}, State: {State}", file.Name, result);
            return result;
        }

        /// <summary>
        /// Downloads parts <paramref name="startPart"/>.. of a persisted split
        /// download. The destination is first cut back to the confirmed bytes, so
        /// a resume never appends after stale data. Stops at the first part that
        /// fails or is canceled (the old loop went on and produced a file with a
        /// hole).
        /// </summary>
        public async Task<StateTask> RunSplitDownloadAsync(PersistedTaskModel task, int startPart, long completedBytes, long transmittedInPart, bool restart)
        {
            if (!_tis.TryRegisterJob(task.InternalId))
            {
                _logger.LogWarning("Split download {Name} is already running - not started twice", task.Name);
                return StateTask.Working;
            }
            try
            {
                if (restart)
                {
                    startPart = 0;
                    completedBytes = 0;
                    transmittedInPart = 0;
                    if (File.Exists(task.DestinationPath))
                        File.Delete(task.DestinationPath);
                }
                string dir = Path.GetDirectoryName(task.DestinationPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                List<int> ids = task.MessageIds;
                List<int> completed = Enumerable.Range(0, Math.Min(startPart, ids.Count)).ToList();
                for (int i = startPart; i < ids.Count; i++)
                {
                    int messageId = ids[i];
                    long baseBytes = completedBytes;
                    DownloadModel part = NewDownloadModel(task.ChannelId, $"({i + 1}) - {task.Name}", task.TotalSize, task.DestinationPath);
                    part.PartStartOffset = baseBytes;
                    part._transmitted = i == startPart ? transmittedInPart : 0;
                    part.OnProgressPersist = (transmitted, progress, state) =>
                        _persistence.UpdateProgress(task.InternalId, baseBytes + transmitted, Percent(baseBytes + transmitted, task.TotalSize), state)
                            .Observe(_logger, $"save progress of {task.Name}");
                    part.callbacks.callback = () => DownloadFileNowV2(task.ChannelId, messageId, task.DestinationPath, part);

                    StateTask result = await QueueAndWaitAsync(part, atFirst: true);
                    if (result != StateTask.Completed)
                    {
                        if (result == StateTask.Canceled)
                            await _persistence.Remove(task.InternalId);
                        else
                            await _persistence.MarkError(task.InternalId, $"Part {i + 1} of {ids.Count}: {part.LastError ?? "download failed"}");
                        return result;
                    }

                    completedBytes = new System.IO.FileInfo(task.DestinationPath).Length;
                    completed.Add(i);
                    await _persistence.UpdateSplitProgress(task.InternalId, i + 1, completed, completedBytes);
                }
                await _persistence.MarkCompleted(task.InternalId);
                return StateTask.Completed;
            }
            finally
            {
                _tis.UnregisterJob(task.InternalId);
            }
        }

        private static int Percent(long done, long total) => total > 0 ? (int)Math.Min(100, done * 100 / total) : 0;

        private string GetChannelName(string dbName)
        {
            try
            {
                return _ts.getChatName(Convert.ToInt64(dbName));
            }
            catch
            {
                return "Public or Shared";
            }
        }

        private DownloadModel NewDownloadModel(string dbName, string name, long size, string path)
        {
            DownloadModel model = new DownloadModel();
            model.path = path;
            model.tis = _tis;
            model.name = name;
            model._size = size;
            model._sizeString = HelperService.SizeSuffix(size);
            model._transmitted = 0;
            model.state = StateTask.Pending;
            model.callbacks = new Callbacks();
            model.channelName = GetChannelName(dbName);
            return model;
        }

        /// <summary>
        /// Keeps the persisted record of a single-file download in step with its
        /// state: removed when completed or canceled, kept with the reason when
        /// it fails (so it can be retried), progress saved debounced.
        /// </summary>
        private void AttachPersistence(DownloadModel model)
        {
            model.OnProgressPersist = (transmitted, progress, state) =>
                _persistence.UpdateProgress(model._internalId, transmitted, progress, state)
                    .Observe(_logger, $"save progress of {model.name}");
            model.EventStatechanged += (sender, args) =>
            {
                switch (model.state)
                {
                    case StateTask.Completed:
                        _persistence.MarkCompleted(model._internalId).Observe(_logger, $"complete {model.name}");
                        break;
                    case StateTask.Canceled:
                        _persistence.Remove(model._internalId).Observe(_logger, $"remove canceled {model.name}");
                        break;
                    case StateTask.Error:
                        _persistence.MarkError(model._internalId, model.LastError ?? "Download failed").Observe(_logger, $"mark {model.name} as failed");
                        break;
                }
            };
        }

        /// <summary>
        /// Queues <paramref name="model"/> and waits until it reaches a final
        /// state. "Completed" is only reported once the run has ended, i.e. the
        /// file stream is closed and the next part can be appended safely.
        /// </summary>
        private Task<StateTask> QueueAndWaitAsync(DownloadModel model, bool atFirst)
        {
            var tcs = new TaskCompletionSource<StateTask>(TaskCreationOptions.RunContinuationsAsynchronously);
            model.RunFinished += (s, e) =>
            {
                if (model.state == StateTask.Completed || model.state == StateTask.Error || model.state == StateTask.Canceled)
                    tcs.TrySetResult(model.state);
            };
            // A download canceled while still queued never runs.
            model.EventStatechanged += (s, e) =>
            {
                if (model.state == StateTask.Canceled && !model.IsRunning)
                    tcs.TrySetResult(StateTask.Canceled);
            };
            _tis.addToPendingDownloadList(model, atFirst: atFirst);
            return tcs.Task;
        }

        public async Task downloadFromTelegramV2(string dbName, int messageId, string destPath, BsonFileManagerModel file = null, bool shouldWait = false, string path = null, Int32 part = 0)
        {
            _logger.LogDebug("Queueing download V2 - DbName: {DbName}, MessageId: {MessageId}, Part: {Part}", dbName, messageId, part);
            string name = file == null ? null : (part == 0 ? "" : $"({part.ToString()}) - ") + file.Name;
            DownloadModel model = NewDownloadModel(dbName, name, file?.Size ?? 0, path ?? destPath);

            // Setup persistence
            AttachPersistence(model);
            try
            {
                await _persistence.PersistDownload(model, dbName, messageId, file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist download task - continuing without persistence");
            }

            model.callbacks.callback = () => DownloadFileNowV2(dbName, messageId, destPath, model);
            if (shouldWait)
                await QueueAndWaitAsync(model, atFirst: true);
            else
                _tis.addToPendingDownloadList(model, atFirst: false);
        }

        /// <summary>
        /// Queues a persisted single-file download again (startup resume or the
        /// "Resume" button). It keeps the persisted id, so progress and the final
        /// state update the same record, and continues from the confirmed bytes.
        /// </summary>
        public DownloadModel QueueResumedDownload(PersistedTaskModel task, bool atFirst)
        {
            int messageId = task.MessageId ?? task.MessageIds?.FirstOrDefault() ?? 0;
            DownloadModel model = NewDownloadModel(task.ChannelId, task.Name, task.TotalSize, task.DestinationPath);
            model._internalId = task.InternalId;
            model.creationDate = task.CreationDate;
            model._transmitted = task.TransmittedBytes;
            model._transmittedString = HelperService.SizeSuffix(task.TransmittedBytes);
            model.progress = Percent(task.TransmittedBytes, task.TotalSize);
            model.state = StateTask.Pending;
            AttachPersistence(model);
            model.callbacks.callback = () => DownloadFileNowV2(task.ChannelId, messageId, task.DestinationPath, model);
            _tis.addToPendingDownloadList(model, atFirst: atFirst);
            return model;
        }

        /// <summary>
        /// Downloads one Telegram file into <paramref name="destPath"/>, continuing
        /// from the bytes already confirmed for <paramref name="model"/> (a resume,
        /// a retry after an error, or a pause) or from zero. For a split part the
        /// bytes of the previous parts (<see cref="DownloadModel.PartStartOffset"/>)
        /// are kept. Anything past the safe offset is cut off first, so stale or
        /// unconfirmed data is never left in the middle of the file (the old code
        /// appended to whatever was there, corrupting retried and paused files).
        /// </summary>
        public async Task DownloadFileNowV2(string dbName, int messageId, string destPath, DownloadModel model)
        {
            TL.Message m;
            try
            {
                m = await _ts.getMessageFile(dbName, messageId);
            }
            catch (KeyNotFoundException)
            {
                throw new InvalidOperationException($"The channel {dbName} is not available to this account any more");
            }
            if (m == null)
                throw new InvalidOperationException($"The Telegram message {messageId} no longer exists in the channel");

            ChatMessages cm = new ChatMessages();
            cm.message = m;
            model.startDate = DateTime.Now;
            cm.user = null;
            cm.isDocument = false;
            long size = 0;
            if (m.media is MessageMediaDocument { document: Document document })
            {
                cm.isDocument = true;
                size = document.size;
            }

            long partStart = model.PartStartOffset;
            long onDisk = File.Exists(destPath) ? new System.IO.FileInfo(destPath).Length : 0;
            if (onDisk < partStart)
                throw new InvalidOperationException("The partial file is shorter than the parts already downloaded; restart the download");

            long offset = size > 0
                ? TransferResumePlanner.ComputeDownloadOffset(onDisk - partStart, model._transmitted, size, ResumeSafetyMargin())
                : 0;

            using (FileStream fs = new FileStream(destPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            {
                if (size > 0 && offset >= size)
                {
                    // Already complete on disk: the run ends as completed.
                    fs.SetLength(partStart + size);
                    _logger.LogInformation("Download {Name} was already complete on disk", model.name);
                    return;
                }
                fs.SetLength(partStart + offset);
                fs.Position = partStart + offset;
                model._transmitted = offset;
                if (offset > 0)
                    _logger.LogInformation("Continuing download {Name} from byte {Offset} of {Size}", model.name, offset, size);
                await _ts.DownloadFileAndReturnWithOffset(cm, ms: fs, model: model, offset: offset);
            }
        }

        /// <summary>
        /// Bytes re-requested before the confirmed offset on a resume: with
        /// parallel transfers the last chunks can complete out of order, so the
        /// tail of the reported progress is not guaranteed to be contiguous.
        /// </summary>
        private static long ResumeSafetyMargin()
        {
            int parallel = Math.Max(1, GeneralConfigStatic.config?.ParallelTransfers ?? 4);
            return parallel * 1024L * 1024L;
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
            await DownloadSplitFileAsync(channelId, file, destPath);
        }

    }
}
