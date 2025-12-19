#nullable disable
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

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

        // Timestamps
        public DateTime CreationDate { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime LastUpdated { get; set; }

        // Error tracking
        public int RetryCount { get; set; }
        public string LastError { get; set; }
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
            var persisted = new PersistedTaskModel
            {
                InternalId = idt._internalId,
                Type = idt.isUpload ? TaskType.BatchUpload : TaskType.BatchDownload,
                State = idt.state,
                ChannelId = idt.channelId,
                IsUpload = idt.isUpload,
                FromPath = idt.fromPath,
                ToPath = idt.toPath,
                Total = idt.total,
                Executed = idt.executed,
                TotalBatchSize = idt.totalSize,
                ExecutedBatchSize = idt.executedSize,
                Progress = idt.progress,
                CreationDate = idt.creationDate,
                LastUpdated = DateTime.Now,
                ChildTasks = new List<PersistedTaskModel>()
            };

            // Persist child downloads
            foreach (var dm in idt.currentDownloads)
            {
                persisted.ChildTasks.Add(new PersistedTaskModel
                {
                    InternalId = dm._internalId,
                    Type = TaskType.Download,
                    State = dm.state,
                    Name = dm.name,
                    DestinationPath = dm.path,
                    TotalSize = dm._size,
                    TransmittedBytes = dm._transmitted,
                    Progress = dm.progress
                });
            }

            // Persist child uploads
            foreach (var um in idt.currentUploads)
            {
                persisted.ChildTasks.Add(new PersistedTaskModel
                {
                    InternalId = um._internalId,
                    Type = TaskType.Upload,
                    State = um.state,
                    Name = um.name,
                    DestinationPath = um.path,
                    TotalSize = um._size,
                    TransmittedBytes = um._transmitted,
                    Progress = um.progress
                });
            }

            return persisted;
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
