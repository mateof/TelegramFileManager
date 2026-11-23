using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Persistence;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Transfers;

namespace Test
{
    /// <summary>In-memory file system for the resume planner.</summary>
    internal sealed class FakeResumeFileSystem : IResumeFileSystem
    {
        public Dictionary<string, long> Files { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public long FileLength(string path) => Files[path];
        public bool DirectoryExists(string path) => Directories.Contains(path);
    }

    public class TransferResumePlanningTest
    {
        private const string Root = "/data/local";
        private static readonly FakeResumeFileSystem NoFiles = new();

        private static PersistedTaskModel Download(StateTask state = StateTask.Pending, long transmitted = 0, long total = 100 * 1024 * 1024)
            => new()
            {
                InternalId = Guid.NewGuid().ToString(),
                Type = TaskType.Download,
                State = state,
                ChannelId = "123",
                MessageId = 42,
                Name = "movie.mkv",
                DestinationPath = "/downloads/movie.mkv",
                TotalSize = total,
                TransmittedBytes = transmitted,
                CreationDate = DateTime.Now
            };

        private static PersistedTaskModel Batch(params PersistedSourceItem[] items)
            => new()
            {
                InternalId = Guid.NewGuid().ToString(),
                Type = TaskType.BatchUpload,
                State = StateTask.Pending,
                ChannelId = "123",
                ToPath = "/backups/",
                SourceItems = items.ToList(),
                CreationDate = DateTime.Now
            };

        [Test]
        public void StartupPicksPendingWorkingAndPausedOldestFirst()
        {
            var older = Download(StateTask.Working);
            older.CreationDate = DateTime.Now.AddHours(-2);
            var newer = Download(StateTask.Pending);
            var paused = Download(StateTask.Paused);
            paused.CreationDate = DateTime.Now.AddHours(-1);
            var failed = Download(StateTask.Error);
            var canceled = Download(StateTask.Canceled);
            var live = Download(StateTask.Pending);

            var selected = TransferResumePlanner.SelectForStartup(
                new[] { newer, failed, older, canceled, paused, live }, id => id == live.InternalId);

            Assert.That(selected, Is.EqualTo(new[] { older, paused, newer }),
                "failures wait for a manual retry, live tasks are never resumed twice");
        }

        [Test]
        public void LiveTaskIsNeverResumedAgain()
        {
            var plan = TransferResumePlanner.Plan(Download(), ResumeTrigger.Manual, isLive: true, NoFiles, Root);
            Assert.That(plan.Kind, Is.EqualTo(ResumeActionKind.Skip));
        }

        [Test]
        public void FailedTaskIsOnlyRetriedByHand()
        {
            var task = Download(StateTask.Error);
            Assert.That(TransferResumePlanner.Plan(task, ResumeTrigger.Startup, false, NoFiles, Root).Kind, Is.EqualTo(ResumeActionKind.Skip));
            Assert.That(TransferResumePlanner.Plan(task, ResumeTrigger.Manual, false, NoFiles, Root).Kind, Is.EqualTo(ResumeActionKind.ResumeDownload));
        }

        [Test]
        public void DownloadContinuesFromThePartialFile()
        {
            var fs = new FakeResumeFileSystem();
            var task = Download(StateTask.Working, transmitted: 30L * 1024 * 1024);
            fs.Files[task.DestinationPath] = 40L * 1024 * 1024; // longer than confirmed (preallocated / unflushed tail)

            var plan = TransferResumePlanner.Plan(task, ResumeTrigger.Startup, false, fs, Root);

            Assert.That(plan.Kind, Is.EqualTo(ResumeActionKind.ResumeDownload));
            Assert.That(plan.ResumeOffset, Is.EqualTo(30L * 1024 * 1024),
                "the old code deleted the file and restarted from zero whenever it was longer than the recorded progress");
        }

