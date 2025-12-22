using MongoDB.Driver;
using MongoDB.Bson;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Persistence;
using Syncfusion.Blazor.FileManager;
using System.Text.RegularExpressions;
using MongoDB.Driver.Linq;
using System.Collections;
using Syncfusion.Blazor.PivotView;
using Microsoft.Extensions.Options;
using Syncfusion.EJ2.Layouts;

namespace TelegramDownloader.Data.db
{
    public class DbService : IDbService
    {
        private MongoClient client { get; set; }
        private IMongoDatabase currentDatabase { get; set; }
        private string dbName { get; set; }
        private readonly ILogger<DbService> _logger;

        private const string CONFIG_DB_NAME = "TCCONFIG";
        private const string TASKS_COLLECTION = "tasks";

        public const string SHARED_DB_NAME = "TFM-SHARED";

        public DbService(ILogger<DbService> logger)
        {
            _logger = logger;
            var connectionString = GeneralConfigStatic.tlconfig?.mongo_connection_string
                ?? Environment.GetEnvironmentVariable("connectionString");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                _logger.LogWarning("DbService: MongoDB connection string not configured - setup required");
                // Use a default that will fail gracefully when actually used
                connectionString = "mongodb://localhost:27017";
            }

            try
            {
                var settings = MongoClientSettings.FromConnectionString(connectionString);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
                settings.ConnectTimeout = TimeSpan.FromSeconds(5);
                client = new MongoClient(settings);
                currentDatabase = getDatabase("default");
                this.dbName = "default";
                _logger.LogInformation("DbService initialized - Connected to MongoDB");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DbService: Could not connect to MongoDB - setup may be required");
                // Create client anyway for later use after setup
                client = new MongoClient(connectionString);
                currentDatabase = client.GetDatabase("default");
                this.dbName = "default";
            }
        }

        /// <summary>
        /// Reinitializes the MongoDB connection with a new connection string.
        /// Call this after setup when the connection string has been configured.
        /// </summary>
        public void ReinitializeConnection(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                _logger.LogWarning("ReinitializeConnection: Connection string is empty");
                return;
            }

