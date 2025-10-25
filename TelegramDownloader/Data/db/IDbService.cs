using MongoDB.Driver;
using Syncfusion.Blazor.FileManager;
using TelegramDownloader.Models;

namespace TelegramDownloader.Data.db
{
    public interface IDbService
    {
        Task<IClientSessionHandle> getSession();
        Task<BsonSharedInfoModel> InsertSharedInfo(BsonSharedInfoModel sim, string dbName = DbService.SHARED_DB_NAME, string collection = "info");
        Task<List<BsonSharedInfoModel>> getSharedInfoList(string dbName = DbService.SHARED_DB_NAME, string collection = "info", string? filter = null);
        Task<BsonSharedInfoModel> getSingleFile(string id, string dbName = DbService.SHARED_DB_NAME, string collection = "info");
        Task DeleteSharedCollection(string collectionId, string dbName = DbService.SHARED_DB_NAME);
        Task DeleteSharedInfo(string id, string dbName = DbService.SHARED_DB_NAME, string collection = "info");
        Task addBytesToFolder(string dbName, string folderId, long bytes, string collectionName = "directory");
        Task checkAndSetDirectoryHasChild(string dbName, string id, string collectionName = "directory");
        Task<BsonFileManagerModel> copyItem(string dbName, string sourceId, FileManagerDirectoryContent target, string targetPath, bool isFile, string collectionName = "directory");
        Task CreateDatabase(string dbName = "default", string collection = "directory", bool CreateDefaultEntry = true);
        Task<List<BsonFileManagerModel>> createEntry(string dbName, BsonFileManagerModel file, string collectionName = "directory", IClientSessionHandle? session = null);
        Task<BsonFileManagerModel> getRootFolder(string dbName, string collectionName = "directory");
        Task createIndex(string dbName, string collectionName = "directory");
        Task deleteDatabase(string dbName = "default");
        Task deleteEntry(string dbName, string id, string collectionName = "directory");
        Task<bool> existItemByTelegramId(string dbName, int id, string collectionName = "directory");
        Task<List<BsonFileManagerModel>> getAllChildFilesInDirectory(string dbName, string path, string collectionName = "directory");
        Task<List<BsonFileManagerModel>> getAllChildFoldersInDirectory(string dbName, string parentId, string collectionName = "directory");
        Task<List<BsonFileManagerModel>> getAllDatabaseData(string dbName, string collectionName = "directory");
        Task<List<BsonFileManagerModel>> getAllFiles(string dbName, string collectionName = "directory");
        Task<List<string>> getAllFileNamesFromChannel(string dbName, string collectionName = "directory");
        Task<List<BsonFileManagerModel>> getAllFilesInDirectory(string dbName, string path, string collectionName = "directory");
        Task<List<BsonFileManagerModel>> getAllFilesInDirectoryById(string dbName, string idFolder, string collectionName = "directory");
        Task<List<BsonFileManagerModel>> getAllFilesInDirectoryPath(string dbName, string path, string collectionName = "directory");
        Task<List<BsonFileManagerModel>> getAllFilesInDirectoryPath2(string dbName, string path, string collectionName = "directory");
        Task<List<BsonFileManagerModel>> getAllFolders(string dbName, string? parentId = null, string collectionName = "directory");
        Task<List<int>> getAllIdsFromChannel(string dbName, string collectionName = "directory");
        IMongoDatabase getDatabase(string dbName);
        Task<BsonFileManagerModel> getEntry(string dbName, string filterId, string name, string collectionName = "directory");
        Task<BsonFileManagerModel> getFileById(string dbName, string id, string collectionName = "directory");
        Task<BsonFileManagerModel> getFileByPath(string dbName, string path, string collectionName = "directory");
        BsonFileManagerModel getFileByPathSync(string dbName, string path, string collectionName = "directory");
        Task<List<BsonFileManagerModel>> getShareFolder(string dbName, string bsonId, string collectionName = "directory");
        Task<BsonFileManagerModel> getParentDirectory(string dbName, string filterPath, string collectionName = "directory");
        Task<BsonFileManagerModel> getParentDirectoryByPath(string dbName, string filterPath, string collectionName = "directory");
        Task<GeneralConfig> LoadConfig();
        Task resetDatabase(string dbName = "default");
        Task resetCollection(string dbName = "default", string collection = "directory", IClientSessionHandle? session = null);
        Task SaveConfig(GeneralConfig gc);
        Task<List<BsonFileManagerModel>> Search(string dbName, string path, string searchText, string collectionName = "directory");
        Task setDirectoryHasChild(string dbName, string id, string collectionName = "directory", bool hasChild = true);
        Task subBytesToFolder(string dbName, string folderId, long bytes, string collectionName = "directory");
        Task<BsonFileManagerModel> toBasonFile(string Path, string FolderName, FileManagerDirectoryContent ParentFolder);
        void updateAllPathFiles(string dbName, string oldPath, string newPath, string collectionName = "directory");
        Task<BsonFileManagerModel> updateName(string dbName, string id, string newName, string oldName, bool isFile, string filePath, string collectionName = "directory");
    }
}