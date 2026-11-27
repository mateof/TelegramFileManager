using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services.Player;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Player data shared by every device of the (single) user: favourite
    /// tracks, play history and resume positions. Tracks are identified by a
    /// key, <c>channel:{channelId}:{fileId}</c> or <c>local:{path}</c>, which
    /// the server computes from the track sent. Every write is announced as
    /// <c>PlayerDataChanged</c> on <c>/hubs/events</c>. Advertised as
    /// <c>player.sync</c>.
    /// </summary>
    [Route("api/v1/player")]
    [Tags("Player")]
    public class PlayerController : ApiV1ControllerBase
    {
        private readonly PlayerDataService _player;
        private readonly ILogger<PlayerController> _logger;

        public PlayerController(PlayerDataService player, ILogger<PlayerController> logger)
        {
            _player = player;
            _logger = logger;
        }

        // ---- Favourites ----

        /// <summary>Favourite tracks, newest first.</summary>
        /// <param name="page">1-based page.</param>
        /// <param name="pageSize">1-500, default 100.</param>
        [HttpGet("favorites")]
        [ProducesResponseType(typeof(ApiResult<ApiPlayerFavoritesPageDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public Task<IActionResult> Favorites([FromQuery] int page = 1, [FromQuery] int pageSize = 100) =>
            Run(async () => OkResult(await _player.ListFavorites(page, pageSize)), "list the favourites");

        /// <summary>Keys of every favourite track (for quick "is favourite" lookups).</summary>
        [HttpGet("favorites/keys")]
        [ProducesResponseType(typeof(ApiResult<List<string>>), StatusCodes.Status200OK)]
        public Task<IActionResult> FavoriteKeys() =>
            Run(async () => OkResult(await _player.FavoriteKeys()), "list the favourite keys");

        /// <summary>Marks a track as favourite (idempotent: refreshes the stored snapshot, keeps <c>addedAt</c>).</summary>
        [HttpPut("favorites")]
        [ProducesResponseType(typeof(ApiResult<ApiPlayerFavoriteDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public Task<IActionResult> AddFavorite([FromBody] ApiPlayerTrackDto? track) =>
            Run(async () => OkResult(await _player.AddFavorite(track)), "save the favourite");

        /// <summary>Removes a favourite. <c>204</c> also when it was not a favourite.</summary>
        [HttpDelete("favorites")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public Task<IActionResult> RemoveFavorite([FromQuery] string? key) =>
            Run(async () =>
            {
                await _player.RemoveFavorite(key);
                return NoContent();
            }, "remove the favourite");

        // ---- History ----

        /// <summary>Records a play. The oldest plays beyond 2000 are deleted.</summary>
        [HttpPost("history")]
        [ProducesResponseType(typeof(ApiResult<ApiPlayerHistoryEntryDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public Task<IActionResult> AddHistory([FromBody] ApiPlayerHistoryWriteRequest? request) =>
            Run(async () =>
            {
                var entry = await _player.AddHistory(request);
                return StatusCode(StatusCodes.Status201Created, ApiResult<ApiPlayerHistoryEntryDto>.Ok(entry));
            }, "record the play");

        /// <summary>Play history, newest first.</summary>
        /// <param name="limit">1-500, default 100.</param>
        /// <param name="before">Only plays strictly before this date (ISO 8601): pass the last <c>playedAt</c> to page.</param>
        [HttpGet("history")]
        [ProducesResponseType(typeof(ApiResult<ApiPlayerHistoryPageDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public Task<IActionResult> History([FromQuery] int limit = 100, [FromQuery] DateTimeOffset? before = null) =>
            Run(async () => OkResult(await _player.ListHistory(limit, before?.UtcDateTime)), "list the history");

        /// <summary>Most played tracks (by number of plays, then by the latest play).</summary>
        /// <param name="limit">1-500, default 50.</param>
        /// <param name="days">Only count the plays of the last N days (1 or more); all when omitted.</param>
        [HttpGet("history/top")]
        [ProducesResponseType(typeof(ApiResult<List<ApiPlayerTopTrackDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public Task<IActionResult> TopPlayed([FromQuery] int limit = 50, [FromQuery] int? days = null) =>
            Run(async () => OkResult(await _player.TopPlayed(limit, days)), "list the most played tracks");

        /// <summary>Clears the whole history.</summary>
        [HttpDelete("history")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public Task<IActionResult> ClearHistory() =>
            Run(async () =>
            {
                await _player.ClearHistory();
                return NoContent();
            }, "clear the history");

        // ---- Resume positions ----

        /// <summary>Saves where a track was left (meant for long audio).</summary>
        /// <remarks>
        /// When <c>positionSeconds</c> is in the last 2% or the last 15 s of
        /// <c>durationSeconds</c> the track counts as finished: its entry is
        /// deleted instead and the answer has <c>finished: true</c>. A
        /// <c>durationSeconds</c> of 0 (unknown) never counts as finished.
        /// </remarks>
        [HttpPut("resume")]
        [ProducesResponseType(typeof(ApiResult<ApiPlayerResumeDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public Task<IActionResult> SaveResume([FromBody] ApiPlayerResumeWriteRequest? request) =>
            Run(async () => OkResult(await _player.SaveResume(request)), "save the resume position");

        /// <summary>The resume position of one track; <c>404</c> when there is none.</summary>
        [HttpGet("resume")]
        [ProducesResponseType(typeof(ApiResult<ApiPlayerResumeDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public Task<IActionResult> GetResume([FromQuery] string? key) =>
            Run(async () =>
            {
                var entry = await _player.GetResume(key);
                return entry == null ? NotFoundResult("No resume position for this track") : OkResult(entry);
            }, "read the resume position");

        /// <summary>Resume positions, most recently updated first.</summary>
        /// <param name="limit">1-500, default 50.</param>
        [HttpGet("resume/list")]
        [ProducesResponseType(typeof(ApiResult<List<ApiPlayerResumeDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public Task<IActionResult> ListResume([FromQuery] int limit = 50) =>
            Run(async () => OkResult(await _player.ListResume(limit)), "list the resume positions");

        /// <summary>Deletes the resume position of a track. <c>204</c> also when there was none.</summary>
        [HttpDelete("resume")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public Task<IActionResult> RemoveResume([FromQuery] string? key) =>
            Run(async () =>
            {
                await _player.RemoveResume(key);
                return NoContent();
            }, "delete the resume position");

        private async Task<IActionResult> Run(Func<Task<IActionResult>> action, string what)
        {
            try
            {
                return await action();
            }
            catch (PlayerDataValidationException ex)
            {
                return BadRequestResult(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not {What}", what);
                return ErrorResult($"Could not {what}", ex);
            }
        }
    }
}
