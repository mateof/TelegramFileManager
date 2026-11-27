using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TelegramDownloader.Data;
using TelegramDownloader.Models;
using TelegramDownloader.Services.Api;

namespace TelegramDownloader.Services.Audio
{
    /// <summary>
    /// Formats, bitrates and parameter rules of the audio transcoding endpoints
    /// (<c>/api/mobile/stream/.../transcoded</c> and <c>/api/v1/.../transcoded</c>).
    /// Pure: no FFmpeg, no disk.
    /// </summary>
    public static class AudioTranscodeOptions
    {
        public const string DefaultFormat = "mp3";
        public const int DefaultBitrate = 192;
        public const int MinBitrate = 64;
        public const int MaxBitrate = 320;

        /// <summary>Target formats, as accepted by the <c>format</c> parameter.</summary>
        public static readonly IReadOnlyList<string> Formats = new[] { "mp3", "aac" };

        /// <summary>Bitrates a client may offer in a picker (any value in range is accepted).</summary>
        public static readonly IReadOnlyList<int> SuggestedBitrates = new[] { 96, 128, 160, 192, 256, 320 };

        /// <summary>
        /// Lower-cases and checks <paramref name="format"/> (null/blank = <see cref="DefaultFormat"/>)
        /// and clamps <paramref name="bitrate"/> to 64-320 (null = <see cref="DefaultBitrate"/>).
        /// Only an unknown format is an error: an out of range bitrate is clamped, as it always was.
        /// </summary>
        public static bool TryNormalize(string? format, int? bitrate, out string normalizedFormat, out int normalizedBitrate, out string? error)
        {
            normalizedFormat = string.IsNullOrWhiteSpace(format) ? DefaultFormat : format.Trim().ToLowerInvariant();
            normalizedBitrate = Math.Clamp(bitrate ?? DefaultBitrate, MinBitrate, MaxBitrate);
            error = null;
            if (!Formats.Contains(normalizedFormat))
            {
                error = "Unsupported format. Use mp3 or aac.";
                return false;
            }
            return true;
        }

        /// <summary>Extension of the transcoded file: mp3 or m4a (AAC in an MP4 container).</summary>
        public static string ExtensionFor(string format) => format == "mp3" ? "mp3" : "m4a";

        /// <summary>Content type of the transcoded file.</summary>
        public static string MimeTypeFor(string format) => format == "mp3" ? "audio/mpeg" : "audio/mp4";

        /// <summary>Cache file name of a channel file transcode (same name the mobile API always used).</summary>
        public static string ChannelTargetName(string cacheFileName, string format, int bitrate) =>
            $"{Path.GetFileNameWithoutExtension(cacheFileName)}-{format}{bitrate}.{ExtensionFor(format)}";

