using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models.Mobile;

namespace TelegramDownloader.Controllers.Mobile
{
    /// <summary>
    /// Navegación de archivos en canales de Telegram y sistema local.
    /// Permite explorar la estructura de carpetas, buscar archivos y obtener información
    /// detallada de cada archivo incluyendo URLs de streaming y descarga.
    /// </summary>
    [Route("api/mobile/files")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Files")]
    public class MobileFileController : ControllerBase
    {
        private readonly IDbService _db;
        private readonly ILogger<MobileFileController> _logger;

        public MobileFileController(IDbService db, ILogger<MobileFileController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Navegar archivos de un canal de Telegram
        /// </summary>
        /// <remarks>
        /// Explora la estructura de archivos de un canal de Telegram específico.
        /// Los archivos se indexan previamente y se almacenan en MongoDB para acceso rápido.
        ///
        /// **Funcionalidades:**
        /// - Navegación por carpetas usando FolderId
        /// - Filtrado por tipo: audio, video, documents, photos, all
        /// - Búsqueda por nombre de archivo
        /// - Ordenamiento por nombre, fecha o tamaño
        /// - Paginación configurable
        ///
        /// Cada archivo incluye StreamUrl para audio/video y DownloadUrl para descarga directa.
        /// </remarks>
        /// <param name="channelId">ID del canal de Telegram (como string)</param>
        /// <param name="request">Parámetros de navegación: FolderId, Path, Filter, SearchText, SortBy, Page, PageSize</param>
        /// <returns>Contenido de la carpeta con archivos, estadísticas e información de paginación</returns>
        [HttpGet("telegram/{channelId}")]
        [ProducesResponseType(typeof(ApiResponse<FolderContentsDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> BrowseTelegramFiles(string channelId, [FromQuery] BrowseRequest request)
        {
            try
            {
                var files = await _db.getFilesByParentId(channelId, request.FolderId);
                var folders = await _db.getFoldersByParentId(channelId, request.FolderId);

                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var channelIdLong = long.TryParse(channelId, out var id) ? id : 0;

                var items = new List<FileItemDto>();

                // Add folders
                foreach (var folder in folders)
                {
                    items.Add(new FileItemDto
                    {
                        Id = folder.Id,
                        Name = folder.Name,
                        Path = folder.FilterPath,
                        IsFolder = true,
                        HasChildren = true,
                        DateModified = folder.DateModified,
                        DateCreated = folder.DateCreated,
                        Type = "folder",
                        Category = "Folder"
                    });
                }

                // Add files
                foreach (var file in files)
                {
                    var ext = file.Type?.ToLowerInvariant() ?? "";
                    var isAudio = IsAudioFile(ext);
                    var isVideo = IsVideoFile(ext);
                    var category = isAudio ? "Audio" : isVideo ? "Video" : GetCategory(ext);

                    string? streamUrl = null;
                    string? downloadUrl = null;

                    // Use /tfm/ endpoint with database ID for audio (handles caching)
                    if (isAudio)
                    {
                        streamUrl = $"{baseUrl}/api/mobile/stream/tfm/{channelId}/{file.Id}?fileName={Uri.EscapeDataString(file.Name)}";
                        downloadUrl = $"{baseUrl}/api/file/GetFileByTfmId/{Uri.EscapeDataString(file.Name)}?idChannel={channelId}&idFile={file.Id}";
                    }
                    else if (isVideo && file.MessageId.HasValue)
                    {
                        streamUrl = $"{baseUrl}/api/video/stream/{channelId}/{file.MessageId}/{Uri.EscapeDataString(file.Name)}";
                        downloadUrl = $"{baseUrl}/api/file/GetFileStream/{channelId}/{file.MessageId}/{Uri.EscapeDataString(file.Name)}";
                    }
                    else if (file.MessageId.HasValue)
                    {
                        downloadUrl = $"{baseUrl}/api/file/GetFileStream/{channelId}/{file.MessageId}/{Uri.EscapeDataString(file.Name)}";
                    }

                    items.Add(new FileItemDto
                    {
                        Id = file.Id,
                        Name = file.Name,
                        Path = file.FilterPath,
                        IsFolder = false,
                        HasChildren = false,
                        Size = file.Size,
                        Type = ext,
                        Category = category,
                        DateModified = file.DateModified,
                        DateCreated = file.DateCreated,
                        StreamUrl = streamUrl,
                        DownloadUrl = downloadUrl
                    });
                }

                // Apply filter
                if (!string.IsNullOrEmpty(request.Filter) && request.Filter.ToLower() != "all")
                {
                    items = items.Where(i => i.IsFolder || i.Category.ToLower() == request.Filter.ToLower()).ToList();
                }

                // Apply search
                if (!string.IsNullOrEmpty(request.SearchText))
                {
                    var searchLower = request.SearchText.ToLower();
                    items = items.Where(i => i.Name.ToLower().Contains(searchLower)).ToList();
                }

                // Sort - folders always first, then apply sorting
                items = (request.SortBy?.ToLower(), request.SortDescending) switch
                {
                    ("date", true) => items.OrderBy(i => !i.IsFolder).ThenByDescending(i => i.DateModified).ToList(),
                    ("date", false) => items.OrderBy(i => !i.IsFolder).ThenBy(i => i.DateModified).ToList(),
                    ("size", true) => items.OrderBy(i => !i.IsFolder).ThenByDescending(i => i.Size).ToList(),
                    ("size", false) => items.OrderBy(i => !i.IsFolder).ThenBy(i => i.Size).ToList(),
                    ("name", true) => items.OrderBy(i => !i.IsFolder).ThenByDescending(i => i.Name).ToList(),
                    ("name", false) => items.OrderBy(i => !i.IsFolder).ThenBy(i => i.Name).ToList(),
                    ("type", true) => items.OrderBy(i => !i.IsFolder).ThenByDescending(i => i.Type).ToList(),
                    ("type", false) => items.OrderBy(i => !i.IsFolder).ThenBy(i => i.Type).ToList(),
                    _ => items.OrderBy(i => !i.IsFolder).ThenBy(i => i.Name).ToList()
                };

                // Pagination
                var totalItems = items.Count;
                var paginatedItems = items
                    .Skip((request.Page - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToList();

                var result = new FolderContentsDto
                {
                    CurrentPath = request.Path ?? "/",
                    CurrentFolderId = request.FolderId ?? "",
                    Items = paginatedItems,
                    TotalItems = totalItems,
                    Stats = new FolderStatsDto
                    {
                        FolderCount = folders.Count,
                        FileCount = files.Count,
                        AudioCount = files.Count(f => IsAudioFile(f.Type)),
                        VideoCount = files.Count(f => IsVideoFile(f.Type)),
                        TotalSize = files.Sum(f => f.Size)
                    }
                };

                return Ok(ApiResponse<FolderContentsDto>.Ok(result, PaginationInfo.Create(request.Page, request.PageSize, totalItems)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error browsing Telegram files for channel {ChannelId}", channelId);
                return StatusCode(500, ApiResponse<FolderContentsDto>.Fail("Error browsing files"));
            }
        }

        /// <summary>
        /// Navegar archivos locales del servidor
        /// </summary>
        /// <remarks>
        /// Explora el sistema de archivos local del servidor dentro del directorio configurado (LOCALDIR).
        /// Útil para acceder a archivos descargados previamente o música almacenada localmente.
        ///
        /// **Seguridad:** Solo se puede navegar dentro del directorio LOCALDIR configurado.
        /// Intentos de acceder a rutas fuera de este directorio serán rechazados.
        ///
        /// **Navegación:**
        /// - Use el parámetro `Path` para navegar (ej: "music/album1")
        /// - Use `ParentPath` en la respuesta para volver al directorio padre
        /// - La raíz se representa con Path vacío o "/"
        ///
        /// Los archivos de audio incluyen StreamUrl para reproducción directa.
        /// </remarks>
        /// <param name="request">Parámetros de navegación: Path (ruta relativa), Filter, SearchText, SortBy, Page, PageSize</param>
        /// <returns>Contenido del directorio con archivos y estadísticas</returns>
        [HttpGet("local")]
        [ProducesResponseType(typeof(ApiResponse<FolderContentsDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<FolderContentsDto>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<FolderContentsDto>), StatusCodes.Status404NotFound)]
        public IActionResult BrowseLocalFiles([FromQuery] BrowseRequest request)
        {
            try
            {
                var basePath = FileService.LOCALDIR;
                var currentPath = string.IsNullOrEmpty(request.Path) ? "" : request.Path.TrimStart('/');
                var fullPath = Path.Combine(basePath, currentPath);

                // Security check - ensure path is within LOCALDIR
                var resolvedPath = Path.GetFullPath(fullPath);
                if (!resolvedPath.StartsWith(Path.GetFullPath(basePath)))
                {
                    return BadRequest(ApiResponse<FolderContentsDto>.Fail("Invalid path"));
                }

                if (!Directory.Exists(resolvedPath))
                {
                    return NotFound(ApiResponse<FolderContentsDto>.Fail("Directory not found"));
                }

                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var items = new List<FileItemDto>();
                var dirInfo = new DirectoryInfo(resolvedPath);

                // Add directories
                foreach (var dir in dirInfo.GetDirectories())
                {
                    var relativePath = Path.Combine(currentPath, dir.Name).Replace("\\", "/");
                    items.Add(FileItemDto.FromLocalDirectory(dir, relativePath));
                }

                // Add files
                foreach (var file in dirInfo.GetFiles())
                {
                    var relativePath = Path.Combine(currentPath, file.Name).Replace("\\", "/");
                    items.Add(FileItemDto.FromLocalFile(file, relativePath, baseUrl));
                }

                // Apply filter
                if (!string.IsNullOrEmpty(request.Filter) && request.Filter.ToLower() != "all")
                {
                    var filterLower = request.Filter.ToLower();
                    if (filterLower == "audio_folders")
                    {
                        // audio_folders = show audio files and folders
                        items = items.Where(i => i.IsFolder || i.Category.ToLower() == "audio").ToList();
                    }
                    else
                    {
                        items = items.Where(i => i.IsFolder || i.Category.ToLower() == filterLower).ToList();
                    }
                }

                // Apply search
                if (!string.IsNullOrEmpty(request.SearchText))
                {
                    var searchLower = request.SearchText.ToLower();
                    items = items.Where(i => i.Name.ToLower().Contains(searchLower)).ToList();
                }

                // Sort - folders always first, then apply sorting
                items = (request.SortBy?.ToLower(), request.SortDescending) switch
                {
                    ("date", true) => items.OrderBy(i => !i.IsFolder).ThenByDescending(i => i.DateModified).ToList(),
                    ("date", false) => items.OrderBy(i => !i.IsFolder).ThenBy(i => i.DateModified).ToList(),
                    ("size", true) => items.OrderBy(i => !i.IsFolder).ThenByDescending(i => i.Size).ToList(),
                    ("size", false) => items.OrderBy(i => !i.IsFolder).ThenBy(i => i.Size).ToList(),
                    ("name", true) => items.OrderBy(i => !i.IsFolder).ThenByDescending(i => i.Name).ToList(),
                    ("name", false) => items.OrderBy(i => !i.IsFolder).ThenBy(i => i.Name).ToList(),
                    ("type", true) => items.OrderBy(i => !i.IsFolder).ThenByDescending(i => i.Type).ToList(),
                    ("type", false) => items.OrderBy(i => !i.IsFolder).ThenBy(i => i.Type).ToList(),
                    _ => items.OrderBy(i => !i.IsFolder).ThenBy(i => i.Name).ToList()
                };

                // Pagination
                var totalItems = items.Count;
                var paginatedItems = items
                    .Skip((request.Page - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToList();

                // Get parent path
                string? parentPath = null;
                if (!string.IsNullOrEmpty(currentPath))
                {
                    var parentDir = Path.GetDirectoryName(currentPath);
                    parentPath = string.IsNullOrEmpty(parentDir) ? "" : parentDir.Replace("\\", "/");
                }

                // Count stats
                var allFiles = dirInfo.GetFiles();

                var result = new FolderContentsDto
                {
                    CurrentPath = "/" + currentPath,
                    CurrentFolderId = currentPath,
                    ParentPath = parentPath,
                    FolderName = string.IsNullOrEmpty(currentPath) ? "Local Files" : dirInfo.Name,
                    Items = paginatedItems,
                    TotalItems = totalItems,
                    Stats = new FolderStatsDto
                    {
                        FolderCount = dirInfo.GetDirectories().Length,
                        FileCount = allFiles.Length,
                        AudioCount = allFiles.Count(f => IsAudioFile(f.Extension)),
                        VideoCount = allFiles.Count(f => IsVideoFile(f.Extension)),
                        TotalSize = allFiles.Sum(f => f.Length)
                    }
                };

                return Ok(ApiResponse<FolderContentsDto>.Ok(result, PaginationInfo.Create(request.Page, request.PageSize, totalItems)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error browsing local files at path {Path}", request.Path);
                return StatusCode(500, ApiResponse<FolderContentsDto>.Fail("Error browsing files"));
            }
        }

        /// <summary>
        /// Obtener información detallada de un archivo por ID
        /// </summary>
        /// <remarks>
        /// Retorna información completa de un archivo específico de Telegram incluyendo:
        /// - Nombre, tamaño, tipo y categoría del archivo
        /// - MessageId original de Telegram
        /// - Fechas de creación y modificación
        /// - URLs de streaming (para audio/video) y descarga
        ///
        /// Útil para obtener las URLs de reproducción de un archivo específico
        /// sin necesidad de navegar por las carpetas.
        /// </remarks>
        /// <param name="channelId">ID del canal de Telegram</param>
        /// <param name="fileId">ID interno del archivo (asignado al indexar)</param>
        /// <returns>Información completa del archivo con URLs de streaming/descarga</returns>
        [HttpGet("telegram/{channelId}/{fileId}")]
        [ProducesResponseType(typeof(ApiResponse<ChannelFileDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<ChannelFileDto>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetFileInfo(string channelId, string fileId)
        {
            try
            {
                var file = await _db.getFileById(channelId, fileId);
                if (file == null)
                {
                    return NotFound(ApiResponse<ChannelFileDto>.Fail("File not found"));
                }

                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var channelIdLong = long.TryParse(channelId, out var id) ? id : 0;
                var dto = ChannelFileDto.FromBsonFileManagerModel(file, channelIdLong, baseUrl);

                return Ok(ApiResponse<ChannelFileDto>.Ok(dto));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting file info {FileId} from channel {ChannelId}", fileId, channelId);
                return StatusCode(500, ApiResponse<ChannelFileDto>.Fail("Error retrieving file info"));
            }
        }

        private static bool IsAudioFile(string type)
        {
            var audioExtensions = new[] { ".mp3", ".ogg", ".flac", ".aac", ".wav", ".m4a", ".wma", ".opus" };
            return audioExtensions.Contains(type?.ToLowerInvariant());
        }

        private static bool IsVideoFile(string type)
        {
            var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v" };
            return videoExtensions.Contains(type?.ToLowerInvariant());
        }

        private static string GetCategory(string ext)
        {
            if (IsAudioFile(ext)) return "Audio";
            if (IsVideoFile(ext)) return "Video";

            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg" };
            if (imageExtensions.Contains(ext)) return "Photo";

            return "Document";
        }
    }
}
