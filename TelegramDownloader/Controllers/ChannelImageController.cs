using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Models;

namespace TelegramDownloader.Controllers
{
    [Route("api/channel")]
    [ApiController]
    public class ChannelImageController : ControllerBase
    {
        private readonly ITelegramService _ts;
        private readonly ILogger<ChannelImageController> _logger;
        private static readonly string CacheFolder = Path.Combine(Path.GetTempPath(), "TFM_ChannelImages");
        private static readonly SemaphoreSlim _downloadLock = new SemaphoreSlim(1, 1);

        public ChannelImageController(ITelegramService ts, ILogger<ChannelImageController> logger)
        {
            _ts = ts;
            _logger = logger;

            // Ensure cache folder exists
            if (!Directory.Exists(CacheFolder))
            {
                Directory.CreateDirectory(CacheFolder);
            }
        }

        [HttpGet("image/{channelId}")]
        [ResponseCache(Duration = 86400)] // Cache for 24 hours
        public async Task<IActionResult> GetChannelImage(long channelId)
        {
            try
            {
                var cachedPath = Path.Combine(CacheFolder, $"{channelId}.jpg");

                // Check if image is cached
                if (System.IO.File.Exists(cachedPath))
                {
                    var fileBytes = await System.IO.File.ReadAllBytesAsync(cachedPath);
                    return File(fileBytes, "image/jpeg");
                }

                // Download and cache the image
                await _downloadLock.WaitAsync();
                try
                {
                    // Double-check after acquiring lock
                    if (System.IO.File.Exists(cachedPath))
                    {
                        var fileBytes = await System.IO.File.ReadAllBytesAsync(cachedPath);
                        return File(fileBytes, "image/jpeg");
                    }

                    var imageBytes = await _ts.DownloadChannelPhoto(channelId);
                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        await System.IO.File.WriteAllBytesAsync(cachedPath, imageBytes);
                        return File(imageBytes, "image/jpeg");
                    }
                }
                finally
                {
                    _downloadLock.Release();
                }

                // Return 404 if no image
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting channel image for {ChannelId}", channelId);
                return NotFound();
            }
        }

        [HttpDelete("image/cache")]
        public IActionResult ClearCache()
        {
            try
            {
                if (Directory.Exists(CacheFolder))
                {
                    var files = Directory.GetFiles(CacheFolder, "*.jpg");
                    foreach (var file in files)
                    {
                        System.IO.File.Delete(file);
                    }
                }
                return Ok(new { message = "Cache cleared" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing image cache");
                return StatusCode(500, new { error = "Failed to clear cache" });
            }
        }
    }
}
