using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Mvc;

using Syncfusion.Blazor.FileManager;

using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;

namespace TelegramDownloader.Controllers.WebDav
{
    /// <summary>
    /// Native WebDAV endpoint (C# rewrite that replaces the Python proxy).
    ///
    /// Phase 1 implements the read side correctly — OPTIONS, PROPFIND, HEAD and
    /// GET (full file as 200, or a byte range as 206) — reusing the disk-cached,
    /// download-once streaming of <see cref="IProgressiveDownloadService"/>. This
    /// fixes the proxy's read bugs: truncated 6 MB GETs, no caching, PROPFIND
    /// hiding single-child folders, and the always-206 behaviour.
    ///
    /// Phase 0 already added PUT (below), which reuses the regular
    /// server-&gt;Telegram upload pipeline (single upload at a time, 2/4 GB split).
    /// MKCOL/DELETE/MOVE/LOCK arrive in Phase 2.
    ///
    /// Mounted at <c>/webdav/{channel}/{**path}</c>. Paths map to the channel
    /// index: a file resolves by <c>FilePath</c>, a directory lists children by
    /// <c>FilterPath</c> (the same convention the file manager uses).
    /// </summary>
    [ApiController]
    public class WebDavServerController : ControllerBase
    {
        private readonly IFileService _files;
        private readonly IDbService _db;
        private readonly ChannelFolderResolver _resolver;
        private readonly IProgressiveDownloadService _progressiveDownload;
        private readonly WebDavLockManager _locks;
        private readonly ILogger<WebDavServerController> _logger;

        // An upload can be slow: it may sit behind other uploads in the single
        // upload queue and then transfer over Telegram. Cap how long a PUT waits.
        private static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(30);

        // How long a GET waits for the background cache download to create the file.
        private static readonly TimeSpan FirstByteTimeout = TimeSpan.FromSeconds(30);

        private const string Dav = "DAV:";

        public WebDavServerController(
            IFileService files,
            IDbService db,
            ChannelFolderResolver resolver,
            IProgressiveDownloadService progressiveDownload,
            WebDavLockManager locks,
            ILogger<WebDavServerController> logger)
        {
            _files = files;
            _db = db;
            _resolver = resolver;
            _progressiveDownload = progressiveDownload;
            _locks = locks;
            _logger = logger;
        }

        // ---------------------------------------------------------------- OPTIONS

        [AcceptVerbs("OPTIONS")]
        [Route("webdav/{**path}")]
        public IActionResult Options(string? path)
        {
            Response.Headers["DAV"] = "1,2";
            Response.Headers["Allow"] = "OPTIONS, PROPFIND, HEAD, GET, PUT, MKCOL, DELETE, MOVE, LOCK, UNLOCK";
            Response.Headers["MS-Author-Via"] = "DAV";
            Response.ContentLength = 0;
            return Ok();
        }

        // --------------------------------------------------------------- PROPFIND

        [AcceptVerbs("PROPFIND")]
        [Route("webdav/{channel}/{**path}")]
        public async Task<IActionResult> PropFind(string channel, string? path)
        {
            if (!IsAuthorized()) return Challenge401();
            if (string.IsNullOrWhiteSpace(channel)) return BadRequest("channel is required");

            var depth = Request.Headers["Depth"].ToString();
            if (string.IsNullOrEmpty(depth)) depth = "1";

            var inner = NormalizeInner(path);            // "/" or "/folder" or "/folder/file.ext"
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.Append($"<D:multistatus xmlns:D=\"{Dav}\">");

            if (inner == "/")
            {
                // Channel root is always a collection.
                AppendResponse(sb, channel, inner, channel, isDir: true, size: 0, type: null, modified: DateTime.UtcNow);
                if (depth != "0")
                    foreach (var child in await _db.getAllFilesInDirectoryPath(channel, "/"))
                        AppendChild(sb, channel, "/", child);
            }
            else
            {
                var node = await _db.getFileByPath(channel, inner);
                if (node == null)
                {
                    sb.Clear();
                    return NotFound();
                }

                if (node.IsFile)
                {
                    AppendResponse(sb, channel, inner, node.Name, isDir: false, size: node.Size, type: node.Type, modified: node.DateModified);
                }
                else
                {
                    AppendResponse(sb, channel, inner, node.Name, isDir: true, size: 0, type: null, modified: node.DateModified);
                    if (depth != "0")
                        foreach (var child in await _db.getAllFilesInDirectoryPath(channel, inner + "/"))
                            AppendChild(sb, channel, inner + "/", child);
                }
            }

            sb.Append("</D:multistatus>");
            return new ContentResult
            {
                Content = sb.ToString(),
                ContentType = "application/xml; charset=utf-8",
                StatusCode = StatusCodes.Status207MultiStatus
            };
        }

