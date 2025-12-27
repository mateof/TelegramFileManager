using System.ComponentModel.DataAnnotations;

namespace TelegramDownloader.Models.Mobile
{
    #region Response DTOs

    /// <summary>
    /// Playlist summary for list views
    /// </summary>
    public class PlaylistDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int TrackCount { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }

        public static PlaylistDto FromModel(PlaylistModel model)
        {
            return new PlaylistDto
            {
                Id = model.Id,
                Name = model.Name,
                Description = model.Description,
                TrackCount = model.TrackCount,
                DateCreated = model.DateCreated,
                DateModified = model.DateModified
            };
        }
    }

    /// <summary>
    /// Playlist with full track list
    /// </summary>
    public class PlaylistDetailDto : PlaylistDto
    {
        public List<TrackDto> Tracks { get; set; } = new();

        public static PlaylistDetailDto FromModel(PlaylistModel model, string baseUrl = "")
        {
            var dto = new PlaylistDetailDto
            {
                Id = model.Id,
                Name = model.Name,
                Description = model.Description,
                TrackCount = model.TrackCount,
                DateCreated = model.DateCreated,
                DateModified = model.DateModified,
                Tracks = model.Tracks?.Select((t, i) => TrackDto.FromModel(t, baseUrl)).ToList() ?? new()
            };
            return dto;
        }
    }

    /// <summary>
    /// Individual track information
    /// </summary>
    public class TrackDto
    {
        public string FileId { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public int Order { get; set; }
        public DateTime DateAdded { get; set; }
        public bool IsLocalFile { get; set; }
        public string? DirectUrl { get; set; }
        /// <summary>
        /// Generated streaming URL for this track
        /// </summary>
        public string StreamUrl { get; set; } = string.Empty;

        public static TrackDto FromModel(PlaylistTrackModel model, string baseUrl = "")
        {
            var isLocal = model.IsLocalFile;
            // Use /tfm/ endpoint for Telegram files - it handles caching and uses TFM database ID
            var streamUrl = isLocal
                ? $"{baseUrl}/api/mobile/stream/local?path={Uri.EscapeDataString(model.DirectUrl ?? "")}"
                : $"{baseUrl}/api/mobile/stream/tfm/{model.ChannelId}/{model.FileId}?fileName={Uri.EscapeDataString(model.FileName)}";

            return new TrackDto
            {
                FileId = model.FileId,
                ChannelId = model.ChannelId.ToString(),
                ChannelName = model.ChannelName,
                FileName = model.FileName,
                FilePath = model.FilePath,
                FileType = model.FileType,
                FileSize = model.FileSize,
                Order = model.Order,
                DateAdded = model.DateAdded,
                IsLocalFile = isLocal,
                DirectUrl = model.DirectUrl,
                StreamUrl = streamUrl
            };
        }
    }

    #endregion

    #region Request DTOs

    /// <summary>
    /// Request to create a new playlist
    /// </summary>
    public class CreatePlaylistRequest
    {
        [Required(ErrorMessage = "Name is required")]
        [StringLength(100, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 100 characters")]
        public string Name { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
        public string? Description { get; set; }
    }

    /// <summary>
    /// Request to update an existing playlist
    /// </summary>
    public class UpdatePlaylistRequest
    {
        [Required(ErrorMessage = "Name is required")]
        [StringLength(100, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 100 characters")]
        public string Name { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
        public string? Description { get; set; }
    }

    /// <summary>
    /// Request to add a track to a playlist
    /// </summary>
    public class AddTrackRequest
    {
        [Required(ErrorMessage = "FileId is required")]
        public string FileId { get; set; } = string.Empty;

        [Required(ErrorMessage = "ChannelId is required")]
        public string ChannelId { get; set; } = string.Empty;

        public string ChannelName { get; set; } = string.Empty;

        [Required(ErrorMessage = "FileName is required")]
        public string FileName { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;
        public string FileType { get; set; } = "audio/mpeg";
        public long FileSize { get; set; }

        /// <summary>
        /// Direct URL for local files (leave empty for Telegram files)
        /// </summary>
        public string? DirectUrl { get; set; }

        public PlaylistTrackModel ToModel()
        {
            return new PlaylistTrackModel
            {
                FileId = FileId,
                ChannelId = ChannelId,
                ChannelName = ChannelName,
                FileName = FileName,
                FilePath = FilePath,
                FileType = FileType,
                FileSize = FileSize,
                DirectUrl = DirectUrl,
                DateAdded = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Request to reorder tracks in a playlist
    /// </summary>
    public class ReorderTracksRequest
    {
        [Required(ErrorMessage = "OrderedFileIds is required")]
        [MinLength(1, ErrorMessage = "At least one FileId is required")]
        public List<string> OrderedFileIds { get; set; } = new();
    }

    #endregion
}