        [Test]
        public void DownloadWithoutMessageFails()
        {
            var task = Download();
            task.MessageId = null;
            var plan = TransferResumePlanner.Plan(task, ResumeTrigger.Startup, false, NoFiles, Root);
            Assert.That(plan.Kind, Is.EqualTo(ResumeActionKind.Fail));
        }

        [Test]
        public void OffsetIsTheSmallerOfDiskAndProgressAlignedDown()
        {
            const long MB = 1024 * 1024;
            Assert.That(TransferResumePlanner.ComputeDownloadOffset(10 * MB + 5, 20 * MB, 100 * MB, 0), Is.EqualTo(10 * MB));
            Assert.That(TransferResumePlanner.ComputeDownloadOffset(20 * MB, 10 * MB + 700_000, 100 * MB, 0), Is.EqualTo(10 * MB + 512 * 1024));
            Assert.That(TransferResumePlanner.ComputeDownloadOffset(20 * MB, 10 * MB, 100 * MB, 4 * MB), Is.EqualTo(6 * MB), "margin for out-of-order chunks");
            Assert.That(TransferResumePlanner.ComputeDownloadOffset(0, 10 * MB, 100 * MB, 0), Is.EqualTo(0), "no file: start over");
            Assert.That(TransferResumePlanner.ComputeDownloadOffset(10 * MB, 0, 100 * MB, 0), Is.EqualTo(0), "nothing confirmed: start over");
            Assert.That(TransferResumePlanner.ComputeDownloadOffset(2 * MB, 2 * MB, 100 * MB, 4 * MB), Is.EqualTo(0));
            Assert.That(TransferResumePlanner.ComputeDownloadOffset(100 * MB, 100 * MB, 100 * MB, 4 * MB), Is.EqualTo(100 * MB), "finished: nothing to download");
        }

        [Test]
        public void OffsetIsAlwaysAValidTelegramOffset()
        {
            var rnd = new Random(7);
            for (int i = 0; i < 1000; i++)
            {
                long total = rnd.NextInt64(1, 4L * 1024 * 1024 * 1024);
                long offset = TransferResumePlanner.ComputeDownloadOffset(rnd.NextInt64(0, total + 1), rnd.NextInt64(0, total + 1), total, rnd.Next(0, 8) * 1024 * 1024);
                Assert.That(offset, Is.InRange(0, total));
                if (offset < total)
                    Assert.That(offset % TransferResumePlanner.ReadAlignment, Is.EqualTo(0));
            }
        }

        private static PersistedTaskModel Split(int parts, List<int> completed, long completedBytes, long transmitted)
        {
            var t = Download(StateTask.Working, transmitted);
            t.IsSplit = true;
            t.MessageIds = Enumerable.Range(100, parts).ToList();
            t.MessageId = t.MessageIds[0];
            t.CompletedPartIndices = completed;
            t.CompletedBytes = completedBytes;
            return t;
        }

        [Test]
        public void SplitDownloadContinuesAtTheFirstUnfinishedPart()
        {
            var fs = new FakeResumeFileSystem();
            var task = Split(4, new List<int> { 0, 1 }, completedBytes: 2000, transmitted: 2600);
            fs.Files[task.DestinationPath] = 2700;

            var plan = TransferResumePlanner.Plan(task, ResumeTrigger.Startup, false, fs, Root);

            Assert.That(plan.Kind, Is.EqualTo(ResumeActionKind.ResumeSplitDownload));
            Assert.That(plan.StartPart, Is.EqualTo(2));
            Assert.That(plan.CompletedBytes, Is.EqualTo(2000));
            Assert.That(plan.TransmittedInPart, Is.EqualTo(600));
            Assert.That(plan.RestartFromScratch, Is.False);
        }