        // ---------------------------------------------------------------- HEAD/GET

        [HttpHead]
        [Route("webdav/{channel}/{**path}")]
        public async Task<IActionResult> Head(string channel, string? path)
        {
            if (!IsAuthorized()) return Challenge401();
            var node = await ResolveFile(channel, path);
            if (node == null) return NotFound();

            Response.Headers["Accept-Ranges"] = "bytes";
            Response.ContentType = FileService.getMimeType(node.Type);
            Response.ContentLength = node.Size;
            return new EmptyResult();
        }

        [HttpGet]
        [Route("webdav/{channel}/{**path}")]
        public async Task<IActionResult> Get(string channel, string? path)
        {
            if (!IsAuthorized()) return Challenge401();
            var node = await ResolveFile(channel, path);
            if (node == null) return NotFound();

            var ct = HttpContext.RequestAborted;
            long totalLength = node.Size;
            var mimeType = FileService.getMimeType(node.Type);

            // ---- Parse Range (bytes=X-, bytes=X-Y, bytes=-N) ----
            long from = 0, to = totalLength - 1;
            bool hasRange = false;
            var rangeHeader = Request.Headers["Range"].ToString();
            if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes=") && !rangeHeader.Contains(','))
            {
                var parts = rangeHeader.Substring("bytes=".Length).Split('-');
                if (parts.Length == 2)
                {
                    if (string.IsNullOrEmpty(parts[0]))
                    {
                        if (long.TryParse(parts[1], out var suffix) && suffix > 0)
                        {
                            from = Math.Max(0, totalLength - suffix);
                            to = totalLength - 1;
                            hasRange = true;
                        }
                    }
                    else if (long.TryParse(parts[0], out var f) && f >= 0)
                    {
                        from = f;
                        hasRange = true;
                        if (!string.IsNullOrEmpty(parts[1]) && long.TryParse(parts[1], out var t))
                            to = Math.Min(t, totalLength - 1);
                    }
                }
            }

            if (hasRange && (from >= totalLength || to < from))
            {
                Response.Headers["Content-Range"] = $"bytes */{totalLength}";
                return StatusCode(StatusCodes.Status416RangeNotSatisfiable);
            }

            long length = totalLength == 0 ? 0 : to - from + 1;

            // ---- Locate / prime the download-once disk cache ----
            var cacheFileName = $"{channel}-{(node.MessageId != null ? node.MessageId.ToString() : node.Id)}-{node.Name}";
            var tempPath = Path.Combine(FileService.TEMPDIR, "_temp");
            Directory.CreateDirectory(tempPath);
            var filePath = Path.Combine(tempPath, cacheFileName);

            bool fullyCached = System.IO.File.Exists(filePath) && new FileInfo(filePath).Length >= totalLength;

            if (!fullyCached && length > 0)
            {
                try
                {
                    await _progressiveDownload.StartOrGetDownloadAsync(cacheFileName, channel, node, filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "WebDAV GET: could not start cache download for {File}", node.Name);
                }

                // Wait for the first bytes to hit disk before committing a status code,
                // so a failure here can still return 503 instead of a half-written 200.
                var deadline = DateTime.UtcNow.Add(FirstByteTimeout);
                while (!System.IO.File.Exists(filePath))
                {
                    if (DateTime.UtcNow > deadline)
                        return StatusCode(StatusCodes.Status503ServiceUnavailable, "cache download did not start");
                    try { await Task.Delay(100, ct); } catch (OperationCanceledException) { return new EmptyResult(); }
                }
            }

            // ---- Write status + headers ----
            Response.Headers["Accept-Ranges"] = "bytes";
            Response.ContentType = mimeType;
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{Uri.EscapeDataString(node.Name)}\"";
            Response.ContentLength = length;
            if (hasRange)
            {
                Response.StatusCode = StatusCodes.Status206PartialContent;
                Response.Headers["Content-Range"] = $"bytes {from}-{to}/{totalLength}";
            }
            else
            {
                Response.StatusCode = StatusCodes.Status200OK;
            }

            if (length == 0) return new EmptyResult();

            // ---- Stream the bytes from the (possibly still growing) cache file ----
            try
            {
                await using var cache = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, useAsync: true);
                cache.Seek(from, SeekOrigin.Begin);

                long remaining = length;
                var buffer = new byte[64 * 1024];
                while (remaining > 0 && !ct.IsCancellationRequested)
                {
                    int toRead = (int)Math.Min(buffer.Length, remaining);
                    int n = await cache.ReadAsync(buffer, 0, toRead, ct);
                    if (n > 0)
                    {
                        await Response.Body.WriteAsync(buffer.AsMemory(0, n), ct);
                        remaining -= n;
                        continue;
                    }

                    // At current EOF of the growing cache: wait if the download is still running.
                    var info = _progressiveDownload.GetDownloadInfo(cacheFileName);
                    if (info != null && info.IsDownloading)
                    {
                        await Task.Delay(100, ct);
                        continue;
                    }

                    // Download stopped. Give the file one last chance (data may have flushed).
                    if (cache.Length > cache.Position) continue;
                    _logger.LogWarning("WebDAV GET: cache stopped short for {File} ({Remaining} bytes missing)", node.Name, remaining);
                    break;
                }

                await Response.Body.FlushAsync(ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("WebDAV GET: client closed connection for {File}", node.Name);
            }

            return new EmptyResult();
        }

