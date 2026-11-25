using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;

namespace Test
{
    /// <summary>One media type after another: each one is indexed before the next one is searched.</summary>
    public class ChannelRefreshSequentialTest
    {
        private sealed class Backend : IChannelRefreshBackend
        {
            public readonly List<string> Log = new();
            public readonly HashSet<int> Indexed = new();
            public readonly List<string> Names = new();
            public readonly Dictionary<RefreshFilter, int> Marks = new();
            public readonly Dictionary<RefreshFilter, List<TelegramChatDocuments>> Channel = new();
            public readonly HashSet<RefreshFilter> Failing = new();
            public Action<RefreshFilter>? OnSearch;

            public Task<IReadOnlyCollection<int>> LoadIndexedIdsAsync() => Task.FromResult<IReadOnlyCollection<int>>(Indexed.ToList());
            public Task<IReadOnlyCollection<string>> LoadFileNamesAsync() => Task.FromResult<IReadOnlyCollection<string>>(new List<string>());
            public Task<IReadOnlyDictionary<RefreshFilter, int>> LoadMarksAsync() =>
                Task.FromResult<IReadOnlyDictionary<RefreshFilter, int>>(new Dictionary<RefreshFilter, int>());

            public async Task<IReadOnlyList<TelegramChatDocuments>> SearchAsync(RefreshFilter filter, int floor, RefreshFilterProgress progress, CancellationToken ct)
            {
                lock (Log) Log.Add("search " + filter.ToKey());
                await Task.Delay(5, CancellationToken.None);
                OnSearch?.Invoke(filter);
                ct.ThrowIfCancellationRequested();
                if (Failing.Contains(filter)) throw new InvalidOperationException("FLOOD_WAIT_7200");
                lock (Log) Log.Add("found " + filter.ToKey());
                List<TelegramChatDocuments> found = Channel.TryGetValue(filter, out var l) ? l : new List<TelegramChatDocuments>();
                progress.ReportPage(found.Count, found.Count);
                return found;
            }

            public Task InsertAsync(IReadOnlyList<TelegramChatDocuments> batch)
            {
                Log.Add("insert " + string.Join(",", batch.Select(d => d.id)));
                foreach (var d in batch) { Indexed.Add(d.id); Names.Add(d.name); }
                return Task.CompletedTask;
            }

            public Task SaveMarksAsync(IReadOnlyDictionary<RefreshFilter, int> marks)
            {
                foreach (var kv in marks) { Marks[kv.Key] = kv.Value; Log.Add("mark " + kv.Key.ToKey()); }
                return Task.CompletedTask;
            }
        }

        private static TelegramChatDocuments Doc(int id, DocumentType type, string? name = null) =>
            new() { id = id, name = name ?? $"f{id}.bin", documentType = type, extension = ".bin" };

        private static Task<ChannelRefreshResult> Run(Backend b, ChannelRefreshProgress p, bool oneAtATime) =>
            ChannelRefreshRunner.RunAsync("1", b, new[] { RefreshFilter.Documents, RefreshFilter.Audio }, false, p, NullLogger.Instance, oneAtATime: oneAtATime);

        private static Backend Sample()
        {
            var b = new Backend();
            b.Channel[RefreshFilter.Documents] = new() { Doc(3, DocumentType.Document, "same.mp3"), Doc(1, DocumentType.Document) };
            b.Channel[RefreshFilter.Audio] = new() { Doc(4, DocumentType.Audio, "same.mp3"), Doc(3, DocumentType.Audio, "same.mp3") };
            return b;
        }

        [Test]
        public async Task EachTypeIsIndexedBeforeTheNextOne()
        {
            var b = Sample();
            var p = new ChannelRefreshProgress("1");
            var result = await Run(b, p, oneAtATime: true);

            Assert.That(b.Log, Is.EqualTo(new[]
            {
                "search documents", "found documents", "insert 3,1", "mark documents",
                "search audio", "found audio", "insert 4", "mark audio"
            }));
            Assert.That((result.Documents, result.Audios, result.Failed, result.Canceled), Is.EqualTo((2, 1, false, false)));
            Assert.That((p.NewFiles, p.Indexed), Is.EqualTo((3, 3)), "the counters add up along the types");
            Assert.That(p.Phase, Is.EqualTo(RefreshPhase.Done));
        }

