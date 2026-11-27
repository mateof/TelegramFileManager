using MongoDB.Bson;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services.Player;

namespace Test
{
    /// <summary>In-memory <see cref="IPlayerDataStore"/> with the same semantics as the Mongo one.</summary>
    internal sealed class MemoryPlayerDataStore : IPlayerDataStore
    {
        public readonly Dictionary<string, PlayerFavoriteDoc> Favorites = new();
        public readonly List<PlayerHistoryDoc> History = new();
        public readonly Dictionary<string, PlayerResumeDoc> Resume = new();

        public Task<PlayerFavoriteDoc> UpsertFavorite(ApiPlayerTrackDto track, DateTime now)
        {
            if (!Favorites.TryGetValue(track.Key!, out var doc))
                Favorites[track.Key!] = doc = new PlayerFavoriteDoc { Key = track.Key!, AddedAt = now };
            doc.Track = track;
            doc.UpdatedAt = now;
            return Task.FromResult(doc);
        }

        public Task<(List<PlayerFavoriteDoc> Items, long Total)> ListFavorites(int skip, int take) =>
            Task.FromResult((Favorites.Values.OrderByDescending(f => f.AddedAt).ThenBy(f => f.Key).Skip(skip).Take(take).ToList(), (long)Favorites.Count));

        public Task<List<string>> FavoriteKeys() => Task.FromResult(Favorites.Keys.ToList());

        public Task DeleteFavorite(string key)
        {
            Favorites.Remove(key);
            return Task.CompletedTask;
        }

        public Task InsertHistory(PlayerHistoryDoc entry)
        {
            if (entry.Id == ObjectId.Empty) entry.Id = ObjectId.GenerateNewId();
            History.Add(entry);
            return Task.CompletedTask;
        }

        public Task<long> CountHistory() => Task.FromResult((long)History.Count);

        public Task DeleteOldestHistory(int count)
        {
            foreach (var old in History.OrderBy(h => h.PlayedAt).ThenBy(h => h.Id).Take(count).ToList())
                History.Remove(old);
            return Task.CompletedTask;
        }

        public Task<List<PlayerHistoryDoc>> ListHistory(DateTime? before, int take) =>
            Task.FromResult(History.Where(h => before == null || h.PlayedAt < before)
                .OrderByDescending(h => h.PlayedAt).ThenByDescending(h => h.Id).Take(take).ToList());

        public Task<List<PlayerHistoryDoc>> HistorySince(DateTime? since) =>
            Task.FromResult(History.Where(h => since == null || h.PlayedAt >= since).ToList());

        public Task ClearHistory()
        {
            History.Clear();
            return Task.CompletedTask;
        }

        public Task UpsertResume(PlayerResumeDoc entry)
        {
            Resume[entry.Key] = entry;
            return Task.CompletedTask;
        }

        public Task<PlayerResumeDoc?> GetResume(string key) => Task.FromResult(Resume.TryGetValue(key, out var d) ? d : null);

        public Task<List<PlayerResumeDoc>> ListResume(int take) =>
            Task.FromResult(Resume.Values.OrderByDescending(r => r.UpdatedAt).Take(take).ToList());

        public Task DeleteResume(string key)
        {
            Resume.Remove(key);
            return Task.CompletedTask;
        }
    }

    public class PlayerDataRulesTest
    {
        private static ApiPlayerTrackDto Channel(string channelId = "-1001", string fileId = "abc") =>
            new() { ChannelId = channelId, FileId = fileId, FileName = "song.flac" };

        private static ApiPlayerTrackDto Local(string path) => new() { IsLocalFile = true, FilePath = path };

        [Test]
        public void ChannelTrackKey()
        {
            Assert.That(PlayerDataRules.TryNormalizeTrack(Channel(" -1001 ", "abc "), out var t, out var error), Is.True, error);
            Assert.Multiple(() =>
            {
                Assert.That(t.Key, Is.EqualTo("channel:-1001:abc"));
                Assert.That(t.ChannelId, Is.EqualTo("-1001"));
                Assert.That(t.FileId, Is.EqualTo("abc"));
                Assert.That(t.IsLocalFile, Is.False);
                Assert.That(t.FileName, Is.EqualTo("song.flac"));
            });
        }

