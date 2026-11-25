#nullable disable
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using System.IO.Compression;
using System.Net;
using System.Web;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.FileSystem;
using TelegramDownloader.Services;
using TL;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace TelegramDownloader.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public partial class FileController : ControllerBase
    {
        public string basePath;
        string root = FileService.RELATIVELOCALDIR;

        private static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1);

        // Limit concurrent direct range fetches against Telegram (seeks ahead of the cache)
        private static readonly SemaphoreSlim telegramRangeSemaphore = new SemaphoreSlim(4);

        // If the requested range starts within this distance of the background download
        // position, wait for the download instead of opening a duplicate Telegram fetch
        private const long WAIT_PROXIMITY_BYTES = 8 * 1024 * 1024;

        IDbService _db { get; set; }
        ITelegramService _ts { get; set; }
        IFileService _fs { get; set; }
        TransactionInfoService _tis { get; set; }
        private readonly IProgressiveDownloadService _progressiveDownload;
        private ILogger<IFileService> _logger { get; set; }

        /// <summary>
        /// Sets up the legacy EJ2 file-manager endpoints, which live in
        /// <c>Controllers/Legacy/FileController.Ej2.cs</c>. Deleting that file
        /// removes the call as well (partial method without implementation).
        /// </summary>
        partial void OnLegacyFileManagerInit();

        public FileController(IDbService db, ITelegramService ts, IFileService fs, TransactionInfoService tis, IProgressiveDownloadService progressiveDownload, ILogger<IFileService> logger)
        {
            this.basePath = Environment.CurrentDirectory;
            if (!System.IO.Directory.Exists(Path.Combine(basePath, root)))
            {
                System.IO.Directory.CreateDirectory(Path.Combine(basePath, root));
            }
            _fs = fs; ;
            _ts = ts;
            _db = db;
            _tis = tis;
            _progressiveDownload = progressiveDownload;
            _logger = logger;
            
            OnLegacyFileManagerInit();

        }


        /// <summary>
        /// Download a file from Telegram by message ID
        /// </summary>
        /// <param name="id">File name</param>
        /// <param name="idChannel">Telegram channel ID</param>
        /// <param name="idFile">Telegram message ID</param>
        [HttpGet]
        [Route("GetFile/{id}")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(503)]
        public async Task<IActionResult> GetFile(string id, string? idChannel, string? idFile)
        {
            var fileName = id;
            var mimeType = FileService.getMimeType(id.Split(".").Last());
            var file = _fs.ExistFileIntempFolder($"{idChannel}-{idFile}-{id}");
            if ( file == null )
            {
                string filePath = System.IO.Path.Combine(FileService.TEMPDIR, "_temp", $"{idChannel}-{idFile}-{id}");

                // Check if file exists (might be downloading by another request - race condition)
                if (System.IO.File.Exists(filePath))
                {
                    // File exists, wait for it to be ready and return it
                    for (int i = 0; i < 60; i++) // Wait up to 30 seconds
                    {
                        await Task.Delay(500);
                        try
                        {
                            file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            break;
                        }
                        catch (IOException)
                        {
                            // File still being written, wait more
                        }
                    }
                    if (file == null)
                    {
                        return StatusCode(503, "File is being processed, please try again");
                    }
                }
                else
                {
                    // File doesn't exist, download it
                    TL.Message idM = await _ts.getMessageFile(idChannel, Convert.ToInt32(idFile));
                    ChatMessages cm = new ChatMessages();
                    cm.message = idM;

                    file = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
                    DownloadModel dm = new DownloadModel();
                    dm.tis = _tis;
                    dm.startDate = DateTime.Now;
                    dm.path = filePath;
                    if (cm.message is Message msgBase)
                    {
                        if (msgBase.media is MessageMediaDocument mediaDoc &&
                            mediaDoc.document is TL.Document doc)
                        {
                            dm._size = doc.size;
                            dm.name = doc.Filename;
                        }
                    }
                    dm.channelName = _ts.getChatName(Convert.ToInt64(idChannel));
                    _tis.addToDownloadList(dm);
                    await _ts.DownloadFileAndReturn(cm, file, model: dm);
                    file.Position = 0;
                }
            }
            var request = HttpContext.Request;
            var rangeHeader = request.Headers["Range"].ToString();

            return new FileStreamResult(file, mimeType)
            {
                FileDownloadName = fileName,
                EnableRangeProcessing = true

            };
        }

        /// <summary>
        /// View file inline (for PDF viewer, etc.) - doesn't trigger download
        /// </summary>
        /// <param name="id">File name</param>
        /// <param name="idChannel">Telegram channel ID</param>
        /// <param name="idFile">Telegram message ID</param>
        [HttpGet]
        [Route("ViewFile/{id}")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        public async Task<IActionResult> ViewFile(string id, string? idChannel, string? idFile)
        {
            var fileName = id;
            var mimeType = FileService.getMimeType(id.Split(".").Last());
            var file = _fs.ExistFileIntempFolder($"{idChannel}-{idFile}-{id}");
            if (file == null)
            {
                TL.Message idM = await _ts.getMessageFile(idChannel, Convert.ToInt32(idFile));
                ChatMessages cm = new ChatMessages();
                cm.message = idM;
                string filePath = System.IO.Path.Combine(FileService.TEMPDIR, "_temp", $"{idChannel}-{idFile}-{id}");
                file = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
                DownloadModel dm = new DownloadModel();
                dm.tis = _tis;
                dm.startDate = DateTime.Now;
                dm.path = filePath;
                if (cm.message is Message msgBase)
                {
                    if (msgBase.media is MessageMediaDocument mediaDoc &&
                        mediaDoc.document is TL.Document doc)
                    {
                        dm._size = doc.size;
                        dm.name = doc.Filename;
                    }
                }
                dm.channelName = _ts.getChatName(Convert.ToInt64(idChannel));
                _tis.addToDownloadList(dm);
                await _ts.DownloadFileAndReturn(cm, file, model: dm);
                file.Position = 0;
            }

            // Return file for inline viewing (no download header)
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{HttpUtility.UrlEncode(fileName)}\"";
            return new FileStreamResult(file, mimeType)
            {
                EnableRangeProcessing = true
            };
        }

        /// <summary>
        /// Get file by TFM database ID (immediate download)
        /// </summary>
        /// <param name="id">File name</param>
        /// <param name="idChannel">Telegram channel ID</param>
        /// <param name="idFile">TFM database file ID</param>
        [HttpGet]
        [Route("GetFileByTfmIdNow/{id}")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetFileByTfmIdNow(string id, [FromQuery] string idChannel, [FromQuery] string idFile)
        {
            var fileName = id;
            var mimeType = FileService.getMimeType(id.Split(".").Last());
            var dbFile = await _fs.getItemById(idChannel, idFile);
            if (dbFile == null)
            {
                return new ObjectResult("") { StatusCode = (int)HttpStatusCode.NotFound };
            }
            var file = _fs.ExistFileIntempFolder($"{idChannel}-{dbFile.MessageId}-{id}");
            if (file == null)
            {
                TL.Message idM = await _ts.getMessageFile(idChannel, Convert.ToInt32(dbFile.MessageId));
                ChatMessages cm = new ChatMessages();
                cm.message = idM;
                String filePath = System.IO.Path.Combine(FileService.TEMPDIR, "_temp", $"{idChannel}-{idFile}-{id}");
                file = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
                DownloadModel dm = new DownloadModel();
                dm.tis = _tis;
                dm.startDate = DateTime.Now;
                dm.path = filePath;
                dm.name = dbFile.Name;
                dm._size = dbFile.Size;
                dm.channelName = _ts.getChatName(Convert.ToInt64(idChannel));
                _tis.addToDownloadList(dm);
                await _ts.DownloadFileAndReturn(cm, file, model: dm);
                file.Position = 0;
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"{HttpUtility.UrlEncode(fileName)}\"";

            return new FileStreamResult(file, mimeType);
        }

        /// <summary>
        /// Get file by TFM database ID with background download (supports range requests)
        /// </summary>
        /// <param name="id">File name</param>
        /// <param name="idChannel">Telegram channel ID</param>
        /// <param name="idFile">TFM database file ID</param>
        [HttpGet]
        [Route("GetFileByTfmId/{id}")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetFileByTfmId(string id, [FromQuery] string idChannel, [FromQuery] string idFile)
        {
            var fileName = id;
            var mimeType = FileService.getMimeType(id.Split(".").Last());
            var dbFile = await _fs.getItemById(idChannel, idFile);
            if (dbFile == null)
            {
                return new ObjectResult("") { StatusCode = (int)HttpStatusCode.NotFound };
            }
            
            dbFile.Name = $"{idChannel}-{(dbFile.MessageId != null ? dbFile.MessageId.ToString() : dbFile.Id)}-{id}";
            String path = Path.Combine(FileService.TEMPDIR, "_temp");
            String filePath = Path.Combine(path, dbFile.Name);
            var mutexState = semaphoreSlim.Wait(10000);
            FileStream? file = null;
            if (!mutexState)
            {
                _logger.LogWarning("Could not acquire download mutex in 10 seconds for file {fileName}", dbFile.Name);
                return StatusCode(500, "Could not acquire download mutex in 10 seconds");
            }

            file = _fs.ExistFileIntempFolder(dbFile.Name);
            var isFileDownloaded = _tis.isFileDownloaded(filePath);
            if (!isFileDownloaded)
                if ((file == null) || (file != null && file.Length < dbFile.Size))
                {
                    _logger.LogWarning("Stream Downloading file {fileName}", dbFile.Name);
                    await _fs.downloadFile(idChannel, new List<FsEntry> { dbFile.ToFsEntry() }, path);
                    Thread.Sleep(4000);
                    file = _fs.ExistFileIntempFolder(dbFile.Name);
                }

            if (mutexState)
                semaphoreSlim.Release();

            if (file == null)
            {
                return NotFound();
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"{HttpUtility.UrlEncode(fileName)}\"";

            try
            {
                return new FileStreamResult(file, mimeType)
                {
                    FileDownloadName = fileName,
                    EnableRangeProcessing = true

                };
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("❌ El cliente cerró la conexión al descargar el archivo {fileName}.", fileName);
                return StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
        }

        /// <summary>
        /// Generate STRM files for a channel (for media player integration)
        /// </summary>
        /// <param name="idChannel">Telegram channel ID</param>
        /// <param name="path">Path in channel</param>
        /// <param name="host">Host URL for streaming</param>
        [HttpGet]
        [Route("strm")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        public async Task<IActionResult> GetStrm([FromQuery] string idChannel, [FromQuery] string path, [FromQuery] string host)
        {
            String zip = await _fs.CreateStrmFiles(path, idChannel, host);

            FileStream fs = new FileStream(zip, FileMode.Open);

            return new FileStreamResult(fs, "application/octet-stream")
            {
                FileDownloadName = Path.GetFileName(zip)
            };
        }

        /// <summary>
        /// Stream file directly from Telegram (supports range requests for seeking)
        /// </summary>
        /// <param name="idChannel">Telegram channel ID</param>
        /// <param name="idFile">TFM database file ID</param>
        /// <param name="name">File name</param>
        [HttpGet]
        [Route("GetFileStream/{idChannel}/{idFile}/{name}")]
        [ProducesResponseType(206)]
        [ProducesResponseType(416)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetFileStream(string idChannel, string idFile, string name)
        {
            var fileName = name;
            var mimeType = FileService.getMimeType(name.Split(".").Last());

            var request = HttpContext.Request;
            var rangeHeader = request.Headers["Range"].ToString();
            _logger.LogDebug("GetFileStream - Range: {Range}", rangeHeader);

            var file = await _fs.getItemById(idChannel, idFile);
            long totalLength = file.Size;

            TL.Message idM = await _ts.getMessageFile(idChannel, file.MessageId ?? file.ListMessageId.FirstOrDefault());

            long totalPartialFileLength = 0;

            if (idM.media is MessageMediaDocument { document: Document document })
            {
                totalPartialFileLength = document.size;
            }

            long from = 0;
            long initialFrom = 0;
            long initialPartFrom = 0;
            long to = 0;

            if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
            {
                var range = rangeHeader.Replace("bytes=", "").Split('-');
                from = long.Parse(range[0]);
                initialFrom = from;
                initialPartFrom = from;
                if (range.Length > 1 && !string.IsNullOrEmpty(range[1]))
                    to = long.Parse(range[1]);
            }

            if (from >= totalPartialFileLength)
            {
                _logger.LogDebug("Take a file part - From: {From}, TotalPartialFileLength: {TotalPartialFileLength}", from, totalPartialFileLength);
                int filePart = 0;
                while(from >= totalPartialFileLength)
                {
                    filePart++;
                    from -= totalPartialFileLength;
                }
                idM = await _ts.getMessageFile(idChannel, file.ListMessageId[filePart]);
                if (idM.media is MessageMediaDocument { document: Document document2 })
                {
                    totalPartialFileLength = document2.size;
                }
                initialPartFrom = from;
            }

            if (from > 0)
            {
                from = (from / 524288) * 524288;
            }

            _logger.LogDebug("Stream position - From: {From}", from);

            if (to == 0)
                if (string.IsNullOrEmpty(rangeHeader) || from == 0)
                    to = (12 * 524288) + from;
                else
                    to = (5 * 524288) + from;
            else
                    to = ((to + 524288) / 524288) * 524288;

            if (to > totalPartialFileLength)
            {
                to = totalPartialFileLength;
            }

            if (totalPartialFileLength == initialPartFrom)
            {
                Response.StatusCode = 416; // Range Not Satisfiable
                Response.Headers["Content-Range"] = $"bytes */{totalLength}";
                return new EmptyResult();
            }

            long length = to - from;
            //Console.WriteLine("To length: " + to);
            //Console.WriteLine("future download length: " + length);

            byte[] data = await _ts.DownloadFileStream(idM, from, (int)length);

            //Console.WriteLine("Total download length: " + data.Length);

            long skipedBytes = initialPartFrom - from - (length - data.Length);

            if (skipedBytes < 0) skipedBytes = 0;

            if (skipedBytes > data.Length)
                return StatusCode(500, "No hay suficientes bytes en el bloque descargado");

            long dataLength = data.Length - skipedBytes;
            Response.ContentLength = dataLength; //(dataLength + initialFrom) >= totalLength ? totalLength - initialFrom : dataLength;
            // Response.StatusCode = (int)HttpStatusCode.PartialContent; // 206
            Response.StatusCode = StatusCodes.Status206PartialContent; // StatusCodes.Status206PartialContent;
            Response.ContentType = mimeType;
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{HttpUtility.UrlEncode(fileName)}\"";
            Response.Headers["Content-Range"] = $"bytes {(initialFrom >= totalLength ? totalLength - 1 : initialFrom)}-{(initialFrom >= totalLength ? totalLength - 1 : initialFrom + Response.ContentLength - 1)}/{totalLength}";
            Response.Headers["Accept-Ranges"] = "bytes";

            //if (Request.Headers.ContainsKey("Range"))
            //{
            //    Console.WriteLine("Range header recibido:");
            //    Console.WriteLine(Request.Headers["Range"]);
            //}

            //foreach (var header in Response.Headers)
            //{
            //    Console.WriteLine($"{header.Key}: {header.Value}");
            //}


            // Console.WriteLine("Skiped bytes: " + skipedBytes);

            var stream = new MemoryStream(data, (int)skipedBytes, (int)Response.ContentLength);
            stream.Position = 0;

            //  Console.WriteLine("Real Data length: " + stream.Length);

            // Console.WriteLine("---------------------------------------------------");

            var cancellationToken = HttpContext.RequestAborted;

            try
            {
                await stream.CopyToAsync(Response.Body, 64 * 1024, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);

                return new EmptyResult();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Client closed connection during stream - File: {FileName}", fileName);
            }

            return new EmptyResult();

        }

        /// <summary>
        /// Stream file from Telegram while caching it to disk in the background (progressive streaming).
        /// Playback starts immediately; once fully cached, ranges are served from disk.
        /// Supports split (multi-message) files: parts are cached sequentially into a single file
        /// and seeks ahead of the cache are fetched from the part that contains the requested range.
        /// </summary>
        /// <param name="idChannel">Telegram channel ID</param>
        /// <param name="idFile">TFM database file ID</param>
        /// <param name="name">File name (used for mime type / Content-Disposition)</param>
        [HttpGet]
        [Route("GetFileStreamCached/{idChannel}/{idFile}/{name}")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        [ProducesResponseType(206)]
        [ProducesResponseType(404)]
        [ProducesResponseType(416)]
        public async Task<IActionResult> GetFileStreamCached(string idChannel, string idFile, string name)
        {
            var dbFile = await _fs.getItemById(idChannel, idFile);
            if (dbFile == null)
            {
                return NotFound();
            }

            return await ServeProgressive(idChannel, dbFile, FileService.getMimeType(name));
        }

        /// <summary>
        /// Body of <see cref="GetFileStreamCached"/> for an entry that is already
        /// resolved: serves the requested range from the progressive disk cache
        /// (filled once by a background download) or straight from Telegram for
        /// seeks ahead of it. Also used by the v1 media endpoints, including
        /// message attachments that are not in any index (a synthetic entry with
        /// the message id, name and size).
        /// </summary>
        [NonAction]
        public async Task<IActionResult> ServeProgressive(string idChannel, BsonFileManagerModel dbFile, string mimeType)
        {
            // Ordered Telegram messages that make up the file (several for split files)
            var messageIds = ProgressiveDownloadService.GetMessageIds(dbFile);
            if (messageIds == null || messageIds.Count == 0)
            {
                return NotFound();
            }

            var fileName = dbFile.Name;
            var cacheFileName = $"{idChannel}-{(dbFile.MessageId != null ? dbFile.MessageId.ToString() : dbFile.Id)}-{fileName}";
            var tempPath = Path.Combine(FileService.TEMPDIR, "_temp");
            var filePath = Path.Combine(tempPath, cacheFileName);
            Directory.CreateDirectory(tempPath);

            long totalLength = dbFile.Size;

            // Fully cached: serve straight from disk with full range support
            if (System.IO.File.Exists(filePath) && new FileInfo(filePath).Length >= totalLength)
            {
                _logger.LogDebug("GetFileStreamCached - serving fully cached file {FileName}", fileName);
                Response.Headers["Content-Disposition"] = $"inline; filename=\"{HttpUtility.UrlEncode(fileName)}\"";
                return PhysicalFile(filePath, mimeType, enableRangeProcessing: true);
            }

            // Parse range header (RFC 7233: bytes=X-, bytes=X-Y, bytes=-N)
            var rangeHeader = Request.Headers["Range"].ToString();
            long from = 0;
            long? to = null;
            bool hasRange = false;

            if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes=") && !rangeHeader.Contains(','))
            {
                var parts = rangeHeader.Substring("bytes=".Length).Split('-');
                if (parts.Length == 2)
                {
                    if (string.IsNullOrEmpty(parts[0]))
                    {
                        // Suffix range: last N bytes
                        if (long.TryParse(parts[1], out var suffixLength) && suffixLength > 0)
                        {
                            from = Math.Max(0, totalLength - suffixLength);
                            to = totalLength - 1;
                            hasRange = true;
                        }
                    }
                    else if (long.TryParse(parts[0], out var parsedFrom) && parsedFrom >= 0)
                    {
                        from = parsedFrom;
                        hasRange = true;
                        if (!string.IsNullOrEmpty(parts[1]) && long.TryParse(parts[1], out var parsedTo))
                            to = parsedTo;
                    }
                }
                // Malformed ranges fall through as "no range" (initial chunk)
            }

            if (hasRange && (from >= totalLength || (to.HasValue && to.Value < from)))
            {
                Response.Headers["Content-Range"] = $"bytes */{totalLength}";
                return StatusCode(StatusCodes.Status416RangeNotSatisfiable);
            }

            // Initial request without Range: return the first chunk as 206 with total size info
            if (!hasRange)
            {
                from = 0;
                to = Math.Min(6 * 1024 * 1024, totalLength - 1);
            }

            // Open-ended or oversized ranges: cap the response so the player keeps asking
            long rangeEnd = (!to.HasValue || to.Value >= totalLength)
                ? Math.Min(from + (4 * 1024 * 1024), totalLength - 1)
                : to.Value;

            // Kick off (or attach to) the background download that fills the disk cache,
            // so the file is downloaded from Telegram only once
            ProgressiveDownloadInfo downloadInfo = null;
            try
            {
                downloadInfo = await _progressiveDownload.StartOrGetDownloadAsync(cacheFileName, idChannel, dbFile, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not start background cache download for {FileName}", fileName);
            }

            long CachedBytes()
            {
                long onDisk = System.IO.File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
                return Math.Max(onDisk, downloadInfo?.DownloadedBytes ?? 0);
            }

            try
            {
                // If the background download is nearby, wait briefly for it to cover the range
                // start instead of opening a duplicate Telegram fetch (normal sequential playback)
                if (downloadInfo != null && downloadInfo.IsDownloading &&
                    from - CachedBytes() <= WAIT_PROXIMITY_BYTES)
                {
                    var waitTarget = Math.Min(rangeEnd, from + 524288); // at least 512KB past the start
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    while (downloadInfo.IsDownloading &&
                           downloadInfo.DownloadedBytes <= waitTarget &&
                           DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(150, HttpContext.RequestAborted);
                    }
                }

                var cachedBytes = CachedBytes();

                // Serve from the (possibly still growing) cache file
                if (from < cachedBytes)
                {
                    var availableEnd = Math.Min(rangeEnd, cachedBytes - 1);
                    var length = availableEnd - from + 1;

                    _logger.LogDebug("GetFileStreamCached - serving from cache: bytes {From}-{To} of {Total}", from, availableEnd, totalLength);

                    using var cacheStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    cacheStream.Seek(from, SeekOrigin.Begin);
                    var buffer = new byte[length];
                    var bytesRead = await cacheStream.ReadAsync(buffer, 0, (int)length, HttpContext.RequestAborted);

                    Response.StatusCode = StatusCodes.Status206PartialContent;
                    Response.ContentType = mimeType;
                    Response.ContentLength = bytesRead;
                    Response.Headers["Content-Range"] = $"bytes {from}-{from + bytesRead - 1}/{totalLength}";
                    Response.Headers["Accept-Ranges"] = "bytes";
                    Response.Headers["Content-Disposition"] = $"inline; filename=\"{HttpUtility.UrlEncode(fileName)}\"";

                    await Response.Body.WriteAsync(buffer, 0, bytesRead, HttpContext.RequestAborted);
                    return new EmptyResult();
                }

                // Range far ahead of the background download (seek): fetch it directly from
                // Telegram, streaming each 512KB chunk to the response as it arrives
                _logger.LogDebug("GetFileStreamCached - streaming from Telegram: bytes {From}-{To} of {Total}", from, rangeEnd, totalLength);

                // Locate the part (Telegram message) containing `from`. Like GetFileStream,
                // assume uniform part sizes (the splitter uses a fixed split size)
                TL.Message message;
                long partStart = 0;
                long partSize;
                if (messageIds.Count == 1)
                {
                    message = await _ts.getMessageFile(idChannel, messageIds[0]);
                    partSize = ProgressiveDownloadService.GetDocumentSize(message);
                }
                else
                {
                    var firstMessage = await _ts.getMessageFile(idChannel, messageIds[0]);
                    long firstPartSize = ProgressiveDownloadService.GetDocumentSize(firstMessage);
                    if (firstPartSize <= 0)
                    {
                        return NotFound();
                    }
                    int partIndex = (int)Math.Min(from / firstPartSize, messageIds.Count - 1);
                    partStart = (long)partIndex * firstPartSize;
                    message = partIndex == 0 ? firstMessage : await _ts.getMessageFile(idChannel, messageIds[partIndex]);
                    partSize = ProgressiveDownloadService.GetDocumentSize(message);
                }

                if (message == null || partSize <= 0)
                {
                    return NotFound();
                }

                // Serve only up to the end of this part; the player asks for the rest itself
                rangeEnd = Math.Min(rangeEnd, partStart + partSize - 1);
                if (rangeEnd < from)
                {
                    Response.Headers["Content-Range"] = $"bytes */{totalLength}";
                    return StatusCode(StatusCodes.Status416RangeNotSatisfiable);
                }

                // Align down to 512KB for Telegram; skip the prefix when writing the response
                var localFrom = from - partStart;
                var alignedFrom = (localFrom / 524288) * 524288;
                var skipBytes = localFrom - alignedFrom;
                var responseLength = rangeEnd - from + 1;

                await telegramRangeSemaphore.WaitAsync(HttpContext.RequestAborted);
                try
                {
                    Response.StatusCode = StatusCodes.Status206PartialContent;
                    Response.ContentType = mimeType;
                    Response.ContentLength = responseLength;
                    Response.Headers["Content-Range"] = $"bytes {from}-{rangeEnd}/{totalLength}";
                    Response.Headers["Accept-Ranges"] = "bytes";
                    Response.Headers["Content-Disposition"] = $"inline; filename=\"{HttpUtility.UrlEncode(fileName)}\"";

                    long remainingSkip = skipBytes;
                    long remainingWrite = responseLength;

                    await foreach (var chunk in _ts.DownloadFileStreamChunks(
                        message, alignedFrom, skipBytes + responseLength, HttpContext.RequestAborted))
                    {
                        int start = 0;
                        int length = chunk.Length;

                        if (remainingSkip > 0)
                        {
                            var toSkip = (int)Math.Min(remainingSkip, length);
                            start += toSkip;
                            length -= toSkip;
                            remainingSkip -= toSkip;
                        }

                        if (length <= 0) continue;

                        var toWrite = (int)Math.Min(length, remainingWrite);
                        await Response.Body.WriteAsync(chunk, start, toWrite, HttpContext.RequestAborted);
                        remainingWrite -= toWrite;

                        if (remainingWrite <= 0) break;
                    }
                }
                finally
                {
                    telegramRangeSemaphore.Release();
                }

                return new EmptyResult();
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Client closed connection during cached stream - File: {FileName}", fileName);
                return new EmptyResult();
            }
        }

        /// <summary>
        /// Export channel database to JSON file
        /// </summary>
        /// <param name="id">Telegram channel ID</param>
        [HttpGet]
        [Route("export/{id}")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        public async Task<IActionResult> ExportDatabase(string id)
        {
            MemoryStream ms = await _fs.exportAllData(id);
            ms.Position = 0;
            return new FileStreamResult(ms, "application/json")
            {
                FileDownloadName = "tfm_" + id + "_" + _ts.getChatName(Convert.ToInt64(id)) + "_" + ConvertToTimestamp(DateTime.Now) + ".json"

            };

        }

        /// <summary>
        /// Create a shareable TFM file package
        /// </summary>
        /// <param name="id">Telegram channel ID</param>
        /// <param name="bsonId">MongoDB document ID (optional)</param>
        /// <param name="fileName">File name for the package</param>
        [HttpGet]
        [Route("share/{id}")]
        [ProducesResponseType(typeof(FileStreamResult), 200)]
        public async Task<IActionResult> ShareFiles(string id, string? bsonId, string? fileName)
        {
            ShareFilesModel sfm = new ShareFilesModel();
            sfm.id = id;
            sfm.name = fileName;
            sfm.fileName = fileName;
            sfm.invitation = await _ts.getInvitationHash(Convert.ToInt64(id));
            sfm.files = await _fs.ShareFile(id, bsonId);
            var json = System.Text.Json.JsonSerializer.Serialize(sfm);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);

            using (MemoryStream memoryStream = new MemoryStream(bytes))
            {
                memoryStream.Position = 0;

                MemoryStream zippedFile = new MemoryStream(await GetZipArchive(memoryStream, $"{id}-{fileName}.tfm"));
                zippedFile.Position = 0;

                return new FileStreamResult(zippedFile, "application/octet-stream")
                {
                    FileDownloadName = $"{id}-{fileName}.tfm"
                };
            }

        }

        private static async Task<byte[]> GetZipArchive(MemoryStream ms, string fileName)
        {
            byte[] archiveFile;
            using (var archiveStream = new MemoryStream())
            {
                using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true))
                {
                    var zipArchiveEntry = archive.CreateEntry(fileName, CompressionLevel.Optimal);

                    using var zipStream = zipArchiveEntry.Open();
                    await ms.CopyToAsync(zipStream);
                }

                archiveFile = archiveStream.ToArray();
            }

            return archiveFile;
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
