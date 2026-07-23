using TL;

namespace TelegramDownloader.Models.Api
{
    /// <summary>A Telegram chat/channel visible to the signed-in account.</summary>
    public class ApiChannelDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary><c>channel</c>, <c>group</c> or <c>chat</c>.</summary>
        public string Type { get; set; } = "chat";

        /// <summary>True when the signed-in account created the channel.</summary>
        public bool IsOwner { get; set; }

        /// <summary>True when the channel is marked as favourite in the app config.</summary>
        public bool IsFavorite { get; set; }

        /// <summary>Relative URL serving the channel avatar.</summary>
        public string ImageUrl { get; set; } = string.Empty;

        /// <summary>True when the app already has an indexed file database for this channel.</summary>
        public bool HasDatabase { get; set; }

        public static ApiChannelDto FromChatViewBase(ChatViewBase chat, bool isFavorite = false, bool isOwner = false)
        {
            var id = chat.chat.ID;
            var name = chat.chat switch
            {
                Channel c => c.title,
                Chat ch => ch.title,
                _ => chat.chat?.ToString() ?? "Unknown"
            };
            var type = chat.chat switch
            {
                Channel c when c.IsChannel => "channel",
                Channel c when c.IsGroup => "group",
                Chat => "group",
                _ => "chat"
            };

            return new ApiChannelDto
            {
                Id = id,
                Name = name,
                Type = type,
                IsOwner = isOwner,
                IsFavorite = isFavorite,
                ImageUrl = $"/api/channel/image/{id}"
            };
        }
    }

    /// <summary>Channel plus indexed-content statistics.</summary>
    public class ApiChannelDetailDto : ApiChannelDto
    {
        public int FileCount { get; set; }
        public int FolderCount { get; set; }
        public long TotalSize { get; set; }
        public string TotalSizeText { get; set; } = "0 B";
        public int AudioCount { get; set; }
        public int VideoCount { get; set; }
        public int PhotoCount { get; set; }
        public int DocumentCount { get; set; }

        /// <summary>True while a background refresh of this channel is running.</summary>
        public bool IsRefreshing { get; set; }

        /// <summary>True when the account can index/refresh this channel from the UI.</summary>
        public bool CanRefresh { get; set; }
    }

    /// <summary>A Telegram chat folder (filter) with the channels it contains.</summary>
    public class ApiChannelFolderDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? IconEmoji { get; set; }
        public List<ApiChannelDto> Channels { get; set; } = new();
        public int ChannelCount => Channels.Count;
    }

    /// <summary>Channels grouped by Telegram folder.</summary>
    public class ApiChannelsWithFoldersDto
    {
        public List<ApiChannelFolderDto> Folders { get; set; } = new();
        public List<ApiChannelDto> Ungrouped { get; set; } = new();
        public int TotalChannels { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/channels</c>.</summary>
    public class CreateChannelRequest
    {
        /// <summary>Channel title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Channel description.</summary>
        public string? About { get; set; }

        /// <summary>Create the MongoDB file database for the channel right away.</summary>
        public bool CreateDatabase { get; set; } = true;
    }

    /// <summary>Body of <c>POST /api/v1/channels/{id}/refresh</c>.</summary>
    public class RefreshChannelRequest
    {
        public bool IncludeDocuments { get; set; } = true;
        public bool IncludeAudio { get; set; } = true;
        public bool IncludeVideo { get; set; } = true;
        public bool IncludePhotos { get; set; } = true;

        /// <summary>Re-scan the channel even when a previous scan already completed.</summary>
        public bool Force { get; set; }

        public RefreshChannelOptions ToOptions() => new()
        {
            IncludeDocuments = IncludeDocuments,
            IncludeAudio = IncludeAudio,
            IncludeVideo = IncludeVideo,
            IncludePhotos = IncludePhotos
        };
    }

    /// <summary>A raw Telegram message from a chat history.</summary>
    public class ApiChatMessageDto
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string? Text { get; set; }

        /// <summary>True when the message carries a document/media attachment.</summary>
        public bool HasMedia { get; set; }

        /// <summary><c>photo</c>, <c>video</c>, <c>audio</c>, <c>document</c> or null.</summary>
        public string? MediaType { get; set; }

        public string? FileName { get; set; }
        public long FileSize { get; set; }
        public string? MimeType { get; set; }

        /// <summary>Sender display name, when resolvable.</summary>
        public string? From { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/channels/{id}/leave</c> and delete operations.</summary>
    public class ChannelDeleteRequest
    {
        /// <summary>Also drop the local MongoDB database that indexes the channel.</summary>
        public bool DeleteLocalDatabase { get; set; }

        /// <summary>Delete the channel on Telegram (owner only) instead of just leaving it.</summary>
        public bool DeleteOnTelegram { get; set; }
    }
}
