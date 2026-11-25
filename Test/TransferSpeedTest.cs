using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Data;
using TelegramDownloader.Models;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;

namespace Test
{
    /// <summary>Sampling of the transfer speeds, with no page of the Blazor UI open.</summary>
    public class TransferSpeedTest
    {
        private const int MB = 1024 * 1024;

        private static TransactionInfoService NewService() => new(NullLogger<IFileService>.Instance);

        private static DownloadModel AddStreamingDownload(TransactionInfoService tis)
        {
            // What the streaming cache does: listed directly, never queued
            var dm = new DownloadModel { tis = tis, name = "video.mp4", _size = 100L * MB };
            tis.addToDownloadList(dm);
            return dm;
        }

        /// <summary>Samples one whole history interval.</summary>
        private static void SampleInterval(TransactionInfoService tis)
        {
            for (int i = 0; i < TransactionInfoService.INTERVAL_SPEED_HISTORY_SECONDS; i++)
                tis.SampleSpeeds();
        }

        [Test]
        public void DownloadOutsideTheQueueStartsTheTimer()
        {
            var tis = NewService();
            var dm = AddStreamingDownload(tis);
            dm.ProgressCallback(MB, dm._size);
            Assert.That(tis.IsTimerRunning, Is.True);
        }

        [Test]
        public void UploadedBytesStartTheTimer()
        {
            var tis = NewService();
            tis.addUploadBytes(0);
            Assert.That(tis.IsTimerRunning, Is.False, "no bytes, nothing to measure");
            tis.addUploadBytes(1);
            Assert.That(tis.IsTimerRunning, Is.True);
        }

        [Test]
        public void SpeedReachesTheSummaryAndTheHistory()
        {
            var tis = NewService();
            var dm = AddStreamingDownload(tis);
            tis.SampleSpeeds();
            dm.ProgressCallback(3L * MB, dm._size);
            tis.SampleSpeeds();

            var summary = TransferSnapshotBuilder.BuildSummary(tis);
            Assert.That(summary.DownloadSpeed, Is.EqualTo(HelperService.SizeSuffixPerTime(3L * MB)));
            Assert.That(summary.DownloadBytesPerSecond, Is.EqualTo(3L * MB));
            Assert.That(summary.UploadSpeed, Is.EqualTo("0 KB/s"));

            tis.SampleSpeeds();
            var point = TransferSnapshotBuilder.BuildSpeedHistory(tis).Download.Single();
            Assert.That(point.BytesPerSecond, Is.EqualTo(MB), "average of the interval");
            Assert.That(point.ActiveFiles, Is.EqualTo(new[] { "video.mp4" }));
        }

        [Test]
        public void OnePointPerInterval()
        {
            var tis = NewService();
            for (int i = 0; i < 4; i++)
                SampleInterval(tis);
            Assert.That(tis.GetDownloadSpeedsHistoryCopy(), Has.Count.EqualTo(4));
        }

        [Test]
        public void TimerStopsOnlyOnceTheBytesAreOutOfTheChart()
        {
            var tis = NewService();
            tis.addDownloadBytes(MB);
            tis.stopTimer();
            Assert.That(tis.IsTimerRunning, Is.True, "bytes not in the history yet");

            SampleInterval(tis);
            tis.stopTimer();
            Assert.That(tis.IsTimerRunning, Is.True, "still in the chart");

            foreach (var point in tis.GetDownloadSpeedsHistoryCopy())
                point.time = point.time.AddSeconds(-TransactionInfoService.MAX_SPEED_HISTORY_SECONDS - 1);
            SampleInterval(tis);
            tis.stopTimer();
            Assert.That(tis.IsTimerRunning, Is.False);
        }

        [Test]
        public async Task RunningTimerPublishesPoints()
        {
            var tis = NewService();
            var published = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            tis.NewSpeedHistoryPoint += (_, e) => published.TrySetResult(e.DownloadPoint.speed);
            var dm = AddStreamingDownload(tis);
            dm.ProgressCallback(3L * MB, dm._size);

            var first = await Task.WhenAny(published.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.That(first, Is.SameAs(published.Task), "no sample published");
            Assert.That(await published.Task, Is.EqualTo(MB));
        }
    }
}