        [Test]
        public void SplitDownloadRestartsWhenThePartialFileLostTheFinishedParts()
        {
            var fs = new FakeResumeFileSystem();
            var task = Split(3, new List<int> { 0 }, completedBytes: 2000, transmitted: 2500);
            fs.Files[task.DestinationPath] = 1500;

            var plan = TransferResumePlanner.Plan(task, ResumeTrigger.Startup, false, fs, Root);

            Assert.That(plan.Kind, Is.EqualTo(ResumeActionKind.ResumeSplitDownload));
            Assert.That(plan.RestartFromScratch, Is.True);
            Assert.That(plan.StartPart, Is.EqualTo(0));
        }

        [Test]
        public void LegacyPerPartRecordRestartsTheWholeSplitFile()
        {
            var task = Split(3, new List<int>(), 0, transmitted: 900);
            task.MessageId = task.MessageIds[2]; // old versions stored one record per part

            var plan = TransferResumePlanner.Plan(task, ResumeTrigger.Startup, false, NoFiles, Root);

            Assert.That(plan.Kind, Is.EqualTo(ResumeActionKind.ResumeSplitDownload));
            Assert.That(plan.RestartFromScratch, Is.True);
            Assert.That(plan.TransmittedInPart, Is.EqualTo(0));
        }

        [Test]
        public void LegacySingleUploadIsFailedInsteadOfSentUnindexed()
        {
            var task = new PersistedTaskModel { InternalId = "u", Type = TaskType.Upload, State = StateTask.Working, SourcePath = "/x" };
            var plan = TransferResumePlanner.Plan(task, ResumeTrigger.Startup, false, NoFiles, Root);
            Assert.That(plan.Kind, Is.EqualTo(ResumeActionKind.Fail));
            Assert.That(plan.Reason, Is.EqualTo(TransferResumePlanner.LegacyUploadMessage));
        }

        [Test]
        public void BatchFromAnOlderVersionCannotBeRebuilt()
        {
            var plan = TransferResumePlanner.Plan(Batch(), ResumeTrigger.Startup, false, NoFiles, Root);
            Assert.That(plan.Kind, Is.EqualTo(ResumeActionKind.Fail));
            Assert.That(plan.Reason, Is.EqualTo(TransferResumePlanner.LegacyBatchMessage));
        }

        [Test]
        public void BatchWhoseSourceVanishedIsFailedWithTheMissingPaths()
        {
            var task = Batch(
                new PersistedSourceItem { Name = "photos", FilterPath = "/", IsFile = false },
                new PersistedSourceItem { Name = "a.zip", FilterPath = "/archives/", IsFile = true });

            var plan = TransferResumePlanner.Plan(task, ResumeTrigger.Startup, false, new FakeResumeFileSystem(), Root);

            Assert.That(plan.Kind, Is.EqualTo(ResumeActionKind.Fail));
            Assert.That(plan.Reason, Does.StartWith("Source no longer exists"));
            Assert.That(plan.Reason, Does.Contain("/photos").And.Contain("/archives/a.zip"));
        }

        [Test]
        public void BatchContinuesWithTheItemsThatStillExist()
        {
            var fs = new FakeResumeFileSystem();
            var photos = new PersistedSourceItem { Name = "photos", FilterPath = "/", IsFile = false };
            var zip = new PersistedSourceItem { Name = "a.zip", FilterPath = "/archives/", IsFile = true };
            fs.Directories.Add(TransferResumePlanner.ResolveSourcePath(Root, photos));

            var plan = TransferResumePlanner.Plan(Batch(photos, zip), ResumeTrigger.Startup, false, fs, Root);

            Assert.That(plan.Kind, Is.EqualTo(ResumeActionKind.ResumeBatchUpload));
            Assert.That(plan.Items, Is.EqualTo(new[] { photos }));
            Assert.That(plan.MissingItems, Is.EqualTo(new[] { zip }));
            Assert.That(plan.Reason, Does.Contain("a.zip"));
        }

        [Test]
        public void SourcePathMatchesTheUploadPipeline()
        {
            var item = new PersistedSourceItem { Name = "a.zip", FilterPath = "\\archives\\", IsFile = true };
            Assert.That(TransferResumePlanner.ResolveSourcePath("/data/local", item), Is.EqualTo("/data/local/archives/a.zip"));
        }

