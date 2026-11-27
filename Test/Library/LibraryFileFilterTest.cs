using TelegramDownloader.Models;
using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace Test.Library
{
    /// <summary>Filters of the review queue: kind, channel and folder rule, and their counts.</summary>
    public class LibraryFileFilterTest
    {
        private static readonly GeneralConfig Config = new()
        {
            LibraryFolderRules = new List<LibraryFolderRule>
            {
                new() { ChannelId = 1, Path = "/Series/", Kind = "series" },
                new() { ChannelId = 1, Path = "/Series/Extras", Kind = "ignore" },
                new() { ChannelId = 2, Path = "/", Kind = "movie", Title = null },
                new() { ChannelId = 2, Path = "/Unused/", Kind = "series" }
            }
        };

        private static LibraryFile File(string id, long channel, string folder, string? kind = null, bool seriesName = false, int? episode = null) => new()
        {
            ChannelId = channel,
            FileId = id,
            FileName = id + ".mkv",
            FolderPath = folder,
            Kind = kind,
            Parsed = new ParsedName { Title = id, IsSeries = seriesName, Episode = episode }
        };

        private static readonly List<LibraryFile> Files = new()
        {
            File("a", 1, "/Series/Show/", kind: "series"),
            File("b", 1, "/Series/Extras/", seriesName: true),
            File("c", 1, "/Otros/"),
            File("d", 2, "/Cine/", kind: "movie"),
            File("e", 2, "/Cine/Sagas/", episode: 3)
        };

        private static string[] Ids(IEnumerable<LibraryFile> files) => files.Select(f => f.FileId).ToArray();

        [Test]
        public void KindIsTheIdentifiedOneElseWhatTheNameSuggests()
        {
            Assert.Multiple(() =>
            {
                Assert.That(LibraryFileFilter.KindOf(Files[0]), Is.EqualTo("series"));
                Assert.That(LibraryFileFilter.KindOf(Files[1]), Is.EqualTo("series"), "name read as a series");
                Assert.That(LibraryFileFilter.KindOf(Files[2]), Is.EqualTo("movie"));
                Assert.That(LibraryFileFilter.KindOf(Files[4]), Is.EqualTo("series"), "an episode number");
                Assert.That(LibraryFileFilter.KindOf(File("x", 1, "/", kind: "movie", seriesName: true)), Is.EqualTo("movie"), "identification wins");
            });
        }

        [Test]
        public void FiltersByKindAndChannel()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Ids(LibraryFileFilter.Apply(Files, Config, "series", null, null)), Is.EqualTo(new[] { "a", "b", "e" }));
                Assert.That(Ids(LibraryFileFilter.Apply(Files, Config, "movie", 2, null)), Is.EqualTo(new[] { "d" }));
                Assert.That(Ids(LibraryFileFilter.Apply(Files, Config, null, null, null)), Has.Length.EqualTo(5));
            });
        }

        [Test]
        public void RuleKeepsTheFilesItDecidesNotThoseOfADeeperRule()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Ids(LibraryFileFilter.Apply(Files, Config, null, 1, "Series")), Is.EqualTo(new[] { "a" }));
                Assert.That(Ids(LibraryFileFilter.Apply(Files, Config, null, 1, "/series/extras/")), Is.EqualTo(new[] { "b" }), "paths compare like the rules do");
                Assert.That(Ids(LibraryFileFilter.Apply(Files, Config, null, 2, "/")), Is.EqualTo(new[] { "d", "e" }));
                Assert.That(Ids(LibraryFileFilter.Apply(Files, Config, null, 2, "/Series/")), Is.Empty, "a rule of another channel");
            });
        }

        [Test]
        public void FacetsCountEachKindChannelAndUsedRule()
        {
            var facets = LibraryFileFilter.Facets(Files, Config);
            Assert.Multiple(() =>
            {
                Assert.That(facets.Total, Is.EqualTo(5));
                Assert.That(facets.Kinds.Select(k => (k.Value, k.Count)), Is.EqualTo(new[] { ("movie", 2), ("series", 3) }));
                Assert.That(facets.Channels.Select(c => (c.ChannelId, c.Count)), Is.EqualTo(new[] { (1L, 3), (2L, 2) }));
                Assert.That(facets.Rules.Select(r => (r.ChannelId, r.Path, r.Kind, r.Count)),
                    Is.EqualTo(new[] { (1L, "/Series/", "series", 1), (1L, "/Series/Extras/", "ignore", 1), (2L, "/", "movie", 2) }),
                    "rules without files are left out");
            });
        }

        [Test]
        public void EachCountAppliesTheOtherFilters()
        {
            var facets = LibraryFileFilter.Facets(Files, Config, kind: "series", channelId: 1, rule: "/Series/");
            Assert.Multiple(() =>
            {
                Assert.That(facets.Total, Is.EqualTo(5));
                Assert.That(facets.Matching, Is.EqualTo(1));
                Assert.That(facets.Kinds.Select(k => (k.Value, k.Count)), Is.EqualTo(new[] { ("series", 1) }), "kinds of the channel and rule");
                Assert.That(facets.Channels.Select(c => (c.ChannelId, c.Count)), Is.EqualTo(new[] { (1L, 2), (2L, 1) }), "channels of the kind");
                Assert.That(facets.Rules.Select(r => (r.Path, r.Count)), Is.EqualTo(new[] { ("/Series/", 1), ("/Series/Extras/", 1), ("/", 1) }), "rules of the kind, every channel");
            });
        }
    }
}
