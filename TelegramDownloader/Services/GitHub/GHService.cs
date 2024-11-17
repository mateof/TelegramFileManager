using Newtonsoft.Json;
using TelegramDownloader.Models.GitHub;
using TL;
using WTelegram;

namespace TelegramDownloader.Services.GitHub
{
    public class GHService
    {

        const string ghVersionUri = "https://api.github.com/repos/mateof/TelegramFileManager/releases?per_page=1";
        public GHService() 
        {
        }

        public async Task<GithubVersionModel> GetLastVersion()
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
            var result = await client.GetAsync(ghVersionUri);
            return JsonConvert.DeserializeObject<List<GithubVersionModel>>(await result.Content.ReadAsStringAsync()).FirstOrDefault();
        }
    }
}