        [Test]
        public void UploadedPartsAreOnlyReusedForTheSameUnchangedFile()
        {
            var file = new FileInfo(Path.GetTempFileName());
            try
            {
                File.WriteAllBytes(file.FullName, new byte[1000]);
                file.Refresh();
                var idt = new InfoDownloadTaksModel();
                idt.RecordUploadedPartAsync(file, UploadedPartMode.Memory, 400, 0, 11).Wait();
                idt.RecordUploadedPartAsync(file, UploadedPartMode.Memory, 400, 1, 12).Wait();

                Assert.That(idt.TryGetUploadedPart(file, UploadedPartMode.Memory, 400, 1), Is.EqualTo(12));
                Assert.That(idt.TryGetUploadedPart(file, UploadedPartMode.Memory, 400, 2), Is.Null);
                Assert.That(idt.TryGetUploadedPart(file, UploadedPartMode.Memory, 500, 1), Is.Null, "split differently");
                Assert.That(idt.TryGetUploadedPart(file, UploadedPartMode.Disk, 400, 1), Is.Null);

                File.AppendAllText(file.FullName, "changed");
                file.Refresh();
                Assert.That(idt.TryGetUploadedPart(file, UploadedPartMode.Memory, 400, 1), Is.Null, "the file changed");

                // A resumed task gets them back from the persisted record.
                var restored = new InfoDownloadTaksModel();
                restored.RestoreUploadInProgress(idt.GetUploadInProgress());
                Assert.That(restored.GetUploadInProgress()!.Parts.Select(p => p.MessageId), Is.EqualTo(new[] { 11, 12 }));

                // Once the file is done they are dropped.
                idt.total = 1;
                idt.AddOne(1000);
                Assert.That(idt.GetUploadInProgress(), Is.Null);
            }
            finally
            {
                File.Delete(file.FullName);
            }
        }

        [Test]
        public void BatchRecordKeepsTheSelectionAndThePartsInProgress()
        {
            var idt = new InfoDownloadTaksModel
            {
                isUpload = true,
                channelId = "123",
                toPath = "/dest/",
                files = new List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent>
                {
                    new() { Name = "a.bin", FilterPath = "/src/", IsFile = true, Size = 5 }
                }
            };
            idt.RestoreUploadInProgress(new PersistedUploadProgress { FilePath = "/x", Parts = { new PersistedUploadedPart { Index = 0, MessageId = 9 } } });

            var record = TaskPersistenceMapper.ToPersistedTask(idt);

            Assert.That(record.SourceItems.Single().Name, Is.EqualTo("a.bin"));
            Assert.That(record.CurrentFile!.Parts.Single().MessageId, Is.EqualTo(9));
            Assert.That(TaskPersistenceMapper.ToSourceContents(record.SourceItems).Single().FilterPath, Is.EqualTo("/src/"));
        }
    }

    /// <summary>State machine of the in-memory transfer models.</summary>
    public class TransferStateTransitionTest
    {
        private static TransactionInfoService Tis() => new(NullLogger<IFileService>.Instance);

        private static Task WaitForRunEnd(DownloadModel dm)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dm.RunFinished += (s, e) => tcs.TrySetResult();
            return tcs.Task;
        }

        [Test]
        public async Task FailedDownloadRunIsMarkedAsErrorWithItsReason()
        {
            var dm = new DownloadModel { tis = Tis(), name = "a", callbacks = new Callbacks() };
            dm.callbacks.callback = () => Task.FromException(new InvalidOperationException("The Telegram message 5 no longer exists in the channel"));
            int stateEvents = 0;
            dm.EventStatechanged += (s, e) => stateEvents++;
            var ended = WaitForRunEnd(dm);

            dm.RetryCallback();
            await ended.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(dm.state, Is.EqualTo(StateTask.Error), "used to stay Working forever and hold a download slot");
            Assert.That(dm.LastError, Does.Contain("no longer exists"));
            Assert.That(stateEvents, Is.EqualTo(1));
            Assert.That(dm.IsRunning, Is.False);
        }

