using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Services.Library
{
    /// <summary>A search hit at a metadata provider.</summary>
    public class ProviderCandidate
    {
        public string Provider { get; set; } = string.Empty;
        public string ProviderId { get; set; } = string.Empty;
        public string Kind { get; set; } = LibraryKind.Movie;
        public string Title { get; set; } = string.Empty;
        public string? OriginalTitle { get; set; }
        public int? Year { get; set; }
        public string? Overview { get; set; }
        public string? PosterUrl { get; set; }
        public string? ImdbId { get; set; }
        public double Popularity { get; set; }
    }

    /// <summary>Static description of a provider, for the settings UI.</summary>
    public class ProviderDescriptor
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;
        /// <summary>Where the user gets an API key.</summary>
        public string KeyUrl { get; set; } = string.Empty;
        public bool RequiresKey { get; set; } = true;
        public bool SupportsEpisodes { get; set; }
        public bool SupportsImages { get; set; }
        public bool SupportsLanguage { get; set; }
    }

    /// <summary>
    /// A source of movie/series metadata (TMDB, OMDb...). Implementations are
    /// stateless apart from their HTTP client; the scan decides which one to
    /// ask and in what order.
    /// </summary>
    public interface IMetadataProvider
    {
        string Id { get; }
        ProviderDescriptor Descriptor { get; }

        Task<IReadOnlyList<ProviderCandidate>> SearchAsync(string kind, string query, int? year, CancellationToken ct);

        /// <summary>Full metadata of one item. Seasons are filled for series.</summary>
        Task<LibraryItem?> GetItemAsync(string kind, string providerId, CancellationToken ct);

        /// <summary>Episodes of one season. Empty when the provider has no episode data.</summary>
        Task<IReadOnlyList<LibraryEpisode>> GetSeasonAsync(string providerId, int season, CancellationToken ct);

        Task<ProviderCandidate?> FindByImdbAsync(string imdbId, string? kind, CancellationToken ct);

        /// <summary>
        /// Fills fields the primary provider left empty (rating, poster, ids)
        /// using the item's external ids. Must not overwrite existing values.
        /// </summary>
        Task EnrichAsync(LibraryItem item, CancellationToken ct);
    }

    /// <summary>Where the scan and the manual actions get their providers from.</summary>
    public interface IMetadataProviderSource
    {
        /// <summary>Enabled and usable providers, best first.</summary>
        IReadOnlyList<IMetadataProvider> GetEnabled();
        IMetadataProvider? Get(string id);
    }

    public class ProviderException : Exception
    {
        public int? StatusCode { get; }
        public ProviderException(string message, int? statusCode = null, Exception? inner = null) : base(message, inner)
        {
            StatusCode = statusCode;
        }
    }
}
