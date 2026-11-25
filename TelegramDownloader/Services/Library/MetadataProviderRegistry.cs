using System.Collections.Concurrent;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services.Library
{
    /// <summary>
    /// Builds the metadata providers the user enabled in the configuration, in
    /// priority order. Instances are reused while their key and language stay
    /// the same so the per-provider request throttle keeps working.
    /// </summary>
    public class MetadataProviderRegistry : IMetadataProviderSource
    {
        public static readonly IReadOnlyList<ProviderDescriptor> Known = new[] { TmdbProvider.Info, OmdbProvider.Info };

        private readonly IHttpClientFactory _httpFactory;
        private readonly ILibraryDbService _libraryDb;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ConcurrentDictionary<string, IMetadataProvider> _instances = new();

        public MetadataProviderRegistry(IHttpClientFactory httpFactory, ILibraryDbService libraryDb, ILoggerFactory loggerFactory)
        {
            _httpFactory = httpFactory;
            _libraryDb = libraryDb;
            _loggerFactory = loggerFactory;
        }

        public static ProviderDescriptor? Describe(string id) =>
            Known.FirstOrDefault(k => k.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The provider entries of the configuration, one per known provider,
        /// so the settings UI and the API always see the full list.
        /// </summary>
        public static List<LibraryProviderConfig> EffectiveConfigs(GeneralConfig config)
        {
            var list = config.LibraryProviders ?? new List<LibraryProviderConfig>();
            var result = new List<LibraryProviderConfig>();
            foreach (var known in Known)
            {
                var existing = list.FirstOrDefault(p => p.Id.Equals(known.Id, StringComparison.OrdinalIgnoreCase));
                result.Add(existing ?? new LibraryProviderConfig { Id = known.Id, Enabled = false, Priority = result.Count + 1 });
            }
            return result.OrderBy(p => p.Priority).ThenBy(p => p.Id).ToList();
        }

        /// <summary>Enabled providers that have what they need to work, best first.</summary>
        public IReadOnlyList<IMetadataProvider> GetEnabled(GeneralConfig? config = null)
        {
            config ??= GeneralConfigStatic.config;
            var result = new List<IMetadataProvider>();
            foreach (var pc in EffectiveConfigs(config))
            {
                if (!pc.Enabled) continue;
                var descriptor = Describe(pc.Id);
                if (descriptor == null) continue;
                if (descriptor.RequiresKey && string.IsNullOrWhiteSpace(pc.ApiKey)) continue;
                result.Add(GetOrCreate(pc, config.LibraryLanguage));
            }
            return result;
        }

        public IReadOnlyList<IMetadataProvider> GetEnabled() => GetEnabled(null);

        public IMetadataProvider? Get(string id) => Get(id, null);

        public IMetadataProvider? Get(string id, GeneralConfig? config) =>
            GetEnabled(config).FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        private IMetadataProvider GetOrCreate(LibraryProviderConfig pc, string? language)
        {
            var key = pc.ApiKey?.Trim() ?? string.Empty;
            var lang = language?.Trim() ?? string.Empty;
            var cacheKey = $"{pc.Id}|{key}|{lang}";
            return _instances.GetOrAdd(cacheKey, _ =>
            {
                var http = _httpFactory.CreateClient("library-provider");
                IMetadataProvider inner = pc.Id.ToLowerInvariant() switch
                {
                    TmdbProvider.ProviderId => new TmdbProvider(http, key, lang, _loggerFactory.CreateLogger<TmdbProvider>()),
                    OmdbProvider.ProviderId => new OmdbProvider(http, key, _loggerFactory.CreateLogger<OmdbProvider>()),
                    _ => throw new NotSupportedException($"Unknown metadata provider '{pc.Id}'")
                };
                return new CachedMetadataProvider(inner, _libraryDb, lang);
            });
        }
    }
}
