using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace Test.Library
{
    /// <summary>The scan working on several titles at once, and what it does when a provider refuses to answer.</summary>
    public class LibraryScanParallelTest
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
            _source = new FakeProviderSource();
            _source.Providers.Add(_provider);
            _scan = new LibraryScanService(_db, _source, NullLogger<LibraryScanService>.Instance);
        }

        private async Task<LibraryScanState> Run(bool force = false, CancellationToken ct = default)
        {
            var state = new LibraryScanState { Running = true, StartedAt = DateTime.UtcNow, Force = force };
            await _scan.ScanAsync(state, _db.Channels.Keys.ToList(), force, ct);
            return state;
        }

        // Letters only: digits in a title would be parsed as a year or an episode
        private static string Title(int i) => $"{(char)('A' + i / 26 % 26)}{(char)('a' + i % 26)}xo Garden";

        private void AddMovies(int count, int channels = 1)
        {
            for (int i = 0; i < count; i++)
            {
                _provider.Add(LibraryKind.Movie, (1000 + i).ToString(), Title(i), 2000, $"tt{i:0000000}");
                _db.AddChannelFile((100 + i % channels).ToString(), $"{Title(i)} (2000).mkv");
            }
        }

        [Test]
        public async Task SeveralTitlesAreIdentifiedAtOnce()
        {
            AddMovies(40, channels: 4);
            _provider.Delay = TimeSpan.FromMilliseconds(50);

            var watch = Stopwatch.StartNew();
            var state = await Run();

            // 40 titles x 2 requests x 50 ms = 4 s one by one
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2.5)));
            Assert.Multiple(() =>
            {
                Assert.That(state.Matched, Is.EqualTo(40));
                Assert.That(state.FilesToProcess, Is.EqualTo(40));
                Assert.That(state.FilesProcessed, Is.EqualTo(40));
                Assert.That(state.ChannelsScanned, Is.EqualTo(4));
                Assert.That(_db.Items, Has.Count.EqualTo(40));
                Assert.That(_db.Items.Values.All(i => i.FileCount == 1), Is.True);
            });
        }

        [Test]
        public async Task ATitleInManyChannelsIsSearchedOnce()
        {
            _provider.Add(LibraryKind.Movie, "1", "Interstellar", 2014);
            for (int channel = 100; channel < 110; channel++)
                _db.AddChannelFile(channel.ToString(), "Interstellar (2014).mkv");

            var state = await Run();

            Assert.Multiple(() =>
            {
                Assert.That(_provider.SearchCalls, Is.EqualTo(1));
                Assert.That(_db.Items.Values.Single().ChannelIds, Has.Count.EqualTo(10));
                Assert.That(state.ChannelsScanned, Is.EqualTo(10));
            });
        }

        [Test]
        public async Task NamesThatLeadToTheSameTitleAtOnceCreateOneItem()
        {
            // Different groups (with and without year), same result
            _provider.Add(LibraryKind.Movie, "1", "Interstellar", 2014);
            _provider.Add(LibraryKind.Series, "2", "Breaking Bad", 2008);
            _provider.Seasons["2:1"] = Enumerable.Range(1, 4).Select(n => new LibraryEpisode { Season = 1, Number = n }).ToList();
            _db.AddChannelFile("100", "Interstellar (2014).mkv");
            _db.AddChannelFile("100", "Interstellar.mkv");
            _db.AddChannelFile("100", "Interstellar (2015).mkv");
            _db.AddChannelFile("100", "Breaking Bad S01E01.mkv");
            _db.AddChannelFile("100", "Breaking Bad (2008) S01E02.mkv");
            _db.LookupDelay = TimeSpan.FromMilliseconds(20);
            _provider.Delay = TimeSpan.FromMilliseconds(20);

            await Run();

            Assert.Multiple(() =>
            {
                Assert.That(_db.Items.Values.Select(i => i.Title), Is.EquivalentTo(new[] { "Interstellar", "Breaking Bad" }));
                Assert.That(_provider.ItemCalls, Is.EqualTo(2), "the second one waits for the first instead of fetching again");
                Assert.That(_provider.SeasonCalls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task TwoProvidersThatFindTheSameTitleAtOnceCreateOneItem()
        {
            var second = new FakeProvider("omdb");
            _source.Providers.Add(second);
            // The first provider only knows one of the two names
            _provider.Add(LibraryKind.Movie, "1", "Interstellar", 2014, "tt0816692");
            second.Add(LibraryKind.Movie, "tt0816692", "Zwischen Sternen", 2014, "tt0816692");
            _db.AddChannelFile("100", "Interstellar (2014).mkv");
            _db.AddChannelFile("100", "Zwischen Sternen (2014).mkv");
            _provider.Delay = second.Delay = TimeSpan.FromMilliseconds(30);

            await Run();

            Assert.That(_db.Items.Values.Single().FileCount, Is.EqualTo(2));
        }

        [Test]
        public async Task AnAbortedScanStillCountsWhatItMatched()
        {
            AddMovies(30);
            _provider.Throw = new ProviderException("quota", 429);
            _provider.SearchesBeforeThrow = 10;

            var state = await Run();

            Assert.That(state.Error, Does.Contain("quota"));
            Assert.That(_db.Items, Is.Not.Empty);
            Assert.That(_db.Items.Values.All(i => i.FileCount == 1 && i.ChannelIds.Count == 1), Is.True);

            // ...and the next one picks up the rest
            _provider.Throw = null;
            var next = await Run();
            Assert.Multiple(() =>
            {
                Assert.That(next.Error, Is.Null);
                Assert.That(_db.Items, Has.Count.EqualTo(30));
                Assert.That(_db.Files.Values.All(f => f.Status == LibraryFileStatus.Matched), Is.True);
            });
        }

        [Test]
        public async Task CancellingStopsAndKeepsTheCounts()
        {
            AddMovies(60);
            _provider.Delay = TimeSpan.FromMilliseconds(50);
            using var cts = new CancellationTokenSource(300);

            var state = await Run(ct: cts.Token);

            Assert.Multiple(() =>
            {
                Assert.That(state.Cancelled, Is.True);
                Assert.That(state.Error, Is.Null);
                Assert.That(_db.Files.Count, Is.LessThan(60));
                Assert.That(_db.Files.Values.Select(f => _db.Items[f.ItemId!].FileCount), Is.All.EqualTo(1));
            });
        }

        [Test]
        public async Task ASecondProviderWithoutQuotaIsLeftOut()
        {
            var second = new FakeProvider("omdb") { ThrowOnEnrich = new ProviderException("limit reached", 429), Throw = new ProviderException("limit reached", 429) };
            _source.Providers.Add(second);
            AddMovies(30);
            _db.AddChannelFile("100", "Some Unknown Movie (2001).mkv");

            var state = await Run();

            Assert.Multiple(() =>
            {
                Assert.That(state.Matched, Is.EqualTo(30));
                Assert.That(state.Unmatched, Is.EqualTo(1));
                Assert.That(state.Error, Does.Contain("limit reached").And.Contain("omdb"));
                Assert.That(second.EnrichCalls + second.SearchCalls, Is.LessThanOrEqualTo(LibraryScanService.TitleParallelism), "not asked again after refusing");
            });
        }

        [Test]
        public async Task UnmatchedFilesAreTriedAgainButNotWrittenAgain()
        {
            _db.AddChannelFile("100", "Some Unknown Movie (2001).mkv");
            await Run();
            Assert.That(_db.FileWrites, Is.EqualTo(1));

            var state = await Run();

            Assert.Multiple(() =>
            {
                Assert.That(state.Unmatched, Is.EqualTo(1), "still reported");
                Assert.That(_provider.SearchCalls, Is.EqualTo(4), "still searched");
                Assert.That(_db.FileWrites, Is.EqualTo(1), "nothing changed");
            });
        }

        #region Providers

        private class SlowHandler : HttpMessageHandler
        {
            private int _inFlight;
            public int MaxInFlight;
            public int Requests;
            public Func<int, HttpResponseMessage>? Respond { get; set; }
            public List<DateTime> Times { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var n = Interlocked.Increment(ref Requests);
                lock (Times)
                {
                    Times.Add(DateTime.UtcNow);
                    MaxInFlight = Math.Max(MaxInFlight, ++_inFlight);
                }
                try
                {
                    await Task.Delay(300, cancellationToken);
                }
                finally
                {
                    lock (Times) _inFlight--;
                }
                return Respond?.Invoke(n) ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"results":[]}""") };
            }
        }

        [Test]
        public async Task RequestsOverlapWithinTheRate()
        {
            var handler = new SlowHandler();
            var tmdb = new TmdbProvider(new HttpClient(handler), "key", "es-ES", NullLogger.Instance);

            await Task.WhenAll(Enumerable.Range(0, 20).Select(i => tmdb.SearchAsync(LibraryKind.Movie, "t" + i, null, CancellationToken.None)));

            var span = handler.Times.Max() - handler.Times.Min();
            Assert.That(handler.MaxInFlight, Is.GreaterThan(1));
            Assert.That(span, Is.GreaterThan(TimeSpan.FromMilliseconds(19 * 40 * 0.8)), "never faster than the limit");
        }

        [Test]
        public async Task TooManyRequestsPausesEveryRequest()
        {
            var handler = new SlowHandler
            {
                Respond = n =>
                {
                    if (n > 1) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"results":[]}""") };
                    var busy = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    busy.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                    return busy;
                }
            };
            var tmdb = new TmdbProvider(new HttpClient(handler), "key", "es-ES", NullLogger.Instance);

            var first = tmdb.SearchAsync(LibraryKind.Movie, "a", null, CancellationToken.None);
            await Task.Delay(500);
            await tmdb.SearchAsync(LibraryKind.Movie, "b", null, CancellationToken.None);
            await first;

            Assert.That(handler.Requests, Is.EqualTo(3));
            Assert.That(handler.Times[1] - handler.Times[0], Is.GreaterThan(TimeSpan.FromSeconds(1)), "the second search waited too");
        }

        [Test]
        public void ATimeoutIsAnErrorNotACancellation()
        {
            var handler = new SlowHandler();
            var tmdb = new TmdbProvider(new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(20) }, "key", "es-ES", NullLogger.Instance);
            Assert.ThrowsAsync<ProviderException>(() => tmdb.SearchAsync(LibraryKind.Movie, "a", null, CancellationToken.None));
        }

        #endregion

        #region Queries

        [Test]
        public void VideoFilterTakesAnyCaseWithOrWithoutDot()
        {
            var args = new RenderArgs<BsonFileManagerModel>(BsonSerializer.LookupSerializer<BsonFileManagerModel>(), BsonSerializer.SerializerRegistry);
            var filter = LibraryDbService.VideosFilter(new[] { ".mkv", ".3gp" }).Render(args);

            Assert.That(filter["IsFile"], Is.EqualTo(BsonBoolean.True));
            var regex = filter["Type"].AsBsonRegularExpression.ToRegex();
            foreach (var type in new[] { ".mkv", "mkv", ".MKV", ".3gp" })
                Assert.That(regex.IsMatch(type), Is.True, type);
            foreach (var type in new[] { ".mkv2", "xmkv", ".txt", "folder" })
                Assert.That(regex.IsMatch(type), Is.False, type);

            var fields = LibraryDbService.ScanFields.Render(args);
            Assert.That(fields.Names, Is.EquivalentTo(new[] { "_id", "Name", "IsFile", "Type", "FilterPath", "Size", "DateCreated", "MessageId", "isSplit" }));
        }

        #endregion
    }
}
