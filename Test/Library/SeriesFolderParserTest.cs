using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace Test.Library
{
    /// <summary>Season and episode of the files of a folder assigned to one series.</summary>
    public class SeriesFolderParserTest
    {
        private const string Rule = "/Series/Dark/";

        private static List<LibrarySeason> Seasons(params int?[] counts) =>
            counts.Select((c, i) => new LibrarySeason { Number = i + 1, EpisodeCount = c }).ToList();

        private static (int Season, int? Episode) Read(string name, string folder, IReadOnlyList<LibrarySeason>? seasons = null, string rule = Rule, string title = "Dark", bool continuous = false) =>
            SeriesFolderParser.Map(SeriesFolderParser.Read(name, folder, rule, new[] { title }), seasons ?? Seasons(10, 8, 8), continuous);

        // Names that say season and episode
        [TestCase("Dark.S02E03.1080p.mkv", 2, 3)]
        [TestCase("Dark S2 E3.mkv", 2, 3)]
        [TestCase("2x03.mkv", 2, 3)]
        [TestCase("Dark 2x3 [720p].mkv", 2, 3)]
        [TestCase("Dark - Temporada 2 - Capítulo 3.mkv", 2, 3)]
        [TestCase("Dark T2 Cap 03.mkv", 2, 3)]
        [TestCase("2ª Temporada - 03 - Fantasmas.mkv", 2, 3)]
        [TestCase("Dark Cap.203.mkv", 2, 3)]
        [TestCase("Dark.HDTV.720p.Cap.203.AC3.5.1.Castellano.www.pagina.com.mkv", 2, 3)]
        // Only a number: first season
        [TestCase("01 - Secretos.mkv", 1, 1)]
        [TestCase("01. Secretos.mkv", 1, 1)]
        [TestCase("1.mkv", 1, 1)]
        [TestCase("Dark 05.mkv", 1, 5)]
        [TestCase("Dark_05_(2017).mkv", 1, 5)]
        [TestCase("Dark - 05 - Verdades (2017) 1080p x265 10bit.mkv", 1, 5)]
        [TestCase("[Grupo] Dark - 05v2 [1080p][AC3 5.1].mkv", 1, 5)]
        [TestCase("[Grupo] Dark [05][1080p].mkv", 1, 5)]
        [TestCase("Serie 05.mkv", 1, 5)]
        [TestCase("Dark E05.mkv", 1, 5)]
        [TestCase("Episodio 5.mkv", 1, 5)]
        [TestCase("Capitulo 05.mkv", 1, 5)]
        [TestCase("Dark #5.mkv", 1, 5)]
        [TestCase("05 de 10.mkv", 1, 5)]
        [TestCase("Dark.05.1920x1080.350MB.mkv", 1, 5)]
        // Number with the season in it
        [TestCase("203.mkv", 2, 3)]
        [TestCase("Dark 203 1080p.mkv", 2, 3)]
        public void ReadsTheNameAtTheRootOfTheFolder(string name, int season, int episode)
        {
            Assert.That(Read(name, Rule), Is.EqualTo((season, (int?)episode)));
        }

        [TestCase("Temporada 2")]
        [TestCase("Dark Temporada 2 (2019) 1080p")]
        [TestCase("Season 02")]
        [TestCase("2ª Temporada")]
        [TestCase("2nd season")]
        [TestCase("Dark.S02.1080p.WEB-DL")]
        [TestCase("T2")]
        [TestCase("2")]
        [TestCase("02")]
        [TestCase("Tempada 2")]
        public void TheSubfolderSaysTheSeason(string folder)
        {
            Assert.Multiple(() =>
            {
                Assert.That(Read("03 - Fantasmas.mkv", $"{Rule}{folder}/"), Is.EqualTo((2, (int?)3)));
                Assert.That(Read("203.mkv", $"{Rule}{folder}/"), Is.EqualTo((2, (int?)3)), "the number repeats the season");
                Assert.That(Read("1x03.mkv", $"{Rule}{folder}/"), Is.EqualTo((2, (int?)3)), "the folder beats a default season");
                Assert.That(Read("03.mkv", $"{Rule}{folder}/Castellano/"), Is.EqualTo((2, (int?)3)), "also from a deeper folder");
            });
        }

        [Test]
        public void TheAssignedFolderCanBeASeason()
        {
            Assert.That(Read("03.mkv", "/Dark/Temporada 3/", rule: "/Dark/Temporada 3/"), Is.EqualTo((3, (int?)3)));
            Assert.That(Read("03.mkv", "/Temporada 3/Dark/", rule: "/Temporada 3/Dark/"), Is.EqualTo((1, (int?)3)), "what is above the folder says nothing");
            Assert.That(Read("03.mkv", "/Colecciones/12/", rule: "/Colecciones/12/"), Is.EqualTo((1, (int?)3)), "a number alone is only a season below the assigned folder");
        }

        [Test]
        public void SpecialsAreSeasonZero()
        {
            Assert.That(Read("02 - Detrás.mkv", Rule + "Especiales/"), Is.EqualTo((0, (int?)2)));
        }

        [Test]
        public void OneFolderPerEpisode()
        {
            Assert.That(Read("video.mkv", Rule + "Dark S02E04 1080p/"), Is.EqualTo((2, (int?)4)));
            Assert.That(Read("video.mkv", Rule + "Temporada 2/Capitulo 4/"), Is.EqualTo((2, (int?)4)));
        }

        [TestCase("Presentación.mkv")]
        [TestCase("Dark Temporada 1 completa 1080p.mkv")]
        [TestCase("Dark (2017) x264.mkv")]
        [TestCase("Dark 2017-12-01.mkv")]
        public void WithoutANumberThereIsNoEpisode(string name)
        {
            Assert.That(Read(name, Rule), Is.EqualTo((1, (int?)null)));
        }

        [TestCase("Los 100 - 03.mkv", "Los 100", 3)]
        [TestCase("Los.100.03.1080p.mkv", "Los 100", 3)]
        [TestCase("24 - 05.mkv", "24", 5)]
        [TestCase("24.mkv", "24", 24, Description = "a title that is a number does not hide the episode")]
        [TestCase("9-1-1 07.mkv", "9-1-1", 7)]
        public void NumbersOfTheTitleAreNotEpisodes(string name, string title, int episode)
        {
            Assert.That(Read(name, "/S/", Seasons(24, 24), "/S/", title), Is.EqualTo((1, (int?)episode)));
        }

        [Test]
        public void ContinuousNumberingRunsAcrossSeasons()
        {
            var seasons = Seasons(61, 16, 14, 39);
            Assert.Multiple(() =>
            {
                Assert.That(Read("One Piece 61.mkv", "/OP/", seasons, "/OP/", "One Piece", true), Is.EqualTo((1, (int?)61)));
                Assert.That(Read("One Piece 62.mkv", "/OP/", seasons, "/OP/", "One Piece", true), Is.EqualTo((2, (int?)1)));
                Assert.That(Read("One Piece 105.mkv", "/OP/", seasons, "/OP/", "One Piece", true), Is.EqualTo((4, (int?)14)));
                Assert.That(Read("Cap.105.mkv", "/OP/", seasons, "/OP/", "One Piece", true), Is.EqualTo((4, (int?)14)));
                Assert.That(Read("105.mkv", "/OP/", seasons, "/OP/", "One Piece"), Is.EqualTo((1, (int?)5)), "no episode below 100: the first digit is the season");
                Assert.That(Read("64.mkv", "/OP/Temporada 2/", seasons, "/OP/", "One Piece"), Is.EqualTo((2, (int?)3)), "season folders numbered on");
            });
        }

        [Test]
        public void WithoutEpisodeCounts()
        {
            var none = new List<LibrarySeason>();
            Assert.Multiple(() =>
            {
                Assert.That(Read("203.mkv", Rule, none), Is.EqualTo((2, (int?)3)));
                Assert.That(Read("Cap.203.mkv", Rule, none), Is.EqualTo((2, (int?)3)));
                Assert.That(Read("12.mkv", Rule, none), Is.EqualTo((1, (int?)12)));
                Assert.That(Read("103.mkv", Rule, none, continuous: true), Is.EqualTo((1, (int?)103)));
                Assert.That(Read("12.mkv", Rule + "Temporada 4/", none), Is.EqualTo((4, (int?)12)));
            });
        }

        [Test]
        public void ASeasonNewerThanTheMetadataIsKept()
        {
            Assert.That(Read("401.mkv", Rule), Is.EqualTo((4, (int?)1)));
            Assert.That(Read("01.mkv", Rule + "Temporada 4/"), Is.EqualTo((4, (int?)1)));
        }

        [Test]
        public void FilesWithSeveralEpisodes()
        {
            Assert.That(SeriesFolderParser.Read("Dark 2x01-02.mkv", Rule, Rule, new[] { "Dark" }).End, Is.EqualTo(2));
            Assert.That(SeriesFolderParser.Read("01-02.mkv", Rule, Rule, new[] { "Dark" }), Is.EqualTo(new EpisodeRead(null, 1, 2)));
        }
    }
}
