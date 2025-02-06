

using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using TelegramDownloader.Data.db;
using Syncfusion.Blazor.RichTextEditor;
using Newtonsoft.Json;
using Syncfusion.Blazor.Charts;
using TelegramDownloader.Data;

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
        public bool ShouldShowLogInTerminal { get; set; } = false;
        public List<long> FavouriteChannels { get; set; } = new List<long>();
        
    }

    public class TLConfig
    {
        public string? api_id { get; set; }
        public string? hash_id { get; set;}
        public string? mongo_connection_string { get; set; }
        public bool? avoid_checking_certificate { get; set; }
    }
}
