using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Data;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Streams;

namespace Test
{
    /// <summary>Pausing and resuming one download or one upload.</summary>
    [NonParallelizable]
    public class TransferPauseTest
    {
        private static async Task Until(Func<bool> condition, string what, int seconds = 10)
        {
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (!condition())
            {
                if (DateTime.UtcNow > end) Assert.Fail("Timed out: " + what);
                await Task.Delay(10);
            }
        }

        private static TransactionInfoService Tis() => new(NullLogger<IFileService>.Instance);

        /// <summary>A queued download that moves 10 bytes every few milliseconds, from where it was.</summary>
        private static DownloadModel Queued(TransactionInfoService tis, string name, List<string>? starts = null)
        {
            var dm = new DownloadModel { tis = tis, name = name, _size = 2000, state = StateTask.Pending, callbacks = new Callbacks() };
            dm.callbacks.callback = async () =>
            {
                lock (starts ?? new()) starts?.Add($"{name}@{dm._transmitted}");
                while (dm._transmitted < dm._size)
                {
                    await Task.Delay(5);
                    dm.ProgressCallback(dm._transmitted + 10, dm._size);
                }
            };
            tis.addToPendingDownloadList(dm);
            return dm;
        }

        #region Downloads

        [Test]
        public async Task APausedDownloadWaitsForTheUserAndGoesOnWhereItWas()
        {
            var tis = Tis();
            var starts = new List<string>();
            var first = Queued(tis, "first", starts);
            await Until(() => first._transmitted > 50, "first runs");

            Assert.That(tis.PauseDownload(first), Is.True);
            await Until(() => !first.IsRunning, "first stops");
            var at = first._transmitted;

            var dto = TransferDto.FromDownload(first, isQueued: true);
            Assert.That((dto.State, dto.CanPause, dto.CanRetry), Is.EqualTo(("Paused", true, true)));

            // The queue moves: another one takes its place, and it stays paused
            var second = Queued(tis, "second", starts);
            await Until(() => second._transmitted > 50, "second runs");
            await tis.CheckPendingDownloads();
            Assert.Multiple(() =>
            {
                Assert.That(first.IsRunning, Is.False);
                Assert.That(first._transmitted, Is.EqualTo(at));
                Assert.That(tis.pendingDownloadModels, Is.EqualTo(new[] { first }));
            });

            second.Cancel();
            await Until(() => !second.IsRunning, "second stops");
            Assert.That(first.IsRunning, Is.False, "nor when the running one ends");

            tis.ResumeDownload(first);
            await Until(() => first.state == StateTask.Completed, "first completes", 30);
            Assert.That(starts.Where(s => s.StartsWith("first")), Is.EqualTo(new[] { "first@0", $"first@{at}" }));
        }

        [Test]
        public async Task PausingTheRunningOneLetsTheNextStart()
        {
            var tis = Tis();
            var first = Queued(tis, "first");
            var second = Queued(tis, "second");
            await Until(() => first._transmitted > 0, "first runs");
            Assert.That(second.IsRunning, Is.False);

            tis.PauseDownload(first);

            await Until(() => second._transmitted > 0, "second starts");
            second.Cancel();
        }

        [Test]
        public async Task AQueuedDownloadCanBePausedAndResumingAllStartsEverything()
        {
            var tis = Tis();
            var first = Queued(tis, "first");
            var second = Queued(tis, "second");
            await Until(() => first._transmitted > 0, "first runs");

            Assert.That(tis.PauseDownload(second), Is.True);
            Assert.That(TransferDto.FromDownload(second, true).State, Is.EqualTo("Paused"));
            first.Cancel();
            await Until(() => !first.IsRunning, "first stops");
            await Task.Delay(100);
            Assert.That(second.IsRunning, Is.False);

            tis.PlayDownloads();
            await Until(() => second._transmitted > 0, "second starts");
            second.Cancel();
        }

        [Test]
        public async Task ResumingWhileThePauseIsTakingEffectKeepsItInTheList()
        {
            var tis = Tis();
            var gate = new TaskCompletionSource();
            int runs = 0;
            var dm = new DownloadModel { tis = tis, name = "a", _size = 100, state = StateTask.Pending, callbacks = new Callbacks() };
            dm.callbacks.callback = async () =>
            {
                if (Interlocked.Increment(ref runs) == 1) await gate.Task;
                else await Task.Delay(300);
                dm.ProgressCallback(100, 100);
            };
            tis.addToPendingDownloadList(dm);
            await Until(() => runs == 1, "runs");

            tis.PauseDownload(dm);
            dm.UserPaused = false;
            dm.RetryCallback();   // what the queue does on a resume: replayed when the run ends
            gate.SetResult();

            await Until(() => runs == 2, "runs again");
            Assert.That(tis.downloadModels, Does.Contain(dm));
            await Until(() => dm.state == StateTask.Completed, "completes");
        }

