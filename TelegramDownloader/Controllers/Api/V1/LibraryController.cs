using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Media library: movies and series identified from the channel indexes
    /// (Plex/Emby style), playback progress and manual corrections.
    ///
    /// Everything here is served from MongoDB, so it works without a Telegram
    /// session; only playing a file needs one. The catalogue endpoints answer
    /// <c>503 library_disabled</c> until the library is enabled in the settings
    /// and at least one metadata provider has an API key. Playback progress is
    /// always available.
    /// </summary>
    [Route("api/v1/library")]
    [Tags("Library")]
    public class LibraryController : ApiV1ControllerBase
    {
        private readonly LibraryService _library;
        private readonly LibraryScanService _scan;
        private readonly LibraryImageService _images;
        private readonly ILibraryDbService _db;
        private readonly ILogger<LibraryController> _logger;

        public LibraryController(LibraryService library, LibraryScanService scan, LibraryImageService images, ILibraryDbService db, ILogger<LibraryController> logger)
        {
            _library = library;
            _scan = scan;
            _images = images;
            _db = db;
            _logger = logger;
        }

        private IActionResult? Disabled() =>
            GeneralConfigStatic.config.LibraryEnabled ? null
                : UnavailableResult("The media library is disabled. Enable it and add a metadata provider key in the server settings.", ApiErrorCodes.LibraryDisabled);

        #region Catalogue

        /// <summary>Lists the movies and series of the library.</summary>
        /// <remarks>
        /// <c>sortBy</c> accepts <c>title</c> (default), <c>year</c>, <c>added</c>,
        /// <c>rating</c> and <c>lastPlayed</c>. <c>status=review</c> keeps only
        /// items that have a file whose identification deserves a look.
        /// </remarks>
        [HttpGet("items")]
        [ProducesResponseType(typeof(ApiResult<List<ApiLibraryItemDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Items(
            [FromQuery] PagedQuery query,
            [FromQuery] string? kind = null,
            [FromQuery] string? search = null,
            [FromQuery] string? genre = null,
            [FromQuery] string? status = null,
            [FromQuery] bool includeHidden = false)
        {
            if (Disabled() is IActionResult r) return r;
            var items = await _library.ListItemsAsync(kind, search, genre, status, includeHidden, query.SortBy, query.SortDescending, BaseUrl);
            var (page, info) = Paginate(items, query);
            return OkPaged(page, info);
        }

        /// <summary>One item with its files (movies) or seasons and episodes (series), each with its watch state.</summary>
        [HttpGet("items/{id}")]
        [ProducesResponseType(typeof(ApiResult<ApiLibraryItemDetailDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Item(string id)
        {
            var dto = await _library.GetItemDetailAsync(id, BaseUrl);
            return dto == null ? NotFoundResult("Item not found") : OkResult(dto);
        }

        /// <summary>Files with playback in progress, most recent first. Includes files that are not identified.</summary>
        [HttpGet("continue")]
        [ProducesResponseType(typeof(ApiResult<List<ApiLibraryFileDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Continue([FromQuery] int limit = 50, [FromQuery] bool includeHidden = false) =>
            OkResult(await _library.ContinueAsync(BaseUrl, Math.Clamp(limit, 1, 500), includeHidden));

        /// <summary>Recently added items.</summary>
        [HttpGet("recent")]
        [ProducesResponseType(typeof(ApiResult<List<ApiLibraryItemDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Recent([FromQuery] string? kind = null, [FromQuery] int limit = 30, [FromQuery] bool includeHidden = false)
        {
            if (Disabled() is IActionResult r) return r;
            return OkResult(await _library.RecentAsync(kind, Math.Clamp(limit, 1, 500), BaseUrl, includeHidden));
        }

        /// <summary>Genres present in the library, with item counts.</summary>
        [HttpGet("genres")]
        [ProducesResponseType(typeof(ApiResult<List<ApiLibraryGenreDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Genres([FromQuery] string? kind = null)
        {
            if (Disabled() is IActionResult r) return r;
            return OkResult(await _library.GenresAsync(kind));
        }

        /// <summary>Library configuration summary, counts and the state of the last scan. Works even when the library is disabled.</summary>
        [HttpGet("stats")]
        [ProducesResponseType(typeof(ApiResult<ApiLibraryStatsDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Stats() => OkResult(await _library.StatsAsync());

        /// <summary>Files known to the library, filtered by identification status. The review queue.</summary>
        /// <remarks><c>status</c>: <c>matched</c>, <c>review</c>, <c>unmatched</c> or <c>ignored</c>.</remarks>
        [HttpGet("files")]
        [ProducesResponseType(typeof(ApiResult<List<ApiLibraryFileDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Files([FromQuery] PagedQuery query, [FromQuery] string? status = null, [FromQuery] long? channelId = null, [FromQuery] string? search = null)
        {
            if (Disabled() is IActionResult r) return r;
            var files = await _library.ListFilesAsync(status, channelId, search, BaseUrl);
            var (page, info) = Paginate(files, query);
            return OkPaged(page, info);
        }

        /// <summary>One file as the library sees it (identification and watch state).</summary>
        [HttpGet("files/{channelId:long}/{fileId}")]
        [ProducesResponseType(typeof(ApiResult<ApiLibraryFileDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> File(long channelId, string fileId)
        {
            var dto = await _library.GetFileAsync(channelId, fileId, BaseUrl);
            return dto == null ? NotFoundResult("File not found in the library", ApiErrorCodes.FileNotFound) : OkResult(dto);
        }

        /// <summary>A poster, backdrop or still, cached on this server. <c>key</c> comes from the item DTOs.</summary>
        [HttpGet("images/{key}")]
        [ResponseCache(Duration = 604800)]
        public async Task<IActionResult> Image(string key, [FromQuery] string? size = null, CancellationToken ct = default)
        {
            var url = LibraryImageService.Decode(key);
            if (url == null || !LibraryImageService.IsAllowed(url))
                return BadRequestResult("Unknown image");
            var path = await _images.GetLocalPathAsync(url, LibraryImageService.NormalizeSize(size), ct);
            if (path == null) return NotFound();
            return PhysicalFile(path, LibraryImageService.ContentTypeOf(path));
        }

        #endregion

        #region Scan

        /// <summary>Starts a library scan in the background.</summary>
        /// <remarks>
        /// Incremental: files already identified are left alone unless
        /// <c>force</c> is set, and manual identifications are never touched.
        /// Poll <c>GET /api/v1/library/scan</c> for progress. Answers
        /// <c>409 already_running</c> while a scan is in progress.
        /// </remarks>
        [HttpPost("scan")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status409Conflict)]
        public IActionResult Scan([FromBody] LibraryScanRequest? request)
        {
            if (Disabled() is IActionResult r) return r;
            request ??= new LibraryScanRequest();
            if (!_scan.Start(request.ChannelId, request.Force, out var error))
            {
                if (_scan.IsRunning) return ConflictResult(error ?? "A scan is already running", ApiErrorCodes.AlreadyRunning);
                return UnavailableResult(error ?? "The library cannot scan right now", ApiErrorCodes.LibraryDisabled);
            }
            return Accepted(ApiResult.Done("Library scan started"));
        }

        /// <summary>State of the current or last scan. Persisted, so it survives restarts.</summary>
        [HttpGet("scan")]
        [ProducesResponseType(typeof(ApiResult<ApiLibraryScanStateDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ScanState() => OkResult(ApiLibraryScanStateDto.From(await _scan.GetStateAsync()));

        /// <summary>Cancels the running scan. What was already identified stays.</summary>
        [HttpDelete("scan")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public IActionResult CancelScan()
        {
            _scan.Cancel();
            return OkEmpty("Scan cancellation requested");
        }

        #endregion

        #region Manual identification

        /// <summary>Searches the enabled metadata providers, for picking the right title by hand.</summary>
        /// <remarks>Pass <c>q</c> (with optional <c>kind</c> and <c>year</c>) or an <c>imdbId</c> like <c>tt0111161</c>.</remarks>
        [HttpGet("providers/search")]
        [ProducesResponseType(typeof(ApiResult<List<ApiProviderCandidateDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> SearchProviders([FromQuery] string? q = null, [FromQuery] string? kind = null, [FromQuery] int? year = null,
            [FromQuery] string? imdbId = null, [FromQuery] string? provider = null, CancellationToken ct = default)
        {
            if (Disabled() is IActionResult r) return r;
            if (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(imdbId))
                return BadRequestResult("q or imdbId is required");
            try
            {
                return OkResult(await _library.SearchProvidersAsync(q?.Trim(), kind, year, imdbId, provider, BaseUrl, ct));
            }
            catch (ProviderException ex)
            {
                return UnavailableResult(ex.Message);
            }
        }

        /// <summary>The metadata providers and how they are configured (keys are masked).</summary>
        [HttpGet("providers")]
        [ProducesResponseType(typeof(ApiResult<List<ApiLibraryProviderDto>>), StatusCodes.Status200OK)]
        public IActionResult Providers() =>
            OkResult(MetadataProviderRegistry.EffectiveConfigs(GeneralConfigStatic.config)
                .Select(c => ApiLibraryProviderDto.From(MetadataProviderRegistry.Describe(c.Id)!, c)).ToList());

        /// <summary>Assigns a file to a title by hand. The file is locked: rescans never change it.</summary>
        /// <remarks>For a series file <c>season</c> and <c>episode</c> are required.</remarks>
        [HttpPut("files/{channelId:long}/{fileId}/match")]
        [ProducesResponseType(typeof(ApiResult<ApiLibraryFileDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> MatchFile(long channelId, string fileId, [FromBody] LibraryMatchFileRequest request, CancellationToken ct)
        {
            if (Disabled() is IActionResult r) return r;
            if (request == null || string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.ProviderId))
                return BadRequestResult("provider and providerId are required");
            try
            {
                var (file, error) = await _library.MatchFileAsync(channelId, fileId, request, ct);
                if (file == null) return error == "file not found" ? NotFoundResult(error, ApiErrorCodes.FileNotFound) : BadRequestResult(error ?? "Could not match the file");
                return OkResult(await _library.GetFileAsync(channelId, fileId, BaseUrl), "File identified");
            }
            catch (ProviderException ex)
            {
                return UnavailableResult(ex.Message);
            }
        }

        /// <summary>Removes the identification of a file. It stays unidentified until matched by hand again.</summary>
        [HttpDelete("files/{channelId:long}/{fileId}/match")]
        [ProducesResponseType(typeof(ApiResult<ApiLibraryFileDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> UnmatchFile(long channelId, string fileId)
        {
            var file = await _library.UnmatchFileAsync(channelId, fileId);
            if (file == null) return NotFoundResult("File not found in the library", ApiErrorCodes.FileNotFound);
            return OkResult(await _library.GetFileAsync(channelId, fileId, BaseUrl), "Identification removed");
        }

        /// <summary>Keeps a file out of the library (trailers, extras, samples).</summary>
        [HttpPost("files/{channelId:long}/{fileId}/ignore")]
        [ProducesResponseType(typeof(ApiResult<ApiLibraryFileDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Ignore(long channelId, string fileId)
        {
            var file = await _library.SetIgnoredAsync(channelId, fileId, true);
            if (file == null) return NotFoundResult("File not found", ApiErrorCodes.FileNotFound);
            return OkResult(await _library.GetFileAsync(channelId, fileId, BaseUrl), "File ignored");
        }

        /// <summary>Puts an ignored file back; the next scan identifies it again.</summary>
        [HttpDelete("files/{channelId:long}/{fileId}/ignore")]
        [ProducesResponseType(typeof(ApiResult<ApiLibraryFileDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Unignore(long channelId, string fileId)
        {
            var file = await _library.SetIgnoredAsync(channelId, fileId, false);
            if (file == null) return NotFoundResult("File not found", ApiErrorCodes.FileNotFound);
            return OkResult(await _library.GetFileAsync(channelId, fileId, BaseUrl), "File restored");
        }

        /// <summary>Re-identifies a whole item: every file it has moves to the given provider title.</summary>
        /// <remarks>The typical fix for "it picked the other movie with the same name". The result is locked.</remarks>
        [HttpPut("items/{id}/identify")]
        [ProducesResponseType(typeof(ApiResult<ApiLibraryItemDetailDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Identify(string id, [FromBody] LibraryIdentifyItemRequest request, CancellationToken ct)
        {
            if (Disabled() is IActionResult r) return r;
            if (request == null || string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.ProviderId))
                return BadRequestResult("provider and providerId are required");
            try
            {
                var (item, error) = await _library.IdentifyItemAsync(id, request, ct);
                if (item == null) return error == "item not found" ? NotFoundResult(error) : BadRequestResult(error ?? "Could not identify the item");
                return OkResult(await _library.GetItemDetailAsync(item.Id, BaseUrl), "Item identified");
            }
            catch (ProviderException ex)
            {
                return UnavailableResult(ex.Message);
            }
        }

        /// <summary>Downloads the metadata of an item again (new poster, new episodes...).</summary>
        [HttpPost("items/{id}/refresh")]
        [ProducesResponseType(typeof(ApiResult<ApiLibraryItemDetailDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> RefreshItem(string id, CancellationToken ct)
        {
            if (Disabled() is IActionResult r) return r;
            try
            {
                var (item, error) = await _library.RefreshItemAsync(id, ct);
                if (item == null) return error == "item not found" ? NotFoundResult(error) : BadRequestResult(error ?? "Could not refresh the item");
                return OkResult(await _library.GetItemDetailAsync(item.Id, BaseUrl), "Metadata refreshed");
            }
            catch (ProviderException ex)
            {
                return UnavailableResult(ex.Message);
            }
        }

        #endregion

        #region Watch state

        /// <summary>Playback progress of a file, or <c>data: null</c> when it was never played.</summary>
        [HttpGet("watch/{channelId:long}/{fileId}")]
        [ProducesResponseType(typeof(ApiResult<ApiWatchStateDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Watch(long channelId, string fileId)
        {
            var state = await _db.GetWatch(channelId, fileId);
            return OkResult(state == null ? null : ApiWatchStateDto.From(state));
        }

        /// <summary>Saves the playback position of a file. Call it every few seconds while playing.</summary>
        /// <remarks>
        /// The file counts as watched once the position passes the configured
        /// threshold of the duration (92 % by default) or when <c>completed</c>
        /// is sent. Works for any indexed video, identified or not.
        /// </remarks>
        [HttpPut("watch/{channelId:long}/{fileId}")]
        [ProducesResponseType(typeof(ApiResult<ApiWatchStateDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateWatch(long channelId, string fileId, [FromBody] LibraryWatchUpdateRequest request)
        {
            if (request == null) return BadRequestResult("positionMs and durationMs are required");
            var state = await _library.UpdateWatchAsync(channelId, fileId, request);
            if (state == null) return NotFoundResult("File not found", ApiErrorCodes.FileNotFound);
            return OkResult(ApiWatchStateDto.From(state));
        }

        /// <summary>Forgets the progress of a file entirely.</summary>
        [HttpDelete("watch/{channelId:long}/{fileId}")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> DeleteWatch(long channelId, string fileId)
        {
            await _library.DeleteWatchAsync(channelId, fileId);
            return OkEmpty("Progress removed");
        }

        /// <summary>Marks a file as watched.</summary>
        [HttpPost("watch/{channelId:long}/{fileId}/watched")]
        [ProducesResponseType(typeof(ApiResult<ApiWatchStateDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> MarkWatched(long channelId, string fileId)
        {
            var state = await _library.SetWatchedAsync(channelId, fileId, true);
            if (state == null) return NotFoundResult("File not found", ApiErrorCodes.FileNotFound);
            return OkResult(ApiWatchStateDto.From(state), "Marked as watched");
        }

        /// <summary>Marks a file as not watched and resets its position.</summary>
        [HttpDelete("watch/{channelId:long}/{fileId}/watched")]
        [ProducesResponseType(typeof(ApiResult<ApiWatchStateDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> MarkUnwatched(long channelId, string fileId)
        {
            var state = await _library.SetWatchedAsync(channelId, fileId, false);
            if (state == null) return NotFoundResult("File not found", ApiErrorCodes.FileNotFound);
            return OkResult(ApiWatchStateDto.From(state), "Marked as not watched");
        }

        /// <summary>Marks a whole movie or series (or one season) as watched.</summary>
        [HttpPost("items/{id}/watched")]
        [ProducesResponseType(typeof(ApiResult<ApiLibraryItemDetailDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> MarkItemWatched(string id, [FromBody] LibraryItemWatchedRequest? request)
        {
            var item = await _db.GetItem(id);
            if (item == null) return NotFoundResult("Item not found");
            await _library.SetItemWatchedAsync(item, request?.Season, true);
            return OkResult(await _library.GetItemDetailAsync(id, BaseUrl), "Marked as watched");
        }

        /// <summary>Marks a whole movie or series (or one season) as not watched.</summary>
        [HttpDelete("items/{id}/watched")]
        [ProducesResponseType(typeof(ApiResult<ApiLibraryItemDetailDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> MarkItemUnwatched(string id, [FromQuery] int? season = null)
        {
            var item = await _db.GetItem(id);
            if (item == null) return NotFoundResult("Item not found");
            await _library.SetItemWatchedAsync(item, season, false);
            return OkResult(await _library.GetItemDetailAsync(id, BaseUrl), "Marked as not watched");
        }

        /// <summary>Files watched to the end, most recent first.</summary>
        [HttpGet("watch/history")]
        [ProducesResponseType(typeof(ApiResult<List<ApiLibraryFileDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> History([FromQuery] int limit = 100, [FromQuery] bool includeHidden = false) =>
            OkResult(await _library.HistoryAsync(BaseUrl, Math.Clamp(limit, 1, 500), includeHidden));

        #endregion
    }
}
