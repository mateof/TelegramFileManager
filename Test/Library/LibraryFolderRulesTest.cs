using TelegramDownloader.Models;
using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace Test.Library
{
    /// <summary>Folder rules and folder-name hints: what a folder of a channel is expected to hold.</summary>
    public class LibraryFolderRulesTest
    {
        private static GeneralConfig Config(params (long channel, string path, string kind)[] rules) => new()
        {
            LibraryFolderRules = rules.Select(r => new LibraryFolderRule { ChannelId = r.channel, Path = r.path, Kind = r.kind }).ToList()
        };

        [TestCase("Series", "/Series/")]
        [TestCase("/Series", "/Series/")]
        [TestCase("\\Series\\Breaking Bad\\", "/Series/Breaking Bad/")]
        [TestCase("", "/")]
        [TestCase("/", "/")]
        public void PathsAreNormalized(string input, string expected)
        {
            Assert.That(LibraryFolderRules.Normalize(input), Is.EqualTo(expected));
        }

        [Test]
        public void LongestMatchingRuleWins()
        {
            var config = Config((100, "/", "movie"), (100, "/Series/", "series"), (100, "/Series/Extras/", "ignore"));
            Assert.Multiple(() =>
            {
                Assert.That(LibraryFolderRules.Resolve(config, 100, "/Cine/"), Is.EqualTo("movie"));
                Assert.That(LibraryFolderRules.Resolve(config, 100, "/Series/Breaking Bad/Temporada 1/"), Is.EqualTo("series"));
                Assert.That(LibraryFolderRules.Resolve(config, 100, "/Series/Extras/"), Is.EqualTo("ignore"));
                Assert.That(LibraryFolderRules.Resolve(config, 200, "/Series/"), Is.Null, "rules belong to a channel");
            });
        }

        [Test]
        public void RuleForTheRootCoversTheWholeChannel()
        {
            var config = Config((100, "/", "series"));
            Assert.That(LibraryFolderRules.Resolve(config, 100, "/"), Is.EqualTo("series"));
            Assert.That(LibraryFolderRules.Resolve(config, 100, "/Anything/Deep/"), Is.EqualTo("series"));
        }

        [TestCase("/Series/Dark/", "series")]
        [TestCase("/Películas/2020/", "movie")]
        [TestCase("/Peliculas/", "movie")]
        [TestCase("/TV Shows/", "series")]
        [TestCase("/Cine/Series/", "series", Description = "deepest folder name wins")]
        [TestCase("/Descargas/", null)]
        [TestCase("/", null)]
        public void FolderNamesHintTheKind(string path, string? expected)
        {
            Assert.That(LibraryFolderRules.HintFromPath(path), Is.EqualTo(expected));
        }

        [Test]
        public void ExplicitRuleBeatsTheFolderName()
        {
            var config = Config((100, "/Series/", "movie"));
            Assert.That(LibraryFolderRules.Effective(config, 100, "/Series/"), Is.EqualTo("movie"));
            Assert.That(LibraryFolderRules.Effective(config, 100, "/Peliculas/"), Is.EqualTo("movie"));
        }

        [Test]
        public void SeriesHintMakesAFileWithoutEpisodeASeries()
        {
            var p = MediaNameParser.Parse("Breaking Bad - Pilot.mkv", "/Series/Breaking Bad/", null, LibraryKind.Series);
            Assert.Multiple(() =>
            {
                Assert.That(p.IsSeries, Is.True);
                Assert.That(p.Episode, Is.Null);
            });
        }

        [Test]
        public void MovieHintDropsEpisodeMarkers()
        {
            var p = MediaNameParser.Parse("Movie 2x01 (2020).mkv", "/Cine/", null, LibraryKind.Movie);
            Assert.Multiple(() =>
            {
                Assert.That(p.IsSeries, Is.False);
                Assert.That(p.Season, Is.Null);
                Assert.That(p.Episode, Is.Null);
            });
        }
    }
}
