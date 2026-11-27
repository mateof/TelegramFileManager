using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Services.Library
{
    /// <summary>
    /// Filters of the library file lists (the review queue): kind, channel and
    /// folder rule, and the counts of each value to offer them as filters.
    /// </summary>
    public static class LibraryFileFilter
    {
        /// <summary>
        /// Series or movie: what the file was identified as, else what its name
        /// looks like (an episode number makes it a series).
        /// </summary>
        public static string KindOf(LibraryFile file) =>
            file.Kind == LibraryKind.Series || file.Kind == LibraryKind.Movie
                ? file.Kind
                : file.Parsed.IsSeries || file.Parsed.Episode.HasValue ? LibraryKind.Series : LibraryKind.Movie;

        /// <summary>The folder rule that decides the file (the most specific one of its channel), or null.</summary>
        public static LibraryFolderRule? RuleOf(GeneralConfig config, LibraryFile file) =>
            LibraryFolderRules.ResolveRule(config, file.ChannelId, file.FolderPath);

        /// <summary>
        /// The files of <paramref name="kind"/> (series or movie), of the channel and, with
        /// <paramref name="rule"/> (the path of a rule of that channel), decided by that rule.
        /// Null filters keep everything.
        /// </summary>
        public static IEnumerable<LibraryFile> Apply(IEnumerable<LibraryFile> files, GeneralConfig config, string? kind, long? channelId, string? rule)
        {
            if (!string.IsNullOrWhiteSpace(kind)) files = files.Where(f => KindOf(f) == kind);
            if (channelId.HasValue) files = files.Where(f => f.ChannelId == channelId.Value);
            if (channelId.HasValue && !string.IsNullOrWhiteSpace(rule))
            {
                var path = LibraryFolderRules.Normalize(rule);
                files = files.Where(f => RuleOf(config, f) is { } r && LibraryFolderRules.Normalize(r.Path).Equals(path, StringComparison.OrdinalIgnoreCase));
            }
            return files;
        }

        /// <summary>
        /// How many of the files there are of each kind, channel and folder rule. Each count
        /// applies the other filters, not its own: the kinds of the chosen channel and rule,
        /// the channels and rules of the chosen kind, so every count is what picking it shows.
        /// </summary>
        public static ApiLibraryFileFacetsDto Facets(IReadOnlyCollection<LibraryFile> files, GeneralConfig config,
            string? kind = null, long? channelId = null, string? rule = null)
        {
            var ofKinds = Apply(files, config, null, channelId, rule).ToList();
            var ofKind = Apply(files, config, kind, null, null).ToList();
            var rules = new Dictionary<(long, string), (LibraryFolderRule Rule, int Count)>();
            foreach (var file in ofKind)
            {
                if (RuleOf(config, file) is not { } decides) continue;
                var key = (decides.ChannelId, LibraryFolderRules.Normalize(decides.Path).ToLowerInvariant());
                rules[key] = rules.TryGetValue(key, out var seen) ? (seen.Rule, seen.Count + 1) : (decides, 1);
            }
            return new ApiLibraryFileFacetsDto
            {
                Total = files.Count,
                Matching = Apply(files, config, kind, channelId, rule).Count(),
                Kinds = ofKinds.GroupBy(KindOf).OrderBy(g => g.Key)
                    .Select(g => new ApiLibraryFacetCountDto { Value = g.Key, Count = g.Count() }).ToList(),
                Channels = ofKind.GroupBy(f => f.ChannelId).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                    .Select(g => new ApiLibraryChannelFacetDto { ChannelId = g.Key, Count = g.Count() }).ToList(),
                Rules = rules.Values.OrderBy(r => r.Rule.ChannelId).ThenBy(r => LibraryFolderRules.Normalize(r.Rule.Path), StringComparer.OrdinalIgnoreCase)
                    .Select(r => new ApiLibraryRuleFacetDto
                    {
                        ChannelId = r.Rule.ChannelId,
                        Path = LibraryFolderRules.Normalize(r.Rule.Path),
                        Kind = r.Rule.Kind,
                        Title = r.Rule.Title,
                        Count = r.Count
                    }).ToList()
            };
        }
    }
}
