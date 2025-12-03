using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data.db;
using TelegramDownloader.Data;
using TelegramDownloader.Models;
using System.Text.Json;

namespace TelegramDownloader.Controllers
{
    [Route("api/nodes")]
    [ApiController]
    public class WebDavController : ControllerBase
    {
        IDbService _db { get; set; }
        private readonly ILogger<WebDavController> _logger;

        public WebDavController(IDbService db, ILogger<WebDavController> logger)
        {
            _db = db;
            _logger = logger;
        }
        [HttpGet]
        public async Task<List<WebDavFileModel>> webDavPaths([FromQuery] string path, [FromQuery] string depth)
        {
            _logger.LogDebug("WebDav request - Path: {Path}, Depth: {Depth}", path, depth);
            var isFile = Path.HasExtension(path);
            if (!path.EndsWith("/") && !isFile)
                path = path + "/";
            if (String.IsNullOrEmpty(path))
                throw new BadHttpRequestException("Path is null or empty");
            string channel = path.Split("/")[1];
            if (String.IsNullOrEmpty(channel))
                throw new BadHttpRequestException("Channel is empty");
            path = path.Remove(0, path.IndexOf("/", 1));
            List<WebDavFileModel> files = new List<WebDavFileModel>();
            if (isFile)
            {
                var bsonFile = await _db.getFileByPath(channel, path);
                if (!(bsonFile == null) && bsonFile.IsFile)
                {
                    WebDavFileModel file = bsonFile.toWebDavFileModel(channel);
                    files = new List<WebDavFileModel>();
                    files.Add(file);
                }
            }
            if ((!isFile) || files.Count == 0)
            {
                if (depth == "0")
                {
                    var bsonFile = await _db.getFileByPath(channel, path[..^1]);
                    if (bsonFile != null)
                    {
                        WebDavFileModel file = bsonFile.toWebDavFileModel(channel);
                        files = new List<WebDavFileModel>();
                        files.Add(file);
                    }
                    
                }
                else
                    files = (await _db.getAllFilesInDirectoryPath(channel, path)).Select(file => file.toWebDavFileModel()).ToList();
            }
                
            // Console.WriteLine(JsonSerializer.Serialize(files));
            return files;
        }

        [HttpGet("meta")]
        public async Task<object> webDavMetadata([FromQuery] string path)
        {
            _logger.LogDebug("WebDav metadata request - Path: {Path}", path);
            if (String.IsNullOrEmpty(path))
                throw new Exception("Path is null or empty");
            string channel = path.Split("/")[1];
            if (String.IsNullOrEmpty(channel))
                throw new Exception("Channel is empty");
            path = path.Remove(0, path.IndexOf("/", 1));
            var bsonFile = await _db.getFileByPath(channel, path);
            if (bsonFile == null || !bsonFile.IsFile)
                throw new FileNotFoundException();
            WebDavFileModel files = bsonFile.toWebDavFileModel(channel);
            return files;
        }
    }
}
