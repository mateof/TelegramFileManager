using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace Test.Library
{
    /// <summary>The scan obeying folder rules: forced kinds, ignored folders, rescans when a rule changes.</summary>
    public class LibraryScanFolderRulesTest
    {
        private FakeLibraryDb _db = null!;
        private FakeProvider _provider = null!;
        private LibraryScanService _scan = null!;
        private GeneralConfig _previous = null!;

        [SetUp]
        public void SetUp()
        {
            _previous = GeneralConfigStatic.config;
            GeneralConfigStatic.config = new GeneralConfig();
            _db = new FakeLibraryDb();
            _provider = new FakeProvider("tmdb");
            _provider.Add(LibraryKind.Movie, "1", "Breaking Bad", 2008);
            _provider.Add(LibraryKind.Series, "1396", "Breaking Bad", 2008, "tt0903747");
            _provider.Seasons["1396:1"] = new List<LibraryEpisode> { new() { Season = 1, Number = 1, Title = "Pilot" } };
            var source = new FakeProviderSource();
            source.Providers.Add(_provider);
            _scan = new LibraryScanService(_db, source, NullLogger<LibraryScanService>.Instance);
        }

        [TearDown]
        public void TearDown() => GeneralConfigStatic.config = _previous;

        private static void Rule(long channel, string path, string kind) =>
            GeneralConfigStatic.config.LibraryFolderRules.Add(new LibraryFolderRule { ChannelId = channel, Path = path, Kind = kind });

        private async Task<LibraryScanState> Run(bool force = false)
        {
            var state = new LibraryScanState { Running = true, StartedAt = DateTime.UtcNow, Force = force };
            await _scan.ScanAsync(state, _db.Channels.Keys.ToList(), force, CancellationToken.None);
            return state;
        }

        [Test]
        public async Task SeriesRuleKeepsAFileWithoutEpisodeOutOfTheMovies()
        {
            Rule(100, "/Series/", LibraryRuleKind.Series);
            _db.AddChannelFile("100", "Breaking Bad.mkv", "/Series/");

            await Run();

            var file = _db.Files.Values.Single();
            Assert.Multiple(() =>
            {
                Assert.That(file.Kind, Is.EqualTo(LibraryKind.Series));
                Assert.That(file.Status, Is.EqualTo(LibraryFileStatus.Review), "no episode number: needs a look");
                Assert.That(file.RuleKind, Is.EqualTo(LibraryRuleKind.Series));
                Assert.That(_db.Items.Values.Single().Kind, Is.EqualTo(LibraryKind.Series));
            });
        }

        [Test]
        public async Task WithoutRuleTheSameFileIsAMovie()
        {
            _db.AddChannelFile("100", "Breaking Bad.mkv", "/Descargas/");
            await Run();
            Assert.That(_db.Items.Values.Single().Kind, Is.EqualTo(LibraryKind.Movie));
        }

        [Test]
        public async Task FolderNamedSeriesWorksWithoutAnyRule()
        {
            _db.AddChannelFile("100", "Breaking Bad.mkv", "/Series/");
            await Run();
            Assert.That(_db.Files.Values.Single().Kind, Is.EqualTo(LibraryKind.Series));
        }

        [Test]
        public async Task IgnoreRuleSkipsTheProviderAndTheCatalogue()
        {
            Rule(100, "/Extras/", LibraryRuleKind.Ignore);
            _db.AddChannelFile("100", "Breaking Bad S01E01.mkv", "/Extras/");

            var state = await Run();

            Assert.Multiple(() =>
            {
                Assert.That(_db.Files.Values.Single().Status, Is.EqualTo(LibraryFileStatus.Ignored));
                Assert.That(_db.Items, Is.Empty);
                Assert.That(_provider.SearchCalls, Is.Zero);
                Assert.That(state.Matched + state.Review + state.Unmatched, Is.Zero);
            });
        }

        [Test]
        public async Task ChangingARuleRescansTheFolderWithoutForce()
        {
            var doc = _db.AddChannelFile("100", "Breaking Bad S01E01.mkv", "/Extras/");
            Rule(100, "/Extras/", LibraryRuleKind.Ignore);
            await Run();
            Assert.That(_db.Files.Values.Single().Status, Is.EqualTo(LibraryFileStatus.Ignored));

            GeneralConfigStatic.config.LibraryFolderRules.Clear();
            await Run();

            var file = _db.Files[LibraryFile.KeyOf(100, doc.Id)];
            Assert.Multiple(() =>
            {
                Assert.That(file.Status, Is.EqualTo(LibraryFileStatus.Matched));
                Assert.That(file.RuleKind, Is.Null);
                Assert.That(_db.Items.Values.Single().Kind, Is.EqualTo(LibraryKind.Series));
            });
        }

        [Test]
        public async Task ManualDecisionsSurviveARuleChange()
        {
            var doc = _db.AddChannelFile("100", "Breaking Bad S01E01.mkv", "/Series/");
            await Run();
            var file = _db.Files[LibraryFile.KeyOf(100, doc.Id)];
            file.Locked = true;
            file.MatchSource = LibraryMatchSource.Manual;

            Rule(100, "/Series/", LibraryRuleKind.Ignore);
            await Run();

            Assert.That(_db.Files[LibraryFile.KeyOf(100, doc.Id)].Status, Is.EqualTo(LibraryFileStatus.Matched));
        }
    }
}