        [Test]
        public void EveryParallelPartStopsAfterAPause()
        {
            var dm = new DownloadModel { tis = Tis(), name = "a", callbacks = new Callbacks() };
            dm.Pause();
            for (int part = 0; part < 3; part++)
                Assert.Throws<TransferStoppedException>(() => dm.ReportParallelProgress(0, 10, 100), "only the first one used to notice");
            Assert.Throws<TransferStoppedException>(() => dm.throwIfStopped());
        }

        [Test]
        public async Task ADownloadThatCannotStartAgainIsNotPausedNorCompletedByARetry()
        {
            var tis = Tis();
            var dm = new DownloadModel { tis = tis, name = "request", _size = 100 };
            tis.addToDownloadList(dm);

            Assert.That(tis.PauseDownload(dm), Is.False);
            tis.PauseDownloads();
            Assert.That(dm.state, Is.EqualTo(StateTask.Working), "pausing all leaves it alone");
            var dto = TransferDto.FromDownload(dm);
            Assert.That((dto.CanPause, dto.CanRetry), Is.EqualTo((false, false)));

            dm.Cancel();
            dm.RetryCallback();
            await Until(() => dm.state == StateTask.Error, "fails");
        }

        [Test]
        public async Task CancellingAPausedDownloadTakesItOutOfTheQueue()
        {
            var tis = Tis();
            var dm = Queued(tis, "a");
            await Until(() => dm._transmitted > 0, "runs");
            tis.PauseDownload(dm);
            await Until(() => !dm.IsRunning, "stops");

            dm.Cancel();

            Assert.That(dm.state, Is.EqualTo(StateTask.Canceled));
            Assert.That(tis.pendingDownloadModels, Is.Empty);
        }

        #endregion

        #region Streaming cache

        private string _temp = null!;

        [SetUp]
        public void SetUp()
        {
            _temp = Path.Combine(Path.GetTempPath(), "tfm-pause-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
            MediaFakes.Delay = 20;
            MediaFakes.Bytes = new byte[20 * 1024 * 1024 + 777];
            new Random(2).NextBytes(MediaFakes.Bytes);
            MediaFakes.TgMessage = MediaFakes.VideoMessage(7, MediaFakes.Bytes.Length, "Movie.mkv", "video/x-matroska");
        }

        [TearDown]
        public void TearDown()
        {
            MediaFakes.Delay = 1;
            try { Directory.Delete(_temp, true); } catch { /* still writing */ }
        }

        [Test]
        public async Task ThePlaybackCacheCanBePausedAndResumed()
        {
            using var services = MediaFakes.Services();
            var tis = services.GetRequiredService<TransactionInfoService>();
            var cache = services.GetRequiredService<IProgressiveDownloadService>();
            var entry = new BsonFileManagerModel { Id = "f1", Name = "Movie.mkv", Type = ".mkv", IsFile = true, Size = MediaFakes.Bytes.Length, MessageId = 7 };
            var path = Path.Combine(_temp, "1-7-Movie.mkv");

            var info = await cache.StartOrGetDownloadAsync("k", "1", entry, path);
            var dm = tis.downloadModels.Single();
            await Until(() => info.DownloadedBytes > 2 * 1024 * 1024, "downloads");

            Assert.That(tis.PauseDownload(dm), Is.True);
            await Until(() => !dm.IsRunning, "stops");
            var at = info.DownloadedBytes;
            Assert.Multiple(() =>
            {
                Assert.That(TransferDto.FromDownload(dm, true).State, Is.EqualTo("Paused"), "it used to end as cancelled");
                Assert.That(tis.pendingDownloadModels, Does.Contain(dm));
                Assert.That(info.IsDownloading, Is.False);
            });

            // Playing meanwhile does not start it again behind the user's back
            info.CancelledAtUtc = DateTime.UtcNow.AddDays(-1);
            Assert.That(await cache.StartOrGetDownloadAsync("k", "1", entry, path), Is.SameAs(info));
            await Task.Delay(100);
            Assert.That(info.DownloadedBytes, Is.EqualTo(at));
            Assert.That(tis.downloadModels, Is.Empty);

            MediaFakes.Delay = 1;
            tis.ResumeDownload(dm);
            await Until(() => dm.state == StateTask.Completed && !dm.IsRunning, "completes", 30);
            Assert.That(info.IsComplete, Is.True);
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(MediaFakes.Bytes));
        }

