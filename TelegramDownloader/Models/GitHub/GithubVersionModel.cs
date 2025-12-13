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
        public bool prerelease { get; set; }
        public bool draft { get; set; }
        public string body { get; set; }

        /// <summary>
        /// Returns version without 'v' prefix
        /// </summary>
        public string Version => tag_name?.TrimStart('v') ?? "0.0.0";

        /// <summary>
        /// Check if this is a dev/prerelease version
        /// </summary>
        public bool IsDev => prerelease || (tag_name?.Contains("-dev") ?? false);
    }

    public class VersionInfo
    {
        public string CurrentVersion { get; set; } = "0.0.0.0";
        public GithubVersionModel? LatestRelease { get; set; }
        public GithubVersionModel? LatestDev { get; set; }
        public bool HasNewRelease { get; set; }
        public bool HasNewDev { get; set; }
        public bool IsDevVersion { get; set; }
        public bool IsDockerVersion => CurrentVersion == "0.0.0.0" || CurrentVersion.Contains("-dev");
    }
}
