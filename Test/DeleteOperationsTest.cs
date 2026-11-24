using System.Diagnostics;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services.Api;
using TelegramDownloader.Services.Deletes;
using TL;

namespace Test
{
    /// <summary>
    /// The shared channel delete against an in-memory index: entries go first,
    /// then only the messages no remaining entry references (in batches of
    /// 100), only in channels the account may delete in, with folder sizes and
    /// HasChild kept consistent.
    /// </summary>
    public class ChannelDeleteRunnerTest
    {
        private sealed class FakeBackend : IChannelDeleteBackend
        {
            public readonly List<BsonFileManagerModel> Entries = new();
            public readonly List<int[]> MessageBatches = new();
            public readonly Dictionary<string, long> Subtracted = new();
            public readonly List<string> HasChildRefreshed = new();
            public int DescendantQueries;
            public Queue<Exception> MessageErrors = new();
            public Action<int>? AfterEntryDelete; // number of DeleteEntriesAsync calls so far

            private int _entryDeletes;

            public Task<List<BsonFileManagerModel>> GetDescendantsAsync(IReadOnlyCollection<string> prefixes, CancellationToken ct)
            {
                DescendantQueries++;
                return Task.FromResult(Entries.Where(e => prefixes.Any(p => (e.FilterPath ?? "").StartsWith(p, StringComparison.Ordinal))).ToList());
            }

            public Task DeleteEntriesAsync(IReadOnlyCollection<string> ids)
            {
                Entries.RemoveAll(e => ids.Contains(e.Id));
                AfterEntryDelete?.Invoke(++_entryDeletes);
                return Task.CompletedTask;
            }

            public Task<HashSet<int>> GetReferencedMessageIdsAsync(IReadOnlyCollection<int> messageIds)
            {
                // Same semantics as existItemByTelegramId, for every id at once.
                var set = new HashSet<int>();
                foreach (var e in Entries)
                {
                    if (e.MessageId.HasValue && messageIds.Contains(e.MessageId.Value)) set.Add(e.MessageId.Value);
                    foreach (var id in e.ListMessageId ?? new List<int>())
                        if (messageIds.Contains(id)) set.Add(id);
                }
                return Task.FromResult(set);
            }

            public Task DeleteMessagesAsync(IReadOnlyList<int> messageIds, CancellationToken ct)
            {
                if (MessageErrors.Count > 0) throw MessageErrors.Dequeue();
                MessageBatches.Add(messageIds.ToArray());
                return Task.CompletedTask;
            }

            public Task SubtractFolderBytesAsync(string folderId, long bytes, string? filterIdHint)
            {
                Subtracted[folderId] = Subtracted.GetValueOrDefault(folderId) + bytes;
                return Task.CompletedTask;
            }

            public Task RefreshHasChildAsync(string folderId)
            {
                HasChildRefreshed.Add(folderId);
                return Task.CompletedTask;
            }

            public BsonFileManagerModel Folder(string id, string parentId, string filterPath, string name)
            {
                var f = new BsonFileManagerModel { Id = id, ParentId = parentId, FilterPath = filterPath, Name = name, IsFile = false, FilePath = filterPath + name };
                Entries.Add(f);
                return f;
            }

            public BsonFileManagerModel File(string id, string parentId, string filterPath, string name, int? message, long size = 10, List<int>? parts = null)
            {
                var f = new BsonFileManagerModel
                {
                    Id = id, ParentId = parentId, FilterPath = filterPath, Name = name, IsFile = true, FilePath = filterPath + name,
                    MessageId = message, Size = size, isSplit = parts != null, ListMessageId = parts, FilterId = "root/" + parentId + "/"
                };
                Entries.Add(f);
                return f;
            }
        }

        private static DeleteOperation Op() => new DeleteOperation(DeleteKinds.Channel, "1", "Música", 1);

        private static ChannelDeleteRunner Runner(FakeBackend b, List<TimeSpan>? delays = null) =>
            new ChannelDeleteRunner(b, delay: (wait, ct) => { delays?.Add(wait); return Task.CompletedTask; });

