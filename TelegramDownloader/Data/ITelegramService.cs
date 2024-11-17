using TelegramDownloader.Models;
using TL;

namespace TelegramDownloader.Data
{
    public interface ITelegramService
    {
        Task<string> checkAuth(string number, bool isPhone = false);
        Task<User> GetUser();
        bool checkChannelExist(string id);
        bool checkUserLogin();
        Task deleteFile(string chatId, int idMessage);
        Task<string> DownloadFile(ChatMessages message, string fileName = null, string folder = null, DownloadModel model = null);
        Task<Stream> DownloadFileAndReturn(ChatMessages message, Stream ms = null, string fileName = null, string folder = null, DownloadModel model = null);
        Task<List<ChatViewBase>> GetFouriteChannels(bool mustRefresh = true);
        Task AddFavouriteChannel(long id);
        Task RemoveFavouriteChannel(long id);
        Task<List<ChatViewBase>> getAllChats();
        Task<List<ChatViewBase>> getAllSavedChats();
        Task<List<ChatMessages>> getChatHistory(long id, int limit = 30, int addOffset = 0);
        string getChatName(long id);
        Task<Message> getMessageFile(string chatId, int idMessage);
        Task<string> getPhotoThumb(ChatBase chat);
        Task logOff();
        Task sendVerificationCode(string vc);
        Task<Message> uploadFile(string chatId, Stream file, string fileName, string mimeType = null, UploadModel um = null);

    }
}