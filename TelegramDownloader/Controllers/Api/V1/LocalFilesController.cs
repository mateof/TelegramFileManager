using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;

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
        public IActionResult Delete([FromBody] LocalDeleteRequest request)
        {
            if (request == null || request.Paths.Count == 0)
                return BadRequestResult("At least one path is required");

            var deleted = 0;
            var skipped = new List<string>();

            foreach (var path in request.Paths)
            {
                if (!TryResolve(path, out var absolute, out _, out _))
                {
                    skipped.Add(path);
                    continue;
                }

                try
                {
                    if (System.IO.File.Exists(absolute)) { System.IO.File.Delete(absolute); deleted++; }
                    else if (Directory.Exists(absolute)) { Directory.Delete(absolute, true); deleted++; }
                    else skipped.Add(path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete local path {Path}", path);
                    skipped.Add(path);
                }
            }

            return OkResult(new TransferAcceptedDto { Accepted = deleted, Skipped = skipped }, $"{deleted} entries deleted");
        }

        /// <summary>Downloads a local file.</summary>
        /// <remarks>
        /// Supports HTTP range requests, so it can be used directly as a media
        /// source by a player.
        /// </remarks>
        [HttpGet("download")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult Download([FromQuery] string path)
        {
            if (!TryResolve(path, out var absolute, out _, out var error))
                return BadRequestResult(error!);

            if (!System.IO.File.Exists(absolute))
                return NotFoundResult("File not found", ApiErrorCodes.FileNotFound);

            var stream = new FileStream(absolute, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, FileService.getMimeType(Path.GetExtension(absolute)) ?? "application/octet-stream",
                Path.GetFileName(absolute), enableRangeProcessing: true);
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
        {
            absolute = string.Empty;
            relative = string.Empty;
            error = null;

            var candidate = (path ?? string.Empty).Replace("\\", "/").Trim().TrimStart('/');
            if (Path.IsPathRooted(candidate))
            {
                error = "Only paths relative to the local root are accepted";
                return false;
            }

            var root = Path.GetFullPath(FileService.LOCALDIR);
            var full = Path.GetFullPath(Path.Combine(root, candidate));

            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                error = "The path escapes the local root";
                return false;
            }

            absolute = full;
            relative = candidate.Trim('/');
            return true;
        }

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
