using TelegramDownloader.Services;

namespace Test
{
    /// <summary>A FLOOD_WAIT in the middle of a scan is waited for, not a failure.</summary>
    public class TelegramRetryTest
    {
        private static readonly DateTime Now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private sealed class Waits
        {
            public List<TimeSpan> Delays = new();
            public List<DateTime?> Reported = new();
            public int Calls;

            public Task<int> Run(Func<int, Exception?> failure, CancellationToken ct = default) =>
                TelegramRetry.RunAsync(() =>
                {
                    Calls++;
                    return failure(Calls) is Exception ex ? Task.FromException<int>(ex) : Task.FromResult(7);
                }, Reported.Add, ct, delay: (d, c) =>
                {
                    Delays.Add(d);
                    c.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }, clock: () => Now);
        }

        private static Exception Flood(int seconds) => new TL.RpcException(420, "FLOOD_WAIT_X", seconds);

        [Test]
        public async Task AFloodIsWaitedForAndTheSameRequestSentAgain()
        {
            var w = new Waits();
            Assert.That(await w.Run(call => call <= 2 ? Flood(90) : null), Is.EqualTo(7));
            Assert.That(w.Calls, Is.EqualTo(3));
            Assert.That(w.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(91), TimeSpan.FromSeconds(91) }));
            Assert.That(w.Reported, Is.EqualTo(new DateTime?[] { Now.AddSeconds(91), null, Now.AddSeconds(91), null }));
        }

        [Test]
        public void AVeryLongFloodFails()
        {
            var w = new Waits();
            Assert.ThrowsAsync<TL.RpcException>(() => w.Run(_ => Flood(3601)));
            Assert.That(w.Calls, Is.EqualTo(1));
            Assert.That(w.Reported, Is.Empty);
        }

        [Test]
        public void FloodsDoNotRepeatForever()
        {
            var w = new Waits();
            Assert.ThrowsAsync<TL.RpcException>(() => w.Run(_ => Flood(70)));
            Assert.That(w.Calls, Is.EqualTo(TelegramRetry.MaxFloodWaits + 1));
        }

        [Test]
        public async Task AServerErrorIsTriedAgainWithoutReportingAWait()
        {
            var w = new Waits();
            Assert.That(await w.Run(call => call switch { 1 => new TL.RpcException(-503, "Timeout"), 2 => new IOException("reset"), _ => null }), Is.EqualTo(7));
            Assert.That(w.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) }));
            Assert.That(w.Reported, Is.Empty);
        }

        [Test]
        public void ServerErrorsDoNotRepeatForever()
        {
            var w = new Waits();
            Assert.ThrowsAsync<IOException>(() => w.Run(_ => new IOException("reset")));
            Assert.That(w.Calls, Is.EqualTo(TelegramRetry.MaxServerErrors + 1));
        }

        [Test]
        public void ABadRequestIsNotTriedAgain()
        {
            var w = new Waits();
            Assert.ThrowsAsync<TL.RpcException>(() => w.Run(_ => new TL.RpcException(400, "CHANNEL_INVALID")));
            Assert.That(w.Calls, Is.EqualTo(1));
        }

        [Test]
        public void ACancelEndsTheWait()
        {
            var w = new Waits();
            using var cts = new CancellationTokenSource();
            Assert.That(async () => await w.Run(_ =>
            {
                cts.Cancel();
                return Flood(30);
            }, cts.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(w.Calls, Is.EqualTo(1));
            Assert.That(w.Reported.Last(), Is.Null, "nothing is left waiting");
        }
    }
}
