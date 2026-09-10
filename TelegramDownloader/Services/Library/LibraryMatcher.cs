using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Services.Library
{
    public class MatchResult
    {
        public ProviderCandidate Candidate { get; set; } = new();
        public double Score { get; set; }
        public string Status => LibraryMatcher.StatusFor(Score);
    }

    /// <summary>
    /// Scores provider candidates against a parsed name. Pure: no I/O.
    /// </summary>
    public static class LibraryMatcher
    {
        public const double MatchedThreshold = 0.85;
        public const double ReviewThreshold = 0.60;

        public static string StatusFor(double score) =>
            score >= MatchedThreshold ? LibraryFileStatus.Matched :
            score >= ReviewThreshold ? LibraryFileStatus.Review :
            LibraryFileStatus.Unmatched;

        public static MatchResult? Best(ParsedName parsed, IReadOnlyList<ProviderCandidate> candidates)
        {
            MatchResult? best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                var score = Score(parsed, candidates[i]);
                // Providers return results by popularity: earlier wins ties
                score += 0.01 * (1.0 - (double)i / Math.Max(1, candidates.Count));
                if (best == null || score > best.Score)
                    best = new MatchResult { Candidate = candidates[i], Score = Math.Min(1.0, score) };
            }
            return best;
        }

        public static double Score(ParsedName parsed, ProviderCandidate c)
        {
            var wanted = MediaNameParser.NormalizeForMatch(parsed.Title);
            if (wanted.Length == 0) return 0;

            var sim = Similarity(wanted, MediaNameParser.NormalizeForMatch(c.Title));
            if (!string.IsNullOrEmpty(c.OriginalTitle))
                sim = Math.Max(sim, Similarity(wanted, MediaNameParser.NormalizeForMatch(c.OriginalTitle)));

            double yearFactor;
            if (parsed.Year.HasValue && c.Year.HasValue)
            {
                var diff = Math.Abs(parsed.Year.Value - c.Year.Value);
                yearFactor = diff == 0 ? 1.0 : diff == 1 ? 0.92 : 0.5;
            }
            else if (!parsed.Year.HasValue)
                yearFactor = 0.9;
            else
                yearFactor = 0.85;

            return sim * yearFactor;
        }

        /// <summary>
        /// Jaro-Winkler similarity, plus a token containment bonus so
        /// "avatar" vs "avatar the way of water" is not treated as an equal.
        /// </summary>
        public static double Similarity(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            if (a == b) return 1;

            var jw = JaroWinkler(a, b);

            var ta = a.Split(' ');
            var tb = b.Split(' ');
            var common = ta.Intersect(tb).Count();
            var tokenScore = (double)common / Math.Max(ta.Length, tb.Length);

            return Math.Max(jw * 0.85, Math.Max(jw, tokenScore) * (0.7 + 0.3 * tokenScore));
        }

        private static double JaroWinkler(string s1, string s2)
        {
            var jaro = Jaro(s1, s2);
            int prefix = 0;
            for (int i = 0; i < Math.Min(4, Math.Min(s1.Length, s2.Length)); i++)
            {
                if (s1[i] == s2[i]) prefix++;
                else break;
            }
            return jaro + prefix * 0.1 * (1 - jaro);
        }

        private static double Jaro(string s1, string s2)
        {
            if (s1 == s2) return 1;
            int len1 = s1.Length, len2 = s2.Length;
            if (len1 == 0 || len2 == 0) return 0;
            int window = Math.Max(0, Math.Max(len1, len2) / 2 - 1);
            var m1 = new bool[len1];
            var m2 = new bool[len2];
            int matches = 0;
            for (int i = 0; i < len1; i++)
            {
                int start = Math.Max(0, i - window), end = Math.Min(i + window + 1, len2);
                for (int j = start; j < end; j++)
                {
                    if (m2[j] || s1[i] != s2[j]) continue;
                    m1[i] = m2[j] = true;
                    matches++;
                    break;
                }
            }
            if (matches == 0) return 0;
            int t = 0, k = 0;
            for (int i = 0; i < len1; i++)
            {
                if (!m1[i]) continue;
                while (!m2[k]) k++;
                if (s1[i] != s2[k]) t++;
                k++;
            }
            double m = matches;
            return (m / len1 + m / len2 + (m - t / 2.0) / m) / 3.0;
        }
    }
}