        [Test]
        public async Task ACancelledCacheDownloadCanBeRetried()
        {
            using var services = MediaFakes.Services();
            var tis = services.GetRequiredService<TransactionInfoService>();
            var cache = services.GetRequiredService<IProgressiveDownloadService>();
            var entry = new BsonFileManagerModel { Id = "f1", Name = "Movie.mkv", Type = ".mkv", IsFile = true, Size = MediaFakes.Bytes.Length, MessageId = 7 };
            var path = Path.Combine(_temp, "1-7-Movie.mkv");
            var info = await cache.StartOrGetDownloadAsync("k", "1", entry, path);
            var dm = tis.downloadModels.Single();
            await Until(() => info.DownloadedBytes > 1024 * 1024, "downloads");
            dm.Cancel();
            await Until(() => !dm.IsRunning, "stops");
            Assert.That(dm.state, Is.EqualTo(StateTask.Canceled));

            MediaFakes.Delay = 1;
            Assert.That(tis.RetryDownload(dm), Is.True);

            await Until(() => dm.state == StateTask.Completed && !dm.IsRunning, "completes", 30);
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(MediaFakes.Bytes), "a retry used to say completed without downloading");
        }

        #endregion

        #region Uploads

        private static UploadModel Upload(TransactionInfoService tis)
        {
            var um = new UploadModel { tis = tis, name = "up.bin", _size = 1000, CanPause = true };
            tis.addToUploadList(um);
            return um;
        }

        /// <summary>Sends a stream in parts, as the Telegram client does.</summary>
        private static async Task<List<byte>> Send(Stream source, UploadModel um)
        {
            var sent = new List<byte>();
            var buffer = new byte[100];
            int n;
            while ((n = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                sent.AddRange(buffer.Take(n));
                await Task.Delay(10);
                um.ProgressCallback(sent.Count, source.Length);
            }
            return sent;
        }

        [Test]
        public async Task AnUploadStopsSendingWhilePausedAndLosesNothing()
        {
            var tis = Tis();
            var um = Upload(tis);
            var data = new byte[1000];
            new Random(3).NextBytes(data);
            var send = Send(new PausableUploadStream(new MemoryStream(data), um), um);
            await Until(() => um._transmitted >= 200, "sends");

            Assert.That(um.Pause(), Is.True);
            Assert.That(um.Pause(), Is.False, "already paused");
            await Task.Delay(60);
            var at = um._transmitted;
            await Task.Delay(150);
            Assert.Multiple(() =>
            {
                Assert.That(um._transmitted, Is.EqualTo(at).And.LessThan(1000));
                Assert.That(um.state, Is.EqualTo(StateTask.Paused));
                Assert.That(send.IsCompleted, Is.False);
                Assert.That(TransferDto.FromUpload(um).State, Is.EqualTo("Paused"));
            });

            tis.clearUploadCompleted();
            Assert.That(tis.uploadModels, Does.Contain(um), "it is not a finished one");

            Assert.That(um.Resume(), Is.True);
            Assert.That(await send.WaitAsync(TimeSpan.FromSeconds(10)), Is.EqualTo(data));
            Assert.That(um.state, Is.EqualTo(StateTask.Completed));
            Assert.That(um.Resume(), Is.False);
        }

        [Test]
        public async Task CancellingATaskReachesItsPausedUpload()
        {
            var tis = Tis();
            var um = Upload(tis);
            var task = new InfoDownloadTaksModel { tis = tis };
            task.addUpload(um);
            var send = Send(new PausableUploadStream(new MemoryStream(new byte[1000]), um), um);
            await Until(() => um._transmitted >= 100, "sends");
            um.Pause();
            await Task.Delay(60);

            task.cancelTask();

            Assert.ThrowsAsync<TransferStoppedException>(() => send.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.That(um.state, Is.EqualTo(StateTask.Canceled));
        }

        [Test]
        public void OnlyRealUploadsCanBePaused()
        {
            var tis = Tis();
            var split = new SplitModel { tis = tis, name = "split" };
            Assert.That(split.Pause(), Is.False);
            Assert.That(TransferDto.FromUpload(split).CanPause, Is.False);
        }

        #endregion
    }
}
