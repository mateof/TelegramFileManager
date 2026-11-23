using MongoDB.Bson;
using TelegramDownloader.Models;
using TelegramDownloader.Services.Mobile;

namespace Test
{
    /// <summary>
    /// In-memory name index behind GET /api/mobile/search. The Mongo loader is
    /// replaced by a counting fake and the clock by a manual one.
    /// </summary>
    public class ChannelSearchIndexTest
    {
        private sealed class ManualClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Now;
        }

        private sealed class FakeLoader
        {
            public Dictionary<string, List<ChannelFileNameEntry>> Data { get; } = new();
            public int Calls;
            public TaskCompletionSource? Gate;

            public async Task<List<ChannelFileNameEntry>> Load(string db, CancellationToken ct)
            {
                Interlocked.Increment(ref Calls);
                if (Gate != null) await Gate.Task;
                return Data.TryGetValue(db, out var list) ? list.ToList() : new List<ChannelFileNameEntry>();
            }
        }

        private static ChannelFileNameEntry F(string name, string type) =>
            new(ObjectId.GenerateNewId().ToString(), name, type);

        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

        private static (ChannelSearchIndex Index, FakeLoader Loader, ManualClock Clock) Create()
        {
            var loader = new FakeLoader();
            var clock = new ManualClock();
            return (new ChannelSearchIndex(loader.Load, clock, Ttl), loader, clock);
        }

        private static List<string> Names(FakeLoader loader, string db, ChannelSearchMatch match) =>
            match.Ids.Select(id => loader.Data[db].Single(f => f.Id == id).Name).ToList();

        [Test]
        public async Task MatchesPartialWordsCaseInsensitiveInNameOrder()
        {
            var (index, loader, _) = Create();
            loader.Data["1"] = new()
            {
                F("The Beatles - Help.mp3", ".mp3"),
                F("beatles_abbey.FLAC", ".FLAC"),
                F("Queen.mp3", ".mp3"),
                F("BEATLES live.mp3", ".mp3"),
            };

            var match = await index.SearchAsync("1", "BeAtL", audioOnly: false, limit: 10);

            Assert.Multiple(() =>
            {
                Assert.That(Names(loader, "1", match),
                    Is.EqualTo(new[] { "BEATLES live.mp3", "beatles_abbey.FLAC", "The Beatles - Help.mp3" }));
                Assert.That(match.HasMore, Is.False);
            });
        }

        [Test]
        public async Task AsteriskIsIgnoredLikeTheMongoSearch()
        {
            var (index, loader, _) = Create();
            loader.Data["1"] = new() { F("Beatles.mp3", ".mp3"), F("Other.mp3", ".mp3") };

            var match = await index.SearchAsync("1", "*eatl*", false, 10);

            Assert.That(Names(loader, "1", match), Is.EqualTo(new[] { "Beatles.mp3" }));
        }

        [Test]
        public async Task StopsAtLimitAndReportsMore()
        {
            var (index, loader, _) = Create();
            loader.Data["1"] = Enumerable.Range(0, 10).Select(i => F($"song {i:D2}.mp3", ".mp3")).ToList();

            var exact = await index.SearchAsync("1", "song", false, 10);
            var capped = await index.SearchAsync("1", "song", false, 3);

            Assert.Multiple(() =>
            {
                Assert.That(exact.Ids, Has.Count.EqualTo(10));
                Assert.That(exact.HasMore, Is.False);
                Assert.That(Names(loader, "1", capped), Is.EqualTo(new[] { "song 00.mp3", "song 01.mp3", "song 02.mp3" }));
                Assert.That(capped.HasMore, Is.True);
            });
        }

        [Test]
        public async Task AudioFilterIsAppliedBeforeTheLimit()
        {
            var (index, loader, _) = Create();
            loader.Data["1"] = new()
            {
                F("a mix.pdf", ".pdf"),
                F("b mix.mp4", ".mp4"),
                F("c mix.mp3", ".mp3"),
                F("d mix.OPUS", ".OPUS"),
                F("e mix.txt", ".txt"),
            };

            var audio = await index.SearchAsync("1", "mix", audioOnly: true, limit: 2);
            var all = await index.SearchAsync("1", "mix", audioOnly: false, limit: 2);

            Assert.Multiple(() =>
            {
                Assert.That(Names(loader, "1", audio), Is.EqualTo(new[] { "c mix.mp3", "d mix.OPUS" }));
                Assert.That(audio.HasMore, Is.False);
                Assert.That(Names(loader, "1", all), Is.EqualTo(new[] { "a mix.pdf", "b mix.mp4" }));
                Assert.That(all.HasMore, Is.True);
            });
        }

