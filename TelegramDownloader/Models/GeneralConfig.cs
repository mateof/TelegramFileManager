

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
    public class GeneralConfigStatic
    {
        public static GeneralConfig config { get; set; } = new GeneralConfig();
        public static TLConfig tlconfig { get; set; } = new TLConfig();

        public static async Task SaveChanges(IDbService db, GeneralConfig gc)
        {
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

    }

    public class TLConfig
    {
        public string? api_id { get; set; }
        public string? hash_id { get; set;}
        public string? mongo_connection_string { get; set; }
        public bool? avoid_checking_certificate { get; set; }
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
