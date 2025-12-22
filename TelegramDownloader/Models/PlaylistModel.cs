using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace TelegramDownloader.Models
{
    public class PlaylistModel
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public List<PlaylistTrackModel> Tracks { get; set; } = new();

        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime DateCreated { get; set; } = DateTime.Now;

        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime DateModified { get; set; } = DateTime.Now;

        [BsonIgnore]
        public int TrackCount => Tracks?.Count ?? 0;
    }

    public class PlaylistTrackModel
    {
        public string FileId { get; set; } = string.Empty;

        public string ChannelId { get; set; } = string.Empty;

        public string ChannelName { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;

        public string FileType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public int Order { get; set; }

        /// <summary>
        /// Direct URL for local files. When set, the track is a local file and this URL should be used directly.
        /// </summary>
        public string? DirectUrl { get; set; }

        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime DateAdded { get; set; } = DateTime.Now;

        /// <summary>
        /// Returns true if this is a local file (has DirectUrl set)
        /// </summary>
        [BsonIgnore]
        public bool IsLocalFile => !string.IsNullOrEmpty(DirectUrl);
    }
}
