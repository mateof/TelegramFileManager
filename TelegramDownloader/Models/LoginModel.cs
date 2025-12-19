#nullable disable
using TL;

namespace TelegramDownloader.Models
{
    public class LoginModel
    {
        public string type {  get; set; }
        public string value { get; set; }
    }

    public class UserData
    {
        public UserData(string phone) {
            this.phone = phone;
        }

        public string phone { get; set; }
    }

    public class ChatViewBase
    {
        public ChatBase chat { get; set; }
        public string img64 { get; set; }

    }

    public class ChatMessages
    {
        public Message message { get; set; }
        public string htmlMessage { get; set; }
        public IPeerInfo user { get; set; }
        public string videoThumb { get; set; }
        public bool isDocument {  get; set; }
    }

    public class TelegramChatDocuments
    {
        public int id { get; set; }
        public string name { get; set; }
        public string extension { get; set; }
        public DateTime creationDate { get; set; }
        public DateTime modifiedDate { get; set; }
        public long fileSize { get; set; }
        public DocumentType documentType { get; set; }
    }

    public enum DocumentType
    {
        Photo,
        Video,
        Audio,
        Document
    }

    /// <summary>
    /// Represents a Telegram chat folder (filter)
    /// </summary>
    public class ChatFolderView
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string IconEmoji { get; set; }
        public List<ChatViewBase> Chats { get; set; } = new List<ChatViewBase>();
        public bool IsExpanded { get; set; } = false;
    }

    /// <summary>
    /// Container for organized chats with folders
    /// </summary>
    public class ChatsWithFolders
    {
        public List<ChatFolderView> Folders { get; set; } = new List<ChatFolderView>();
        public List<ChatViewBase> UngroupedChats { get; set; } = new List<ChatViewBase>();
    }
}
