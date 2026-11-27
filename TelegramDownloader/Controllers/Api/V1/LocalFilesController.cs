using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;
using TelegramDownloader.Services.Audio;
using TelegramDownloader.Services.Deletes;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// The server's local storage: the folder downloads land in and uploads are
    /// taken from. This mirrors the "Local" tab of the web file manager.
    ///
    /// Every path is relative to the local root and is validated against
    /// directory traversal; absolute paths and <c>..</c> segments that escape
    /// the root are rejected with <c>400 invalid_request</c>.
    /// </summary>
    [Route("api/v1/local")]
    [Tags("Local files")]
    public class LocalFilesController : ApiV1ControllerBase
    {
        private readonly ILogger<LocalFilesController> _logger;

        public LocalFilesController(ILogger<LocalFilesController> logger)
        {
            _logger = logger;
        }

        /// <summary>Lists a local directory.</summary>
        /// <remarks>
        /// Use an empty <c>path</c> for the root. Folders come first; sorting
        /// and filtering behave exactly like the channel browse endpoint.
        /// </remarks>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResult<ApiFolderContentsDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult Browse([FromQuery] BrowseQuery query)
        {
            if (!TryResolve(query.Path, out var absolute, out var relative, out var error))
                return BadRequestResult(error!);

            if (!Directory.Exists(absolute))
                return NotFoundResult("Directory not found");

            try
            {
                var dir = new DirectoryInfo(absolute);
                var items = new List<ApiFileDto>();

                foreach (var sub in dir.GetDirectories())
                    items.Add(ApiFileDto.FromLocalDirectory(sub, Join(relative, sub.Name)));

                foreach (var file in dir.GetFiles())
                    items.Add(ApiFileDto.FromLocalFile(file, Join(relative, file.Name), BaseUrl));

                var stats = new ApiFolderStatsDto
                {
                    FolderCount = items.Count(i => !i.IsFile),
                    FileCount = items.Count(i => i.IsFile),
                    AudioCount = items.Count(i => i.Category == "Audio"),
                    VideoCount = items.Count(i => i.Category == "Video"),
                    PhotoCount = items.Count(i => i.Category == "Photo"),
                    DocumentCount = items.Count(i => i.Category == "Document"),
                    TotalSize = items.Where(i => i.IsFile).Sum(i => i.Size)
                };
                stats.TotalSizeText = HelperService.SizeSuffix(stats.TotalSize);

                var filtered = query.FilesOnly ? items.Where(i => i.IsFile).ToList() : items;
                filtered = ApplyFilter(filtered, query.Filter);

                if (!string.IsNullOrWhiteSpace(query.Search))
                    filtered = filtered.Where(i => i.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase)).ToList();

                filtered = ApplySort(filtered, query);

                var (pageItems, page) = Paginate(filtered, query);

                var parent = string.IsNullOrEmpty(relative)
                    ? null
                    : (Path.GetDirectoryName(relative)?.Replace("\\", "/") ?? string.Empty);

                var dto = new ApiFolderContentsDto
                {
                    CurrentPath = "/" + relative,
                    CurrentFolderId = relative,
                    ParentPath = parent,
                    FolderName = string.IsNullOrEmpty(relative) ? "Local" : dir.Name,
                    Items = pageItems,
                    Stats = stats,
                    Breadcrumbs = BuildBreadcrumbs(relative)
                };

                return OkPaged(dto, page);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing local path {Path}", query.Path);
                return ErrorResult("Could not list the directory", ex);
            }
        }

        /// <summary>Metadata of one local file or directory.</summary>
        [HttpGet("info")]
        [ProducesResponseType(typeof(ApiResult<ApiFileDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult Info([FromQuery] string path)
        {
            if (!TryResolve(path, out var absolute, out var relative, out var error))
                return BadRequestResult(error!);

            if (System.IO.File.Exists(absolute))
                return OkResult(ApiFileDto.FromLocalFile(new FileInfo(absolute), relative, BaseUrl));

            if (Directory.Exists(absolute))
                return OkResult(ApiFileDto.FromLocalDirectory(new DirectoryInfo(absolute), relative));

            return NotFoundResult("Path not found");
        }

        /// <summary>Recursive size and file-type breakdown of a local directory.</summary>
        [HttpGet("size")]
        [ProducesResponseType(typeof(ApiResult<Models.DirectorySizeModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Size([FromQuery] string? path)
        {
            if (!TryResolve(path, out var absolute, out _, out var error))
                return BadRequestResult(error!);

            if (!Directory.Exists(absolute))
                return NotFoundResult("Directory not found");

            try
            {
                return OkResult(await HelperService.GetDirecctorySizeAsync(absolute));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error measuring local path {Path}", path);
                return ErrorResult("Could not measure the directory", ex);
            }
        }

        /// <summary>Creates a local directory.</summary>
        [HttpPost("folders")]
        [ProducesResponseType(typeof(ApiResult<ApiFileDto>), StatusCodes.Status201Created)]
        public IActionResult CreateFolder([FromBody] LocalCreateFolderRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
                return BadRequestResult("A folder name is required");
            if (request.Name.Contains('/') || request.Name.Contains('\\'))
                return BadRequestResult("A folder name cannot contain path separators");

            if (!TryResolve(request.Path, out var parentAbsolute, out var parentRelative, out var error))
                return BadRequestResult(error!);

            try
            {
                var target = Path.Combine(parentAbsolute, request.Name.Trim());
                if (Directory.Exists(target))
                    return ConflictResult("A folder with that name already exists");

                var info = Directory.CreateDirectory(target);
                var dto = ApiFileDto.FromLocalDirectory(info, Join(parentRelative, info.Name));
                return StatusCode(StatusCodes.Status201Created, ApiResult<ApiFileDto>.Ok(dto, "Folder created"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating local folder under {Path}", request.Path);
                return ErrorResult("Could not create the folder", ex);
            }
        }

        /// <summary>Renames a local file or directory.</summary>
        [HttpPost("rename")]
        [ProducesResponseType(typeof(ApiResult<ApiFileDto>), StatusCodes.Status200OK)]
        public IActionResult Rename([FromBody] LocalRenameRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.NewName))
                return BadRequestResult("A new name is required");
            if (request.NewName.Contains('/') || request.NewName.Contains('\\'))
                return BadRequestResult("A name cannot contain path separators");

            if (!TryResolve(request.Path, out var absolute, out var relative, out var error))
                return BadRequestResult(error!);

            try
            {
                var parentAbsolute = Path.GetDirectoryName(absolute)!;
                var parentRelative = Path.GetDirectoryName(relative)?.Replace("\\", "/") ?? string.Empty;
                var target = Path.Combine(parentAbsolute, request.NewName.Trim());

                if (System.IO.File.Exists(absolute))
                {
                    if (System.IO.File.Exists(target)) return ConflictResult("A file with that name already exists");
                    System.IO.File.Move(absolute, target);
                    return OkResult(ApiFileDto.FromLocalFile(new FileInfo(target), Join(parentRelative, request.NewName.Trim()), BaseUrl), "Renamed");
                }

                if (Directory.Exists(absolute))
                {
                    if (Directory.Exists(target)) return ConflictResult("A folder with that name already exists");
                    Directory.Move(absolute, target);
                    return OkResult(ApiFileDto.FromLocalDirectory(new DirectoryInfo(target), Join(parentRelative, request.NewName.Trim())), "Renamed");
                }

                return NotFoundResult("Path not found");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming local path {Path}", request.Path);
                return ErrorResult("Could not rename the entry", ex);
            }
        }

        /// <summary>Deletes local files and directories.</summary>
        /// <remarks>Directories are deleted recursively and the data is not recoverable.</remarks>
        [HttpPost("delete")]
        [ProducesResponseType(typeof(ApiResult<TransferAcceptedDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Delete([FromBody] LocalDeleteRequest request)
        {
            if (request == null || request.Paths.Count == 0)
                return BadRequestResult("At least one path is required");

            // Same implementation as the background operations, without a listener.
            var progress = new DeleteOperation(DeleteKinds.Local, null, null, request.Paths.Count);
            var result = await new LocalDeleteRunner(FileService.LOCALDIR, _logger).RunAsync(request.Paths, progress);
            var deleted = result.Deleted.Count;
            return OkResult(new TransferAcceptedDto { Accepted = deleted, Skipped = result.Skipped }, $"{deleted} entries deleted");
        }

        /// <summary>Starts deleting local files and directories in the background, with live progress.</summary>
        /// <remarks>
        /// Counts the files first, then deletes them one by one and the folders
        /// bottom-up. Answers at once with an <c>operationId</c>: follow it with
        /// <c>GET /api/v1/delete-operations/{operationId}</c> or the
        /// <c>DeleteProgress</c> message of <c>/hubs/events</c>, cancel it with
        /// <c>POST /api/v1/delete-operations/{operationId}/cancel</c>. Paths that
        /// escape the local root and the root itself are rejected with 400;
        /// paths reached through a link outside the root are reported as failed
        /// items, and links inside the folders are removed, never followed.
        /// </remarks>
        /// <param name="request">Paths relative to the local root.</param>
        /// <param name="operations">Delete operations service.</param>
        [HttpPost("delete-operations")]
        [ProducesResponseType(typeof(ApiResult<DeleteOperationStartedDto>), StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public IActionResult StartDeleteOperation([FromBody] LocalDeleteRequest request, [FromServices] DeleteOperationService operations)
        {
            if (request == null || request.Paths == null || request.Paths.Count == 0)
                return BadRequestResult("At least one path is required");
            foreach (var path in request.Paths)
            {
                if (!TryResolve(path, out _, out var relative, out var error))
                    return BadRequestResult(error ?? "Invalid path");
                if (string.IsNullOrEmpty(relative))
                    return BadRequestResult("The local root cannot be deleted");
            }

            var operation = operations.StartLocal(FileService.LOCALDIR, request.Paths);
            return Accepted(ApiResult<DeleteOperationStartedDto>.Ok(new DeleteOperationStartedDto { OperationId = operation.Id }, "Delete started"));
        }

        /// <summary>Downloads a local file.</summary>
        /// <remarks>
        /// Supports HTTP range requests, so it can be used directly as a media
        /// source by a player. <c>disposition=inline</c> serves it for viewing
        /// in the browser instead of saving it.
        /// </remarks>
        /// <param name="path">File path relative to the local root.</param>
        /// <param name="disposition"><c>attachment</c> (default) or <c>inline</c>.</param>
        [HttpGet("download")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult Download([FromQuery] string path, [FromQuery] string? disposition = null)
        {
            if (!TryResolve(path, out var absolute, out _, out var error))
                return BadRequestResult(error!);

            if (!System.IO.File.Exists(absolute))
                return NotFoundResult("File not found", ApiErrorCodes.FileNotFound);

            var mime = FileService.getMimeType(Path.GetFileName(absolute)) ?? "application/octet-stream";
            var stream = new FileStream(absolute, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (string.Equals(disposition, "inline", StringComparison.OrdinalIgnoreCase))
            {
                Response.Headers["Content-Disposition"] = $"inline; filename*=UTF-8''{Uri.EscapeDataString(Path.GetFileName(absolute))}";
                return File(stream, mime, enableRangeProcessing: true);
            }
            return File(stream, mime, Path.GetFileName(absolute), enableRangeProcessing: true);
        }

        /// <summary>A local audio file transcoded to MP3 or AAC (offline downloads of the audio app).</summary>
        /// <remarks>
        /// Same behaviour as <c>/api/mobile/stream/local/transcoded</c>. FFmpeg
        /// runs on the first request (which can take a while); the result is
        /// kept on disk keyed by path, size and modification time, so editing
        /// the file transcodes it again. Answers <c>200</c>, <c>206</c> (one
        /// <c>Range</c>) or <c>416</c>; <c>400</c> for a missing or escaping
        /// <c>path</c> or an unknown <c>format</c>; <c>404</c> when the file does
        /// not exist; <c>501</c> when FFmpeg is missing.
        /// </remarks>
        /// <param name="transcoder">Injected.</param>
        /// <param name="path">File path relative to the local root.</param>
        /// <param name="format"><c>mp3</c> (default) or <c>aac</c> (served as <c>.m4a</c>).</param>
        /// <param name="bitrate">kbps, 64-320 (out of range values are clamped). Default 192.</param>
        [HttpGet("transcoded")]
        [Produces("audio/mpeg", "audio/mp4", "application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status206PartialContent)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status501NotImplemented)]
        public async Task<IActionResult> Transcoded(
            [FromServices] AudioTranscodeService transcoder,
            [FromQuery] string? path,
            [FromQuery] string? format = null,
            [FromQuery] int? bitrate = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequestResult("A path is required");
            if (!AudioTranscodeOptions.TryNormalize(format, bitrate, out var fmt, out var kbps, out var formatError))
                return BadRequestResult(formatError!);
            if (!transcoder.IsAvailable)
                return AudioController.FfmpegUnavailable();
            if (!TryResolve(path, out var absolute, out _, out var error))
                return BadRequestResult(error!);
            if (!System.IO.File.Exists(absolute))
                return NotFoundResult("File not found", ApiErrorCodes.FileNotFound);

            try
            {
                var result = await transcoder.TranscodeLocalFile(absolute, fmt, kbps, HttpContext.RequestAborted);
                return PhysicalFile(result.Path, result.MimeType, result.DownloadName, enableRangeProcessing: true);
            }
            catch (OperationCanceledException)
            {
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error transcoding local audio at path {Path}", path);
                return ErrorResult("Error transcoding audio", ex);
            }
        }

        /// <summary>Copies local files and folders into another local folder.</summary>
        /// <remarks>
        /// Every path (sources and <c>targetPath</c>) must stay inside the local
        /// root. Folders are copied recursively; the target folder is created
        /// when missing. Entries whose name already exists in the target are
        /// skipped unless <c>overwrite</c> is true. Copying a folder into itself
        /// (or one of its sub-folders) is refused for that entry.
        /// </remarks>
        [HttpPost("copy")]
        [ProducesResponseType(typeof(ApiResult<TransferAcceptedDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public IActionResult Copy([FromBody] LocalCopyMoveRequest request) => CopyOrMove(request, move: false);

        /// <summary>Moves local files and folders into another local folder.</summary>
        /// <remarks>Same rules as <c>POST /api/v1/local/copy</c>.</remarks>
        [HttpPost("move")]
        [ProducesResponseType(typeof(ApiResult<TransferAcceptedDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public IActionResult Move([FromBody] LocalCopyMoveRequest request) => CopyOrMove(request, move: true);

        private IActionResult CopyOrMove(LocalCopyMoveRequest request, bool move)
        {
            if (request == null || request.Paths == null || request.Paths.Count == 0)
                return BadRequestResult("At least one path is required");
            if (!TryResolve(request.TargetPath, out var targetAbsolute, out _, out var error))
                return BadRequestResult(error!);
            if (System.IO.File.Exists(targetAbsolute))
                return BadRequestResult("targetPath is a file, not a folder");

            var done = 0;
            var skipped = new List<string>();
            try
            {
                Directory.CreateDirectory(targetAbsolute);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not create the local folder {Path}", request.TargetPath);
                return ErrorResult("Could not create the target folder", ex);
            }

            foreach (var path in request.Paths)
            {
                if (!TryResolve(path, out var source, out var sourceRelative, out _) || string.IsNullOrEmpty(sourceRelative))
                {
                    skipped.Add(path);
                    continue;
                }

                var destination = Path.Combine(targetAbsolute, Path.GetFileName(source));
                if (!LocalCopyMovePlanner.CanPlace(source, destination, Directory.Exists(source)))
                {
                    skipped.Add(path);
                    continue;
                }

                try
                {
                    var exists = System.IO.File.Exists(destination) || Directory.Exists(destination);
                    if (exists)
                    {
                        if (!request.Overwrite)
                        {
                            skipped.Add(path);
                            continue;
                        }
                        if (System.IO.File.Exists(destination)) System.IO.File.Delete(destination);
                        else Directory.Delete(destination, true);
                    }

                    if (System.IO.File.Exists(source))
                    {
                        if (move) System.IO.File.Move(source, destination);
                        else System.IO.File.Copy(source, destination);
                    }
                    else if (Directory.Exists(source))
                    {
                        if (move) MoveDirectory(source, destination);
                        else CopyDirectory(source, destination);
                    }
                    else
                    {
                        skipped.Add(path);
                        continue;
                    }
                    done++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not {Operation} local path {Path}", move ? "move" : "copy", path);
                    skipped.Add(path);
                }
            }

            return OkResult(new TransferAcceptedDto { Accepted = done, Skipped = skipped },
                move ? $"{done} entries moved" : $"{done} entries copied");
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source))
                System.IO.File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.EnumerateDirectories(source))
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }

        private static void MoveDirectory(string source, string destination)
        {
            try
            {
                Directory.Move(source, destination);
            }
            catch (IOException)
            {
                // Different volume (e.g. a bind mount): copy, then delete.
                CopyDirectory(source, destination);
                Directory.Delete(source, true);
            }
        }

        /// <summary>Uploads a file into the local storage.</summary>
        /// <remarks>
        /// Send <c>multipart/form-data</c> with a <c>file</c> part. To then push
        /// it to Telegram, call <c>POST /api/v1/transfers/uploads</c> with the
        /// returned path.
        /// </remarks>
        [HttpPost("upload")]
        [RequestSizeLimit(long.MaxValue)]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
        [ProducesResponseType(typeof(ApiResult<ApiFileDto>), StatusCodes.Status201Created)]
        public async Task<IActionResult> Upload(IFormFile file, [FromForm] string? path)
        {
            if (file == null || file.Length == 0)
                return BadRequestResult("A non-empty file part is required");

            if (!TryResolve(path, out var absolute, out var relative, out var error))
                return BadRequestResult(error!);

            try
            {
                Directory.CreateDirectory(absolute);
                var safeName = Path.GetFileName(file.FileName);
                var target = Path.Combine(absolute, safeName);

                await using (var fs = System.IO.File.Create(target))
                    await file.CopyToAsync(fs);

                var dto = ApiFileDto.FromLocalFile(new FileInfo(target), Join(relative, safeName), BaseUrl);
                return StatusCode(StatusCodes.Status201Created, ApiResult<ApiFileDto>.Ok(dto, "File stored"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing an upload under {Path}", path);
                return ErrorResult("Could not store the file", ex);
            }
        }

        /// <summary>Empties the streaming/temporary cache folder.</summary>
        /// <remarks>
        /// The cache holds files pulled from Telegram for playback. Clearing it
        /// frees disk space; the next playback re-downloads what it needs.
        /// </remarks>
        [HttpPost("cache/clear")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public IActionResult ClearCache([FromServices] IFileService files)
        {
            try
            {
                files.cleanTempFolder();
                return OkEmpty("Temporary cache cleared");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing the temporary cache");
                return ErrorResult("Could not clear the temporary cache", ex);
            }
        }

        private static List<ApiBreadcrumbDto> BuildBreadcrumbs(string relative)
        {
            var crumbs = new List<ApiBreadcrumbDto> { new() { Name = "Local", Path = "", FolderId = "" } };
            if (string.IsNullOrEmpty(relative)) return crumbs;

            var acc = string.Empty;
            foreach (var segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                acc = string.IsNullOrEmpty(acc) ? segment : acc + "/" + segment;
                crumbs.Add(new ApiBreadcrumbDto { Name = segment, Path = acc, FolderId = acc });
            }
            return crumbs;
        }

        private static string Join(string parent, string name) =>
            string.IsNullOrEmpty(parent) ? name : parent.TrimEnd('/') + "/" + name;

        /// <summary>
        /// Resolves a client path against the local root, refusing anything that
        /// escapes it.
        /// </summary>
        private static bool TryResolve(string? path, out string absolute, out string relative, out string? error)
            => LocalPathGuard.TryResolve(FileService.LOCALDIR, path, out absolute, out relative, out error);

        private static List<ApiFileDto> ApplyFilter(List<ApiFileDto> items, string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter) || filter.Equals("all", StringComparison.OrdinalIgnoreCase))
                return items;

            var wanted = filter.Trim().ToLowerInvariant() switch
            {
                "audio" => "Audio",
                "video" => "Video",
                "photo" or "photos" or "image" or "images" => "Photo",
                "document" or "documents" or "doc" => "Document",
                "archive" or "archives" => "Archive",
                _ => filter
            };

            return items.Where(i => !i.IsFile || i.Category.Equals(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static List<ApiFileDto> ApplySort(List<ApiFileDto> items, BrowseQuery query) =>
            (query.SortBy?.ToLowerInvariant(), query.SortDescending) switch
            {
                ("date", true) => items.OrderBy(i => i.IsFile).ThenByDescending(i => i.DateModified).ToList(),
                ("date", false) => items.OrderBy(i => i.IsFile).ThenBy(i => i.DateModified).ToList(),
                ("size", true) => items.OrderBy(i => i.IsFile).ThenByDescending(i => i.Size).ToList(),
                ("size", false) => items.OrderBy(i => i.IsFile).ThenBy(i => i.Size).ToList(),
                ("type", true) => items.OrderBy(i => i.IsFile).ThenByDescending(i => i.Type).ToList(),
                ("type", false) => items.OrderBy(i => i.IsFile).ThenBy(i => i.Type).ToList(),
                (_, true) => items.OrderBy(i => i.IsFile).ThenByDescending(i => i.Name).ToList(),
                _ => items.OrderBy(i => i.IsFile).ThenBy(i => i.Name).ToList()
            };
    }
}
