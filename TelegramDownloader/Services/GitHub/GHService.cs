using Newtonsoft.Json;
using System.Reflection;
using TelegramDownloader.Models.GitHub;

namespace TelegramDownloader.Services.GitHub
{
    public class GHService
    {
        private const string GH_RELEASES_URI = "https://api.github.com/repos/mateof/TelegramFileManager/releases";
        private readonly HttpClient _client;
        private VersionInfo? _cachedVersionInfo;
        private DateTime _lastCheck = DateTime.MinValue;
        private readonly TimeSpan _cacheTime = TimeSpan.FromMinutes(30);

        public GHService()
        {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            _client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            _client.DefaultRequestHeaders.UserAgent.TryParseAdd("TelegramFileManager");
        }

        /// <summary>
        /// Get current app version from assembly
        /// </summary>
        public string GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        }

        /// <summary>
        /// Check if current version is a dev version
        /// </summary>
        public bool IsDevVersion()
        {
            var version = GetCurrentVersion();
            return version.Contains("-dev") || version == "0.0.0.0";
        }

        /// <summary>
        /// Get last stable release (non-prerelease)
        /// </summary>
        public async Task<GithubVersionModel?> GetLastVersion()
        {
            var releases = await GetReleases(10);
            return releases?.FirstOrDefault(r => !r.prerelease && !r.draft);
        }

        /// <summary>
        /// Get last dev/prerelease version
        /// </summary>
        public async Task<GithubVersionModel?> GetLastDevVersion()
        {
            var releases = await GetReleases(10);
            return releases?.FirstOrDefault(r => r.prerelease && !r.draft);
        }

        /// <summary>
        /// Get complete version info with comparison
        /// </summary>
        public async Task<VersionInfo> GetVersionInfo(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedVersionInfo != null && DateTime.Now - _lastCheck < _cacheTime)
            {
                return _cachedVersionInfo;
            }

            var info = new VersionInfo
            {
                CurrentVersion = GetCurrentVersion(),
                IsDevVersion = IsDevVersion()
            };

            try
            {
                var releases = await GetReleases(20);
                if (releases != null)
                {
                    info.LatestRelease = releases.FirstOrDefault(r => !r.prerelease && !r.draft);
                    info.LatestDev = releases.FirstOrDefault(r => r.prerelease && !r.draft);

                    // Compare versions
                    if (info.LatestRelease != null)
                    {
                        info.HasNewRelease = CompareVersions(info.CurrentVersion, info.LatestRelease.Version) < 0;
                    }

                    if (info.LatestDev != null && info.IsDevVersion)
                    {
                        info.HasNewDev = CompareDevVersions(info.CurrentVersion, info.LatestDev.Version);
                    }
                }
            }
            catch (Exception)
            {
                // Silently fail - don't break app if GitHub is unreachable
            }

            _cachedVersionInfo = info;
            _lastCheck = DateTime.Now;
            return info;
        }

        private async Task<List<GithubVersionModel>?> GetReleases(int count)
        {
            try
            {
                var result = await _client.GetAsync($"{GH_RELEASES_URI}?per_page={count}");
                if (result.IsSuccessStatusCode)
                {
                    var content = await result.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<GithubVersionModel>>(content);
                }
            }
            catch
            {
                // Silently fail
            }
            return null;
        }

        /// <summary>
        /// Compare two semantic versions. Returns -1 if v1 &lt; v2, 0 if equal, 1 if v1 &gt; v2
        /// </summary>
        private int CompareVersions(string v1, string v2)
        {
            // Remove -dev suffix for comparison
            v1 = v1.Split('-')[0];
            v2 = v2.Split('-')[0];

            if (Version.TryParse(v1, out var version1) && Version.TryParse(v2, out var version2))
            {
                return version1.CompareTo(version2);
            }
            return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Compare dev versions by date component
        /// </summary>
        private bool CompareDevVersions(string current, string latest)
        {
            // Dev versions format: 0.0.0-dev.YYYYMMDD.HHMMSS.sha
            if (!current.Contains("-dev") || !latest.Contains("-dev"))
                return false;

            try
            {
                var currentParts = current.Split('.');
                var latestParts = latest.Split('.');

                // Compare date parts (index 1 after split on 0.0.0-dev)
                if (currentParts.Length >= 2 && latestParts.Length >= 2)
                {
                    var currentDate = currentParts[1].Replace("-dev", "");
                    var latestDate = latestParts[1].Replace("-dev", "");
                    return string.Compare(latestDate, currentDate, StringComparison.Ordinal) > 0;
                }
            }
            catch
            {
                // Fallback to string comparison
            }

            return string.Compare(latest, current, StringComparison.Ordinal) > 0;
        }
    }
}
