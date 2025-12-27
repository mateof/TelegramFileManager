using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Mobile;

namespace TelegramDownloader.Controllers.Mobile
{
    /// <summary>
    /// Gestión de canales y chats de Telegram. Permite listar canales accesibles,
    /// organizarlos por carpetas, marcarlos como favoritos y navegar por su contenido.
    /// Los canales se obtienen de la sesión activa de Telegram del usuario.
    /// </summary>
    [Route("api/mobile/channels")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Channels")]
    public class MobileChannelController : ControllerBase
    {
        private readonly ITelegramService _ts;
        private readonly IDbService _db;
        private readonly ILogger<MobileChannelController> _logger;

        public MobileChannelController(ITelegramService ts, IDbService db, ILogger<MobileChannelController> logger)
        {
            _ts = ts;
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Obtener todos los canales de Telegram accesibles
        /// </summary>
        /// <remarks>
        /// Retorna un listado de todos los canales, grupos y chats a los que el usuario tiene acceso.
        /// Cada canal incluye información básica como nombre, tipo (channel/group/chat),
        /// si es favorito y si el usuario es propietario o puede publicar.
        /// Los canales se obtienen de los chats guardados en la sesión de Telegram.
        /// </remarks>
        /// <returns>Lista de canales con información básica</returns>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<List<ChannelDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllChannels()
        {
            try
            {
                var chats = await _ts.getAllSavedChats();
                var favorites = GeneralConfigStatic.config.FavouriteChannels ?? new List<long>();

                var dtos = new List<ChannelDto>();
                foreach (var chat in chats)
                {
                    var isFavorite = favorites.Contains(chat.chat.ID);
                    var isOwner = _ts.isChannelOwner(chat.chat.ID);
                    var dto = ChannelDto.FromChatViewBase(chat, isFavorite, isOwner);

                    // Get file count for this channel
                    try
                    {
                        var files = await _db.getAllFilesInDirectoryById(chat.chat.ID.ToString(), null);
                        dto.FileCount = files.Count(f => f.IsFile);
                    }
                    catch
                    {
                        dto.FileCount = 0;
                    }

                    dtos.Add(dto);
                }

                return Ok(ApiResponse<List<ChannelDto>>.Ok(dtos));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting channels");
                return StatusCode(500, ApiResponse<List<ChannelDto>>.Fail("Error retrieving channels"));
            }
        }

        /// <summary>
        /// Obtener canales organizados por carpetas de Telegram
        /// </summary>
        /// <remarks>
        /// Retorna los canales agrupados según las carpetas configuradas en Telegram.
        /// Las carpetas permiten organizar los chats por categorías (ej: Música, Trabajo, etc.).
        /// Incluye también una lista de canales que no pertenecen a ninguna carpeta.
        /// Cada carpeta muestra su emoji de icono y el conteo de canales que contiene.
        /// </remarks>
        /// <returns>Carpetas con sus canales y canales sin agrupar</returns>
        [HttpGet("folders")]
        [ProducesResponseType(typeof(ApiResponse<ChannelsWithFoldersDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetChannelsWithFolders()
        {
            try
            {
                var chatsWithFolders = await _ts.getChatsWithFolders();
                var favorites = GeneralConfigStatic.config.FavouriteChannels ?? new List<long>();

                // Helper function to create ChannelDto with FileCount
                async Task<ChannelDto> CreateChannelDtoAsync(ChatViewBase chat, bool isFavorite)
                {
                    var isOwner = _ts.isChannelOwner(chat.chat.ID);
                    var dto = ChannelDto.FromChatViewBase(chat, isFavorite, isOwner);
                    try
                    {
                        var files = await _db.getAllFilesInDirectoryById(chat.chat.ID.ToString(), null);
                        dto.FileCount = files.Count(f => f.IsFile);
                    }
                    catch { dto.FileCount = 0; }
                    return dto;
                }

                var result = new ChannelsWithFoldersDto { Folders = new(), UngroupedChannels = new() };

                // Process folders
                if (chatsWithFolders.Folders != null)
                {
                    foreach (var f in chatsWithFolders.Folders)
                    {
                        var folderDto = new ChannelFolderDto
                        {
                            Id = f.Id,
                            Title = f.Title,
                            IconEmoji = f.IconEmoji,
                            ChannelCount = f.Chats?.Count ?? 0,
                            Channels = new()
                        };

                        if (f.Chats != null)
                        {
                            foreach (var chat in f.Chats)
                            {
                                var isFavorite = favorites.Contains(chat.chat.ID);
                                folderDto.Channels.Add(await CreateChannelDtoAsync(chat, isFavorite));
                            }
                        }

                        result.Folders.Add(folderDto);
                    }
                }

                // Process ungrouped channels
                if (chatsWithFolders.UngroupedChats != null)
                {
                    foreach (var chat in chatsWithFolders.UngroupedChats)
                    {
                        var isFavorite = favorites.Contains(chat.chat.ID);
                        result.UngroupedChannels.Add(await CreateChannelDtoAsync(chat, isFavorite));
                    }
                }

                result.TotalChannels = result.Folders.Sum(f => f.ChannelCount) + result.UngroupedChannels.Count;

                return Ok(ApiResponse<ChannelsWithFoldersDto>.Ok(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting channels with folders");
                return StatusCode(500, ApiResponse<ChannelsWithFoldersDto>.Fail("Error retrieving channels"));
            }
        }

        /// <summary>
        /// Obtener canales favoritos
        /// </summary>
        /// <remarks>
        /// Retorna únicamente los canales que el usuario ha marcado como favoritos.
        /// Los favoritos se almacenan en la configuración local y persisten entre sesiones.
        /// Útil para acceder rápidamente a los canales más utilizados.
        /// </remarks>
        /// <returns>Lista de canales favoritos</returns>
        [HttpGet("favorites")]
        [ProducesResponseType(typeof(ApiResponse<List<ChannelDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetFavoriteChannels()
        {
            try
            {
                var favorites = await _ts.GetFouriteChannels(false);

                var dtos = new List<ChannelDto>();
                foreach (var chat in favorites)
                {
                    var isOwner = _ts.isChannelOwner(chat.chat.ID);
                    var dto = ChannelDto.FromChatViewBase(chat, true, isOwner);

                    try
                    {
                        var files = await _db.getAllFilesInDirectoryById(chat.chat.ID.ToString(), null);
                        dto.FileCount = files.Count(f => f.IsFile);
                    }
                    catch
                    {
                        dto.FileCount = 0;
                    }

                    dtos.Add(dto);
                }

                return Ok(ApiResponse<List<ChannelDto>>.Ok(dtos));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting favorite channels");
                return StatusCode(500, ApiResponse<List<ChannelDto>>.Fail("Error retrieving favorites"));
            }
        }

        /// <summary>
        /// Obtener información detallada y estadísticas de un canal
        /// </summary>
        /// <remarks>
        /// Retorna información completa del canal incluyendo estadísticas de archivos.
        /// Las estadísticas incluyen: cantidad total de archivos, tamaño total,
        /// y desglose por tipo (audio, video, documentos).
        /// También indica si el usuario es propietario del canal y si puede publicar.
        /// </remarks>
        /// <param name="id">ID del canal de Telegram (número largo)</param>
        /// <returns>Información detallada del canal con estadísticas</returns>
        [HttpGet("{id}/info")]
        [ProducesResponseType(typeof(ApiResponse<ChannelDetailDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<ChannelDetailDto>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetChannelInfo(long id)
        {
            try
            {
                var channelInfo = _ts.GetChannelInfo(id);
                if (!channelInfo.exists)
                {
                    return NotFound(ApiResponse<ChannelDetailDto>.Fail("Channel not found"));
                }

                var allChats = await _ts.getAllSavedChats();
                var chat = allChats.FirstOrDefault(c => c.chat.ID == id);

                if (chat == null)
                {
                    return NotFound(ApiResponse<ChannelDetailDto>.Fail("Channel not found"));
                }

                var favorites = GeneralConfigStatic.config.FavouriteChannels ?? new List<long>();
                var isFavorite = favorites.Contains(id);
                var isOwner = _ts.isChannelOwner(id);

                // Get file statistics
                var files = await _db.getAllFilesInDirectoryById(id.ToString(), null);
                var audioCount = files.Count(f => f.IsFile && IsAudioFile(f.Type));
                var videoCount = files.Count(f => f.IsFile && IsVideoFile(f.Type));
                var documentCount = files.Count(f => f.IsFile && !IsAudioFile(f.Type) && !IsVideoFile(f.Type));

                var dto = new ChannelDetailDto
                {
                    Id = id,
                    Name = channelInfo.name ?? "Unknown",
                    ImageUrl = $"/api/channel/image/{id}",
                    IsOwner = isOwner,
                    CanPost = isOwner || _ts.isMyChat(id),
                    IsFavorite = isFavorite,
                    FileCount = files.Count(f => f.IsFile),
                    TotalSize = files.Where(f => f.IsFile).Sum(f => f.Size),
                    AudioCount = audioCount,
                    VideoCount = videoCount,
                    DocumentCount = documentCount
                };

                return Ok(ApiResponse<ChannelDetailDto>.Ok(dto));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting channel info {Id}", id);
                return StatusCode(500, ApiResponse<ChannelDetailDto>.Fail("Error retrieving channel info"));
            }
        }

        /// <summary>
        /// Obtener archivos de un canal con filtrado y paginación
        /// </summary>
        /// <remarks>
        /// Lista los archivos de un canal con soporte completo para filtrado, búsqueda,
        /// ordenamiento y paginación. Incluye tanto carpetas como archivos.
        ///
        /// **Filtros disponibles (Filter):** audio, video, documents, photos, all, audio_folders (solo audio y carpetas)
        /// **Ordenamiento (SortBy):** name, date, size, type
        ///
        /// Cada archivo incluye URLs de streaming y descarga generadas automáticamente.
        /// Las carpetas se muestran primero, seguidas de los archivos.
        /// </remarks>
        /// <param name="id">ID del canal de Telegram</param>
        /// <param name="request">Parámetros de filtrado: Page, PageSize, Filter, SortBy, FolderId, SearchText</param>
        /// <returns>Lista paginada de archivos con URLs de streaming</returns>
        [HttpGet("{id}/files")]
        [ProducesResponseType(typeof(ApiResponse<List<ChannelFileDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetChannelFiles(long id, [FromQuery] ChannelFilesRequest request)
        {
            try
            {
                var files = await _db.getFilesByParentId(id.ToString(), request.FolderId);
                var folders = await _db.getFoldersByParentId(id.ToString(), request.FolderId);

                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                // Combine folders and files
                var items = new List<ChannelFileDto>();

                // Add folders first
                items.AddRange(folders.Select(f => ChannelFileDto.FromBsonFileManagerModel(f, id, baseUrl)));

                // Add files
                var fileItems = files.Select(f => ChannelFileDto.FromBsonFileManagerModel(f, id, baseUrl));

                // Apply filter
                if (!string.IsNullOrEmpty(request.Filter) && request.Filter.ToLower() != "all")
                {
                    var filterLower = request.Filter.ToLower();

                    // Special filter: audio_folders shows only folders and audio files
                    if (filterLower == "audio_folders")
                    {
                        fileItems = fileItems.Where(f => f.Category == "Audio");
                        // Folders are already added above
                    }
                    else
                    {
                        fileItems = filterLower switch
                        {
                            "audio" => fileItems.Where(f => f.Category == "Audio"),
                            "video" => fileItems.Where(f => f.Category == "Video"),
                            "documents" => fileItems.Where(f => f.Category == "Document"),
                            "photos" => fileItems.Where(f => f.Category == "Photo"),
                            _ => fileItems
                        };

                        // For non-audio_folders filters, also filter out folders if filter is specific
                        if (filterLower != "folders")
                        {
                            // Keep folders when filtering
                        }
                    }
                }

                // Apply search to files
                if (!string.IsNullOrEmpty(request.SearchText))
                {
                    var searchLower = request.SearchText.ToLower();
                    fileItems = fileItems.Where(f => f.Name.ToLower().Contains(searchLower));

                    // Also search in folders
                    items = items.Where(f => f.Name.ToLower().Contains(searchLower)).ToList();
                }

                items.AddRange(fileItems);

                // Apply sorting (folders first, then apply sort)
                items = (request.SortBy?.ToLower(), request.SortDescending) switch
                {
                    ("date", true) => items.OrderBy(f => f.IsFile).ThenByDescending(f => f.DateModified).ToList(),
                    ("date", false) => items.OrderBy(f => f.IsFile).ThenBy(f => f.DateModified).ToList(),
                    ("size", true) => items.OrderBy(f => f.IsFile).ThenByDescending(f => f.Size).ToList(),
                    ("size", false) => items.OrderBy(f => f.IsFile).ThenBy(f => f.Size).ToList(),
                    ("name", true) => items.OrderBy(f => f.IsFile).ThenByDescending(f => f.Name).ToList(),
                    ("name", false) => items.OrderBy(f => f.IsFile).ThenBy(f => f.Name).ToList(),
                    ("type", true) => items.OrderBy(f => f.IsFile).ThenByDescending(f => f.Type).ToList(),
                    ("type", false) => items.OrderBy(f => f.IsFile).ThenBy(f => f.Type).ToList(),
                    _ => items.OrderBy(f => f.IsFile).ThenBy(f => f.Name).ToList() // Folders first, then by name
                };

                // Apply pagination
                var totalItems = items.Count;
                var paginatedItems = items
                    .Skip((request.Page - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToList();

                var pagination = PaginationInfo.Create(request.Page, request.PageSize, totalItems);

                return Ok(ApiResponse<List<ChannelFileDto>>.Ok(paginatedItems, pagination));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting files from channel {Id}", id);
                return StatusCode(500, ApiResponse<List<ChannelFileDto>>.Fail("Error retrieving files"));
            }
        }

        /// <summary>
        /// Navegar por la estructura de carpetas del canal
        /// </summary>
        /// <remarks>
        /// Permite explorar el contenido del canal como un sistema de archivos.
        /// Retorna el contenido de una carpeta específica incluyendo subcarpetas y archivos.
        /// Incluye información de la carpeta actual, ruta padre y estadísticas.
        ///
        /// Para navegar a una subcarpeta, use el FolderId del item deseado.
        /// Para volver al directorio padre, use el ParentFolderId retornado.
        /// </remarks>
        /// <param name="id">ID del canal de Telegram</param>
        /// <param name="request">Parámetros de navegación: FolderId, Page, PageSize, Filter, SortBy</param>
        /// <returns>Contenido de la carpeta con estadísticas</returns>
        [HttpGet("{id}/browse")]
        [ProducesResponseType(typeof(ApiResponse<FolderContentsDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> BrowseChannel(long id, [FromQuery] BrowseRequest request)
        {
            try
            {
                var files = await _db.getFilesByParentId(id.ToString(), request.FolderId);
                var folders = await _db.getFoldersByParentId(id.ToString(), request.FolderId);

                var baseUrl = $"{Request.Scheme}://{Request.Host}";

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
                        DateCreated = folder.DateCreated
                    });
                }

                // Add files
                foreach (var file in files)
                {
                    var dto = ChannelFileDto.FromBsonFileManagerModel(file, id, baseUrl);
                    items.Add(new FileItemDto
                    {
                        Id = file.Id,
                        Name = file.Name,
                        Path = file.FilterPath,
                        IsFolder = false,
                        HasChildren = false,
                        Size = file.Size,
                        Type = file.Type,
                        Category = dto.Category,
                        DateModified = file.DateModified,
                        DateCreated = file.DateCreated,
                        StreamUrl = dto.StreamUrl,
                        DownloadUrl = dto.DownloadUrl
                    });
                }

                // Apply filter and search
                if (!string.IsNullOrEmpty(request.Filter) && request.Filter.ToLower() != "all")
                {
                    items = items.Where(i => i.IsFolder || i.Category.ToLower() == request.Filter.ToLower()).ToList();
                }

                if (!string.IsNullOrEmpty(request.SearchText))
                {
                    var searchLower = request.SearchText.ToLower();
                    items = items.Where(i => i.Name.ToLower().Contains(searchLower)).ToList();
                }

                // Sort
                items = (request.SortBy?.ToLower(), request.SortDescending) switch
                {
                    ("date", true) => items.OrderBy(i => !i.IsFolder).ThenByDescending(i => i.DateModified).ToList(),
                    ("date", false) => items.OrderBy(i => !i.IsFolder).ThenBy(i => i.DateModified).ToList(),
                    ("size", true) => items.OrderBy(i => !i.IsFolder).ThenByDescending(i => i.Size).ToList(),
                    ("size", false) => items.OrderBy(i => !i.IsFolder).ThenBy(i => i.Size).ToList(),
                    _ => items.OrderBy(i => !i.IsFolder).ThenBy(i => i.Name).ToList()
                };

                // Pagination
                var totalItems = items.Count;
                var paginatedItems = items
                    .Skip((request.Page - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToList();

                // Get folder info
                BsonFileManagerModel? currentFolder = null;
                if (!string.IsNullOrEmpty(request.FolderId))
                {
                    currentFolder = await _db.getFileById(id.ToString(), request.FolderId);
                }

                var result = new FolderContentsDto
                {
                    CurrentPath = currentFolder?.FilterPath ?? "/",
                    CurrentFolderId = request.FolderId ?? "",
                    ParentFolderId = currentFolder?.FilterId,
                    FolderName = currentFolder?.Name ?? _ts.getChatName(id) ?? "Channel",
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
                _logger.LogError(ex, "Error browsing channel {Id}", id);
                return StatusCode(500, ApiResponse<FolderContentsDto>.Fail("Error browsing channel"));
            }
        }

        /// <summary>
        /// Agregar canal a favoritos
        /// </summary>
        /// <remarks>
        /// Marca el canal especificado como favorito. Los canales favoritos
        /// aparecen en el listado de favoritos y pueden ser accedidos rápidamente.
        /// Si el canal ya es favorito, la operación no produce error.
        /// </remarks>
        /// <param name="id">ID del canal de Telegram a agregar como favorito</param>
        [HttpPost("{id}/favorite")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> AddToFavorites(long id)
        {
            try
            {
                await _ts.AddFavouriteChannel(id);
                return Ok(ApiResponse<object>.Ok(null, "Channel added to favorites"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding channel {Id} to favorites", id);
                return StatusCode(500, ApiResponse<object>.Fail("Error adding to favorites"));
            }
        }

        /// <summary>
        /// Quitar canal de favoritos
        /// </summary>
        /// <remarks>
        /// Elimina el canal de la lista de favoritos del usuario.
        /// Si el canal no estaba en favoritos, la operación no produce error.
        /// Retorna 204 No Content en caso de éxito.
        /// </remarks>
        /// <param name="id">ID del canal de Telegram a quitar de favoritos</param>
        [HttpDelete("{id}/favorite")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> RemoveFromFavorites(long id)
        {
            try
            {
                await _ts.RemoveFavouriteChannel(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing channel {Id} from favorites", id);
                return StatusCode(500, ApiResponse<object>.Fail("Error removing from favorites"));
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
    }
}
