using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TL;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Telegram chats, channels and groups: discovery, favourites, folders,
    /// creation and deletion, message history and index refresh.
    ///
    /// A "channel" in this API is any Telegram peer the account can see. When
    /// the app indexes a channel it creates a MongoDB database named after the
    /// channel id; that database is what the <c>files</c> endpoints browse.
    /// </summary>
    [Route("api/v1/channels")]
    [Tags("Channels")]
    [RequireTelegramSession]
    public class ChannelsController : ApiV1ControllerBase
    {
        private readonly ITelegramService _telegram;
        private readonly IFileService _files;
        private readonly IDbService _db;
        private readonly ILogger<ChannelsController> _logger;

        public ChannelsController(
            ITelegramService telegram,
            IFileService files,
            IDbService db,
            ILogger<ChannelsController> logger)
        {
            _telegram = telegram;
            _files = files;
            _db = db;
            _logger = logger;
        }

        /// <summary>Lists the chats the signed-in account can access.</summary>
        /// <remarks>
        /// Set <paramref name="onlySaved"/> to list only the channels that
        /// already have a local file index, which is what the file manager
        /// navigates. Sorting accepts <c>name</c> (default) and <c>id</c>.
        /// </remarks>
        /// <param name="query">Paging and sorting.</param>
        /// <param name="onlySaved">Only channels with a local index.</param>
        /// <param name="favoritesOnly">Only channels marked as favourite.</param>
        /// <param name="search">Case-insensitive substring match on the name.</param>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResult<List<ApiChannelDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(
            [FromQuery] PagedQuery query,
            [FromQuery] bool onlySaved = false,
            [FromQuery] bool favoritesOnly = false,
            [FromQuery] string? search = null)
        {
            try
            {
                var chats = onlySaved
                    ? await _telegram.getAllSavedChats()
                    : await _telegram.getAllChats();

                var favourites = GeneralConfigStatic.config.FavouriteChannels ?? new List<long>();
                var items = (chats ?? new List<ChatViewBase>())
                    .Where(c => c?.chat != null)
                    .Select(c => ApiChannelDto.FromChatViewBase(
                        c,
                        isFavorite: favourites.Contains(c.chat.ID),
                        isOwner: SafeIsOwner(c.chat.ID)))
                    .ToList();

                if (favoritesOnly)
                    items = items.Where(c => c.IsFavorite).ToList();

                if (!string.IsNullOrWhiteSpace(search))
                    items = items.Where(c => c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

                items = (query.SortBy?.ToLowerInvariant(), query.SortDescending) switch
                {
                    ("id", true) => items.OrderByDescending(c => c.Id).ToList(),
                    ("id", false) => items.OrderBy(c => c.Id).ToList(),
                    (_, true) => items.OrderByDescending(c => c.Name).ToList(),
                    _ => items.OrderBy(c => c.Name).ToList()
                };

                var (page, info) = Paginate(items, query);
                return OkPaged(page, info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing channels");
                return ErrorResult("Could not list the channels", ex);
            }
        }

        /// <summary>Lists chats grouped by their Telegram folder (chat filter).</summary>
        [HttpGet("folders")]
        [ProducesResponseType(typeof(ApiResult<ApiChannelsWithFoldersDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Folders()
        {
            try
            {
                var data = await _telegram.getChatsWithFolders();
                var favourites = GeneralConfigStatic.config.FavouriteChannels ?? new List<long>();

                var dto = new ApiChannelsWithFoldersDto
                {
                    Folders = (data?.Folders ?? new List<ChatFolderView>()).Select(f => new ApiChannelFolderDto
                    {
                        Id = f.Id,
                        Title = f.Title,
                        IconEmoji = f.IconEmoji,
                        Channels = (f.Chats ?? new List<ChatViewBase>())
                            .Where(c => c?.chat != null)
                            .Select(c => ApiChannelDto.FromChatViewBase(c, favourites.Contains(c.chat.ID), SafeIsOwner(c.chat.ID)))
                            .ToList()
                    }).ToList(),
                    Ungrouped = (data?.UngroupedChats ?? new List<ChatViewBase>())
                        .Where(c => c?.chat != null)
                        .Select(c => ApiChannelDto.FromChatViewBase(c, favourites.Contains(c.chat.ID), SafeIsOwner(c.chat.ID)))
                        .ToList()
                };
                dto.TotalChannels = dto.Folders.Sum(f => f.ChannelCount) + dto.Ungrouped.Count;

                return OkResult(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing channel folders");
                return ErrorResult("Could not list the channel folders", ex);
            }
        }

        /// <summary>Lists the favourite channels.</summary>
        [HttpGet("favorites")]
        [ProducesResponseType(typeof(ApiResult<List<ApiChannelDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Favorites([FromQuery] bool refresh = true)
        {
            try
            {
                var chats = await _telegram.GetFouriteChannels(refresh);
                var items = (chats ?? new List<ChatViewBase>())
                    .Where(c => c?.chat != null)
                    .Select(c => ApiChannelDto.FromChatViewBase(c, isFavorite: true, isOwner: SafeIsOwner(c.chat.ID)))
                    .OrderBy(c => c.Name)
                    .ToList();
                return OkResult(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing favourite channels");
                return ErrorResult("Could not list the favourite channels", ex);
            }
        }

        /// <summary>Marks a channel as favourite.</summary>
        [HttpPost("{id}/favorite")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> AddFavorite(long id)
        {
            try
            {
                await _telegram.AddFavouriteChannel(id);
                return OkEmpty("Channel added to favourites");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding channel {Id} to favourites", id);
                return ErrorResult("Could not add the channel to favourites", ex);
            }
        }

        /// <summary>Removes a channel from the favourites.</summary>
        [HttpDelete("{id}/favorite")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> RemoveFavorite(long id)
        {
            try
            {
                await _telegram.RemoveFavouriteChannel(id);
                return OkEmpty("Channel removed from favourites");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing channel {Id} from favourites", id);
                return ErrorResult("Could not remove the channel from favourites", ex);
            }
        }

        /// <summary>Details and indexed-content statistics of one channel.</summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResult<ApiChannelDetailDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(string id)
        {
            if (!long.TryParse(id, out var channelId))
                return BadRequestResult("The channel id must be numeric");

            try
            {
                var (name, exists) = _telegram.GetChannelInfo(channelId);
                if (!exists && name == null)
                    return NotFoundResult("Channel not found", ApiErrorCodes.ChannelNotFound);

                var isOwner = SafeIsOwner(channelId);
                var dto = new ApiChannelDetailDto
                {
                    Id = channelId,
                    Name = name ?? channelId.ToString(),
                    IsOwner = isOwner,
                    IsFavorite = (GeneralConfigStatic.config.FavouriteChannels ?? new List<long>()).Contains(channelId),
                    ImageUrl = $"/api/channel/image/{channelId}",
                    IsRefreshing = _files.isChannelRefreshing(id),
                    CanRefresh = !_telegram.isMyChat(channelId) || GeneralConfigStatic.config.EnableRefreshOwnChannels
                };

                try
                {
                    var all = await _db.getAllDatabaseData(id);
                    dto.HasDatabase = all != null;
                    if (all != null)
                    {
                        var files = all.Where(f => f.IsFile).ToList();
                        dto.FileCount = files.Count;
                        dto.FolderCount = all.Count - files.Count;
                        dto.TotalSize = files.Sum(f => f.Size);
                        dto.TotalSizeText = Services.HelperService.SizeSuffix(dto.TotalSize);
                        dto.AudioCount = files.Count(f => ApiFileDto.CategoryOf(f.Type) == "Audio");
                        dto.VideoCount = files.Count(f => ApiFileDto.CategoryOf(f.Type) == "Video");
                        dto.PhotoCount = files.Count(f => ApiFileDto.CategoryOf(f.Type) == "Photo");
                        dto.DocumentCount = files.Count(f => ApiFileDto.CategoryOf(f.Type) == "Document");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Channel {Id} has no local index yet", id);
                    dto.HasDatabase = false;
                }

                return OkResult(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading channel {Id}", id);
                return ErrorResult("Could not read the channel", ex);
            }
        }

        /// <summary>Creates a Telegram channel owned by the signed-in account.</summary>
        /// <remarks>
        /// With <c>createDatabase: true</c> (the default) the local file index is
        /// created at the same time, so the channel can be used as a storage
        /// target immediately.
        /// </remarks>
        [HttpPost]
        [ProducesResponseType(typeof(ApiResult<ApiChannelDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] CreateChannelRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Title))
                return BadRequestResult("A channel title is required");

            try
            {
                var channel = await _telegram.CreateChannel(request.Title.Trim(), request.About ?? string.Empty);
                if (channel == null)
                    return ErrorResult("Telegram did not return the created channel");

                if (request.CreateDatabase)
                    await _files.CreateDatabase(channel.ID.ToString());

                var dto = new ApiChannelDto
                {
                    Id = channel.ID,
                    Name = channel.title,
                    Type = channel.IsGroup ? "group" : "channel",
                    IsOwner = true,
                    ImageUrl = $"/api/channel/image/{channel.ID}",
                    HasDatabase = request.CreateDatabase
                };

                return StatusCode(StatusCodes.Status201Created, ApiResult<ApiChannelDto>.Ok(dto, "Channel created"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating channel {Title}", request.Title);
                return ErrorResult("Could not create the channel", ex);
            }
        }

        /// <summary>Creates the local file index (MongoDB database) for a channel.</summary>
        [HttpPost("{id}/database")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateDatabase(string id)
        {
            try
            {
                await _files.CreateDatabase(id);
                return OkEmpty("Channel database created");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating database for channel {Id}", id);
                return ErrorResult("Could not create the channel database", ex);
            }
        }

        /// <summary>Drops the local file index of a channel.</summary>
        /// <remarks>
        /// Only the local index is removed: the files stay in Telegram, but the
        /// app forgets the folder structure until the channel is refreshed again.
        /// </remarks>
        [HttpDelete("{id}/database")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> DeleteDatabase(string id)
        {
            try
            {
                await _db.deleteDatabase(id);
                return OkEmpty("Channel database deleted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting database for channel {Id}", id);
                return ErrorResult("Could not delete the channel database", ex);
            }
        }

        /// <summary>Leaves a channel, and optionally deletes it.</summary>
        /// <remarks>
        /// With <c>deleteOnTelegram: true</c> the channel is deleted for every
        /// member, which only works when the account owns it. This is
        /// irreversible and also destroys the files stored inside.
        /// </remarks>
        [HttpPost("{id}/leave")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Leave(long id, [FromBody] ChannelDeleteRequest? request)
        {
            request ??= new ChannelDeleteRequest();
            try
            {
                if (request.DeleteOnTelegram)
                {
                    if (!_telegram.isChannelOwner(id))
                        return ForbiddenResult("Only the channel owner can delete it");
                    await _telegram.DeleteChannel(id);
                }
                else
                {
                    await _telegram.LeaveChannel(id);
                }

                if (request.DeleteLocalDatabase)
                {
                    try { await _db.deleteDatabase(id.ToString()); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not drop the local database of channel {Id}", id); }
                }

                return OkEmpty(request.DeleteOnTelegram ? "Channel deleted" : "Channel left");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving/deleting channel {Id}", id);
                return ErrorResult("Could not leave or delete the channel", ex);
            }
        }

        /// <summary>Scans the channel on Telegram and indexes new files.</summary>
        /// <remarks>
        /// The scan runs in the background and can take minutes on large
        /// channels. Poll <c>GET /api/v1/channels/{id}/refresh</c> for the state,
        /// and watch the <c>transfers</c> hub for the resulting activity. Only
        /// files that are not indexed yet are added, so calling this repeatedly
        /// is safe.
        /// </remarks>
        [HttpPost("{id}/refresh")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status409Conflict)]
        public IActionResult Refresh(string id, [FromBody] RefreshChannelRequest? request)
        {
            request ??= new RefreshChannelRequest();
            if (!request.ToOptions().HasAnySelection)
                return BadRequestResult("Select at least one media type to fetch");

            if (_files.isChannelRefreshing(id))
                return ConflictResult("This channel is already being refreshed", ApiErrorCodes.AlreadyRunning);

            // Fire and forget: the scan is long running and reports through the
            // notification/transfer pipeline.
            _ = Task.Run(async () =>
            {
                try
                {
                    await _files.refreshChannelFIles(id, request.Force, request.ToOptions());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background refresh of channel {Id} failed", id);
                }
            });

            return Accepted(ApiResult.Done("Channel refresh started"));
        }

        /// <summary>Tells whether a background refresh is running for a channel.</summary>
        [HttpGet("{id}/refresh")]
        [ProducesResponseType(typeof(ApiResult<bool>), StatusCodes.Status200OK)]
        public IActionResult RefreshStatus(string id) => OkResult(_files.isChannelRefreshing(id));

        /// <summary>Reads the recent message history of a chat.</summary>
        /// <remarks>
        /// This hits Telegram directly and does not use the local index, so it
        /// also works for channels that have never been indexed.
        /// </remarks>
        /// <param name="id">Chat id.</param>
        /// <param name="limit">Messages to return (1-100).</param>
        /// <param name="offset">Messages to skip from the newest one.</param>
        /// <param name="onlyMedia">Return only messages carrying a file.</param>
        [HttpGet("{id}/messages")]
        [ProducesResponseType(typeof(ApiResult<List<ApiChatMessageDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Messages(
            long id,
            [FromQuery] int limit = 30,
            [FromQuery] int offset = 0,
            [FromQuery] bool onlyMedia = false)
        {
            if (limit < 1) limit = 1;
            if (limit > 100) limit = 100;

            try
            {
                var messages = await _telegram.getChatHistory(id, limit, offset);
                var items = (messages ?? new List<ChatMessages>())
                    .Where(m => m?.message != null)
                    .Select(ToMessageDto)
                    .Where(m => !onlyMedia || m.HasMedia)
                    .ToList();
                return OkResult(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading messages of chat {Id}", id);
                return ErrorResult("Could not read the chat history", ex);
            }
        }

        /// <summary>Returns the channel avatar as a PNG/JPEG image.</summary>
        [HttpGet("{id}/image")]
        [Produces("image/jpeg", "image/png", "application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Image(long id)
        {
            try
            {
                var bytes = await _telegram.DownloadChannelPhoto(id);
                if (bytes == null || bytes.Length == 0)
                    return NotFoundResult("This channel has no avatar");
                return File(bytes, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not download the avatar of channel {Id}", id);
                return NotFoundResult("This channel has no avatar");
            }
        }

        /// <summary>Returns the invitation link of a channel, generating one if needed.</summary>
        [HttpGet("{id}/invitation")]
        [ProducesResponseType(typeof(ApiResult<InvitationInfo>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Invitation(long id)
        {
            try
            {
                var info = await _telegram.getInvitationHash(id);
                if (info == null)
                    return NotFoundResult("No invitation link is available for this channel");
                return OkResult(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading the invitation of channel {Id}", id);
                return ErrorResult("Could not read the channel invitation", ex);
            }
        }

        /// <summary>Joins a channel using an invitation hash.</summary>
        /// <param name="hash">
        /// The part after <c>t.me/+</c> or <c>joinchat/</c> in the invitation link.
        /// </param>
        [HttpPost("join")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Join([FromQuery] string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return BadRequestResult("An invitation hash is required");

            try
            {
                await _telegram.joinChatInvitationHash(hash);
                return OkEmpty("Joined the channel");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining with hash {Hash}", hash);
                return ErrorResult("Could not join the channel", ex);
            }
        }

        private bool SafeIsOwner(long channelId)
        {
            try { return _telegram.isChannelOwner(channelId); }
            catch { return false; }
        }

        private static ApiChatMessageDto ToMessageDto(ChatMessages m)
        {
            var dto = new ApiChatMessageDto
            {
                Id = m.message.ID,
                Date = m.message.Date,
                Text = m.message.message,
                From = m.user?.ToString()
            };

            switch (m.message.media)
            {
                case MessageMediaPhoto:
                    dto.HasMedia = true;
                    dto.MediaType = "photo";
                    break;
                case MessageMediaDocument { document: Document doc }:
                    dto.HasMedia = true;
                    dto.FileName = doc.Filename;
                    dto.FileSize = doc.size;
                    dto.MimeType = doc.mime_type;
                    dto.MediaType = doc.mime_type switch
                    {
                        not null when doc.mime_type.StartsWith("video") => "video",
                        not null when doc.mime_type.StartsWith("audio") => "audio",
                        not null when doc.mime_type.StartsWith("image") => "photo",
                        _ => "document"
                    };
                    break;
            }

            return dto;
        }
    }
}
