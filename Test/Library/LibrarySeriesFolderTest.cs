using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace Test.Library
{
    /// <summary>A folder assigned to one series: the scan, and assigning and removing it.</summary>
    public class LibrarySeriesFolderTest
    {
        private sealed class TestLibraryService : LibraryService
        {
            public int Saves;
            public TestLibraryService(FakeLibraryDb db, LibraryScanService scan, IMetadataProviderSource source)
                : base(db, scan, source, null!, NullLogger<LibraryService>.Instance) { }
            protected override Task SaveConfigAsync(GeneralConfig config) { Saves++; return Task.CompletedTask; }
        }

        private FakeLibraryDb _db = null!;
        private FakeProvider _provider = null!;
        private FakeProviderSource _source = null!;
        private LibraryScanService _scan = null!;
        private TestLibraryService _library = null!;
        private GeneralConfig _previous = null!;

        [SetUp]
        public void SetUp()
        {
            _previous = GeneralConfigStatic.config;
            GeneralConfigStatic.config = new GeneralConfig();
            _db = new FakeLibraryDb();
            _provider = new FakeProvider("tmdb");
            _provider.Add(LibraryKind.Series, "70523", "Dark", 2017, "tt5753856");
            _provider.Add(LibraryKind.Series, "1396", "Breaking Bad", 2008);
            _provider.Add(LibraryKind.Movie, "9", "Secretos", 2017);
            AddSeason("70523", 1, 10);
            AddSeason("70523", 2, 8);
            AddSeason("1396", 1, 7);
            _source = new FakeProviderSource();
            _source.Providers.Add(_provider);
            _scan = new LibraryScanService(_db, _source, NullLogger<LibraryScanService>.Instance);
            _library = new TestLibraryService(_db, _scan, _source);
        }

        [TearDown]
        public async Task TearDown()
        {
            await Idle();
            GeneralConfigStatic.config = _previous;
        }

        private void AddSeason(string id, int season, int episodes) =>
            _provider.Seasons[$"{id}:{season}"] = Enumerable.Range(1, episodes).Select(n => new LibraryEpisode { Season = season, Number = n, Title = $"E{n}" }).ToList();

        private static LibraryFolderRule Rule(string path, string id = "70523", string title = "Dark") =>
            new() { ChannelId = 100, Path = path, Kind = LibraryRuleKind.Series, Provider = "tmdb", ProviderId = id, Title = title };

        private async Task<LibraryScanState> Run(bool force = false)
        {
            var state = new LibraryScanState { Running = true, StartedAt = DateTime.UtcNow, Force = force };
            await _scan.ScanAsync(state, _db.Channels.Keys.ToList(), force, CancellationToken.None);
            return state;
        }

        private async Task Idle()
        {
            for (int i = 0; i < 500 && _scan.IsRunning; i++) await Task.Delay(10);
            Assert.That(_scan.IsRunning, Is.False, "the scan ends");
        }

        private LibraryFile FileNamed(string name) => _db.Files.Values.Single(f => f.FileName == name);
        private LibraryItem ItemOf(string name) => _db.Items[FileNamed(name).ItemId!];
        private (int?, int?) EpisodeOf(string name) => (FileNamed(name).Season, FileNamed(name).Episode);

        private Task<(ApiLibraryFolderDto? Folder, string? Error)> Assign(string path, string id = "70523", bool includeLocked = false) =>
            _library.AssignFolderAsync(new LibraryAssignFolderRequest { ChannelId = 100, Path = path, Provider = "tmdb", ProviderId = id, IncludeLocked = includeLocked }, CancellationToken.None);

        [Test]
        public async Task EveryFileBelongsToTheSeriesWithoutASearch()
        {
            GeneralConfigStatic.config.LibraryFolderRules.Add(Rule("/Misc/"));
            _db.AddChannelFile("100", "01 - Secretos.mkv", "/Misc/Temporada 1/");
            _db.AddChannelFile("100", "02.mkv", "/Misc/Temporada 1/");
            _db.AddChannelFile("100", "Otro nombre 2x03.mkv", "/Misc/");
            _db.AddChannelFile("100", "Presentación.mkv", "/Misc/");
            _db.AddChannelFile("100", "Breaking Bad S01E01.mkv", "/Otra/");

            var state = await Run();

            Assert.Multiple(() =>
            {
                Assert.That(state.Error, Is.Null);
                Assert.That(ItemOf("01 - Secretos.mkv").Title, Is.EqualTo("Dark"));
                Assert.That(EpisodeOf("01 - Secretos.mkv"), Is.EqualTo(((int?)1, (int?)1)));
                Assert.That(EpisodeOf("02.mkv"), Is.EqualTo(((int?)1, (int?)2)));
                Assert.That(EpisodeOf("Otro nombre 2x03.mkv"), Is.EqualTo(((int?)2, (int?)3)));
                Assert.That(FileNamed("02.mkv").Status, Is.EqualTo(LibraryFileStatus.Matched));
                Assert.That(FileNamed("02.mkv").Confidence, Is.EqualTo(1));
                Assert.That(FileNamed("02.mkv").Parsed.Title, Is.EqualTo("Dark"));
                Assert.That(FileNamed("Presentación.mkv").Status, Is.EqualTo(LibraryFileStatus.Review), "no episode number");
                Assert.That(ItemOf("Presentación.mkv").Title, Is.EqualTo("Dark"));
                Assert.That(ItemOf("Breaking Bad S01E01.mkv").Title, Is.EqualTo("Breaking Bad"));
                Assert.That(_provider.SearchCalls, Is.EqualTo(1), "only for the other folder");
                Assert.That(_db.Items.Values.Single(i => i.Title == "Dark").EpisodeCount, Is.EqualTo(3));
            });
        }

        [Test]
        public async Task AnEpisodeTheProviderDoesNotHaveIsLeftForReview()
        {
            GeneralConfigStatic.config.LibraryFolderRules.Add(Rule("/Misc/"));
            _db.AddChannelFile("100", "Dark 2x30.mkv", "/Misc/");
            await Run();
            Assert.That(FileNamed("Dark 2x30.mkv").Status, Is.EqualTo(LibraryFileStatus.Review));
        }

        [Test]
        public async Task NewFilesKeepTheNumberingOfTheFolder()
        {
            AddSeason("70523", 1, 60);
            AddSeason("70523", 2, 60);
            GeneralConfigStatic.config.LibraryFolderRules.Add(Rule("/Misc/"));
            _db.AddChannelFile("100", "01.mkv", "/Misc/");
            await Run();
            _db.AddChannelFile("100", "102.mkv", "/Misc/");
            await Run();
            Assert.That(EpisodeOf("102.mkv"), Is.EqualTo(((int?)2, (int?)42)), "the folder starts at 01, so 102 is not 1x02");
        }

        [Test]
        public async Task ASpecificFolderWins()
        {
            GeneralConfigStatic.config.LibraryFolderRules.Add(Rule("/Misc/"));
            GeneralConfigStatic.config.LibraryFolderRules.Add(Rule("/Misc/BB/", "1396", "Breaking Bad"));
            _db.AddChannelFile("100", "01.mkv", "/Misc/");
            _db.AddChannelFile("100", "02.mkv", "/Misc/BB/");
            await Run();
            Assert.That(ItemOf("01.mkv").Title, Is.EqualTo("Dark"));
            Assert.That(ItemOf("02.mkv").Title, Is.EqualTo("Breaking Bad"));
        }

        [Test]
        public async Task AMovieWithTheSameTmdbIdIsAnotherTitle()
        {
            _provider.Add(LibraryKind.Movie, "70523", "Otra película", 2001);
            _db.AddChannelFile("100", "Otra película (2001).mkv", "/Cine/");
            await Run();
            GeneralConfigStatic.config.LibraryFolderRules.Add(Rule("/Misc/"));
            _db.AddChannelFile("100", "01.mkv", "/Misc/");
            await Run();

            Assert.That(ItemOf("01.mkv").Title, Is.EqualTo("Dark"));
            Assert.That(ItemOf("01.mkv").Kind, Is.EqualTo(LibraryKind.Series));
            Assert.That(ItemOf("Otra película (2001).mkv").Kind, Is.EqualTo(LibraryKind.Movie));
        }

        [Test]
        public void TheKindIsPartOfTheQuery()
        {
            var f = MongoDB.Driver.Builders<LibraryItem>.Filter;
            string Render(MongoDB.Driver.FilterDefinition<LibraryItem> filter) => filter.Render(new MongoDB.Driver.RenderArgs<LibraryItem>(
                MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<LibraryItem>(), MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry)).ToString();
            Assert.That(Render(TelegramDownloader.Data.db.LibraryDbService.WithKind(f.Where(x => x.Provider == "tmdb" && x.ProviderId == "1"), "series")),
                Is.EqualTo("{ \"Provider\" : \"tmdb\", \"ProviderId\" : \"1\", \"Kind\" : \"series\" }"));
            Assert.That(Render(TelegramDownloader.Data.db.LibraryDbService.WithKind(f.Eq("ExternalIds.imdb", "tt1"), null)), Is.EqualTo("{ \"ExternalIds.imdb\" : \"tt1\" }"));
        }

        [Test]
        public async Task AProviderThatIsNotEnabledIsOnlyNeededOnce()
        {
            GeneralConfigStatic.config.LibraryFolderRules.Add(Rule("/Misc/"));
            _db.AddChannelFile("100", "01.mkv", "/Misc/");
            _source.Providers.Clear();
            await Run();
            Assert.That(FileNamed("01.mkv").Status, Is.EqualTo(LibraryFileStatus.Unmatched));
            Assert.That(FileNamed("01.mkv").Error, Does.Contain("tmdb"));

            _source.Providers.Add(_provider);
            await Run();
            Assert.That(FileNamed("01.mkv").Status, Is.EqualTo(LibraryFileStatus.Matched), "the next scan tries again");

            var calls = _provider.ItemCalls;
            _db.AddChannelFile("100", "02.mkv", "/Misc/");
            await Run();
            Assert.That(ItemOf("02.mkv").Title, Is.EqualTo("Dark"));
            Assert.That(_provider.ItemCalls, Is.EqualTo(calls), "the library already has the series");
        }

        [Test]
        public async Task AssigningReidentifiesOnlyTheFolder()
        {
            _db.AddChannelFile("100", "Secretos (2017).mkv", "/Misc/");
            _db.AddChannelFile("100", "05.mkv", "/Misc/");
            _db.AddChannelFile("100", "Desconocido (2017).mkv", "/Otra/");
            await Run();
            var movie = FileNamed("Secretos (2017).mkv").ItemId!;
            Assert.That(_db.Items[movie].Kind, Is.EqualTo(LibraryKind.Movie), "taken for a movie");
            var searches = _provider.SearchCalls;

            var (folder, error) = await Assign("Misc");
            await Idle();

            Assert.Multiple(() =>
            {
                Assert.That(error, Is.Null);
                Assert.That(folder!.Path, Is.EqualTo("/Misc/"));
                Assert.That(folder.Videos, Is.EqualTo(2));
                Assert.That(folder.Locked, Is.Zero);
                Assert.That(folder.ScanStarted, Is.True);
                Assert.That(folder.Rule!.Title, Is.EqualTo("Dark"));
                Assert.That(folder.Rule.Year, Is.EqualTo(2017));
                Assert.That(_library.Saves, Is.EqualTo(1));
                Assert.That(FileNamed("05.mkv").ItemId, Is.EqualTo(folder.ItemId));
                Assert.That(FileNamed("Secretos (2017).mkv").ItemId, Is.EqualTo(folder.ItemId));
                Assert.That(EpisodeOf("05.mkv"), Is.EqualTo(((int?)1, (int?)5)));
                Assert.That(_db.Items.ContainsKey(movie), Is.False, "left without files");
                Assert.That(_provider.SearchCalls, Is.EqualTo(searches), "the unknown file of the other folder is not searched again");
            });
        }

        [Test]
        public async Task AssigningAnotherTitleReplacesTheRule()
        {
            _db.AddChannelFile("100", "05.mkv", "/Misc/");
            await Assign("/Misc/");
            await Idle();
            await Assign("/misc", "1396");
            await Idle();
            Assert.That(GeneralConfigStatic.config.LibraryFolderRules, Has.Count.EqualTo(1));
            Assert.That(ItemOf("05.mkv").Title, Is.EqualTo("Breaking Bad"));
            Assert.That(_db.Items.Values.Select(i => i.Title), Is.EqualTo(new[] { "Breaking Bad" }));
        }

        [Test]
        public async Task AnUnknownTitleChangesNothing()
        {
            Assert.ThrowsAsync<ProviderException>(() => Assign("/Misc/", "404"));
            Assert.That((await _library.AssignFolderAsync(new LibraryAssignFolderRequest { ChannelId = 100, Path = "/", Provider = "x", ProviderId = "1" }, CancellationToken.None)).Error, Does.Contain("'x'"));
            Assert.That(GeneralConfigStatic.config.LibraryFolderRules, Is.Empty);
            Assert.That(_library.Saves, Is.Zero);
        }

        [Test]
        public async Task FilesIdentifiedByHandStayUnlessAsked()
        {
            var fixedByHand = _db.AddChannelFile("100", "01.mkv", "/Misc/");
            var ignored = _db.AddChannelFile("100", "trailer 1.mkv", "/Misc/");
            await Run();
            await _library.MatchFileAsync(100, fixedByHand.Id, new LibraryMatchFileRequest { Provider = "tmdb", ProviderId = "1396", Kind = LibraryKind.Series, Season = 1, Episode = 1 }, CancellationToken.None);
            await _library.SetIgnoredAsync(100, ignored.Id, true);

            var (folder, _) = await Assign("/Misc/");
            await Idle();
            Assert.That(folder!.Locked, Is.EqualTo(1), "the ignored one does not count");
            Assert.That(ItemOf("01.mkv").Title, Is.EqualTo("Breaking Bad"));

            await Assign("/Misc/", includeLocked: true);
            await Idle();
            Assert.Multiple(() =>
            {
                Assert.That(ItemOf("01.mkv").Title, Is.EqualTo("Dark"));
                Assert.That(FileNamed("01.mkv").Locked, Is.False);
                Assert.That(FileNamed("01.mkv").MatchSource, Is.EqualTo(LibraryMatchSource.Auto));
                Assert.That(FileNamed("trailer 1.mkv").Status, Is.EqualTo(LibraryFileStatus.Ignored));
            });
        }

        [Test]
        public async Task RemovingTheRuleIdentifiesEachFileByItsName()
        {
            _db.AddChannelFile("100", "Breaking Bad S01E01.mkv", "/Misc/");
            await Assign("/Misc/");
            await Idle();
            Assert.That(ItemOf("Breaking Bad S01E01.mkv").Title, Is.EqualTo("Dark"));

            Assert.That(await _library.UnassignFolderAsync(100, "/Misc"), Is.True);
            await Idle();
            Assert.That(ItemOf("Breaking Bad S01E01.mkv").Title, Is.EqualTo("Breaking Bad"));
            Assert.That(await _library.UnassignFolderAsync(100, "/Misc"), Is.False);
        }

        [Test]
        public async Task IdentifyingTheSeriesAsAnotherMovesItsFolders()
        {
            _db.AddChannelFile("100", "01.mkv", "/Misc/");
            var (folder, _) = await Assign("/Misc/");
            await Idle();

            await _library.IdentifyItemAsync(folder!.ItemId!, new LibraryIdentifyItemRequest { Provider = "tmdb", ProviderId = "1396" }, CancellationToken.None);
            _db.AddChannelFile("100", "02.mkv", "/Misc/");
            await Run();

            Assert.That(GeneralConfigStatic.config.LibraryFolderRules.Single().Title, Is.EqualTo("Breaking Bad"));
            Assert.That(ItemOf("02.mkv").Title, Is.EqualTo("Breaking Bad"));
        }

        [Test]
        public async Task AssigningDuringAScanWaitsForIt()
        {
            for (int i = 0; i < 30; i++) _db.AddChannelFile("100", $"Pelicula numero{i} larga.mkv", "/Cine/");
            _db.AddChannelFile("100", "05.mkv", "/Misc/");
            _provider.Delay = TimeSpan.FromMilliseconds(20);
            Assert.That(_scan.Start("100", false, out _), Is.True);
            var (folder, _) = await Assign("/Misc/");
            Assert.That(folder!.ScanStarted, Is.True);
            for (int i = 0; i < 300 && !(_db.Files.Values.Any(f => f.FileName == "05.mkv" && f.ItemId == folder.ItemId) && !_scan.IsRunning); i++) await Task.Delay(20);
            Assert.That(ItemOf("05.mkv").Title, Is.EqualTo("Dark"));
        }
    }
}
