using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using TelegramDownloader.Data;
using TelegramDownloader.Models;
using TelegramDownloader.Services;

namespace TelegramDownloader.Controllers
{
    [Route("api/localvideo")]
    [ApiController]
    public class LocalVideoStreamController : ControllerBase
    {
        private readonly ILogger<LocalVideoStreamController> _logger;

        // Formats that need transcoding (not natively supported by browsers)
        private static readonly HashSet<string> TranscodingRequiredFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".avi", ".wmv", ".flv", ".mov", ".m4v", ".3gp", ".ts", ".mts", ".m2ts", ".vob", ".divx", ".xvid"
        };

        // Formats natively supported by most browsers
        private static readonly HashSet<string> NativeFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".ogg", ".ogv"
        };

        public LocalVideoStreamController(ILogger<LocalVideoStreamController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Check if a video format requires transcoding
        /// </summary>
        [HttpGet("needs-transcode")]
        public IActionResult NeedsTranscode([FromQuery] string fileName)
        {
            var extension = Path.GetExtension(fileName);
            var needsTranscode = TranscodingRequiredFormats.Contains(extension);
            return Ok(new { needsTranscode, extension });
        }

        /// <summary>
        /// Stream local video with transcoding support for non-browser formats
        /// </summary>
        [HttpGet("stream")]
        public async Task<IActionResult> StreamVideo([FromQuery] string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return BadRequest("Path is required");
            }

            // Decode and clean the path
            path = Uri.UnescapeDataString(path);

            // Build the full path from the local directory
            var fullPath = Path.Combine(FileService.LOCALDIR, path.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));

            if (!System.IO.File.Exists(fullPath))
            {
                _logger.LogWarning("File not found: {Path}", fullPath);
                return NotFound("File not found");
            }

            var extension = Path.GetExtension(fullPath).ToLowerInvariant();

            // If format is natively supported, serve directly
            if (NativeFormats.Contains(extension))
            {
                var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, GetMimeType(extension), enableRangeProcessing: true);
            }

            // Check if transcoding is enabled in config
            if (!GeneralConfigStatic.config.EnableVideoTranscoding)
            {
                _logger.LogInformation("Video transcoding is disabled in configuration");
                return StatusCode(403, new {
                    error = "Video transcoding is disabled",
                    message = "This video format requires transcoding. Enable 'Video Transcoding' in Settings to play this file.",
                    format = extension
                });
            }

            // Check if FFmpeg is available
            if (!IsFFmpegAvailable())
            {
                _logger.LogWarning("FFmpeg not available, falling back to direct stream");
                var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, "video/mp4", enableRangeProcessing: true);
            }

            try
            {
                // Set response headers for streaming
                Response.ContentType = "video/mp4";
                Response.Headers["Accept-Ranges"] = "none"; // Transcoded streams don't support range requests well
                Response.Headers["Cache-Control"] = "no-cache";

                _logger.LogInformation("Starting FFmpeg transcoding for local file {FileName}", Path.GetFileName(fullPath));

                // Start FFmpeg transcoding process
                await TranscodeAndStream(fullPath, Response.Body, HttpContext.RequestAborted);

                return new EmptyResult();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Client closed connection during video transcode - File: {FileName}", Path.GetFileName(fullPath));
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error transcoding video {FileName}", Path.GetFileName(fullPath));
                return StatusCode(500, "Transcoding failed");
            }
        }

        /// <summary>
        /// Get video info for a local file
        /// </summary>
        [HttpGet("info")]
        public IActionResult GetVideoInfo([FromQuery] string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return BadRequest("Path is required");
                }

                path = Uri.UnescapeDataString(path);
                var fullPath = Path.Combine(FileService.LOCALDIR, path.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));

                if (!System.IO.File.Exists(fullPath))
                {
                    return NotFound();
                }

                var extension = Path.GetExtension(fullPath).ToLowerInvariant();
                var needsTranscode = TranscodingRequiredFormats.Contains(extension);
                var ffmpegAvailable = IsFFmpegAvailable();

                return Ok(new
                {
                    fileName = Path.GetFileName(fullPath),
                    extension,
                    needsTranscode,
                    ffmpegAvailable,
                    canPlay = !needsTranscode || ffmpegAvailable
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting video info");
                return StatusCode(500, ex.Message);
            }
        }

        private string GetMimeType(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".ogg" or ".ogv" => "video/ogg",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".mkv" => "video/x-matroska",
                ".wmv" => "video/x-ms-wmv",
                ".flv" => "video/x-flv",
                _ => "video/mp4"
            };
        }

        private bool IsFFmpegAvailable()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = "-version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit(5000);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task TranscodeAndStream(string sourcePath, Stream outputStream, CancellationToken cancellationToken)
        {
            // FFmpeg arguments for transcoding to browser-compatible MP4
            // -i: input file
            // -c:v libx264: use H.264 video codec
            // -preset ultrafast: fastest encoding (lower quality but real-time)
            // -crf 23: quality level (18-28 is good, lower = better quality)
            // -c:a aac: use AAC audio codec
            // -b:a 128k: audio bitrate
            // -movflags frag_keyframe+empty_moov+faststart: enable streaming
            // -f mp4: output format
            // pipe:1: output to stdout

            var ffmpegArgs = $"-i \"{sourcePath}\" " +
                            "-c:v libx264 -preset ultrafast -crf 23 " +
                            "-c:a aac -b:a 128k " +
                            "-movflags frag_keyframe+empty_moov+faststart " +
                            "-f mp4 pipe:1";

            _logger.LogDebug("FFmpeg command: ffmpeg {Args}", ffmpegArgs);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = ffmpegArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Log FFmpeg stderr for debugging (async)
            _ = Task.Run(async () =>
            {
                using var reader = process.StandardError;
                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                    {
                        _logger.LogDebug("FFmpeg: {Line}", line);
                    }
                }
            }, cancellationToken);

            try
            {
                // Stream FFmpeg output directly to the response
                await process.StandardOutput.BaseStream.CopyToAsync(outputStream, 64 * 1024, cancellationToken);
                await outputStream.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Transcoding cancelled by client");
            }
            finally
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch { }
                }
            }
        }
    }
}
