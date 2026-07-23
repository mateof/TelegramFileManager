using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Playlists mixing Telegram-hosted tracks and local files, shared with the
    /// web player and the audio app.
    ///
    /// A track either points at an indexed channel file (<c>channelId</c> +
    /// <c>fileId</c>) or at a local file (<c>directUrl</c>).
    /// </summary>
    [Route("api/v1/playlists")]
    [Tags("Playlists")]
    public class PlaylistsController : ApiV1ControllerBase
    {
        private readonly IDbService _db;
        private readonly IFileService _files;
        private readonly ILogger<PlaylistsController> _logger;

        public PlaylistsController(IDbService db, IFileService files, ILogger<PlaylistsController> logger)
        {
            _db = db;
            _files = files;
            _logger = logger;
        }

        /// <summary>Lists every playlist.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResult<List<PlaylistModel>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] PagedQuery? query = null)
        {
            query ??= new PagedQuery();
            try
            {
                var playlists = await _db.GetAllPlaylists() ?? new List<PlaylistModel>();
                var ordered = (query.SortBy?.ToLowerInvariant(), query.SortDescending) switch
                {
                    ("date", true) => playlists.OrderByDescending(p => p.DateModified).ToList(),
                    ("date", false) => playlists.OrderBy(p => p.DateModified).ToList(),
                    ("tracks", true) => playlists.OrderByDescending(p => p.TrackCount).ToList(),
                    ("tracks", false) => playlists.OrderBy(p => p.TrackCount).ToList(),
                    (_, true) => playlists.OrderByDescending(p => p.Name).ToList(),
                    _ => playlists.OrderBy(p => p.Name).ToList()
                };

                var (items, page) = Paginate(ordered, query);
                return OkPaged(items, page);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing playlists");
                return ErrorResult("Could not list the playlists", ex);
            }
        }

        /// <summary>One playlist with all of its tracks, in order.</summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResult<PlaylistModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                var playlist = await _db.GetPlaylistById(id);
                if (playlist == null)
                    return NotFoundResult("Playlist not found", ApiErrorCodes.PlaylistNotFound);

                playlist.Tracks = (playlist.Tracks ?? new List<PlaylistTrackModel>()).OrderBy(t => t.Order).ToList();
                return OkResult(playlist);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading playlist {Id}", id);
                return ErrorResult("Could not read the playlist", ex);
            }
        }

        /// <summary>Creates a playlist.</summary>
        [HttpPost]
        [ProducesResponseType(typeof(ApiResult<PlaylistModel>), StatusCodes.Status201Created)]
        public async Task<IActionResult> Create([FromBody] PlaylistModel playlist)
        {
            if (playlist == null || string.IsNullOrWhiteSpace(playlist.Name))
                return BadRequestResult("A playlist name is required");

            try
            {
                playlist.DateCreated = DateTime.Now;
                playlist.DateModified = DateTime.Now;
                var created = await _db.CreatePlaylist(playlist);
                return StatusCode(StatusCodes.Status201Created, ApiResult<PlaylistModel>.Ok(created, "Playlist created"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating playlist {Name}", playlist.Name);
                return ErrorResult("Could not create the playlist", ex);
            }
        }

        /// <summary>Updates a playlist's name, description or full track list.</summary>
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(ApiResult<PlaylistModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update(string id, [FromBody] PlaylistModel playlist)
        {
            if (playlist == null)
                return BadRequestResult("A playlist body is required");

            try
            {
                var existing = await _db.GetPlaylistById(id);
                if (existing == null)
                    return NotFoundResult("Playlist not found", ApiErrorCodes.PlaylistNotFound);

                existing.Name = string.IsNullOrWhiteSpace(playlist.Name) ? existing.Name : playlist.Name;
                existing.Description = playlist.Description ?? existing.Description;
                if (playlist.Tracks != null && playlist.Tracks.Count > 0)
                    existing.Tracks = playlist.Tracks;
                existing.DateModified = DateTime.Now;

                await _db.UpdatePlaylist(existing);
                return OkResult(existing, "Playlist updated");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating playlist {Id}", id);
                return ErrorResult("Could not update the playlist", ex);
            }
        }

        /// <summary>Deletes a playlist.</summary>
        [HttpDelete("{id}")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                await _db.DeletePlaylist(id);
                return OkEmpty("Playlist deleted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting playlist {Id}", id);
                return ErrorResult("Could not delete the playlist", ex);
            }
        }

        /// <summary>Appends a track to a playlist.</summary>
        [HttpPost("{id}/tracks")]
        [ProducesResponseType(typeof(ApiResult<PlaylistModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AddTrack(string id, [FromBody] PlaylistTrackModel track)
        {
            if (track == null || (string.IsNullOrWhiteSpace(track.FileId) && string.IsNullOrWhiteSpace(track.DirectUrl)))
                return BadRequestResult("A track needs either a fileId or a directUrl");

            try
            {
                var playlist = await _db.GetPlaylistById(id);
                if (playlist == null)
                    return NotFoundResult("Playlist not found", ApiErrorCodes.PlaylistNotFound);

                track.Order = (playlist.Tracks?.Count ?? 0);
                track.DateAdded = DateTime.Now;
                await _db.AddTrackToPlaylist(id, track);

                return OkResult(await _db.GetPlaylistById(id) ?? playlist, "Track added");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding a track to playlist {Id}", id);
                return ErrorResult("Could not add the track", ex);
            }
        }

        /// <summary>Removes a track from a playlist.</summary>
        [HttpDelete("{id}/tracks/{fileId}")]
        [ProducesResponseType(typeof(ApiResult<PlaylistModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> RemoveTrack(string id, string fileId)
        {
            try
            {
                var playlist = await _db.GetPlaylistById(id);
                if (playlist == null)
                    return NotFoundResult("Playlist not found", ApiErrorCodes.PlaylistNotFound);

                await _db.RemoveTrackFromPlaylist(id, fileId);
                return OkResult(await _db.GetPlaylistById(id) ?? playlist, "Track removed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing track {FileId} from playlist {Id}", fileId, id);
                return ErrorResult("Could not remove the track", ex);
            }
        }

        /// <summary>Reorders the tracks of a playlist.</summary>
        /// <param name="id">Playlist id.</param>
        /// <param name="orderedFileIds">File ids in the desired order.</param>
        [HttpPut("{id}/tracks/order")]
        [ProducesResponseType(typeof(ApiResult<PlaylistModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Reorder(string id, [FromBody] List<string> orderedFileIds)
        {
            if (orderedFileIds == null || orderedFileIds.Count == 0)
                return BadRequestResult("An ordered list of file ids is required");

            try
            {
                var playlist = await _db.GetPlaylistById(id);
                if (playlist == null)
                    return NotFoundResult("Playlist not found", ApiErrorCodes.PlaylistNotFound);

                await _db.ReorderPlaylistTracks(id, orderedFileIds);
                return OkResult(await _db.GetPlaylistById(id) ?? playlist, "Playlist reordered");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reordering playlist {Id}", id);
                return ErrorResult("Could not reorder the playlist", ex);
            }
        }

        /// <summary>Downloads every track of a playlist to the local storage.</summary>
        /// <remarks>
        /// Runs in the background and reports on the <c>transfers</c> hub like
        /// any other download.
        /// </remarks>
        /// <param name="id">Playlist id.</param>
        /// <param name="destinationFolder">Folder relative to the local root.</param>
        [HttpPost("{id}/download")]
        [RequireTelegramSession]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status202Accepted)]
        public async Task<IActionResult> Download(string id, [FromQuery] string? destinationFolder)
        {
            try
            {
                var playlist = await _db.GetPlaylistById(id);
                if (playlist == null)
                    return NotFoundResult("Playlist not found", ApiErrorCodes.PlaylistNotFound);

                var folder = string.IsNullOrWhiteSpace(destinationFolder) ? playlist.Name : destinationFolder;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _files.DownloadPlaylistToLocal(playlist, folder);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background download of playlist {Id} failed", id);
                    }
                });

                return Accepted(ApiResult.Done("Playlist download started"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting the download of playlist {Id}", id);
                return ErrorResult("Could not start the playlist download", ex);
            }
        }
    }
}