        [Test]
        public async Task CanceledDownloadIsNotReportedAsFailed()
        {
            var tis = Tis();
            var gate = new TaskCompletionSource();
            var dm = new DownloadModel { tis = tis, name = "a", callbacks = new Callbacks() };
            dm.callbacks.callback = async () =>
            {
                await gate.Task;
                dm.ProgressCallback(10, 100); // throws: canceled
            };
            var states = new List<StateTask>();
            dm.EventStatechanged += (s, e) => states.Add(dm.state);
            var ended = WaitForRunEnd(dm);

            dm.RetryCallback();
            dm.Cancel();
            gate.SetResult();
            await ended.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(dm.state, Is.EqualTo(StateTask.Canceled));
            Assert.That(states, Is.EqualTo(new[] { StateTask.Canceled }), "the owner removes the record on cancel");
        }

        [Test]
        public async Task PausedDownloadGoesBackToTheQueueWithoutFailing()
        {
            var tis = Tis();
            var gate = new TaskCompletionSource();
            var dm = new DownloadModel { tis = tis, name = "a", callbacks = new Callbacks() };
            dm.callbacks.callback = async () =>
            {
                await gate.Task;
                dm.ProgressCallback(10, 100); // throws: paused
            };
            var ended = WaitForRunEnd(dm);

            dm.RetryCallback();
            dm.Pause();
            gate.SetResult();
            await ended.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(dm.state, Is.Not.EqualTo(StateTask.Error));
            Assert.That(dm.LastError, Is.Null);
        }

        [Test]
        public async Task RunThatEndsWithoutAFinalReportIsCompleted()
        {
            var dm = new DownloadModel { tis = Tis(), name = "a", callbacks = new Callbacks() };
            dm.callbacks.callback = () => Task.CompletedTask; // e.g. file already complete on disk
            var ended = WaitForRunEnd(dm);

            dm.RetryCallback();
            await ended.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(dm.state, Is.EqualTo(StateTask.Completed));
        }

        [Test]
        public async Task ADownloadNeverRunsTwiceAtTheSameTime()
        {
            int runs = 0;
            var gate = new TaskCompletionSource();
            var dm = new DownloadModel { tis = Tis(), name = "a", callbacks = new Callbacks() };
            dm.callbacks.callback = async () =>
            {
                Interlocked.Increment(ref runs);
                await gate.Task;
            };
            var ended = WaitForRunEnd(dm);

            dm.RetryCallback();
            dm.RetryCallback();
            dm.RetryCallback();
            Assert.That(runs, Is.EqualTo(1));
            gate.SetResult();
            await ended.WaitAsync(TimeSpan.FromSeconds(10));
        }

        private static InfoDownloadTaksModel Batch(TransactionInfoService tis, Func<Task> run, DateTime created)
        {
            var idt = new InfoDownloadTaksModel { tis = tis, total = 1, creationDate = created, callbacks = new Callbacks() };
            idt.callbacks.callback = () => run();
            return idt;
        }

        [Test]
        public async Task FailedBatchIsMarkedAndTheNextOneStarts()
        {
            var tis = Tis();
            var failNow = new TaskCompletionSource();
            var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = Batch(tis, async () =>
            {
                await failNow.Task;
                throw new DirectoryNotFoundException("Could not find /data/local/photos");
            }, DateTime.Now.AddMinutes(-1));
            var second = Batch(tis, () => { secondStarted.TrySetResult(); return Task.Delay(Timeout.Infinite); }, DateTime.Now);

            tis.addToInfoDownloadTaskList(first);
            tis.addToInfoDownloadTaskList(second);
            Assert.That(first.state, Is.EqualTo(StateTask.Working));
            Assert.That(second.state, Is.EqualTo(StateTask.Pending), "batches run one at a time");

            failNow.SetResult();
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(first.state, Is.EqualTo(StateTask.Error), "a failed batch used to stay Working and block the queue");
            Assert.That(first.LastError, Does.Contain("photos"));
            Assert.That(second.state, Is.EqualTo(StateTask.Working));
        }