            try
            {
                _logger.LogInformation("Reinitializing MongoDB connection...");
                var settings = MongoClientSettings.FromConnectionString(connectionString);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
                settings.ConnectTimeout = TimeSpan.FromSeconds(5);

                // Create new client with updated connection string
                client = new MongoClient(settings);
                currentDatabase = getDatabase("default");
                this.dbName = "default";

                _logger.LogInformation("DbService reinitialized - Connected to MongoDB with new connection string");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reinitialize MongoDB connection: {Message}", ex.Message);
                throw;
            }
        }

        public async Task<IClientSessionHandle> getSession()
        {
            return await client.StartSessionAsync();
        }

        public IMongoDatabase getDatabase(string dbName)
        {
            // currentDatabase = client.GetDatabase(dbName);
            
            return client.GetDatabase(dbName);
        }

        public async Task createIndex(string dbName, string collectionName = "directory")
        {
            if (collectionName == null) collectionName = "directory";
            var collection = getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName);

            var indexes = await collection.Indexes.ListAsync();
            var indexList = await indexes.ToListAsync();

            bool existsFilePath = indexList.Any(i => i["name"] == "uniquefile");
            bool existsFilterPath = indexList.Any(i => i["name"] == "uniquefilterpath");

            if (!existsFilePath)
            {
                var options = new CreateIndexOptions() { Unique = true, Name = "uniquefile" };
                var indexKeysDefinition = Builders<BsonFileManagerModel>.IndexKeys.Ascending(x => x.FilePath);
                await collection.Indexes.CreateOneAsync(indexKeysDefinition, options);
            }
            if (!existsFilterPath)
            {
                var options2 = new CreateIndexOptions() { Unique = false, Name = "uniquefilterpath" };
                var indexKeysDefinition2 = Builders<BsonFileManagerModel>.IndexKeys.Ascending(x => x.FilterPath);
                await collection.Indexes.CreateOneAsync(indexKeysDefinition2, options2);
            }
        }

        public async Task CreateDatabase(string dbName = "default", string collection = "directory", bool CreateDefaultEntry = true)
        {
            _logger.LogInformation("Creating database - DbName: {DbName}, Collection: {Collection}", dbName, collection);
            if (dbName == null)
                dbName = "default";
            if (collection == null) collection = "directory";
            this.dbName = dbName;
            var db = getDatabase(dbName);
            var filter = new BsonDocument("name", collection);
            if (!(await (await db.ListCollectionsAsync(new ListCollectionsOptions { Filter = filter })).AnyAsync()))
            {
                await db.CreateCollectionAsync(collection);
                _logger.LogInformation("Collection created - DbName: {DbName}, Collection: {Collection}", dbName, collection);
            }
            await createIndex(dbName, collection);
            if (CreateDefaultEntry)
                await createDefaultEntry(dbName);
            _logger.LogInformation("Database setup completed - DbName: {DbName}", dbName);
        }

        public async Task deleteDatabase(string dbName = "default")
        {
            _logger.LogWarning("Deleting database - DbName: {DbName}", dbName);
            if (dbName == null)
                dbName = "default";
            await client.DropDatabaseAsync(dbName);
            _logger.LogInformation("Database deleted - DbName: {DbName}", dbName);
        }

        public async Task resetDatabase(string dbName = "default")
        {
            _logger.LogWarning("Resetting database - DbName: {DbName}", dbName);
            if (dbName == null)
                dbName = "default";
            await deleteDatabase(dbName);
            await CreateDatabase(dbName, CreateDefaultEntry: false);
            _logger.LogInformation("Database reset completed - DbName: {DbName}", dbName);
        }

        public async Task resetCollection(string dbName = "default", string collection = "directory", IClientSessionHandle? session = null)
        {
            if (collection == null)
            {
                collection = "directory";
            }
            if (dbName == null)
                dbName = "default";
                
            var db = getDatabase(dbName);
            if (session != null)
            {
                await db.DropCollectionAsync(session, collection);
                await db.CreateCollectionAsync(session, collection);
            } else
            {
                await db.DropCollectionAsync(collection);
                await db.CreateCollectionAsync(collection);
            }
            
        }

        public async Task<BsonSharedInfoModel> InsertSharedInfo(BsonSharedInfoModel sim, string dbName = SHARED_DB_NAME, string collection = "info")
        {
            if (collection == null)
                collection = "info";
            await getDatabase(dbName).GetCollection<BsonSharedInfoModel>(collection).InsertOneAsync(sim);
            return sim;

        }

        public async Task DeleteSharedCollection(string collectionId, string dbName = SHARED_DB_NAME)
        {
            await getDatabase(dbName).DropCollectionAsync(collectionId);
        }

        public async Task DeleteSharedInfo(string id, string dbName = SHARED_DB_NAME, string collection = "info")
        {
            await getDatabase(dbName).GetCollection<BsonSharedInfoModel>(collection).DeleteOneAsync(x => x.Id == id);
        }

        public async Task<List<BsonSharedInfoModel>> getSharedInfoList(string dbName = SHARED_DB_NAME, string collection = "info", string? filter = null)
        {
            if (collection == null)
            if (collection == null)
                collection = "info";
            List<BsonSharedInfoModel> list = new List<BsonSharedInfoModel>();
            if (string.IsNullOrEmpty(filter))
                list = await (await getDatabase(dbName).GetCollection<BsonSharedInfoModel>(collection).FindAsync<BsonSharedInfoModel>(Builders<BsonSharedInfoModel>.Filter.Empty)).ToListAsync();
            else
                list = await getDatabase(dbName).GetCollection<BsonSharedInfoModel>(collection).Find(Builders<BsonSharedInfoModel>.Filter.Where( x => x.Name.Contains(filter) || x.Description.Contains(filter))).ToListAsync();
            return list;
        }

        public async Task<BsonSharedInfoModel> getSingleFile(string id, string dbName = SHARED_DB_NAME, string collection = "info")
        {
            if (collection == null)
                collection = "info";
            return await getDatabase(dbName).GetCollection<BsonSharedInfoModel>(collection).Find(Builders<BsonSharedInfoModel>.Filter.Where(x => x.Id == id)).FirstOrDefaultAsync();
        }

        public async Task SaveConfig(GeneralConfig gc)
        {
            await getDatabase(CONFIG_DB_NAME).GetCollection<GeneralConfig>("config").ReplaceOneAsync(new BsonDocument("_id", gc.type), options: new ReplaceOptions { IsUpsert = true }, replacement: gc);
        }

        public async Task<GeneralConfig> LoadConfig()
        {
            return await (await getDatabase(CONFIG_DB_NAME).GetCollection<GeneralConfig>("config").FindAsync(Builders<GeneralConfig>.Filter.Where(x => x.type == "general"))).FirstOrDefaultAsync() ?? new GeneralConfig();
        }

        public async Task<List<BsonFileManagerModel>> getAllDatabaseData(string dbName, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            return await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).AsQueryable<BsonFileManagerModel>().ToListAsync();
        }

        public async Task<List<BsonFileManagerModel>> getShareFolder(string dbName, string bsonId, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            BsonFileManagerModel father = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.Id == bsonId))).FirstOrDefaultAsync();
            if (father == null)
            {
                throw new InvalidOperationException("File not found");
            }

            if (father.FilePath == "")
            {
                return await getAllDatabaseData(dbName);
            }
            List<BsonFileManagerModel> bsonFileManagerModels = new List<BsonFileManagerModel>();

            if (father.IsFile)
            {
                var newFather = getDefaultEntry("1");
                father.ParentId = newFather.Id;
                father.FilePath = "/" + newFather.Name;
                father.FilterId = newFather.Id + "/";
                father.FilterPath = "/";
                bsonFileManagerModels.Add(newFather);
                bsonFileManagerModels.Add(father);
            } else
            {
                

                List<BsonFileManagerModel> childs = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilterPath.StartsWith(father.FilePath)))).ToListAsync();
                
                foreach (var child in childs)
                {
                    var regexFP = new Regex(Regex.Escape(father.FilePath));
                    child.FilterPath = regexFP.Replace(child.FilterPath, "", 1);
                    var regexFi = new Regex(Regex.Escape(father.FilterId));
                    child.FilterId = regexFi.Replace(child.FilterId, "", 1);
                    var regexFilePath = new Regex(Regex.Escape(father.FilePath));
                    child.FilePath = regexFilePath.Replace(child.FilePath, "", 1);
                }

                father.FilterPath = "";
                father.FilterId = "";
                father.FilePath = "";
                father.ParentId = "";
                bsonFileManagerModels.Add(father);

                bsonFileManagerModels.AddRange(childs);
            }

            
            
            return bsonFileManagerModels;
        }

        public async Task<BsonFileManagerModel> getParentDirectory(string dbName, string filterPath, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            if (filterPath == "/")
                return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.FilterPath, filterPath == "/" ? "" : filterPath))).FirstOrDefaultAsync();
            else
                return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => (x.FilterId + x.Id.ToString() + "/") == filterPath))).FirstOrDefaultAsync();
        }

        public async Task<BsonFileManagerModel> getParentDirectoryByPath(string dbName, string filterPath, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            if (filterPath == "/")
                return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.FilterPath, filterPath == "/" ? "" : filterPath))).FirstOrDefaultAsync();
            else
                return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilterPath + x.Name + "/" == filterPath))).FirstOrDefaultAsync();
        }

        public async Task setDirectoryHasChild(string dbName, string id, string collectionName = "directory", bool hasChild = true)
        {
            if (collectionName == null)
                collectionName = "directory";
            var update = new UpdateDefinitionBuilder<BsonFileManagerModel>().Set(n => n.HasChild, hasChild);
            await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).UpdateManyAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.Id == id), update);
        }

        public async Task checkAndSetDirectoryHasChild(string dbName, string id, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var listFiles = await getAllFilesInDirectoryById(dbName, id, collectionName);
            await setDirectoryHasChild(dbName, id, collectionName, listFiles.Count() > 0);
        }

        public async Task<BsonFileManagerModel> getEntry(string dbName, string filterId, string name, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilterPath == filterId && x.Name == name))).FirstOrDefaultAsync();
        }

        public async Task deleteEntry(string dbName, string id, string collectionName = "directory")
        {
            _logger.LogDebug("Deleting entry - DbName: {DbName}, Id: {Id}", dbName, id);
            if (collectionName == null)
                collectionName = "directory";
            //BsonFileManagerModel entry = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.Id, id))).FirstOrDefaultAsync();
            await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).DeleteOneAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.Id, id));
            // return entry;
        }

        public async Task<List<BsonFileManagerModel>> Search(string dbName, string path, string searchText, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var files = await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilePath.StartsWith(path) && x.Name.ToLower().Contains(searchText.Replace("*", "").ToLower())));
            return files.ToList();
        }

        public async Task<BsonFileManagerModel> getFileByPath(string dbName, string path, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var files = await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilePath == path));
            var file = await files.FirstOrDefaultAsync();
            return file;
        }

        public BsonFileManagerModel getFileByPathSync(string dbName, string path, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var files = getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).Find(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilePath == path));
            var file = files.FirstOrDefault();
            return file;
        }

        public async Task<List<BsonFileManagerModel>> getAllFiles(string dbName, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.FilterPath, "/") | Builders<BsonFileManagerModel>.Filter.Eq(x => x.FilterPath, "Files/") | Builders<BsonFileManagerModel>.Filter.Eq(x => x.FilterId, ""))).ToListAsync();
        }

        public async Task<List<BsonFileManagerModel>> getAllFilesInDirectory(string dbName, string path, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.FilterId, path) | Builders<BsonFileManagerModel>.Filter.Where(x => x.FilterId + x.Id.ToString() + "/" == path))).ToListAsync();
            return result;
        }

        public async Task<List<BsonFileManagerModel>> getAllChildFilesInDirectory(string dbName, string path, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilterPath.StartsWith(path)))).ToListAsync();
            return result;
        }

        public async Task<List<BsonFileManagerModel>> getAllChildFoldersInDirectory(string dbName, string parentId, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.ParentId == parentId && !x.IsFile))).ToListAsync();
            return result;
        }

        public async Task<List<BsonFileManagerModel>> getAllFilesInDirectoryPath(string dbName, string path, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilterPath == path))).ToListAsync();
            return result;
        }

        public async Task<List<BsonFileManagerModel>> getAllFilesInDirectoryPath2(string dbName, string path, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilterId == path || x.FilePath + "/" == path || x.FilterPath == path))).ToListAsync();
            return result;
        }

        public async Task<List<BsonFileManagerModel>> getAllFolders(string dbName, string? parentId = null, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => !x.IsFile))).ToListAsync();
            //if (parentId  == null)
            //{
            //    return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => !x.IsFile && (x.FilterPath == "" || x.FilterPath == "/") ))).ToListAsync();
            //}
            //return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => !x.IsFile && x.ParentId == parentId))).ToListAsync();
        }

        public async Task<List<BsonFileManagerModel>> getFoldersByParentId(string dbName, string? parentId, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";

            if (string.IsNullOrEmpty(parentId))
            {
                // Root level folders - ParentId is empty
                return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName)
                    .FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => !x.IsFile && string.IsNullOrEmpty(x.ParentId))))
                    .ToListAsync();
            }

            // Child folders by ParentId
            return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName)
                .FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => !x.IsFile && x.ParentId == parentId)))
                .ToListAsync();
        }

        public async Task<List<BsonFileManagerModel>> getFilesByParentId(string dbName, string parentId, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";

            // Get all items (files and folders) by ParentId
            return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName)
                .FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.ParentId == parentId)))
                .ToListAsync();
        }

        /// <summary>
        /// Find a folder by its name and parent FilterPath.
        /// This is useful for finding folders when FilePath format doesn't match the navigation path.
        /// </summary>
        public async Task<BsonFileManagerModel?> getFolderByNameAndParentPath(string dbName, string folderName, string parentFilterPath, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";

            // For first-level folders, FilterPath is "/"
            // For nested folders, FilterPath is the parent path (e.g., "/Parent/")
            var filter = Builders<BsonFileManagerModel>.Filter.Where(x =>
                !x.IsFile &&
                x.Name == folderName &&
                x.FilterPath == parentFilterPath);

            return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName)
                .FindAsync(filter))
                .FirstOrDefaultAsync();
        }

        public async Task<List<int>> getAllIdsFromChannel(string dbName, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var collection = getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName);

            var items = await collection
            .Find(x => x.IsFile)
            .Project(x => x.MessageId)
            .ToListAsync();

            var allMessageIds = new HashSet<int>();
            foreach (var item in items)
            {
                if (item != null)
                    allMessageIds.Add(item.Value);

                //if (item != null)
                //    allMessageIds.UnionWith(item);
            }

            return allMessageIds.OrderBy(x => x).ToList();
        }

        public async Task<List<string>> getAllFileNamesFromChannel(string dbName, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var collection = getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName);

            var items = await collection
            .Find(x => x.IsFile)
            .Project(x => x.Name)
            .ToListAsync();

            var allMessageNames = new HashSet<string>();
            foreach (var item in items)
            {
                if (item != null)
                    allMessageNames.Add(item);

                //if (item != null)
                //    allMessageIds.UnionWith(item);
            }

            return allMessageNames.OrderBy(x => x).ToList();
        }

        public async Task<List<BsonFileManagerModel>> getAllFilesInDirectoryById(string dbName, string idFolder, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.ParentId, idFolder))).ToListAsync();
            return result;
        }

        public async Task<BsonFileManagerModel> getFileById(string dbName, string id, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.Id, id))).FirstOrDefaultAsync();
        }

        public async Task<bool> existItemByTelegramId(string dbName, int id, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.MessageId == id || (x.ListMessageId != null && x.ListMessageId.Contains(id))))).FirstOrDefaultAsync();
            return result != null;
        }

        public async Task<BsonFileManagerModel> copyItem(string dbName, string sourceId, Syncfusion.Blazor.FileManager.FileManagerDirectoryContent target, string targetPath, bool isFile, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.Id == sourceId))).FirstOrDefaultAsync();
            result.Id = null;
            result.ParentId = target.Id;
            result.DateModified = DateTime.Now;
            result.FilterId = (target.FilterId ?? "") + target.Id + "/";
            result.ParentId = target.Id;
            // Only empty string indicates root folder (the "Files" folder)
            // FilterPath "/" means a first-level folder, not the root itself
            var isRootTarget = string.IsNullOrEmpty(target.FilterPath);
            result.FilterPath = isRootTarget ? "/" : target.FilterPath + target.Name + "/";
            result.FilePath = isFile ? targetPath + result.Name : targetPath.TrimEnd('/');
            await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).InsertOneAsync(result);
            return result;

        }


        private async Task<List<string>> getParentFolders(string dbName, string folderId, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            List<string> folders = new List<string>();
            while (!string.IsNullOrEmpty(folderId))
            {
                folders.Add(folderId);
                folderId = (await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.Id == folderId))).FirstOrDefaultAsync()).ParentId;

            }
            return folders;
        }

        /// <summary>
        /// add bytes to folder
        /// </summary>
        /// <param name="dbName"></param>
        /// <param name="folderId"></param>
        /// <param name="bytes"></param>
        /// <param name="collectionName"></param>
        /// <returns></returns>
        public async Task addBytesToFolder(string dbName, string folderId, long bytes, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            List<string> folders = await getParentFolders(dbName, folderId, collectionName);
            var update = new UpdateDefinitionBuilder<BsonFileManagerModel>().Inc(n => n.Size, bytes);
            await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).UpdateManyAsync(Builders<BsonFileManagerModel>.Filter.Where(x => folders.Contains(x.Id) && !x.IsFile), update);
        }
        /// <summary>
        /// substract bytes to folder
        /// </summary>
        /// <param name="dbName"></param>
        /// <param name="folderId"></param>
        /// <param name="bytes"></param>
        /// <param name="collectionName"></param>
        /// <returns></returns>
        public async Task subBytesToFolder(string dbName, string folderId, long bytes, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            List<string> folders = await getParentFolders(dbName, folderId, collectionName);
            var update = new UpdateDefinitionBuilder<BsonFileManagerModel>().Inc(n => n.Size, -bytes);
            await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).UpdateManyAsync(Builders<BsonFileManagerModel>.Filter.Where(x => folders.Contains(x.Id) && !x.IsFile), update);
        }

        public void updateAllPathFiles(string dbName, string oldPath, string newPath, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).AsQueryable().Where(r => r.FilePath.StartsWith(oldPath)).ToList().ForEach(x =>
            {
                var regex = new Regex(Regex.Escape(oldPath));
                // x.FilePath = regex.Replace(x.FilePath, newPath, 1);
                var update = new UpdateDefinitionBuilder<BsonFileManagerModel>().Set(n => n.FilePath, regex.Replace(x.FilePath, newPath, 1));
                var updateFilterPath = new UpdateDefinitionBuilder<BsonFileManagerModel>().Set(n => n.FilterPath, regex.Replace(x.FilterPath, newPath, 1));
                var filter = Builders<BsonFileManagerModel>.Filter.Eq(f => f.Id, x.Id);
                getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).UpdateOne(filter, update);
                getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).UpdateOne(filter, updateFilterPath);
            });

        }

        public async Task<BsonFileManagerModel> updateName(string dbName, string id, string newName, string oldName, bool isFile, string filePath, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";

            // Calculate new FilePath (FilterPath + newName)
            string newFilePath = filePath + newName;

            var update = new UpdateDefinitionBuilder<BsonFileManagerModel>()
                .Set(n => n.Name, newName)
                .Set(n => n.FilePath, newFilePath);
            await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).UpdateOneAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.Id == id), update);
            if (!isFile)
            {
                updateAllPathFiles(dbName, filePath + oldName + "/", filePath + newName + "/", collectionName);
            }
            return await getFileById(dbName, id);
        }

        public async Task<List<BsonFileManagerModel>> createEntry(string dbName, BsonFileManagerModel file, string collectionName = "directory", IClientSessionHandle? session = null)
        {
            _logger.LogDebug("Creating entry - DbName: {DbName}, Name: {Name}, IsFile: {IsFile}, Path: {Path}",
                dbName, file?.Name, file?.IsFile, file?.FilePath);
            if (collectionName == null)
                collectionName = "directory";
            if (file != null)
            {
                if (session != null)
                    await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).InsertOneAsync(session, file);
                else
                    await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).InsertOneAsync(file);
                if (!file.IsFile)
                {
                    //return file.FilePath == "/" ? await getAllFilesInDirectoryPath2(file.FilePath) : await getAllFilesInDirectoryPath(file.FilePath);
                    return new List<BsonFileManagerModel>() { file };
                }
            }
            return null;
        }

        public async Task<BsonFileManagerModel> getRootFolder(string dbName, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.ParentId == "" && x.Name == "Files"))).FirstOrDefaultAsync();
        }



        public async Task<BsonFileManagerModel> toBasonFile(string Path, string FolderName, FileManagerDirectoryContent ParentFolder)
        {
            return new BsonFileManagerModel(Path, FolderName, ParentFolder);
        }

        //public async Task<List<BsonFileManagerModel>> createConfigEntry(object file, string collectionName = "directory")
        //{
        //    if (file != null)
        //    {
        //        await currentDatabase.GetCollection<BsonFileManagerModel>(collectionName).InsertOneAsync(file);
        //        if (!file.IsFile)
        //        {
        //            return await getAllFilesInDirectory(file.Id, file.Path);
        //        }
        //    }
        //    return null;
        //}

        async Task<MemoryStream> ToMemoryStreamAsync(Stream stream)
        {
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }



        private async Task createDefaultEntry(string dbName, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";
            var filter = Builders<BsonFileManagerModel>.Filter.Eq(x => x.FilterPath, "");
            BsonFileManagerModel lbfmm = (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(filter)).FirstOrDefault();
            if (lbfmm == null)
            {
                lbfmm = getDefaultEntry();
                await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).InsertOneAsync(lbfmm);
            }
        }

        private BsonFileManagerModel getDefaultEntry(string? id = null)
        {
            BsonFileManagerModel entry = new BsonFileManagerModel()
            {
                CaseSensitive = false,
                DateCreated = DateTime.Now,
                DateModified = DateTime.Now,
                FilterPath = "",
                FilterId = "",
                FilePath = "",
                HasChild = true,
                IsFile = false,
                Name = "Files",
                ParentId = "",
                ShowHiddenItems = false,
                Size = 0,
                Type = "folder"
            };

            if (id != null)
            {
                entry.Id = id;
            }
            return entry;
        }

        #region Task Persistence Operations

        /// <summary>
        /// Save a new task to the persistence store
        /// </summary>
        public async Task<PersistedTaskModel> SaveTask(PersistedTaskModel task)
        {
            task.LastUpdated = DateTime.Now;
            if (string.IsNullOrEmpty(task.Id))
            {
                task.Id = ObjectId.GenerateNewId().ToString();
            }

            await getDatabase(CONFIG_DB_NAME)
                .GetCollection<PersistedTaskModel>(TASKS_COLLECTION)
                .InsertOneAsync(task);

            _logger.LogInformation("Saved task {InternalId} ({Type}) to persistence", task.InternalId, task.Type);
            return task;
        }

        /// <summary>
        /// Update an existing task in the persistence store
        /// </summary>
        public async Task<PersistedTaskModel> UpdateTask(PersistedTaskModel task)
        {
            task.LastUpdated = DateTime.Now;

            var filter = Builders<PersistedTaskModel>.Filter.Eq(x => x.InternalId, task.InternalId);
            await getDatabase(CONFIG_DB_NAME)
                .GetCollection<PersistedTaskModel>(TASKS_COLLECTION)
                .ReplaceOneAsync(filter, task, new ReplaceOptions { IsUpsert = true });

            return task;
        }

        /// <summary>
        /// Update task progress (optimized for frequent updates)
        /// </summary>
        public async Task UpdateTaskProgress(string internalId, long transmitted, int progress, StateTask state)
        {
            var filter = Builders<PersistedTaskModel>.Filter.Eq(x => x.InternalId, internalId);
            var update = Builders<PersistedTaskModel>.Update
                .Set(x => x.TransmittedBytes, transmitted)
                .Set(x => x.Progress, progress)
                .Set(x => x.State, state)
                .Set(x => x.LastUpdated, DateTime.Now);

            await getDatabase(CONFIG_DB_NAME)
                .GetCollection<PersistedTaskModel>(TASKS_COLLECTION)
                .UpdateOneAsync(filter, update);
        }

        /// <summary>
        /// Update only the task state
        /// </summary>
        public async Task UpdateTaskState(string internalId, StateTask state)
        {
            var filter = Builders<PersistedTaskModel>.Filter.Eq(x => x.InternalId, internalId);
            var update = Builders<PersistedTaskModel>.Update
                .Set(x => x.State, state)
                .Set(x => x.LastUpdated, DateTime.Now);

            await getDatabase(CONFIG_DB_NAME)
                .GetCollection<PersistedTaskModel>(TASKS_COLLECTION)
                .UpdateOneAsync(filter, update);
        }

        /// <summary>
        /// Delete a task from the persistence store
        /// </summary>
        public async Task DeleteTask(string internalId)
        {
            await getDatabase(CONFIG_DB_NAME)
                .GetCollection<PersistedTaskModel>(TASKS_COLLECTION)
                .DeleteOneAsync(x => x.InternalId == internalId);

            _logger.LogInformation("Deleted task {InternalId} from persistence", internalId);
        }

        /// <summary>
        /// Get all tasks that should be resumed (Pending, Working, Paused, Error)
        /// </summary>
        public async Task<List<PersistedTaskModel>> GetAllPendingTasks()
        {
            var filter = Builders<PersistedTaskModel>.Filter.In(
                x => x.State,
                new[] { StateTask.Pending, StateTask.Working, StateTask.Paused, StateTask.Error }
            );

            var tasks = await (await getDatabase(CONFIG_DB_NAME)
                .GetCollection<PersistedTaskModel>(TASKS_COLLECTION)
                .FindAsync(filter)).ToListAsync();

            _logger.LogInformation("Loaded {Count} pending tasks from persistence", tasks.Count);
            return tasks;
        }

        /// <summary>
        /// Get a task by its internal ID
        /// </summary>
        public async Task<PersistedTaskModel> GetTaskByInternalId(string internalId)
        {
            var filter = Builders<PersistedTaskModel>.Filter.Eq(x => x.InternalId, internalId);
            return await (await getDatabase(CONFIG_DB_NAME)
                .GetCollection<PersistedTaskModel>(TASKS_COLLECTION)
                .FindAsync(filter)).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Mark a task as error with an error message
        /// </summary>
        public async Task MarkTaskAsError(string internalId, string errorMessage)
        {
            var filter = Builders<PersistedTaskModel>.Filter.Eq(x => x.InternalId, internalId);
            var update = Builders<PersistedTaskModel>.Update
                .Set(x => x.State, StateTask.Error)
                .Set(x => x.LastError, errorMessage)
                .Inc(x => x.RetryCount, 1)
                .Set(x => x.LastUpdated, DateTime.Now);

            await getDatabase(CONFIG_DB_NAME)
                .GetCollection<PersistedTaskModel>(TASKS_COLLECTION)
                .UpdateOneAsync(filter, update);

            _logger.LogWarning("Marked task {InternalId} as error: {Error}", internalId, errorMessage);
        }

        /// <summary>
        /// Cleanup stale tasks older than maxAgeDays
        /// </summary>
        public async Task CleanupStaleTasks(int maxAgeDays = 7)
        {
            var cutoffDate = DateTime.Now.AddDays(-maxAgeDays);
            var filter = Builders<PersistedTaskModel>.Filter.And(
                Builders<PersistedTaskModel>.Filter.Lt(x => x.LastUpdated, cutoffDate),
                Builders<PersistedTaskModel>.Filter.In(x => x.State,
                    new[] { StateTask.Error, StateTask.Canceled })
            );

            var result = await getDatabase(CONFIG_DB_NAME)
                .GetCollection<PersistedTaskModel>(TASKS_COLLECTION)
                .DeleteManyAsync(filter);

            if (result.DeletedCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} stale tasks older than {Days} days",
                    result.DeletedCount, maxAgeDays);
            }
        }

        /// <summary>
        /// Clear all persisted tasks from the database
        /// </summary>
        public async Task ClearAllTasks()
        {
            var result = await getDatabase(CONFIG_DB_NAME)
                .GetCollection<PersistedTaskModel>(TASKS_COLLECTION)
                .DeleteManyAsync(Builders<PersistedTaskModel>.Filter.Empty);

            _logger.LogInformation("Cleared all persisted tasks - Count: {Count}", result.DeletedCount);
        }

        #endregion

        #region Maintenance Operations

        /// <summary>
        /// Get all database names that represent Telegram channels (numeric IDs)
        /// Excludes system databases like TCCONFIG, TFM-SHARED, admin, local, config
        /// </summary>
        public async Task<List<string>> GetAllChannelDatabaseNames()
        {
            var excludedDatabases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                CONFIG_DB_NAME,
                SHARED_DB_NAME,
                "admin",
                "local",
                "config",
                "default"
            };

            var databaseNames = new List<string>();

            using (var cursor = await client.ListDatabaseNamesAsync())
            {
                while (await cursor.MoveNextAsync())
                {
                    foreach (var dbName in cursor.Current)
                    {
                        // Only include databases that are numeric (channel IDs) or start with "-" (group IDs)
                        if (!excludedDatabases.Contains(dbName) &&
                            (long.TryParse(dbName, out _) || (dbName.StartsWith("-") && long.TryParse(dbName, out _))))
                        {
                            databaseNames.Add(dbName);
                        }
                    }
                }
            }

            _logger.LogInformation("Found {Count} channel databases", databaseNames.Count);
            return databaseNames;
        }

        /// <summary>
        /// Get statistics for a specific database including size, document count, and dates
        /// </summary>
        public async Task<DatabaseStats> GetDatabaseStats(string dbName)
        {
            var stats = new DatabaseStats();

            try
            {
                var database = getDatabase(dbName);

                // Get database stats using command
                var command = new BsonDocument { { "dbStats", 1 } };
                var result = await database.RunCommandAsync<BsonDocument>(command);

                if (result.Contains("dataSize"))
                {
                    stats.SizeInBytes = result["dataSize"].ToInt64();
                }

                // Get document count from the directory collection
                var collection = database.GetCollection<BsonFileManagerModel>("directory");
                stats.DocumentCount = await collection.CountDocumentsAsync(Builders<BsonFileManagerModel>.Filter.Empty);

                // Get creation date (oldest document) and last modified (newest document)
                var oldestDoc = await collection
                    .Find(Builders<BsonFileManagerModel>.Filter.Empty)
                    .Sort(Builders<BsonFileManagerModel>.Sort.Ascending(x => x.DateCreated))
                    .Limit(1)
                    .FirstOrDefaultAsync();

                var newestDoc = await collection
                    .Find(Builders<BsonFileManagerModel>.Filter.Empty)
                    .Sort(Builders<BsonFileManagerModel>.Sort.Descending(x => x.DateModified))
                    .Limit(1)
                    .FirstOrDefaultAsync();

                if (oldestDoc != null)
                {
                    stats.CreatedAt = oldestDoc.DateCreated;
                }

                if (newestDoc != null)
                {
                    stats.LastModified = newestDoc.DateModified;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting stats for database {DbName}", dbName);
            }

            return stats;
        }

        /// <summary>
        /// Analyze FilterPath issues without repairing them.
        /// Returns detailed information about items that need repair.
        /// </summary>
        public async Task<FilterPathAnalysisResult> AnalyzeFilterPaths(string dbName, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";

            var result = new FilterPathAnalysisResult { DatabaseName = dbName };

            try
            {
                var collection = getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName);
                var allItems = await (await collection.FindAsync(Builders<BsonFileManagerModel>.Filter.Empty)).ToListAsync();

                result.TotalItems = allItems.Count;

                // Build a dictionary for quick lookup by Id
                var itemsById = allItems.ToDictionary(x => x.Id, x => x);

                foreach (var item in allItems)
                {
                    // Skip the root folder (no ParentId)
                    if (string.IsNullOrEmpty(item.ParentId))
                        continue;

                    // Calculate the correct FilterPath and FilterId
                    var correctFilterPath = CalculateFilterPath(item.ParentId, itemsById);
                    var correctFilterId = CalculateFilterId(item.ParentId, itemsById);
                    var normalizedFilePath = item.FilePath?.Replace("\\", "/");

                    bool hasIssue = false;

                    if (item.FilterPath != correctFilterPath)
                    {
                        result.FilterPathIssues++;
                        hasIssue = true;
                    }

                    if (item.FilterId != correctFilterId)
                    {
                        result.FilterIdIssues++;
                        hasIssue = true;
                    }

                    if (normalizedFilePath != item.FilePath)
                    {
                        result.FilePathIssues++;
                        hasIssue = true;
                    }

                    if (hasIssue)
                    {
                        result.ItemsWithIssues++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing FilterPaths for database {DbName}", dbName);
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Repair FilterPath and FilterId for all items in a database.
        /// This fixes data corruption caused by incorrect path calculations during move operations.
        /// </summary>
        public async Task<int> RepairFilterPaths(string dbName, string collectionName = "directory")
        {
            if (collectionName == null)
                collectionName = "directory";

            var collection = getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName);
            var allItems = await (await collection.FindAsync(Builders<BsonFileManagerModel>.Filter.Empty)).ToListAsync();

            // Build a dictionary for quick lookup by Id
            var itemsById = allItems.ToDictionary(x => x.Id, x => x);

            int repairedCount = 0;

            foreach (var item in allItems)
            {
                // Skip the root folder (no ParentId)
                if (string.IsNullOrEmpty(item.ParentId))
                    continue;

                // Calculate the correct FilterPath and FilterId by walking up the parent chain
                var correctFilterPath = CalculateFilterPath(item.ParentId, itemsById);
                var correctFilterId = CalculateFilterId(item.ParentId, itemsById);

                // Also normalize FilePath (replace backslashes with forward slashes)
                var normalizedFilePath = item.FilePath?.Replace("\\", "/");

                bool needsUpdate = false;
                var updateDef = Builders<BsonFileManagerModel>.Update.Combine();

                if (item.FilterPath != correctFilterPath)
                {
                    updateDef = updateDef.Set(x => x.FilterPath, correctFilterPath);
                    needsUpdate = true;
                }

                if (item.FilterId != correctFilterId)
                {
                    updateDef = updateDef.Set(x => x.FilterId, correctFilterId);
                    needsUpdate = true;
                }

                if (normalizedFilePath != item.FilePath)
                {
                    updateDef = updateDef.Set(x => x.FilePath, normalizedFilePath);
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    await collection.UpdateOneAsync(
                        Builders<BsonFileManagerModel>.Filter.Eq(x => x.Id, item.Id),
                        updateDef);
                    repairedCount++;
                }
            }

            _logger.LogInformation("Repaired {Count} items in database {DbName}", repairedCount, dbName);
            return repairedCount;
        }

        private string CalculateFilterPath(string parentId, Dictionary<string, BsonFileManagerModel> itemsById)
        {
            if (string.IsNullOrEmpty(parentId) || !itemsById.TryGetValue(parentId, out var parent))
                return "/";

            // If parent is root (no ParentId), return "/"
            if (string.IsNullOrEmpty(parent.ParentId))
                return "/";

            // Otherwise, build the path recursively
            var parentPath = CalculateFilterPath(parent.ParentId, itemsById);
            return parentPath + parent.Name + "/";
        }

        private string CalculateFilterId(string parentId, Dictionary<string, BsonFileManagerModel> itemsById)
        {
            if (string.IsNullOrEmpty(parentId) || !itemsById.TryGetValue(parentId, out var parent))
                return "";

            // If parent is root (no ParentId), return just the parent's Id
            if (string.IsNullOrEmpty(parent.ParentId))
                return parent.Id + "/";

            // Otherwise, build the FilterId recursively
            var parentFilterId = CalculateFilterId(parent.ParentId, itemsById);
            return parentFilterId + parent.Id + "/";
        }

        #endregion

        #region Playlist Operations

        private const string PLAYLIST_COLLECTION = "playlists";

        public async Task<PlaylistModel> CreatePlaylist(PlaylistModel playlist)
        {
            if (string.IsNullOrEmpty(playlist.Id))
            {
                playlist.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
            }
            playlist.DateCreated = DateTime.Now;
            playlist.DateModified = DateTime.Now;

            var collection = getDatabase(CONFIG_DB_NAME).GetCollection<PlaylistModel>(PLAYLIST_COLLECTION);
            await collection.InsertOneAsync(playlist);

            _logger.LogInformation("Created playlist {Name} with Id {Id}", playlist.Name, playlist.Id);
            return playlist;
        }

        public async Task<List<PlaylistModel>> GetAllPlaylists()
        {
            var collection = getDatabase(CONFIG_DB_NAME).GetCollection<PlaylistModel>(PLAYLIST_COLLECTION);
            var playlists = await (await collection.FindAsync(Builders<PlaylistModel>.Filter.Empty)).ToListAsync();
            return playlists.OrderByDescending(p => p.DateModified).ToList();
        }

        public async Task<PlaylistModel?> GetPlaylistById(string id)
        {
            var collection = getDatabase(CONFIG_DB_NAME).GetCollection<PlaylistModel>(PLAYLIST_COLLECTION);
            return await (await collection.FindAsync(Builders<PlaylistModel>.Filter.Eq(x => x.Id, id))).FirstOrDefaultAsync();
        }

        public async Task UpdatePlaylist(PlaylistModel playlist)
        {
            playlist.DateModified = DateTime.Now;

            var collection = getDatabase(CONFIG_DB_NAME).GetCollection<PlaylistModel>(PLAYLIST_COLLECTION);
            var filter = Builders<PlaylistModel>.Filter.Eq(x => x.Id, playlist.Id);
            await collection.ReplaceOneAsync(filter, playlist, new ReplaceOptions { IsUpsert = false });

            _logger.LogInformation("Updated playlist {Name} with Id {Id}", playlist.Name, playlist.Id);
        }

        public async Task DeletePlaylist(string id)
        {
            var collection = getDatabase(CONFIG_DB_NAME).GetCollection<PlaylistModel>(PLAYLIST_COLLECTION);
            await collection.DeleteOneAsync(Builders<PlaylistModel>.Filter.Eq(x => x.Id, id));

            _logger.LogInformation("Deleted playlist with Id {Id}", id);
        }

        public async Task AddTrackToPlaylist(string playlistId, PlaylistTrackModel track)
        {
            var collection = getDatabase(CONFIG_DB_NAME).GetCollection<PlaylistModel>(PLAYLIST_COLLECTION);

            // Get current playlist to determine next order
            var playlist = await GetPlaylistById(playlistId);
            if (playlist == null)
            {
                _logger.LogWarning("Playlist {Id} not found when adding track", playlistId);
                return;
            }

            // Set order to be at the end
            track.Order = playlist.Tracks.Count;
            track.DateAdded = DateTime.Now;

            var filter = Builders<PlaylistModel>.Filter.Eq(x => x.Id, playlistId);
            var update = Builders<PlaylistModel>.Update
                .Push(x => x.Tracks, track)
                .Set(x => x.DateModified, DateTime.Now);

            await collection.UpdateOneAsync(filter, update);

            _logger.LogInformation("Added track {FileName} to playlist {Id}", track.FileName, playlistId);
        }

        public async Task RemoveTrackFromPlaylist(string playlistId, string fileId)
        {
            var collection = getDatabase(CONFIG_DB_NAME).GetCollection<PlaylistModel>(PLAYLIST_COLLECTION);

            var filter = Builders<PlaylistModel>.Filter.Eq(x => x.Id, playlistId);
            var update = Builders<PlaylistModel>.Update
                .PullFilter(x => x.Tracks, t => t.FileId == fileId)
                .Set(x => x.DateModified, DateTime.Now);

            await collection.UpdateOneAsync(filter, update);

            // Reorder remaining tracks
            var playlist = await GetPlaylistById(playlistId);
            if (playlist != null && playlist.Tracks.Count > 0)
            {
                for (int i = 0; i < playlist.Tracks.Count; i++)
                {
                    playlist.Tracks[i].Order = i;
                }
                await UpdatePlaylist(playlist);
            }

            _logger.LogInformation("Removed track {FileId} from playlist {Id}", fileId, playlistId);
        }

        public async Task ReorderPlaylistTracks(string playlistId, List<string> orderedFileIds)
        {
            var playlist = await GetPlaylistById(playlistId);
            if (playlist == null)
            {
                _logger.LogWarning("Playlist {Id} not found when reordering tracks", playlistId);
                return;
            }

            // Reorder tracks based on the provided order
            var reorderedTracks = new List<PlaylistTrackModel>();
            for (int i = 0; i < orderedFileIds.Count; i++)
            {
                var track = playlist.Tracks.FirstOrDefault(t => t.FileId == orderedFileIds[i]);
                if (track != null)
                {
                    track.Order = i;
                    reorderedTracks.Add(track);
                }
            }

            playlist.Tracks = reorderedTracks;
            await UpdatePlaylist(playlist);

            _logger.LogInformation("Reordered {Count} tracks in playlist {Id}", reorderedTracks.Count, playlistId);
        }

        #endregion

    }
}
