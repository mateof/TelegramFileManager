using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Services;
using static TelegramDownloader.Models.GeneralConfigStatic;

namespace TelegramDownloader.Controllers
{
    [Route("api/video")]
    [ApiController]
    public class VideoStreamController : ControllerBase
    {
        private readonly ITelegramService _ts;
        private readonly IFileService _fs;
        private readonly ILogger<VideoStreamController> _logger;
        private readonly TransactionInfoService _tis;

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

        public VideoStreamController(ITelegramService ts, IFileService fs, ILogger<VideoStreamController> logger, TransactionInfoService tis)
        {
            _ts = ts;
            _fs = fs;
            _logger = logger;
            _tis = tis;
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
        /// Stream video with transcoding support for non-browser formats
        /// </summary>
        [HttpGet("stream/{idChannel}/{idFile}/{name}")]
        public async Task<IActionResult> StreamVideo(string idChannel, string idFile, string name)
        {
            var extension = Path.GetExtension(name).ToLowerInvariant();

            // If format is natively supported, redirect to regular file stream
            if (NativeFormats.Contains(extension))
            {
                return Redirect($"/api/File/GetFileStream/{idChannel}/{idFile}/{name}");
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
                return Redirect($"/api/File/GetFileStream/{idChannel}/{idFile}/{name}");
            }

            try
            {
                var file = await _fs.getItemById(idChannel, idFile);
                if (file == null)
                {
                    return NotFound("File not found");
                }

                // Get the source file path (download first if needed)
                string sourcePath = await EnsureFileDownloaded(idChannel, file, name);
                if (string.IsNullOrEmpty(sourcePath))
                {
                    return StatusCode(500, "Failed to prepare source file");
                }

                // Set response headers for streaming
                Response.ContentType = "video/mp4";
                Response.Headers["Accept-Ranges"] = "none"; // Transcoded streams don't support range requests well
                Response.Headers["Cache-Control"] = "no-cache";

                _logger.LogInformation("Starting FFmpeg transcoding for {FileName}", name);

                // Start FFmpeg transcoding process
                await TranscodeAndStream(sourcePath, Response.Body, HttpContext.RequestAborted);

                return new EmptyResult();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Client closed connection during video transcode - File: {FileName}", name);
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error transcoding video {FileName}", name);
                return StatusCode(500, "Transcoding failed");
            }
        }

        /// <summary>
        /// Get video info (duration, codec, etc.)
        /// </summary>
        [HttpGet("info/{idChannel}/{idFile}/{name}")]
        public async Task<IActionResult> GetVideoInfo(string idChannel, string idFile, string name)
        {
            try
            {
                var file = await _fs.getItemById(idChannel, idFile);
                if (file == null)
                {
                    return NotFound();
                }

                var extension = Path.GetExtension(name).ToLowerInvariant();
                var needsTranscode = TranscodingRequiredFormats.Contains(extension);
                var ffmpegAvailable = IsFFmpegAvailable();

                return Ok(new
                {
                    fileName = name,
                    extension,
                    needsTranscode,
                    ffmpegAvailable,
                    canPlay = !needsTranscode || ffmpegAvailable,
                    streamUrl = needsTranscode && ffmpegAvailable
                        ? $"/api/video/stream/{idChannel}/{idFile}/{name}"
                        : $"/api/File/GetFileStream/{idChannel}/{idFile}/{name}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting video info");
                return StatusCode(500, ex.Message);
            }
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

        private async Task<string> EnsureFileDownloaded(string idChannel, BsonFileManagerModel file, string name)
        {
            string tempFileName = $"{idChannel}-{(file.MessageId != null ? file.MessageId.ToString() : file.Id)}-{name}";
            string tempPath = Path.Combine(FileService.TEMPDIR, "_temp", tempFileName);

            // Check if file already exists and is complete
            if (System.IO.File.Exists(tempPath))
            {
                var fileInfo = new FileInfo(tempPath);
                if (fileInfo.Length >= file.Size)
                {
                    return tempPath;
                }
            }

            // Download the file first
            try
            {
                var message = await _ts.getMessageFile(idChannel, file.MessageId ?? file.ListMessageId.FirstOrDefault());
                if (message == null)
                {
                    return null;
                }

                ChatMessages cm = new ChatMessages { message = message };

                Directory.CreateDirectory(Path.GetDirectoryName(tempPath));

                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    var dm = new DownloadModel
                    {
                        tis = _tis,
                        startDate = DateTime.Now,
                        path = tempPath,
                        name = name,
                        _size = file.Size,
                        channelName = _ts.getChatName(Convert.ToInt64(idChannel))
                    };
                    _tis.addToDownloadList(dm);
                    await _ts.DownloadFileAndReturn(cm, fs, model: dm);
                }

                return tempPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download file for transcoding");
                return null;
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