        [Test]
        public async Task BatchStoppedByACanceledFileIsCanceled()
        {
            var tis = Tis();
            var idt = Batch(tis, () => Task.CompletedTask, DateTime.Now);
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            idt.EventChanged += (s, e) => { if (!idt.IsRunning) changed.TrySetResult(); };

            tis.addToInfoDownloadTaskList(idt);
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(idt.state, Is.EqualTo(StateTask.Canceled));
        }

        [Test]
        public async Task CompletedBatchStaysCompleted()
        {
            var tis = Tis();
            InfoDownloadTaksModel idt = null!;
            idt = Batch(tis, () => { idt.AddOne(10); idt.markAsCompleted(); return Task.CompletedTask; }, DateTime.Now);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            idt.EventChanged += (s, e) => { if (!idt.IsRunning) done.TrySetResult(); };

            tis.addToInfoDownloadTaskList(idt);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(idt.state, Is.EqualTo(StateTask.Completed));
            Assert.That(idt.executed, Is.EqualTo(1));
            Assert.That(idt.progress, Is.EqualTo(100));
        }
    }

    /// <summary>
    /// Persistence rules, against an in-memory stand-in for the MongoDB task
    /// collection.
    /// </summary>
    public class TaskPersistenceRulesTest
    {
        public class FakeTaskDb : DispatchProxy
        {
            public readonly List<PersistedTaskModel> Records = new();

            protected override object? Invoke(MethodInfo? method, object?[]? args)
            {
                args ??= Array.Empty<object?>();
                lock (Records)
                {
                    switch (method!.Name)
                    {
                        case nameof(IDbService.SaveTask):
                        {
                            var t = (PersistedTaskModel)args[0]!;
                            t.Id ??= Guid.NewGuid().ToString("N");
                            Records.Add(t);
                            return Task.FromResult(t);
                        }
                        case nameof(IDbService.UpdateTask):
                        {
                            var t = (PersistedTaskModel)args[0]!;
                            Records.RemoveAll(r => r.InternalId == t.InternalId);
                            Records.Add(t);
                            return Task.FromResult(t);
                        }
                        case nameof(IDbService.UpdateTaskProgress):
                        {
                            var r = Records.FirstOrDefault(x => x.InternalId == (string)args[0]!);
                            if (r != null)
                            {
                                r.TransmittedBytes = (long)args[1]!;
                                r.Progress = (int)args[2]!;
                                r.State = (StateTask)args[3]!;
                            }
                            return Task.CompletedTask;
                        }
                        case nameof(IDbService.UpdateTaskState):
                        {
                            var r = Records.FirstOrDefault(x => x.InternalId == (string)args[0]!);
                            if (r != null) r.State = (StateTask)args[1]!;
                            return Task.CompletedTask;
                        }
                        case nameof(IDbService.DeleteTask):
                            Records.RemoveAll(r => r.InternalId == (string)args[0]!);
                            return Task.CompletedTask;
                        case nameof(IDbService.GetTaskByInternalId):
                            return Task.FromResult(Records.FirstOrDefault(r => r.InternalId == (string)args[0]!));
                        case nameof(IDbService.GetAllPendingTasks):
                            return Task.FromResult(Records.Where(r => r.State != StateTask.Completed && r.State != StateTask.Canceled).ToList());
                        case nameof(IDbService.MarkTaskAsError):
                        {
                            var r = Records.FirstOrDefault(x => x.InternalId == (string)args[0]!);
                            if (r != null) { r.State = StateTask.Error; r.LastError = (string)args[1]!; r.RetryCount++; }
                            return Task.CompletedTask;
                        }
                        case nameof(IDbService.CleanupStaleTasks):
                            return Task.CompletedTask;
                    }
                }
                throw new NotSupportedException(method.Name);
            }
        }

