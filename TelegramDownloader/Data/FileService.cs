using BlazorBootstrap;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.StaticFiles;
using MongoDB.Driver;
using Newtonsoft.Json;
using Syncfusion.Blazor.FileManager;
using Syncfusion.Blazor.Inputs;

using Syncfusion.EJ2.FileManager.PhysicalFileProvider;
using Syncfusion.EJ2.Linq;
using System.Dynamic;
using System.IO.Compression;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Services;
using TL;

namespace TelegramDownloader.Data
{
    public class FileService : IFileService
    {
        public static readonly List<string> ImageExtensions = new List<string> { ".JPG", ".JPEG", ".JPE", ".BMP", ".GIF", ".PNG" };
        public static string IMGDIR = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "img", "telegram");
        public static string LOCALDIR = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "local");
        public static string RELATIVELOCALDIR = System.IO.Path.Combine("local");
        public static string STATICRELATIVELOCALDIR = System.IO.Path.Combine("local");
        public static string TEMPDIR = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "local", "temp");
        public static string TEMPORARYPDIR = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "local", "temp", "_temp");
        public static string RELATIVETEMPDIR = System.IO.Path.Combine("local", "temp");
        public static readonly Dictionary<string, string> MIMETypesDictionary = new Dictionary<string, string>
  {
    {"ai", "application/postscript"},
    {"aif", "audio/x-aiff"},
    {"aifc", "audio/x-aiff"},
    {"aiff", "audio/x-aiff"},
    {"asc", "text/plain"},
    {"atom", "application/atom+xml"},
    {"au", "audio/basic"},
    {"avi", "video/x-msvideo"},
    {"bcpio", "application/x-bcpio"},
    {"bin", "application/octet-stream"},
    {"bmp", "image/bmp"},
    {"cdf", "application/x-netcdf"},
    {"cgm", "image/cgm"},
    {"class", "application/octet-stream"},
    {"cpio", "application/x-cpio"},
    {"cpt", "application/mac-compactpro"},
    {"csh", "application/x-csh"},
    {"css", "text/css"},
    {"dcr", "application/x-director"},
    {"dif", "video/x-dv"},
    {"dir", "application/x-director"},
    {"djv", "image/vnd.djvu"},
    {"djvu", "image/vnd.djvu"},
    {"dll", "application/octet-stream"},
    {"dmg", "application/octet-stream"},
    {"dms", "application/octet-stream"},
    {"doc", "application/msword"},
    {"docx","application/vnd.openxmlformats-officedocument.wordprocessingml.document"},
    {"dotx", "application/vnd.openxmlformats-officedocument.wordprocessingml.template"},
    {"docm","application/vnd.ms-word.document.macroEnabled.12"},
    {"dotm","application/vnd.ms-word.template.macroEnabled.12"},
    {"dtd", "application/xml-dtd"},
    {"dv", "video/x-dv"},
    {"dvi", "application/x-dvi"},
    {"dxr", "application/x-director"},
    {"eps", "application/postscript"},
    {"etx", "text/x-setext"},
    {"exe", "application/octet-stream"},
    {"ez", "application/andrew-inset"},
    {"flac", "audio/flac"},
    {"gif", "image/gif"},
    {"gram", "application/srgs"},
    {"grxml", "application/srgs+xml"},
    {"gtar", "application/x-gtar"},
    {"hdf", "application/x-hdf"},
    {"hqx", "application/mac-binhex40"},
    {"htm", "text/html"},
    {"html", "text/html"},
    {"ice", "x-conference/x-cooltalk"},
    {"ico", "image/x-icon"},
    {"ics", "text/calendar"},
    {"ief", "image/ief"},
    {"ifb", "text/calendar"},
    {"iges", "model/iges"},
    {"igs", "model/iges"},
    {"jnlp", "application/x-java-jnlp-file"},
    {"jp2", "image/jp2"},
    {"jpe", "image/jpeg"},
    {"jpeg", "image/jpeg"},
    {"jpg", "image/jpeg"},
    {"js", "application/x-javascript"},
    {"kar", "audio/midi"},
    {"latex", "application/x-latex"},
    {"lha", "application/octet-stream"},
    {"lzh", "application/octet-stream"},
    {"m3u", "audio/x-mpegurl"},
    {"m4a", "audio/mp4a-latm"},
    {"m4b", "audio/mp4a-latm"},
    {"m4p", "audio/mp4a-latm"},
    {"m4u", "video/vnd.mpegurl"},
    {"m4v", "video/x-m4v"},
    {"mac", "image/x-macpaint"},
    {"man", "application/x-troff-man"},
    {"mathml", "application/mathml+xml"},
    {"me", "application/x-troff-me"},
    {"mesh", "model/mesh"},
    {"mid", "audio/midi"},
    {"midi", "audio/midi"},
    {"mif", "application/vnd.mif"},
    {"mkv", "video/x-matroska" },
    {"mov", "video/quicktime"},
    {"movie", "video/x-sgi-movie"},
    {"mp2", "audio/mpeg"},
    {"mp3", "audio/mpeg"},
    {"mp4", "video/mp4"},
    {"mpe", "video/mpeg"},
    {"mpeg", "video/mpeg"},
    {"mpg", "video/mpeg"},
    {"mpga", "audio/mpeg"},
    {"ms", "application/x-troff-ms"},
    {"msh", "model/mesh"},
    {"mxu", "video/vnd.mpegurl"},
    {"nc", "application/x-netcdf"},
    {"oda", "application/oda"},
    {"ogg", "application/ogg"},
    {"pbm", "image/x-portable-bitmap"},
    {"pct", "image/pict"},
    {"pdb", "chemical/x-pdb"},
    {"pdf", "application/pdf"},
    {"pgm", "image/x-portable-graymap"},
    {"pgn", "application/x-chess-pgn"},
    {"pic", "image/pict"},
    {"pict", "image/pict"},
    {"png", "image/png"},
    {"pnm", "image/x-portable-anymap"},
    {"pnt", "image/x-macpaint"},
    {"pntg", "image/x-macpaint"},
    {"ppm", "image/x-portable-pixmap"},
    {"ppt", "application/vnd.ms-powerpoint"},
    {"pptx","application/vnd.openxmlformats-officedocument.presentationml.presentation"},
    {"potx","application/vnd.openxmlformats-officedocument.presentationml.template"},
    {"ppsx","application/vnd.openxmlformats-officedocument.presentationml.slideshow"},
    {"ppam","application/vnd.ms-powerpoint.addin.macroEnabled.12"},
    {"pptm","application/vnd.ms-powerpoint.presentation.macroEnabled.12"},
    {"potm","application/vnd.ms-powerpoint.template.macroEnabled.12"},
    {"ppsm","application/vnd.ms-powerpoint.slideshow.macroEnabled.12"},
    {"ps", "application/postscript"},
    {"qt", "video/quicktime"},
    {"qti", "image/x-quicktime"},
    {"qtif", "image/x-quicktime"},
    {"ra", "audio/x-pn-realaudio"},
    {"ram", "audio/x-pn-realaudio"},
    {"ras", "image/x-cmu-raster"},
    {"rdf", "application/rdf+xml"},
    {"rgb", "image/x-rgb"},
    {"rm", "application/vnd.rn-realmedia"},
    {"roff", "application/x-troff"},
    {"rtf", "text/rtf"},
    {"rtx", "text/richtext"},
    {"sgm", "text/sgml"},
    {"sgml", "text/sgml"},
    {"sh", "application/x-sh"},
    {"shar", "application/x-shar"},
    {"silo", "model/mesh"},
    {"sit", "application/x-stuffit"},
    {"skd", "application/x-koan"},
    {"skm", "application/x-koan"},
    {"skp", "application/x-koan"},
    {"skt", "application/x-koan"},
    {"smi", "application/smil"},
    {"smil", "application/smil"},
    {"snd", "audio/basic"},
    {"so", "application/octet-stream"},
    {"spl", "application/x-futuresplash"},
    {"src", "application/x-wais-source"},
    {"sv4cpio", "application/x-sv4cpio"},
    {"sv4crc", "application/x-sv4crc"},
    {"svg", "image/svg+xml"},
    {"swf", "application/x-shockwave-flash"},
    {"t", "application/x-troff"},
    {"tar", "application/x-tar"},
    {"tcl", "application/x-tcl"},
    {"tex", "application/x-tex"},
    {"texi", "application/x-texinfo"},
    {"texinfo", "application/x-texinfo"},
    {"tif", "image/tiff"},
    {"tiff", "image/tiff"},
    {"tr", "application/x-troff"},
    {"tsv", "text/tab-separated-values"},
    {"txt", "text/plain"},
    {"ustar", "application/x-ustar"},
    {"vcd", "application/x-cdlink"},
    {"vrml", "model/vrml"},
    {"vxml", "application/voicexml+xml"},
    {"wav", "audio/x-wav"},
    {"wbmp", "image/vnd.wap.wbmp"},
    {"wbmxl", "application/vnd.wap.wbxml"},
    {"wma", "audio/wma"},
    {"wml", "text/vnd.wap.wml"},
    {"wmlc", "application/vnd.wap.wmlc"},
    {"wmls", "text/vnd.wap.wmlscript"},
    {"wmlsc", "application/vnd.wap.wmlscriptc"},
    {"wrl", "model/vrml"},
    {"xbm", "image/x-xbitmap"},
    {"xht", "application/xhtml+xml"},
    {"xhtml", "application/xhtml+xml"},
    {"xls", "application/vnd.ms-excel"},
    {"xml", "application/xml"},
    {"xpm", "image/x-xpixmap"},
    {"xsl", "application/xml"},
    {"xlsx","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"},
    {"xltx","application/vnd.openxmlformats-officedocument.spreadsheetml.template"},
    {"xlsm","application/vnd.ms-excel.sheet.macroEnabled.12"},
    {"xltm","application/vnd.ms-excel.template.macroEnabled.12"},
    {"xlam","application/vnd.ms-excel.addin.macroEnabled.12"},
    {"xlsb","application/vnd.ms-excel.sheet.binary.macroEnabled.12"},
    {"xslt", "application/xslt+xml"},
    {"xul", "application/vnd.mozilla.xul+xml"},
    {"xwd", "image/x-xwindowdump"},
    {"xyz", "chemical/x-xyz"},
    {"zip", "application/zip"}
  };
        public static List<string> refreshChannelList = new List<string>();

        protected PhysicalFileProvider operation = new PhysicalFileProvider();
        protected ITelegramService _ts { get; set; }
        protected IDbService _db { get; set; }
        protected ILogger<IFileService> _logger { get; set; }
        protected TransactionInfoService _tis { get; set; }
        protected ToastService _toastService { get; set;  }
        private static Mutex refreshMutex = new Mutex();

        const int MaxSize = 1024 * 1024 * 1000; // 1GB 


        public FileService(ITelegramService ts, IDbService db, ILogger<IFileService> logger, TransactionInfoService tis, ToastService toastService)
        {
            _ts = ts;
            _db = db;
            _tis = tis;
            _toastService = toastService;
            _logger = logger;
            createTempFolder();
        }

        private void createTempFolder()
        {
            if (!Directory.Exists(System.IO.Path.Combine(Directory.GetCurrentDirectory(), "local", "temp")))
            {
                Directory.CreateDirectory(System.IO.Path.Combine(Directory.GetCurrentDirectory(), "local", "temp"));
            }
            if (!Directory.Exists(System.IO.Path.Combine(Directory.GetCurrentDirectory(), "local", "temp", "_temp")))
            {
                Directory.CreateDirectory(System.IO.Path.Combine(Directory.GetCurrentDirectory(), "local", "temp", "_temp"));
            }
        }

        public void cleanTempFolder()
        {
            if (Directory.Exists(TEMPORARYPDIR))
            {
                Directory.Delete(TEMPORARYPDIR, true);
                createTempFolder();
            }
        }

        public async Task<BsonFileManagerModel> getItemById(string dbName, string id)
        {
            return await _db.getFileById(dbName, id);
        }

        public async Task<BsonFileManagerModel> getSharedItemById(string id, string collection)
        {
            return await _db.getFileById(DbService.SHARED_DB_NAME, id, collection);
        }

        public async Task<BsonSharedInfoModel> GetSharedInfoById(string id)
        {
            return await _db.getSingleFile(id);
        }

        public async Task DeleteShared(string id, string collectionId)
        {
            await _db.DeleteSharedCollection(collectionId);
            await _db.DeleteSharedInfo(id);
        }

        public async Task<MemoryStream> exportAllData(string dbName)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(await _db.getAllDatabaseData(dbName));
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return new MemoryStream(bytes);
        }

        public async Task<List<BsonFileManagerModel>> ShareFile(string dbName, string bsonId)
        {
            return await _db.getShareFolder(dbName, bsonId);
        }

        public async Task<FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>> SearchAsync(string dbName, string path, string searchText, string? collectionId = null)
        {
            FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> response = new FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>();
            var files = collectionId == null ? await _db.Search(dbName, path, searchText) : await _db.Search(dbName, path, searchText, collectionName: collectionId);
            response.Files = files.Select(x => x.toFileManagerContent()).ToList();
            return response;
        }

        public FileStream? ExistFileIntempFolder(string id)
        {
            String filePath = System.IO.Path.Combine(TEMPDIR, "_temp", id);
            if (File.Exists(filePath))
            {
                return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            return null;
        }

        public async Task<FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>> itemDeleteAsync(string dbName, ItemsDeleteEventArgs<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> args)
        {
            _logger.LogInformation("Deleting items - DbName: {DbName}, Count: {Count}, Path: {Path}",
                dbName, args.Files.Count(), args.Path);
            string[] names = args.Files.Select(x => x.Name).ToArray();
            bool isMyChannel = _ts.isMyChat(Convert.ToInt64(dbName));
            // args.Response = await FileManagerService.Delete(args.Path, names, args.Files.ToArray());
            foreach (Syncfusion.Blazor.FileManager.FileManagerDirectoryContent File in args.Files)
            {
                BsonFileManagerModel entry = await _db.getEntry(dbName, File.FilterPath, File.Name);
                if (entry == null)
                    throw new Exception($"File {System.IO.Path.Combine(File.FilterPath, File.Name)} not found");
                if (!entry.IsFile)
                {
                    List<BsonFileManagerModel> allChilds = await _db.getAllChildFilesInDirectory(dbName, File.FilterPath + File.Name + "/");
                    foreach (BsonFileManagerModel child in allChilds)
                    {
                        await _db.deleteEntry(dbName, child.Id);
                        if (child.IsFile)
                        {
                            if (child.isSplit)
                            {
                                foreach (int id in child.ListMessageId)
                                {
                                    if (isMyChannel && !await _db.existItemByTelegramId(dbName, id))
                                        await _ts.deleteFile(dbName, id);
                                }
                            }
                            else
                            {
                                if (isMyChannel && !await _db.existItemByTelegramId(dbName, (int)child.MessageId))
                                    await _ts.deleteFile(dbName, (int)child.MessageId);
                            }
                        }

                        //if (item.IsFile)
                        //    await _db.subBytesToFolder(dbName, item.ParentId, item.Size);
                    }
                    await _db.deleteEntry(dbName, entry.Id);
                    // await _db.subBytesToFolder(dbName, entry.ParentId, entry.Size);
                }

                if (entry.IsFile)
                {
                    await _db.deleteEntry(dbName, entry.Id);
                    if (entry.isSplit)
                    {
                        foreach (int id in entry.ListMessageId)
                        {
                            if (isMyChannel && !await _db.existItemByTelegramId(dbName, id))
                                await _ts.deleteFile(dbName, id);
                        }
                    }
                    else
                    {
                        if (isMyChannel && !await _db.existItemByTelegramId(dbName, (int)entry.MessageId))
                            await _ts.deleteFile(dbName, (int)entry.MessageId);
                    }
                }



                await _db.subBytesToFolder(dbName, entry.ParentId, entry.Size);
            }

            await _db.checkAndSetDirectoryHasChild(dbName, args.Files.FirstOrDefault().ParentId);
            return await GetFilesPath(dbName, args.Path);
        }

        private async Task itemDeleteAsync(string dbName, string filterPath, string name, string parentId, string path)
        {

            BsonFileManagerModel entry = await _db.getEntry(dbName, filterPath, name);
            if (entry == null)
                throw new Exception($"File {System.IO.Path.Combine(filterPath, name)} not found");
            if (!entry.IsFile)
            {
                List<BsonFileManagerModel> allChilds = await _db.getAllChildFilesInDirectory(dbName, filterPath + name + "/");
                foreach (BsonFileManagerModel child in allChilds)
                {
                    await _db.deleteEntry(dbName, child.Id);
                    if (child.IsFile)
                    {
                        if (child.isSplit)
                        {
                            foreach (int id in child.ListMessageId)
                            {
                                if (!await _db.existItemByTelegramId(dbName, id))
                                    await _ts.deleteFile(dbName, id);
                            }
                        }
                        else
                        {
                            if (!await _db.existItemByTelegramId(dbName, (int)child.MessageId))
                                await _ts.deleteFile(dbName, (int)child.MessageId);
                        }
                    }

                    //if (item.IsFile)
                    //    await _db.subBytesToFolder(dbName, item.ParentId, item.Size);
                }
                await _db.deleteEntry(dbName, entry.Id);
                // await _db.subBytesToFolder(dbName, entry.ParentId, entry.Size);
            }

            if (entry.IsFile)
            {
                await _db.deleteEntry(dbName, entry.Id);
                if (entry.isSplit)
                {
                    foreach (int id in entry.ListMessageId)
                    {
                        if (!await _db.existItemByTelegramId(dbName, id))
                            await _ts.deleteFile(dbName, id);
                    }
                }
                else
                {
                    if (!await _db.existItemByTelegramId(dbName, (int)entry.MessageId))
                        await _ts.deleteFile(dbName, (int)entry.MessageId);
                }
            }



            await _db.subBytesToFolder(dbName, entry.ParentId, entry.Size);

            await _db.checkAndSetDirectoryHasChild(dbName, parentId);
        }

        public async Task oneItemDeleteAsync(string dbName, Syncfusion.Blazor.FileManager.FileManagerDirectoryContent File)
        {

            BsonFileManagerModel entry = await _db.getEntry(dbName, File.FilterPath, File.Name);
            if (entry == null)
                throw new Exception($"File {System.IO.Path.Combine(File.FilterPath, File.Name)} not found");
            if (!entry.IsFile)
            {
                List<BsonFileManagerModel> allChilds = await _db.getAllChildFilesInDirectory(dbName, File.FilterPath + File.Name + "/");
                foreach (BsonFileManagerModel child in allChilds)
                {
                    if (child.IsFile)
                    {
                        if (child.isSplit)
                        {
                            foreach (int id in child.ListMessageId)
                            {
                                if (!await _db.existItemByTelegramId(dbName, id))
                                    await _ts.deleteFile(dbName, id);
                            }
                        }
                        else
                        {
                            if (!await _db.existItemByTelegramId(dbName, (int)child.MessageId))
                                await _ts.deleteFile(dbName, (int)child.MessageId);
                        }
                    }

                    await _db.deleteEntry(dbName, child.Id);
                    //if (item.IsFile)
                    //    await _db.subBytesToFolder(dbName, item.ParentId, item.Size);
                }
                await _db.subBytesToFolder(dbName, entry.ParentId, entry.Size);
            }
            if (entry.IsFile)
                if (entry.isSplit)
                {
                    foreach (int id in entry.ListMessageId)
                    {
                        if (!await _db.existItemByTelegramId(dbName, id))
                            await _ts.deleteFile(dbName, id);
                    }
                }
                else
                {
                    if (!await _db.existItemByTelegramId(dbName, (int)entry.MessageId))
                        await _ts.deleteFile(dbName, (int)entry.MessageId);
                }

            await _db.deleteEntry(dbName, entry.Id);
            await _db.subBytesToFolder(dbName, entry.ParentId, entry.Size);
        }

        public async Task<FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>> RenameFileOrFolder(string dbName, Syncfusion.Blazor.FileManager.FileManagerDirectoryContent file, string newName)
        {
            FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> fm = new FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>();
            var lista = new List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>();
            lista.Add((await _db.updateName(dbName, file.Id, newName, file.Name, file.IsFile, file.FilterPath)).toFileManagerContent());
            fm.Files = lista;
            return fm;
        }

        public async Task<FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>> CopyItems(string dbName, ItemsMoveEventArgs<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> args)
        {
            try
            {
                FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> fm = new FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>();
                var lista = new List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>();
                foreach (var item in args.Files)
                {
                    if (!item.IsFile)
                    {
                        var result = await copyAllDirectoryFiles(dbName, item.Id, args.TargetData, args.TargetPath + item.Name + "/");
                        lista.Add(result.toFileManagerContentInCopy());

                    }
                    else
                    {
                        var result = await _db.copyItem(dbName, item.Id, args.TargetData, args.TargetPath, item.IsFile);
                        lista.Add(result.toFileManagerContentInCopy());
                        if (!args.IsCopy)
                        {
                            await _db.deleteEntry(dbName, item.Id);
                        }

                    }
                    await _db.addBytesToFolder(dbName, item.ParentId, item.Size);
                }
                fm.Files = lista;
                return fm;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error on CopyItems");
                if (e is MongoWriteException ex)
                {
                    if (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
                    {
                        NotificationModel nm = new NotificationModel();
                        nm.sendEvent(new Notification("The file exist in the directory", "Duplicate", NotificationTypes.Error));
                    }

                }
                throw e;
            }

        }

        public async Task<FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>> CopyOrMoveItems(string dbName, Syncfusion.Blazor.FileManager.FileManagerDirectoryContent[] files, string targetPath, Syncfusion.Blazor.FileManager.FileManagerDirectoryContent targetData, bool isCopy)
        {
            try
            {
                FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> fm = new FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>();
                var lista = new List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>();
                foreach (var item in files)
                {
                    if (!item.IsFile)
                    {
                        var result = await copyAllDirectoryFiles(dbName, item.Id, targetData, targetPath + item.Name + "/");
                        lista.Add(result.toFileManagerContentInCopy());
                        if (!isCopy)
                        {
                            await _db.deleteEntry(dbName, item.Id);
                        }
                    }
                    else
                    {
                        var result = await _db.copyItem(dbName, item.Id, targetData, targetPath, item.IsFile);
                        lista.Add(result.toFileManagerContentInCopy());
                        if (!isCopy)
                        {
                            await _db.deleteEntry(dbName, item.Id);
                        }
                    }
                    await _db.addBytesToFolder(dbName, item.ParentId, item.Size);
                }
                fm.Files = lista;
                return fm;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error on CopyOrMoveItems");
                if (e is MongoWriteException ex)
                {
                    if (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
                    {
                        NotificationModel nm = new NotificationModel();
                        nm.sendEvent(new Notification("The file exist in the directory", "Duplicate", NotificationTypes.Error));
                    }
                }
                throw e;
            }
        }

        private async Task<BsonFileManagerModel> copyAllDirectoryFiles(string dbName, string idfolder, Syncfusion.Blazor.FileManager.FileManagerDirectoryContent target, string targetPath, bool isCopy = true)
        {
            var result = await _db.copyItem(dbName, idfolder, target, targetPath, false);
            var files = await _db.getAllFilesInDirectoryById(dbName, idfolder);

            foreach (var file in files)
            {
                if (file.IsFile)
                {
                    await _db.copyItem(dbName, file.Id, result.toFileManagerContent(), targetPath, file.IsFile);
                    if (!isCopy)
                    {
                        await _db.deleteEntry(dbName, file.Id);
                    }
                }
                else
                {
                    await copyAllDirectoryFiles(dbName, file.Id, result.toFileManagerContent(), targetPath + file.Name + "/");
                    if (!isCopy)
                    {
                        await _db.deleteEntry(dbName, file.Id);
                    }
                }
            }
            if (!isCopy)
            {
                await _db.deleteEntry(dbName, idfolder);
            }
            return result;
        }

        public async Task<MemoryStream> getImage(string dbName, string path, string fileName, MemoryStream ms = null, string? collectionId = null)
        {
            try
            {
                BsonFileManagerModel file = collectionId == null ? _db.getFileByPathSync(dbName, path + fileName) : _db.getFileByPathSync(dbName, path + fileName, collectionId);
                string currentFilePath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), RELATIVELOCALDIR, dbName + path);
                Directory.CreateDirectory(currentFilePath);
                if (file == null)
                    throw new Exception($"{path} does not exist");
                if (file.isSplit)
                {
                    int i = 1;
                    List<string> splitPaths = new List<string>();
                    foreach (int messageId in file.ListMessageId)
                    {
                        string filePathPart = System.IO.Path.Combine(currentFilePath, $"({i})" + fileName);
                        await downloadFromTelegram(dbName, messageId, filePathPart);
                        splitPaths.Add(filePathPart);
                        i++;
                    }
                    await mergeFileStreamAsync(splitPaths, System.IO.Path.Combine(currentFilePath, fileName));
                    foreach (string filePath in splitPaths)
                    {
                        File.Delete(filePath);
                    }
                    using (FileStream ff = new FileStream(System.IO.Path.Combine(currentFilePath, fileName), FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        ms = await ToMemoryStreamAsync(ff);
                    }
                    File.Delete(System.IO.Path.Combine(currentFilePath, fileName));
                    return ms;
                }
                else
                {
                    return await downloadFromTelegramAndReturn(dbName, (int)file.MessageId, System.IO.Path.Combine(currentFilePath, fileName));
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on getImage");
                throw ex;
            }

        }

        public async Task downloadFile(string dbName, string path, List<string> files, string targetPath, string? collectionId = null, string? channelId = null)
        {
            _logger.LogInformation("Starting download - DbName: {DbName}, Path: {Path}, FilesCount: {Count}, TargetPath: {TargetPath}",
                dbName, path, files.Count, targetPath);
            NotificationModel nm = new NotificationModel();
            try
            {

                nm.sendEvent(new Notification("Download Start", "Download", NotificationTypes.Info));
                if (targetPath == null) targetPath = path;
                foreach (string fileName in files)
                {
                    WaitingTime wt = new WaitingTime();
                    BsonFileManagerModel file = collectionId == null ? _db.getFileByPathSync(dbName, path + fileName) : _db.getFileByPathSync(dbName, path + fileName, collectionId);
                    string currentFilePath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), RELATIVETEMPDIR, targetPath[0] == '/' ? targetPath.Substring(1) : targetPath);
                    if (file == null)
                    {
                        Directory.CreateDirectory(System.IO.Path.Combine(currentFilePath, fileName));
                        var filesInDir = collectionId == null ?  await _db.getAllFilesInDirectoryPath(dbName, path) : await _db.getAllFilesInDirectoryPath(dbName, path, collectionId);
                        if (filesInDir.Count() > 0)
                        {
                            await downloadFile(dbName, path.EndsWith(fileName + "/") ? path : System.IO.Path.Combine(path, fileName) + "/", filesInDir.Select(x => x.Name).ToList(), System.IO.Path.Combine(targetPath, fileName), collectionId, channelId);
                        }

                    }
                    else
                    {
                        Directory.CreateDirectory(currentFilePath);
                        if (file == null)
                            throw new Exception($"{targetPath} does not exist");
                        if (file.isSplit)
                        {
                            int i = 1;
                            List<string> splitPaths = new List<string>();
                            foreach (int messageId in file.ListMessageId)
                            {
                                string filePathPart = System.IO.Path.Combine(currentFilePath, $"({i})" + fileName);
                                await downloadFromTelegram(channelId == null ? dbName : channelId, messageId, filePathPart, file);
                                splitPaths.Add(filePathPart);
                                i++;
                            }
                            await mergeFileStreamAsync(splitPaths, System.IO.Path.Combine(currentFilePath, fileName));
                            foreach (string filePath in splitPaths)
                            {
                                File.Delete(filePath);
                            }
                        }
                        else
                        {
                            await downloadFromTelegram(channelId == null ? dbName : channelId, (int)file.MessageId, System.IO.Path.Combine(currentFilePath, fileName), file);
                        }
                        await wt.Sleep();
                    }


                }

                nm.sendEvent(new Notification("downloads have been completed", "Download completed", NotificationTypes.Success));

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading files");
                nm.sendEvent(new Notification("Error at download.", "Download Error", NotificationTypes.Error));
                throw ex;
            }

        }

        public virtual async Task downloadFile(string dbName, List<FileManagerDirectoryContent> files, string targetPath, string? collectionId = null, string? channelId = null)
        {
            NotificationModel nm = new NotificationModel();
            try
            {
                foreach(FileManagerDirectoryContent file in  files)
                {
                    _logger.LogInformation($"Download files to {targetPath} :: {file.Name}");
                }
                
                nm.sendEvent(new Notification("Download Start", "Download", NotificationTypes.Info));
                string currentTargetPath = targetPath == null ? "/" : targetPath;
                // if (targetPath == null) targetPath = "/"; //path.Replace("\\", "/");
                foreach (var itemFile in files)
                {
                    var filterPath = itemFile.FilterPath == "Files/" ? "/" : itemFile.FilterPath;
                    BsonFileManagerModel file = null;
                    if (itemFile.Id != null)
                        file = collectionId == null ? await _db.getFileById(dbName, itemFile.Id) : await _db.getFileById(dbName, itemFile.Id, collectionId);
                    else
                        file = collectionId == null ? _db.getFileByPathSync(dbName, filterPath.Replace("\\", "/") + itemFile.Name) : _db.getFileByPathSync(dbName, filterPath.Replace("\\", "/") + itemFile.Name, collectionId);
                    string currentFilePath = currentTargetPath;
                    if (!itemFile.IsFile)
                    {
                        Directory.CreateDirectory(System.IO.Path.Combine(currentFilePath, itemFile.Name));
                        var filesInDir = collectionId == null ? await _db.getAllFilesInDirectoryPath(dbName, itemFile.FilterPath == "" ? "/" : itemFile.FilterPath + itemFile.Name + "/") : await _db.getAllFilesInDirectoryPath(dbName, itemFile.FilterPath == "" ?  "/" : itemFile.FilterPath + itemFile.Name + "/", collectionId);
                        if (filesInDir.Count() > 0)
                        {
                            await downloadFile(dbName, filesInDir.Select(x => x.toFileManagerContent()).ToList(), System.IO.Path.Combine(currentTargetPath, itemFile.Name).Replace("\\", "/"), collectionId, channelId);
                        }

                    }
                    else
                    {
                        Directory.CreateDirectory(currentFilePath);
                        if (file == null)
                            throw new Exception($"{currentTargetPath} does not exist");
                        if (file.isSplit)
                        {
                            downloadSplitFiles(itemFile, file, currentFilePath, channelId == null ? dbName : channelId);
                        }
                        else
                        {
                            await downloadFromTelegram(channelId == null ? dbName : channelId, (int)file.MessageId, System.IO.Path.Combine(currentFilePath, itemFile.Name), file);
                        }
                    }


                }

                nm.sendEvent(new Notification("downloads have been completed", "Download completed", NotificationTypes.Success));

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on downloadFile");
                nm.sendEvent(new Notification("Error at download.", "Download Error", NotificationTypes.Error));
                throw ex;
            }

        }

        public virtual async Task downloadSplitFiles(FileManagerDirectoryContent itemFile, BsonFileManagerModel file, string currentFilePath, string dbName)
        {
            int i = 1;
            List<string> splitPaths = new List<string>();
            foreach (int messageId in file.ListMessageId)
            {
                string filePathPart = Path.Combine(currentFilePath, $"({i})" + itemFile.Name);
                await downloadFromTelegram(dbName, messageId, filePathPart, file, true, Path.Combine(currentFilePath, itemFile.Name));
                splitPaths.Add(filePathPart);
                i++;
            }
            await mergeFileStreamAsync(splitPaths, Path.Combine(currentFilePath, itemFile.Name));
            foreach (string filePath in splitPaths)
            {
                File.Delete(filePath);
            }
        }

        private async Task<MemoryStream> downloadFromTelegramAndReturn(string dbName, int messageId, string destPath, MemoryStream ms = null)
        {
            TL.Message m = await _ts.getMessageFile(dbName, messageId);
            ChatMessages cm = new ChatMessages();
            cm.message = m;

            cm.user = null;
            cm.isDocument = false;
            if (m.media is MessageMediaDocument { document: Document document })
            {
                cm.isDocument = true;
            }
            if (ms == null)
            {
                ms = new MemoryStream();
            }
            await _ts.DownloadFileAndReturn(cm, ms: ms);
            ms.Position = 0;
            return ms;
        }

        public static void functionCalll(string dbName, int messageId, string destPath)
        {

        }
        public virtual async Task downloadFromTelegram(string dbName, int messageId, string destPath, BsonFileManagerModel file = null, bool shouldWait = false, string path = null)
        {
            _logger.LogDebug("Queueing download from Telegram - DbName: {DbName}, MessageId: {MessageId}, DestPath: {DestPath}",
                dbName, messageId, destPath);
            DownloadModel model = new DownloadModel();
            model.path = path ?? destPath;
            model.tis = _tis;
            if (file != null)
            {
                model.name = file.Name;
                model._size = file.Size;
            }
            model._transmitted = 0;
            model.callbacks = new Callbacks();
            try
            {
                model.channelName = _ts.getChatName(Convert.ToInt64(dbName));
            } catch(Exception ex) {
                model.channelName = "Public or Shared";
            }
            var tcs = new TaskCompletionSource<bool>();

            EventHandler handler = null;
            handler = (sender, args) =>
            {
                if (model.state == StateTask.Completed)
                {
                    model.EventStatechanged -= handler;
                    tcs.SetResult(true);
                }
            };

            model.EventStatechanged += handler;
            model.callbacks.callback = async () => await DownloadFileNow(dbName, messageId, destPath, model);
            _tis.addToPendingDownloadList(model, atFirst: shouldWait);
            // Espera hasta que el estado sea "Completed"
            if (shouldWait)
                await tcs.Task;
        }

        public async Task DownloadFileFromChat(ChatMessages message, string fileName = null, string folder = null, DownloadModel model = null)
        {
            if (model == null)
            {
                model = new DownloadModel();
                model.tis = _tis;
            }
                
            model.name = fileName;
            if (message.message.media is MessageMediaDocument { document: Document document })
            {
                model._size = document.size;
            }
            if (message.user is TL.Channel channel)
            {
                model.channelName = channel.Title;
            }
            model._transmitted = 0;
            model.channel = message.user;
            //model.channelName = message.user.;
            model.callbacks = new Callbacks();

            model.callbacks.callback = async () => await _ts.DownloadFile(message,fileName, folder, model);
            _tis.addToPendingDownloadList(model);
        }

        public async Task DownloadFileNow(string dbName, int messageId, string destPath, DownloadModel model)
        {
            TL.Message m = await _ts.getMessageFile(dbName, messageId);
            ChatMessages cm = new ChatMessages();
            cm.message = m;
            model.startDate = DateTime.Now;
            cm.user = null;
            cm.isDocument = false;
            if (m.media is MessageMediaDocument { document: Document document })
            {
                cm.isDocument = true;
            }
            using (FileStream fs = new FileStream(destPath, FileMode.Create))
                await _ts.DownloadFileAndReturn(cm, ms: fs, model: model);
        }

        public async Task downloadFileToServer(string dbName, string path, string destPath)
        {
            try
            {
                BsonFileManagerModel file = _db.getFileByPathSync(dbName, path);
                if (file == null)
                    throw new Exception($"{path} does not exist");
                TL.Message m = await _ts.getMessageFile(dbName, (int)file.MessageId);
                ChatMessages cm = new ChatMessages();
                cm.message = m;

                cm.user = null;
                cm.isDocument = false;
                if (m.media is MessageMediaDocument { document: Document document })
                {
                    cm.isDocument = true;
                }
                using (FileStream fs = new FileStream(destPath, FileMode.Create))
                    await _ts.DownloadFileAndReturn(cm, ms: fs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on downloadFileToServer");
                throw ex;
            }

        }

        public async Task importData(string dbName, string path, GenericNotificationProgressModel gnp)
        {
            try
            {
                await _db.resetDatabase(dbName);
                using (StreamReader r = new StreamReader(path))
                {
                    string json = r.ReadToEnd();
                    List<BsonFileManagerModel> items = JsonConvert.DeserializeObject<List<BsonFileManagerModel>>(json);
                    int total = items.Count();
                    int completed = 0;
                    gnp.sendMessage(total, completed);
                    foreach (BsonFileManagerModel item in items)
                    {
                        //BsonFileManagerModel prev = await _db.getFileById(dbName, item.Id);
                        //if (prev != null)
                        //{
                        //    if (prev.DateModified < item.DateModified || string.IsNullOrEmpty(prev.FilePath))
                        //    {
                        //        await _db.deleteEntry(dbName, item.Id);
                        //        await _db.createEntry(dbName, item);
                        //        continue;
                        //    }
                        //} else
                        //{
                        //    await _db.createEntry(dbName, item);
                        //}
                        await _db.createEntry(dbName, item);
                        gnp.sendMessage(total, ++completed);

                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on importData");
                throw ex;
            }

        }

        public async Task importSharedData(ShareFilesModel sfm, GenericNotificationProgressModel gnp)
        {

            try
            {
                if (!_ts.isInChat(Convert.ToInt64(sfm.id)) && sfm.invitation != null && !string.IsNullOrEmpty(sfm.invitation.invitationHash))
                    try
                    {
                        await _ts.joinChatInvitationHash(sfm.invitation.invitationHash);
                        _toastService.Notify(new(ToastType.Success, "Join chat", $"Joined to chat"));
                    }
                    catch (Exception ex)
                    {
                        _toastService.Notify(new(ToastType.Danger, $"Error: {ex.Message}."));
                    }
                else
                    _toastService.Notify(new(ToastType.Info, "You were already joined the chat"));
                    
                BsonSharedInfoModel bsi = new BsonSharedInfoModel();
                bsi.ChannelId = sfm.id;
                bsi.Name = sfm.name;
                bsi.Description = sfm.description;
                bsi.CollectionId = Guid.NewGuid().ToString();
                string dbName = DbService.SHARED_DB_NAME;
                await _db.InsertSharedInfo(bsi, dbName);
                await _db.resetCollection(dbName, bsi.CollectionId);

                int total = sfm.files.Count();
                int completed = 0;
                gnp.sendMessage(total, completed);
                foreach (BsonFileManagerModel item in sfm.files)
                {
                    await _db.createEntry(dbName, item, bsi.CollectionId);
                    gnp.sendMessage(total, ++completed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on importSharedData");
                throw ex;
            }
   
            

        }

        public static async Task<T?> UnZipArchiveToFile<T>(Stream ms)
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    using (var stream = entry.Open())
                    {
                        var sr = new StreamReader(stream);
                        string json = sr.ReadToEnd();
                        return JsonConvert.DeserializeObject<T>(json);
                    }
                }
            }
            return default(T);
        }

        public async Task<List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>> createFolder(string dbName, FolderCreateEventArgs<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> args)
        {
            return (await _db.createEntry(dbName, await _db.toBasonFile(args.Path, args.FolderName, args.ParentFolder))).Select(x => x.toFileManagerContent()).ToList();
        }

        public async Task<List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>> createFolder(string dbName, string path, string folderName, Syncfusion.Blazor.FileManager.FileManagerDirectoryContent? parentFolder)
        {
            return (await _db.createEntry(dbName, await _db.toBasonFile(path, folderName, parentFolder))).Select(x => x.toFileManagerContent()).ToList();
        }

        public async Task CreateDatabase(string id)
        {
            if (_ts.checkChannelExist(id))
                await _db.CreateDatabase(id);
            else
                throw new Exception($"Channel {id} does not exist");
        }

        //public async Task UploadFileFromServer(string dbName, string currentPath, string filePath) // ItemsUploadedEventArgs<FileManagerDirectoryContent> args)
        //{
        //    // string currentPath = args.Path;
        //    try
        //    {
        //        //foreach (var file in args.Files)
        //        //{

        //        //using (var filestream = new FileStream(Path.Combine(Environment.CurrentDirectory, "download","assss.jpg"), FileMode.Create, FileAccess.Write))
        //        //{
        //        //    await file.File.OpenReadStream(maxAllowedSize: long.MaxValue).CopyToAsync(filestream);
        //        //}
        //        //MemoryStream ms = new MemoryStream(file.File.OpenReadStream(maxAllowedSize: long.MaxValue));
        //        //await file.File.OpenReadStream().CopyToAsync(ms);

        //        string currentFilePath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "workingDir", dbName + currentPath);
        //        await SaveToFile(file.File, currentFilePath);


        //        BsonFileManagerModel model = new BsonFileManagerModel();
        //        model.Size = file.File.Size;
        //        Message m = null;
        //        if (file.File.Size > MaxSize)
        //        {

        //            List<string> files = await splitFileStreamAsync(currentFilePath, file.File.Name, MaxSize);
        //            File.Delete(System.IO.Path.Combine(currentFilePath, file.File.Name));
        //            int i = 1;
        //            model.ListMessageId = new List<int>();
        //            model.isSplit = true;
        //            foreach (string s in files)
        //            {
        //                using (FileStream fs = new FileStream(s, FileMode.Open, FileAccess.Read, FileShare.Read))
        //                    m = await _ts.uploadFile(dbName, fs, $"({i} of {files.Count}) - " + file.File.Name);
        //                model.ListMessageId.Add(m.ID);
        //                File.Delete(s);
        //                i++;
        //            }

        //        }
        //        else
        //        {
        //            using (FileStream ms = new FileStream(System.IO.Path.Combine(currentFilePath, file.File.Name), FileMode.Open))
        //            {
        //                m = await _ts.uploadFile(dbName, ms, file.File.Name);
        //            }
        //            model.MessageId = m.ID;
        //        }

        //        BsonFileManagerModel parent = await _db.getParentDirectoryByPath(dbName, currentPath);
        //        model.Name = file.File.Name;
        //        model.IsFile = true;
        //        model.HasChild = false;
        //        model.DateCreated = DateTime.Now;
        //        model.DateModified = DateTime.Now;
        //        model.FilterPath = string.Concat(parent.FilterPath, parent.Name, "/");
        //        model.FilterId = string.Concat(parent.FilterId, parent.Id.ToString(), "/");
        //        model.ParentId = parent.Id;
        //        model.FilePath = System.IO.Path.Combine(currentPath, file.File.Name);
        //        model.Type = file.File.Name.Split(".").LastOrDefault() != null ? "." + file.File.Name.Split(".").LastOrDefault() : file.File.ContentType;

        //        await _db.createEntry(dbName, model);

        //        GC.Collect();




        //        // var folders = (file.FileInfo.Name).Split('/');
        //        // if (folders.Length > 1)
        //        // {
        //        //     for (var i = 0; i < folders.Length - 1; i++)
        //        //     {
        //        //         string newDirectoryPath = Path.Combine(FileManagerService.basePath + currentPath, folders[i]);
        //        //         if (Path.GetFullPath(newDirectoryPath) != (Path.GetDirectoryName(newDirectoryPath) + Path.DirectorySeparatorChar + folders[i]))
        //        //         {
        //        //             throw new UnauthorizedAccessException("Access denied for Directory-traversal");
        //        //         }
        //        //         if (!Directory.Exists(newDirectoryPath))
        //        //         {
        //        //             await FileManagerService.Create(currentPath, folders[i]);
        //        //         }
        //        //         currentPath += folders[i] + "/";
        //        //     }
        //        // }
        //        // var fullName = Path.Combine((FileManagerService.contentRootPath + currentPath), file.File.Name);
        //        // using (var filestream = new FileStream(fullName, FileMode.Create, FileAccess.Write))
        //        // {
        //        //     await file.File.OpenReadStream(long.MaxValue).CopyToAsync(filestream);
        //        // }
        //        //}
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine(ex.Message);
        //        throw ex;
        //    }
        //}

        public async Task AddUploadFileFromServer(string dbName, string currentPath, List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> files, InfoDownloadTaksModel idt = null) // ItemsUploadedEventArgs<FileManagerDirectoryContent> args)
        {
            _logger.LogInformation("Adding upload task from server - DbName: {DbName}, Path: {Path}, FilesCount: {Count}",
                dbName, currentPath, files.Count);
            idt = new InfoDownloadTaksModel();
            idt.tis = _tis;
            idt.id = Guid.NewGuid().ToString();
            idt.total = 0;
            idt.totalSize = 0;
            idt.isUpload = true;
            idt.toPath = currentPath;
            idt.executed = 0;
            idt.executedSize = 0;
            idt.isUpload = true;
            idt.files = files;
            
            foreach (var file in files)
            {
                var filePath = file.IsFile ? file.FilterPath.Replace("\\", "/") + file.Name : file.FilterPath.Replace("\\", "/") + file.Name + "/";
                string currentFilePath = System.IO.Path.Combine(LOCALDIR, filePath[0] == '/' ? filePath.Substring(1) : filePath).Replace("\\", "/");

                var fileInfo = new System.IO.FileInfo(currentFilePath);
                if (!file.IsFile)
                {
                    if (file.Name == "@eaDir") continue;
                    var allFiles = new DirectoryInfo(currentFilePath).GetFiles("*.*", SearchOption.AllDirectories).Where(x => !x.Attributes.HasFlag(FileAttributes.Hidden) && !x.Attributes.HasFlag(FileAttributes.Directory) && !x.FullName.Contains("@eaDir") && x.Length > 0);
                    idt.total += allFiles.Count();
                    idt.totalSize += allFiles.Sum(x => x.Length);
                }
                else
                {
                    if (file.Size > 0)
                    {
                        idt.total++;
                        idt.totalSize += fileInfo.Length;
                    }
                    
                }

            }
            idt.callbacks = new Callbacks();
            idt.callbacks.callback = async () => await UploadFileFromServer(dbName, currentPath, files, idt);
            _tis.addToInfoDownloadTaskList(idt);
            _tis.CheckPendingUploadInfoTasks();
        }

        public async Task<String> CreateStrmFiles(string path, string dbName, string host)
        {
            String folderPathName = Path.GetFileName(path.TrimEnd('/'));
            String strmPath = Path.Combine(TEMPDIR, "Strm");


            DateTime limite = DateTime.Now.AddHours(-1);

            // Eliminar ficheros
            foreach (string fichero in Directory.GetFiles(strmPath))
            {
                DateTime creacion = File.GetCreationTime(fichero);
                if (creacion <= limite)
                {
                    File.Delete(fichero);
                    Console.WriteLine($"Archivo eliminado: {fichero}");
                }
            }

            // Eliminar carpetas
            foreach (string carpeta in Directory.GetDirectories(strmPath))
            {
                DateTime creacion = Directory.GetCreationTime(carpeta);
                if (creacion <= limite)
                {
                    Directory.Delete(carpeta, true); // true elimina recursivamente
                    Console.WriteLine($"Carpeta eliminada: {carpeta}");
                }
            }

            String basePath = Path.Combine(TEMPDIR, "Strm", dbName, folderPathName);
            try
            {
                Directory.Delete(basePath, true);
            }
            catch (Exception ex)
            {
            }
            
            Directory.CreateDirectory(basePath);
            List<BsonFileManagerModel> filesAndFolders = await _db.getAllChildFilesInDirectory(dbName, path);
            foreach (BsonFileManagerModel file in filesAndFolders)
            {
                String filePath = Path.Combine(basePath, file.FilePath.Substring(path.Length));
                if (file.IsFile)
                {
                    if (FileExtensionTypeTest.isVideoExtension(file.Type) || FileExtensionTypeTest.isAudioExtension(file.Type))
                    {
                        string contenido = Path.Combine(host, "api/file/GetFileStream", dbName, file.Id, "file" + file.Type).Replace("\\", "/");
                        if (GeneralConfigStatic.config.PreloadFilesOnStream || HelperService.bytesToMegaBytes(file.Size) < GeneralConfigStatic.config.MaxPreloadFileSizeInMb)
                        {
                            contenido = Path.Combine(host, "api/file/GetFileByTfmId", Uri.EscapeDataString(file.Name)).Replace("\\", "/") + $"?idChannel={dbName}&idFile={file.Id}";
                        }
                        string pattern = $@"\.({file.Type.Replace(".", "")})$";
                        File.WriteAllText(Regex.Replace(filePath, pattern, ".strm"), contenido);
                    }
                } else
                {
                    Directory.CreateDirectory(filePath);
                } 
            }

            string zipPath = $"{basePath}.zip";

            File.Delete(zipPath);
            ZipFile.CreateFromDirectory(basePath, zipPath, CompressionLevel.Fastest, true);
            Directory.Delete(basePath, true);
            return zipPath;
        }
        public async Task UploadFileFromServer(string dbName, string currentPath, List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> files, InfoDownloadTaksModel dm = null) // ItemsUploadedEventArgs<FileManagerDirectoryContent> args)
        {
            _logger.LogInformation("Starting upload from server - DbName: {DbName}, Path: {Path}, FilesCount: {Count}",
                dbName, currentPath, files.Count);
            // string currentPath = args.Path;
            NotificationModel nm = new NotificationModel();
            InfoDownloadTaksModel idt = dm;

            try
            {
                nm.sendEvent(new Notification($"Uploading files from folder {currentPath} to Telegram", "Telegram Upload", NotificationTypes.Info));
                foreach (var file in files)
                {

                    WaitingTime wt = new WaitingTime();
                    var folderPath = System.IO.Path.Combine(LOCALDIR, file.IsFile ? file.FilterPath.Replace("\\", "/").Substring(1) : file.FilterPath.Replace("\\", "/").Substring(1));
                    var filePath = file.IsFile ? file.FilterPath.Replace("\\", "/") + file.Name : file.FilterPath.Replace("\\", "/") + file.Name + "/";
                    string currentFilePath = System.IO.Path.Combine(LOCALDIR, filePath[0] == '/' ? filePath.Substring(1) : filePath).Replace("\\", "/");
                    var fileInfo = new System.IO.FileInfo(currentFilePath);


                    BsonFileManagerModel model = new BsonFileManagerModel();
                    if (file.IsFile)
                    {
                        if (idt.executed > idt.currentUpload)
                        {
                            idt.currentUpload++;
                            continue;
                        }
                        idt.currentUpload++;
                        BsonFileManagerModel savedFile = await _db.getFileByPath(dbName, System.IO.Path.Combine(currentPath, file.Name));
                        if (file.Size == 0 || (savedFile != null && savedFile.DateModified > file.DateModified))
                        {
                            if (idt != null) idt.AddOne(file.Size);
                            continue;
                        };
                        model.Size = fileInfo.Length;
                        if (GeneralConfigStatic.config.CheckHash)
                        {
                            _logger.LogInformation($"Calculating HASH of file {currentFilePath}");
                            model.XXHash = ComputeXXHash64(currentFilePath);
                            _logger.LogInformation($"Calculated HASH of file {currentFilePath}: {model.XXHash}");
                        }
                        TL.Message m = null;
                        long max = (long)MaxSize * (long)TelegramService.splitSizeGB;
                        if (fileInfo.Length > max)
                        {

                            List<string> filesSplit = await splitFileStreamAsync(folderPath, file.Name, MaxSize);
                            // File.Delete(System.IO.Path.Combine(folderPath, file.Name));
                            int i = 1;
                            model.ListMessageId = new List<int>();
                            model.isSplit = true;
                            foreach (string s in filesSplit)
                            {
                                int attempts = 3;
                                int waitForNextAttempt = 1000;
                                UploadModel um = new UploadModel();
                                um.tis = _tis;
                                um.startDate = DateTime.Now;
                                um.path = currentFilePath;
                                um.chatName = _ts.getChatName(Convert.ToInt64(dbName));
                                // add upload to task list
                                idt.addUpload(um);
                                while (attempts != 0 || um.state == StateTask.Canceled)
                                    try
                                    {
                                        wt = new WaitingTime();
                                        using (FileStream fs = new FileStream(s, FileMode.Open, FileAccess.Read, FileShare.Read))
                                            m = await _ts.uploadFile(dbName, fs, $"({i} of {filesSplit.Count}) - " + file.Name, um: um, caption: getCaption(filePath));
                                        attempts = 0;
                                        await wt.Sleep(); // sleep 1 second to avoid 420 flood_wait_x
                                    }
                                    catch (Exception e)
                                    {
                                        if (e.InnerException is ThreadInterruptedException)
                                        {
                                            _logger.LogInformation(e, "Current work cancelled");
                                            throw e;
                                        }
                                        _logger.LogError(e, "Exception sending file to Telegram - FileName: {FileName}, Attempt: {Attempt}, Remaining: {Remaining}",
                                            file.Name, 4 - attempts, attempts - 1);
                                        attempts--;
                                        // waitForNextAttempt *= 2;
                                        if (attempts == 0 || um.state == StateTask.Canceled)
                                        {
                                            if (um.state == StateTask.Canceled)
                                            {
                                                um.SendNotification();
                                                return;
                                            }
                                            um.state = StateTask.Error;
                                            throw e;
                                        }
                                        _logger.LogWarning("Retrying upload in {DelayMs}ms - FileName: {FileName}", waitForNextAttempt, file.Name);
                                        await Task.Delay(waitForNextAttempt);
                                    }

                                model.ListMessageId.Add(m.ID);
                                File.Delete(s);
                                i++;
                            }

                        }
                        else
                        {

                            int attempts = 3;
                            int waitForNextAttempt = 60000;
                            UploadModel um = new UploadModel();
                            um.tis = _tis;
                            um.path = currentFilePath;
                            um.chatName = _ts.getChatName(Convert.ToInt64(dbName));
                            // add upload to task list
                            idt.addUpload(um);
                            um.startDate = DateTime.Now;
                            while (attempts != 0 || um.state == StateTask.Canceled)
                                try
                                {

                                    try
                                    {
                                        wt = new WaitingTime();
                                        using (FileStream ms = new FileStream(System.IO.Path.Combine(currentFilePath), FileMode.Open))
                                            if (ImageExtensions.Any(x => file.Name.ToUpper().EndsWith(x)) && file.Size >= (1024 * 1024 * GeneralConfigStatic.config.MaxImageUploadSizeInMb))
                                            {
                                                m = await _ts.uploadFile(dbName, ms, file.Name, "application/octet-stream", um, caption: getCaption(filePath));
                                            }
                                            else
                                                m = await _ts.uploadFile(dbName, ms, file.Name, um: um, caption: getCaption(filePath));
                                    }
                                    catch (Exception ex)
                                    {
                                        if (new List<string> { "IMAGE", "PHOTO" }.Any(x => ex.Message.Contains(x)))
                                        {
                                            using (FileStream ms = new FileStream(System.IO.Path.Combine(currentFilePath), FileMode.Open))
                                                m = await _ts.uploadFile(dbName, ms, file.Name, "application/octet-stream", um, caption: getCaption(filePath));
                                        }
                                        else
                                        {
                                            throw ex;
                                        }
                                    }

                                    attempts = 0;
                                    await wt.Sleep(); // sleep 1 second to avoid 420 flood_wait_x
                                }
                                catch (Exception e)
                                {
                                    if (e.InnerException is ThreadInterruptedException)
                                    {
                                        _logger.LogInformation(e, "Current work cancelled");
                                        throw e;
                                    }
                                    _logger.LogError(e, "Exception sending file to Telegram - FileName: {FileName}, Attempt: {Attempt}, Remaining: {Remaining}",
                                        file.Name, 4 - attempts, attempts - 1);
                                    attempts--;
                                    // waitForNextAttempt *= 2;
                                    if (attempts == 0 || um.state == StateTask.Canceled)
                                    {
                                        if (um.state == StateTask.Canceled)
                                        {
                                            um.SendNotification();
                                            return;
                                        }

                                        um.state = StateTask.Error;
                                        throw e;
                                    }
                                    _logger.LogWarning("Retrying upload in {DelayMs}ms - FileName: {FileName}", waitForNextAttempt, file.Name);
                                    await Task.Delay(waitForNextAttempt);
                                }

                            model.MessageId = m.ID;
                        }
                        // delete previous file, when the new modified date file is newest
                        if (savedFile != null)
                        {
                            await itemDeleteAsync(dbName, savedFile.FilterPath, savedFile.Name, savedFile.ParentId, savedFile.FilePath);
                        }
                    }
                    BsonFileManagerModel parent = await _db.getParentDirectoryByPath(dbName, currentPath);
                    model.Name = file.IsFile ? fileInfo.Name : file.Name;
                    model.IsFile = file.IsFile;
                    model.HasChild = false;
                    model.DateCreated = DateTime.Now;
                    model.DateModified = DateTime.Now;
                    model.FilterPath = (currentPath == "/" || currentPath == "File/") ? currentPath : string.Concat(parent.FilterPath, parent.Name, "/");
                    model.FilterId = string.Concat(parent.FilterId, parent.Id.ToString(), "/");
                    model.ParentId = parent.Id;
                    model.FilePath = System.IO.Path.Combine(currentPath, file.IsFile ? fileInfo.Name : file.Name);
                    model.Type = file.IsFile ? (fileInfo.Name.Split(".").LastOrDefault() != null ? "." + fileInfo.Name.Split(".").LastOrDefault() : GetMimeType(fileInfo.Name)) : "folder";
                    // if file or folder does not exist, it will be created
                    if ((await _db.getFileByPath(dbName, System.IO.Path.Combine(currentPath, file.Name))) == null)
                        await _db.createEntry(dbName, model);
                    if (!parent.HasChild)
                        await _db.setDirectoryHasChild(dbName, parent.Id);
                    if (file.IsFile)
                    {
                        await _db.addBytesToFolder(dbName, model.ParentId, model.Size);
                        if (idt != null) idt.AddOne(file.Size);
                    }
                    else
                    {
                        await getFilesInDirectory(dbName, model.FilePath + "/", filePath, idt);
                    }


                    GC.Collect();
                }

                if (idt.files == files)
                {
                    idt.markAsCompleted();
                }

                nm.sendEvent(new Notification($"Upload files completed in {currentPath}", "Telegram Upload", NotificationTypes.Success));
            }
            catch (Exception ex)
            {
                if (ex.InnerException is ThreadInterruptedException)
                {
                    return;
                }
                idt.state = StateTask.Error;
                _logger.LogError(ex, "Error on uploadFileFromServer - Path: {Path}, Message: {Message}", currentPath, ex.Message);
                nm.sendEvent(new Notification("Error Uploading files to Telegram", "Telegram Upload", NotificationTypes.Error));
                throw ex;
            }
        }

        private async Task getFilesInDirectory(string dbName, string currentPath, string path, InfoDownloadTaksModel dm = null)
        {
            var operation = new PhysicalFileProvider();
            operation.RootFolder(Path.Combine(Environment.CurrentDirectory, FileService.RELATIVELOCALDIR));
            var files = operation.GetFiles(path, false);
            List<Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent> fileContents = new List<Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent>();
            foreach (var f in files.Files)
            {
                if (!f.IsFile && f.Name == "@eaDir") continue;
                fileContents.Add(f);
            }
            files.Files = fileContents;
            await UploadFileFromServer(dbName, currentPath, converToFMD(files.Files), dm);
        }

        private List<FileManagerDirectoryContent> converToFMD(IEnumerable<Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent> liste2j)
        {
            List<FileManagerDirectoryContent> files = new List<FileManagerDirectoryContent>();
            foreach (var e2j in liste2j)
            {
                FileManagerDirectoryContent file = new FileManagerDirectoryContent();

                file.Action = e2j.Action;
                file.Name = e2j.Name;
                file.Path = e2j.Path;
                file.FilterPath = e2j.FilterPath;
                file.Path = e2j.Path;
                file.TargetPath = e2j.TargetPath;
                file.HasChild = e2j.HasChild;
                file.CaseSensitive = e2j.CaseSensitive;
                file.DateCreated = e2j.DateCreated;
                file.DateModified = e2j.DateModified;
                file.FilterId = e2j.FilterId;
                file.IsFile = e2j.IsFile;
                file.NewName = e2j.NewName;
                file.ParentId = e2j.ParentId;
                file.PreviousName = e2j.PreviousName;
                file.ShowHiddenItems = e2j.ShowHiddenItems;
                file.Size = e2j.Size;
                files.Add(file);
            }
            return files;
        }

        private string GetMimeType(string fileName)
        {
            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(fileName, out var contentType))
            {
                contentType = "application/octet-stream";
            }
            return contentType;
        }

        public async Task refreshChannelFIles(string channelId, bool force = false)
        {
            int totalNewMessages = 0;
            refreshMutex.WaitOne();
            refreshChannelList.Add(channelId);
            refreshMutex.ReleaseMutex();
            DateTime init = DateTime.Now;
            List<int> presentIds = await _db.getAllIdsFromChannel(channelId);
            _logger.LogInformation($"Refresh channel with id: {channelId}");
            List<TelegramChatDocuments> telegramChatDocuments = (await _ts.searchAllChannelFiles(Convert.ToInt64(channelId), (presentIds.Count > 0 && !force) ? presentIds.Max() : 0)).Where(x => x.name != null).ToList();
            _logger.LogInformation($"Get the telegram files in: {(DateTime.Now - init).TotalSeconds} seconds  for channel id:{channelId}");
            List<string> fileNames = await _db.getAllFileNamesFromChannel(channelId);
            var nameCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (string name in fileNames)
            {
                nameCount[name] = 1;
            }

            foreach (var doc in telegramChatDocuments)
            {
                var baseName = doc.name;
                var name = baseName;
                int count = 0;

                while (nameCount.ContainsKey(name))
                {
                    count++;
                    name = $"{baseName}({count})";
                }

                doc.name = name;
                nameCount[name] = 1;
                if (count == 0)
                    nameCount[baseName] = 1;
            }
            
            var rootFolder = await _db.getRootFolder(channelId);
            foreach (TelegramChatDocuments tcd in telegramChatDocuments)
            {
                if (!presentIds.Contains(tcd.id))
                {
                    BsonFileManagerModel model = new BsonFileManagerModel();
                    model.Size = tcd.fileSize;
                    model.MessageId = tcd.id;
                    model.Name = tcd.name;
                    model.IsFile = true;
                    model.HasChild = false;
                    model.DateCreated = tcd.creationDate;
                    model.DateModified = tcd.modifiedDate;
                    model.FilterPath = "/";
                    model.FilterId = rootFolder.Id + "/";
                    model.ParentId = rootFolder.Id;
                    model.FilePath = "/" + tcd.name;
                    model.Type = tcd.extension;
                    model.isSplit = false;
                    model.isEncrypted = false;
                    await _db.createEntry(channelId, model);
                    totalNewMessages++;
                }
            }
            refreshMutex.WaitOne();
            refreshChannelList.Remove(channelId);
            refreshMutex.ReleaseMutex();
            _logger.LogInformation($"Finish Refresh channel with id: {channelId} with {totalNewMessages} new files added.");

            // Fix for CS1739: Removed the invalid 'autoHide' parameter and replaced it with the correct property assignment.
            ToastMessage tm = new ToastMessage
            {
                Type = ToastType.Success,
                IconName = IconName.CheckCircle,
                Title = "Refresh channel files",
                Message = $"Files channel has been refreshed with {totalNewMessages} new files added.",
                AutoHide = true
                
            };
            _toastService.Notify(tm);
        }

        public bool isChannelRefreshing(string channelId)
        {
            return refreshChannelList.Contains(channelId);
        }

        public async Task UploadFile(string dbName, string currentPath, UploadFiles file) // ItemsUploadedEventArgs<FileManagerDirectoryContent> args)
        {
            _logger.LogInformation("UploadFile started - DbName: {DbName}, CurrentPath: {CurrentPath}, FileName: {FileName}, FileSize: {FileSize} bytes ({FileSizeMB:F2} MB)",
                dbName, currentPath, file.File.Name, file.File.Size, file.File.Size / 1024.0 / 1024.0);

            try
            {
                string currentFilePath = System.IO.Path.Combine(TEMPDIR, dbName + currentPath);
                _logger.LogDebug("Saving file to temp path: {TempPath}", currentFilePath);

                await SaveToFile(file.File, currentFilePath);
                _logger.LogDebug("File saved to temp successfully");

                BsonFileManagerModel model = new BsonFileManagerModel();
                model.Size = file.File.Size;
                TL.Message m = null;

                if (file.File.Size > MaxSize)
                {
                    _logger.LogInformation("File exceeds MaxSize ({MaxSizeMB:F2} MB), splitting file into chunks", MaxSize / 1024.0 / 1024.0);

                    List<string> files = await splitFileStreamAsync(currentFilePath, file.File.Name, MaxSize);
                    _logger.LogDebug("File split into {ChunkCount} chunks", files.Count);

                    File.Delete(System.IO.Path.Combine(currentFilePath, file.File.Name));
                    int i = 1;
                    model.ListMessageId = new List<int>();
                    model.isSplit = true;

                    foreach (string s in files)
                    {
                        _logger.LogDebug("Uploading chunk {ChunkNumber} of {TotalChunks}: {ChunkPath}", i, files.Count, s);

                        using (FileStream fs = new FileStream(s, FileMode.Open, FileAccess.Read, FileShare.Read))
                            m = await _ts.uploadFile(dbName, fs, $"({i} of {files.Count}) - " + file.File.Name, caption: getCaption(currentPath));

                        model.ListMessageId.Add(m.ID);
                        _logger.LogDebug("Chunk {ChunkNumber} uploaded successfully, MessageId: {MessageId}", i, m.ID);

                        File.Delete(s);
                        i++;
                    }

                    _logger.LogInformation("All {ChunkCount} chunks uploaded successfully", files.Count);
                }
                else
                {
                    _logger.LogDebug("Uploading single file to Telegram");

                    using (FileStream ms = new FileStream(System.IO.Path.Combine(currentFilePath, file.File.Name), FileMode.Open))
                    {
                        m = await _ts.uploadFile(dbName, ms, file.File.Name, caption: getCaption(currentPath));
                    }
                    model.MessageId = m.ID;

                    _logger.LogDebug("File uploaded to Telegram, MessageId: {MessageId}", m.ID);
                }

                BsonFileManagerModel parent = await _db.getParentDirectoryByPath(dbName, currentPath);
                _logger.LogDebug("Parent directory found - ParentId: {ParentId}, ParentName: {ParentName}", parent?.Id, parent?.Name);

                model.Name = file.File.Name;
                model.IsFile = true;
                model.HasChild = false;
                model.DateCreated = DateTime.Now;
                model.DateModified = DateTime.Now;
                model.FilterPath = string.Concat(parent.FilterPath, parent.Name, "/");
                model.FilterId = string.Concat(parent.FilterId, parent.Id.ToString(), "/");
                model.ParentId = parent.Id;
                model.FilePath = System.IO.Path.Combine(currentPath, file.File.Name);
                model.Type = file.File.Name.Split(".").LastOrDefault() != null ? "." + file.File.Name.Split(".").LastOrDefault() : file.File.ContentType;

                await _db.createEntry(dbName, model);
                _logger.LogDebug("Database entry created for file");

                await _db.addBytesToFolder(dbName, model.ParentId, model.Size);
                _logger.LogDebug("Folder size updated");

                GC.Collect();

                _logger.LogInformation("UploadFile completed successfully - FileName: {FileName}, IsSplit: {IsSplit}, MessageId: {MessageId}",
                    file.File.Name, model.isSplit, model.isSplit ? string.Join(",", model.ListMessageId) : model.MessageId.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on uploadFile - FileName: {FileName}, CurrentPath: {CurrentPath}, DbName: {DbName}, Message: {Message}",
                    file.File.Name, currentPath, dbName, ex.Message);
                NotificationModel nm = new NotificationModel();
                nm.sendEvent(new Notification($"Error on UploadFile: {file.File.Name}", "Error", NotificationTypes.Error));
                throw ex;
            }
        }
        public static List<FolderModel> getFolderNodes(string path)
        {
            List<FolderModel> node = new List<FolderModel>();
            if (Directory.Exists(path))
            {
                FolderModel folder = new FolderModel();
                folder.FolderName = Path.GetFileName(path);
                folder.Id = folder.FolderName;
                folder.Expanded = true;
                folder.Folders = getSubforlders(path);
                node.Add(folder);
            }

            return node;
        }

        public static string getMimeType(string extension)
        {
            try
            {
                return MIMETypesDictionary[extension.Replace(".", "")] ?? "application/octet-stream";
            }
            catch (Exception e)
            {
                return "application/octet-stream";
            }

        }

        private static List<FolderModel> getSubforlders(string path)
        {
            List<FolderModel> node = new List<FolderModel>();
            foreach (string item in Directory.GetDirectories(path))
            {
                FolderModel n = new FolderModel();
                n.FolderName = Path.GetFileName(item);
                n.Id = item;
                n.Folders = getSubforlders(item);
                n.Expanded = false;
                node.Add(n);
            }
            return node.Count() > 0 ? node : null;
        }

        public async Task<List<BsonFileManagerModel>> getTelegramFolders(string dbName, string? parentId = null)
        {
            List<FolderModel> node = new List<FolderModel>();
            var result = await _db.getAllFolders(dbName, parentId);
            foreach (var folder in result)
            {
                if (String.IsNullOrEmpty(folder.FilterPath))
                {
                    folder.ParentId = null;
                    folder.Id = "/";
                } else
                {
                    folder.ParentId = folder.FilterPath;
                    folder.Id = folder.FilterPath + folder.Name + "/";
                }
            }
            return result;
        }

        public async Task<List<BsonFileManagerModel>> getTelegramFoldersByParentId(string dbName, string? parentId)
        {
            var result = await _db.getFoldersByParentId(dbName, parentId);

            return result;
        }

        private async Task<List<FolderModel>> getTelegramSubfolders(string id, List<BsonFileManagerModel> listFolders, string prevPath)
        {
            List<FolderModel> node = new List<FolderModel>();
            foreach (var folder in listFolders.Where(x => x.ParentId == id).ToList())
            {
                FolderModel n = new FolderModel();
                n.FolderName = folder.Name;
                n.Id = prevPath + folder.Name + "/";
                n.Folders = await getTelegramSubfolders(folder.Id, listFolders.Where(x => x.ParentId != id).ToList(), n.Id);
                // n.Expanded = false;
                node.Add(n);
            }
            return node.Count() > 0 ? node : null;
        }

        public async Task<List<ExpandoObject>> GetTelegramFoldersExpando(string id, string parentId)
        {
            List<ExpandoObject> Data = new List<ExpandoObject>();
            List<BsonFileManagerModel> folderList = await _db.getAllChildFoldersInDirectory(id, parentId);
            foreach(var folder in folderList)
            {
                dynamic ParentRecord = new ExpandoObject();
                ParentRecord.ID = folder.Id;
                ParentRecord.Name = folder.Name;
                ParentRecord.Path = folder.FilePath + "/";
                ParentRecord.ParentID = string.IsNullOrEmpty(folder.ParentId) ? null : folder.ParentId;
                ParentRecord.Expanded = true;
                Data.Add(ParentRecord);
                Data.AddRange(await AddChildRecords(id, folder.Id));
            }

            return Data;
        }

        private async Task<List<ExpandoObject>> AddChildRecords(string id, string parentId)
        {
            List<ExpandoObject> Data = new List<ExpandoObject>();
            List<BsonFileManagerModel> folderList = await _db.getAllChildFoldersInDirectory(id, parentId);
            foreach (var folder in folderList)
            {
                dynamic ChildRecord = new ExpandoObject();
                ChildRecord.ID = folder.Id;
                ChildRecord.Name = folder.Name;
                ChildRecord.Path = folder.FilePath + "/";
                ChildRecord.ParentID = parentId;
                Data.Add(ChildRecord);
            }
            return Data;
        }

        public async Task<FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>> GetFilesPath(string dbName, string path, List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> fileDetails = null, string? collectionName = null)
        {
            if (fileDetails == null) fileDetails = new List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>();
            FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> response = new FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>();

            if (path == "/")
            {
                List<BsonFileManagerModel> Data = collectionName == null ? await _db.getAllFiles(dbName) : await _db.getAllFiles(dbName, collectionName);
                string ParentId = Data
                    .Where(x => string.IsNullOrEmpty(x.ParentId))
                    .Select(x => x.Id).First();
                response.CWD = Data
                    .Where(x => string.IsNullOrEmpty(x.ParentId)).First().toFileManagerContent();
                response.Files = Data
                    .Where(x => x.ParentId == ParentId).Select(x => x.toFileManagerContent()).ToList();
            }
            else
            {
                List<BsonFileManagerModel> Data = collectionName == null ? await _db.getAllFilesInDirectoryPath2(dbName, path) : await _db.getAllFilesInDirectoryPath2(dbName, path, collectionName);
                var childItem = fileDetails.Count > 0 && fileDetails[0] != null ? fileDetails[0] : Data
                .Where(x => x.FilePath + "/" == path).First().toFileManagerContent();
                response.CWD = childItem;

                // First try to find children in the query result (by ParentId)
                var files = Data.Where(x => x.ParentId == childItem.Id).ToList();

                // If no children found in query result, do a separate query by ParentId
                // This handles the case where children's FilterPath format doesn't match the query path
                if (files.Count == 0 && !string.IsNullOrEmpty(childItem.Id))
                {
                    files = collectionName == null
                        ? await _db.getFilesByParentId(dbName, childItem.Id)
                        : await _db.getFilesByParentId(dbName, childItem.Id, collectionName);
                }

                response.Files = files.Select(x => x.toFileManagerContent()).ToList();
            }
            await Task.Yield();
            return response;
        }

        private string? getCaption(string filePath)
        {
            return GeneralConfigStatic.config.ShouldShowCaptionPath ? filePath : null;
        }

        private async Task<MemoryStream> ToMemoryStreamAsync(Stream stream)
        {
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }

        private string GetMd5HashFromFile(string filePath)
        {
            Md5Model _md5 = new Md5Model();
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    _tis.addToUploadList(_md5);
                    _md5.Init(stream.Length, Path.GetFileName(filePath));
                    byte[] hash = md5.ComputeHash(stream);
                    _md5.Finish();
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        private string ComputeXXHash64(string filePath)
        {
            XxHashModel _xxHash = new XxHashModel();
            _xxHash.path = filePath;
            var hashAlgorithm = new XxHash64();
            var buffer = new Span<byte>(new byte[512]);
            using (Stream entryStream = File.OpenRead(filePath))
            {
                _tis.addToUploadList(_xxHash);
                _xxHash.Init(entryStream.Length, Path.GetFileName(filePath));
                while (entryStream.Read(buffer) > 0)
                {
                    hashAlgorithm.Append(buffer);
                }
            }

            return BitConverter.ToString(hashAlgorithm.GetHashAndReset()).Replace("-", string.Empty);

        }

        private async Task<List<Stream>> splitFileAsync(Stream s, int size)
        {
            List<Stream> ls = new List<Stream>();
            byte[] buffer = new byte[size];
            int index = 0;
            while (s.Position < s.Length)
            {
                int readBytes = await s.ReadAsync(buffer, 0, Convert.ToInt32(Math.Min(size, s.Length - s.Position)));
                index += size;
                ls.Add(await ToMemoryStreamAsync(new MemoryStream(buffer)));
            }
            return ls;
        }

        private async Task SaveToFile(IBrowserFile file, string path)
        {
            Directory.CreateDirectory(path);
            using (FileStream fs = new FileStream(System.IO.Path.Combine(path, file.Name), FileMode.Create))
                await file.OpenReadStream(long.MaxValue).CopyToAsync(fs);

        }

        private async Task<List<string>> splitFileStreamAsync(string path, string name, int splitSize)
        {
            _logger.LogInformation("Starting file split - FileName: {FileName}, SizeMB: {SizeMB:F2}, SplitSizeMB: {SplitSizeMB}",
                name, new System.IO.FileInfo(System.IO.Path.Combine(path, name)).Length / (1024.0 * 1024.0), splitSize / (1024 * 1024));
            int _gb = TelegramService.splitSizeGB;
            List<string> listPath = new List<string>();
            byte[] buffer = new byte[splitSize];
            using (FileStream fs = new FileStream(System.IO.Path.Combine(path, name), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                SplitModel sm = new SplitModel();
                sm.name = name;
                sm._size = fs.Length;
                sm._transmitted = 0;
                _tis.addToUploadList(sm);
                int index = 1;
                while (fs.Position < fs.Length)
                {
                    string partPath = System.IO.Path.Combine(path, $"({index})" + name);
                    if (File.Exists(partPath))
                    {
                        File.Delete(partPath);
                    }
                    int gb = _gb;
                    while (fs.Position < fs.Length && gb > 0)
                    {
                        int readSize = Convert.ToInt32(Math.Min(splitSize, fs.Length - fs.Position));
                        int readBytes = await fs.ReadAsync(buffer, 0, readSize);

                        using (FileStream fsPart = new FileStream(partPath, FileMode.Append))
                            await fsPart.WriteAsync(buffer, 0, readSize);
                        gb--;
                        sm.ProgressCallback(fs.Position, fs.Length);
                    }


                    // await fs.CopyToAsync(fsPart, Convert.ToInt32(Math.Min(splitSize, fs.Length - fs.Position)));
                    listPath.Add(partPath);
                    index++;
                }
            }
            _logger.LogInformation("File split completed - FileName: {FileName}, Parts: {PartsCount}", name, listPath.Count);
            return listPath;
        }

        private async Task mergeFileStreamAsync(List<string> pathList, string destName)
        {
            _logger.LogInformation("Starting file merge - DestName: {DestName}, PartsCount: {PartsCount}", destName, pathList.Count);
            using (FileStream fileResult = new FileStream(destName, FileMode.Create))
                foreach (string pathSplited in pathList)
                {
                    using (FileStream fs = new FileStream(pathSplited, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        await fs.CopyToAsync(fileResult);
                        //byte[] buffer = new byte[fs.Length];
                        //int readBytes = await fs.ReadAsync(buffer);
                        //await fileResult.WriteAsync(buffer);
                    }
                }
            _logger.LogInformation("File merge completed - DestName: {DestName}", destName);
        }

        public virtual Task<int> PreloadFilesToTemp(string channelId, List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> items)
        {
            throw new NotImplementedException("PreloadFilesToTemp is implemented in FileServiceV2");
        }
    }
}
