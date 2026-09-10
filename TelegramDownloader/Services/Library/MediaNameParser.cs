using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Services.Library
{
    /// <summary>
    /// Turns a release-style file name ("Serie.S01E02.1080p.Castellano.mkv",
    /// "Pelicula (2019) [BluRay].mkv", "Cap.102") plus its folder into a title,
    /// year and season/episode. Pure and deterministic so it can be unit tested
    /// against a dump of real channel names.
    /// </summary>
    public static class MediaNameParser
    {
        private static readonly RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        // Episode markers, most specific first.
        private static readonly Regex SxxExx = new(@"(?<![a-z0-9])S(?<s>\d{1,2})\s*[ .\-_]?\s*E(?<e>\d{1,3})(?:\s*[-–]?\s*E?(?<e2>\d{1,3}))?(?![a-z0-9])", Opts);
        private static readonly Regex NxNN = new(@"(?<![a-z0-9])(?<s>\d{1,2})x(?<e>\d{2,3})(?:\s*[-–]\s*(?<e2>\d{2,3}))?(?![a-z0-9])", Opts);
        private static readonly Regex Capitulo = new(@"(?<![a-z0-9])Cap(?:[ií]tulo)?\.?\s*(?<n>\d{3,4})(?![a-z0-9])", Opts);
        private static readonly Regex SeasonWord = new(@"(?<![a-z0-9])(?:Temporada|Season|Saison|Stagione|T)\s*\.?\s*(?<s>\d{1,2})(?![a-z0-9])", Opts);
        private static readonly Regex EpisodeWord = new(@"(?<![a-z0-9])(?:Episodio|Episode|Cap[ií]tulo|Cap|Ep|E)\s*\.?\s*(?<e>\d{1,3})(?![a-z0-9])", Opts);
        private static readonly Regex SeasonFolder = new(@"^\s*(?:Temporada|Season|Saison|Stagione|T|S)\s*\.?\s*(?<s>\d{1,2})\s*$", Opts);

        private static readonly Regex YearRx = new(@"(?<![0-9])(?<y>(?:19|20)\d{2})(?![0-9])", Opts);
        private static readonly Regex BracketedYear = new(@"[\(\[]\s*(?<y>(?:19|20)\d{2})\s*[\)\]]", Opts);
        private static readonly Regex TitleYearFolder = new(@"^(?<t>.+?)\s*[\(\[]\s*(?<y>(?:19|20)\d{2})\s*[\)\]]\s*$", Opts);

        private static readonly Regex QualityMarker = new(
            @"(?<![a-z0-9])(?:" +
            @"2160p|1080p|1080i|720p|576p|480p|4k|uhd|hdr10?\+?|dolby\s*vision|dv|" +
            @"x264|x265|h\.?264|h\.?265|hevc|avc|xvid|divx|av1|10bit|8bit|" +
            @"web-?dl|webrip|bluray|blu-ray|bdrip|brrip|bdremux|remux|hdtv|dvdrip|dvdscr|hdrip|screener|telesync|" +
            @"castellano|latino|spanish|espa[ñn]ol|english|dual|multi|vose|vos|subs?|subtitulado|" +
            @"ac3|aac|dts(?:-?hd)?|truehd|atmos|eac3|ddp?5\.1|5\.1|7\.1|" +
            @"proper|repack|extended|unrated|remastered|directors?\.?cut|complete|" +
            @"www\.[a-z0-9.-]+" +
            @")(?![a-z0-9])", Opts);

        private static readonly Regex Brackets = new(@"[\[\{][^\]\}]*[\]\}]", Opts);
        private static readonly Regex ReleaseGroup = new(@"-\s*[A-Za-z0-9]+\s*$", Opts);
        private static readonly Regex DuplicateSuffix = new(@"\(\d+\)\s*$", Opts);
        private static readonly Regex Junk = new(@"^(?:video|vid|vídeo|movie|film|file|document|doc|telegram|img|mov|clip|untitled|\d+)[\s_\-\d]*$", Opts);
        private static readonly Regex Spaces = new(@"\s+", Opts);
        private static readonly Regex Articles = new(@"^(?:the|a|an|el|la|los|las|un|una|unos|unas|le|les|der|die|das|il|lo|gli)\s+", Opts);

        /// <param name="kindHint"><c>series</c> or <c>movie</c> when the folder rules already know what this is.</param>
        public static ParsedName Parse(string fileName, string? folderPath = null, string? caption = null, string? kindHint = null)
        {
            var result = ParseInner(fileName, folderPath, caption);
            if (kindHint == LibraryKind.Series)
            {
                result.IsSeries = true;
            }
            else if (kindHint == LibraryKind.Movie)
            {
                result.IsSeries = false;
                result.Season = null;
                result.Episode = null;
                result.EpisodeEnd = null;
            }
            return result;
        }

        private static ParsedName ParseInner(string fileName, string? folderPath, string? caption)
        {
            var result = ParseName(StripExtension(fileName ?? string.Empty), "file");

            var folders = SplitFolders(folderPath);
            ApplyFolderHints(result, folders);

            if (!result.HasTitle || IsJunk(result.Title))
            {
                var fromFolder = TitleFromFolders(folders, result);
                if (fromFolder != null)
                {
                    // A junk name's "year" is usually a date stamp: the folder decides
                    result.Title = fromFolder.Title;
                    result.Year = fromFolder.Year;
                    result.Source = "folder";
                }
                else if (!string.IsNullOrWhiteSpace(caption))
                {
                    var fromCaption = ParseName(FirstLine(caption), "caption");
                    if (fromCaption.HasTitle && !IsJunk(fromCaption.Title))
                    {
                        result.Title = fromCaption.Title;
                        result.Year ??= fromCaption.Year;
                        result.Season ??= fromCaption.Season;
                        result.Episode ??= fromCaption.Episode;
                        result.IsSeries |= fromCaption.IsSeries;
                        result.Source = "caption";
                    }
                }
            }

            if (result.Episode.HasValue)
            {
                result.IsSeries = true;
                result.Season ??= 1;
            }
            return result;
        }

        /// <summary>Parses a bare name (no extension, no folder context).</summary>
        public static ParsedName ParseName(string name, string source = "file")
        {
            var p = new ParsedName { Source = source };
            if (string.IsNullOrWhiteSpace(name)) return p;

            var text = Normalize(name);
            int cut = text.Length;

            // Episode markers
            var m = SxxExx.Match(text);
            if (!m.Success) m = NxNN.Match(text);
            if (m.Success)
            {
                p.Season = int.Parse(m.Groups["s"].Value);
                p.Episode = int.Parse(m.Groups["e"].Value);
                if (m.Groups["e2"].Success) p.EpisodeEnd = int.Parse(m.Groups["e2"].Value);
                cut = Math.Min(cut, m.Index);
            }
            else
            {
                m = Capitulo.Match(text);
                if (m.Success)
                {
                    // Spanish releases: "Cap.102" = season 1 episode 02, "Cap.1012" = season 10 episode 12
                    var n = m.Groups["n"].Value;
                    p.Season = int.Parse(n[..^2]);
                    p.Episode = int.Parse(n[^2..]);
                    cut = Math.Min(cut, m.Index);
                }
                else
                {
                    var sm = SeasonWord.Match(text);
                    var em = EpisodeWord.Match(text);
                    if (em.Success && em.Index > 0)
                    {
                        p.Episode = int.Parse(em.Groups["e"].Value);
                        cut = Math.Min(cut, em.Index);
                    }
                    if (sm.Success && sm.Index > 0 && (em.Success || LooksLikeSeasonOnly(text, sm)))
                    {
                        p.Season = int.Parse(sm.Groups["s"].Value);
                        cut = Math.Min(cut, sm.Index);
                        p.IsSeries = true;
                    }
                }
            }

            // Year: a bracketed one wins ("Blade Runner 2049 (2017)"); otherwise the
            // first plausible one that is not the very first token
            var maxYear = DateTime.UtcNow.Year + 1;
            var bracketed = BracketedYear.Match(text);
            if (bracketed.Success && bracketed.Index > 0 && int.Parse(bracketed.Groups["y"].Value) <= maxYear)
            {
                p.Year = int.Parse(bracketed.Groups["y"].Value);
                cut = Math.Min(cut, bracketed.Index);
            }
            else
            {
                foreach (Match ym in YearRx.Matches(text))
                {
                    if (ym.Index == 0) continue;
                    var year = int.Parse(ym.Groups["y"].Value);
                    if (year > maxYear) continue;
                    var before = text[..ym.Index].Trim(' ', '(', '[', '-', '.');
                    if (before.Length == 0) continue;
                    p.Year = year;
                    cut = Math.Min(cut, ym.Index);
                    break;
                }
            }

            // Quality / language markers end the title
            var qm = QualityMarker.Match(text);
            while (qm.Success && qm.Index == 0) qm = qm.NextMatch();
            if (qm.Success) cut = Math.Min(cut, qm.Index);

            var title = text[..cut];
            title = Brackets.Replace(title, " ");
            title = title.Trim().Trim('(', '[', '{', '-', '–', ':', '.', ',', '_', ' ');
            if (title.Contains(' ') || title.Contains('-'))
                title = ReleaseGroup.Replace(title, string.Empty);
            title = Spaces.Replace(title, " ").Trim();

            if (title.Length == 0)
            {
                // "2012 (2009).mkv": the year we cut on was actually the title
                var first = text.Trim().Split(' ', 2)[0];
                if (YearRx.IsMatch(first) && p.Year.HasValue && int.Parse(first) != p.Year) title = first;
                else if (YearRx.IsMatch(first)) { title = first; p.Year = null; }
            }

            p.Title = title;
            return p;
        }

        /// <summary>Lower case, no diacritics, no punctuation, no leading article. What the matcher compares.</summary>
        public static string NormalizeForMatch(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;
            var s = RemoveDiacritics(title.ToLowerInvariant());
            s = s.Replace('&', ' ').Replace("'", string.Empty).Replace("’", string.Empty);
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            s = Spaces.Replace(sb.ToString(), " ").Trim();
            s = Articles.Replace(s, string.Empty);
            return s;
        }

        public static string SortTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;
            return Articles.Replace(title.Trim(), string.Empty).ToLowerInvariant();
        }

        public static bool IsJunk(string? title) =>
            string.IsNullOrWhiteSpace(title) || title.Trim().Length < 2 || Junk.IsMatch(title.Trim());

        // ----- helpers -----

        private static readonly HashSet<string> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "mkv", "mp4", "avi", "mov", "wmv", "flv", "webm", "m4v", "ts", "mpg", "mpeg", "m2ts", "3gp", "ogv", "divx", "iso", "vob", "srt"
        };

        private static string StripExtension(string fileName)
        {
            var name = DuplicateSuffix.Replace(fileName.Trim(), string.Empty).Trim();
            var dot = name.LastIndexOf('.');
            // Only a real extension is dropped: "Serie Cap.102" keeps its episode number
            if (dot > 0 && KnownExtensions.Contains(name[(dot + 1)..].Trim()))
                name = name[..dot];
            return name;
        }

        private static string Normalize(string name)
        {
            var text = name.Replace('_', ' ');
            // Dot-separated release names: "Some.Movie.2019.1080p"
            if (!text.Contains(' '))
                text = text.Replace('.', ' ');
            else
                text = Regex.Replace(text, @"\.(?=\S)", ". ");
            return Spaces.Replace(text, " ").Trim();
        }

        private static bool LooksLikeSeasonOnly(string text, Match seasonMatch)
        {
            // "Serie Temporada 2" with nothing usable after: a season pack name
            var rest = text[(seasonMatch.Index + seasonMatch.Length)..].Trim();
            return rest.Length == 0 || QualityMarker.IsMatch(rest);
        }

        private static List<string> SplitFolders(string? folderPath) =>
            (folderPath ?? string.Empty).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0 && !s.Equals("Files", StringComparison.OrdinalIgnoreCase)).ToList();

        private static void ApplyFolderHints(ParsedName p, List<string> folders)
        {
            if (folders.Count == 0) return;
            var last = folders[^1];
            var sf = SeasonFolder.Match(last);
            if (sf.Success)
            {
                p.IsSeries = true;
                if (p.Episode.HasValue && (!p.Season.HasValue || p.Season == 1))
                    p.Season = int.Parse(sf.Groups["s"].Value);
                if (folders.Count >= 2)
                {
                    var series = ParseName(folders[^2], "folder");
                    // "1x02.mkv" inside "Serie/Temporada 1": the folder is the only source of the title
                    if (!p.HasTitle || IsJunk(p.Title) || p.Source == "file" && SameTitle(p.Title, series.Title) == false && p.Title.Length <= 3)
                    {
                        p.Title = series.Title;
                        p.Year ??= series.Year;
                        p.Source = "folder";
                    }
                }
                return;
            }

            // "Title (Year)" folder holding the file: adopt the year when the titles agree
            var ty = TitleYearFolder.Match(last);
            if (ty.Success && !p.Year.HasValue && SameTitle(p.Title, ty.Groups["t"].Value) == true)
                p.Year = int.Parse(ty.Groups["y"].Value);
        }

        private static ParsedName? TitleFromFolders(List<string> folders, ParsedName current)
        {
            for (int i = folders.Count - 1; i >= 0; i--)
            {
                if (SeasonFolder.IsMatch(folders[i])) continue;
                var parsed = ParseName(folders[i], "folder");
                if (parsed.HasTitle && !IsJunk(parsed.Title))
                {
                    if (parsed.Season.HasValue && !current.Season.HasValue) current.Season = parsed.Season;
                    return parsed;
                }
            }
            return null;
        }

        private static bool? SameTitle(string? a, string? b)
        {
            var na = NormalizeForMatch(a);
            var nb = NormalizeForMatch(b);
            if (na.Length == 0 || nb.Length == 0) return null;
            return na == nb || na.StartsWith(nb + " ") || nb.StartsWith(na + " ");
        }

        private static string FirstLine(string caption)
        {
            var line = caption.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
            // Captions often carry hashtags and emoji; keep the readable part
            line = Regex.Replace(line, @"#\S+", " ");
            line = Regex.Replace(line, @"[\p{So}\p{Cs}]", " ");
            return Spaces.Replace(line, " ").Trim();
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
