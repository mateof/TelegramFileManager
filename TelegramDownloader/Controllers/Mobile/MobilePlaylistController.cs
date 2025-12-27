using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Mobile;

namespace TelegramDownloader.Controllers.Mobile
{
    /// <summary>
    /// Gestión completa de playlists de audio. Permite crear, modificar, eliminar playlists
    /// y administrar los tracks dentro de cada playlist. Las playlists se almacenan en MongoDB
    /// y pueden contener tracks tanto de canales de Telegram como de archivos locales.
    /// </summary>
    [Route("api/mobile/playlists")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Playlists")]
    public class MobilePlaylistController : ControllerBase
    {
        private readonly IDbService _db;
        private readonly ILogger<MobilePlaylistController> _logger;

        public MobilePlaylistController(IDbService db, ILogger<MobilePlaylistController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Obtener todas las playlists
        /// </summary>
        /// <remarks>
        /// Retorna un listado de todas las playlists del usuario, ordenadas por fecha de modificación.
        /// Cada playlist incluye su ID, nombre, descripción y cantidad de tracks.
        /// No incluye los tracks completos - use GET /api/mobile/playlists/{id} para obtener los tracks.
        /// </remarks>
        /// <returns>Lista de playlists</returns>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<List<PlaylistDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllPlaylists()
        {
            try
            {
                var playlists = await _db.GetAllPlaylists();
                var dtos = playlists.Select(PlaylistDto.FromModel).ToList();

                return Ok(ApiResponse<List<PlaylistDto>>.Ok(dtos));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting playlists");
                return StatusCode(500, ApiResponse<List<PlaylistDto>>.Fail("Error retrieving playlists"));
            }
        }

        /// <summary>
        /// Obtener playlist por ID con todos sus tracks
        /// </summary>
        /// <remarks>
        /// Retorna la playlist completa incluyendo todos los tracks con sus URLs de streaming.
        /// Cada track incluye información del archivo, canal de origen y URL lista para reproducir.
        /// </remarks>
        /// <param name="id">ID de la playlist (MongoDB ObjectId)</param>
        /// <returns>Playlist con lista completa de tracks</returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResponse<PlaylistDetailDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<PlaylistDetailDto>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetPlaylistById(string id)
        {
            try
            {
                var playlist = await _db.GetPlaylistById(id);
                if (playlist == null)
                {
                    return NotFound(ApiResponse<PlaylistDetailDto>.Fail("Playlist not found"));
                }

                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var dto = PlaylistDetailDto.FromModel(playlist, baseUrl);

                return Ok(ApiResponse<PlaylistDetailDto>.Ok(dto));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting playlist {Id}", id);
                return StatusCode(500, ApiResponse<PlaylistDetailDto>.Fail("Error retrieving playlist"));
            }
        }

        /// <summary>
        /// Crear nueva playlist
        /// </summary>
        /// <remarks>
        /// Crea una nueva playlist vacía. Después de crearla, use POST /api/mobile/playlists/{id}/tracks
        /// para agregar tracks. El nombre es obligatorio y debe tener entre 1 y 100 caracteres.
        /// </remarks>
        /// <param name="request">Datos de la nueva playlist</param>
        /// <returns>Playlist creada con su ID asignado</returns>
        [HttpPost]
        [ProducesResponseType(typeof(ApiResponse<PlaylistDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<PlaylistDto>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreatePlaylist([FromBody] CreatePlaylistRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<PlaylistDto>.Fail("Invalid request"));
                }

                var playlist = new PlaylistModel
                {
                    Name = request.Name,
                    Description = request.Description
                };

                var created = await _db.CreatePlaylist(playlist);
                var dto = PlaylistDto.FromModel(created);

                return CreatedAtAction(nameof(GetPlaylistById), new { id = created.Id },
                    ApiResponse<PlaylistDto>.Ok(dto, "Playlist created successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating playlist");
                return StatusCode(500, ApiResponse<PlaylistDto>.Fail("Error creating playlist"));
            }
        }

        /// <summary>
        /// Actualizar datos de una playlist
        /// </summary>
        /// <remarks>
        /// Actualiza el nombre y/o descripción de una playlist existente.
        /// No modifica los tracks - use los endpoints específicos de tracks para eso.
        /// </remarks>
        /// <param name="id">ID de la playlist</param>
        /// <param name="request">Nuevos datos de la playlist</param>
        /// <returns>Playlist actualizada</returns>
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(ApiResponse<PlaylistDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<PlaylistDto>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdatePlaylist(string id, [FromBody] UpdatePlaylistRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<PlaylistDto>.Fail("Invalid request"));
                }

                var existing = await _db.GetPlaylistById(id);
                if (existing == null)
                {
                    return NotFound(ApiResponse<PlaylistDto>.Fail("Playlist not found"));
                }

                existing.Name = request.Name;
                existing.Description = request.Description;

                await _db.UpdatePlaylist(existing);
                var dto = PlaylistDto.FromModel(existing);

                return Ok(ApiResponse<PlaylistDto>.Ok(dto, "Playlist updated successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating playlist {Id}", id);
                return StatusCode(500, ApiResponse<PlaylistDto>.Fail("Error updating playlist"));
            }
        }

        /// <summary>
        /// Eliminar una playlist
        /// </summary>
        /// <remarks>
        /// Elimina permanentemente una playlist y todos sus tracks asociados.
        /// Esta acción no se puede deshacer. Los archivos originales no se eliminan,
        /// solo la referencia en la playlist.
        /// </remarks>
        /// <param name="id">ID de la playlist a eliminar</param>
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeletePlaylist(string id)
        {
            try
            {
                var existing = await _db.GetPlaylistById(id);
                if (existing == null)
                {
                    return NotFound(ApiResponse<object>.Fail("Playlist not found"));
                }

                await _db.DeletePlaylist(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting playlist {Id}", id);
                return StatusCode(500, ApiResponse<object>.Fail("Error deleting playlist"));
            }
        }

        /// <summary>
        /// Agregar track a una playlist
        /// </summary>
        /// <remarks>
        /// Agrega un nuevo track al final de la playlist. El track puede ser de un canal de Telegram
        /// (especificando ChannelId y FileId) o un archivo local (especificando DirectUrl).
        /// No se permiten tracks duplicados basados en el FileId.
        ///
        /// **Para archivos de Telegram:** Enviar ChannelId, FileId, FileName
        /// **Para archivos locales:** Enviar DirectUrl con la ruta del archivo
        /// </remarks>
        /// <param name="id">ID de la playlist</param>
        /// <param name="request">Datos del track a agregar</param>
        /// <returns>Track agregado con URL de streaming</returns>
        [HttpPost("{id}/tracks")]
        [ProducesResponseType(typeof(ApiResponse<TrackDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<TrackDto>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<TrackDto>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> AddTrack(string id, [FromBody] AddTrackRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<TrackDto>.Fail("Invalid request"));
                }

                var playlist = await _db.GetPlaylistById(id);
                if (playlist == null)
                {
                    return NotFound(ApiResponse<TrackDto>.Fail("Playlist not found"));
                }

                // Check for duplicates
                if (playlist.Tracks?.Any(t => t.FileId == request.FileId) == true)
                {
                    return Conflict(ApiResponse<TrackDto>.Fail("Track already exists in playlist"));
                }

                var track = request.ToModel();
                await _db.AddTrackToPlaylist(id, track);

                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var dto = TrackDto.FromModel(track, baseUrl);

                return Created($"/api/mobile/playlists/{id}/tracks/{track.FileId}",
                    ApiResponse<TrackDto>.Ok(dto, "Track added successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding track to playlist {Id}", id);
                return StatusCode(500, ApiResponse<TrackDto>.Fail("Error adding track"));
            }
        }

        /// <summary>
        /// Eliminar track de una playlist
        /// </summary>
        /// <remarks>
        /// Elimina un track específico de la playlist. Los demás tracks se reordenan automáticamente.
        /// El archivo original no se elimina, solo se quita de la playlist.
        /// </remarks>
        /// <param name="id">ID de la playlist</param>
        /// <param name="fileId">FileId del track a eliminar</param>
        [HttpDelete("{id}/tracks/{fileId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RemoveTrack(string id, string fileId)
        {
            try
            {
                var playlist = await _db.GetPlaylistById(id);
                if (playlist == null)
                {
                    return NotFound(ApiResponse<object>.Fail("Playlist not found"));
                }

                if (playlist.Tracks?.Any(t => t.FileId == fileId) != true)
                {
                    return NotFound(ApiResponse<object>.Fail("Track not found in playlist"));
                }

                await _db.RemoveTrackFromPlaylist(id, fileId);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing track {FileId} from playlist {Id}", fileId, id);
                return StatusCode(500, ApiResponse<object>.Fail("Error removing track"));
            }
        }

        /// <summary>
        /// Reordenar tracks en una playlist
        /// </summary>
        /// <remarks>
        /// Permite reorganizar el orden de reproducción de los tracks en una playlist.
        /// Envíe la lista completa de FileIds en el nuevo orden deseado.
        ///
        /// **Importante:**
        /// - La lista debe contener TODOS los FileIds de la playlist
        /// - El orden en el array determina la posición de cada track
        /// - FileIds que no existan en la playlist serán ignorados
        ///
        /// **Ejemplo de uso:**
        /// Si la playlist tiene tracks [A, B, C] y quiere el orden [C, A, B],
        /// envíe: `{ "orderedFileIds": ["C", "A", "B"] }`
        /// </remarks>
        /// <param name="id">ID de la playlist (MongoDB ObjectId)</param>
        /// <param name="request">Lista ordenada de FileIds en el nuevo orden deseado</param>
        /// <returns>Playlist actualizada con el nuevo orden de tracks</returns>
        [HttpPut("{id}/tracks/reorder")]
        [ProducesResponseType(typeof(ApiResponse<PlaylistDetailDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ReorderTracks(string id, [FromBody] ReorderTracksRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<object>.Fail("Invalid request"));
                }

                var playlist = await _db.GetPlaylistById(id);
                if (playlist == null)
                {
                    return NotFound(ApiResponse<object>.Fail("Playlist not found"));
                }

                await _db.ReorderPlaylistTracks(id, request.OrderedFileIds);

                // Return updated playlist
                var updated = await _db.GetPlaylistById(id);
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var dto = PlaylistDetailDto.FromModel(updated!, baseUrl);

                return Ok(ApiResponse<PlaylistDetailDto>.Ok(dto, "Tracks reordered successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reordering tracks in playlist {Id}", id);
                return StatusCode(500, ApiResponse<object>.Fail("Error reordering tracks"));
            }
        }
    }
}
