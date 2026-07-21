

using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using TelegramDownloader.Data.db;
using Syncfusion.Blazor.RichTextEditor;
using Newtonsoft.Json;
using Syncfusion.Blazor.Charts;
using TelegramDownloader.Data;
using System.Diagnostics;
using TelegramDownloader.Services;

namespace TelegramDownloader.Models
{
    /// <summary>
    /// How exported STRM files (Emby/Kodi/etc.) play media from Telegram.
    /// </summary>
    public enum StreamingMode
    {
        /// <summary>
        /// Stream chunks directly from Telegram on demand. Playback starts immediately,
        /// nothing is stored on disk, but every play re-downloads from Telegram.
        /// </summary>
        DirectStream = 0,
        /// <summary>
        /// Stream immediately while a background download fills the local cache.
        /// Playback starts from second one and subsequent plays are served from disk.
        /// </summary>
        ProgressiveCache = 1,
        /// <summary>
        /// Download the whole file to the local cache before playback starts (legacy behavior).
        /// </summary>
        Preload = 2
    }

    public class GeneralConfigStatic
    {
        public static GeneralConfig config { get; set; } = new GeneralConfig();
        public static TLConfig tlconfig { get; set; } = new TLConfig();

        public static async Task SaveChanges(IDbService db, GeneralConfig gc)
        {
            // Validate MemorySplitSizeGB <= SplitSize
            if (gc.EnableMemorySplitUpload && gc.SplitSize > 0 && gc.MemorySplitSizeGB > gc.SplitSize)
            {
                gc.MemorySplitSizeGB = gc.SplitSize;
            }

            // Ensure MemorySplitSizeGB is within valid range based on Telegram limits
            // Premium: max 4, Non-premium: max 2 (same as Telegram file size limits)
            int maxAllowedSize = TelegramService.isPremium ? 4 : 2;
            if (gc.MemorySplitSizeGB < 1) gc.MemorySplitSizeGB = 1;
            if (gc.MemorySplitSizeGB > maxAllowedSize) gc.MemorySplitSizeGB = maxAllowedSize;

            // Keep the legacy flag in sync so older builds reading this config behave the same
            if (gc.StrmStreamingMode.HasValue)
                gc.PreloadFilesOnStream = gc.StrmStreamingMode.Value == StreamingMode.Preload;

            await db.SaveConfig(gc);
            config = gc;
            if (gc.SplitSize > 0)
            {
                TelegramService.SetSplitSizeGB();
            }


        }

        public static void AddFavouriteChannel(long id)
        {
            config.FavouriteChannels.Add(id);
        }

        public static void DeleteFavouriteChannel(long id)
        {
            config.FavouriteChannels.Remove(id);
        }

        public static void loadDbConfig()
        {
            tlconfig = LoadJson<TLConfig>("./Configuration/config.json");
        } 

        public static async Task<GeneralConfig> Load(IDbService db)
        {
            config = await db.LoadConfig();
            return config;
        }

        public static T LoadJson<T>(string file)
        {
            if (File.Exists(file))
                using (StreamReader r = new StreamReader(file))
                {
                    string json = r.ReadToEnd();
                    T item = JsonConvert.DeserializeObject<T>(json);
                    return item;
                }
            return default(T);
        }

    }

    [BsonIgnoreExtraElements]
    public class GeneralConfig
    {
        [BsonId]
        public string type = "general";
        public bool ShouldNotify { get; set; } = false;
        public int TimeSleepBetweenTransactions { get; set; } = 2000;
        public int SplitSize { get; set; } = 0;
        public int MaxSimultaneousDownloads = 1;
        public bool CheckHash { get; set; } = false;
        /// <summary>
        /// Maximum image upload size in megabytes. if 0, All images will be sent as a file
        /// </summary>
        public int MaxImageUploadSizeInMb { get; set; } = 10;
        /// <summary>
        /// downloads the entire file when it weighs less than indicated
        /// </summary>
        public int MaxPreloadFileSizeInMb { get; set; } = 30;
        public bool ShouldShowCaptionPath { get; set; } = false;
        public bool ShouldShowLogInTerminal { get; set; } = false;
        public bool PreloadFilesOnStream { get; set; } = false;
        /// <summary>
        /// Streaming mode used by exported STRM files. Null means "not set yet":
        /// the effective mode is then derived from the legacy PreloadFilesOnStream flag.
        /// </summary>
        [BsonRepresentation(BsonType.String)]
        public StreamingMode? StrmStreamingMode { get; set; } = null;

