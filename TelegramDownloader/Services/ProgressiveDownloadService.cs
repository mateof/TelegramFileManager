using System.Collections.Concurrent;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services
{
    /// <summary>
    /// Manages progressive downloads - tracks which files are being downloaded
    /// and what portions have been cached locally.
    /// </summary>
    public interface IProgressiveDownloadService
    {
        /// <summary>
        /// Gets download info for a file, or null if not being downloaded
        /// </summary>
        ProgressiveDownloadInfo? GetDownloadInfo(string cacheKey);

        /// <summary>
        /// Starts a background download for a file if not already in progress
        /// </summary>
        Task<ProgressiveDownloadInfo> StartOrGetDownloadAsync(
            string cacheKey,
            string channelId,
            BsonFileManagerModel dbFile,
            string filePath);

        /// <summary>
        /// Checks if a byte range is available in the local cache
        /// </summary>
        bool IsRangeAvailable(string cacheKey, long start, long end);

        /// <summary>
        /// Gets the downloaded bytes count for a file
        /// </summary>
        long GetDownloadedBytes(string cacheKey);
    }

    public class ProgressiveDownloadInfo
    {
        public string CacheKey { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public long DownloadedBytes { get; set; }
        public bool IsComplete { get; set; }
        public bool IsDownloading { get; set; }
        public DateTime StartTime { get; set; }
        public CancellationTokenSource? CancellationTokenSource { get; set; }
    }

    public class ProgressiveDownloadService : IProgressiveDownloadService
    {
        private readonly ITelegramService _ts;
        private readonly TransactionInfoService _tis;
        private readonly ILogger<ProgressiveDownloadService> _logger;

        // Track active downloads
        private readonly ConcurrentDictionary<string, ProgressiveDownloadInfo> _activeDownloads = new();

        // Lock for starting downloads
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _downloadLocks = new();

        public ProgressiveDownloadService(
            ITelegramService ts,
            TransactionInfoService tis,
            ILogger<ProgressiveDownloadService> logger)
        {
            _ts = ts;
            _tis = tis;
            _logger = logger;
        }

        public ProgressiveDownloadInfo? GetDownloadInfo(string cacheKey)
        {
            _activeDownloads.TryGetValue(cacheKey, out var info);
            return info;
        }

        public long GetDownloadedBytes(string cacheKey)
        {
            if (_activeDownloads.TryGetValue(cacheKey, out var info))
            {
                return info.DownloadedBytes;
            }
            return 0;
        }

        public bool IsRangeAvailable(string cacheKey, long start, long end)
        {
            if (!_activeDownloads.TryGetValue(cacheKey, out var info))
            {
                // Check if file exists and is complete
                if (File.Exists(info?.FilePath ?? ""))
                {
                    var fileInfo = new FileInfo(info!.FilePath);
                    return end <= fileInfo.Length;
                }
                return false;
            }

            // Check if the requested range has been downloaded
            return end <= info.DownloadedBytes;
        }

        public async Task<ProgressiveDownloadInfo> StartOrGetDownloadAsync(
            string cacheKey,
            string channelId,
            BsonFileManagerModel dbFile,
            string filePath)
        {
            // Quick check if already complete
            if (_activeDownloads.TryGetValue(cacheKey, out var existingInfo) && existingInfo.IsComplete)
            {
                return existingInfo;
            }

            // Check if file already exists and is complete
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length >= dbFile.Size)
                {
                    var completeInfo = new ProgressiveDownloadInfo
                    {
                        CacheKey = cacheKey,
                        FilePath = filePath,
                        TotalSize = dbFile.Size,
                        DownloadedBytes = dbFile.Size,
                        IsComplete = true,
                        IsDownloading = false
                    };
                    _activeDownloads[cacheKey] = completeInfo;
                    return completeInfo;
                }
            }

            // Get or create lock for this file
            var downloadLock = _downloadLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

            await downloadLock.WaitAsync();
            try
            {
                // Double-check after acquiring lock
                if (_activeDownloads.TryGetValue(cacheKey, out existingInfo))
                {
                    if (existingInfo.IsComplete || existingInfo.IsDownloading)
                    {
                        return existingInfo;
                    }
                }

                // Start new download
                var info = new ProgressiveDownloadInfo
                {
                    CacheKey = cacheKey,
                    FilePath = filePath,
                    TotalSize = dbFile.Size,
                    DownloadedBytes = 0,
                    IsComplete = false,
                    IsDownloading = true,
                    StartTime = DateTime.UtcNow,
                    CancellationTokenSource = new CancellationTokenSource()
                };

                _activeDownloads[cacheKey] = info;

                // Start background download
                _ = DownloadInBackgroundAsync(info, channelId, dbFile);

                return info;
            }
            finally
            {
                downloadLock.Release();
            }
        }

        private async Task DownloadInBackgroundAsync(
            ProgressiveDownloadInfo info,
            string channelId,
            BsonFileManagerModel dbFile)
        {
            try
            {
                _logger.LogInformation("Starting background download: {FileName} ({Size} bytes)",
                    Path.GetFileName(info.FilePath), info.TotalSize);

                // Ensure directory exists
                var directory = Path.GetDirectoryName(info.FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Get message from Telegram
                if (!dbFile.MessageId.HasValue)
                {
                    _logger.LogError("File has no MessageId: {CacheKey}", info.CacheKey);
                    info.IsDownloading = false;
                    return;
                }

                var message = await _ts.getMessageFile(channelId, dbFile.MessageId.Value);
                if (message == null)
                {
                    _logger.LogError("Message not found in Telegram: {CacheKey}", info.CacheKey);
                    info.IsDownloading = false;
                    return;
                }

                // Create or open file for writing
                using var fileStream = new FileStream(
                    info.FilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.Read);

                // Resume from where we left off if file partially exists
                var startOffset = fileStream.Length;
                fileStream.Seek(0, SeekOrigin.End);
                info.DownloadedBytes = startOffset;

                // Create download model for progress tracking
                var dm = new DownloadModel
                {
                    tis = _tis,
                    startDate = DateTime.Now,
                    path = info.FilePath,
                    name = Path.GetFileName(info.FilePath),
                    _size = info.TotalSize,
                    channelName = _ts.getChatName(Convert.ToInt64(channelId))
                };
                _tis.addToDownloadList(dm);

                // Download in chunks
                const int chunkSize = 512 * 1024; // 512KB chunks
                var chatMessage = new ChatMessages { message = message };

                while (info.DownloadedBytes < info.TotalSize && !info.CancellationTokenSource!.Token.IsCancellationRequested)
                {
                    var remaining = info.TotalSize - info.DownloadedBytes;
                    var toDownload = (int)Math.Min(chunkSize, remaining);

                    try
                    {
                        var chunk = await _ts.DownloadFileStream(message, info.DownloadedBytes, toDownload);
                        if (chunk.Length == 0)
                        {
                            _logger.LogWarning("Received empty chunk at offset {Offset}", info.DownloadedBytes);
                            break;
                        }

                        await fileStream.WriteAsync(chunk, 0, chunk.Length, info.CancellationTokenSource.Token);
                        await fileStream.FlushAsync(info.CancellationTokenSource.Token);

                        info.DownloadedBytes += chunk.Length;
                        dm._transmitted = info.DownloadedBytes;

                        // Log progress every 10%
                        var progress = (double)info.DownloadedBytes / info.TotalSize * 100;
                        if ((int)progress % 10 == 0)
                        {
                            _logger.LogDebug("Download progress: {FileName} - {Progress:F1}%",
                                Path.GetFileName(info.FilePath), progress);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Error downloading chunk at offset {Offset}", info.DownloadedBytes);
                        // Wait a bit and retry
                        await Task.Delay(1000);
                    }
                }

                info.IsComplete = info.DownloadedBytes >= info.TotalSize;
                info.IsDownloading = false;

                _logger.LogInformation("Background download {Status}: {FileName} ({Downloaded}/{Total} bytes)",
                    info.IsComplete ? "complete" : "incomplete",
                    Path.GetFileName(info.FilePath),
                    info.DownloadedBytes,
                    info.TotalSize);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Download cancelled: {CacheKey}", info.CacheKey);
                info.IsDownloading = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in background download: {CacheKey}", info.CacheKey);
                info.IsDownloading = false;
            }
        }
    }
}
