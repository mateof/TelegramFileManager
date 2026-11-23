using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Models;
using TelegramDownloader.Services;

namespace Test
{
    /// <summary>
    /// The refresh orchestration against an in-memory channel: per-filter
    /// high-water marks, cancellation and failures must leave the index and the
    /// marks consistent with each other.
    /// </summary>
    public class ChannelRefreshRunnerTest
    {
        private static TelegramChatDocuments Doc(int id, DocumentType type = DocumentType.Document, string? name = null) =>
            new TelegramChatDocuments { id = id, name = name ?? $"f{id}.bin", documentType = type, extension = ".bin" };

        private sealed class FakeBackend : IChannelRefreshBackend
        {
            public HashSet<int> Indexed = new HashSet<int>();
            public Dictionary<RefreshFilter, int> Marks = new Dictionary<RefreshFilter, int>();
            public Dictionary<RefreshFilter, List<TelegramChatDocuments>> Channel = new Dictionary<RefreshFilter, List<TelegramChatDocuments>>();
            public Dictionary<RefreshFilter, int> FloorsAsked = new Dictionary<RefreshFilter, int>();
            public List<int[]> InsertedBatches = new List<int[]>();
            public List<Dictionary<RefreshFilter, int>> SavedMarks = new List<Dictionary<RefreshFilter, int>>();
            public HashSet<RefreshFilter> FailingSearch = new HashSet<RefreshFilter>();
            public Action<RefreshFilter>? OnSearch;
            public Action<int>? BeforeInsert; // batch index

            public Task<IReadOnlyCollection<int>> LoadIndexedIdsAsync() => Task.FromResult<IReadOnlyCollection<int>>(Indexed.ToList());
            public Task<IReadOnlyCollection<string>> LoadFileNamesAsync() => Task.FromResult<IReadOnlyCollection<string>>(new List<string>());
            public Task<IReadOnlyDictionary<RefreshFilter, int>> LoadMarksAsync() =>
                Task.FromResult<IReadOnlyDictionary<RefreshFilter, int>>(new Dictionary<RefreshFilter, int>(Marks));

            public async Task<IReadOnlyList<TelegramChatDocuments>> SearchAsync(RefreshFilter filter, int floor, RefreshFilterProgress progress, CancellationToken ct)
            {
                await Task.Yield();
                lock (FloorsAsked) FloorsAsked[filter] = floor;
                OnSearch?.Invoke(filter);
                ct.ThrowIfCancellationRequested();
                if (FailingSearch.Contains(filter)) throw new InvalidOperationException("telegram down");
                List<TelegramChatDocuments> all = Channel.TryGetValue(filter, out var l) ? l : new List<TelegramChatDocuments>();
                List<TelegramChatDocuments> page = all.Where(d => d.id > floor).OrderByDescending(d => d.id)
                    .Select(d => new TelegramChatDocuments { id = d.id, name = d.name, documentType = d.documentType, extension = d.extension })
                    .ToList();
                progress.ReportPage(page.Count, all.Count);
                return page;
            }

            public Task InsertAsync(IReadOnlyList<TelegramChatDocuments> batch)
            {
                BeforeInsert?.Invoke(InsertedBatches.Count);
                InsertedBatches.Add(batch.Select(d => d.id).ToArray());
                foreach (var d in batch) Indexed.Add(d.id);
                return Task.CompletedTask;
            }

            public Task SaveMarksAsync(IReadOnlyDictionary<RefreshFilter, int> marks)
            {
                SavedMarks.Add(new Dictionary<RefreshFilter, int>(marks));
                foreach (var kv in marks) Marks[kv.Key] = kv.Value;
                return Task.CompletedTask;
            }
        }

        private static Task<ChannelRefreshResult> Run(FakeBackend b, ChannelRefreshProgress p, bool force = false, int batchSize = 1000, params RefreshFilter[] filters)
        {
            if (filters.Length == 0) filters = new[] { RefreshFilter.Documents, RefreshFilter.Audio };
            return ChannelRefreshRunner.RunAsync("1", b, filters, force, p, NullLogger.Instance, batchSize);
        }

