using System.Globalization;
using System.Text.Json;
using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Services.Library
{
    /// <summary>
    /// OMDb (omdbapi.com): IMDb data by title or IMDb id. Its ids are IMDb ids,
    /// so an item found here links straight to the other providers. English
    /// only, no backdrops or episode stills, and the free tier is limited to
    /// 1000 requests a day, hence the slower request rate.
    /// </summary>
    public class OmdbProvider : HttpMetadataProviderBase
    {
        public const string ProviderId = "omdb";

        public static readonly ProviderDescriptor Info = new()
        {
            Id = ProviderId,
            Name = "OMDb (IMDb data)",
            Website = "https://www.omdbapi.com",
            KeyUrl = "https://www.omdbapi.com/apikey.aspx",
            RequiresKey = true,
            SupportsEpisodes = true,
            SupportsImages = true,
            SupportsLanguage = false
        };

        public OmdbProvider(HttpClient http, string apiKey, ILogger logger) : base(http, apiKey, logger)
        {
            if (Http.BaseAddress == null)
                Http.BaseAddress = new Uri("https://www.omdbapi.com/");
        }

        public override string Id => ProviderId;
        public override ProviderDescriptor Descriptor => Info;
        protected override TimeSpan MinInterval => TimeSpan.FromMilliseconds(500);

        private string Url(params (string key, string? value)[] query)
        {
            var parts = new List<string> { $"apikey={Uri.EscapeDataString(ApiKey)}" };
            foreach (var (key, value) in query)
                if (!string.IsNullOrEmpty(value))
                    parts.Add($"{key}={Uri.EscapeDataString(value)}");
            return "?" + string.Join("&", parts);
        }

        private static bool Ok(JsonElement root, out string? error)
        {
            error = null;
            if (Str(root, "Response") == "False")
            {
                error = Str(root, "Error");
                return false;
            }
            return true;
        }

        private static void CheckKey(string? error)
        {
            if (error != null && error.Contains("key", StringComparison.OrdinalIgnoreCase))
                throw new ProviderException($"OMDb rejected the API key: {error}", 401);
            if (error != null && error.Contains("limit", StringComparison.OrdinalIgnoreCase))
                throw new ProviderException($"OMDb daily request limit reached: {error}", 429);
        }

        public override async Task<IReadOnlyList<ProviderCandidate>> SearchAsync(string kind, string query, int? year, CancellationToken ct)
        {
            var type = kind == LibraryKind.Movie ? "movie" : "series";
            using var doc = await GetJsonAsync(Url(("s", query), ("type", type), ("y", year?.ToString())), ct);
            var list = new List<ProviderCandidate>();
            if (doc == null) return list;
            if (!Ok(doc.RootElement, out var error))
            {
                CheckKey(error);
                return list;   // "Movie not found!"
            }
            if (!doc.RootElement.TryGetProperty("Search", out var results)) return list;
            int i = 0;
            foreach (var r in results.EnumerateArray())
            {
                list.Add(new ProviderCandidate
                {
                    Provider = ProviderId,
                    ProviderId = Str(r, "imdbID") ?? string.Empty,
                    Kind = kind,
                    Title = Str(r, "Title") ?? string.Empty,
                    Year = YearOf(Str(r, "Year")),
                    PosterUrl = PosterOf(Str(r, "Poster")),
                    ImdbId = Str(r, "imdbID"),
                    Popularity = 100 - i++
                });
            }
            return list;
        }

        public override async Task<LibraryItem?> GetItemAsync(string kind, string providerId, CancellationToken ct)
        {
            using var doc = await GetJsonAsync(Url(("i", providerId), ("plot", "full")), ct);
            if (doc == null) return null;
            var root = doc.RootElement;
            if (!Ok(root, out var error)) { CheckKey(error); return null; }

            var item = new LibraryItem
            {
                Kind = kind,
                Provider = ProviderId,
                ProviderId = providerId,
                Title = Str(root, "Title") ?? string.Empty,
                Year = YearOf(Str(root, "Year")),
                Overview = NotNa(Str(root, "Plot")),
                Rating = double.TryParse(NotNa(Str(root, "imdbRating")), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : null,
                VoteCount = int.TryParse(NotNa(Str(root, "imdbVotes"))?.Replace(",", string.Empty), out var v) ? v : null,
                Runtime = int.TryParse(NotNa(Str(root, "Runtime"))?.Replace("min", string.Empty).Trim(), out var rt) ? rt : null,
                PosterUrl = PosterOf(Str(root, "Poster")),
                MetadataFetchedAt = DateTime.UtcNow
            };
            item.ExternalIds["imdb"] = providerId;
            if (NotNa(Str(root, "Genre")) is string genre)
                item.Genres = genre.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0).ToList();

            if (kind == LibraryKind.Series && int.TryParse(NotNa(Str(root, "totalSeasons")), out var total))
                for (int s = 1; s <= total; s++)
                    item.Seasons.Add(new LibrarySeason { Number = s, Name = $"Season {s}" });

            item.SortTitle = MediaNameParser.SortTitle(item.Title);
            return item;
        }

        public override async Task<IReadOnlyList<LibraryEpisode>> GetSeasonAsync(string providerId, int season, CancellationToken ct)
        {
            using var doc = await GetJsonAsync(Url(("i", providerId), ("Season", season.ToString())), ct);
            var list = new List<LibraryEpisode>();
            if (doc == null) return list;
            var root = doc.RootElement;
            if (!Ok(root, out var error)) { CheckKey(error); return list; }
            if (!root.TryGetProperty("Episodes", out var episodes)) return list;
            foreach (var e in episodes.EnumerateArray())
            {
                if (!int.TryParse(Str(e, "Episode"), out var number)) continue;
                list.Add(new LibraryEpisode
                {
                    Season = season,
                    Number = number,
                    Title = NotNa(Str(e, "Title")),
                    AirDate = DateOf(NotNa(Str(e, "Released"))),
                    Rating = double.TryParse(NotNa(Str(e, "imdbRating")), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : null
                });
            }
            return list;
        }

        public override async Task<ProviderCandidate?> FindByImdbAsync(string imdbId, string? kind, CancellationToken ct)
        {
            using var doc = await GetJsonAsync(Url(("i", imdbId)), ct);
            if (doc == null) return null;
            var root = doc.RootElement;
            if (!Ok(root, out var error)) { CheckKey(error); return null; }
            var type = Str(root, "Type");
            var foundKind = type == "series" || type == "episode" ? LibraryKind.Series : LibraryKind.Movie;
            if (kind != null && kind != foundKind) return null;
            return new ProviderCandidate
            {
                Provider = ProviderId,
                ProviderId = imdbId,
                Kind = foundKind,
                Title = Str(root, "Title") ?? string.Empty,
                Year = YearOf(Str(root, "Year")),
                Overview = NotNa(Str(root, "Plot")),
                PosterUrl = PosterOf(Str(root, "Poster")),
                ImdbId = imdbId
            };
        }

        public override async Task EnrichAsync(LibraryItem item, CancellationToken ct)
        {
            if (item.Provider == ProviderId) return;
            if (item.Rating != null && item.PosterUrl != null) return;
            var imdb = item.ImdbId;
            if (imdb == null) return;
            var full = await GetItemAsync(item.Kind, imdb, ct);
            if (full == null) return;
            item.Rating ??= full.Rating;
            item.VoteCount ??= full.VoteCount;
            item.PosterUrl ??= full.PosterUrl;
            item.Overview ??= full.Overview;
            item.Runtime ??= full.Runtime;
            if (item.Genres.Count == 0) item.Genres = full.Genres;
        }

        private static string? NotNa(string? s) => string.IsNullOrWhiteSpace(s) || s == "N/A" ? null : s.Trim();
        private static string? PosterOf(string? s) => NotNa(s) is string p && p.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? p : null;
    }
}
