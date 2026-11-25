using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Controllers.Api.V1;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace Test.Library
{
    /// <summary>A scan limited to some folders, of one or several channels.</summary>
    public class LibraryScanFoldersTest
    {
        private FakeLibraryDb _db = null!;
        private FakeProvider _provider = null!;
        private LibraryScanService _scan = null!;
        private GeneralConfig _previous = null!;

        [SetUp]
        public void SetUp()
        {
            _previous = GeneralConfigStatic.config;
            GeneralConfigStatic.config = new GeneralConfig { LibraryEnabled = true };
            _db = new FakeLibraryDb();
            _provider = new FakeProvider("tmdb");
            _provider.Add(LibraryKind.Movie, "1", "Interstellar", 2014);
            _provider.Add(LibraryKind.Movie, "2", "Dune", 2021);
            _provider.Add(LibraryKind.Movie, "3", "Arrival", 2016);
            _provider.Add(LibraryKind.Series, "4", "Dark", 2017);
            _provider.Seasons["4:1"] = new List<LibraryEpisode> { new() { Season = 1, Number = 1, Title = "Pilot" } };
            var source = new FakeProviderSource();
            source.Providers.Add(_provider);
            _scan = new LibraryScanService(_db, source, NullLogger<LibraryScanService>.Instance);

            _db.AddChannelFile("100", "Interstellar (2014).mkv", "/Cine/");
            _db.AddChannelFile("100", "Dune (2021).mkv", "/Cine/2021/");
            _db.AddChannelFile("100", "Arrival (2016).mkv", "/Otra/");
            _db.AddChannelFile("200", "Dark 1x01.mkv", "/Series/Dark/");
            _db.AddChannelFile("200", "Interstellar (2014) 4K.mkv", "/Extras/");
            _db.AddChannelFile("300", "Arrival (2016) 1080p.mkv", "/Cine/");
        }

        [TearDown]
        public async Task TearDown()
        {
            await Idle();
            GeneralConfigStatic.config = _previous;
        }

        private async Task Idle()
        {
            for (int i = 0; i < 500 && _scan.IsRunning; i++) await Task.Delay(10);
            Assert.That(_scan.IsRunning, Is.False, "the scan ends");
        }

        private static LibraryScanFolder Folder(long channel, string path) => new() { ChannelId = channel, Path = path };

        private async Task<LibraryScanState> Scan(bool force, params LibraryScanFolder[] folders)
        {
            Assert.That(_scan.Start(folders, force, out var error), Is.True, error);
            await Idle();
            return _scan.Current!;
        }

        private string[] Known => _db.Files.Values.Select(f => f.FileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        [Test]
        public async Task OnlyTheFoldersAndWhatIsBelowAreScanned()
        {
            var state = await Scan(false, Folder(100, "Cine"), Folder(200, "/Series/"));

            Assert.Multiple(() =>
            {
                Assert.That(Known, Is.EqualTo(new[] { "Dark 1x01.mkv", "Dune (2021).mkv", "Interstellar (2014).mkv" }));
                Assert.That(state.Scope, Is.EqualTo("folders"));
                Assert.That((state.ChannelsTotal, state.ChannelsScanned), Is.EqualTo((2, 2)));
                Assert.That((state.FilesSeen, state.FilesNew, state.Matched), Is.EqualTo((3, 3, 3)), "the other folders are not counted");
                Assert.That(ApiLibraryScanStateDto.From(state)!.Folders!.Select(f => (f.ChannelId, f.Path)), Is.EqualTo(new[] { (100L, "/Cine/"), (200L, "/Series/") }));
            });
        }

        [Test]
        public async Task TheScopeOfOneChannelIsTheChannel()
        {
            var state = await Scan(false, Folder(100, "/Otra/"), Folder(100, "/Cine/2021/"));
            Assert.That(state.Scope, Is.EqualTo("100"));
            Assert.That(Known, Is.EqualTo(new[] { "Arrival (2016).mkv", "Dune (2021).mkv" }));
        }

        [Test]
        public async Task TheRootIsTheWholeChannel()
        {
            await Scan(false, Folder(200, "/"));
            Assert.That(Known, Is.EqualTo(new[] { "Dark 1x01.mkv", "Interstellar (2014) 4K.mkv" }));
        }

        [Test]
        public async Task ForceOnlyReidentifiesThoseFolders()
        {
            await Scan(false, Folder(100, "/"));
            var writes = _db.Files.Values.ToDictionary(f => f.FileName, f => f.ScannedAt);
            await Task.Delay(20);

            var state = await Scan(true, Folder(100, "/Otra/"));

            Assert.That(state.FilesToProcess, Is.EqualTo(1));
            Assert.That(_db.Files.Values.Where(f => f.ScannedAt != writes[f.FileName]).Select(f => f.FileName), Is.EqualTo(new[] { "Arrival (2016).mkv" }));
        }

        [Test]
        public async Task TheRulesOfTheFoldersApply()
        {
            GeneralConfigStatic.config.LibraryFolderRules.Add(new LibraryFolderRule { ChannelId = 200, Path = "/Extras/", Kind = LibraryRuleKind.Ignore });
            await Scan(false, Folder(200, "/Extras/"));
            Assert.That(_db.Files.Values.Single().Status, Is.EqualTo(LibraryFileStatus.Ignored));
            Assert.That(_provider.SearchCalls, Is.Zero);
        }

        [Test]
        public void NoFolderIsNotAScanOfEverything()
        {
            Assert.That(_scan.Start(Array.Empty<LibraryScanFolder>(), false, out var error), Is.False);
            Assert.That(error, Is.Not.Null);
            Assert.That(_scan.Current, Is.Null);
        }

        [Test]
        public void AFolderInsideAnotherIsNotScannedTwice()
        {
            var folders = LibraryFolderRules.Outermost(new[] { Folder(1, "/a/b/"), Folder(1, "a"), Folder(1, "/A/"), Folder(2, "/a/b"), Folder(1, "/ab/") });
            Assert.That(folders.Select(f => (f.ChannelId, f.Path)), Is.EquivalentTo(new[] { (1L, "/a/"), (2L, "/a/b/"), (1L, "/ab/") }));
        }

        private IActionResult Post(LibraryScanRequest request) =>
            new LibraryController(null!, _scan, null!, _db, NullLogger<LibraryController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            }.Scan(request);

        [Test]
        public async Task TheApiScansTheFoldersOfTheRequest()
        {
            var answer = Post(new LibraryScanRequest { Folders = new() { new() { ChannelId = 300, Path = "/Cine" } } });
            Assert.That((answer as ObjectResult)?.StatusCode, Is.EqualTo(202));
            await Idle();
            Assert.That(Known, Is.EqualTo(new[] { "Arrival (2016) 1080p.mkv" }));
        }

        [Test]
        public void TheApiRefusesAmbiguousRequests()
        {
            var empty = Post(new LibraryScanRequest { Folders = new() });
            var noChannel = Post(new LibraryScanRequest { Folders = new() { new() { Path = "/Cine/" } } });
            var both = Post(new LibraryScanRequest { ChannelId = "100", Folders = new() { new() { ChannelId = 100, Path = "/Cine/" } } });
            Assert.That(new[] { empty, noChannel, both }.Select(a => (a as ObjectResult)?.StatusCode), Is.All.EqualTo(400));
            Assert.That(_scan.Current, Is.Null);
        }
    }
}
