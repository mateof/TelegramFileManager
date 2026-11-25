using TelegramDownloader.Models;
using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Services.Library
{
    public static class LibraryRuleKind
    {
        public const string Movie = LibraryKind.Movie;
        public const string Series = LibraryKind.Series;
        public const string Ignore = "ignore";

        public static bool IsValid(string? kind) => kind == Movie || kind == Series || kind == Ignore;
    }

    /// <summary>
    /// Decides what a folder of a channel holds: an explicit rule from the
    /// configuration (longest matching path wins), else a hint taken from the
    /// folder names on the path ("Series", "Películas"...), else nothing.
    /// </summary>
    public static class LibraryFolderRules
    {
        private static readonly HashSet<string> SeriesNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "series", "serie", "tv", "tv shows", "tvshows", "shows", "temporadas"
        };

        private static readonly HashSet<string> MovieNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "peliculas", "pelicula", "pelis", "cine", "movies", "movie", "films", "film"
        };

        /// <summary>Canonical folder path: forward slashes, leading and trailing slash.</summary>
        public static string Normalize(string? path)
        {
            var p = (path ?? string.Empty).Replace('\\', '/').Trim();
            if (!p.StartsWith('/')) p = "/" + p;
            if (!p.EndsWith('/')) p += "/";
            while (p.Contains("//")) p = p.Replace("//", "/");
            return p;
        }

        /// <summary>Whether a folder is <paramref name="root"/> or is below it.</summary>
        public static bool IsWithin(string? folderPath, string? root) =>
            Normalize(folderPath).StartsWith(Normalize(root), StringComparison.OrdinalIgnoreCase);

        /// <summary>The kind the explicit rule of a folder sets, or null.</summary>
        public static string? Resolve(GeneralConfig config, long channelId, string? folderPath) =>
            ResolveRule(config, channelId, folderPath)?.Kind;

        /// <summary>The explicit rule for a folder, or null.</summary>
        public static LibraryFolderRule? ResolveRule(GeneralConfig config, long channelId, string? folderPath)
        {
            var rules = config.LibraryFolderRules;
            if (rules == null || rules.Count == 0) return null;
            var folder = Normalize(folderPath);
            LibraryFolderRule? best = null;
            foreach (var rule in rules)
            {
                if (rule.ChannelId != channelId || !LibraryRuleKind.IsValid(rule.Kind)) continue;
                var rulePath = Normalize(rule.Path);
                if (!folder.StartsWith(rulePath, StringComparison.OrdinalIgnoreCase)) continue;
                if (best == null || rulePath.Length > Normalize(best.Path).Length)
                    best = rule;
            }
            return best;
        }

        /// <summary>A kind suggested by the folder names themselves ("Series/", "Películas/"). Deepest wins.</summary>
        public static string? HintFromPath(string? folderPath)
        {
            var segments = Normalize(folderPath).Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                var name = MediaNameParser.NormalizeForMatch(segments[i]);
                if (SeriesNames.Contains(name)) return LibraryRuleKind.Series;
                if (MovieNames.Contains(name)) return LibraryRuleKind.Movie;
            }
            return null;
        }

        /// <summary>Rule first, folder-name hint second.</summary>
        public static string? Effective(GeneralConfig config, long channelId, string? folderPath) =>
            Resolve(config, channelId, folderPath) ?? HintFromPath(folderPath);

        /// <summary>The rules with the one of that exact folder replaced, or removed when there is no new one.</summary>
        public static List<LibraryFolderRule> With(IEnumerable<LibraryFolderRule>? rules, long channelId, string path, LibraryFolderRule? rule)
        {
            var normalized = Normalize(path);
            var result = (rules ?? Enumerable.Empty<LibraryFolderRule>())
                .Where(r => r.ChannelId != channelId || !Normalize(r.Path).Equals(normalized, StringComparison.OrdinalIgnoreCase)).ToList();
            if (rule != null) result.Add(rule);
            return result;
        }

        /// <summary>The rule set for exactly that folder.</summary>
        public static LibraryFolderRule? Find(IEnumerable<LibraryFolderRule>? rules, long channelId, string path)
        {
            var normalized = Normalize(path);
            return rules?.FirstOrDefault(r => r.ChannelId == channelId && Normalize(r.Path).Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }
    }
}
