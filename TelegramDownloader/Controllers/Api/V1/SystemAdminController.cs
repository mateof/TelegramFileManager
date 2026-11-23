using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;
using TelegramDownloader.Services.GitHub;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Maintenance actions of the settings page: restart, image caches,
    /// orphaned favourites, FFmpeg and version checks.
    /// </summary>
    [Route("api/v1/system")]
    [Tags("System")]
    public class SystemAdminController : ApiV1ControllerBase
    {
        /// <summary>Folder of the channel avatar cache (same as <c>/api/channel/image</c>).</summary>
        public static readonly string ChannelImageCacheFolder = Path.Combine(Path.GetTempPath(), "TFM_ChannelImages");

        private readonly ITelegramService _telegram;
        private readonly IDbService _db;
        private readonly ILogger<SystemAdminController> _logger;

        public SystemAdminController(ITelegramService telegram, IDbService db, ILogger<SystemAdminController> logger)
        {
            _telegram = telegram;
            _db = db;
            _logger = logger;
        }

        /// <summary>Restarts the application.</summary>
        /// <remarks>
        /// Stops the process gracefully about a second after answering
        /// <c>202</c>. It only comes back when something restarts it: the
        /// Docker restart policy (<c>restart: unless-stopped</c> in the
        /// official compose file), systemd, a Windows service... Run by hand,
        /// the server simply stops. Poll <c>GET /api/v1/system/ping</c> to know
        /// when it is back.
        /// </remarks>
        [HttpPost("restart")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status202Accepted)]
        public IActionResult Restart([FromServices] IHostApplicationLifetime lifetime)
        {
            _logger.LogWarning("Application restart requested through the API from {IP}", HttpContext.Connection.RemoteIpAddress);
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                lifetime.StopApplication();
            });
            return Accepted(ApiResult.Done("The application is restarting"));
        }

        /// <summary>Size of the image caches (channel avatars and message thumbnails).</summary>
        [HttpGet("image-cache")]
        [ProducesResponseType(typeof(ApiResult<ImageCacheInfoDto>), StatusCodes.Status200OK)]
        public IActionResult ImageCache() => OkResult(ReadImageCache());

        /// <summary>Empties the image caches. Images are downloaded again when next shown.</summary>
        [HttpDelete("image-cache")]
        [ProducesResponseType(typeof(ApiResult<ImageCacheInfoDto>), StatusCodes.Status200OK)]
        public IActionResult ClearImageCache()
        {
            var deleted = 0;
            foreach (var (folder, pattern) in CacheFolders())
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder, pattern))
                {
                    try
                    {
                        System.IO.File.Delete(file);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not delete cached image {File}", file);
                    }
                }
            }
            return OkResult(ReadImageCache(), $"{deleted} cached images deleted");
        }

        /// <summary>Favourite channels the account can no longer reach (left or deleted).</summary>
        [HttpGet("orphaned-favorites")]
        [RequireTelegramSession]
        [ProducesResponseType(typeof(ApiResult<List<long>>), StatusCodes.Status200OK)]
        public IActionResult OrphanedFavorites() => OkResult(FindOrphanedFavorites());

        /// <summary>Removes the orphaned favourite channels and returns the ids removed.</summary>
        [HttpPost("orphaned-favorites")]
        [RequireTelegramSession]
        [ProducesResponseType(typeof(ApiResult<List<long>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> CleanOrphanedFavorites()
        {
            try
            {
                var orphaned = FindOrphanedFavorites();
                foreach (var id in orphaned)
                    GeneralConfigStatic.DeleteFavouriteChannel(id);
                if (orphaned.Count > 0)
                    await GeneralConfigStatic.SaveChanges(_db, GeneralConfigStatic.config);
                return OkResult(orphaned, $"{orphaned.Count} orphaned favourites removed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning the orphaned favourites");
                return ErrorResult("Could not clean the orphaned favourites", ex);
            }
        }

        /// <summary>Whether FFmpeg is installed (needed for video transcoding) and its version.</summary>
        /// <param name="refresh">Probe again instead of using the 5-minute cached answer.</param>
        [HttpGet("ffmpeg")]
        [ProducesResponseType(typeof(ApiResult<FfmpegInfoDto>), StatusCodes.Status200OK)]
        public IActionResult Ffmpeg([FromQuery] bool refresh = false)
        {
            var (available, version) = FfmpegProbe.Probe(refresh);
            return OkResult(new FfmpegInfoDto
            {
                Available = available,
                Version = version,
                TranscodingEnabled = GeneralConfigStatic.config?.EnableVideoTranscoding ?? false
            });
        }

        /// <summary>Current version and the latest GitHub release / dev build.</summary>
        /// <remarks>
        /// Uses the same check as the web UI, cached for 30 minutes
        /// (<c>refresh=true</c> forces a new one). When GitHub cannot be reached
        /// the latest versions are null.
        /// </remarks>
        [HttpGet("version")]
        [ProducesResponseType(typeof(ApiResult<VersionCheckDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Version([FromServices] GHService github, [FromQuery] bool refresh = false)
        {
            var info = await github.GetVersionInfo(refresh);
            return OkResult(new VersionCheckDto
            {
                CurrentVersion = info.CurrentVersion,
                IsDevVersion = info.IsDevVersion,
                LatestRelease = ReleaseDto.From(info.LatestRelease),
                LatestDev = ReleaseDto.From(info.LatestDev),
                HasNewRelease = info.HasNewRelease,
                HasNewDev = info.HasNewDev
            });
        }

        private List<long> FindOrphanedFavorites()
        {
            var result = new List<long>();
            foreach (var id in (GeneralConfigStatic.config?.FavouriteChannels ?? new List<long>()).ToList())
            {
                try
                {
                    var (_, exists) = _telegram.GetChannelInfo(id);
                    if (!exists) result.Add(id);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not check favourite channel {Id}", id);
                }
            }
            return result;
        }

        private static IEnumerable<(string Folder, string Pattern)> CacheFolders()
        {
            yield return (ChannelImageCacheFolder, "*.jpg");
            yield return (MediaController.ThumbnailCacheFolder, "*");
        }

        private static ImageCacheInfoDto ReadImageCache()
        {
            var dto = new ImageCacheInfoDto();
            foreach (var (folder, pattern) in CacheFolders())
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder, pattern))
                {
                    try
                    {
                        var length = new FileInfo(file).Length;
                        if (folder == ChannelImageCacheFolder) dto.ChannelImages++;
                        else if (file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) dto.Thumbnails++;
                        dto.SizeInBytes += length;
                    }
                    catch { }
                }
            }
            dto.SizeText = HelperService.SizeSuffix(dto.SizeInBytes);
            return dto;
        }
    }

    /// <summary>Image cache usage.</summary>
    public class ImageCacheInfoDto
    {
        public int ChannelImages { get; set; }
        public int Thumbnails { get; set; }
        public long SizeInBytes { get; set; }
        public string SizeText { get; set; } = "0 B";
    }

    /// <summary>FFmpeg availability.</summary>
    public class FfmpegInfoDto
    {
        public bool Available { get; set; }

        /// <summary>e.g. <c>6.1.1</c>; null when FFmpeg is missing.</summary>
        public string? Version { get; set; }

        /// <summary>Current value of <c>enableVideoTranscoding</c>.</summary>
        public bool TranscodingEnabled { get; set; }
    }

    /// <summary>Result of <c>GET /api/v1/system/version</c>.</summary>
    public class VersionCheckDto
    {
        public string CurrentVersion { get; set; } = string.Empty;
        public bool IsDevVersion { get; set; }
        public ReleaseDto? LatestRelease { get; set; }
        public ReleaseDto? LatestDev { get; set; }
        public bool HasNewRelease { get; set; }
        public bool HasNewDev { get; set; }
    }

    /// <summary>A GitHub release.</summary>
    public class ReleaseDto
    {
        public string Version { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Url { get; set; }
        public DateTime PublishedAt { get; set; }
        public bool Prerelease { get; set; }

        /// <summary>Release notes (Markdown).</summary>
        public string? Notes { get; set; }

        public static ReleaseDto? From(Models.GitHub.GithubVersionModel? m) => m == null ? null : new ReleaseDto
        {
            Version = m.Version,
            Name = m.name,
            Url = m.html_url,
            PublishedAt = m.published_at,
            Prerelease = m.prerelease,
            Notes = m.body
        };
    }
}