        [Test]
        public async Task EachFilterStartsFromItsOwnMark()
        {
            var b = new FakeBackend();
            b.Marks[RefreshFilter.Documents] = 100;           // audio has no mark yet
            b.Channel[RefreshFilter.Documents] = new() { Doc(90), Doc(110) };
            b.Channel[RefreshFilter.Audio] = new() { Doc(5, DocumentType.Audio), Doc(95, DocumentType.Audio) };
            b.Indexed.Add(90);

            var result = await Run(b, new ChannelRefreshProgress("1"));

            Assert.That(b.FloorsAsked[RefreshFilter.Documents], Is.EqualTo(100));
            Assert.That(b.FloorsAsked[RefreshFilter.Audio], Is.EqualTo(0), "a filter without a mark is scanned fully");
            // The old single lastId (110) would have skipped both audio files.
            Assert.That(b.Indexed, Is.EquivalentTo(new[] { 5, 90, 95, 110 }));
            Assert.That(result.Documents, Is.EqualTo(1));
            Assert.That(result.Audios, Is.EqualTo(2));
            Assert.That(b.Marks[RefreshFilter.Documents], Is.EqualTo(110));
            Assert.That(b.Marks[RefreshFilter.Audio], Is.EqualTo(95));
            Assert.That(result.Failed || result.Canceled, Is.False);
        }

        [Test]
        public async Task ForceRescansEverySelectedFilterFromTheStart()
        {
            var b = new FakeBackend();
            b.Marks[RefreshFilter.Documents] = 100;
            b.Marks[RefreshFilter.Audio] = 50;
            b.Channel[RefreshFilter.Documents] = new() { Doc(10), Doc(110) };

            var result = await Run(b, new ChannelRefreshProgress("1"), force: true);

            Assert.That(b.FloorsAsked.Values, Is.All.EqualTo(0));
            Assert.That(b.Indexed, Is.EquivalentTo(new[] { 10, 110 }), "a file deleted from the index below the mark comes back");
            Assert.That(result.Total, Is.EqualTo(2));
        }

        [Test]
        public async Task CancelWhileSearchingIndexesNothingAndKeepsTheMarks()
        {
            var b = new FakeBackend();
            b.Marks[RefreshFilter.Documents] = 10;
            b.Channel[RefreshFilter.Documents] = new() { Doc(11), Doc(12) };
            b.Channel[RefreshFilter.Audio] = new() { Doc(3, DocumentType.Audio) };
            var p = new ChannelRefreshProgress("1");
            b.OnSearch = f => { if (f == RefreshFilter.Audio) p.Cancel(); };

            var result = await Run(b, p);

            Assert.That(result.Canceled, Is.True);
            Assert.That(result.Failed, Is.False);
            Assert.That(result.Total, Is.EqualTo(0));
            Assert.That(b.InsertedBatches, Is.Empty);
            Assert.That(b.SavedMarks, Is.Empty);
            Assert.That(p.Phase, Is.EqualTo(RefreshPhase.Done));
            Assert.That(p.Snapshot().Filters.All(f => f.Done), Is.True);
        }

        [Test]
        public async Task CancelWhileIndexingKeepsTheWrittenBatchesAndReportsThem()
        {
            var b = new FakeBackend();
            // Search returns newest first, so the first batch holds the newest files.
            b.Channel[RefreshFilter.Documents] = new() { Doc(1), Doc(2), Doc(3), Doc(4), Doc(5) };
            var p = new ChannelRefreshProgress("1");
            b.BeforeInsert = i => { if (i == 1) p.Cancel(); }; // the second batch is already in flight

            var result = await Run(b, p, batchSize: 2, filters: RefreshFilter.Documents);

            // Batches 0 and 1 got in, batch 2 was never started.
            Assert.That(b.InsertedBatches.Select(x => x.Length), Is.EqualTo(new[] { 2, 2 }));
            Assert.That(b.Indexed, Is.EquivalentTo(new[] { 5, 4, 3, 2 }));
            Assert.That(result.Canceled, Is.True);
            Assert.That(result.Failed, Is.False);
            Assert.That(result.Documents, Is.EqualTo(4), "the result counts exactly what is in the index");
            Assert.That(p.Indexed, Is.EqualTo(4));
            Assert.That(p.NewFiles, Is.EqualTo(5));
            // Message 1 is not indexed, so the mark cannot move past it: no mark at all.
            Assert.That(b.Marks.ContainsKey(RefreshFilter.Documents), Is.False);

            // The next refresh picks up the rest and only then sets the mark.
            var again = await Run(b, new ChannelRefreshProgress("1"), batchSize: 2, filters: RefreshFilter.Documents);
            Assert.That(again.Documents, Is.EqualTo(1));
            Assert.That(b.Marks[RefreshFilter.Documents], Is.EqualTo(5));
        }

