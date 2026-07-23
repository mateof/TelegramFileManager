using Microsoft.AspNetCore.Mvc;
using Syncfusion.Blazor.FileManager;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Browsing and managing the files a channel stores in Telegram, through the
    /// local index. This mirrors the "Remote" tab of the web file manager.
    ///
    /// Folders are addressed either by <c>path</c> (<c>/music/rock/</c>) or by
    /// <c>folderId</c>. Both are accepted everywhere; <c>folderId</c> wins when
    /// both are present.
    /// </summary>
    [Route("api/v1/channels/{channelId}/files")]
    [Tags("Files")]
    [RequireTelegramSession]
    public class FilesController : ApiV1ControllerBase
    {
        private readonly IDbService _db;
        private readonly IFileService _files;
        private readonly ChannelFolderResolver _resolver;
        private readonly ILogger<FilesController> _logger;

        public FilesController(
            IDbService db,
            IFileService files,
            ChannelFolderResolver resolver,
            ILogger<FilesController> logger)
        {
            _db = db;
            _files = files;
            _resolver = resolver;
            _logger = logger;
        }

        /// <summary>Lists the contents of a folder.</summary>
        /// <remarks>
        /// Folders are always returned before files. Supported <c>sortBy</c>
        /// values are <c>name</c> (default), <c>date</c>, <c>size</c> and
        /// <c>type</c>. <c>filter</c> narrows the result to one category:
        /// <c>audio</c>, <c>video</c>, <c>photo</c>, <c>document</c>,
        /// <c>archive</c>.
        /// </remarks>
        /// <param name="channelId">Channel id (also the name of its index database).</param>
        /// <param name="query">Navigation, filtering, sorting and paging.</param>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResult<ApiFolderContentsDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Browse(string channelId, [FromQuery] BrowseQuery query)
        {
            try
            {
                var folder = await _resolver.ResolveFolder(channelId, query.FolderId, query.Path);
                if (folder == null)
                    return NotFoundResult("Folder not found");
                if (folder.IsFile)
                    return BadRequestResult("The requested id refers to a file, not a folder");

                var children = await _resolver.ListChildren(channelId, folder);
                var dto = BuildContents(channelId, folder, children, query, out var pageInfo);
                return OkPaged(dto, pageInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error browsing channel {ChannelId}", channelId);
                return ErrorResult("Could not browse the channel", ex);
            }
        }

        /// <summary>Searches files by name across a subtree.</summary>
        /// <param name="channelId">Channel id.</param>
        /// <param name="q">Text to look for (case-insensitive, substring match).</param>
        /// <param name="query">Scope (<c>path</c>), filtering, sorting and paging.</param>
        [HttpGet("search")]
        [ProducesResponseType(typeof(ApiResult<List<ApiFileDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Search(string channelId, [FromQuery] string q, [FromQuery] BrowseQuery query)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequestResult("A search term is required");

            try
            {
                var scope = ChannelFolderResolver.NormalizeFolderPath(query.Path);
                var searchRoot = scope == "/" ? "" : scope.TrimEnd('/');
                var matches = await _db.Search(channelId, searchRoot, q);

                var items = (matches ?? new List<BsonFileManagerModel>())
                    .Select(m => ApiFileDto.FromBson(m, channelId, BaseUrl))
                    .ToList();

                items = ApplyFilter(items, query);
                items = ApplySort(items, query);

                var (pageItems, info) = Paginate(items, query);
                return OkPaged(pageItems, info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching channel {ChannelId} for '{Term}'", channelId, q);
                return ErrorResult("Could not run the search", ex);
            }
        }

        /// <summary>Details of a single file or folder.</summary>
        [HttpGet("{fileId}")]
        [ProducesResponseType(typeof(ApiResult<ApiFileDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(string channelId, string fileId)
        {
            try
            {
                var entry = await _db.getFileById(channelId, fileId);
                if (entry == null)
                    return NotFoundResult("File not found", ApiErrorCodes.FileNotFound);
                return OkResult(ApiFileDto.FromBson(entry, channelId, BaseUrl));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading file {FileId} of channel {ChannelId}", fileId, channelId);
                return ErrorResult("Could not read the file", ex);
            }
        }

        /// <summary>Aggregate size and file-type breakdown of a folder subtree.</summary>
        [HttpGet("stats")]
        [ProducesResponseType(typeof(ApiResult<ApiFolderStatsDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Stats(string channelId, [FromQuery] string? path, [FromQuery] string? folderId)
        {
            try
            {
                var folder = await _resolver.ResolveFolder(channelId, folderId, path);
                if (folder == null)
                    return NotFoundResult("Folder not found");

                var childPath = ChannelFolderResolver.ChildFolderPath(folder);
                var all = await _db.getAllChildFilesInDirectory(channelId, childPath);
                var files = (all ?? new List<BsonFileManagerModel>()).Where(f => f.IsFile).ToList();

                var stats = new ApiFolderStatsDto
                {
                    FileCount = files.Count,
                    FolderCount = (all?.Count ?? 0) - files.Count,
                    AudioCount = files.Count(f => ApiFileDto.CategoryOf(f.Type) == "Audio"),
                    VideoCount = files.Count(f => ApiFileDto.CategoryOf(f.Type) == "Video"),
                    PhotoCount = files.Count(f => ApiFileDto.CategoryOf(f.Type) == "Photo"),
                    DocumentCount = files.Count(f => ApiFileDto.CategoryOf(f.Type) == "Document"),
                    TotalSize = files.Sum(f => f.Size)
                };
                stats.TotalSizeText = HelperService.SizeSuffix(stats.TotalSize);

                return OkResult(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error computing stats for channel {ChannelId}", channelId);
                return ErrorResult("Could not compute the folder statistics", ex);
            }
        }

        /// <summary>Creates a folder.</summary>
        [HttpPost("folders")]
        [ProducesResponseType(typeof(ApiResult<ApiFileDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CreateFolder(string channelId, [FromBody] CreateFolderRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
                return BadRequestResult("A folder name is required");
            if (request.Name.Contains('/') || request.Name.Contains('\\'))
                return BadRequestResult("A folder name cannot contain path separators");

            try
            {
                var parent = await _resolver.ResolveFolder(channelId, null, request.Path);
                if (parent == null)
                    return NotFoundResult("Parent folder not found");

                var created = await _files.createFolder(
                    channelId,
                    ChannelFolderResolver.CreateChildPath(parent),
                    request.Name.Trim(),
                    ChannelFolderResolver.ToContent(parent));

                var first = created?.FirstOrDefault();
                if (first == null)
                    return ErrorResult("The folder was not created");

                var entry = await _db.getFileById(channelId, first.Id);
                var dto = entry != null
                    ? ApiFileDto.FromBson(entry, channelId, BaseUrl)
                    : new ApiFileDto { Id = first.Id, Name = request.Name, IsFile = false, Type = "folder", Category = "Folder" };

                return StatusCode(StatusCodes.Status201Created, ApiResult<ApiFileDto>.Ok(dto, "Folder created"));
            }
            catch (MongoDB.Driver.MongoWriteException ex) when (ex.WriteError?.Category == MongoDB.Driver.ServerErrorCategory.DuplicateKey)
            {
                return ConflictResult("A folder with that name already exists here");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating folder in channel {ChannelId}", channelId);
                return ErrorResult("Could not create the folder", ex);
            }
        }

        /// <summary>Renames a file or folder.</summary>
        [HttpPut("{fileId}/name")]
        [ProducesResponseType(typeof(ApiResult<ApiFileDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Rename(string channelId, string fileId, [FromBody] RenameRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.NewName))
                return BadRequestResult("A new name is required");
            if (request.NewName.Contains('/') || request.NewName.Contains('\\'))
                return BadRequestResult("A name cannot contain path separators");

            try
            {
                var entry = await _db.getFileById(channelId, fileId);
                if (entry == null)
                    return NotFoundResult("File not found", ApiErrorCodes.FileNotFound);

                await _files.RenameFileOrFolder(channelId, ChannelFolderResolver.ToContent(entry), request.NewName.Trim());

                var updated = await _db.getFileById(channelId, fileId);
                return OkResult(
                    updated != null ? ApiFileDto.FromBson(updated, channelId, BaseUrl) : ApiFileDto.FromBson(entry, channelId, BaseUrl),
                    "Renamed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming {FileId} in channel {ChannelId}", fileId, channelId);
                return ErrorResult("Could not rename the entry", ex);
            }
        }

        /// <summary>Deletes files and folders.</summary>
        /// <remarks>
        /// Deleting also removes the underlying Telegram messages when no other
        /// indexed entry references them, so this frees the channel storage.
        /// Folders are deleted recursively. The operation is not reversible.
        /// </remarks>
        [HttpPost("delete")]
        [ProducesResponseType(typeof(ApiResult<TransferAcceptedDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Delete(string channelId, [FromBody] FileIdsRequest request)
        {
            if (request == null || request.Ids.Count == 0)
                return BadRequestResult("At least one id is required");

            var deleted = 0;
            var skipped = new List<string>();

            foreach (var id in request.Ids)
            {
                try
                {
                    var entry = await _db.getFileById(channelId, id);
                    if (entry == null)
                    {
                        skipped.Add(id);
                        continue;
                    }
                    await _files.oneItemDeleteAsync(channelId, ChannelFolderResolver.ToContent(entry));
                    deleted++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete {FileId} in channel {ChannelId}", id, channelId);
                    skipped.Add(id);
                }
            }

            return OkResult(new TransferAcceptedDto { Accepted = deleted, Skipped = skipped },
                $"{deleted} entries deleted");
        }

        /// <summary>Copies files and folders to another folder of the same channel.</summary>
        /// <remarks>
        /// Copies are index-level: the Telegram messages are shared, so a copy
        /// consumes no extra channel storage.
        /// </remarks>
        [HttpPost("copy")]
        [ProducesResponseType(typeof(ApiResult<TransferAcceptedDto>), StatusCodes.Status200OK)]
        public Task<IActionResult> Copy(string channelId, [FromBody] CopyMoveRequest request) =>
            CopyOrMove(channelId, request, isCopy: true);

        /// <summary>Moves files and folders to another folder of the same channel.</summary>
        [HttpPost("move")]
        [ProducesResponseType(typeof(ApiResult<TransferAcceptedDto>), StatusCodes.Status200OK)]
        public Task<IActionResult> Move(string channelId, [FromBody] CopyMoveRequest request) =>
            CopyOrMove(channelId, request, isCopy: false);

        private async Task<IActionResult> CopyOrMove(string channelId, CopyMoveRequest request, bool isCopy)
        {
            if (request == null || request.Ids.Count == 0)
                return BadRequestResult("At least one id is required");

            try
            {
                var target = await _resolver.ResolveFolder(channelId, request.TargetFolderId, request.TargetPath);
                if (target == null || target.IsFile)
                    return NotFoundResult("Target folder not found");

                var entries = new List<BsonFileManagerModel>();
                var skipped = new List<string>();
                foreach (var id in request.Ids)
                {
                    var entry = await _db.getFileById(channelId, id);
                    if (entry == null) skipped.Add(id);
                    else entries.Add(entry);
                }

                if (entries.Count > 0)
                {
                    var contents = entries.Select(ChannelFolderResolver.ToContent).ToArray();
                    await _files.CopyOrMoveItems(
                        channelId,
                        contents,
                        ChannelFolderResolver.ChildFolderPath(target),
                        ChannelFolderResolver.ToContent(target),
                        isCopy);
                }

                return OkResult(new TransferAcceptedDto { Accepted = entries.Count, Skipped = skipped },
                    isCopy ? "Entries copied" : "Entries moved");
            }
            catch (MongoDB.Driver.MongoWriteException ex) when (ex.WriteError?.Category == MongoDB.Driver.ServerErrorCategory.DuplicateKey)
            {
                return ConflictResult("An entry with the same name already exists in the target folder");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error on {Operation} in channel {ChannelId}", isCopy ? "copy" : "move", channelId);
                return ErrorResult(isCopy ? "Could not copy the entries" : "Could not move the entries", ex);
            }
        }

        /// <summary>Uploads a file directly into a channel folder.</summary>
        /// <remarks>
        /// The request must be <c>multipart/form-data</c> with a <c>file</c>
        /// part. The upload is streamed to Telegram and its progress is
        /// published on the <c>transfers</c> hub like any other upload.
        ///
        /// To push files that already live on the server, use
        /// <c>POST /api/v1/transfers/uploads</c> instead: it avoids sending the
        /// bytes twice.
        /// </remarks>
        /// <param name="channelId">Destination channel.</param>
        /// <param name="file">File part of the multipart body.</param>
        /// <param name="path">Destination folder inside the channel. Defaults to the root.</param>
        [HttpPost("upload")]
        [RequestSizeLimit(long.MaxValue)]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status202Accepted)]
        public async Task<IActionResult> Upload(string channelId, IFormFile file, [FromForm] string? path)
        {
            if (file == null || file.Length == 0)
                return BadRequestResult("A non-empty file part is required");

            try
            {
                var folder = ChannelFolderResolver.NormalizeFolderPath(path);

                // Stage the bytes under the local root, then reuse the regular
                // server-to-Telegram pipeline so the upload shows up in the task
                // list, is persisted and streams its progress over SignalR.
                var stagingRelative = $"{ApiUploadStaging.FolderName}/{Guid.NewGuid():N}";
                var stagingAbsolute = Path.Combine(FileService.LOCALDIR, stagingRelative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(stagingAbsolute);

                var safeName = Path.GetFileName(file.FileName);
                await using (var fs = System.IO.File.Create(Path.Combine(stagingAbsolute, safeName)))
                    await file.CopyToAsync(fs);

                var content = new FileManagerDirectoryContent
                {
                    Name = safeName,
                    IsFile = true,
                    Size = file.Length,
                    FilterPath = "/" + stagingRelative + "/",
                    Type = Path.GetExtension(safeName)
                };

                await _files.AddUploadFileFromServer(channelId, folder, new List<FileManagerDirectoryContent> { content });
                return Accepted(ApiResult.Done($"Upload of {safeName} started"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading {FileName} to channel {ChannelId}", file?.FileName, channelId);
                return ErrorResult("Could not upload the file", ex);
            }
        }

        /// <summary>Exports the whole channel index as a JSON document.</summary>
        /// <remarks>
        /// The export can be re-imported into another instance with
        /// <c>POST /api/v1/channels/{channelId}/files/import</c>, which is how
        /// the app moves a library between servers.
        /// </remarks>
        [HttpGet("export")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Export(string channelId)
        {
            try
            {
                var ms = await _files.exportAllData(channelId);
                ms.Position = 0;
                return File(ms, "application/json", $"{channelId}.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting channel {ChannelId}", channelId);
                return ErrorResult("Could not export the channel index", ex);
            }
        }

        /// <summary>Imports a previously exported channel index.</summary>
        /// <remarks>
        /// Send the export file as <c>multipart/form-data</c> in a <c>file</c>
        /// part. Import runs in the background and reports through the
        /// notification pipeline.
        /// </remarks>
        [HttpPost("import")]
        [RequestSizeLimit(long.MaxValue)]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status202Accepted)]
        public async Task<IActionResult> Import(string channelId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequestResult("A non-empty file part is required");

            try
            {
                var tempPath = Path.Combine(FileService.TEMPDIR, $"import-{Guid.NewGuid():N}.json");
                Directory.CreateDirectory(FileService.TEMPDIR);
                await using (var fs = System.IO.File.Create(tempPath))
                    await file.CopyToAsync(fs);

                var progress = new GenericNotificationProgressModel();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _files.importData(channelId, tempPath, progress);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background import into channel {ChannelId} failed", channelId);
                    }
                    finally
                    {
                        try { System.IO.File.Delete(tempPath); } catch { }
                    }
                });

                return Accepted(ApiResult.Done("Import started"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing into channel {ChannelId}", channelId);
                return ErrorResult("Could not import the channel index", ex);
            }
        }

        private ApiFolderContentsDto BuildContents(
            string channelId,
            BsonFileManagerModel folder,
            List<BsonFileManagerModel> children,
            BrowseQuery query,
            out PageInfo pageInfo)
        {
            var all = children.Select(m => ApiFileDto.FromBson(m, channelId, BaseUrl)).ToList();

            var stats = new ApiFolderStatsDto
            {
                FolderCount = all.Count(i => !i.IsFile),
                FileCount = all.Count(i => i.IsFile),
                AudioCount = all.Count(i => i.Category == "Audio"),
                VideoCount = all.Count(i => i.Category == "Video"),
                PhotoCount = all.Count(i => i.Category == "Photo"),
                DocumentCount = all.Count(i => i.Category == "Document"),
                TotalSize = all.Where(i => i.IsFile).Sum(i => i.Size)
            };
            stats.TotalSizeText = HelperService.SizeSuffix(stats.TotalSize);

            var items = query.FilesOnly ? all.Where(i => i.IsFile).ToList() : all;
            items = ApplyFilter(items, query);

            if (!string.IsNullOrWhiteSpace(query.Search))
                items = items.Where(i => i.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase)).ToList();

            items = ApplySort(items, query);

            var (pageItems, info) = Paginate(items, query);
            pageInfo = info;

            var crumbs = ChannelFolderResolver.Breadcrumbs(folder);
            var currentPath = ChannelFolderResolver.ChildFolderPath(folder);

            return new ApiFolderContentsDto
            {
                ChannelId = channelId,
                CurrentPath = currentPath,
                CurrentFolderId = folder.Id,
                ParentFolderId = string.IsNullOrEmpty(folder.ParentId) ? null : folder.ParentId,
                ParentPath = currentPath == "/" ? null : (crumbs.Count > 1 ? crumbs[^2].Path : "/"),
                FolderName = folder.Name,
                Items = pageItems,
                Stats = stats,
                Breadcrumbs = crumbs.Select(c => new ApiBreadcrumbDto { Name = c.Name, Path = c.Path }).ToList()
            };
        }

        private static List<ApiFileDto> ApplyFilter(List<ApiFileDto> items, BrowseQuery query)
        {
            if (string.IsNullOrWhiteSpace(query.Filter) || query.Filter.Equals("all", StringComparison.OrdinalIgnoreCase))
                return items;

            var wanted = query.Filter.Trim().ToLowerInvariant() switch
            {
                "audio" => "Audio",
                "video" => "Video",
                "photo" or "photos" or "image" or "images" => "Photo",
                "document" or "documents" or "doc" => "Document",
                "archive" or "archives" => "Archive",
                _ => query.Filter
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