        /// <summary>
        /// Cache file name of a local file transcode. The key mixes path, size and
        /// modification time, so editing the local file yields a new name and a
        /// stale transcode is never served.
        /// </summary>
        public static string LocalTargetName(string resolvedPath, long length, DateTime lastWriteUtc, string format, int bitrate)
        {
            var identity = $"{resolvedPath.ToLowerInvariant()}|{length}|{lastWriteUtc.Ticks}";
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(identity))).Substring(0, 16);
            return $"local-{hash}-{format}{bitrate}.{ExtensionFor(format)}";
        }

        /// <summary>File name offered to the client: the original name with the new extension.</summary>
        public static string DownloadName(string originalName, string format) =>
            $"{Path.GetFileNameWithoutExtension(originalName)}.{ExtensionFor(format)}";
    }

    /// <summary>A transcoded file ready on disk (serve it with range processing).</summary>
    public sealed record AudioTranscodeResult(string Path, string MimeType, string DownloadName);

    /// <summary>
    /// Transcodes channel and local audio files to MP3/AAC with FFmpeg and keeps
    /// the result in <c>local/temp/_temp/transcoded</c>. Shared by the legacy mobile
    /// stream controller and the v1 endpoints. At most two FFmpeg processes run at
    /// once, and the same target is never transcoded twice at the same time.
    /// </summary>
    public class AudioTranscodeService
    {
        private static readonly SemaphoreSlim TranscodeSemaphore = new(2);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> TranscodeLocks = new();

        private readonly ITelegramService _telegram;
        private readonly ILogger<AudioTranscodeService> _logger;

        public AudioTranscodeService(ITelegramService telegram, ILogger<AudioTranscodeService> logger)
        {
            _telegram = telegram;
            _logger = logger;
        }

        /// <summary>Whether FFmpeg can be run (cached for a few minutes).</summary>
        public virtual bool IsAvailable => FfmpegProbe.Probe().Available;

        private static string TempPath => Path.Combine(FileService.TEMPDIR, "_temp");
        private static string TranscodedDir => Path.Combine(TempPath, "transcoded");

        /// <summary>
        /// Transcodes an indexed channel file. Downloads the original into the
        /// streaming cache first when it is not complete there.
        /// </summary>
        public async Task<AudioTranscodeResult> TranscodeChannelFile(
            string channelId, BsonFileManagerModel dbFile, string format, int bitrate, string? fileName, CancellationToken ct)
        {
            var name = fileName ?? dbFile.Name;
            var cacheFileName = $"{channelId}-{(dbFile.MessageId != null ? dbFile.MessageId.ToString() : dbFile.Id)}-{name}";
            var sourcePath = Path.Combine(TempPath, cacheFileName);
            Directory.CreateDirectory(TranscodedDir);

            var targetName = AudioTranscodeOptions.ChannelTargetName(cacheFileName, format, bitrate);
            var targetPath = Path.Combine(TranscodedDir, targetName);
            var result = new AudioTranscodeResult(targetPath, AudioTranscodeOptions.MimeTypeFor(format), AudioTranscodeOptions.DownloadName(name, format));

            if (File.Exists(targetPath)) return result;

            await WithTargetLock(targetName, async () =>
            {
                if (!File.Exists(sourcePath) || new FileInfo(sourcePath).Length < dbFile.Size)
                {
                    _logger.LogInformation("Downloading original before transcode: {FileName}", name);
                    await DownloadOriginalToCache(channelId, dbFile, sourcePath, ct);
                }
                _logger.LogInformation("Transcoding {FileName} to {Format} {Bitrate}k", name, format, bitrate);
                await Transcode(sourcePath, targetPath, format, bitrate, ct);
            }, targetPath, ct);

            return result;
        }

        /// <summary>Transcodes a local file (already resolved and checked to exist).</summary>
        public async Task<AudioTranscodeResult> TranscodeLocalFile(string resolvedPath, string format, int bitrate, CancellationToken ct)
        {
            var fileInfo = new FileInfo(resolvedPath);
            Directory.CreateDirectory(TranscodedDir);
            var targetName = AudioTranscodeOptions.LocalTargetName(resolvedPath, fileInfo.Length, fileInfo.LastWriteTimeUtc, format, bitrate);
            var targetPath = Path.Combine(TranscodedDir, targetName);
            var result = new AudioTranscodeResult(targetPath, AudioTranscodeOptions.MimeTypeFor(format), AudioTranscodeOptions.DownloadName(fileInfo.Name, format));

            if (File.Exists(targetPath)) return result;

            await WithTargetLock(targetName, async () =>
            {
                _logger.LogInformation("Transcoding local {FileName} to {Format} {Bitrate}k", fileInfo.Name, format, bitrate);
                await Transcode(resolvedPath, targetPath, format, bitrate, ct);
            }, targetPath, ct);

            return result;
        }

        // Per-target lock so concurrent requests don't transcode twice
        private static async Task WithTargetLock(string targetName, Func<Task> work, string targetPath, CancellationToken ct)
        {
            var fileLock = TranscodeLocks.GetOrAdd(targetName, _ => new SemaphoreSlim(1, 1));
            await fileLock.WaitAsync(ct);
            try
            {
                if (!File.Exists(targetPath)) await work();
            }
            finally
            {
                fileLock.Release();
            }
        }

        private static async Task Transcode(string sourcePath, string targetPath, string format, int bitrate, CancellationToken ct)
        {
            await TranscodeSemaphore.WaitAsync(ct);
            try
            {
                await TranscodeAudioFile(sourcePath, targetPath, format, bitrate, ct);
            }
            finally
            {
                TranscodeSemaphore.Release();
            }
        }

        // Sequential full download of the original file into the streaming cache
        private async Task DownloadOriginalToCache(string channelId, BsonFileManagerModel dbFile, string path, CancellationToken ct)
        {
            if (!dbFile.MessageId.HasValue)
                throw new InvalidOperationException("File has no MessageId");

            var message = await _telegram.getMessageFile(channelId, dbFile.MessageId.Value);
            if (message == null)
                throw new InvalidOperationException("Message not found in Telegram");

            using var fileStream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            var offset = fileStream.Length;
            fileStream.Seek(0, SeekOrigin.End);

            const int chunkSize = 512 * 1024;
            while (offset < dbFile.Size)
            {
                ct.ThrowIfCancellationRequested();
                var toDownload = (int)Math.Min(chunkSize, dbFile.Size - offset);
                var chunk = await _telegram.DownloadFileStream(message, offset, toDownload);
                if (chunk.Length == 0) break;
                await fileStream.WriteAsync(chunk, 0, chunk.Length, ct);
                offset += chunk.Length;
            }
            await fileStream.FlushAsync();
        }

        // Run FFmpeg to a temp file, then move into place so partial results
        // are never served
        private static async Task TranscodeAudioFile(string sourcePath, string targetPath, string format, int bitrate, CancellationToken ct)
        {
            var tempOut = targetPath + ".part";

            // mp3: keep embedded cover art (optional video stream copied as attached pic)
            // aac/m4a: audio only (cover copying into ipod container is less reliable)
            var codecArgs = format == "mp3"
                ? $"-map 0:a:0 -map 0:v? -c:v copy -disposition:v:0 attached_pic -codec:a libmp3lame -b:a {bitrate}k -id3v2_version 3 -f mp3"
                : $"-vn -codec:a aac -b:a {bitrate}k -movflags +faststart -f ipod";

            var args = $"-y -i \"{sourcePath}\" -map_metadata 0 {codecArgs} \"{tempOut}\"";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stderrTask = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
                try { File.Delete(tempOut); } catch { /* best effort */ }
                throw;
            }

            if (process.ExitCode != 0)
            {
                var stderr = await stderrTask;
                try { File.Delete(tempOut); } catch { /* best effort */ }
                throw new InvalidOperationException(
                    $"FFmpeg exited with code {process.ExitCode}: {stderr.Substring(0, Math.Min(500, stderr.Length))}");
            }

            File.Move(tempOut, targetPath, overwrite: true);
        }
    }
}