        [Test]
        public async Task LoadsEachChannelOnceAndReusesIt()
        {
            var (index, loader, _) = Create();
            loader.Data["1"] = new() { F("x.mp3", ".mp3") };
            loader.Data["2"] = new() { F("x.mp3", ".mp3") };

            await index.SearchAsync("1", "x.", false, 10);
            await index.SearchAsync("1", "mp", false, 10);
            await index.SearchAsync("2", "x.", false, 10);

            Assert.Multiple(() =>
            {
                Assert.That(loader.Calls, Is.EqualTo(2));
                Assert.That(index.CachedChannelCount, Is.EqualTo(2));
            });
        }

        [Test]
        public async Task ConcurrentSearchesShareOneLoad()
        {
            var (index, loader, _) = Create();
            loader.Data["1"] = new() { F("x.mp3", ".mp3") };
            loader.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var searches = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => index.SearchAsync("1", "x.", false, 10)))
                .ToArray();
            await Task.Delay(50);
            loader.Gate.SetResult();
            var results = await Task.WhenAll(searches);

            Assert.Multiple(() =>
            {
                Assert.That(loader.Calls, Is.EqualTo(1));
                Assert.That(results.All(r => r.Ids.Count == 1), Is.True);
            });
        }

        [Test]
        public async Task InvalidateReloadsTheChannelOnNextSearch()
        {
            var (index, loader, _) = Create();
            loader.Data["1"] = new() { F("old.mp3", ".mp3") };
            await index.SearchAsync("1", "mp3", false, 10);

            loader.Data["1"].Add(F("new.mp3", ".mp3"));
            var stale = await index.SearchAsync("1", "new", false, 10);
            index.Invalidate("1");
            var fresh = await index.SearchAsync("1", "new", false, 10);

            Assert.Multiple(() =>
            {
                Assert.That(stale.Ids, Is.Empty);
                Assert.That(Names(loader, "1", fresh), Is.EqualTo(new[] { "new.mp3" }));
                Assert.That(loader.Calls, Is.EqualTo(2));
            });
        }

        [Test]
        public async Task ExpiresAfterTtl()
        {
            var (index, loader, clock) = Create();
            loader.Data["1"] = new() { F("old.mp3", ".mp3") };
            await index.SearchAsync("1", "mp3", false, 10);
            loader.Data["1"].Add(F("new.mp3", ".mp3"));

            clock.Now += Ttl - TimeSpan.FromSeconds(1);
            var beforeTtl = await index.SearchAsync("1", "new", false, 10);
            clock.Now += TimeSpan.FromSeconds(1);
            var afterTtl = await index.SearchAsync("1", "new", false, 10);

            Assert.Multiple(() =>
            {
                Assert.That(beforeTtl.Ids, Is.Empty);
                Assert.That(afterTtl.Ids, Has.Count.EqualTo(1));
                Assert.That(loader.Calls, Is.EqualTo(2));
            });
        }

        [Test]
        public async Task EvictExpiredFreesOnlyExpiredChannels()
        {
            var (index, loader, clock) = Create();
            loader.Data["1"] = new() { F("a.mp3", ".mp3") };
            loader.Data["2"] = new() { F("b.mp3", ".mp3") };
            await index.SearchAsync("1", "mp3", false, 10);
            clock.Now += TimeSpan.FromMinutes(6);
            await index.SearchAsync("2", "mp3", false, 10);
            clock.Now += TimeSpan.FromMinutes(5);

            index.EvictExpired();

            Assert.That(index.CachedChannelCount, Is.EqualTo(1));
        }

        [Test]
        public async Task FailedLoadIsRetriedOnNextSearch()
        {
            var calls = 0;
            var index = new ChannelSearchIndex((db, ct) =>
            {
                if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("db down");
                return Task.FromResult(new List<ChannelFileNameEntry> { F("x.mp3", ".mp3") });
            }, new ManualClock(), Ttl);

            Assert.ThrowsAsync<InvalidOperationException>(() => index.SearchAsync("1", "x.", false, 10));
            var match = await index.SearchAsync("1", "x.", false, 10);

            Assert.That(match.Ids, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task SkipsEntriesWithoutValidIdOrName()
        {
            var (index, loader, _) = Create();
            loader.Data["1"] = new()
            {
                new ChannelFileNameEntry("not-an-objectid", "bad id.mp3", ".mp3"),
                new ChannelFileNameEntry(ObjectId.GenerateNewId().ToString(), null!, ".mp3"),
                F("good.mp3", ".mp3"),
            };

            var match = await index.SearchAsync("1", "mp3", false, 10);

            Assert.That(Names(loader, "1", match), Is.EqualTo(new[] { "good.mp3" }));
        }
    }
}
