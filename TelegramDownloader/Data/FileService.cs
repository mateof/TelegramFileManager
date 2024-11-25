﻿using BlazorBootstrap;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Newtonsoft.Json;
using SharpCompress.Common;
using Syncfusion.Blazor.FileManager;
using Syncfusion.Blazor.Inputs;

using Syncfusion.EJ2.FileManager.PhysicalFileProvider;
using Syncfusion.EJ2.Linq;
using System.Dynamic;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Services;
using TL;

namespace TelegramDownloader.Data
{
    public class FileService : IFileService
    {
        public static readonly List<string> ImageExtensions = new List<string> { ".JPG", ".JPEG", ".JPE", ".BMP", ".GIF", ".PNG" };
        public static readonly long MAXIMAGESIZE = 1024 * 1024 * 10;
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
    {"mkv", "video/webm" },
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

        private PhysicalFileProvider operation = new PhysicalFileProvider();
        private ITelegramService _ts { get; set; }
        private IDbService _db { get; set; }
        private ILogger<IFileService> _logger { get; set; }
        private TransactionInfoService _tis { get; set; }
        private ToastService _toastService { get; set;  }

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
                return new FileStream(filePath, FileMode.Open, FileAccess.Read);
            }
            return null;
        }

        public async Task<FileManagerResponse<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>> itemDeleteAsync(string dbName, ItemsDeleteEventArgs<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> args)
        {
            string[] names = args.Files.Select(x => x.Name).ToArray();
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
            }

            await _db.checkAndSetDirectoryHasChild(dbName, args.Files.FirstOrDefault().ParentId);
            return await GetFilesPath(dbName, args.Path);
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

        public async Task downloadFile(string dbName, List<FileManagerDirectoryContent> files, string targetPath, string? collectionId = null, string? channelId = null)
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

                    BsonFileManagerModel file = collectionId == null ? _db.getFileByPathSync(dbName, itemFile.FilterPath.Replace("\\", "/") + itemFile.Name) : _db.getFileByPathSync(dbName, itemFile.FilterPath.Replace("\\", "/") + itemFile.Name, collectionId);
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
                            int i = 1;
                            List<string> splitPaths = new List<string>();
                            foreach (int messageId in file.ListMessageId)
                            {
                                string filePathPart = System.IO.Path.Combine(currentFilePath, $"({i})" + itemFile.Name);
                                await downloadFromTelegram(channelId == null ? dbName : channelId, messageId, filePathPart, file);
                                splitPaths.Add(filePathPart);
                                i++;
                            }
                            await mergeFileStreamAsync(splitPaths, System.IO.Path.Combine(currentFilePath, itemFile.Name));
                            foreach (string filePath in splitPaths)
                            {
                                File.Delete(filePath);
                            }
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

        private async Task<MemoryStream> downloadFromTelegramAndReturn(string dbName, int messageId, string destPath, MemoryStream ms = null)
        {
            Message m = await _ts.getMessageFile(dbName, messageId);
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
        private async Task downloadFromTelegram(string dbName, int messageId, string destPath, BsonFileManagerModel file = null)
        {
            DownloadModel model = new DownloadModel();
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
            
            model.callbacks.callback = async () => await DownloadFileNow(dbName, messageId, destPath, model);
            _tis.addToPendingDownloadList(model);
        }

        public async Task DownloadFileNow(string dbName, int messageId, string destPath, DownloadModel model)
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
            idt = new InfoDownloadTaksModel();
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
                    var allFiles = new DirectoryInfo(currentFilePath).GetFiles("*.*", SearchOption.AllDirectories).Where(x => !x.Attributes.HasFlag(FileAttributes.Hidden) && !x.Attributes.HasFlag(FileAttributes.Directory) && x.Name != "@eaDir" && x.Length > 0);
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
            TransactionInfoService ti = new TransactionInfoService();
            ti.addToInfoDownloadTaskList(idt);
            ti.CheckPendingUploadInfoTasks();
        }


        public async Task UploadFileFromServer(string dbName, string currentPath, List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> files, InfoDownloadTaksModel dm = null) // ItemsUploadedEventArgs<FileManagerDirectoryContent> args)
        {

            // string currentPath = args.Path;
            NotificationModel nm = new NotificationModel();
            InfoDownloadTaksModel idt = dm;

            try
            {
                //if (dm == null)
                //{
                    
                //    idt = new InfoDownloadTaksModel();
                //    idt.id = Guid.NewGuid().ToString();
                //    idt.total = 0;
                //    idt.totalSize = 0;
                //    idt.isUpload = true;
                //    idt.toPath = currentPath;
                //    idt.executed = 0;
                //    idt.executedSize = 0;
                //    idt.isUpload = true;
                //    idt.files = files;
                //    idt.callbacks = new Callbacks();
                //    idt.callbacks.callback = async () => await UploadFileFromServer(dbName, currentPath, files);
                //    foreach (var file in files)
                //    {
                //        var filePath = file.IsFile ? file.FilterPath.Replace("\\", "/") + file.Name : file.FilterPath.Replace("\\", "/") + file.Name + "/";
                //        string currentFilePath = System.IO.Path.Combine(LOCALDIR, filePath[0] == '/' ? filePath.Substring(1) : filePath).Replace("\\", "/");
                        
                //        var fileInfo = new System.IO.FileInfo(currentFilePath);
                //        if (!file.IsFile)
                //        {
                //            var allFiles = new DirectoryInfo(currentFilePath).GetFiles("*.*", SearchOption.AllDirectories).Where(x => !x.Attributes.HasFlag(FileAttributes.Hidden));
                //            idt.total = allFiles.Count();
                //            idt.totalSize += allFiles.Sum(x => x.Length);
                //        } else
                //        {
                //            idt.total++;
                //            idt.totalSize += fileInfo.Length;
                //        }

                //    }
                //    TransactionInfoService ti = new TransactionInfoService();
                //    ti.addToInfoDownloadTaskList(idt);


                //}
                nm.sendEvent(new Notification($"Uploading files from folder {currentPath} to Telegram", "Telegram Upload", NotificationTypes.Info));
                foreach (var file in files)
                {

                    WaitingTime wt = new WaitingTime();
                    var folderPath = System.IO.Path.Combine(LOCALDIR, file.IsFile ? file.FilterPath.Replace("\\", "/").Substring(1) : file.FilterPath.Replace("\\", "/").Substring(1));
                    var filePath = file.IsFile ? file.FilterPath.Replace("\\", "/") + file.Name : file.FilterPath.Replace("\\", "/") + file.Name + "/";
                    string currentFilePath = System.IO.Path.Combine(LOCALDIR, filePath[0] == '/' ? filePath.Substring(1) : filePath).Replace("\\", "/");
                    var fileInfo = new System.IO.FileInfo(currentFilePath);

                    //if (!file.IsFile)
                        //if (dm == null)
                        //{
                        //    idt = new InfoDownloadTaksModel();
                        //    idt.id = Guid.NewGuid().ToString();
                        //    var allFiles = new DirectoryInfo(currentFilePath).GetFiles("*.*", SearchOption.AllDirectories).Where(x => !x.Attributes.HasFlag(FileAttributes.Hidden));
                        //    idt.total = allFiles.Count();
                        //    idt.totalSize = allFiles.Sum(x => x.Length);
                        //    idt.fromPath = currentFilePath;
                        //    idt.toPath = Path.Combine(currentPath, file.Name);
                        //    idt.executed = 0;
                        //    idt.executedSize = 0;
                        //    idt.isUpload = true;
                        //    idt.file = file;
                        //    idt.thread = Thread.CurrentThread;
                        //    TransactionInfoService ti = new TransactionInfoService();
                        //    ti.addToInfoDownloadTaskList(idt);
                        //}

                    BsonFileManagerModel model = new BsonFileManagerModel();
                    if (file.IsFile)
                    {
                        if (idt.executed > idt.currentUpload)
                        {
                            idt.currentUpload++;
                            continue;
                        }
                        idt.currentUpload++;
                        if (file.Size == 0 || (await _db.getFileByPath(dbName, System.IO.Path.Combine(currentPath, file.Name))) != null)
                        {
                            if (idt != null) idt.AddOne(file.Size);
                            continue;
                        };
                        model.Size = fileInfo.Length;
                        _logger.LogInformation($"Calculating MD5 of file {currentFilePath}");
                        model.MD5Hash = GetMd5HashFromFile(currentFilePath);
                        _logger.LogInformation($"Calculated MD5 of file {currentFilePath}: {model.MD5Hash}");
                        Message m = null;
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
                                // add upload to task list
                                idt.addUpload(um);
                                um.thread = Thread.CurrentThread;
                                while (attempts != 0 || um.state == StateTask.Canceled)
                                    try
                                    {
                                        wt = new WaitingTime();
                                        using (FileStream fs = new FileStream(s, FileMode.Open, FileAccess.Read, FileShare.Read))
                                            m = await _ts.uploadFile(dbName, fs, $"({i} of {filesSplit.Count}) - " + file.Name, um: um);
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
                                        _logger.LogError(e, "Exception sending file to Telegram");
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
                            // add upload to task list
                            idt.addUpload(um);
                            while (attempts != 0 || um.state == StateTask.Canceled)
                                try
                                {

                                    try
                                    {
                                        wt = new WaitingTime();
                                        using (FileStream ms = new FileStream(System.IO.Path.Combine(currentFilePath), FileMode.Open))
                                            if (ImageExtensions.Any(x => file.Name.ToUpper().EndsWith(x)) && file.Size >= MAXIMAGESIZE)
                                            {
                                                m = await _ts.uploadFile(dbName, ms, file.Name, "application/octet-stream", um);
                                            }
                                            else
                                                m = await _ts.uploadFile(dbName, ms, file.Name, um: um);
                                    }
                                    catch (Exception ex)
                                    {
                                        if (new List<string> { "IMAGE", "PHOTO" }.Any(x => ex.Message.Contains(x)))
                                        {
                                            using (FileStream ms = new FileStream(System.IO.Path.Combine(currentFilePath), FileMode.Open))
                                                m = await _ts.uploadFile(dbName, ms, file.Name, "application/octet-stream", um);
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
                                    _logger.LogError(e, "Exception sending file to Telegram");
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

                                    await Task.Delay(waitForNextAttempt);
                                }

                            model.MessageId = m.ID;
                        }

                    }
                    BsonFileManagerModel parent = await _db.getParentDirectoryByPath(dbName, currentPath);
                    model.Name = file.IsFile ? fileInfo.Name : file.Name;
                    model.IsFile = file.IsFile;
                    model.HasChild = false;
                    model.DateCreated = DateTime.Now;
                    model.DateModified = DateTime.Now;
                    model.FilterPath = currentPath == "/" ? currentPath : string.Concat(parent.FilterPath, parent.Name, "/");
                    model.FilterId = string.Concat(parent.FilterId, parent.Id.ToString(), "/");
                    model.ParentId = parent.Id;
                    model.FilePath = System.IO.Path.Combine(currentPath, file.IsFile ? fileInfo.Name : file.Name);
                    model.Type = file.IsFile ? (fileInfo.Name.Split(".").LastOrDefault() != null ? "." + fileInfo.Name.Split(".").LastOrDefault() : GetMimeType(fileInfo.Name)) : "folder";
                    // if file or folder does not exist, it will be created
                    if ((await _db.getFileByPath(dbName, System.IO.Path.Combine(currentPath, file.Name))) == null)
                        await _db.createEntry(dbName, model);
                    if (file.IsFile)
                    {
                        await _db.addBytesToFolder(dbName, model.ParentId, model.Size);
                        await _db.setDirectoryHasChild(dbName, parent.Id);
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
                _logger.LogError(ex, $"Error on uploadFileFromServer, path: {currentPath}");
                nm.sendEvent(new Notification("Error Uploading files to Telegram", "Telegram Upload", NotificationTypes.Error));
                Console.WriteLine(ex.Message);
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

        public async Task UploadFile(string dbName, string currentPath, UploadFiles file) // ItemsUploadedEventArgs<FileManagerDirectoryContent> args)
        {
            // string currentPath = args.Path;
            try
            {
                //foreach (var file in args.Files)
                //{

                //using (var filestream = new FileStream(Path.Combine(Environment.CurrentDirectory, "download","assss.jpg"), FileMode.Create, FileAccess.Write))
                //{
                //    await file.File.OpenReadStream(maxAllowedSize: long.MaxValue).CopyToAsync(filestream);
                //}
                //MemoryStream ms = new MemoryStream(file.File.OpenReadStream(maxAllowedSize: long.MaxValue));
                //await file.File.OpenReadStream().CopyToAsync(ms);

                string currentFilePath = System.IO.Path.Combine(TEMPDIR, dbName + currentPath);
                await SaveToFile(file.File, currentFilePath);


                BsonFileManagerModel model = new BsonFileManagerModel();
                model.Size = file.File.Size;
                Message m = null;
                if (file.File.Size > MaxSize)
                {

                    List<string> files = await splitFileStreamAsync(currentFilePath, file.File.Name, MaxSize);
                    File.Delete(System.IO.Path.Combine(currentFilePath, file.File.Name));
                    int i = 1;
                    model.ListMessageId = new List<int>();
                    model.isSplit = true;
                    foreach (string s in files)
                    {
                        using (FileStream fs = new FileStream(s, FileMode.Open, FileAccess.Read, FileShare.Read))
                            m = await _ts.uploadFile(dbName, fs, $"({i} of {files.Count}) - " + file.File.Name);
                        model.ListMessageId.Add(m.ID);
                        File.Delete(s);
                        i++;
                    }

                }
                else
                {
                    using (FileStream ms = new FileStream(System.IO.Path.Combine(currentFilePath, file.File.Name), FileMode.Open))
                    {
                        m = await _ts.uploadFile(dbName, ms, file.File.Name);
                    }
                    model.MessageId = m.ID;
                }

                BsonFileManagerModel parent = await _db.getParentDirectoryByPath(dbName, currentPath);
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
                await _db.addBytesToFolder(dbName, model.ParentId, model.Size);

                GC.Collect();




                // var folders = (file.FileInfo.Name).Split('/');
                // if (folders.Length > 1)
                // {
                //     for (var i = 0; i < folders.Length - 1; i++)
                //     {
                //         string newDirectoryPath = Path.Combine(FileManagerService.basePath + currentPath, folders[i]);
                //         if (Path.GetFullPath(newDirectoryPath) != (Path.GetDirectoryName(newDirectoryPath) + Path.DirectorySeparatorChar + folders[i]))
                //         {
                //             throw new UnauthorizedAccessException("Access denied for Directory-traversal");
                //         }
                //         if (!Directory.Exists(newDirectoryPath))
                //         {
                //             await FileManagerService.Create(currentPath, folders[i]);
                //         }
                //         currentPath += folders[i] + "/";
                //     }
                // }
                // var fullName = Path.Combine((FileManagerService.contentRootPath + currentPath), file.File.Name);
                // using (var filestream = new FileStream(fullName, FileMode.Create, FileAccess.Write))
                // {
                //     await file.File.OpenReadStream(long.MaxValue).CopyToAsync(filestream);
                // }
                //}
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on uploadFile");
                NotificationModel nm = new NotificationModel();
                nm.sendEvent(new Notification($"Error on UploadFile: {file.File.Name}", "Error", NotificationTypes.Error));
                Console.WriteLine(ex.Message);

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
            //var root = listFolders.Where(x => string.IsNullOrEmpty(x.ParentId)).FirstOrDefault();
            //FolderModel folder = new FolderModel();
            //folder.FolderName = root.Name;
            //folder.Id = "/";
            //folder.Expanded = true;
            //folder.Folders = await getTelegramSubfolders(root.Id, listFolders.Where(x => !string.IsNullOrEmpty(x.ParentId)).ToList(), folder.Id);
            //node.Add(folder);
            //return node;

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

            //if (path == "/")
            //{
            //    List<BsonFileManagerModel> Data = await _db.getAllFiles();
            //    string ParentId = Data
            //        .Where(x => string.IsNullOrEmpty(x.ParentId))
            //        .Select(x => x.Id).First();
            //    response.CWD = Data
            //        .Where(x => string.IsNullOrEmpty(x.ParentId)).First().toFileManagerContent();
            //    response.Files = Data
            //        .Where(x => x.ParentId == ParentId).Select(x => x.toFileManagerContent()).ToList();
            //}
            //else
            //{
            //List<BsonFileManagerModel> Data = await _db.getAllFilesInDirectoryPath(path);
            //    var childItem = fileDetails.Count > 0 && fileDetails[0] != null ? fileDetails[0] : Data
            //    .Where(x => x.FilterPath == path).First().toFileManagerContent();
            //    response.CWD = childItem;
            //    response.Files = Data
            //        .Where(x => x.ParentId == childItem.Id).Select(x => x.toFileManagerContent()).ToList();

            //}
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
                response.Files = Data
                    .Where(x => x.ParentId == childItem.Id).Select(x => x.toFileManagerContent()).ToList();

            }
            await Task.Yield();
            return response;
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
            int _gb = TelegramService.splitSizeGB;
            List<string> listPath = new List<string>();
            byte[] buffer = new byte[splitSize];
            using (FileStream fs = new FileStream(System.IO.Path.Combine(path, name), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                SplitModel sm = new SplitModel();
                sm.name = name;
                sm._size = fs.Length;
                sm._transmitted = 0;
                sm.thread = Thread.CurrentThread;
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

            return listPath;
        }

        private async Task mergeFileStreamAsync(List<string> pathList, string destName)
        {
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
        }
    }
}
