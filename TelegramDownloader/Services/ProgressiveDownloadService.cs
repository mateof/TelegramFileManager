using System.Collections.Concurrent;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TL;

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
        /// <summary>
        /// Set when the user cancelled the background download from the downloads list.
        /// While set (and not expired), new range requests won't restart the cache download;
        /// playback keeps working through direct Telegram fetches.
        /// </summary>
        public bool CancelledByUser { get; set; }
        public DateTime CancelledAtUtc { get; set; }
        /// <summary>The entry of the downloads list; pausing, resuming and retrying it act on this download.</summary>
        public DownloadModel? Model { get; set; }
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

        // Cache directory size cap: oldest files are evicted first (by write time)
        private const long MAX_CACHE_BYTES = 10L * 1024 * 1024 * 1024; // 10 GB
        private static DateTime _lastCleanup = DateTime.MinValue;
        private static readonly object _cleanupLock = new();

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
            if (_activeDownloads.TryGetValue(cacheKey, out var existingInfo))
            {
                if (existingInfo.IsComplete)
                {
                    if (File.Exists(existingInfo.FilePath))
                    {
                        return existingInfo;
                    }
                    // Cache file was evicted: forget the stale entry and re-download
                    _activeDownloads.TryRemove(cacheKey, out _);
                }
                else if (IsUserCancelActive(existingInfo))
                {
                    // User stopped this cache download: don't restart it on every range
                    // request; playback is served through direct Telegram fetches instead
                    return existingInfo;
                }
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
                    if (existingInfo.IsDownloading ||
                        IsUserCancelActive(existingInfo) ||
                        (existingInfo.IsComplete && File.Exists(existingInfo.FilePath)))
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
                StartDownload(info, channelId, dbFile);
                return info;
            }
            finally
            {
                downloadLock.Release();
            }
        }

        /// <summary>Lists the download and starts it. Its entry can be paused, resumed and retried like any other.</summary>
        private void StartDownload(ProgressiveDownloadInfo info, string channelId, BsonFileManagerModel dbFile)
        {
            // Ordered list of Telegram messages that make up this file
            // (split files span several messages, one document per part)
            var messageIds = GetMessageIds(dbFile);
            if (messageIds == null || messageIds.Count == 0)
            {
                _logger.LogError("File has no MessageId: {CacheKey}", info.CacheKey);
                info.IsDownloading = false;
                return;
            }

            var dm = new DownloadModel
            {
                tis = _tis,
                startDate = DateTime.Now,
                path = info.FilePath,
                name = Path.GetFileName(info.FilePath),
                _size = info.TotalSize,
                channelName = _ts.getChatName(Convert.ToInt64(channelId)),
                callbacks = new Callbacks()
            };
            dm.callbacks.callback = () => DownloadInBackgroundAsync(info, dm, channelId, messageIds);
            info.Model = dm;
            _tis.addToDownloadList(dm);
            dm.RetryCallback();
        }

        /// <summary>One run of the download, from the bytes already in the cache file. Throws when it does not get to the end.</summary>
        private async Task DownloadInBackgroundAsync(
            ProgressiveDownloadInfo info,
            DownloadModel dm,
            string channelId,
            List<int> messageIds)
        {
            // A run that starts again (resume, retry) after a newer download of the same file
            if (_activeDownloads.TryGetValue(info.CacheKey, out var current) && current != info && current.IsDownloading)
                throw new InvalidOperationException("This file is already being downloaded");
            _activeDownloads[info.CacheKey] = info;
            info.CancelledByUser = false;
            info.IsDownloading = true;
            if (info.CancellationTokenSource == null || info.CancellationTokenSource.IsCancellationRequested)
                info.CancellationTokenSource = new CancellationTokenSource();

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

                // Create or open file for writing
                using var fileStream = new FileStream(
                    info.FilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.Read);

                // Resume from where we left off if file partially exists.
                // Align down to 512KB so per-part offsets stay Telegram-aligned.
                var startOffset = (fileStream.Length / 524288) * 524288;
                fileStream.Seek(startOffset, SeekOrigin.Begin);
                info.DownloadedBytes = startOffset;

                // Start the counter at the resume offset so progress and global
                // speed stats only account for newly downloaded bytes
                dm._transmitted = startOffset;

                // Download in chunks, part by part (a single-message file is just one part)
                const int chunkSize = 512 * 1024; // 512KB chunks
                const int maxConsecutiveErrors = 5;
                var consecutiveErrors = 0;
                var aborted = false;
                long partStartGlobal = 0;
                var ct = info.CancellationTokenSource!.Token;

                foreach (var msgId in messageIds)
                {
                    if (aborted || ct.IsCancellationRequested)
                        break;

                    var message = await _ts.getMessageFile(channelId, msgId);
                    var partSize = GetDocumentSize(message);
                    if (message == null || partSize <= 0)
                    {
                        _logger.LogError("Message {MessageId} not found in Telegram or has no document: {CacheKey}", msgId, info.CacheKey);
                        aborted = true;
                        break;
                    }

                    // Part already fully cached (resume): skip it
                    if (info.DownloadedBytes >= partStartGlobal + partSize)
                    {
                        partStartGlobal += partSize;
                        continue;
                    }

                    var localOffset = info.DownloadedBytes - partStartGlobal;

                    while (localOffset < partSize && !ct.IsCancellationRequested)
                    {
                        // React to Cancel/Pause pressed in the downloads list
                        dm.throwIfStopped();

                        var toDownload = (int)Math.Min(chunkSize, partSize - localOffset);

                        try
                        {
                            var chunk = await _ts.DownloadFileStream(message, localOffset, toDownload);
                            if (chunk.Length == 0)
                            {
                                _logger.LogWarning("Received empty chunk at offset {Offset} of message {MessageId}", localOffset, msgId);
                                aborted = true;
                                break;
                            }

                            await fileStream.WriteAsync(chunk, 0, chunk.Length, ct);
                            await fileStream.FlushAsync(ct);

                            localOffset += chunk.Length;
                            info.DownloadedBytes = partStartGlobal + localOffset;
                            consecutiveErrors = 0;

                            // Updates progress/speed and notifies the downloads UI;
                            // marks the task completed when the last byte is written.
                            // Throws when Cancel/Pause was pressed.
                            dm.ProgressCallback(info.DownloadedBytes, info.TotalSize);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException && ex is not TransferStoppedException)
                        {
                            consecutiveErrors++;
                            _logger.LogError(ex, "Error downloading chunk at offset {Offset} of message {MessageId} (attempt {Attempt}/{Max})",
                                localOffset, msgId, consecutiveErrors, maxConsecutiveErrors);

                            if (consecutiveErrors >= maxConsecutiveErrors)
                            {
                                _logger.LogError("Aborting background download after {Max} consecutive failures: {CacheKey}",
                                    maxConsecutiveErrors, info.CacheKey);
                                aborted = true;
                                break;
                            }

                            // Back off progressively before retrying
                            await Task.Delay(1000 * consecutiveErrors);
                        }
                    }

                    partStartGlobal += partSize;
                }

                info.IsComplete = info.DownloadedBytes >= info.TotalSize;
                info.IsDownloading = false;

                _logger.LogInformation("Background download {Status}: {FileName} ({Downloaded}/{Total} bytes)",
                    info.IsComplete ? "complete" : "incomplete",
                    Path.GetFileName(info.FilePath),
                    info.DownloadedBytes,
                    info.TotalSize);

                if (!info.IsComplete)
                    throw new IOException($"The download stopped at {info.DownloadedBytes} of {info.TotalSize} bytes");

                if (!string.IsNullOrEmpty(directory))
                {
                    _ = Task.Run(() => TrimCacheDirectory(directory));
                }
            }
            catch (TransferStoppedException)
            {
                // Paused or cancelled in the downloads list
                _logger.LogInformation("Background download stopped by user: {CacheKey}", info.CacheKey);
                info.CancelledByUser = true;
                info.CancelledAtUtc = DateTime.UtcNow;
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The entry of the downloads list shows it as failed and can retry it
                _logger.LogError(ex, "Error in background download: {CacheKey}", info.CacheKey);
                throw;
            }
            finally
            {
                info.IsDownloading = false;
            }
        }

        // A user cancel blocks automatic restarts for this long; afterwards a new
        // playback of the file starts caching again
        private static readonly TimeSpan USER_CANCEL_TTL = TimeSpan.FromHours(1);

        private bool IsUserCancelActive(ProgressiveDownloadInfo info)
        {
            // Paused: it waits for the user however long it takes
            if (info.Model is { } model && _tis.pendingDownloadModels.Contains(model))
                return true;
            return info.CancelledByUser && DateTime.UtcNow - info.CancelledAtUtc < USER_CANCEL_TTL;
        }

        /// <summary>
        /// Ordered list of Telegram message IDs that make up a file: a single message for
        /// regular files, several (one per part) for split files.
        /// </summary>
        internal static List<int>? GetMessageIds(BsonFileManagerModel dbFile)
        {
            if (dbFile.MessageId.HasValue && (dbFile.ListMessageId == null || dbFile.ListMessageId.Count <= 1))
                return new List<int> { dbFile.MessageId.Value };
            return dbFile.ListMessageId;
        }

        internal static long GetDocumentSize(Message? message)
        {
            return message?.media is MessageMediaDocument { document: Document doc } ? doc.size : 0;
        }

        /// <summary>
        /// Keeps the streaming cache directory under MAX_CACHE_BYTES, deleting the
        /// oldest files first (by write time). Files being downloaded are skipped.
        /// Throttled to run at most every 30 minutes.
        /// </summary>
        private void TrimCacheDirectory(string directory)
        {
            lock (_cleanupLock)
            {
                if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromMinutes(30)) return;
                _lastCleanup = DateTime.UtcNow;
            }

            try
            {
                var files = new DirectoryInfo(directory).GetFiles();
                var totalSize = files.Sum(f => f.Length);
                if (totalSize <= MAX_CACHE_BYTES) return;

                var activePaths = _activeDownloads.Values
                    .Where(i => i.IsDownloading)
                    .Select(i => i.FilePath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var removed = 0;
                foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
                {
                    if (totalSize <= MAX_CACHE_BYTES) break;
                    if (activePaths.Contains(file.FullName)) continue;

                    try
                    {
                        var size = file.Length;
                        file.Delete();
                        totalSize -= size;
                        removed++;

                        // Drop any stale tracking entry pointing at the deleted file
                        var stale = _activeDownloads.FirstOrDefault(kv =>
                            string.Equals(kv.Value.FilePath, file.FullName, StringComparison.OrdinalIgnoreCase));
                        if (stale.Key != null)
                        {
                            _activeDownloads.TryRemove(stale.Key, out _);
                        }
                    }
                    catch (IOException)
                    {
                        // File in use (e.g. being served): skip it
                    }
                }

                if (removed > 0)
                {
                    _logger.LogInformation("Cache trim: removed {Count} files from {Directory}", removed, directory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache trim failed for {Directory}", directory);
            }
        }
    }
}