        [Test]
        public async Task ItIndexesTheSameAsTheParallelRefresh()
        {
            var one = Sample();
            var all = Sample();
            var r1 = await Run(one, new ChannelRefreshProgress("1"), oneAtATime: true);
            var r2 = await Run(all, new ChannelRefreshProgress("1"), oneAtATime: false);

            Assert.That(one.Indexed, Is.EquivalentTo(all.Indexed));
            Assert.That(one.Marks, Is.EquivalentTo(all.Marks));
            Assert.That(r1.Total, Is.EqualTo(r2.Total));
            Assert.That(one.Names, Is.Unique, "a name of the first type is taken for the second one");
        }

        [Test]
        public async Task TheNextTypeWaitsForItsTurn()
        {
            var b = Sample();
            var p = new ChannelRefreshProgress("1");
            List<ApiChannelRefreshFilterProgressDto>? during = null;
            b.OnSearch = f => { if (f == RefreshFilter.Documents) during = ApiChannelRefreshProgressDto.From(p.Snapshot()).Filters; };
            await Run(b, p, oneAtATime: true);

            Assert.That(during!.Select(f => (f.Type, f.Pending)), Is.EqualTo(new[] { ("documents", false), ("audio", true) }));
            Assert.That(ApiChannelRefreshProgressDto.From(p.Snapshot()).Filters.Any(f => f.Pending), Is.False);
        }

        [Test]
        public async Task ACancelKeepsTheTypesAlreadyDone()
        {
            var b = Sample();
            var p = new ChannelRefreshProgress("1");
            b.OnSearch = f => { if (f == RefreshFilter.Audio) p.Cancel(); };
            var result = await Run(b, p, oneAtATime: true);

            Assert.That(result.Canceled, Is.True);
            Assert.That(b.Indexed, Is.EquivalentTo(new[] { 1, 3 }));
            Assert.That(b.Marks.Keys, Is.EqualTo(new[] { RefreshFilter.Documents }));
        }

        [Test]
        public async Task AFailedTypeDoesNotStopTheOthersAndSaysWhy()
        {
            var b = Sample();
            b.Failing.Add(RefreshFilter.Documents);
            var result = await Run(b, new ChannelRefreshProgress("1"), oneAtATime: true);

            Assert.That(result.Failed, Is.True);
            Assert.That(result.Error, Is.EqualTo("documents: FLOOD_WAIT_7200"));
            Assert.That(ApiChannelRefreshResultDto.From(result).Error, Is.EqualTo(result.Error));
            Assert.That(result.Audios, Is.EqualTo(2));
            Assert.That(b.Marks.Keys, Is.EqualTo(new[] { RefreshFilter.Audio }));
        }

        [Test]
        public void TheWaitOfAFloodIsReported()
        {
            var p = new ChannelRefreshProgress("1");
            var until = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            p.Filter(RefreshFilter.Audio).SetWaiting(until);
            var dto = ApiChannelRefreshProgressDto.From(p.Snapshot()).Filters.Single();
            Assert.That(dto.WaitUntil, Is.EqualTo(until));
            Assert.That(dto.WaitUntil!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            p.Filter(RefreshFilter.Audio).SetWaiting(null);
            Assert.That(ApiChannelRefreshProgressDto.From(p.Snapshot()).Filters.Single().WaitUntil, Is.Null);
        }

        [Test]
        public void TheRequestCanChooseOrLeaveItToTheSettings()
        {
            Assert.That(new RefreshChannelRequest().ToOptions().OneTypeAtATime, Is.Null);
            Assert.That(new RefreshChannelRequest { OneTypeAtATime = true }.ToOptions().OneTypeAtATime, Is.True);
        }

        [Test]
        public void TheSettingIsOffByDefaultAndExposed()
        {
            Assert.That(AppConfigDto.From(new GeneralConfig()).RefreshOneTypeAtATime, Is.False);
            Assert.That(AppConfigDto.From(new GeneralConfig { RefreshOneTypeAtATime = true }).RefreshOneTypeAtATime, Is.True);
        }
    }
}
