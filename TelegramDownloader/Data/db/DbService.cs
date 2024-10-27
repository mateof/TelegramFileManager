using MongoDB.Driver;
using MongoDB.Bson;
using TelegramDownloader.Models;
using Syncfusion.Blazor.FileManager;
using System.Text.RegularExpressions;
using MongoDB.Driver.Linq;

namespace TelegramDownloader.Data.db
{
    public class DbService : IDbService
    {
        private MongoClient client { get; set; }
        private ITelegramService _ts { get; set; }
        private IMongoDatabase currentDatabase { get; set; }
        private string dbName { get; set; }

        public DbService(ITelegramService ts)
        {
            _ts = ts;
            client = new MongoClient(GeneralConfigStatic.tlconfig?.mongo_connection_string ?? Environment.GetEnvironmentVariable("connectionString"));
            currentDatabase = getDatabase("default");
            this.dbName = "default";
        }

        public IMongoDatabase getDatabase(string dbName)
        {
            // currentDatabase = client.GetDatabase(dbName);
            return client.GetDatabase(dbName);
        }

        public async Task createIndex(string dbName, string collectionName = "directory")
        {
            var options = new CreateIndexOptions() { Unique = true, Name = "uniquefile" };
            var indexKeysDefinition = Builders<BsonFileManagerModel>.IndexKeys.Ascending(x => x.FilePath);
            await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).Indexes.CreateOneAsync(indexKeysDefinition, options);
        }

        public async Task CreateDatabase(string dbName = "default", string collection = "directory", bool CreateDefaultEntry = true)
        {
            this.dbName = dbName;
            var db = getDatabase(dbName);
            var filter = new BsonDocument("name", collection);
            if (!(await (await db.ListCollectionsAsync(new ListCollectionsOptions { Filter = filter })).AnyAsync()))
            {
                await db.CreateCollectionAsync(collection);
                // await createIndex(dbName, collection);
            }
            await createIndex(dbName, collection);
            if (CreateDefaultEntry)
                await createDefaultEntry(dbName);

        }

        public async Task deleteDatabase(string dbName = "default")
        {
            await client.DropDatabaseAsync(dbName);
        }

        public async Task resetDatabase(string dbName = "default")
        {
            await deleteDatabase(dbName);
            await CreateDatabase(dbName, CreateDefaultEntry: false);
        }

        public async Task SaveConfig(GeneralConfig gc)
        {
            await getDatabase("TCCONFIG").GetCollection<GeneralConfig>("config").ReplaceOneAsync(new BsonDocument("_id", gc.type), options: new ReplaceOptions { IsUpsert = true }, replacement: gc);
        }

        public async Task<GeneralConfig> LoadConfig()
        {
            return await (await getDatabase("TCCONFIG").GetCollection<GeneralConfig>("config").FindAsync(Builders<GeneralConfig>.Filter.Where(x => x.type == "general"))).FirstOrDefaultAsync() ?? new GeneralConfig();
        }

