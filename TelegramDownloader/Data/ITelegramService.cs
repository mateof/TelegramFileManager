#nullable disable
using TelegramDownloader.Models;
using TL;
using WTelegram;

namespace TelegramDownloader.Data
{
    public interface ITelegramService
    {
        bool IsConfigured { get; }
        void InitializeClient();
        Task<string> checkAuth(string number, bool isPhone = false);
        Task<bool> TryRestoreSessionAsync();
        Task<User> GetUser();
        bool checkChannelExist(string id);
        bool checkUserLogin();
        Task deleteFile(string chatId, int idMessage);
        /// <summary>Deletes several messages of a chat in one call (at most 100 ids, Telegram's limit).</summary>
        Task deleteFiles(string chatId, IReadOnlyList<int> idMessages);
        bool isInChat(long id);
        Task<InvitationInfo?> getInvitationHash(long id);
        Task joinChatInvitationHash(string? hash);
        Task<User> CallQrGenerator(Action<string> func, CancellationToken ct, bool logoutFirst = false);
        void ProvideQrLoginPassword(string password);
        Task<string> DownloadFile(ChatMessages message, string fileName = null, string folder = null, DownloadModel model = null, bool shouldAddToList = false);
        Task<Byte[]> DownloadFileStream(Message message, long offset, int limit);
        IAsyncEnumerable<byte[]> DownloadFileStreamChunks(Message message, long offset, long limit, CancellationToken ct = default);
        Task<Stream> DownloadFileAndReturn(ChatMessages message, Stream ms = null, string fileName = null, string folder = null, DownloadModel model = null);
        Task<Stream> DownloadFileAndReturnWithOffset(ChatMessages message, Stream ms = null, string fileName = null, string folder = null, DownloadModel model = null, long offset = 0);
        Task<List<ChatViewBase>> GetFouriteChannels(bool mustRefresh = true);
        Task AddFavouriteChannel(long id);
        Task RemoveFavouriteChannel(long id);
        Task AddHiddenChannel(long id);
        Task RemoveHiddenChannel(long id);
        Task<List<ChatViewBase>> GetHiddenChannels();
        Task<List<ChatViewBase>> getAllChats();
        Task<List<ChatViewBase>> getAllSavedChats();
        Task<ChatsWithFolders> getChatsWithFolders();
        Task PreloadChannelsAsync();
        Task<List<ChatMessages>> getAllMessages(long id, Boolean onlyFiles = false);
        Task<PagedResult<ChatMessages>> getPaginatedMessages(long id, int page, int size, Boolean onlyFiles = false);
        Task<List<ChatMessages>> getAllMediaMessages(long id, Boolean onlyFiles = false);
        Task<List<ChatMessages>> getChatHistory(long id, int limit = 30, int addOffset = 0);
        /// <summary>A window of the history of a chat: its end, or before, after or around a message.</summary>
        Task<ChatMessagesPage> getChatWindow(long id, ChatWindow window);
        /// <summary>Messages of a chat that match a text and/or a kind, newest first, older than <paramref name="offsetId"/> (0: from the newest).</summary>
        Task<ChatMessagesPage> searchChatMessages(long id, string query, ChatMessageKind? kind, int offsetId, int limit);
        /// <summary>How many messages of each kind a chat has.</summary>
        Task<IReadOnlyDictionary<ChatMessageKind, int>> countChatMessages(long id);
        /// <summary>The messages with these ids that still exist (several requests above 100).</summary>
        Task<IReadOnlyList<Message>> getMessagesById(long id, IReadOnlyList<int> ids);
        string getChatName(long id);
        Task<Message> getMessageFile(string chatId, int idMessage);
        Task<string> getPhotoThumb(ChatBase chat);
        Task<string> downloadPhotoThumb(Photo thumb);
        /// <summary>JPEG preview of a photo message (a small size) or of a video/document message (its embedded thumbnail); null when it has none.</summary>
        Task<byte[]?> downloadMessageThumb(Message message);
        Task<byte[]?> DownloadChannelPhoto(long channelId);
        Task logOff();
        Task sendVerificationCode(string vc);
        Task<Message> uploadFile(string chatId, Stream file, string fileName, string mimeType = null, UploadModel um = null, string caption = null);
        Task<List<TelegramChatDocuments>> searchAllChannelFiles(long id, int lastId, Models.RefreshChannelOptions? options = null);
        /// <summary>One media filter of a channel refresh: messages newer than <paramref name="floor"/> (all when 0), newest first; cancellable between pages.</summary>
        Task<IReadOnlyList<TelegramChatDocuments>> searchChannelFilter(long id, Models.RefreshFilter filter, int floor, Models.RefreshFilterProgress progress, CancellationToken cancellationToken);
        bool isMyChat(long id);
        bool isChannelOwner(long id);
        Task<TL.Channel?> CreateChannel(string title, string about);
        Task LeaveChannel(long id);
        Task DeleteChannel(long id);
        (string? name, bool exists) GetChannelInfo(long id);
    }
}