        [Test]
        public async Task InsertFailureStopsAndTheMarkStaysBelowTheGap()
        {
            var b = new FakeBackend();
            b.Marks[RefreshFilter.Documents] = 10;
            b.Indexed.UnionWith(new[] { 1, 10 });
            b.Channel[RefreshFilter.Documents] = new() { Doc(1), Doc(10), Doc(11), Doc(12), Doc(13), Doc(14) };
            // Batches (newest first, size 2): [14,13] ok, [12,11] fails.
            b.BeforeInsert = i => { if (i == 1) throw new IOException("mongo down"); };

            var result = await Run(b, new ChannelRefreshProgress("1"), batchSize: 2, filters: RefreshFilter.Documents);

            Assert.That(result.Failed, Is.True);
            Assert.That(result.Documents, Is.EqualTo(2));
            Assert.That(b.Indexed, Is.EquivalentTo(new[] { 1, 10, 13, 14 }));
            Assert.That(b.Marks[RefreshFilter.Documents], Is.EqualTo(10), "11 is missing, so the mark must not pass it");
        }

        [Test]
        public async Task AFailedFilterKeepsItsMarkWhileTheOthersAreIndexed()
        {
            var b = new FakeBackend();
            b.Marks[RefreshFilter.Audio] = 7;
            b.Channel[RefreshFilter.Documents] = new() { Doc(20) };
            b.Channel[RefreshFilter.Audio] = new() { Doc(21, DocumentType.Audio) };
            b.FailingSearch.Add(RefreshFilter.Audio);

            var result = await Run(b, new ChannelRefreshProgress("1"));

            Assert.That(result.Failed, Is.True);
            Assert.That(b.Indexed, Is.EquivalentTo(new[] { 20 }));
            Assert.That(b.Marks[RefreshFilter.Documents], Is.EqualTo(20));
            Assert.That(b.Marks[RefreshFilter.Audio], Is.EqualTo(7));
        }

        [Test]
        public async Task AMessageClaimedByAnEarlierFilterStillAdvancesTheLaterOne()
        {
            var b = new FakeBackend();
            // An audio file with a filename is found by both the documents and the audio search.
            b.Channel[RefreshFilter.Documents] = new() { Doc(30) };
            b.Channel[RefreshFilter.Audio] = new() { Doc(30, DocumentType.Audio) };

            var result = await Run(b, new ChannelRefreshProgress("1"));

            Assert.That(result.Total, Is.EqualTo(1));
            Assert.That(b.Marks[RefreshFilter.Documents], Is.EqualTo(30));
            Assert.That(b.Marks[RefreshFilter.Audio], Is.EqualTo(30));
        }

        [Test]
        public async Task NamelessEntriesDoNotHoldTheMarkBack()
        {
            var b = new FakeBackend();
            b.Channel[RefreshFilter.Documents] = new() { Doc(1), new TelegramChatDocuments { id = 2, name = null! }, Doc(3) };

            var result = await Run(b, new ChannelRefreshProgress("1"), filters: RefreshFilter.Documents);

            Assert.That(result.Documents, Is.EqualTo(2));
            Assert.That(b.Marks[RefreshFilter.Documents], Is.EqualTo(3));
        }

        [Test]
        public async Task NothingNewLeavesTheMarksUntouched()
        {
            var b = new FakeBackend();
            b.Marks[RefreshFilter.Documents] = 5;
            b.Marks[RefreshFilter.Audio] = 9;
            b.Channel[RefreshFilter.Documents] = new() { Doc(5) };

            var p = new ChannelRefreshProgress("1");
            var result = await Run(b, p);

            Assert.That(result.Total, Is.EqualTo(0));
            Assert.That(b.SavedMarks, Is.Empty);
            Assert.That(p.Phase, Is.EqualTo(RefreshPhase.Done));
            Assert.That(p.NewFiles, Is.EqualTo(0));
        }
    }
}