        public StreamingMode GetEffectiveStreamingMode()
        {
            if (StrmStreamingMode.HasValue)
                return StrmStreamingMode.Value;
            return PreloadFilesOnStream ? StreamingMode.Preload : StreamingMode.DirectStream;
        }
        public bool ShouldShowPaginatedFileChannel { get; set; } = false;
        public bool hasFileManagerVirtualScroll { get; set; } = false;
        public bool UseMobileFileManagerAlways { get; set; } = false;
        public bool ShowChannelImages { get; set; } = false;
        public List<long> FavouriteChannels { get; set; } = new List<long>();
        public WebDavModel webDav { get; set; } = new WebDavModel();

        // Task Persistence Settings
        public bool EnableTaskPersistence { get; set; } = true;
        public int TaskPersistenceDebounceSeconds { get; set; } = 5;
        public int StaleTaskCleanupDays { get; set; } = 7;
        public bool AutoResumeOnStartup { get; set; } = true;

        // Video Transcoding Settings
        /// <summary>
        /// Enable FFmpeg video transcoding for non-browser formats (MKV, AVI, WMV, etc.)
        /// Requires FFmpeg to be installed on the system
        /// </summary>
        public bool EnableVideoTranscoding { get; set; } = false;

        // Refresh Data Settings
        /// <summary>
        /// Enable refresh data option for channels where you are admin/owner.
        /// By default, refresh is only available for channels you don't own.
        /// </summary>
        public bool EnableRefreshOwnChannels { get; set; } = false;

        // Memory Split Upload Settings
        /// <summary>
        /// Enable memory-based file splitting for large file uploads.
        /// When enabled, files larger than MemorySplitSizeGB will be read and uploaded in chunks
        /// without creating temporary split files on disk.
        /// </summary>
        public bool EnableMemorySplitUpload { get; set; } = false;

        /// <summary>
        /// Size in GB for each memory chunk when uploading large files.
        /// Premium accounts: 1-4 GB, Non-premium: 1-2 GB.
        /// Only used when EnableMemorySplitUpload is true.
        /// Note: Uses Telegram's actual size limits (1GB = 1024*1024*1000 bytes, not 1024^3).
        /// </summary>
        public int MemorySplitSizeGB { get; set; } = 2;

        // Transfer Speed Settings
        /// <summary>
        /// Number of 512KB file chunks requested in parallel per transfer (1-16).
        /// WTelegramClient's default of 2 caps throughput at roughly 1MB per
        /// round-trip to Telegram's data center (~5-7 MB/s on typical latency).
        /// Higher values remove that latency bottleneck; the server-side speed
        /// limit for non-Premium accounts still applies. Takes effect on the
        /// next transfer, no restart needed.
        /// </summary>
        public int ParallelTransfers { get; set; } = 4;

        // Multi-connection download settings
        /// <summary>
        /// EXPERIMENTAL: download large files using several parallel MTProto
        /// connections, the same technique Telegram Desktop uses to reach high
        /// speeds. Telegram limits throughput per connection (~5-6 MB/s), so a
        /// single connection cannot go faster no matter how many chunks are in
        /// flight; multiple connections each get their own allowance. The extra
        /// connections are clones of the main session (same authorization, own
        /// connection), so no new device entries are created on the account.
        /// </summary>
        public bool EnableMultiConnectionDownloads { get; set; } = false;

        /// <summary>
        /// Number of parallel connections used per file download (2-8).
        /// Only used when EnableMultiConnectionDownloads is true.
        /// </summary>
        public int DownloadConnections { get; set; } = 4;

    }

    public class TLConfig
    {
        public string? api_id { get; set; }
        public string? hash_id { get; set;}
        public string? mongo_connection_string { get; set; }
        public bool? avoid_checking_certificate { get; set; }
        public bool? open_browser_on_startup { get; set; }
        /// <summary>
        /// API key for mobile app authentication. If set, mobile API endpoints require this key in X-Api-Key header.
        /// </summary>
        public string? mobile_api_key { get; set; }
    }

    public class WebDavModel
    {
        public string Host { get; set; } = "127.0.0.1";
        public int PuertoEntrada { get; set; } = 8080;
        public int PuertoSalida { get; set; } = 9081;
        [BsonIgnore]
        public WebbDavService? webDavService { get; set; } = new WebbDavService();

        public void start()
        {
            webDavService.Start(port: PuertoEntrada, externalPort: PuertoSalida, host: Host);
        }

        public void stop()
        {
            webDavService.Stop();
        }
    }
}