        private static (TaskPersistenceService service, FakeTaskDb db) Create()
        {
            var proxy = DispatchProxy.Create<IDbService, FakeTaskDb>();
            return (new TaskPersistenceService(proxy, NullLogger<TaskPersistenceService>.Instance), (FakeTaskDb)(object)proxy);
        }

        private static InfoDownloadTaksModel Batch() => new()
        {
            isUpload = true,
            channelId = "1",
            toPath = "/",
            total = 3,
            files = new List<Syncfusion.Blazor.FileManager.FileManagerDirectoryContent> { new() { Name = "a", FilterPath = "/", IsFile = true } }
        };

        [Test]
        public async Task PersistingTheSameTaskTwiceKeepsOneRecord()
        {
            var (service, db) = Create();
            var idt = Batch();

            await service.PersistBatchTask(idt);
            await service.PersistBatchTask(idt); // a resume persists it again under the same id

            Assert.That(db.Records, Has.Count.EqualTo(1));
            Assert.That(idt.Persistence, Is.SameAs(service));
        }

        [Test]
        public async Task LateProgressNeverResurrectsACompletedTask()
        {
            var (service, db) = Create();
            var idt = Batch();
            await service.PersistBatchTask(idt);

            await service.MarkCompleted(idt._internalId);
            idt.executed = 2;
            await service.SaveBatchProgress(idt, force: true);
            await service.UpdateProgress(idt._internalId, 10, 50, StateTask.Working);

            Assert.That(db.Records, Is.Empty);
        }

        [Test]
        public async Task BatchProgressAndPartsArePersisted()
        {
            var (service, db) = Create();
            var idt = Batch();
            await service.PersistBatchTask(idt);
            var file = new FileInfo(Path.GetTempFileName());
            try
            {
                await idt.RecordUploadedPartAsync(file, UploadedPartMode.Disk, 1000, 0, 77);
                Assert.That(db.Records.Single().CurrentFile!.Parts.Single().MessageId, Is.EqualTo(77));

                idt.AddOne(5);
                await Task.Delay(200);
                var record = db.Records.Single();
                Assert.That(record.Executed, Is.EqualTo(1));
                Assert.That(record.CurrentFile, Is.Null, "a finished file drops its parts");
            }
            finally
            {
                File.Delete(file.FullName);
            }
        }

        [Test]
        public async Task InterruptedTasksGoBackToPendingExceptLiveOnes()
        {
            var (service, db) = Create();
            db.Records.Add(new PersistedTaskModel { InternalId = "crashed", State = StateTask.Working });
            db.Records.Add(new PersistedTaskModel { InternalId = "running", State = StateTask.Working });
            db.Records.Add(new PersistedTaskModel { InternalId = "failed", State = StateTask.Error });

            int reset = await service.ResetInterruptedTasks(id => id == "running");

            Assert.That(reset, Is.EqualTo(1));
            Assert.That(db.Records.Single(r => r.InternalId == "crashed").State, Is.EqualTo(StateTask.Pending));
            Assert.That(db.Records.Single(r => r.InternalId == "running").State, Is.EqualTo(StateTask.Working));
            Assert.That(db.Records.Single(r => r.InternalId == "failed").State, Is.EqualTo(StateTask.Error));
        }

        [Test]
        public async Task CanceledTaskIsRemoved()
        {
            var (service, db) = Create();
            var idt = Batch();
            await service.PersistBatchTask(idt);
            var tis = new TransactionInfoService(NullLogger<IFileService>.Instance);
            idt.tis = tis;

            idt.cancelTask();
            await Task.Delay(200);

            Assert.That(db.Records, Is.Empty, "canceled tasks used to come back after a restart");
        }
    }

