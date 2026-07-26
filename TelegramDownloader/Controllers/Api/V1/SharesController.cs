using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Sharing a folder of a channel with another TelegramFileManager instance,
    /// and importing what somebody else shared.
    ///
    /// A share is a portable description of the files (names, sizes, Telegram
    /// message ids) plus an invitation to the channel that holds them. The
    /// bytes stay in Telegram: importing a share only rebuilds the index and,
    /// when needed, joins the channel.
    /// </summary>
    [Route("api/v1/shares")]
    [Tags("Shares")]
    [RequireTelegramSession]
    public class SharesController : ApiV1ControllerBase
    {
        private readonly IFileService _files;
        private readonly IDbService _db;
        private readonly ITelegramService _telegram;
        private readonly ILogger<SharesController> _logger;

        public SharesController(
            IFileService files,
            IDbService db,
            ITelegramService telegram,
            ILogger<SharesController> logger)
        {
            _files = files;
            _db = db;
            _telegram = telegram;
            _logger = logger;
        }

        /// <summary>Lists the shared collections stored on this server.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResult<List<SharedCollectionDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] string? filter, [FromQuery] PagedQuery? query = null)
        {
            query ??= new PagedQuery();
            try
            {
                var list = await _db.getSharedInfoList(filter: filter) ?? new List<BsonSharedInfoModel>();
                var items = list.Select(ToDto).OrderByDescending(s => s.DateModified).ToList();
                var (page, info) = Paginate(items, query);
                return OkPaged(page, info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing shared collections");
                return ErrorResult("Could not list the shared collections", ex);
            }
        }

        /// <summary>Details of one shared collection.</summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResult<SharedCollectionDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                var info = await _files.GetSharedInfoById(id);
                if (info == null)
                    return NotFoundResult("Shared collection not found");
                return OkResult(ToDto(info));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading shared collection {Id}", id);
                return ErrorResult("Could not read the shared collection", ex);
            }
        }

        /// <summary>Builds a share payload for a channel folder.</summary>
        /// <remarks>
        /// The returned document is what another instance passes to
        /// <c>POST /api/v1/shares/import</c>. It contains the file descriptors
        /// and, when available, an invitation link to the channel, so the
        /// receiving account can join and read the files.
        /// </remarks>
        /// <param name="channelId">Channel that holds the files.</param>
        /// <param name="folderId">Folder to share. Omit to share the whole channel.</param>
        /// <param name="name">Label for the share.</param>
        [HttpGet("export")]
        [ProducesResponseType(typeof(ApiResult<ShareFilesModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Export(
            [FromQuery] string channelId,
            [FromQuery] string? folderId,
            [FromQuery] string? name)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return BadRequestResult("A channel id is required");

            try
            {
                var share = new ShareFilesModel
                {
                    id = channelId,
                    name = name,
                    fileName = name,
                    files = await _files.ShareFile(channelId, folderId)
                };

                try
                {
                    share.chatName = _telegram.getChatName(Convert.ToInt64(channelId));
                    share.invitation = await _telegram.getInvitationHash(Convert.ToInt64(channelId));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not attach an invitation to the share of channel {ChannelId}", channelId);
                }

                return OkResult(share);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting a share of channel {ChannelId}", channelId);
                return ErrorResult("Could not export the share", ex);
            }
        }

        /// <summary>Imports a share published by another instance.</summary>
        /// <remarks>
        /// The account joins the channel when it is not a member yet and the
        /// share carries an invitation hash. Import runs in the background; the
        /// imported files then appear under the shared collections and can be
        /// downloaded with <c>POST /api/v1/transfers/downloads</c> using
        /// <c>sharedCollectionId</c>.
        /// </remarks>
        [HttpPost("import")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status202Accepted)]
        public IActionResult Import([FromBody] ImportSharedRequest request)
        {
            if (request?.Share == null || string.IsNullOrWhiteSpace(request.Share.id))
                return BadRequestResult("A share payload with a channel id is required");

            var progress = new GenericNotificationProgressModel();
            _ = Task.Run(async () =>
            {
                try
                {
                    await _files.importSharedData(request.Share, progress);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background import of a share failed");
                }
            });

            return Accepted(ApiResult.Done("Share import started"));
        }

        /// <summary>Deletes a shared collection from this server.</summary>
        [HttpDelete("{id}")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var info = await _files.GetSharedInfoById(id);
                if (info == null)
                    return NotFoundResult("Shared collection not found");

                await _files.DeleteShared(id, info.CollectionId);
                return OkEmpty("Shared collection deleted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting shared collection {Id}", id);
                return ErrorResult("Could not delete the shared collection", ex);
            }
        }

        /// <summary>Exports a channel folder as Emby/Kodi <c>.strm</c> files.</summary>
        /// <remarks>
        /// Each <c>.strm</c> holds a URL that streams the file straight from
        /// Telegram, so a media server can present the whole library without
        /// storing anything. The URL flavour depends on
        /// <c>strmStreamingMode</c> in the configuration.
        ///
        /// With <c>destinationFolder</c> the files are written under the server
        /// local root; without it, the response carries a relative URL to a zip
        /// archive.
        /// </remarks>
        [HttpPost("strm")]
        [ProducesResponseType(typeof(ApiResult<string>), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateStrm([FromQuery] string channelId, [FromBody] CreateStrmRequest request)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return BadRequestResult("A channel id is required");

            request ??= new CreateStrmRequest();
            var host = string.IsNullOrWhiteSpace(request.Host) ? BaseUrl : request.Host;
            var path = string.IsNullOrWhiteSpace(request.Path) ? "/" : request.Path;

            try
            {
                if (!string.IsNullOrWhiteSpace(request.DestinationFolder))
                {
                    await _files.CreateStrmFilesToLocal(path, channelId, host, request.DestinationFolder);
                    return OkResult(request.DestinationFolder, "STRM files written to the local storage");
                }

                var result = await _files.CreateStrmFiles(path, channelId, host);
                return OkResult(result, "STRM archive created");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating STRM files for channel {ChannelId}", channelId);
                return ErrorResult("Could not create the STRM files", ex);
            }
        }

        private static SharedCollectionDto ToDto(BsonSharedInfoModel m) => new()
        {
            Id = m.Id,
            Name = m.Name,
            Description = m.Description,
            ChannelId = m.ChannelId,
            CollectionId = m.CollectionId,
            DateCreated = m.DateCreated,
            DateModified = m.DateModified
        };
    }
}