        [Test]
        public async Task FolderIsDeletedRecursivelyWithItsMessages()
        {
            var b = new FakeBackend();
            var music = b.Folder("music", "root", "/", "music");
            var rock = b.Folder("rock", "music", "/music/", "rock");
            b.File("a", "music", "/music/", "a.mp3", 1, 100);
            b.File("b", "rock", "/music/rock/", "b.mp3", 2, 50);
            b.File("other", "root", "/", "other.mp3", 3, 7);
            var op = Op();

            var result = await Runner(b).RunAsync(new[] { music }, true, op);

            var s = op.Snapshot();
            Assert.Multiple(() =>
            {
                Assert.That(b.Entries.Select(e => e.Id), Is.EquivalentTo(new[] { "other" }));
                Assert.That(b.MessageBatches.SelectMany(x => x), Is.EquivalentTo(new[] { 1, 2 }));
                Assert.That(b.DescendantQueries, Is.EqualTo(1), "one query for all the descendants");
                Assert.That(b.Subtracted["music"], Is.EqualTo(100));
                Assert.That(b.Subtracted["rock"], Is.EqualTo(50));
                Assert.That(b.HasChildRefreshed, Is.EquivalentTo(new[] { "root" }));
                Assert.That(result.DeletedRootIds, Is.EquivalentTo(new[] { "music" }));
                Assert.That(s.TotalFiles, Is.EqualTo(2));
                Assert.That(s.TotalFolders, Is.EqualTo(2));
                Assert.That(s.TotalBytes, Is.EqualTo(150));
                Assert.That(s.ProcessedFiles, Is.EqualTo(2));
                Assert.That(s.ProcessedFolders, Is.EqualTo(2));
                Assert.That(s.ProcessedBytes, Is.EqualTo(150));
                Assert.That(s.MessagesDeleted, Is.EqualTo(2));
                Assert.That(s.Counted, Is.True);
            });
        }

        [Test]
        public async Task SelectedFileMessageIsDeletedOnceItsEntryIsGone()
        {
            // The bug of the old v1 path: checking the references before removing the
            // entry always found the entry itself, so the message was never deleted.
            var b = new FakeBackend();
            var file = b.File("a", "root", "/", "a.mp3", 42, 9);

            await Runner(b).RunAsync(new[] { file }, true, Op());

            Assert.That(b.MessageBatches.SelectMany(x => x), Is.EqualTo(new[] { 42 }));
            Assert.That(b.Subtracted["root"], Is.EqualTo(9));
        }

        [Test]
        public async Task MessageStillReferencedByACopyIsKept()
        {
            var b = new FakeBackend();
            var original = b.File("a", "root", "/", "a.mp3", 42);
            b.File("copy", "f", "/f/", "a.mp3", 42);

            await Runner(b).RunAsync(new[] { original }, true, Op());

            Assert.That(b.MessageBatches, Is.Empty);
            Assert.That(b.Entries.Select(e => e.Id), Is.EqualTo(new[] { "copy" }));
        }

        [Test]
        public async Task CopiesInsideTheSelectionFreeTheMessage()
        {
            var b = new FakeBackend();
            var f = b.Folder("f", "root", "/", "f");
            b.File("a", "f", "/f/", "a.mp3", 42);
            b.File("copy", "f", "/f/", "b.mp3", 42);
            var split = b.File("s", "root", "/", "big.bin", null, 30, new List<int> { 7, 8, 9 });

            await Runner(b).RunAsync(new[] { f, split }, true, Op());

            Assert.That(b.MessageBatches.SelectMany(x => x), Is.EquivalentTo(new[] { 42, 7, 8, 9 }));
            Assert.That(b.Entries, Is.Empty);
        }

        [Test]
        public async Task MessagesAreDeletedInBatchesOf100()
        {
            var b = new FakeBackend();
            var folder = b.Folder("f", "root", "/", "f");
            for (int i = 1; i <= 250; i++) b.File("x" + i, "f", "/f/", $"{i}.bin", i);

            await Runner(b).RunAsync(new[] { folder }, true, Op());

            Assert.That(b.MessageBatches.Select(x => x.Length), Is.EqualTo(new[] { 100, 100, 50 }));
            Assert.That(b.MessageBatches.SelectMany(x => x).Distinct().Count(), Is.EqualTo(250));
            Assert.That(b.Subtracted["f"], Is.EqualTo(2500));
        }

        [Test]
        public async Task ChannelsThatAreNotMineOnlyLoseTheIndexEntries()
        {
            var b = new FakeBackend();
            var file = b.File("a", "root", "/", "a.mp3", 42);
            var op = Op();

            var result = await Runner(b).RunAsync(new[] { file }, canDeleteMessages: false, op);

            Assert.Multiple(() =>
            {
                Assert.That(b.MessageBatches, Is.Empty);
                Assert.That(b.Entries, Is.Empty);
                Assert.That(op.Snapshot().IndexOnly, Is.True);
                Assert.That(result.DeletedRootIds, Is.EqualTo(new[] { "a" }));
            });
        }