        public async Task<List<BsonFileManagerModel>> getAllDatabaseData(string dbName, string collectionName = "directory")
        {
            return await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).AsQueryable<BsonFileManagerModel>().ToListAsync();
        }

        public async Task<BsonFileManagerModel> getParentDirectory(string dbName, string filterPath, string collectionName = "directory")
        {
            if (filterPath == "/")
                return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.FilterPath, filterPath == "/" ? "" : filterPath))).FirstOrDefaultAsync();
            else
                return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => (x.FilterId + x.Id.ToString() + "/") == filterPath))).FirstOrDefaultAsync();
        }

        public async Task<BsonFileManagerModel> getParentDirectoryByPath(string dbName, string filterPath, string collectionName = "directory")
        {
            if (filterPath == "/")
                return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.FilterPath, filterPath == "/" ? "" : filterPath))).FirstOrDefaultAsync();
            else
                return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilterPath + x.Name + "/" == filterPath))).FirstOrDefaultAsync();
        }

        public async Task setDirectoryHasChild(string dbName, string id, string collectionName = "directory", bool hasChild = true)
        {

            var update = new UpdateDefinitionBuilder<BsonFileManagerModel>().Set(n => n.HasChild, hasChild);
            await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).UpdateManyAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.Id == id), update);
        }

        public async Task checkAndSetDirectoryHasChild(string dbName, string id, string collectionName = "directory")
        {
            var listFiles = await getAllFilesInDirectoryById(dbName, id, collectionName);
            await setDirectoryHasChild(dbName, id, collectionName, listFiles.Count() > 0);
        }

        public async Task<BsonFileManagerModel> getEntry(string dbName, string filterId, string name, string collectionName = "directory")
        {
            return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilterPath == filterId && x.Name == name))).FirstOrDefaultAsync();
        }

        public async Task deleteEntry(string dbName, string id, string collectionName = "directory")
        {
            //BsonFileManagerModel entry = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.Id, id))).FirstOrDefaultAsync();
            await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).DeleteOneAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.Id, id));
            // return entry;
        }

        public async Task<List<BsonFileManagerModel>> Search(string dbName, string path, string searchText, string collectionName = "directory")
        {
            var files = await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilePath.StartsWith(path) && x.Name.ToLower().Contains(searchText.Replace("*", "").ToLower())));
            return files.ToList();
        }

        public async Task<BsonFileManagerModel> getFileByPath(string dbName, string path, string collectionName = "directory")
        {
            var files = await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilePath == path));
            var file = await files.FirstOrDefaultAsync();
            return file;
        }

        public BsonFileManagerModel getFileByPathSync(string dbName, string path, string collectionName = "directory")
        {
            var files = getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).Find(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilePath == path));
            var file = files.FirstOrDefault();
            return file;
        }

        public async Task<List<BsonFileManagerModel>> getAllFiles(string dbName, string collectionName = "directory")
        {
            return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.FilterPath, "/") | Builders<BsonFileManagerModel>.Filter.Eq(x => x.FilterId, ""))).ToListAsync();
        }

        public async Task<List<BsonFileManagerModel>> getAllFilesInDirectory(string dbName, string path, string collectionName = "directory")
        {
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.FilterId, path) | Builders<BsonFileManagerModel>.Filter.Where(x => x.FilterId + x.Id.ToString() + "/" == path))).ToListAsync();
            return result;
        }

        public async Task<List<BsonFileManagerModel>> getAllChildFilesInDirectory(string dbName, string path, string collectionName = "directory")
        {
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilterPath.StartsWith(path)))).ToListAsync();
            return result;
        }

        public async Task<List<BsonFileManagerModel>> getAllChildFoldersInDirectory(string dbName, string parentId, string collectionName = "directory")
        {
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.ParentId == parentId && !x.IsFile))).ToListAsync();
            return result;
        }

        public async Task<List<BsonFileManagerModel>> getAllFilesInDirectoryPath(string dbName, string path, string collectionName = "directory")
        {
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilterPath == path))).ToListAsync();
            return result;
        }

        public async Task<List<BsonFileManagerModel>> getAllFilesInDirectoryPath2(string dbName, string path, string collectionName = "directory")
        {
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.FilePath + "/" == path || x.FilterPath == path))).ToListAsync();
            return result;
        }

        public async Task<List<BsonFileManagerModel>> getAllFolders(string dbName, string collectionName = "directory")
        {
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => !x.IsFile))).ToListAsync();
            return result;
        }

        public async Task<List<BsonFileManagerModel>> getAllFilesInDirectoryById(string dbName, string idFolder, string collectionName = "directory")
        {
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.ParentId, idFolder))).ToListAsync();
            return result;
        }

        public async Task<BsonFileManagerModel> getFileById(string dbName, string id, string collectionName = "directory")
        {
            return await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Eq(x => x.Id, id))).FirstOrDefaultAsync();
        }

        public async Task<bool> existItemByTelegramId(string dbName, int id, string collectionName = "directory")
        {
            var result = await (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.MessageId == id || (x.ListMessageId != null && x.ListMessageId.Contains(id))))).FirstOrDefaultAsync();
            return result != null;
        }

        public async Task<BsonFileManagerModel> copyItem(string dbName, string sourceId, Syncfusion.Blazor.FileManager.FileManagerDirectoryContent target, string targetPath, bool isFile, string collectionName = "directory")
        {
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
            List<string> folders = await getParentFolders(dbName, folderId, collectionName);
            var update = new UpdateDefinitionBuilder<BsonFileManagerModel>().Inc(n => n.Size, -bytes);
            await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).UpdateManyAsync(Builders<BsonFileManagerModel>.Filter.Where(x => folders.Contains(x.Id) && !x.IsFile), update);
        }

        public void updateAllPathFiles(string dbName, string oldPath, string newPath, string collectionName = "directory")
        {
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
            var update = new UpdateDefinitionBuilder<BsonFileManagerModel>().Set(n => n.Name, newName);
            await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).UpdateOneAsync(Builders<BsonFileManagerModel>.Filter.Where(x => x.Id == id), update);
            if (!isFile)
            {
                updateAllPathFiles(dbName, filePath + oldName + "/", filePath + newName + "/", collectionName);
            }
            return await getFileById(dbName, id);
        }

        public async Task<List<BsonFileManagerModel>> createEntry(string dbName, BsonFileManagerModel file, string collectionName = "directory")
        {
            if (file != null)
            {
                await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).InsertOneAsync(file);
                if (!file.IsFile)
                {
                    //return file.FilePath == "/" ? await getAllFilesInDirectoryPath2(file.FilePath) : await getAllFilesInDirectoryPath(file.FilePath);
                    return new List<BsonFileManagerModel>() { file };
                }
            }
            return null;
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
            var filter = Builders<BsonFileManagerModel>.Filter.Eq(x => x.FilterPath, "");
            BsonFileManagerModel lbfmm = (await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).FindAsync(filter)).FirstOrDefault();
            if (lbfmm == null)
            {
                lbfmm = new BsonFileManagerModel()
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
                await getDatabase(dbName).GetCollection<BsonFileManagerModel>(collectionName).InsertOneAsync(lbfmm);
            }
        }


    }
}
