using TelegramDownloader.Services.Library;

namespace Test
{
    /// <summary>
    /// What the library understands from release-style file names found in
    /// Telegram channels, Spanish conventions included.
    /// </summary>
    public class MediaNameParserTest
    {
        [TestCase("Interstellar (2014) [BluRay 1080p].mkv", "Interstellar", 2014)]
        [TestCase("Interstellar.2014.1080p.BluRay.x264-GROUP.mkv", "Interstellar", 2014)]
        [TestCase("El Laberinto del Fauno 2006 Castellano.avi", "El Laberinto del Fauno", 2006)]
        [TestCase("Dune Parte Dos (2024) 2160p HDR Dual.mkv", "Dune Parte Dos", 2024)]
        [TestCase("Blade Runner 2049 (2017).mp4", "Blade Runner 2049", 2017)]
        [TestCase("1917 (2019).mkv", "1917", 2019)]
        [TestCase("Oppenheimer.mkv", "Oppenheimer", null)]
        [TestCase("Mr.Robot.mkv", "Mr Robot", null)]
        public void MovieTitleAndYear(string fileName, string title, int? year)
        {
            var p = MediaNameParser.Parse(fileName);
            Assert.Multiple(() =>
            {
                Assert.That(p.Title, Is.EqualTo(title));
                Assert.That(p.Year, Is.EqualTo(year));
                Assert.That(p.IsSeries, Is.False);
            });
        }

        [TestCase("Breaking.Bad.S01E02.1080p.WEB-DL.mkv", "Breaking Bad", 1, 2)]
        [TestCase("Breaking Bad - 1x02 - Cat's in the Bag.mkv", "Breaking Bad", 1, 2)]
        [TestCase("Breaking Bad 1x02.mkv", "Breaking Bad", 1, 2)]
        [TestCase("La Casa de Papel Cap.102 Castellano.mkv", "La Casa de Papel", 1, 2)]
        [TestCase("Aquí no hay quien viva Cap.1012.avi", "Aquí no hay quien viva", 10, 12)]
        [TestCase("The Bear S02E05.mkv", "The Bear", 2, 5)]
        [TestCase("The Bear s02e05 (2023) 720p.mkv", "The Bear", 2, 5)]
        [TestCase("Dark Temporada 1 Episodio 3.mkv", "Dark", 1, 3)]
        public void SeriesSeasonAndEpisode(string fileName, string title, int season, int episode)
        {
            var p = MediaNameParser.Parse(fileName);
            Assert.Multiple(() =>
            {
                Assert.That(p.Title, Is.EqualTo(title));
                Assert.That(p.Season, Is.EqualTo(season));
                Assert.That(p.Episode, Is.EqualTo(episode));
                Assert.That(p.IsSeries, Is.True);
            });
        }

        [Test]
        public void EpisodeRangeKeepsBothEnds()
        {
            var p = MediaNameParser.Parse("Show S01E01-E02.mkv");
            Assert.Multiple(() =>
            {
                Assert.That(p.Episode, Is.EqualTo(1));
                Assert.That(p.EpisodeEnd, Is.EqualTo(2));
            });
        }

        [Test]
        public void SeasonFolderSuppliesSeriesTitleWhenTheFileHasNone()
        {
            var p = MediaNameParser.Parse("1x02.mkv", "/Breaking Bad (2008)/Temporada 1/");
            Assert.Multiple(() =>
            {
                Assert.That(p.Title, Is.EqualTo("Breaking Bad"));
                Assert.That(p.Year, Is.EqualTo(2008));
                Assert.That(p.Season, Is.EqualTo(1));
                Assert.That(p.Episode, Is.EqualTo(2));
                Assert.That(p.Source, Is.EqualTo("folder"));
            });
        }

        [Test]
        public void SeasonFolderOverridesTheDefaultSeason()
        {
            var p = MediaNameParser.Parse("Dark Episodio 3.mkv", "/Dark/Season 2/");
            Assert.That(p.Season, Is.EqualTo(2));
            Assert.That(p.Episode, Is.EqualTo(3));
        }

        [Test]
        public void MovieFolderWithYearLendsItsYear()
        {
            var p = MediaNameParser.Parse("Interstellar.1080p.mkv", "/Interstellar (2014)/");
            Assert.That(p.Year, Is.EqualTo(2014));
        }

        [Test]
        public void JunkNameFallsBackToTheFolder()
        {
            var p = MediaNameParser.Parse("video_2023-01-01.mp4", "/Peliculas/Interstellar (2014)/");
            Assert.That(p.Title, Is.EqualTo("Interstellar"));
            Assert.That(p.Year, Is.EqualTo(2014));
        }

        [Test]
        public void JunkNameFallsBackToTheCaption()
        {
            var p = MediaNameParser.Parse("IMG_1234.mp4", "/", "🎬 Interstellar (2014) #scifi\nMás texto");
            Assert.Multiple(() =>
            {
                Assert.That(p.Title, Is.EqualTo("Interstellar"));
                Assert.That(p.Year, Is.EqualTo(2014));
                Assert.That(p.Source, Is.EqualTo("caption"));
            });
        }

        [Test]
        public void DuplicateSuffixAddedByTheIndexerIsIgnored()
        {
            // The refresh renames clashes as "name.mkv(1)"
            var p = MediaNameParser.Parse("Interstellar (2014).mkv(1)");
            Assert.That(p.Title, Is.EqualTo("Interstellar"));
            Assert.That(p.Year, Is.EqualTo(2014));
        }

        [Test]
        public void JunkDetection()
        {
            Assert.Multiple(() =>
            {
                Assert.That(MediaNameParser.IsJunk("video 123"), Is.True);
                Assert.That(MediaNameParser.IsJunk("IMG 4521"), Is.True);
                Assert.That(MediaNameParser.IsJunk("12345"), Is.True);
                Assert.That(MediaNameParser.IsJunk("Up"), Is.False);
                Assert.That(MediaNameParser.IsJunk("Interstellar"), Is.False);
            });
        }

        [TestCase("The Lord of the Rings", "lord of the rings")]
        [TestCase("El Señor de los Anillos", "senor de los anillos")]
        [TestCase("Spider-Man: No Way Home", "spider man no way home")]
        [TestCase("Ocean's Eleven", "oceans eleven")]
        public void NormalizationForMatching(string input, string expected)
        {
            Assert.That(MediaNameParser.NormalizeForMatch(input), Is.EqualTo(expected));
        }
    }
}