        [TestCase("music/rock/a.mp3", "local:music/rock/a.mp3")]
        [TestCase("/music/rock/a.mp3", "local:music/rock/a.mp3")]
        [TestCase("\\music\\rock\\a.mp3", "local:music/rock/a.mp3")]
        [TestCase("  Music/A B.mp3 ", "local:Music/A B.mp3")]
        public void LocalTrackKeyIsNormalised(string path, string key)
        {
            Assert.That(PlayerDataRules.TryNormalizeTrack(Local(path), out var t, out var error), Is.True, error);
            Assert.Multiple(() =>
            {
                Assert.That(t.Key, Is.EqualTo(key));
                Assert.That(t.IsLocalFile, Is.True);
                Assert.That(t.ChannelId, Is.Null);
                Assert.That(t.FileId, Is.Null);
                Assert.That(t.FileName, Is.EqualTo(key.Substring(key.LastIndexOf('/') + 1)));
            });
        }

        [Test]
        public void ServerKeyWinsOverTheClientOne()
        {
            var track = Channel();
            track.Key = "channel:999:zzz";
            Assert.That(PlayerDataRules.TryNormalizeTrack(track, out var t, out _), Is.True);
            Assert.That(t.Key, Is.EqualTo("channel:-1001:abc"));
        }

        [Test]
        public void PathWithoutChannelIdsIsLocal()
        {
            Assert.That(PlayerDataRules.TryNormalizeTrack(new ApiPlayerTrackDto { FilePath = "a/b.mp3", ChannelId = "1" }, out var t, out _), Is.True);
            Assert.That(t.Key, Is.EqualTo("local:a/b.mp3"));
        }

        [TestCase("channel:-1001:abc", "channel:-1001:abc")]
        [TestCase("local:/x/y.mp3", "local:x/y.mp3")]
        public void KeyAloneIsEnough(string key, string expected)
        {
            Assert.That(PlayerDataRules.TryNormalizeTrack(new ApiPlayerTrackDto { Key = key }, out var t, out var error), Is.True, error);
            Assert.That(t.Key, Is.EqualTo(expected));
        }

        [Test]
        public void UnresolvableTracksAreRefused()
        {
            Assert.Multiple(() =>
            {
                Assert.That(PlayerDataRules.TryNormalizeTrack(null, out _, out _), Is.False);
                Assert.That(PlayerDataRules.TryNormalizeTrack(new ApiPlayerTrackDto { FileName = "x.mp3" }, out _, out _), Is.False);
                Assert.That(PlayerDataRules.TryNormalizeTrack(new ApiPlayerTrackDto { ChannelId = "1" }, out _, out _), Is.False);
                Assert.That(PlayerDataRules.TryNormalizeTrack(new ApiPlayerTrackDto { IsLocalFile = true, ChannelId = "1", FileId = "2" }, out _, out _), Is.False);
                Assert.That(PlayerDataRules.TryNormalizeTrack(new ApiPlayerTrackDto { Key = "nope" }, out _, out _), Is.False);
                Assert.That(PlayerDataRules.TryNormalizeTrack(new ApiPlayerTrackDto { ChannelId = "1", FileId = "2", Duration = -1 }, out _, out _), Is.False);
            });
        }

        [TestCase("channel:1:2", "channel:1:2")]
        [TestCase(" channel:1:2 ", "channel:1:2")]
        [TestCase("local:\\a\\b.mp3", "local:a/b.mp3")]
        [TestCase("channel:1:", null)]
        [TestCase("channel::2", null)]
        [TestCase("local:", null)]
        [TestCase("local:/", null)]
        [TestCase("", null)]
        [TestCase(null, null)]
        [TestCase("http://x", null)]
        public void NormalizeKey(string? key, string? expected) =>
            Assert.That(PlayerDataRules.NormalizeKey(key), Is.EqualTo(expected));

