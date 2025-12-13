using MongoDB.Driver;
using MongoDB.Bson;
using TelegramDownloader.Models;
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

        public const string SHARED_DB_NAME = "TFM-SHARED";

        public DbService(ILogger<DbService> logger)
        {
            _logger = logger;
            client = new MongoClient(GeneralConfigStatic.tlconfig?.mongo_connection_string ?? Environment.GetEnvironmentVariable("connectionString"));
            currentDatabase = getDatabase("default");
            this.dbName = "default";
            _logger.LogInformation("DbService initialized - Connected to MongoDB");
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
            result.FilterId = target.FilterId + target.Id + "/";
            result.ParentId = target.Id;
            result.FilterPath = target.FilterPath == "" ? "/" : target.FilterPath + target.Name + "/";
            result.FilePath = isFile ? targetPath + result.Name : targetPath;
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
            var update = new UpdateDefinitionBuilder<BsonFileManagerModel>().Set(n => n.Name, newName);
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


    }
}
