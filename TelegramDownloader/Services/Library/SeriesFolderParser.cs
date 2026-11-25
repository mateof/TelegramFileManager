using System.Text;
using System.Text.RegularExpressions;
using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Services.Library
{
    /// <summary>What the name and folder of a file say about its episode.</summary>
    /// <param name="Exact">Season and episode were both written in the name (<c>1x02</c>).</param>
    /// <param name="Joined">The number may hold the season too (<c>Cap.102</c>).</param>
    public sealed record EpisodeRead(int? Season, int? Number, int? End = null, bool Exact = false, bool Joined = false)
    {
        /// <summary>A number with nothing that tells its season.</summary>
        public bool IsLoose => Number.HasValue && !Season.HasValue && !Exact;
    }

    /// <summary>
    /// Season and episode of the files of a folder assigned to one series. The
    /// title is known, so it accepts names the general parser cannot trust:
    /// <c>01 - Piloto.mkv</c>, <c>102.mkv</c>, <c>Serie 05.mkv</c>, season
    /// folders like <c>2ª temporada</c> or just <c>2</c>, and numbers that run
    /// on from one season to the next. Pure.
    /// </summary>
    public static class SeriesFolderParser
    {
        private static readonly RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private const string SeasonWords = "temporadas?|tempada|temp|season|saison|stagione|staffel";

        private static readonly Regex SxxExx = new(@"(?<![a-z0-9])s(?<s>\d{1,2})\s*[ .\-_]?\s*e(?:p\.?)?\s*(?<e>\d{1,4})(?:\s*-?\s*e(?<e2>\d{1,4})|\s*-\s*(?<e2>\d{1,4}))?(?![a-z0-9])", Opts);
        private static readonly Regex NxNN = new(@"(?<![a-z0-9])(?<s>\d{1,2})x(?<e>\d{1,3})(?:\s*-\s*(?:\d{1,2}x)?(?<e2>\d{1,3}))?(?![a-z0-9])", Opts);
        // "Temporada 2", "2a temporada", "2nd season", "S02", "T2"
        private static readonly Regex SeasonAfterWord = new(@"(?<![a-z0-9])(?:" + SeasonWords + @")(?:\s*[.#]?\s*|-)(?<s>\d{1,2})(?![a-z0-9])", Opts);
        private static readonly Regex SeasonOrdinal = new(@"(?<![a-z0-9])(?<s>\d{1,2})\s*(?:a|o|st|nd|rd|th)\s+(?:" + SeasonWords + @")(?![a-z0-9])", Opts);
        private static readonly Regex SeasonBeforeWord = new(@"(?<![a-z0-9])(?<s>\d{1,2})\s*(?:" + SeasonWords + @")(?![a-z0-9])", Opts);
        private static readonly Regex SeasonShort = new(@"(?<![a-z0-9])[st](?<s>\d{1,2})(?![a-z0-9])", Opts);
        private static readonly Regex BareNumber = new(@"^\s*0*(?<s>\d{1,2})\s*$", Opts);
        private static readonly Regex Specials = new(@"^\s*(?:especial(?:es)?|specials?|ovas?)\s*$", Opts);

        private static readonly Regex EpisodeWord = new(@"(?<![a-z0-9])(?<w>episodios?|episodes?|capitulos?|caps?|cp|chapter|folge|ep|e)\s*[.\-:#]?\s*(?<e>\d{1,4})(?:v\d)?(?:\s*(?:-|y|&)\s*(?:e|ep|cap)?\.?\s*(?<e2>\d{1,4}))?(?![a-z0-9])", Opts);
        private static readonly Regex HashNumber = new(@"#\s*(?<e>\d{1,4})(?![a-z0-9])", Opts);
        private static readonly Regex LeadingNumber = new(@"^\s*(?<e>\d{1,4})(?:v\d)?(?:\s*-\s*(?<e2>\d{1,4}))?(?![a-z0-9])", Opts);
        private static readonly Regex DashNumber = new(@"-\s*(?<e>\d{1,4})(?:v\d)?(?:\s*-\s*(?<e2>\d{1,4}))?(?![a-z0-9])", Opts);
        private static readonly Regex AnyNumber = new(@"(?<![a-z0-9])(?<e>\d{1,4})(?:v\d)?(?![a-z0-9])", Opts);

        // Numbers that are never an episode
        private static readonly Regex Dates = new(@"(?<!\d)(?:(?:19|20)\d{2}[ .\-_]\d{1,2}[ .\-_]\d{1,2}|\d{1,2}[ .\-_]\d{1,2}[ .\-_](?:19|20)\d{2})(?!\d)", Opts);
        private static readonly Regex Years = new(@"(?<!\d)(?:19|20)\d{2}(?!\d)", Opts);
        private static readonly Regex Measures = new(@"(?<![a-z0-9])\d+(?:[.,]\d+)?\s*(?:mb|gb|kbps|mbps|fps|hz|khz|bits?|ch)(?![a-z0-9])|(?<!\d)\d{3,4}\s*x\s*\d{3,4}(?!\d)|(?<!\d)[257][.]1(?!\d)", Opts);
        private static readonly Regex Brackets = new(@"[\[\{][^\]\}]*[\]\}]", Opts);
        private static readonly Regex Spaces = new(@"\s+", Opts);

        /// <param name="rulePath">The folder assigned to the series: what is above it says nothing.</param>
        /// <param name="titles">Titles of the series, so a number in them is not taken for an episode.</param>
        public static EpisodeRead Read(string fileName, string? folderPath, string rulePath, IEnumerable<string?> titles)
        {
            var titleList = titles.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
            var (own, below) = Folders(folderPath, rulePath);
            var season = SeasonOfFolders(own, below, titleList);

            var read = ReadName(Plain(MediaNameParser.StripExtension(fileName ?? string.Empty)), titleList, markedOnly: false);
            if (read.Number == null)
            {
                // One folder per episode: "Serie S01E03 1080p/video.mkv"
                for (int i = below.Count - 1; i >= 0 && read.Number == null; i--)
                {
                    var folder = ReadName(Plain(below[i]), titleList, markedOnly: true);
                    if (folder.Number != null) read = folder;
                }
            }
            if (read.Exact)
            {
                // "1x05" inside "Temporada 2": the folder knows better
                return read.Season == 1 && season > 1 ? read with { Season = season } : read;
            }
            return read with { Season = read.Season ?? season };
        }

        /// <summary>
        /// The episode of the series a read points to. Episode counts, when the
        /// provider has them, tell <c>102</c> = 1x02 from episode 102 of a
        /// series numbered without seasons.
        /// </summary>
        /// <param name="continuous">The files of the folder are numbered from 1 on, across seasons.</param>
        public static (int Season, int? Episode) Map(EpisodeRead read, IReadOnlyList<LibrarySeason> seasons, bool continuous)
        {
            if (read.Number is not int n) return (read.Season ?? 1, null);
            if (read.Exact) return (read.Season ?? 1, n);

            var regular = seasons.Where(s => s.Number > 0).OrderBy(s => s.Number).ToList();
            int? Count(int season) => regular.FirstOrDefault(s => s.Number == season)?.EpisodeCount is int c && c > 0 ? c : null;
            bool Known(int season) => regular.Any(s => s.Number == season);

            if (read.Season is int known)
            {
                if (known == 0) return (0, n);
                var count = Count(known);
                if (n <= (count ?? 99)) return (known, n);
                if (n >= 100 && n / 100 == known && n % 100 > 0) return (known, n % 100);
                // Numbered on from the season before
                var before = regular.Where(s => s.Number < known).ToList();
                if (count != null && before.Count == known - 1 && before.All(s => s.EpisodeCount > 0))
                {
                    var rest = n - before.Sum(s => s.EpisodeCount!.Value);
                    if (rest >= 1 && rest <= count) return (known, rest);
                }
                return (known, n);
            }

            var (splitSeason, splitEpisode) = (n / 100, n % 100);
            var canSplit = n >= 100 && splitEpisode > 0 && !continuous
                && (regular.Count == 0 ? read.Joined : Known(splitSeason) && splitEpisode <= (Count(splitSeason) ?? 99));
            if (canSplit && read.Joined) return (splitSeason, splitEpisode);
            if (Count(1) is int first && n <= first) return (1, n);
            if (canSplit) return (splitSeason, splitEpisode);

            var left = n;
            foreach (var s in regular)
            {
                if (s.EpisodeCount is not > 0) break;
                if (left <= s.EpisodeCount) return (s.Number, left);
                left -= s.EpisodeCount.Value;
            }
            // Of a season newer than the metadata, or no counts at all
            return n >= 100 && splitEpisode > 0 && !continuous ? (splitSeason, splitEpisode) : (1, n);
        }

        // ----- folders -----

        /// <summary>The last segment of the assigned folder, and the segments below it.</summary>
        private static (string? Own, List<string> Below) Folders(string? folderPath, string rulePath)
        {
            static string[] Split(string? p) => LibraryFolderRules.Normalize(p).Split('/', StringSplitOptions.RemoveEmptyEntries);
            var rule = Split(rulePath);
            return (rule.Length == 0 ? null : rule[^1], Split(folderPath).Skip(rule.Length).ToList());
        }

        private static int? SeasonOfFolders(string? own, List<string> below, List<string?> titles)
        {
            for (int i = below.Count - 1; i >= 0; i--)
                if (SeasonOfFolder(below[i], titles, allowBare: true) is int season)
                    return season;
            return own == null ? null : SeasonOfFolder(own, titles, allowBare: false);
        }

        private static int? SeasonOfFolder(string name, List<string?> titles, bool allowBare)
        {
            var text = Plain(name);
            if (Specials.IsMatch(text)) return 0;
            var marked = SxxExx.Match(text);
            if (marked.Success) return int.Parse(marked.Groups["s"].Value);
            return SeasonIn(WithoutTitles(text, titles), out _)
                ?? (allowBare && BareNumber.Match(text) is { Success: true } bare ? int.Parse(bare.Groups["s"].Value) : null);
        }

        private static int? SeasonIn(string text, out string rest)
        {
            foreach (var rx in new[] { SeasonOrdinal, SeasonAfterWord, SeasonBeforeWord, SeasonShort })
            {
                var m = rx.Match(text);
                if (!m.Success) continue;
                rest = text.Remove(m.Index, m.Length).Insert(m.Index, " - ");
                return int.Parse(m.Groups["s"].Value);
            }
            rest = text;
            return null;
        }

        // ----- names -----

        private static EpisodeRead ReadName(string text, List<string?> titles, bool markedOnly)
        {
            var m = SxxExx.Match(text);
            if (!m.Success) m = NxNN.Match(text);
            if (m.Success)
            {
                var episode = Int(m, "e");
                return new EpisodeRead(Int(m, "s"), episode, End(m, episode), Exact: true);
            }

            // The title may hold numbers ("Los 100 - 03"); but "24.mkv" is an episode, whatever the series
            var read = ReadNumber(WithoutTitles(text, titles), markedOnly);
            return read.Number == null ? ReadNumber(text, markedOnly) : read;
        }

        private static EpisodeRead ReadNumber(string text, bool markedOnly)
        {
            text = Dates.Replace(text, " ");
            text = MediaNameParser.QualityMarker.Replace(Measures.Replace(text, " "), " ");
            var season = SeasonIn(text, out text);

            var m = EpisodeWord.Match(text);
            if (m.Success)
            {
                var n = Int(m, "e");
                // "Cap.102" of the Spanish releases
                var joined = n >= 100 && season == null && m.Groups["w"].Value.StartsWith("cap", StringComparison.OrdinalIgnoreCase);
                return new EpisodeRead(season, n, End(m, n), Joined: joined);
            }
            if (markedOnly) return new EpisodeRead(season, null);

            text = Years.Replace(text, " ");
            foreach (var candidate in new[] { Brackets.Replace(text, " "), text.Replace('[', ' ').Replace(']', ' ') })
            {
                var name = Spaces.Replace(candidate, " ").Trim();
                foreach (var rx in new[] { HashNumber, LeadingNumber, DashNumber, AnyNumber })
                {
                    m = rx.Match(name);
                    if (!m.Success) continue;
                    var n = Int(m, "e");
                    return new EpisodeRead(season, n, End(m, n));
                }
            }
            return new EpisodeRead(season, null);
        }

        private static int Int(Match m, string group) => int.Parse(m.Groups[group].Value);

        /// <summary>Last episode of a file with several. A far number is something else.</summary>
        private static int? End(Match m, int first) =>
            m.Groups["e2"].Success && Int(m, "e2") is var end && end > first && end - first <= 5 ? end : null;

        /// <summary>Lower case, no accents, dots and underscores as spaces.</summary>
        private static string Plain(string name)
        {
            var text = name.Normalize(NormalizationForm.FormKC);
            text = MediaNameParser.RemoveDiacritics(text.ToLowerInvariant()).Replace('–', '-').Replace('—', '-');
            text = text.Replace('_', ' ');
            // "5.1" and "1.5gb" keep their dot until they are dropped
            text = Regex.Replace(text, @"\.(?!\d(?!\d))", " ");
            text = Measures.Replace(text, " ").Replace('.', ' ');
            return Spaces.Replace(text, " ").Trim();
        }

        private static string WithoutTitles(string text, List<string?> titles)
        {
            foreach (var title in titles.OrderByDescending(t => t!.Length))
            {
                var words = Regex.Split(Plain(title!), "[^a-z0-9]+", Opts).Where(w => w.Length > 0).Select(Regex.Escape).ToList();
                if (words.Count == 0) continue;
                var rx = new Regex(@"(?<![a-z0-9])" + string.Join("[^a-z0-9]*", words) + @"(?![a-z0-9])", Opts);
                var m = rx.Match(text);
                if (!m.Success) continue;
                text = text.Remove(m.Index, m.Length).Insert(m.Index, m.Index == 0 ? " " : " - ");
                break;
            }
            return text;
        }
    }
}
