using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;
using TL;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// The bytes behind channel entries, for browsers and players: inline
    /// viewing, downloads, playback (with transcoding when the format needs
    /// it) and message thumbnails. Every URL here lives under <c>/api/v1</c>,
    /// so it is protected like the rest of the API; the matching URLs are
    /// already filled in on every <c>ApiFileDto</c>.
    /// </summary>
    [Route("api/v1/channels/{channelId}")]
    [Tags("Files")]
    [RequireTelegramSession]
    public class MediaController : ApiV1ControllerBase
    {
        /// <summary>How long browsers may keep a thumbnail.</summary>
        public const int ThumbnailMaxAgeSeconds = 7 * 24 * 3600;

        /// <summary>Thumbnails are cached here (next to the channel image cache).</summary>
        public static readonly string ThumbnailCacheFolder = Path.Combine(Path.GetTempPath(), "TFM_Thumbnails");

        private static readonly SemaphoreSlim ThumbnailLock = new SemaphoreSlim(2, 2);

        private readonly IDbService _db;
        private readonly ITelegramService _telegram;
        private readonly ILogger<MediaController> _logger;

        public MediaController(IDbService db, ITelegramService telegram, ILogger<MediaController> logger)
        {
            _db = db;
            _telegram = telegram;
            _logger = logger;
        }

        /// <summary>The bytes of a file, inline or as a download, with range support.</summary>
        /// <remarks>
        /// <c>disposition=inline</c> (default) is meant for <c>&lt;img&gt;</c>,
        /// <c>&lt;audio&gt;</c>, <c>&lt;video&gt;</c> and PDF viewers;
        /// <c>disposition=attachment</c> makes the browser save the file.
        /// Answers <c>200</c> (whole file), <c>206</c> (one <c>Range</c>) or
        /// <c>416</c>. Files already in the server cache (played before or
        /// preloaded) are served from disk. Inline audio and video of a channel
        /// use the progressive cache of the legacy stream endpoint, so the file
        /// is fetched from Telegram once while it plays. Everything else is
        /// streamed straight from Telegram (photos are cached first).
        /// Pass <c>collectionId</c> for entries of a shared collection.
        /// </remarks>
        /// <param name="channelId">Channel that holds the file.</param>
        /// <param name="fileId">Entry id.</param>
        /// <param name="disposition"><c>inline</c> or <c>attachment</c>.</param>
        /// <param name="collectionId">Read the entry from this shared collection.</param>
        [HttpGet("files/{fileId}/content")]
        // application/json too: error answers are JSON envelopes (with only
        // octet-stream MVC finds no formatter for them and answers 406).
        [Produces("application/octet-stream", "application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status206PartialContent)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
        public async Task<IActionResult> Content(
            string channelId,
            string fileId,
            [FromQuery] string? disposition = "inline",
            [FromQuery] string? collectionId = null)
        {
            var attachment = string.Equals(disposition, "attachment", StringComparison.OrdinalIgnoreCase);
            collectionId = FilesController.NormalizeCollection(collectionId);

            BsonFileManagerModel? entry;
            try
            {
                entry = await _db.getFileById(FilesController.DbFor(channelId, collectionId), fileId, FilesController.CollectionFor(collectionId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading file {FileId} of channel {ChannelId}", fileId, channelId);
                return ErrorResult("Could not read the file", ex);
            }
            if (entry == null || !entry.IsFile)
                return NotFoundResult("File not found", ApiErrorCodes.FileNotFound);

            var mime = FileService.getMimeType(entry.Name ?? string.Empty);
            var cachePath = CachePath(channelId, entry);
            SetDisposition(entry.Name, attachment);

            var category = ApiFileDto.CategoryOf(entry.Type);
            if (System.IO.File.Exists(cachePath))
            {
                // Photo sizes in the index are not always the size Telegram serves.
                var cached = new FileInfo(cachePath).Length;
                if (cached >= entry.Size || (category == "Photo" && cached > 0))
                    return PhysicalFile(cachePath, mime, enableRangeProcessing: true);
            }

            if (!attachment && collectionId == null && (category == "Audio" || category == "Video"))
            {
                // Same progressive cache the web player uses.
                return await ServeProgressive(channelId, entry, mime);
            }

            var messageIds = ProgressiveDownloadService.GetMessageIds(entry);
            if (messageIds == null || messageIds.Count == 0)
                return NotFoundResult("The file has no Telegram message", ApiErrorCodes.FileNotFound);

            try
            {
                var first = await _telegram.getMessageFile(channelId, messageIds[0]);
                if (first == null)
                    return NotFoundResult("The Telegram message of this file was not found", ApiErrorCodes.FileNotFound);

                if (first.media is MessageMediaPhoto)
                {
                    // Photos cannot be fetched by ranges: cache the whole (small) file, then serve it.
                    await DownloadWholeToCache(first, cachePath);
                    return PhysicalFile(cachePath, mime, enableRangeProcessing: true);
                }

                return await StreamFromTelegram(channelId, entry, messageIds, first, mime);
            }
            catch (OperationCanceledException)
            {
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serving file {FileId} of channel {ChannelId}", fileId, channelId);
                if (Response.HasStarted) return new EmptyResult();
                return ErrorResult("Could not read the file from Telegram", ex);
            }
        }

        /// <summary>Video playback, transcoded to MP4 when the browser cannot play the format.</summary>
        /// <remarks>
        /// Formats browsers play natively (<c>mp4</c>, <c>webm</c>, <c>ogg</c>...)
        /// redirect to <c>content</c>. Others (<c>mkv</c>, <c>avi</c>, <c>wmv</c>...)
        /// are downloaded to the server cache and transcoded on the fly with
        /// FFmpeg (no seeking); this needs <c>enableVideoTranscoding</c> and
        /// FFmpeg (<c>GET /api/v1/system/ffmpeg</c>), otherwise <c>403</c> or a
        /// redirect to the direct stream. Same behaviour as <c>/api/video/stream</c>.
        /// </remarks>
        [HttpGet("files/{fileId}/stream")]
        [Produces("video/mp4", "application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status302Found)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Stream(string channelId, string fileId)
        {
            var entry = await _db.getFileById(channelId, fileId);
            if (entry == null || !entry.IsFile)
                return NotFoundResult("File not found", ApiErrorCodes.FileNotFound);

            var name = entry.Name ?? "video";
            if (!MediaUrlBuilder.NeedsTranscoding(name))
                return Redirect(MediaUrlBuilder.Content(BaseUrl, channelId, fileId));

            if (!GeneralConfigStatic.config.EnableVideoTranscoding)
                return ForbiddenResult("This video format needs transcoding. Enable enableVideoTranscoding in the configuration.");

            var legacy = ActivatorUtilities.CreateInstance<VideoStreamController>(HttpContext.RequestServices);
            legacy.ControllerContext = ControllerContext;
            return await legacy.StreamVideo(channelId, fileId, name);
        }

        /// <summary>The attachment of a message, without indexing it first.</summary>
        /// <remarks>
        /// Plays or downloads the file of any message of a chat straight from
        /// Telegram, by message id: the channel index is not read nor changed.
        /// Same behaviour as <c>files/{fileId}/content</c>: <c>disposition=inline</c>
        /// (default) for <c>&lt;video&gt;</c>, <c>&lt;audio&gt;</c>, <c>&lt;img&gt;</c>,
        /// <c>disposition=attachment</c> to save it; <c>200</c>, <c>206</c> (one
        /// <c>Range</c>) or <c>416</c>. Inline audio and video go through the
        /// progressive disk cache (fetched from Telegram once while they play,
        /// shared with the indexed entry of the same message); photos are cached
        /// whole; anything else is streamed. The type comes from the Telegram
        /// document (<c>mime_type</c>), the name from its file name.
        /// Answers <c>404</c> for messages without a file.
        /// </remarks>
        /// <param name="channelId">Chat that holds the message.</param>
        /// <param name="messageId">Telegram message id.</param>
        /// <param name="disposition"><c>inline</c> or <c>attachment</c>.</param>
        [HttpGet("messages/{messageId:int}/content")]
        [Produces("application/octet-stream", "application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status206PartialContent)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
        public async Task<IActionResult> MessageContent(string channelId, int messageId, [FromQuery] string? disposition = "inline")
        {
            if (!long.TryParse(channelId, out _))
                return BadRequestResult("channelId must be numeric");
            var attachment = string.Equals(disposition, "attachment", StringComparison.OrdinalIgnoreCase);

            Message? message;
            try
            {
                message = await _telegram.getMessageFile(channelId, messageId);
            }
            catch (KeyNotFoundException)
            {
                return NotFoundResult("Channel not found", ApiErrorCodes.ChannelNotFound);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read message {MessageId} of chat {ChannelId}", messageId, channelId);
                return ErrorResult("Could not read the message from Telegram", ex);
            }

            var file = message == null ? null : MessageAttachment.From(message);
            if (message == null || file == null)
                return NotFoundResult("This message has no file", ApiErrorCodes.FileNotFound);

            var entry = file.ToEntry(messageId);
            var cachePath = CachePath(channelId, entry);
            SetDisposition(file.Name, attachment);

            try
            {
                if (file.IsPhoto)
                {
                    // Photos cannot be fetched by ranges: cache the whole (small) file, then serve it.
                    if (!System.IO.File.Exists(cachePath) || new FileInfo(cachePath).Length == 0)
                        await DownloadWholeToCache(message, cachePath);
                    return PhysicalFile(cachePath, file.MimeType, enableRangeProcessing: true);
                }

                if (System.IO.File.Exists(cachePath) && new FileInfo(cachePath).Length >= file.Size)
                    return PhysicalFile(cachePath, file.MimeType, enableRangeProcessing: true);

                if (!attachment && (file.Category == "Audio" || file.Category == "Video"))
                    return await ServeProgressive(channelId, entry, file.MimeType);

                return await StreamFromTelegram(channelId, entry, new List<int> { messageId }, message, file.MimeType);
            }
            catch (OperationCanceledException)
            {
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serving the file of message {MessageId} in chat {ChannelId}", messageId, channelId);
                if (Response.HasStarted) return new EmptyResult();
                return ErrorResult("Could not read the file from Telegram", ex);
            }
        }

        /// <summary>The progressive cache of the legacy stream endpoint, for an entry resolved here.</summary>
        private Task<IActionResult> ServeProgressive(string channelId, BsonFileManagerModel entry, string mime)
        {
            var legacy = ActivatorUtilities.CreateInstance<FileController>(HttpContext.RequestServices);
            legacy.ControllerContext = ControllerContext;
            return legacy.ServeProgressive(channelId, entry, mime);
        }

        /// <summary>JPEG thumbnail of a photo or video message.</summary>
        /// <remarks>
        /// Photos return one of their small sizes; videos (and documents that
        /// carry one) return their embedded preview. Thumbnails are cached on
        /// disk and sent with a one-week <c>Cache-Control</c>. Messages without
        /// a preview answer <c>404</c> (also cached).
        /// </remarks>
        [HttpGet("messages/{messageId:int}/thumbnail")]
        [Produces("image/jpeg", "application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Thumbnail(string channelId, int messageId)
        {
            if (!long.TryParse(channelId, out _))
                return BadRequestResult("channelId must be numeric");

            Directory.CreateDirectory(ThumbnailCacheFolder);
            var path = Path.Combine(ThumbnailCacheFolder, $"{channelId}_{messageId}.jpg");
            var missing = path + ".none";

            if (System.IO.File.Exists(path)) return CachedThumbnail(path);
            if (System.IO.File.Exists(missing) && System.IO.File.GetLastWriteTimeUtc(missing) > DateTime.UtcNow.AddDays(-1))
                return NotFoundResult("This message has no thumbnail", ApiErrorCodes.FileNotFound);

            await ThumbnailLock.WaitAsync(HttpContext.RequestAborted);
            try
            {
                if (System.IO.File.Exists(path)) return CachedThumbnail(path);

                var message = await _telegram.getMessageFile(channelId, messageId);
                var bytes = message == null ? null : await _telegram.downloadMessageThumb(message);
                if (bytes == null || bytes.Length == 0)
                {
                    await System.IO.File.WriteAllTextAsync(missing, string.Empty);
                    return NotFoundResult("This message has no thumbnail", ApiErrorCodes.FileNotFound);
                }

                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await System.IO.File.WriteAllBytesAsync(temp, bytes);
                System.IO.File.Move(temp, path, overwrite: true);
                return CachedThumbnail(path);
            }
            catch (KeyNotFoundException)
            {
                return NotFoundResult("Channel not found", ApiErrorCodes.ChannelNotFound);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not get the thumbnail of message {MessageId} in channel {ChannelId}", messageId, channelId);
                return ErrorResult("Could not get the thumbnail", ex);
            }
            finally
            {
                ThumbnailLock.Release();
            }
        }

        private IActionResult CachedThumbnail(string path)
        {
            Response.Headers[HeaderNames.CacheControl] = $"private, max-age={ThumbnailMaxAgeSeconds}";
            return PhysicalFile(path, "image/jpeg", enableRangeProcessing: false);
        }

        /// <summary>Same cache file name as the legacy stream and preload code.</summary>
        private static string CachePath(string channelId, BsonFileManagerModel entry)
        {
            var folder = Path.Combine(FileService.TEMPDIR, "_temp");
            Directory.CreateDirectory(folder);
            var key = entry.MessageId != null ? entry.MessageId.ToString() : entry.Id;
            return Path.Combine(folder, $"{channelId}-{key}-{entry.Name}");
        }

        private void SetDisposition(string? fileName, bool attachment)
        {
            var name = HttpUtility.UrlEncode(fileName ?? "file");
            Response.Headers[HeaderNames.ContentDisposition] =
                $"{(attachment ? "attachment" : "inline")}; filename=\"{name}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName ?? "file")}";
        }

        private async Task DownloadWholeToCache(Message message, string cachePath)
        {
            // Photos already saved by the chat view are reused as they are
            // (DownloadFileAndReturn skips those and writes nothing).
            if (message.media is MessageMediaPhoto { photo: Photo photo })
            {
                var saved = Path.Combine(FileService.IMGDIR, $"{photo.id}.jpg");
                if (System.IO.File.Exists(saved))
                {
                    System.IO.File.Copy(saved, cachePath, overwrite: true);
                    return;
                }
            }

            var temp = cachePath + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
                    await _telegram.DownloadFileAndReturn(new ChatMessages { message = message }, fs);
                if (new FileInfo(temp).Length == 0)
                    throw new InvalidOperationException("Telegram returned no data for this photo");
                System.IO.File.Move(temp, cachePath, overwrite: true);
            }
            finally
            {
                if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp);
            }
        }

        /// <summary>
        /// Streams the requested range of a (possibly split) document straight
        /// from Telegram, part by part, in 512 KB blocks.
        /// </summary>
        private async Task<IActionResult> StreamFromTelegram(string channelId, BsonFileManagerModel entry, List<int> messageIds, Message first, string mime)
        {
            var total = entry.Size;
            var firstPartSize = ProgressiveDownloadService.GetDocumentSize(first);
            if (firstPartSize <= 0)
                return NotFoundResult("The Telegram message of this file holds no document", ApiErrorCodes.FileNotFound);
            if (messageIds.Count == 1 || total <= 0) total = Math.Max(total, firstPartSize);

            var kind = HttpRangeParser.Parse(Request.Headers[HeaderNames.Range].ToString(), total, out var range);
            Response.Headers[HeaderNames.AcceptRanges] = "bytes";
            if (kind == RangeKind.Unsatisfiable)
            {
                Response.Headers[HeaderNames.ContentRange] = $"bytes */{total}";
                return StatusCode(StatusCodes.Status416RangeNotSatisfiable);
            }

            Response.StatusCode = kind == RangeKind.Partial ? StatusCodes.Status206PartialContent : StatusCodes.Status200OK;
            Response.ContentType = mime;
            Response.ContentLength = range.Length;
            if (kind == RangeKind.Partial)
                Response.Headers[HeaderNames.ContentRange] = $"bytes {range.From}-{range.To}/{total}";

            var ct = HttpContext.RequestAborted;
            var position = range.From;
            while (position <= range.To)
            {
                var (partIndex, partStart) = SplitFileLayout.Locate(position, firstPartSize, messageIds.Count);
                var message = partIndex == 0 ? first : await _telegram.getMessageFile(channelId, messageIds[partIndex]);
                var partSize = ProgressiveDownloadService.GetDocumentSize(message);
                if (message == null || partSize <= 0) break;

                var partEnd = Math.Min(range.To, partStart + partSize - 1);
                var (aligned, skip) = SplitFileLayout.Align(position - partStart);
                var toWrite = partEnd - position + 1;
                long remainingSkip = skip;

                await foreach (var chunk in _telegram.DownloadFileStreamChunks(message, aligned, skip + toWrite, ct))
                {
                    var start = 0;
                    var length = chunk.Length;
                    if (remainingSkip > 0)
                    {
                        var s = (int)Math.Min(remainingSkip, length);
                        start += s;
                        length -= s;
                        remainingSkip -= s;
                    }
                    if (length <= 0) continue;

                    var n = (int)Math.Min(length, toWrite);
                    await Response.Body.WriteAsync(chunk.AsMemory(start, n), ct);
                    toWrite -= n;
                    position += n;
                    if (toWrite <= 0) break;
                }

                if (toWrite > 0) break; // Telegram ended the part early: stop rather than loop.
            }

            return new EmptyResult();
        }
    }
}