        // ---------------------------------------------------------------- PUT

        [AcceptVerbs("PUT")]
        [Route("webdav/{channel}/{**path}")]
        [RequestSizeLimit(long.MaxValue)]
        public async Task<IActionResult> Put(string channel, string path)
        {
            if (!IsAuthorized()) return Challenge401();

            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(path))
                return BadRequest("channel and path are required");

            // Partial PUT (Content-Range) is not part of the WebDAV spec and the
            // Telegram backend is append-only; reject it explicitly.
            if (Request.Headers.ContainsKey("Content-Range"))
                return StatusCode(StatusCodes.Status501NotImplemented, "partial PUT is not supported");

            path = path.Replace('\\', '/').Trim('/');
            var lastSlash = path.LastIndexOf('/');
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(fileName))
                return BadRequest("a file name is required");

            // The destination folder MUST carry a trailing slash: the upload pipeline
            // resolves the parent via `FilterPath + Name + "/"` and builds the child's
            // FilePath with Path.Combine (which only stays forward-slashed when the base
            // ends in a separator). Reuse the same normaliser the REST upload uses.
            var folderRaw = lastSlash < 0 ? string.Empty : path.Substring(0, lastSlash);
            var folder = ChannelFolderResolver.NormalizeFolderPath(folderRaw);