        [Test]
        public async Task FloodWaitIsRespectedAndReported()
        {
            var b = new FakeBackend();
            b.MessageErrors.Enqueue(new RpcException(420, "FLOOD_WAIT_X", 30));
            var file = b.File("a", "root", "/", "a.mp3", 42);
            var op = Op();
            var phases = new List<string>();
            op.Changed += o => phases.Add(o.Phase);
            var delays = new List<TimeSpan>();

            await Runner(b, delays).RunAsync(new[] { file }, true, op);

            Assert.Multiple(() =>
            {
                Assert.That(delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(30) }));
                Assert.That(phases, Does.Contain(DeletePhases.Waiting));
                Assert.That(b.MessageBatches.SelectMany(x => x), Is.EqualTo(new[] { 42 }));
                Assert.That(op.Snapshot().WaitUntil, Is.Null);
                Assert.That(op.Snapshot().FailedCount, Is.Zero);
            });
        }

        [Test]
        public async Task TelegramErrorIsReportedAsAFailedItem()
        {
            var b = new FakeBackend();
            b.MessageErrors.Enqueue(new InvalidOperationException("MESSAGE_DELETE_FORBIDDEN"));
            var file = b.File("a", "root", "/", "a.mp3", 42);
            var op = Op();

            var result = await Runner(b).RunAsync(new[] { file }, true, op);

            Assert.Multiple(() =>
            {
                Assert.That(op.Snapshot().FailedCount, Is.EqualTo(1));
                Assert.That(op.Snapshot().Failed[0].Path, Is.EqualTo("/a.mp3"));
                Assert.That(op.Snapshot().Failed[0].Reason, Does.Contain("MESSAGE_DELETE_FORBIDDEN"));
                Assert.That(result.FailedRootIds, Is.EqualTo(new[] { "a" }));
            });
        }

        [Test]
        public void CancelStopsBetweenChunksAndKeepsTheIndexConsistent()
        {
            var b = new FakeBackend();
            var folder = b.Folder("f", "root", "/", "f");
            for (int i = 1; i <= 250; i++) b.File("x" + i, "f", "/f/", $"{i:000}.bin", i);
            var op = Op();
            b.AfterEntryDelete = n => { if (n == 1) op.Cancel(); };

            Assert.ThrowsAsync<OperationCanceledException>(() => Runner(b).RunAsync(new[] { folder }, true, op, op.Token));

            Assert.Multiple(() =>
            {
                Assert.That(b.Entries.Count(e => e.IsFile), Is.EqualTo(150), "the first chunk stays deleted");
                Assert.That(b.Entries.Any(e => e.Id == "f"), Is.True, "the folder still has files");
                Assert.That(b.MessageBatches.Select(x => x.Length), Is.EqualTo(new[] { 100 }), "the chunk in flight completes");
                Assert.That(b.Subtracted["f"], Is.EqualTo(1000));
                Assert.That(b.HasChildRefreshed, Does.Contain("f"));
                Assert.That(op.Snapshot().ProcessedFiles, Is.EqualTo(100));
            });
        }
    }

    public class DeleteOperationRegistryTest
    {
        private static async Task WaitFor(DeleteOperation op) => await op.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        [Test]
        public async Task ReportsProgressAndEndsDone()
        {
            var registry = new DeleteOperationRegistry();
            var changes = 0;
            registry.Changed += _ => Interlocked.Increment(ref changes);
            var op = registry.Start(DeleteKinds.Local, null, null, 2, (o, ct) =>
            {
                o.SetTotals(2, 1, 30);
                o.AddProcessed(2, 1, 30);
                return Task.CompletedTask;
            });
            await WaitFor(op);

            var dto = ApiDeleteOperationDto.From(registry.Get(op.Id)!.Snapshot());
            Assert.Multiple(() =>
            {
                Assert.That(dto.Phase, Is.EqualTo(DeletePhases.Done));
                Assert.That(dto.Finished, Is.True);
                Assert.That(dto.ProcessedFiles, Is.EqualTo(2));
                Assert.That(dto.TotalBytes, Is.EqualTo(30));
                Assert.That(dto.FinishedAt, Is.Not.Null);
                Assert.That(changes, Is.GreaterThanOrEqualTo(3));
                Assert.That(registry.Snapshots().Single().Id, Is.EqualTo(op.Id));
            });
        }

        [Test]
        public async Task CancelEndsCancelled()
        {
            var registry = new DeleteOperationRegistry();
            var started = new TaskCompletionSource();
            var op = registry.Start(DeleteKinds.Channel, "1", "x", 1, async (o, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            });
            await started.Task;

            Assert.That(registry.TryCancel(op.Id), Is.EqualTo(DeleteCancelResult.Cancelling));
            await WaitFor(op);

            Assert.Multiple(() =>
            {
                Assert.That(op.Phase, Is.EqualTo(DeletePhases.Cancelled));
                Assert.That(op.Snapshot().CancelRequested, Is.True);
                Assert.That(registry.TryCancel(op.Id), Is.EqualTo(DeleteCancelResult.AlreadyFinished));
                Assert.That(registry.TryCancel("nope"), Is.EqualTo(DeleteCancelResult.NotFound));
            });
        }

        [Test]
        public async Task ErrorEndsFailedWithTheMessage()
        {
            var registry = new DeleteOperationRegistry();
            var op = registry.Start(DeleteKinds.Channel, "1", null, 1, (o, ct) => throw new InvalidOperationException("mongo down"));
            await WaitFor(op);
            Assert.That(op.Snapshot().Phase, Is.EqualTo(DeletePhases.Failed));
            Assert.That(op.Snapshot().Error, Is.EqualTo("mongo down"));
        }

        [Test]
        public async Task FinishedOperationsAreKeptForTenMinutes()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var registry = new DeleteOperationRegistry(() => now);
            var op = registry.Start(DeleteKinds.Local, null, null, 1, (o, ct) => Task.CompletedTask);
            await WaitFor(op);

            now += TimeSpan.FromMinutes(9);
            Assert.That(registry.Get(op.Id), Is.Not.Null);
            now += TimeSpan.FromMinutes(2);
            Assert.That(registry.Get(op.Id), Is.Null);
            Assert.That(registry.Snapshots(), Is.Empty);
        }

        [Test]
        public async Task RunningOperationsComeFirst()
        {
            var registry = new DeleteOperationRegistry();
            var done = registry.Start(DeleteKinds.Local, null, null, 1, (o, ct) => Task.CompletedTask);
            await WaitFor(done);
            var gate = new TaskCompletionSource();
            var running = registry.Start(DeleteKinds.Local, null, null, 1, (o, ct) => gate.Task);

            Assert.That(registry.Snapshots().Select(s => s.Id), Is.EqualTo(new[] { running.Id, done.Id }));
            Assert.That(registry.Running().Select(o => o.Id), Is.EqualTo(new[] { running.Id }));
            gate.SetResult();
            await WaitFor(running);
        }

        [Test]
        public void FailuresAreCappedButCounted()
        {
            var op = new DeleteOperation(DeleteKinds.Local, null, null, 1);
            for (int i = 0; i < DeleteOperation.MaxFailuresKept + 20; i++) op.AddFailure("p" + i, "r");
            Assert.That(op.Snapshot().FailedCount, Is.EqualTo(DeleteOperation.MaxFailuresKept + 20));
            Assert.That(op.Snapshot().Failed, Has.Count.EqualTo(DeleteOperation.MaxFailuresKept));
        }

        [Test]
        public void ProgressSendStateOnlyLetsNewerVersionsThroughAndNothingAfterTheFinal()
        {
            var state = new DeleteProgressSendState();
            Assert.That(state.IsNewer("a", 1), Is.True);
            state.MarkSent("a", 3, final: false);
            Assert.That(state.IsNewer("a", 3), Is.False);
            Assert.That(state.IsNewer("a", 4), Is.True);
            state.MarkSent("a", 5, final: true);
            Assert.That(state.IsNewer("a", 99), Is.False);
        }
    }

    public class LocalDeleteRunnerTest
    {
        private string _root = null!;
        private string _outside = null!;

        [SetUp]
        public void SetUp()
        {
            var baseDir = Path.Combine(Path.GetTempPath(), "tfm-del-" + Guid.NewGuid().ToString("N"));
            _root = Path.Combine(baseDir, "root");
            _outside = Path.Combine(baseDir, "outside");
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(_outside);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(Path.GetDirectoryName(_root)!, true); } catch { }
        }

        private void Write(string relative, int bytes)
        {
            var path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[bytes]);
        }

        private static DeleteOperation Op() => new DeleteOperation(DeleteKinds.Local, null, null, 1);

        /// <summary>A junction on Windows (no admin needed), a symlink elsewhere; null when not possible.</summary>
        private static bool TryLinkDirectory(string link, string target)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
                    p!.WaitForExit();
                    return Directory.Exists(link);
                }
                Directory.CreateSymbolicLink(link, target);
                return true;
            }
            catch
            {
                return false;
            }
        }

        [Test]
        public async Task CountsThenDeletesFilesAndFoldersBottomUp()
        {
            Write("music/a.mp3", 10);
            Write("music/rock/b.mp3", 20);
            Write("music/rock/deep/c.mp3", 30);
            Write("keep.txt", 1);
            var op = Op();
            var currents = new List<string?>();
            op.Changed += o => currents.Add(o.Snapshot().CurrentItem);

            var result = await new LocalDeleteRunner(_root).RunAsync(new[] { "music" }, op);

            var s = op.Snapshot();
            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(Path.Combine(_root, "music")), Is.False);
                Assert.That(File.Exists(Path.Combine(_root, "keep.txt")), Is.True);
                Assert.That(result.Deleted, Is.EqualTo(new[] { "music" }));
                Assert.That(s.TotalFiles, Is.EqualTo(3));
                Assert.That(s.TotalFolders, Is.EqualTo(3));
                Assert.That(s.TotalBytes, Is.EqualTo(60));
                Assert.That(s.ProcessedFiles, Is.EqualTo(3));
                Assert.That(s.ProcessedFolders, Is.EqualTo(3));
                Assert.That(s.ProcessedBytes, Is.EqualTo(60));
                Assert.That(currents, Does.Contain("music/rock/deep/c.mp3"));
            });
        }

        [Test]
        public async Task RejectsTheRootAndPathsOutsideIt()
        {
            File.WriteAllText(Path.Combine(_outside, "secret.txt"), "x");
            var op = Op();

            var result = await new LocalDeleteRunner(_root).RunAsync(new[] { "", "/", "../outside/secret.txt", "missing.bin" }, op);

            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(_root), Is.True);
                Assert.That(File.Exists(Path.Combine(_outside, "secret.txt")), Is.True);
                Assert.That(result.Deleted, Is.Empty);
                Assert.That(result.Skipped, Has.Count.EqualTo(4));
                Assert.That(op.Snapshot().FailedCount, Is.EqualTo(4));
            });
        }

        [Test]
        public async Task NeverFollowsALinkInsideAFolder()
        {
            File.WriteAllText(Path.Combine(_outside, "secret.txt"), "x");
            Write("folder/a.txt", 3);
            if (!TryLinkDirectory(Path.Combine(_root, "folder", "link"), _outside)) Assert.Ignore("Cannot create directory links here");

            var result = await new LocalDeleteRunner(_root).RunAsync(new[] { "folder" }, Op());

            Assert.Multiple(() =>
            {
                Assert.That(result.Deleted, Is.EqualTo(new[] { "folder" }));
                Assert.That(Directory.Exists(Path.Combine(_root, "folder")), Is.False);
                Assert.That(File.Exists(Path.Combine(_outside, "secret.txt")), Is.True, "the link target is untouched");
            });
        }

        [Test]
        public async Task RejectsAPathReachedThroughALinkOutsideTheRoot()
        {
            File.WriteAllText(Path.Combine(_outside, "secret.txt"), "x");
            if (!TryLinkDirectory(Path.Combine(_root, "link"), _outside)) Assert.Ignore("Cannot create directory links here");
            var op = Op();

            var result = await new LocalDeleteRunner(_root).RunAsync(new[] { "link/secret.txt" }, op);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(_outside, "secret.txt")), Is.True);
                Assert.That(result.Skipped, Is.EqualTo(new[] { "link/secret.txt" }));
                Assert.That(op.Snapshot().Failed[0].Reason, Does.Contain("link"));
            });
        }

        [Test]
        public void CancelStopsAndKeepsWhatIsLeft()
        {
            for (int i = 0; i < 5; i++) Write($"many/{i}.bin", 1);
            var op = Op();
            op.Changed += o => { if (o.Snapshot().ProcessedFiles == 2) o.Cancel(); };

            Assert.ThrowsAsync<OperationCanceledException>(() => new LocalDeleteRunner(_root).RunAsync(new[] { "many" }, op, op.Token));

            Assert.That(Directory.GetFiles(Path.Combine(_root, "many")), Has.Length.EqualTo(3));
        }
    }
}
