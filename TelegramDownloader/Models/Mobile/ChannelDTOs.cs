using System.ComponentModel.DataAnnotations;
using TL;

namespace TelegramDownloader.Models.Mobile
{
    #region Response DTOs

    /// <summary>
    /// Basic channel information
    /// </summary>
    public class ChannelDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public bool IsOwner { get; set; }
        public bool CanPost { get; set; }
        public bool IsFavorite { get; set; }
        public string Type { get; set; } = string.Empty; // channel, group, chat
        public int FileCount { get; set; }

        public static ChannelDto FromChatViewBase(ChatViewBase chat, bool isFavorite = false, bool isOwner = false)
        {
            var channelId = chat.chat.ID;
            var name = chat.chat switch
            {
                Channel c => c.title,
                Chat ch => ch.title,
                _ => chat.chat.ToString() ?? "Unknown"
            };

            var type = chat.chat switch
            {
                Channel c when c.IsChannel => "channel",
                Channel c when c.IsGroup => "group",
                Chat => "group",
                _ => "chat"
            };

            var canPost = chat.chat switch
            {
                Channel c => !c.IsChannel || c.flags.HasFlag(Channel.Flags.creator) || c.flags.HasFlag(Channel.Flags.has_link),
                _ => true
            };

            return new ChannelDto
            {
                Id = channelId,
                Name = name,
                ImageUrl = $"/api/channel/image/{channelId}",
                IsOwner = isOwner,
                CanPost = canPost,
                IsFavorite = isFavorite,
                Type = type
            };
        }
    }

    /// <summary>
    /// Channel with additional details and statistics
    /// </summary>
    public class ChannelDetailDto : ChannelDto
    {
        public int FileCount { get; set; }
        public long TotalSize { get; set; }
        public DateTime? LastRefreshed { get; set; }
        public int AudioCount { get; set; }
        public int VideoCount { get; set; }
        public int DocumentCount { get; set; }
    }

    /// <summary>
    /// Telegram folder containing channels
    /// </summary>
    public class ChannelFolderDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string IconEmoji { get; set; } = string.Empty;
        public int ChannelCount { get; set; }
        public List<ChannelDto> Channels { get; set; } = new();
    }

    /// <summary>
    /// All channels organized by folders
    /// </summary>
    public class ChannelsWithFoldersDto
    {
        public List<ChannelFolderDto> Folders { get; set; } = new();
        public List<ChannelDto> UngroupedChannels { get; set; } = new();
        public int TotalChannels { get; set; }
    }

    /// <summary>
    /// File/folder item in a channel
    /// </summary>
    public class ChannelFileDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Type { get; set; } = string.Empty; // file extension
        public string Category { get; set; } = string.Empty; // Audio, Video, Document, Photo, Folder
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public int? MessageId { get; set; }
        public bool IsFile { get; set; }
        public bool HasChildren { get; set; }
        public string? StreamUrl { get; set; }
        public string? DownloadUrl { get; set; }
        public string? ThumbnailUrl { get; set; }

        public static ChannelFileDto FromBsonFileManagerModel(BsonFileManagerModel model, long channelId, string baseUrl = "")
        {
            var isAudio = IsAudioFile(model.Type);
            var isVideo = IsVideoFile(model.Type);
            var category = model.IsFile
                ? (isAudio ? "Audio" : isVideo ? "Video" : GetCategory(model.Type))
                : "Folder";

            string? streamUrl = null;
            string? downloadUrl = null;

            // Use TFM ID (MongoDB ObjectId) for streaming - this endpoint checks cache before downloading
            if (model.IsFile)
            {
                if (isAudio)
                {
                    // Use new /tfm/ endpoint that uses database ID and supports caching
                    streamUrl = $"{baseUrl}/api/mobile/stream/tfm/{channelId}/{model.Id}?fileName={Uri.EscapeDataString(model.Name)}";
                }
                else if (isVideo && model.MessageId.HasValue)
                {
                    streamUrl = $"{baseUrl}/api/video/stream/{channelId}/{model.MessageId}/{Uri.EscapeDataString(model.Name)}";
                }

                // Download URL using TFM ID with cache support
                downloadUrl = $"{baseUrl}/api/file/GetFileByTfmId/{Uri.EscapeDataString(model.Name)}?idChannel={channelId}&idFile={model.Id}";
            }

            return new ChannelFileDto
            {
                Id = model.Id,
                Name = model.Name,
                Path = model.FilterPath,
                ParentId = model.FilterId,
                Size = model.Size,
                Type = model.Type,
                Category = category,
                DateCreated = model.DateCreated,
                DateModified = model.DateModified,
                MessageId = model.MessageId,
                IsFile = model.IsFile,
                HasChildren = !model.IsFile,
                StreamUrl = streamUrl,
                DownloadUrl = downloadUrl
            };
        }

        private static bool IsAudioFile(string type)
        {
            var audioExtensions = new[] { ".mp3", ".ogg", ".flac", ".aac", ".wav", ".m4a", ".wma", ".opus" };
            return audioExtensions.Contains(type?.ToLowerInvariant());
        }

        private static bool IsVideoFile(string type)
        {
            var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v" };
            return videoExtensions.Contains(type?.ToLowerInvariant());
        }

        private static string GetCategory(string type)
        {
            var ext = type?.ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) return "Document";

            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg" };
            if (imageExtensions.Contains(ext)) return "Photo";

            var documentExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt" };
            if (documentExtensions.Contains(ext)) return "Document";

            return "Document";
        }
    }

    #endregion

    #region Request DTOs

    /// <summary>
    /// Parameters for listing channel files
    /// </summary>
    public class ChannelFilesRequest
    {
        [Range(1, int.MaxValue)]
        public int Page { get; set; } = 1;

        [Range(1, 500)]
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// Filter by category: audio, video, documents, photos, all
        /// </summary>
        public string? Filter { get; set; }

        /// <summary>
        /// Sort by: name, date, size
        /// </summary>
        public string? SortBy { get; set; }

        public bool SortDescending { get; set; } = true;

        /// <summary>
        /// Folder ID for navigation within channel
        /// </summary>
        public string? FolderId { get; set; }

        /// <summary>
        /// Search text to filter files
        /// </summary>
        public string? SearchText { get; set; }
    }

    #endregion
}
