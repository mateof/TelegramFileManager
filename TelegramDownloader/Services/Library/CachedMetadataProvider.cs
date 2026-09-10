using System.Text.Json;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Services.Library
{
    /// <summary>
    /// Remembers searches and IMDb lookups in MongoDB so rescans and manual
    /// corrections don't burn the provider quota on the same titles.
    /// </summary>
    public class CachedMetadataProvider : IMetadataProvider
    {
        private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);
        private readonly IMetadataProvider _inner;
        private readonly ILibraryDbService _cache;
        private readonly string _scope;

        public CachedMetadataProvider(IMetadataProvider inner, ILibraryDbService cache, string scope)
        {
            _inner = inner;
            _cache = cache;
            _scope = scope;
        }

        public string Id => _inner.Id;
        public ProviderDescriptor Descriptor => _inner.Descriptor;

        public async Task<IReadOnlyList<ProviderCandidate>> SearchAsync(string kind, string query, int? year, CancellationToken ct)
        {
            var key = $"{Id}:{_scope}:search:{kind}:{query.ToLowerInvariant()}:{year}";
            var cached = await _cache.GetCached(key);
            if (cached != null)
                return JsonSerializer.Deserialize<List<ProviderCandidate>>(cached) ?? new List<ProviderCandidate>();
            var result = await _inner.SearchAsync(kind, query, year, ct);
            await _cache.SetCached(key, JsonSerializer.Serialize(result), Ttl);
            return result;
        }

        public Task<LibraryItem?> GetItemAsync(string kind, string providerId, CancellationToken ct) =>
            _inner.GetItemAsync(kind, providerId, ct);

        public Task<IReadOnlyList<LibraryEpisode>> GetSeasonAsync(string providerId, int season, CancellationToken ct) =>
            _inner.GetSeasonAsync(providerId, season, ct);

        public async Task<ProviderCandidate?> FindByImdbAsync(string imdbId, string? kind, CancellationToken ct)
        {
            var key = $"{Id}:{_scope}:imdb:{imdbId}:{kind}";
            var cached = await _cache.GetCached(key);
            if (cached != null)
                return cached == "null" ? null : JsonSerializer.Deserialize<ProviderCandidate>(cached);
            var result = await _inner.FindByImdbAsync(imdbId, kind, ct);
            await _cache.SetCached(key, result == null ? "null" : JsonSerializer.Serialize(result), Ttl);
            return result;
        }

        public Task EnrichAsync(LibraryItem item, CancellationToken ct) => _inner.EnrichAsync(item, ct);
    }
}
