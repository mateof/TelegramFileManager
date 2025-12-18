using BlazorBootstrap;
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
        Task<User> GetUser();
        bool checkChannelExist(string id);
        bool checkUserLogin();
        Task deleteFile(string chatId, int idMessage);
        bool isInChat(long id);
        Task<InvitationInfo?> getInvitationHash(long id);
        Task joinChatInvitationHash(string? hash);
        Task<User> CallQrGenerator(Action<string> func, CancellationToken ct, bool logoutFirst = false);
        Task<string> DownloadFile(ChatMessages message, string fileName = null, string folder = null, DownloadModel model = null, bool shouldAddToList = false);
        Task<Byte[]> DownloadFileStream(Message message, long offset, int limit);
        Task<Stream> DownloadFileAndReturn(ChatMessages message, Stream ms = null, string fileName = null, string folder = null, DownloadModel model = null);
        Task<Stream> DownloadFileAndReturnWithOffset(ChatMessages message, Stream ms = null, string fileName = null, string folder = null, DownloadModel model = null, long offset = 0);
        Task<List<ChatViewBase>> GetFouriteChannels(bool mustRefresh = true);
        Task AddFavouriteChannel(long id);
        Task RemoveFavouriteChannel(long id);
        Task<List<ChatViewBase>> getAllChats();
        Task<List<ChatViewBase>> getAllSavedChats();
        Task<ChatsWithFolders> getChatsWithFolders();
        Task<List<ChatMessages>> getAllMessages(long id, Boolean onlyFiles = false);
        Task<GridDataProviderResult<ChatMessages>> getPaginatedMessages(long id, int page, int size, Boolean onlyFiles = false);
        Task<List<ChatMessages>> getAllMediaMessages(long id, Boolean onlyFiles = false);
        Task<List<ChatMessages>> getChatHistory(long id, int limit = 30, int addOffset = 0);
        string getChatName(long id);
        Task<Message> getMessageFile(string chatId, int idMessage);
        Task<string> getPhotoThumb(ChatBase chat);
        Task<string> downloadPhotoThumb(Photo thumb);
        Task<byte[]?> DownloadChannelPhoto(long channelId);
        Task logOff();
        Task sendVerificationCode(string vc);
        Task<Message> uploadFile(string chatId, Stream file, string fileName, string mimeType = null, UploadModel um = null, string caption = null);
        Task<List<TelegramChatDocuments>> searchAllChannelFiles(long id, int lastId);
        bool isMyChat(long id);
        Task<TL.Channel?> CreateChannel(string title, string about);
    }
}