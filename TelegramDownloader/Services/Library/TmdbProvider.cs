using System.Text.Json;
using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Services.Library
{
    /// <summary>
    /// The Movie Database (api.themoviedb.org v3). Accepts either a v3 API key
    /// or a v4 read access token (a long JWT, sent as a bearer token).
    /// </summary>
    public class TmdbProvider : HttpMetadataProviderBase
    {
        public const string ProviderId = "tmdb";
        public const string ImageBase = "https://image.tmdb.org/t/p/original";

        public static readonly ProviderDescriptor Info = new()
        {
            Id = ProviderId,
            Name = "TMDB (The Movie Database)",
            Website = "https://www.themoviedb.org",
            KeyUrl = "https://www.themoviedb.org/settings/api",
            RequiresKey = true,
            SupportsEpisodes = true,
            SupportsImages = true,
            SupportsLanguage = true
        };

        private readonly string _language;
        private readonly bool _bearer;

        public TmdbProvider(HttpClient http, string apiKey, string language, ILogger logger) : base(http, apiKey, logger)
        {
            _language = string.IsNullOrWhiteSpace(language) ? "en-US" : language.Trim();
            _bearer = apiKey.StartsWith("ey", StringComparison.Ordinal) && apiKey.Length > 60;
            if (Http.BaseAddress == null)
                Http.BaseAddress = new Uri("https://api.themoviedb.org/3/");
        }

        public override string Id => ProviderId;
        public override ProviderDescriptor Descriptor => Info;

        protected override void Authorize(HttpRequestMessage request)
        {
            if (_bearer)
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
        }

        private string Url(string path, params (string key, string? value)[] query)
        {
            var parts = new List<string>();
            if (!_bearer) parts.Add($"api_key={Uri.EscapeDataString(ApiKey)}");
            foreach (var (key, value) in query)
                if (!string.IsNullOrEmpty(value))
                    parts.Add($"{key}={Uri.EscapeDataString(value)}");
            return parts.Count == 0 ? path : $"{path}?{string.Join("&", parts)}";
        }

        public override async Task<IReadOnlyList<ProviderCandidate>> SearchAsync(string kind, string query, int? year, CancellationToken ct)
        {
            var isMovie = kind == LibraryKind.Movie;
            var url = Url(isMovie ? "search/movie" : "search/tv",
                ("query", query),
                ("language", _language),
                ("include_adult", "false"),
                (isMovie ? "year" : "first_air_date_year", year?.ToString()));
            using var doc = await GetJsonAsync(url, ct);
            var list = new List<ProviderCandidate>();
            if (doc == null || !doc.RootElement.TryGetProperty("results", out var results)) return list;
            foreach (var r in results.EnumerateArray())
                list.Add(ToCandidate(r, kind));
            // Without a year TMDB returns everything with the words; a second pass
            // with the year is the cheapest way to disambiguate remakes.
            return list;
        }

        private static ProviderCandidate ToCandidate(JsonElement r, string kind)
        {
            var isMovie = kind == LibraryKind.Movie;
            var poster = Str(r, "poster_path");
            return new ProviderCandidate
            {
                Provider = ProviderId,
                ProviderId = Int(r, "id")?.ToString() ?? string.Empty,
                Kind = kind,
                Title = Str(r, isMovie ? "title" : "name") ?? string.Empty,
                OriginalTitle = Str(r, isMovie ? "original_title" : "original_name"),
                Year = YearOf(Str(r, isMovie ? "release_date" : "first_air_date")),
                Overview = Blank(Str(r, "overview")),
                PosterUrl = poster == null ? null : ImageBase + poster,
                Popularity = Dbl(r, "popularity") ?? 0
            };
        }

        public override async Task<LibraryItem?> GetItemAsync(string kind, string providerId, CancellationToken ct)
        {
            var isMovie = kind == LibraryKind.Movie;
            var path = (isMovie ? "movie/" : "tv/") + providerId;
            using var doc = await GetJsonAsync(Url(path, ("language", _language), ("append_to_response", "external_ids")), ct);
            if (doc == null) return null;
            var root = doc.RootElement;

            var item = new LibraryItem
            {
                Kind = kind,
                Provider = ProviderId,
                ProviderId = providerId,
                Title = Str(root, isMovie ? "title" : "name") ?? string.Empty,
                OriginalTitle = Blank(Str(root, isMovie ? "original_title" : "original_name")),
                Year = YearOf(Str(root, isMovie ? "release_date" : "first_air_date")),
                Overview = Blank(Str(root, "overview")),
                Tagline = Blank(Str(root, "tagline")),
                Status = Blank(Str(root, "status")),
                Rating = Dbl(root, "vote_average") is double v && v > 0 ? Math.Round(v, 1) : null,
                VoteCount = Int(root, "vote_count"),
                PosterUrl = Str(root, "poster_path") is string p ? ImageBase + p : null,
                BackdropUrl = Str(root, "backdrop_path") is string b ? ImageBase + b : null,
                MetadataFetchedAt = DateTime.UtcNow
            };
            item.ExternalIds["tmdb"] = providerId;
            if (root.TryGetProperty("genres", out var genres))
                foreach (var g in genres.EnumerateArray())
                    if (Str(g, "name") is string name) item.Genres.Add(name);

            if (isMovie)
            {
                item.Runtime = Int(root, "runtime") is int rt && rt > 0 ? rt : null;
            }
            else
            {
                if (root.TryGetProperty("episode_run_time", out var runTimes) && runTimes.ValueKind == JsonValueKind.Array)
                    foreach (var r in runTimes.EnumerateArray())
                        if (r.TryGetInt32(out var rt) && rt > 0) { item.Runtime = rt; break; }
                if (root.TryGetProperty("seasons", out var seasons))
                    foreach (var s in seasons.EnumerateArray())
                    {
                        var number = Int(s, "season_number") ?? -1;
                        if (number < 0) continue;
                        item.Seasons.Add(new LibrarySeason
                        {
                            Number = number,
                            Name = Blank(Str(s, "name")),
                            Overview = Blank(Str(s, "overview")),
                            PosterUrl = Str(s, "poster_path") is string sp ? ImageBase + sp : null,
                            EpisodeCount = Int(s, "episode_count"),
                            AirDate = DateOf(Str(s, "air_date"))
                        });
                    }
            }

            var externalIds = root.TryGetProperty("external_ids", out var ext) ? ext : root;
            if (Blank(Str(externalIds, "imdb_id")) is string imdb) item.ExternalIds["imdb"] = imdb;
            else if (Blank(Str(root, "imdb_id")) is string imdb2) item.ExternalIds["imdb"] = imdb2;
            if (Int(externalIds, "tvdb_id") is int tvdb) item.ExternalIds["tvdb"] = tvdb.ToString();

            // Texts missing in the configured language: fall back to English
            if (item.Overview == null && !_language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                using var en = await GetJsonAsync(Url(path, ("language", "en-US")), ct);
                if (en != null)
                {
                    item.Overview = Blank(Str(en.RootElement, "overview"));
                    item.Tagline ??= Blank(Str(en.RootElement, "tagline"));
                }
            }

            item.SortTitle = MediaNameParser.SortTitle(item.Title);
            return item;
        }

        public override async Task<IReadOnlyList<LibraryEpisode>> GetSeasonAsync(string providerId, int season, CancellationToken ct)
        {
            using var doc = await GetJsonAsync(Url($"tv/{providerId}/season/{season}", ("language", _language)), ct);
            var list = new List<LibraryEpisode>();
            if (doc == null || !doc.RootElement.TryGetProperty("episodes", out var episodes)) return list;
            foreach (var e in episodes.EnumerateArray())
            {
                list.Add(new LibraryEpisode
                {
                    Season = Int(e, "season_number") ?? season,
                    Number = Int(e, "episode_number") ?? 0,
                    Title = Blank(Str(e, "name")),
                    Overview = Blank(Str(e, "overview")),
                    StillUrl = Str(e, "still_path") is string s ? ImageBase + s : null,
                    AirDate = DateOf(Str(e, "air_date")),
                    Runtime = Int(e, "runtime") is int rt && rt > 0 ? rt : null,
                    Rating = Dbl(e, "vote_average") is double v && v > 0 ? Math.Round(v, 1) : null
                });
            }
            return list;
        }

        public override async Task<ProviderCandidate?> FindByImdbAsync(string imdbId, string? kind, CancellationToken ct)
        {
            using var doc = await GetJsonAsync(Url($"find/{imdbId}", ("external_source", "imdb_id"), ("language", _language)), ct);
            if (doc == null) return null;
            var root = doc.RootElement;
            if (kind != LibraryKind.Series && root.TryGetProperty("movie_results", out var movies) && movies.GetArrayLength() > 0)
                return ToCandidate(movies[0], LibraryKind.Movie);
            if (kind != LibraryKind.Movie && root.TryGetProperty("tv_results", out var tv) && tv.GetArrayLength() > 0)
                return ToCandidate(tv[0], LibraryKind.Series);
            return null;
        }

        public override async Task EnrichAsync(LibraryItem item, CancellationToken ct)
        {
            if (item.Provider == ProviderId) return;
            if (item.PosterUrl != null && item.BackdropUrl != null && item.Overview != null && item.ExternalIds.ContainsKey("tmdb")) return;
            var imdb = item.ImdbId;
            if (imdb == null) return;

            var candidate = await FindByImdbAsync(imdb, item.Kind, ct);
            if (candidate == null) return;
            var full = await GetItemAsync(item.Kind, candidate.ProviderId, ct);
            if (full == null) return;

            item.ExternalIds.TryAdd("tmdb", candidate.ProviderId);
            item.PosterUrl ??= full.PosterUrl;
            item.BackdropUrl ??= full.BackdropUrl;
            item.Overview ??= full.Overview;
            item.Tagline ??= full.Tagline;
            item.OriginalTitle ??= full.OriginalTitle;
            item.Runtime ??= full.Runtime;
            item.Year ??= full.Year;
            if (item.Genres.Count == 0) item.Genres = full.Genres;
            if (item.Seasons.Count == 0) item.Seasons = full.Seasons;
            foreach (var s in item.Seasons)
                s.PosterUrl ??= full.Seasons.FirstOrDefault(x => x.Number == s.Number)?.PosterUrl;
        }
    }
}
