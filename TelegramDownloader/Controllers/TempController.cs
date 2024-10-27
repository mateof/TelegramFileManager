using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Syncfusion.Blazor.FileManager;
using Syncfusion.EJ2.FileManager.Base;
//File Manager's operations are available in the below namespace.
using Syncfusion.EJ2.FileManager.PhysicalFileProvider;
using System.Collections;
using System.IO;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TL;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace TelegramDownloader.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TempController : ControllerBase
    {
        public PhysicalFileProvider operation;
        public string basePath;
        string root = FileService.RELATIVELOCALDIR;
        IDbService _db { get; set; }
        ITelegramService _ts { get; set; }
        IFileService _fs { get; set; }

        public TempController(IDbService db, ITelegramService ts, IFileService fs)
        {
            _fs = fs; ;
            _ts = ts;
            _db = db;
            this.basePath = Environment.CurrentDirectory;
            this.operation = new PhysicalFileProvider();
            this.operation.RootFolder(Path.Combine(basePath, root));

        }

        [Route("FileOperations/{id:int}")]
        public async Task<object> FileOperations([FromBody] Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent args, int id)
        {
            this.operation.RootFolder(Path.Combine(basePath, root, id.ToString()));
            switch (args.Action)
            {
                // Add your custom action here.
                case "read":
                    //return null;
                    //string path = System.IO.Path.Combine("/" + id.ToString() + "/", args.Path);
                    // Path - Current path; ShowHiddenItems - Boolean value to show/hide hidden items.
                    var f = this.operation.GetFiles(args.Path, false);
                    return this.operation.ToCamelCase(f);
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
                    return this.operation.ToCamelCase(this.operation.Copy(args.Path, args.TargetPath, args.Names, args.RenameFiles, args.TargetData));
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
                    return this.operation.ToCamelCase(this.operation.Search(args.Path, args.SearchString, args.ShowHiddenItems, args.CaseSensitive));
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

        [Route("Download/{id:int}")]
        public IActionResult Download([FromForm] string downloadInput, int id)
        {
            this.operation.RootFolder(Path.Combine(basePath, root, id.ToString()));
            //Invoking download operation with the required parameters.
            // path - Current path where the file is downloaded; Names - Files to be downloaded;
            Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent args = JsonConvert.DeserializeObject<Syncfusion.EJ2.FileManager.Base.FileManagerDirectoryContent>(downloadInput);
            return operation.Download(args.Path, args.Names);
        }

        // Processing the Upload operation.
        //[Route("Upload/{id:int}")]
        //[HttpPost]
        //[DisableRequestSizeLimit]
        //// [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = long.MaxValue)]
        //[RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
        //public async Task<IActionResult> Upload([FromForm] string path, IList<IFormFile> uploadFiles, [FromForm] string? data, [FromForm] string action, int id)
        //{
        //    this.operation.RootFolder(Path.Combine(basePath, root, id.ToString()));
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


        //[Route("Upload/{id:int}")]
        //[HttpPost]
        //[DisableRequestSizeLimit]
        //// [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = long.MaxValue)]
        //[RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
        //public async Task<IActionResult> Upload([FromForm] string path, Stream uploadFiles, [FromForm] string? data, [FromForm] string action, int id)
        //{
        //    this.operation.RootFolder(Path.Combine(basePath, root, id.ToString()));
        //    FileManagerResponse uploadResponse;

        //    // uploadResponse = operation.Upload(Path.Combine(basePath, root, path), uploadFiles, action, null);
        //    //if (uploadResponse.Error != null)
        //    //{
        //    //    Response.Clear();
        //    //    Response.ContentType = "application/json; charset=utf-8";
        //    //    Response.StatusCode = Convert.ToInt32(uploadResponse.Error.Code);
        //    //    Response.HttpContext.Features.Get<IHttpResponseFeature>().ReasonPhrase = uploadResponse.Error.Message;
        //    //}
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
            this.operation.RootFolder(Path.Combine(basePath, root, id.ToString()));
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

        [Route("GetImage/{id:int}")]
        public async Task<IActionResult> GetImage(string path, int id)
        {
            this.operation.RootFolder(Path.Combine(basePath, root, id.ToString()));
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
    }

    //public class uploadFile
    //{
    //    public IFormFile uploadFiles
    //}
}
