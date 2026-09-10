using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace Test.Library
{
    /// <summary>
    /// The scan against an in-memory store and a hand-written provider: what
    /// gets identified, what is left for review, what a rescan touches.
    /// </summary>
    public class LibraryScanServiceTest
    {
        private FakeLibraryDb _db = null!;
        private FakeProvider _provider = null!;
        private FakeProviderSource _source = null!;
        private LibraryScanService _scan = null!;

        [SetUp]
        public void SetUp()
        {
            _db = new FakeLibraryDb();
            _provider = new FakeProvider("tmdb");
            _provider.Add(LibraryKind.Movie, "157336", "Interstellar", 2014, "tt0816692");
            _provider.Add(LibraryKind.Movie, "438631", "Dune", 2021, "tt1160419");
            _provider.Add(LibraryKind.Movie, "841", "Dune", 1984, "tt0087182");
            _provider.Add(LibraryKind.Series, "1396", "Breaking Bad", 2008, "tt0903747");
            _provider.Seasons["1396:1"] = new List<LibraryEpisode>
            {
                new() { Season = 1, Number = 1, Title = "Pilot" },
                new() { Season = 1, Number = 2, Title = "Cat's in the Bag..." }
            };
            _source = new FakeProviderSource();
            _source.Providers.Add(_provider);
            _scan = new LibraryScanService(_db, _source, NullLogger<LibraryScanService>.Instance);
        }

        private async Task<LibraryScanState> Run(bool force = false, params string[] channels)
        {
            var state = new LibraryScanState { Running = true, StartedAt = DateTime.UtcNow, Force = force };
            await _scan.ScanAsync(state, channels.Length == 0 ? _db.Channels.Keys.ToList() : channels.ToList(), force, CancellationToken.None);
            return state;
        }

        [Test]
        public async Task IdentifiesMoviesAndSeriesAndCountsThem()
        {
            _db.AddChannelFile("100", "Interstellar (2014) 1080p.mkv");
            _db.AddChannelFile("100", "Breaking.Bad.S01E01.mkv");
            _db.AddChannelFile("100", "Breaking.Bad.S01E02.mkv");
            _db.AddChannelFile("100", "readme.txt");

            var state = await Run();

            Assert.Multiple(() =>
            {
                Assert.That(state.Running, Is.False);
                Assert.That(state.Error, Is.Null);
                Assert.That(state.FilesSeen, Is.EqualTo(3), "the text file is not a video");
                Assert.That(state.Matched, Is.EqualTo(3));
                Assert.That(_db.Items.Values.Count(i => i.Kind == LibraryKind.Movie), Is.EqualTo(1));
                Assert.That(_db.Items.Values.Count(i => i.Kind == LibraryKind.Series), Is.EqualTo(1));
            });

            var series = _db.Items.Values.Single(i => i.Kind == LibraryKind.Series);
            Assert.Multiple(() =>
            {
                Assert.That(series.EpisodeCount, Is.EqualTo(2));
                Assert.That(series.FileCount, Is.EqualTo(2));
                Assert.That(series.ChannelIds, Is.EqualTo(new List<long> { 100 }));
                Assert.That(_db.Episodes.Count(e => e.ItemId == series.Id), Is.EqualTo(2), "the season was fetched once");
                Assert.That(_provider.SeasonCalls, Is.EqualTo(1));
            });
            var movie = _db.Items.Values.Single(i => i.Kind == LibraryKind.Movie);
            Assert.That(movie.ImdbId, Is.EqualTo("tt0816692"));
        }

        [Test]
        public async Task OneSearchPerTitleNotPerFile()
        {
            _db.AddChannelFile("100", "Interstellar (2014) 1080p.mkv");
            _db.AddChannelFile("100", "Interstellar (2014) 720p.mkv");
            _db.AddChannelFile("100", "Interstellar.2014.4K.mkv");

            await Run();

            Assert.That(_provider.SearchCalls, Is.EqualTo(1));
            Assert.That(_db.Items.Values.Single().FileCount, Is.EqualTo(3));
        }

        [Test]
        public async Task UnknownTitleStaysUnmatchedAndJunkIsNotSearched()
        {
            _db.AddChannelFile("100", "Some Random Movie (2001).mkv");
            _db.AddChannelFile("100", "video_0001.mp4");

            var state = await Run();

            Assert.Multiple(() =>
            {
                Assert.That(state.Unmatched, Is.EqualTo(2));
                Assert.That(_db.Items, Is.Empty);
                Assert.That(_provider.SearchCalls, Is.EqualTo(2), "the real title is searched with and without year; junk is never searched");
                Assert.That(_db.Files.Values.All(f => f.Status == LibraryFileStatus.Unmatched), Is.True);
            });
        }

        [Test]
        public async Task WrongYearGoesToReviewWithTheBestGuess()
        {
            _db.AddChannelFile("100", "Dune (2021).mkv");
            _db.AddChannelFile("100", "Dune (1984).mkv");

            await Run();

            var files = _db.Files.Values.OrderBy(f => f.FileName).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(files.All(f => f.Status == LibraryFileStatus.Matched), Is.True);
                Assert.That(_db.Items.Count, Is.EqualTo(2), "two different movies");
                Assert.That(_db.Items[files[0].ItemId!].Year, Is.EqualTo(1984));
                Assert.That(_db.Items[files[1].ItemId!].Year, Is.EqualTo(2021));
            });
        }

        [Test]
        public async Task EpisodeMissingAtTheProviderIsFlaggedForReview()
        {
            _db.AddChannelFile("100", "Breaking Bad 1x07.mkv");

            await Run();

            var file = _db.Files.Values.Single();
            Assert.That(file.Status, Is.EqualTo(LibraryFileStatus.Review));
            Assert.That(file.ItemId, Is.Not.Null, "still linked to the series");
        }

        [Test]
        public async Task RescanSkipsMatchedFilesUnlessForced()
        {
            _db.AddChannelFile("100", "Interstellar (2014).mkv");
            await Run();
            var before = _provider.SearchCalls;
            _db.Cache.Clear();

            await Run();
            Assert.That(_provider.SearchCalls, Is.EqualTo(before), "an incremental scan leaves matched files alone");

            await Run(force: true);
            Assert.That(_provider.SearchCalls, Is.EqualTo(before + 1), "a forced scan identifies again");
        }

        [Test]
        public async Task UnmatchedFilesGetAnotherChanceAndLockedOnesDoNot()
        {
            var doc = _db.AddChannelFile("100", "Some Random Movie (2001).mkv");
            await Run();
            Assert.That(_db.Files.Values.Single().Status, Is.EqualTo(LibraryFileStatus.Unmatched));

            // The provider learns the title
            _provider.Add(LibraryKind.Movie, "999", "Some Random Movie", 2001);
            _db.Cache.Clear();
            await Run();
            Assert.That(_db.Files.Values.Single().Status, Is.EqualTo(LibraryFileStatus.Matched));

            // A manual decision is never overridden
            var file = _db.Files.Values.Single();
            file.Locked = true;
            file.ItemId = null;
            file.Status = LibraryFileStatus.Unmatched;
            await Run(force: true);
            Assert.That(_db.Files[LibraryFile.KeyOf(100, doc.Id)].Status, Is.EqualTo(LibraryFileStatus.Unmatched));
        }

        [Test]
        public async Task RemovedFilesDisappearAndEmptyItemsAreDeleted()
        {
            var doc = _db.AddChannelFile("100", "Interstellar (2014).mkv");
            await Run();
            Assert.That(_db.Items, Has.Count.EqualTo(1));

            _db.Channels["100"].Remove(doc);
            var state = await Run();

            Assert.Multiple(() =>
            {
                Assert.That(state.FilesRemoved, Is.EqualTo(1));
                Assert.That(_db.Files, Is.Empty);
                Assert.That(_db.Items, Is.Empty);
            });
        }

        [Test]
        public async Task TheSameSeriesAcrossTwoChannelsIsOneItem()
        {
            _db.AddChannelFile("100", "Breaking Bad S01E01.mkv");
            _db.AddChannelFile("200", "Breaking.Bad.S01E02.mkv");

            await Run();

            var series = _db.Items.Values.Single();
            Assert.That(series.ChannelIds, Is.EqualTo(new List<long> { 100, 200 }));
            Assert.That(series.EpisodeCount, Is.EqualTo(2));
        }

        [Test]
        public async Task SecondProviderIsAskedOnlyWhenTheFirstIsNotSure()
        {
            var second = new FakeProvider("omdb");
            second.Add(LibraryKind.Movie, "tt0000001", "Some Random Movie", 2001, "tt0000001");
            _source.Providers.Add(second);
            _db.AddChannelFile("100", "Interstellar (2014).mkv");
            _db.AddChannelFile("100", "Some Random Movie (2001).mkv");

            await Run();

            Assert.Multiple(() =>
            {
                Assert.That(_db.Files.Values.All(f => f.Status == LibraryFileStatus.Matched), Is.True);
                Assert.That(second.SearchCalls, Is.EqualTo(1), "only the title the first provider did not know");
                Assert.That(_db.Items.Values.Single(i => i.Provider == "omdb").ImdbId, Is.EqualTo("tt0000001"));
            });
        }

        [Test]
        public async Task OtherProvidersEnrichANewItem()
        {
            var second = new FakeProvider("omdb") { EnrichRating = 8.7 };
            _source.Providers.Add(second);
            _db.AddChannelFile("100", "Interstellar (2014).mkv");

            await Run();

            Assert.That(_db.Items.Values.Single().Rating, Is.EqualTo(8.7));
            Assert.That(second.EnrichCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task RejectedKeyAbortsTheScanWithAnError()
        {
            _provider.Throw = new ProviderException("bad key", 401);
            _db.AddChannelFile("100", "Interstellar (2014).mkv");

            var state = await Run();

            Assert.Multiple(() =>
            {
                Assert.That(state.Running, Is.False);
                Assert.That(state.Error, Does.Contain("bad key"));
                Assert.That(_db.ScanState!.Error, Is.EqualTo(state.Error), "the failure is persisted");
            });
        }

        [Test]
        public async Task ExistingWatchStateIsLinkedToTheIdentifiedItem()
        {
            var doc = _db.AddChannelFile("100", "Interstellar (2014).mkv");
            _db.Watch[LibraryFile.KeyOf(100, doc.Id)] = new WatchState { Id = LibraryFile.KeyOf(100, doc.Id), ChannelId = 100, FileId = doc.Id, PositionMs = 1000, DurationMs = 5000 };

            await Run();

            Assert.That(_db.Watch.Values.Single().ItemId, Is.EqualTo(_db.Items.Values.Single().Id));
        }
    }
}