        [TestCase(0, 0, false)]          // unknown duration
        [TestCase(100, 0, false)]
        [TestCase(0, 3600, false)]
        [TestCase(3500, 3600, false)]
        [TestCase(3528, 3600, true)]     // 98%
        [TestCase(3585, 3600, true)]     // last 15 s
        [TestCase(84, 100, false)]
        [TestCase(85, 100, true)]        // 100 - 15
        [TestCase(5, 10, true)]          // shorter than 15 s: always finished
        public void ResumeFinishedRule(double position, double duration, bool finished) =>
            Assert.That(PlayerDataRules.IsFinished(position, duration), Is.EqualTo(finished));

        [TestCase(0, 0)]
        [TestCase(2000, 0)]
        [TestCase(2001, 1)]
        [TestCase(2500, 500)]
        public void HistoryExcess(long count, int excess) =>
            Assert.That(PlayerDataRules.HistoryExcess(count), Is.EqualTo(excess));

        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(500, true)]
        [TestCase(501, false)]
        [TestCase(-3, false)]
        public void Limits(int value, bool valid) => Assert.That(PlayerDataRules.IsValidLimit(value), Is.EqualTo(valid));

        [Test]
        public void TopPlayedCountsByKeyAndKeepsTheLatestSnapshot()
        {
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            ApiPlayerTrackDto T(string key, string title) => new() { Key = key, Title = title };
            var plays = new[]
            {
                (T("a", "A old"), t0),
                (T("b", "B"), t0.AddMinutes(1)),
                (T("a", "A new"), t0.AddMinutes(2)),
                (T("c", "C"), t0.AddMinutes(3)),
                (T("c", "C"), t0.AddMinutes(4)),
                (T("d", "D"), t0.AddMinutes(5))
            };

            var top = PlayerDataRules.TopPlayed(plays, 3);

            Assert.Multiple(() =>
            {
                Assert.That(top.Select(t => t.Track.Key), Is.EqualTo(new[] { "c", "a", "d" }));
                Assert.That(top.Select(t => t.PlayCount), Is.EqualTo(new[] { 2, 2, 1 }));
                Assert.That(top[1].Track.Title, Is.EqualTo("A new"));
                Assert.That(top[1].LastPlayedAt, Is.EqualTo(t0.AddMinutes(2)));
            });
        }
    }

    public class PlayerDataServiceTest
    {
        private MemoryPlayerDataStore _store = null!;
        private PlayerDataService _service = null!;
        private DateTime _now;
        private List<PlayerDataChangedEventDto> _events = null!;

        [SetUp]
        public void SetUp()
        {
            _store = new MemoryPlayerDataStore();
            _now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            _service = new PlayerDataService(_store, () => _now);
            _events = new List<PlayerDataChangedEventDto>();
            _service.Changed += e => _events.Add(e);
        }

        private static ApiPlayerTrackDto Track(string fileId, string? title = null) =>
            new() { ChannelId = "-100", FileId = fileId, FileName = fileId + ".mp3", Title = title };

        [Test]
        public async Task FavouriteIsIdempotentAndKeepsAddedAt()
        {
            var first = await _service.AddFavorite(Track("1", "Old"));
            _now = _now.AddHours(1);
            var again = await _service.AddFavorite(Track("1", "New"));

            Assert.Multiple(() =>
            {
                Assert.That(first.Key, Is.EqualTo("channel:-100:1"));
                Assert.That(again.AddedAt, Is.EqualTo(first.AddedAt));
                Assert.That(again.Title, Is.EqualTo("New"));
                Assert.That(_store.Favorites, Has.Count.EqualTo(1));
                Assert.That(_events.Select(e => (e.Kind, e.Key)), Is.EqualTo(new[] { ("favorites", "channel:-100:1"), ("favorites", "channel:-100:1") }));
            });
        }

        [Test]
        public async Task FavouritesAreListedNewestFirstAndPaged()
        {
            for (var i = 1; i <= 5; i++)
            {
                await _service.AddFavorite(Track(i.ToString()));
                _now = _now.AddMinutes(1);
            }

            var page = await _service.ListFavorites(2, 2);

            Assert.Multiple(() =>
            {
                Assert.That(page.Total, Is.EqualTo(5));
                Assert.That(page.Page, Is.EqualTo(2));
                Assert.That(page.PageSize, Is.EqualTo(2));
                Assert.That(page.Items.Select(f => f.FileId), Is.EqualTo(new[] { "3", "2" }));
            });
        }

        [Test]
        public async Task RemovingAFavouriteIsIdempotentAndNormalisesTheKey()
        {
            await _service.AddFavorite(new ApiPlayerTrackDto { IsLocalFile = true, FilePath = "music/a.mp3" });
            await _service.RemoveFavorite("local:/music/a.mp3");
            await _service.RemoveFavorite("local:/music/a.mp3");

            Assert.That(await _service.FavoriteKeys(), Is.Empty);
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.RemoveFavorite(null));
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.RemoveFavorite("garbage"));
        }

        [Test]
        public void InvalidPagingIsRefused()
        {
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.ListFavorites(0, 10));
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.ListFavorites(1, 0));
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.ListFavorites(1, 501));
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.ListHistory(0, null));
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.TopPlayed(501, null));
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.TopPlayed(10, 0));
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.ListResume(1000));
        }

        [Test]
        public async Task HistoryDefaultsToNowAndIsTrimmedTo2000()
        {
            var t0 = _now.AddDays(-10);
            for (var i = 0; i < PlayerDataRules.MaxHistoryEntries; i++)
                _store.History.Add(new PlayerHistoryDoc { Id = ObjectId.GenerateNewId(), Key = "k", Track = new ApiPlayerTrackDto { Key = "k" }, PlayedAt = t0.AddSeconds(i) });

            var entry = await _service.AddHistory(new ApiPlayerHistoryWriteRequest { Track = Track("1"), PositionSeconds = 31, Completed = false });

            Assert.Multiple(() =>
            {
                Assert.That(entry.PlayedAt, Is.EqualTo(_now));
                Assert.That(entry.Id, Is.Not.Empty);
                Assert.That(entry.Track.Key, Is.EqualTo("channel:-100:1"));
                Assert.That(entry.PositionSeconds, Is.EqualTo(31));
                Assert.That(_store.History, Has.Count.EqualTo(PlayerDataRules.MaxHistoryEntries));
                Assert.That(_store.History.Min(h => h.PlayedAt), Is.EqualTo(t0.AddSeconds(1)), "the oldest play goes");
                Assert.That(_events.Single().Kind, Is.EqualTo("history"));
            });
        }

        [Test]
        public async Task HistoryPagesWithBefore()
        {
            for (var i = 0; i < 5; i++)
                await _service.AddHistory(new ApiPlayerHistoryWriteRequest { Track = Track(i.ToString()), PlayedAt = new DateTimeOffset(_now.AddMinutes(i)) });

            var first = await _service.ListHistory(2, null);
            var second = await _service.ListHistory(2, first.Items.Last().PlayedAt);
            var last = await _service.ListHistory(2, second.Items.Last().PlayedAt);

            Assert.Multiple(() =>
            {
                Assert.That(first.Items.Select(i => i.Track.FileId), Is.EqualTo(new[] { "4", "3" }));
                Assert.That(first.HasMore, Is.True);
                Assert.That(second.Items.Select(i => i.Track.FileId), Is.EqualTo(new[] { "2", "1" }));
                Assert.That(last.Items.Select(i => i.Track.FileId), Is.EqualTo(new[] { "0" }));
                Assert.That(last.HasMore, Is.False);
            });
        }

        [Test]
        public void InvalidHistoryIsRefused()
        {
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.AddHistory(null));
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.AddHistory(new ApiPlayerHistoryWriteRequest()));
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.AddHistory(new ApiPlayerHistoryWriteRequest { Track = Track("1"), PositionSeconds = -1 }));
            Assert.That(_events, Is.Empty);
        }

        [Test]
        public async Task TopPlayedHonoursDays()
        {
            await _service.AddHistory(new ApiPlayerHistoryWriteRequest { Track = Track("old"), PlayedAt = new DateTimeOffset(_now.AddDays(-20)) });
            await _service.AddHistory(new ApiPlayerHistoryWriteRequest { Track = Track("old"), PlayedAt = new DateTimeOffset(_now.AddDays(-19)) });
            await _service.AddHistory(new ApiPlayerHistoryWriteRequest { Track = Track("new") });

            var all = await _service.TopPlayed(10, null);
            var week = await _service.TopPlayed(10, 7);

            Assert.Multiple(() =>
            {
                Assert.That(all.Select(t => (t.Track.FileId, t.PlayCount)), Is.EqualTo(new[] { ("old", 2), ("new", 1) }));
                Assert.That(week.Select(t => t.Track.FileId), Is.EqualTo(new[] { "new" }));
            });
        }

        [Test]
        public async Task ClearHistoryAnnouncesANullKey()
        {
            await _service.AddHistory(new ApiPlayerHistoryWriteRequest { Track = Track("1") });
            await _service.ClearHistory();
            Assert.Multiple(() =>
            {
                Assert.That(_store.History, Is.Empty);
                Assert.That(_events.Last().Kind, Is.EqualTo("history"));
                Assert.That(_events.Last().Key, Is.Null);
            });
        }

        [Test]
        public async Task ResumeIsSavedUntilFinished()
        {
            var saved = await _service.SaveResume(new ApiPlayerResumeWriteRequest { Track = Track("book"), PositionSeconds = 600, DurationSeconds = 3600 });
            var read = await _service.GetResume("channel:-100:book");

            Assert.Multiple(() =>
            {
                Assert.That(saved.Finished, Is.False);
                Assert.That(read, Is.Not.Null);
                Assert.That(read!.PositionSeconds, Is.EqualTo(600));
                Assert.That(read.DurationSeconds, Is.EqualTo(3600));
                Assert.That(read.UpdatedAt, Is.EqualTo(_now));
            });

            var finished = await _service.SaveResume(new ApiPlayerResumeWriteRequest { Track = Track("book"), PositionSeconds = 3590, DurationSeconds = 3600 });

            var afterFinish = await _service.GetResume("channel:-100:book");
            Assert.Multiple(() =>
            {
                Assert.That(finished.Finished, Is.True);
                Assert.That(afterFinish, Is.Null);
                Assert.That(_events.Select(e => e.Kind), Is.EqualTo(new[] { "resume", "resume" }));
            });
        }

        [Test]
        public async Task ResumeListIsNewestFirst()
        {
            await _service.SaveResume(new ApiPlayerResumeWriteRequest { Track = Track("a"), PositionSeconds = 10, DurationSeconds = 1000 });
            _now = _now.AddMinutes(1);
            await _service.SaveResume(new ApiPlayerResumeWriteRequest { Track = Track("b"), PositionSeconds = 10, DurationSeconds = 1000 });

            var list = await _service.ListResume(50);
            Assert.That(list.Select(r => r.Track.FileId), Is.EqualTo(new[] { "b", "a" }));

            await _service.RemoveResume("channel:-100:b");
            Assert.That((await _service.ListResume(50)).Select(r => r.Track.FileId), Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void InvalidResumeIsRefused()
        {
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.SaveResume(null));
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.SaveResume(new ApiPlayerResumeWriteRequest { PositionSeconds = 1, DurationSeconds = 10 }));
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.SaveResume(new ApiPlayerResumeWriteRequest { Track = Track("a"), PositionSeconds = -1, DurationSeconds = 10 }));
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.SaveResume(new ApiPlayerResumeWriteRequest { Track = Track("a"), PositionSeconds = 1, DurationSeconds = -10 }));
            Assert.ThrowsAsync<PlayerDataValidationException>(() => _service.GetResume(""));
        }
    }
}
