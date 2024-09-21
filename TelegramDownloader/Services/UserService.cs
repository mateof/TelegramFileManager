using TelegramDownloader.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using System.Net.Http.Json;

namespace TelegramDownloader.Services
{
    public class UserService
    {
        public static string USERDATAFOLDER = $"{AppDomain.CurrentDomain.BaseDirectory.Replace("\\","/")}/datauser";
        public static async Task setUserDataToFile(UserData ud)
        {
            string json = JsonSerializer.Serialize(ud);
            await File.WriteAllTextAsync($"{USERDATAFOLDER}/userData.json", json);
        }

        public static void deleteUserDataToFile()
        {
            File.Delete($"{USERDATAFOLDER}/userData.json");
        }

        public static async Task<UserData?> getUserDataFromFile()
        {
            string json = null;
            try
            {
                json = await File.ReadAllTextAsync($"{USERDATAFOLDER}/userData.json");
            } catch(Exception ex)
            {
                return null;
            }
            
            if(json == null)
            {
                return null;
            }
            UserData ud = JsonSerializer.Deserialize<UserData>(json); ;
            return ud;
        }
    }
}
