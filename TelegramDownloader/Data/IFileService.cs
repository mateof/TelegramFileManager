using Syncfusion.Blazor.FileManager;
using Syncfusion.Blazor.Inputs;
using System.Dynamic;
using TelegramDownloader.Models;

namespace TelegramDownloader.Data
{
    public interface IFileService
    {
        Task<FileManagerResponse<FileManagerDirectoryContent>> CopyItems(string dbName, ItemsMoveEventArgs<FileManagerDirectoryContent> args);
        Task CreateDatabase(string id);
        Task<List<FileManagerDirectoryContent>> createFolder(string dbName, FolderCreateEventArgs<FileManagerDirectoryContent> args);
        void cleanTempFolder();
        Task downloadFile(string dbName, List<FileManagerDirectoryContent> files, string targetPath);
        Task downloadFile(string dbName, string path, List<string> files, string targetPath);
        Task downloadFileToServer(string dbName, string path, string destPath);
        FileStream? ExistFileIntempFolder(string id);
        Task<MemoryStream> exportAllData(string dbName);
        Task<FileManagerResponse<FileManagerDirectoryContent>> GetFilesPath(string dbName, string path, List<FileManagerDirectoryContent> fileDetails = null);
        Task<MemoryStream> getImage(string dbName, string path, string fileName, MemoryStream ms = null);
        Task<BsonFileManagerModel> getItemById(string dbName, string id);
        Task<List<BsonFileManagerModel>> getTelegramFolders(string dbName, string? parentId = null);
        Task<List<ExpandoObject>> GetTelegramFoldersExpando(string id, string parentId);
        Task importData(string dbName, string path, GenericNotificationProgressModel gnp);
        Task<FileManagerResponse<FileManagerDirectoryContent>> itemDeleteAsync(string dbName, ItemsDeleteEventArgs<FileManagerDirectoryContent> args);
        Task oneItemDeleteAsync(string dbName, FileManagerDirectoryContent File);
        Task<FileManagerResponse<FileManagerDirectoryContent>> RenameFileOrFolder(string dbName, FileManagerDirectoryContent file, string newName);
        Task<FileManagerResponse<FileManagerDirectoryContent>> SearchAsync(string dbName, string path, string searchText);
        Task UploadFile(string dbName, string currentPath, UploadFiles file);
        Task UploadFileFromServer(string dbName, string currentPath, List<FileManagerDirectoryContent> files, InfoDownloadTaksModel dm = null);
        Task AddUploadFileFromServer(string dbName, string currentPath, List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> files, InfoDownloadTaksModel idt = null);
    }
}