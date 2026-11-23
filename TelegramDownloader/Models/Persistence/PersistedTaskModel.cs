#nullable disable
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using TelegramDownloader.Models.FileSystem;

namespace TelegramDownloader.Models.Persistence
{
    /// <summary>
    /// Represents a persisted task (download, upload, or batch) that can survive application restarts
    /// </summary>
    public class PersistedTaskModel
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        // Core identification
        public string InternalId { get; set; }
        public TaskType Type { get; set; }
        public StateTask State { get; set; }

        // Telegram channel/chat info for reconstruction
        public string ChannelId { get; set; }
        public string ChannelName { get; set; }
        public int? MessageId { get; set; }
        public List<int> MessageIds { get; set; }
        public bool IsSplit { get; set; }

        // File metadata
        public string Name { get; set; }
        public string DestinationPath { get; set; }
        public string SourcePath { get; set; }
        public long TotalSize { get; set; }
        public long TransmittedBytes { get; set; }
        public int Progress { get; set; }

        // Split file tracking
        public int CurrentPartIndex { get; set; }
        public List<int> CompletedPartIndices { get; set; } = new List<int>();
        /// <summary>
        /// Split downloads: bytes of the destination file that belong to the
        /// completed parts. A resume truncates the file back to this length
        /// (plus whatever of the current part is confirmed) before continuing.
        /// </summary>
        public long CompletedBytes { get; set; }

        // Upload-specific fields
        public string DbFilePath { get; set; }
        public string ParentId { get; set; }

        // Batch task fields
        public bool IsUpload { get; set; }
        public string FromPath { get; set; }
        public string ToPath { get; set; }
        public int Total { get; set; }
        public int Executed { get; set; }
        public long TotalBatchSize { get; set; }
        public long ExecutedBatchSize { get; set; }
        public List<PersistedTaskModel> ChildTasks { get; set; } = new List<PersistedTaskModel>();
        /// <summary>
        /// Batch uploads: the items the user selected (relative to the server
        /// local root). Needed to rebuild the batch after a restart; batches
        /// persisted by older versions do not have it and cannot be resumed.
        /// </summary>
        public List<PersistedSourceItem> SourceItems { get; set; } = new List<PersistedSourceItem>();
        /// <summary>
        /// Batch uploads: Telegram messages already sent for the file that was
        /// being uploaded when the batch stopped, so a resume reuses them
        /// instead of sending the same parts again.
        /// </summary>
        public PersistedUploadProgress CurrentFile { get; set; }

        // Timestamps
        public DateTime CreationDate { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime LastUpdated { get; set; }

        // Error tracking
        public int RetryCount { get; set; }
        public string LastError { get; set; }
    }

    /// <summary>
    /// One item selected for a batch upload, enough to rebuild the
    /// FsEntry the upload pipeline takes.
    /// </summary>
    public class PersistedSourceItem
    {
        public string Name { get; set; }
        public string FilterPath { get; set; }
        public bool IsFile { get; set; }
        public long Size { get; set; }
        public DateTime DateModified { get; set; }
        public string Type { get; set; }
    }

    /// <summary>How the parts of an uploaded file were produced.</summary>
    public static class UploadedPartMode
    {
        public const string Single = "single";
        public const string Memory = "memory";
        public const string Disk = "disk";
    }

    /// <summary>
    /// Parts already sent to Telegram for the file a batch upload was working
    /// on. They are only reused when the local file is unchanged (same path,
    /// size and modification time) and was split the same way.
    /// </summary>
    public class PersistedUploadProgress
    {
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public long LastWriteTicksUtc { get; set; }
        public string Mode { get; set; }
        public long ChunkSize { get; set; }
        public List<PersistedUploadedPart> Parts { get; set; } = new List<PersistedUploadedPart>();

        public bool Matches(string filePath, long fileSize, long lastWriteTicksUtc, string mode, long chunkSize)
        {
            return string.Equals(FilePath, filePath, StringComparison.Ordinal)
                && FileSize == fileSize
                && LastWriteTicksUtc == lastWriteTicksUtc
                && string.Equals(Mode, mode, StringComparison.Ordinal)
                && ChunkSize == chunkSize;
        }

        public PersistedUploadProgress Clone()
        {
            return new PersistedUploadProgress
            {
                FilePath = FilePath,
                FileSize = FileSize,
                LastWriteTicksUtc = LastWriteTicksUtc,
                Mode = Mode,
                ChunkSize = ChunkSize,
                Parts = Parts.Select(p => new PersistedUploadedPart { Index = p.Index, MessageId = p.MessageId }).ToList()
            };
        }
    }

    public class PersistedUploadedPart
    {
        public int Index { get; set; }
        public int MessageId { get; set; }
    }

    /// <summary>
    /// Type of persisted task
    /// </summary>
    public enum TaskType
    {
        Download,
        Upload,
        BatchDownload,
        BatchUpload
    }

    /// <summary>
    /// Mapper class for converting between runtime models and persisted models
    /// </summary>
    public static class TaskPersistenceMapper
    {
        /// <summary>
        /// Convert DownloadModel to PersistedTaskModel
        /// </summary>
        public static PersistedTaskModel ToPersistedTask(
            DownloadModel dm,
            string channelId,
            int messageId,
            BsonFileManagerModel fileInfo = null)
        {
            return new PersistedTaskModel
            {
                InternalId = dm._internalId,
                Type = TaskType.Download,
                State = dm.state,
                ChannelId = channelId,
                ChannelName = dm.channelName,
                MessageId = messageId,
                IsSplit = fileInfo?.isSplit ?? false,
                MessageIds = fileInfo?.ListMessageId,
                Name = dm.name,
                DestinationPath = dm.path,
                TotalSize = dm._size,
                TransmittedBytes = dm._transmitted,
                Progress = dm.progress,
                CreationDate = dm.creationDate,
                StartDate = dm.startDate != default ? dm.startDate : null,
                LastUpdated = DateTime.Now
            };
        }