            var stagingRelative = $"{ApiUploadStaging.FolderName}/{Guid.NewGuid():N}";
            var stagingAbsolute = Path.Combine(FileService.LOCALDIR, stagingRelative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(stagingAbsolute);
            var stagedFile = Path.Combine(stagingAbsolute, fileName);

            try
            {
                long size;
                await using (var fs = System.IO.File.Create(stagedFile))
                {
                    await Request.Body.CopyToAsync(fs, HttpContext.RequestAborted);
                    size = fs.Length;
                }

                if (size == 0)
                {
                    // Telegram can't store an empty message: represent 0-byte files as
                    // index-only nodes (no Telegram upload). The GET path already serves
                    // Size==0 with an empty body without touching Telegram.
                    TryCleanup(stagingAbsolute);
                    var innerPath = folder + fileName; // folder is normalized with a trailing slash
                    var existing = await _db.getFileByPath(channel, innerPath);
                    if (existing != null)
                        await _files.oneItemDeleteAsync(channel, ChannelFolderResolver.ToContent(existing));
                    await _files.CreateEmptyFile(channel, folder, fileName);
                    _logger.LogInformation("WebDAV PUT created empty file {File} in channel {Channel}", fileName, channel);
                    return StatusCode(existing != null ? StatusCodes.Status204NoContent : StatusCodes.Status201Created);
                }

                var content = new FileManagerDirectoryContent
                {
                    Name = fileName,
                    IsFile = true,
                    Size = size,
                    FilterPath = "/" + stagingRelative + "/",
                    Type = Path.GetExtension(fileName)
                };

                // Pass our own task model so we can await THIS upload (no LastOrDefault race).
                var task = new InfoDownloadTaksModel();
                await _files.AddUploadFileFromServer(channel, folder,
                    new List<FileManagerDirectoryContent> { content }, task);

                var terminal = await WaitForCompletionAsync(task, HttpContext.RequestAborted);

                switch (terminal)
                {
                    case StateTask.Completed:
                        _logger.LogInformation("WebDAV PUT stored {File} ({Size} bytes) in channel {Channel}", fileName, size, channel);
                        return StatusCode(StatusCodes.Status201Created);
                    case StateTask.Error:
                        return StatusCode(StatusCodes.Status500InternalServerError, "upload failed");
                    case StateTask.Canceled:
                        return StatusCode(StatusCodes.Status499ClientClosedRequest);
                    default:
                        _logger.LogWarning("WebDAV PUT timed out waiting for upload of {File} to channel {Channel}", fileName, channel);
                        return StatusCode(StatusCodes.Status504GatewayTimeout, "upload still in progress");
                }
            }
            catch (OperationCanceledException)
            {
                TryCleanup(stagingAbsolute);
                return StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebDAV PUT failed for {File} in channel {Channel}", fileName, channel);
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        // ---------------------------------------------------------------- MKCOL

        [AcceptVerbs("MKCOL")]
        [Route("webdav/{channel}/{**path}")]
        public async Task<IActionResult> MkCol(string channel, string? path)
        {
            if (!IsAuthorized()) return Challenge401();

            // A MKCOL request body is not defined by the spec.
            if (Request.ContentLength.GetValueOrDefault() > 0)
                return StatusCode(StatusCodes.Status415UnsupportedMediaType);

            var inner = NormalizeInner(path);
            if (inner == "/") return StatusCode(StatusCodes.Status405MethodNotAllowed, "the root already exists");

            if (await _db.getFileByPath(channel, inner) != null)
                return StatusCode(StatusCodes.Status405MethodNotAllowed, "resource already exists");

            var trimmed = inner.Trim('/');
            var idx = trimmed.LastIndexOf('/');
            var folderName = idx < 0 ? trimmed : trimmed.Substring(idx + 1);
            var parentPath = idx < 0 ? "/" : "/" + trimmed.Substring(0, idx) + "/";

            var parent = await _resolver.ResolveFolder(channel, null, parentPath);
            if (parent == null || parent.IsFile)
                return StatusCode(StatusCodes.Status409Conflict, "parent folder does not exist");

            await _files.createFolder(channel, ChannelFolderResolver.CreateChildPath(parent), folderName, ChannelFolderResolver.ToContent(parent));
            return StatusCode(StatusCodes.Status201Created);
        }

        // ---------------------------------------------------------------- DELETE

        [HttpDelete]
        [Route("webdav/{channel}/{**path}")]
        public async Task<IActionResult> Delete(string channel, string? path)
        {
            if (!IsAuthorized()) return Challenge401();

            var inner = NormalizeInner(path);
            if (inner == "/") return StatusCode(StatusCodes.Status403Forbidden, "cannot delete the channel root");

            var node = await _db.getFileByPath(channel, inner);
            if (node == null) return NotFound();

            await _files.oneItemDeleteAsync(channel, ChannelFolderResolver.ToContent(node));
            return NoContent();
        }

        // ---------------------------------------------------------------- MOVE

        [AcceptVerbs("MOVE")]
        [Route("webdav/{channel}/{**path}")]
        public async Task<IActionResult> Move(string channel, string? path)
        {
            if (!IsAuthorized()) return Challenge401();

            var inner = NormalizeInner(path);
            if (inner == "/") return StatusCode(StatusCodes.Status403Forbidden, "cannot move the channel root");

            var node = await _db.getFileByPath(channel, inner);
            if (node == null) return NotFound();

            var dest = ParseDestination(Request.Headers["Destination"].ToString());
            if (dest == null) return BadRequest("a valid Destination header is required");
            var (destChannel, destInner) = dest.Value;
            if (destChannel != channel)
                return StatusCode(StatusCodes.Status502BadGateway, "cross-channel MOVE is not supported");
            if (destInner == "/") return BadRequest("invalid destination");

            var overwrite = Request.Headers["Overwrite"].ToString();
            var existing = await _db.getFileByPath(channel, destInner);
            bool destExisted = existing != null;
            if (destExisted)
            {
                if (string.Equals(overwrite, "F", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(StatusCodes.Status412PreconditionFailed);
                await _files.oneItemDeleteAsync(channel, ChannelFolderResolver.ToContent(existing!));
            }

            // Split source and destination into (parent folder, name).
            SplitPath(inner, out var srcParent, out var srcName);
            SplitPath(destInner, out var dstParent, out var dstName);

            if (srcParent == dstParent)
            {
                // Same folder: pure rename (the common temp -> final case).
                if (srcName != dstName)
                    await _db.updateName(channel, node.Id, dstName, srcName, node.IsFile, node.FilterPath);
            }
            else
            {
                var destParent = await _resolver.ResolveFolder(channel, null, dstParent);
                if (destParent == null || destParent.IsFile)
                    return StatusCode(StatusCodes.Status409Conflict, "destination folder does not exist");

                var destChildPath = ChannelFolderResolver.ChildFolderPath(destParent);
                await _files.CopyOrMoveItems(channel,
                    new[] { ChannelFolderResolver.ToContent(node) },
                    destChildPath,
                    ChannelFolderResolver.ToContent(destParent),
                    isCopy: false);

                if (srcName != dstName)
                {
                    var moved = (await _db.getAllFilesInDirectoryPath(channel, destChildPath))
                        .FirstOrDefault(x => x.Name == srcName);
                    if (moved != null)
                        await _db.updateName(channel, moved.Id, dstName, srcName, moved.IsFile, destChildPath);
                }
            }

            return destExisted ? NoContent() : StatusCode(StatusCodes.Status201Created);
        }

        // ---------------------------------------------------------------- LOCK / UNLOCK

        [AcceptVerbs("LOCK")]
        [Route("webdav/{channel}/{**path}")]
        public async Task<IActionResult> Lock(string channel, string? path)
        {
            if (!IsAuthorized()) return Challenge401();

            var inner = NormalizeInner(path);
            var key = channel + ":" + inner;
            var timeout = _locks.ClampTimeout(ParseTimeoutHeader(Request.Headers["Timeout"].ToString()));

            string token;
            // Refresh: a LOCK carrying the existing token in the If header (no body).
            var ifToken = ExtractToken(Request.Headers["If"].ToString());
            if (!string.IsNullOrEmpty(ifToken) && _locks.Refresh(key, ifToken, timeout))
            {
                token = ifToken;
            }
            else
            {
                var owner = await ReadOwnerFromBodyAsync();
                var acquired = _locks.TryAcquire(key, owner, timeout);
                if (acquired == null)
                    return StatusCode(StatusCodes.Status423Locked);
                token = acquired;
            }

            var exists = inner == "/" || await _db.getFileByPath(channel, inner) != null;
            Response.Headers["Lock-Token"] = "<" + token + ">";
            return new ContentResult
            {
                Content = BuildLockDiscoveryXml(channel, inner, token, timeout),
                ContentType = "application/xml; charset=utf-8",
                // 200 for an existing resource; 201 when the lock creates a lock-null resource.
                StatusCode = exists ? StatusCodes.Status200OK : StatusCodes.Status201Created
            };
        }

        [AcceptVerbs("UNLOCK")]
        [Route("webdav/{channel}/{**path}")]
        public IActionResult Unlock(string channel, string? path)
        {
            if (!IsAuthorized()) return Challenge401();

            var token = ExtractToken(Request.Headers["Lock-Token"].ToString());
            if (string.IsNullOrEmpty(token))
                return BadRequest("a Lock-Token header is required");

            _locks.Release(channel + ":" + NormalizeInner(path), token);
            return NoContent(); // lenient: 204 even if the token was unknown/expired
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Resolves a WebDAV path to a file node, or null if missing / a directory.</summary>
        private async Task<BsonFileManagerModel?> ResolveFile(string channel, string? path)
        {
            if (string.IsNullOrWhiteSpace(channel)) return null;
            var inner = NormalizeInner(path);
            if (inner == "/") return null; // the root is a collection, not a file
            var node = await _db.getFileByPath(channel, inner);
            return (node != null && node.IsFile) ? node : null;
        }

        /// <summary>WebDAV sub-path → index path ("/", "/folder", "/folder/file.ext").</summary>
        private static string NormalizeInner(string? path)
        {
            var p = (path ?? string.Empty).Replace('\\', '/').Trim('/');
            return p.Length == 0 ? "/" : "/" + p;
        }

        /// <summary>Splits an index path into its parent folder path ("/", "/a/") and leaf name.</summary>
        private static void SplitPath(string inner, out string parent, out string name)
        {
            var trimmed = inner.Trim('/');
            var idx = trimmed.LastIndexOf('/');
            name = idx < 0 ? trimmed : trimmed.Substring(idx + 1);
            parent = idx < 0 ? "/" : "/" + trimmed.Substring(0, idx) + "/";
        }

        /// <summary>Parses a WebDAV Destination header into (channel, inner path), or null if invalid.</summary>
        private static (string Channel, string Inner)? ParseDestination(string destination)
        {
            if (string.IsNullOrWhiteSpace(destination)) return null;

            string absPath;
            if (Uri.TryCreate(destination, UriKind.Absolute, out var abs))
                absPath = abs.AbsolutePath;
            else
                absPath = destination;

            absPath = Uri.UnescapeDataString(absPath);
            const string prefix = "/webdav/";
            var i = absPath.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;

            var rest = absPath.Substring(i + prefix.Length).Trim('/');
            if (rest.Length == 0) return null;

            var slash = rest.IndexOf('/');
            if (slash < 0) return (rest, "/");
            return (rest.Substring(0, slash), "/" + rest.Substring(slash + 1).Trim('/'));
        }

        private void AppendChild(StringBuilder sb, string channel, string parentInner, BsonFileManagerModel child)
        {
            var childInner = (parentInner == "/" ? "/" : parentInner) + child.Name;
            AppendResponse(sb, channel, childInner, child.Name, !child.IsFile, child.Size, child.Type, child.DateModified);
        }

        private void AppendResponse(StringBuilder sb, string channel, string inner, string displayName, bool isDir, long size, string? type, DateTime modified)
        {
            var href = BuildHref(channel, inner, isDir);
            sb.Append("<D:response>");
            sb.Append($"<D:href>{XmlEscape(href)}</D:href>");
            sb.Append("<D:propstat><D:prop>");
            sb.Append($"<D:displayname>{XmlEscape(displayName)}</D:displayname>");
            if (isDir)
            {
                sb.Append("<D:resourcetype><D:collection/></D:resourcetype>");
            }
            else
            {
                sb.Append("<D:resourcetype/>");
                sb.Append($"<D:getcontentlength>{size}</D:getcontentlength>");
                sb.Append($"<D:getcontenttype>{XmlEscape(FileService.getMimeType(type))}</D:getcontenttype>");
            }
            sb.Append($"<D:getlastmodified>{modified.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture)}</D:getlastmodified>");
            sb.Append($"<D:creationdate>{modified.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}</D:creationdate>");
            sb.Append("</D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>");
            sb.Append("</D:response>");
        }

        /// <summary>Builds a URL-encoded absolute href under /webdav/{channel}/... .</summary>
        private static string BuildHref(string channel, string inner, bool isDir)
        {
            var sb = new StringBuilder("/webdav/");
            sb.Append(Uri.EscapeDataString(channel));
            foreach (var seg in inner.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                sb.Append('/');
                sb.Append(Uri.EscapeDataString(seg));
            }
            if (isDir) sb.Append('/');
            return sb.ToString();
        }

        private static string XmlEscape(string? s) =>
            System.Security.SecurityElement.Escape(s ?? string.Empty) ?? string.Empty;

        /// <summary>Parses a WebDAV Timeout header ("Second-3600", "Infinite", CSV) into a TimeSpan.</summary>
        private static TimeSpan? ParseTimeoutHeader(string? header)
        {
            if (string.IsNullOrWhiteSpace(header)) return null;
            foreach (var raw in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (raw.StartsWith("Second-", StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(raw.AsSpan("Second-".Length), out var secs) && secs > 0)
                    return TimeSpan.FromSeconds(secs);
                // "Infinite" (or anything else) falls through to the clamped default.
            }
            return null;
        }

        /// <summary>Extracts the first opaquelocktoken from an If / Lock-Token header value.</summary>
        private static string ExtractToken(string? header)
        {
            if (string.IsNullOrWhiteSpace(header)) return string.Empty;
            var m = System.Text.RegularExpressions.Regex.Match(header, @"opaquelocktoken:[^>)\s]+");
            return m.Success ? m.Value : string.Empty;
        }

        /// <summary>Best-effort extraction of the &lt;owner&gt; element from a LOCK request body.</summary>
        private async Task<string?> ReadOwnerFromBodyAsync()
        {
            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(body)) return null;
                var m = System.Text.RegularExpressions.Regex.Match(
                    body, @"<(?:\w+:)?owner>(.*?)</(?:\w+:)?owner>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private string BuildLockDiscoveryXml(string channel, string inner, string token, TimeSpan timeout)
        {
            var href = BuildHref(channel, inner, isDir: inner == "/");
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.Append($"<D:prop xmlns:D=\"{Dav}\"><D:lockdiscovery><D:activelock>");
            sb.Append("<D:locktype><D:write/></D:locktype>");
            sb.Append("<D:lockscope><D:exclusive/></D:lockscope>");
            sb.Append("<D:depth>infinity</D:depth>");
            sb.Append($"<D:timeout>Second-{(int)timeout.TotalSeconds}</D:timeout>");
            sb.Append($"<D:locktoken><D:href>{XmlEscape(token)}</D:href></D:locktoken>");
            sb.Append($"<D:lockroot><D:href>{XmlEscape(href)}</D:href></D:lockroot>");
            sb.Append("</D:activelock></D:lockdiscovery></D:prop>");
            return sb.ToString();
        }

        private static async Task<StateTask> WaitForCompletionAsync(InfoDownloadTaksModel task, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.Add(UploadTimeout);
            while (DateTime.UtcNow < deadline)
            {
                switch (task.state)
                {
                    case StateTask.Completed:
                    case StateTask.Error:
                    case StateTask.Canceled:
                        return task.state;
                }
                await Task.Delay(250, ct);
            }
            return StateTask.Working; // timeout sentinel
        }

        private bool IsAuthorized()
        {
            // Prefer credentials managed from the Config UI (persisted in Mongo);
            // fall back to config.json as a whole pair for backward compatibility.
            string? user;
            string pass;
            if (!string.IsNullOrEmpty(GeneralConfigStatic.config?.WebDavUser))
            {
                user = GeneralConfigStatic.config.WebDavUser;
                pass = GeneralConfigStatic.config.WebDavPassword ?? "";
            }
            else
            {
                user = GeneralConfigStatic.tlconfig?.webdav_user;
                pass = GeneralConfigStatic.tlconfig?.webdav_password ?? "";
            }

            // No credentials configured => open (dev mode), mirroring ApiKeyMiddleware.
            if (string.IsNullOrEmpty(user))
                return true;

            var header = Request.Headers["Authorization"].ToString();
            if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Substring("Basic ".Length).Trim()));
                var sep = decoded.IndexOf(':');
                if (sep < 0) return false;
                return decoded.Substring(0, sep) == user && decoded.Substring(sep + 1) == pass;
            }
            catch
            {
                return false;
            }
        }

        private IActionResult Challenge401()
        {
            Response.Headers["WWW-Authenticate"] = "Basic realm=\"TFM WebDAV\"";
            return Unauthorized();
        }

        private static void TryCleanup(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }
}
