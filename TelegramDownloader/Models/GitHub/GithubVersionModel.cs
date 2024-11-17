namespace TelegramDownloader.Models.GitHub
{
    public class GithubVersionModel
    {
        public string url { get; set; }
        public string assets_url { get; set; }
        public string upload_url { get; set; }
        public string html_url { get; set; }
        public int id { get; set; }
        public string tag_name { get; set; }
        public string name { get; set; }
        public DateTime created_at { get; set; }
        public DateTime published_at { get; set; }

    }
}
