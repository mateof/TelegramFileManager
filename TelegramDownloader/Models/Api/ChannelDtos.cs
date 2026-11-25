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

        /// <summary>True when the channel is hidden from the channel lists in the app config.</summary>
        public bool IsHidden { get; set; }

        /// <summary>Relative URL serving the channel avatar.</summary>
        public string ImageUrl { get; set; } = string.Empty;

        /// <summary>True when the app already has an indexed file database for this channel.</summary>
        public bool HasDatabase { get; set; }

        public static ApiChannelDto FromChatViewBase(ChatViewBase chat, bool isFavorite = false, bool isOwner = false, bool isHidden = false)
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
                IsHidden = isHidden,
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

    /// <summary>What the last finished index refresh added, per media type.</summary>
    public class ApiChannelRefreshResultDto
    {
        public string ChannelId { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }

        /// <summary>The scan threw; the counts are what it managed before failing.</summary>
        public bool Failed { get; set; }

        /// <summary>The refresh was canceled; the counts are what was indexed before it stopped.</summary>
        public bool Canceled { get; set; }

        public int Photos { get; set; }
        public int Videos { get; set; }
        public int Audios { get; set; }
        public int Documents { get; set; }
        public int Total { get; set; }

        /// <summary>Why it failed (the first error), when it did.</summary>
        public string? Error { get; set; }

        public static ApiChannelRefreshResultDto From(ChannelRefreshResult r) => new ApiChannelRefreshResultDto
        {
            ChannelId = r.ChannelId,
            StartedAt = r.StartedAt,
            FinishedAt = r.FinishedAt,
            Failed = r.Failed,
            Canceled = r.Canceled,
            Photos = r.Photos,
            Videos = r.Videos,
            Audios = r.Audios,
            Documents = r.Documents,
            Total = r.Total,
            Error = r.Error
        };
    }

    /// <summary>Live counters of one media filter of a running refresh.</summary>
    public class ApiChannelRefreshFilterProgressDto
    {
        /// <summary>documents, audio, video or photos.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Messages read from Telegram so far.</summary>
        public int Fetched { get; set; }

        /// <summary>
        /// Messages of this type in the whole channel as Telegram reports it;
        /// null until the first page arrives. An incremental scan stops at the
        /// last indexed message, so it usually ends well below this.
        /// </summary>
        public int? Total { get; set; }

        /// <summary>The search of this type is over.</summary>
        public bool Done { get; set; }

        /// <summary>It has not begun: with one type at a time, it waits for the previous one.</summary>
        public bool Pending { get; set; }

        /// <summary>Telegram asked to wait (FLOOD_WAIT): the search goes on by itself at this time (UTC). Null otherwise.</summary>
        public DateTime? WaitUntil { get; set; }
    }

    /// <summary>Live progress of a running channel refresh.</summary>
    public class ApiChannelRefreshProgressDto
    {
        /// <summary>Always true (the endpoint answers <c>data: null</c> when nothing runs).</summary>
        public bool Running { get; set; } = true;

        /// <summary>searching (Telegram), indexing (writing the new files) or done.</summary>
        public string Phase { get; set; } = string.Empty;

        public DateTime StartedAt { get; set; }

        public List<ApiChannelRefreshFilterProgressDto> Filters { get; set; } = new();

        /// <summary>Files found that were not indexed yet (known once the search is over).</summary>
        public int NewFiles { get; set; }

        /// <summary>New files written to the index so far.</summary>
        public int Indexed { get; set; }

        /// <summary>A cancel was requested; the refresh stops after the page or batch in flight.</summary>
        public bool CancelRequested { get; set; }

        public static ApiChannelRefreshProgressDto From(ChannelRefreshProgressSnapshot s) => new ApiChannelRefreshProgressDto
        {
            Running = true,
            Phase = s.Phase.ToKey(),
            StartedAt = s.StartedAt,
            Filters = s.Filters.Select(f => new ApiChannelRefreshFilterProgressDto
            {
                Type = f.Filter.ToKey(),
                Fetched = f.Fetched,
                Total = f.Total,
                Done = f.Done,
                Pending = !f.Started && !f.Done,
                WaitUntil = f.WaitUntil
            }).ToList(),
            NewFiles = s.NewFiles,
            Indexed = s.Indexed,
            CancelRequested = s.CancelRequested
        };
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

        /// <summary>One media type after another instead of all at once; omit it to use the setting <c>refreshOneTypeAtATime</c>.</summary>
        public bool? OneTypeAtATime { get; set; }

        public RefreshChannelOptions ToOptions() => new()
        {
            IncludeDocuments = IncludeDocuments,
            IncludeAudio = IncludeAudio,
            IncludeVideo = IncludeVideo,
            IncludePhotos = IncludePhotos,
            OneTypeAtATime = OneTypeAtATime
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

        /// <summary>Length of an audio or a video, in seconds.</summary>
        public int? Duration { get; set; }

        /// <summary>Title of a song, from its tags.</summary>
        public string? Title { get; set; }

        /// <summary>Artist of a song, from its tags.</summary>
        public string? Performer { get; set; }

        public static ApiChatMessageDto FromMessage(ChatMessages m)
        {
            var dto = new ApiChatMessageDto
            {
                Id = m.message.ID,
                Date = m.message.Date,
                Text = m.message.message,
                From = m.user?.ToString()
            };

            switch (m.message.media)
            {
                case TL.MessageMediaPhoto photo:
                    dto.HasMedia = true;
                    dto.MediaType = "photo";
                    dto.FileSize = (photo.photo as TL.Photo)?.LargestPhotoSize?.FileSize ?? 0;
                    break;
                case TL.MessageMediaDocument { document: TL.Document doc }:
                    dto.HasMedia = true;
                    dto.FileName = doc.Filename;
                    dto.FileSize = doc.size;
                    dto.MimeType = doc.mime_type;
                    dto.MediaType = doc.mime_type switch
                    {
                        not null when doc.mime_type.StartsWith("video") => "video",
                        not null when doc.mime_type.StartsWith("audio") => "audio",
                        not null when doc.mime_type.StartsWith("image") => "photo",
                        _ => "document"
                    };
                    foreach (var attribute in doc.attributes ?? Array.Empty<TL.DocumentAttribute>())
                    {
                        if (attribute is TL.DocumentAttributeAudio audio)
                        {
                            dto.Duration = audio.duration;
                            dto.Title = string.IsNullOrWhiteSpace(audio.title) ? null : audio.title;
                            dto.Performer = string.IsNullOrWhiteSpace(audio.performer) ? null : audio.performer;
                        }
                        else if (attribute is TL.DocumentAttributeVideo video)
                        {
                            dto.Duration = (int)Math.Round(video.duration);
                        }
                    }
                    break;
            }

            return dto;
        }
    }

    /// <summary>A window of the history of a chat.</summary>
    public class ApiChatWindowDto
    {
        /// <summary>Newest first.</summary>
        public List<ApiChatMessageDto> Items { get; set; } = new();

        /// <summary>Older messages may exist: ask for <c>before=oldestId</c>.</summary>
        public bool HasOlder { get; set; }

        /// <summary>Newer messages may exist: ask for <c>after=newestId</c>.</summary>
        public bool HasNewer { get; set; }

        /// <summary>Ends of the window, counting messages that are not listed (a member joined...). Null when it is empty.</summary>
        public int? OldestId { get; set; }
        public int? NewestId { get; set; }

        public static ApiChatWindowDto From(ChatWindow window, ChatMessagesPage page)
        {
            var (older, newer) = window.More(page.Ids);
            return new ApiChatWindowDto
            {
                Items = page.Messages.Select(ApiChatMessageDto.FromMessage).ToList(),
                HasOlder = older,
                HasNewer = newer,
                OldestId = page.Ids.Count == 0 ? null : page.Ids.Min(),
                NewestId = page.Ids.Count == 0 ? null : page.Ids.Max()
            };
        }
    }

    /// <summary>One page of a search in a chat.</summary>
    public class ApiChatSearchDto
    {
        /// <summary>Newest first.</summary>
        public List<ApiChatMessageDto> Items { get; set; } = new();

        /// <summary>Matches in the whole chat, as Telegram reports it.</summary>
        public int Total { get; set; }

        /// <summary><c>offsetId</c> of the next (older) page; null when there is none.</summary>
        public int? NextOffsetId { get; set; }

        public static ApiChatSearchDto From(ChatMessagesPage page, int offsetId)
        {
            // The first page may already hold every match; later ones end with an empty page.
            var last = page.Ids.Count == 0 || (offsetId == 0 && page.Ids.Count >= page.Total);
            return new ApiChatSearchDto
            {
                Items = page.Messages.Select(ApiChatMessageDto.FromMessage).ToList(),
                Total = page.Total,
                NextOffsetId = last ? null : page.Ids.Min()
            };
        }
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