        /// <summary>
        /// Convert UploadModel to PersistedTaskModel
        /// </summary>
        public static PersistedTaskModel ToPersistedTask(
            UploadModel um,
            string channelId,
            string sourcePath,
            string dbFilePath = null,
            string parentId = null)
        {
            return new PersistedTaskModel
            {
                InternalId = um._internalId,
                Type = TaskType.Upload,
                State = um.state,
                ChannelId = channelId,
                ChannelName = um.chatName,
                Name = um.name,
                DestinationPath = um.path,
                SourcePath = sourcePath,
                DbFilePath = dbFilePath,
                ParentId = parentId,
                TotalSize = um._size,
                TransmittedBytes = um._transmitted,
                Progress = um.progress,
                CreationDate = um.creationDate,
                StartDate = um.startDate != default ? um.startDate : null,
                LastUpdated = DateTime.Now
            };
        }

        /// <summary>
        /// Convert InfoDownloadTaksModel to PersistedTaskModel
        /// </summary>
        public static PersistedTaskModel ToPersistedTask(InfoDownloadTaksModel idt)
        {
            // The running children (one model per file or part) are not stored:
            // a resume rebuilds the batch from the selected items and skips what
            // is already in the destination, so they would only grow the record.
            return new PersistedTaskModel
            {
                InternalId = idt._internalId,
                Type = idt.isUpload ? TaskType.BatchUpload : TaskType.BatchDownload,
                State = idt.state,
                ChannelId = idt.channelId,
                Name = idt.DisplayName,
                IsUpload = idt.isUpload,
                FromPath = idt.fromPath,
                ToPath = idt.toPath,
                Total = idt.total,
                Executed = idt.executed,
                TotalBatchSize = idt.totalSize,
                ExecutedBatchSize = idt.executedSize,
                TotalSize = idt.totalSize,
                TransmittedBytes = idt.executedSize,
                Progress = idt.progress,
                CreationDate = idt.creationDate,
                LastUpdated = DateTime.Now,
                LastError = idt.LastError,
                SourceItems = (idt.files ?? new List<FsEntry>())
                    .Select(f => new PersistedSourceItem
                    {
                        Name = f.Name,
                        FilterPath = f.FilterPath,
                        IsFile = f.IsFile,
                        Size = f.Size,
                        DateModified = f.DateModified,
                        Type = f.Type
                    }).ToList(),
                CurrentFile = idt.GetUploadInProgress(),
                ChildTasks = new List<PersistedTaskModel>()
            };
        }

        /// <summary>Rebuilds the items of a batch upload.</summary>
        public static List<FsEntry> ToSourceContents(IEnumerable<PersistedSourceItem> items)
        {
            return (items ?? Enumerable.Empty<PersistedSourceItem>())
                .Select(i => new FsEntry
                {
                    Name = i.Name,
                    FilterPath = i.FilterPath,
                    IsFile = i.IsFile,
                    Size = i.Size,
                    DateModified = i.DateModified,
                    Type = i.Type
                }).ToList();
        }

        /// <summary>
        /// Reconstruct DownloadModel from persisted task
        /// </summary>
        public static DownloadModel ToDownloadModel(PersistedTaskModel persisted)
        {
            return new DownloadModel
            {
                _internalId = persisted.InternalId,
                name = persisted.Name,
                path = persisted.DestinationPath,
                _size = persisted.TotalSize,
                _sizeString = Services.HelperService.SizeSuffix(persisted.TotalSize),
                _transmitted = persisted.TransmittedBytes,
                _transmittedString = Services.HelperService.SizeSuffix(persisted.TransmittedBytes),
                progress = persisted.Progress,
                state = StateTask.Pending,
                channelName = persisted.ChannelName,
                creationDate = persisted.CreationDate,
                startDate = persisted.StartDate ?? DateTime.Now,
                // Persistence context
                PersistenceChannelId = persisted.ChannelId,
                PersistenceMessageId = persisted.MessageId,
                PersistenceIsSplit = persisted.IsSplit,
                PersistenceMessageIds = persisted.MessageIds,
                PersistenceCurrentPartIndex = persisted.CurrentPartIndex,
                PersistenceCompletedPartIndices = persisted.CompletedPartIndices
            };
        }

        /// <summary>
        /// Reconstruct UploadModel from persisted task
        /// </summary>
        public static UploadModel ToUploadModel(PersistedTaskModel persisted)
        {
            return new UploadModel
            {
                _internalId = persisted.InternalId,
                name = persisted.Name,
                path = persisted.DestinationPath,
                _size = persisted.TotalSize,
                _sizeString = Services.HelperService.SizeSuffix(persisted.TotalSize),
                _transmitted = 0, // Uploads restart from zero (Telegram limitation)
                _transmittedString = "0 B",
                progress = 0,
                state = StateTask.Pending,
                chatName = persisted.ChannelName,
                creationDate = persisted.CreationDate,
                startDate = persisted.StartDate ?? DateTime.Now,
                // Persistence context
                PersistenceChannelId = persisted.ChannelId,
                PersistenceSourcePath = persisted.SourcePath,
                PersistenceDbFilePath = persisted.DbFilePath,
                PersistenceParentId = persisted.ParentId
            };
        }
    }
}