    /// <summary>The locks that replaced System.Threading.Mutex.</summary>
    public class TransferLockingTest
    {
        [Test]
        public async Task ConcurrentCountersAreExact()
        {
            var idt = new InfoDownloadTaksModel { total = 20_000 };
            await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
            {
                for (int i = 0; i < 2_500; i++)
                    idt.AddOne(3);
            })));

            Assert.That(idt.executed, Is.EqualTo(20_000));
            Assert.That(idt.executedSize, Is.EqualTo(60_000));
            Assert.That(idt.progress, Is.EqualTo(100));
        }

        [Test]
        public void AThrowingSubscriberDoesNotLeaveTheModelLocked()
        {
            // With the Mutex, an exception between WaitOne and ReleaseMutex left
            // it owned by that thread and every other thread blocked forever.
            var tis = new TransactionInfoService(NullLogger<IFileService>.Instance);
            var um = new UploadModel { tis = tis, name = "a" };
            bool fail = true;
            um.EventChanged += (s, e) => { if (fail) throw new InvalidOperationException("subscriber bug"); };

            Assert.Throws<InvalidOperationException>(() => um.ProgressCallback(10, 100));
            fail = false;
            var other = Task.Run(() => um.ProgressCallback(20, 100));

            Assert.That(other.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(um._transmitted, Is.EqualTo(20));
        }

        [Test]
        public async Task ListsCanBeReadWhileTheyChange()
        {
            var tis = new TransactionInfoService(NullLogger<IFileService>.Instance);
            tis.isPauseDownloads = true; // queue only, nothing starts
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var errors = new List<Exception>();

            var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    var dm = new DownloadModel { tis = tis, name = "x", path = "/p", state = StateTask.Completed };
                    tis.addToPendingDownloadList(dm, chekDownloads: false);
                    tis.addToDownloadList(dm);
                    tis.addToUploadList(new UploadModel { tis = tis, state = StateTask.Completed });
                    tis.deletePendingDownloadInList(dm);
                    tis.clearUploadCompleted();
                    tis.clearDownloadCompleted();
                }
            })).ToList();
            var readers = Enumerable.Range(0, 4).Select(r => Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        // What the UI and the API do while transfers change.
                        foreach (var d in tis.GetDownloadModels(0, 50)) _ = d.name;
                        foreach (var d in tis.GetDownloadModels(0, 50, isPending: true)) _ = d.name;
                        foreach (var u in tis.uploadModels) _ = u.state;
                        _ = tis.isDownloading() || tis.isUploading() || tis.IsLive("nope") || tis.IsDestinationBusy("/p");
                    }
                    catch (Exception ex)
                    {
                        lock (errors) errors.Add(ex);
                    }
                }
            })).ToList();

            await Task.WhenAll(writers.Concat(readers));
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public async Task QueueNeverStartsMoreThanTheConfiguredDownloads()
        {
            var tis = new TransactionInfoService(NullLogger<IFileService>.Instance);
            int running = 0, maxSeen = 0;
            var release = new TaskCompletionSource();
            var models = Enumerable.Range(0, 20).Select(i =>
            {
                var dm = new DownloadModel { tis = tis, name = $"f{i}", path = $"/f{i}", callbacks = new Callbacks() };
                dm.callbacks.callback = async () =>
                {
                    int now = Interlocked.Increment(ref running);
                    lock (tis) maxSeen = Math.Max(maxSeen, now);
                    await release.Task;
                    Interlocked.Decrement(ref running);
                };
                return dm;
            }).ToList();

            await Task.WhenAll(models.Select(dm => Task.Run(() => tis.addToPendingDownloadList(dm))));
            int max = GeneralConfigStatic.config?.MaxSimultaneousDownloads ?? 1;
            Assert.That(maxSeen, Is.LessThanOrEqualTo(max));
            Assert.That(models.Count(m => m.IsRunning), Is.EqualTo(Math.Min(max, models.Count)));
            release.SetResult();
        }
    }
}
