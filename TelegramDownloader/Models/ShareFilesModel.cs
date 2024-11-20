using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;

namespace TelegramDownloader.Models
{
    [Serializable]
    public class ShareFilesModel
    {
        public string id {  get; set; }
        [MaxLength(40)]
        public string name { get; set; }
        public string chatName { get; set; }
        public string fileName { get; set; }
        public string description { get; set; }
        public InvitationInfo? invitation { get; set; }
        public List<BsonFileManagerModel> files { get; set; }
    }

    public class BsonSharedInfoModel
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        public string ChannelId { get; set; }
        public string CollectionId { get; set; }

        [BsonRepresentation(BsonType.Document)]
        public DateTime DateCreated { get; set; } = DateTime.Now;
        [BsonRepresentation(BsonType.Document)]
        public DateTime DateModified { get; set; } = DateTime.Now;
        public string Name { get; set; }
        public string Description { get; set; }

    }

    public class InvitationInfo
    {
        public string? invitationHash { get; set; }
        public string? invitationLink { get; set; }

        public InvitationInfo() { }
        public InvitationInfo(string? invitationHash, string? invitationLink)
        {
            this.invitationHash = invitationHash;
            this.invitationLink = invitationLink;
        }
    }
}
