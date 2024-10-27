using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using Syncfusion.Blazor.FileManager;
using Syncfusion.EJ2.FileManager.Base;
using Syncfusion.EJ2.FileManager.PhysicalFileProvider;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TL;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace TelegramDownloader.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileController : ControllerBase
    {
        public PhysicalFileProvider operation;
        public string basePath;
        string root = FileService.RELATIVELOCALDIR;
        IDbService _db { get; set; }
        ITelegramService _ts { get; set; }
        IFileService _fs { get; set; }

        public FileController(IDbService db, ITelegramService ts, IFileService fs)
        {
            this.basePath = Environment.CurrentDirectory;
            if (!System.IO.Directory.Exists(Path.Combine(basePath, root)))
            {
                System.IO.Directory.CreateDirectory(Path.Combine(basePath, root));
            }
            _fs = fs; ;
            _ts = ts;
            _db = db;
            
            this.operation = new PhysicalFileProvider();
            this.operation.RootFolder(Path.Combine(basePath, root));

        }

        [Route("FileOperations")]
        public async Task<object> FileOperations([FromBody] Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent args)
        {

            switch (args.Action)
            {
                // Add your custom action here.
                case "read":
                    //return null;
                    //string path = System.IO.Path.Combine("/" + id.ToString() + "/", args.Path);
                    // Path - Current path; ShowHiddenItems - Boolean value to show/hide hidden items.
                    //var f = this.operation.GetFiles(args.Path, false);
                    //return this.operation.ToCamelCase(f);
                    return this.operation.ToCamelCase(this.operation.GetFiles(args.Path, args.ShowHiddenItems));
                // return ToCamelCase(await convertAsync(args.Path == "/" ? await _db.getAllFiles() : await _db.getAllFilesInDirectory(args.Data.Count() > 0 && args.Data[0] != null ? args.Data[0].FilterId + args.Data[0].Id + "/" : args.Path), args.Data.Count() > 0 && args.Data[0] != null ? args.Data[0].Id : null));  // this.operation.ToCamelCase(this.operation.GetFiles(args.Path, args.ShowHiddenItems));
                case "delete":
                    //foreach (string name in args.Names)
                    //{
                    //    BsonFileManagerModel entry = await _db.getEntry(args.Path, name);
                    //    if (entry == null)
                    //        throw new Exception("File not found");
                    //    await _ts.deleteFile(_db.dbName, entry.MessageId);
                    //    await _db.deleteEntry(entry.Id);
                    //    // Path - Current path where the folder to be deleted; Names - Name of the files to be deleted
                        
                    //}
                    return this.operation.ToCamelCase(this.operation.Delete(args.Path, args.Names));
                    //return ToCamelCase(await convertAsync(await _db.getAllFilesInDirectoryPath(args.Path), null, args.Action, false));
                case "copy":
                    //  Path - Path from where the file was copied; TargetPath - Path where the file/folder is to be copied; RenameFiles - Files with same name in the copied location that is confirmed for renaming; TargetData - Data of the copied file
                    var result = this.operation.Copy(args.Path, args.TargetPath, args.Names, args.RenameFiles, args.TargetData);
                    return this.operation.ToCamelCase(result);
                case "move":
                    // Path - Path from where the file was cut; TargetPath - Path where the file/folder is to be moved; RenameFiles - Files with same name in the moved location that is confirmed for renaming; TargetData - Data of the moved file
                    return this.operation.ToCamelCase(this.operation.Move(args.Path, args.TargetPath, args.Names, args.RenameFiles, args.TargetData));
                case "details":
                    // Path - Current path where details of file/folder is requested; Name - Names of the requested folders
                    return this.operation.ToCamelCase(this.operation.Details(args.Path, args.Names));
                case "create":
                    //var create = await _db.createEntry(new BsonFileManagerModel(args));
                    //// Path - Current path where the folder is to be created; Name - Name of the new folder
                    //return ToCamelCase(await convertAsync(create, args.TargetData?.Id, args.Action, false));
                return this.operation.ToCamelCase(this.operation.Create(args.Path, args.Name));
                // return ToCamelCase(await convertAsync(args, await _db.getAllFiles()));
                case "search":
                    // Path - Current path where the search is performed; SearchString - String typed in the searchbox; CaseSensitive - Boolean value which specifies whether the search must be casesensitive
                    var search =  this.operation.Search(args.Path, args.SearchString, args.ShowHiddenItems, args.CaseSensitive);
                    return this.operation.ToCamelCase(search);
                case "rename":
                    // Path - Current path of the renamed file; Name - Old file name; NewName - New file name
                    return this.operation.ToCamelCase(this.operation.Rename(args.Path, args.Name, args.NewName));
            }
            return null;
        }

        private ReadEventArgs<Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent> convertEvent(ReadEventArgs<Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent> args)
        {
            return args;
        }

        private async Task<FileManagerResponse<BsonFileManagerModel>> convertAsync(List<BsonFileManagerModel> Data, string parentId = "", string action = "read", bool cwd = true)
        {
            FileManagerResponse<BsonFileManagerModel> response = new FileManagerResponse<BsonFileManagerModel>();

            //string ParentId = Data
            //    .Where(x => string.IsNullOrEmpty(x.ParentId))
            //    .Select(x => x.Id).First();
            //response.CWD = Data
            //    .Where(x => string.IsNullOrEmpty(x.ParentId)).First();
            //response.Files = Data
            //    .Where(x => x.ParentId == ParentId).ToList();
            if (action != "create" && cwd)
                response.CWD = string.IsNullOrEmpty(parentId)
                    ? Data
                    .Where(x => string.IsNullOrEmpty(x.ParentId)).First()
                    : Data
                    .Where(x => x.Id == parentId).First();
            if (!cwd)
                response.Files = Data;
            else
                response.Files = string.IsNullOrEmpty(parentId)
                    ? Data
                    .Where(x => !string.IsNullOrEmpty(x.ParentId)).ToList()
                    : Data
                    .Where(x => x.Id != parentId).ToList();

            // await Task.Yield();
            //await Task.Yield();
            //args.Response = (FileManagerResponse<Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent>)response;
            return response;
        }

        [Route("Download")]
        public IActionResult Download([FromForm] string downloadInput)
        {
            //Invoking download operation with the required parameters.
            // path - Current path where the file is downloaded; Names - Files to be downloaded;
            Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent args = JsonConvert.DeserializeObject<Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent>(downloadInput);
            return operation.Download(args.Path, args.Names);
        }

        //// Processing the Upload operation.
        //[Route("Upload")]
        //public IActionResult Upload([FromForm] string path, IList<IFormFile> uploadFiles, [FromForm] Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent data, [FromForm] string action)
        //{

        //    //foreach (var file in uploadFiles)
        //    //{
        //    //    Message m = await _ts.uploadFile(_db.dbName, file.OpenReadStream(), file.FileName);
        //    //    BsonFileManagerModel parent = await _db.getParentDirectory(path);
        //    //    BsonFileManagerModel model = new BsonFileManagerModel();
        //    //    model.Name = file.FileName;
        //    //    model.IsFile = true;
        //    //    model.HasChild = false;
        //    //    model.DateCreated = DateTime.Now;
        //    //    model.DateModified = DateTime.Now;
        //    //    model.FilterPath = string.Concat(parent.FilterPath, parent.Name, "/");
        //    //    model.FilterId = string.Concat(parent.FilterId, parent.Id.ToString(), "/");
        //    //    model.ParentId = parent.Id;
        //    //    model.FilePath = System.IO.Path.Combine(path, file.FileName);
        //    //    model.Size = file.OpenReadStream().Length;
        //    //    model.Type = file.FileName.Split(".").LastOrDefault() != null ? "." + file.FileName.Split(".").LastOrDefault() : file.ContentType;
        //    //    model.MessageId = m.ID;
        //    //    await _db.createEntry(model);
        //    //}

        //    //Invoking upload operation with the required parameters.
        //    // path - Current path where the file is to uploaded; uploadFiles - Files to be uploaded; action - name of the operation(upload)
        //    FileManagerResponse uploadResponse;
        //    uploadResponse = operation.Upload(Path.Combine(basePath, root, path), uploadFiles, action, null);
        //    if (uploadResponse.Error != null)
        //    {
        //        Response.Clear();
        //        Response.ContentType = "application/json; charset=utf-8";
        //        Response.StatusCode = Convert.ToInt32(uploadResponse.Error.Code);
        //        Response.HttpContext.Features.Get<IHttpResponseFeature>().ReasonPhrase = uploadResponse.Error.Message;
        //    }
        //    return Content("");
        //}

        [Route("Upload")]
        [HttpPost]
        [DisableRequestSizeLimit]
        // [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = long.MaxValue)]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
        public async Task<IActionResult> Upload([FromForm] string path, IFormFile file, [FromForm] string? data, [FromForm] string action, [FromForm] int id)
        {
            IList<IFormFile> lfile = new List<IFormFile>();
            lfile.Add(file);
            this.operation.RootFolder(Path.Combine(basePath, root));
            FileManagerResponse uploadResponse;

            uploadResponse = operation.Upload(Path.Combine(basePath, root, path), lfile, action, file.Length, null);
            if (uploadResponse.Error != null)
            {
                Response.Clear();
                Response.ContentType = "application/json; charset=utf-8";
                Response.StatusCode = Convert.ToInt32(uploadResponse.Error.Code);
                Response.HttpContext.Features.Get<IHttpResponseFeature>().ReasonPhrase = uploadResponse.Error.Message;
            }
            return Content("");
        }

        [Route("GetImage")]
        public async Task<IActionResult> GetImage(string path)
        {
            //Invoking GetImage operation with the required parameters.
            // path - Current path of the image file; Id - Image file id;
            //var file = await _fs.getImage(path);
            //FileStreamResult fsr = new FileStreamResult(file, new MediaTypeHeaderValue("image/jpg"))
            //{
            //    FileDownloadName = path.Split("/").Last()
            //};
            //return fsr;
            return this.operation.GetImage(path, "", false, null, null);
        }

        [Route("GetFile/{id}")]
        public async Task<IActionResult> GetFile(string id, string? idChannel, string? idFile )
        {
            var fileName = id;
            var mimeType = FileService.getMimeType(id.Split(".").Last());
            var file = _fs.ExistFileIntempFolder($"{idChannel}-{idFile}-{id}");
            if ( file == null )
            {
                HttpResponseMessage fullResponse = new HttpResponseMessage(HttpStatusCode.OK); // Request.CreateResponse(HttpStatusCode.OK);
                Message idM = await _ts.getMessageFile(idChannel, Convert.ToInt32(idFile));
                ChatMessages cm = new ChatMessages();
                cm.message = idM;
                file = new FileStream(System.IO.Path.Combine(FileService.TEMPDIR, "_temp", $"{idChannel}-{idFile}-{id}"), FileMode.Create, FileAccess.ReadWrite);

                await _ts.DownloadFileAndReturn(cm, file);
                file.Position = 0; 
            }

            return new FileStreamResult(file, mimeType)
            {
                FileDownloadName = fileName,
                EnableRangeProcessing = true
                
            };

        }

        [Route("export/{id}")]
        public async Task<IActionResult> ExportDatabase(string id)
        {
            MemoryStream ms = await _fs.exportAllData(id);
            ms.Position = 0;
            return new FileStreamResult(ms, "application/json")
            {
                FileDownloadName = "tfm_" + id + "_" + _ts.getChatName(Convert.ToInt64(id)) + "_" + ConvertToTimestamp(DateTime.Now) + ".json"

            };

        }

        public string ToCamelCase(FileManagerResponse<BsonFileManagerModel> userData)
        {
            return JsonConvert.SerializeObject(userData, new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                }
            });
        }

        // GET: api/<FileController>
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET api/<FileController>/5
        [HttpGet("{id}")]
        public string Get(int id)
        {
            return "value";
        }

        // POST api/<FileController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<FileController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<FileController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }


        private static long ConvertToTimestamp(DateTime value)
        {
            DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan elapsedTime = value - Epoch;
            return (long)elapsedTime.TotalSeconds;
        }
    }
}
