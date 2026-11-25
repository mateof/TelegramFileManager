using TelegramDownloader.Models;
using TelegramDownloader.Services.Upload;
using TL;

namespace Test
{
    /// <summary>
    /// Retry rules of the server-folder upload loops: when to (re)send, and
    /// how long to wait between attempts.
    /// </summary>
    public class UploadRetryPolicyTest
    {
        [TestCase(3, StateTask.Working, ExpectedResult = true)]
        [TestCase(1, StateTask.Pending, ExpectedResult = true)]
        [TestCase(0, StateTask.Working, ExpectedResult = false)]
        [TestCase(-1, StateTask.Working, ExpectedResult = false)]
        [TestCase(3, StateTask.Canceled, ExpectedResult = false)]
        [TestCase(0, StateTask.Canceled, ExpectedResult = false)]
        public bool ShouldAttempt(int attemptsLeft, StateTask state)
            => UploadRetryPolicy.ShouldAttempt(attemptsLeft, state);

        [Test]
        public void Loop_stops_after_a_cancel_that_lands_once_the_file_was_sent()
        {
            // Mirrors the upload loop: the send succeeds (attempts = 0) and the
            // user cancels at the same time. The old "attempts != 0 || canceled"
            // condition would send the file again.
            int attempts = UploadRetryPolicy.MaxAttempts;
            var state = StateTask.Working;
            int sends = 0;
            while (UploadRetryPolicy.ShouldAttempt(attempts, state))
            {
                sends++;
                attempts = 0;
                state = StateTask.Canceled;
            }
            Assert.That(sends, Is.EqualTo(1));

            bool oldCondition = attempts != 0 || state == StateTask.Canceled;
            Assert.That(oldCondition, Is.True, "the old loop would have run again");
        }

        [Test]
        public void Backoff_is_exponential_and_capped()
        {
            var b = TimeSpan.FromSeconds(5);
            var max = TimeSpan.FromSeconds(60);
            Assert.Multiple(() =>
            {
                Assert.That(UploadRetryPolicy.GetBackoff(1, b, max), Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(UploadRetryPolicy.GetBackoff(2, b, max), Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(UploadRetryPolicy.GetBackoff(3, b, max), Is.EqualTo(TimeSpan.FromSeconds(20)));
                Assert.That(UploadRetryPolicy.GetBackoff(5, b, max), Is.EqualTo(TimeSpan.FromSeconds(60)));
                Assert.That(UploadRetryPolicy.GetBackoff(1000, b, max), Is.EqualTo(TimeSpan.FromSeconds(60)));
                Assert.That(UploadRetryPolicy.GetBackoff(0, b, max), Is.EqualTo(TimeSpan.FromSeconds(5)));
            });
        }

        [Test]
        public void Flood_wait_from_Telegram_wins_over_the_backoff()
        {
            var flood = new RpcException(420, "FLOOD_WAIT_X", 137);
            Assert.That(UploadRetryPolicy.GetRetryDelay(flood, 1), Is.EqualTo(TimeSpan.FromSeconds(137)));
            // Not capped: Telegram rejects any earlier retry anyway.
            Assert.That(UploadRetryPolicy.GetRetryDelay(new RpcException(420, "FLOOD_WAIT_X", 900), 1),
                Is.EqualTo(TimeSpan.FromSeconds(900)));
        }

        [Test]
        public void Flood_wait_is_found_inside_wrapped_exceptions()
        {
            var flood = new RpcException(420, "FLOOD_WAIT_X", 42);
            Assert.Multiple(() =>
            {
                Assert.That(UploadRetryPolicy.GetTelegramWait(new Exception("outer", flood)), Is.EqualTo(TimeSpan.FromSeconds(42)));
                Assert.That(UploadRetryPolicy.GetTelegramWait(new AggregateException(new Exception("x"), flood)), Is.EqualTo(TimeSpan.FromSeconds(42)));
                Assert.That(UploadRetryPolicy.GetTelegramWait(new RpcException(400, "SLOWMODE_WAIT_X", 30)), Is.EqualTo(TimeSpan.FromSeconds(30)));
            });
        }

        [Test]
        public void Errors_without_a_wait_use_the_backoff()
        {
            Assert.Multiple(() =>
            {
                Assert.That(UploadRetryPolicy.GetTelegramWait(new RpcException(400, "FILE_PARTS_INVALID")), Is.Null);
                Assert.That(UploadRetryPolicy.GetTelegramWait(new IOException("disk")), Is.Null);
                Assert.That(UploadRetryPolicy.GetTelegramWait(null), Is.Null);
                Assert.That(UploadRetryPolicy.GetRetryDelay(new IOException("disk"), 2), Is.EqualTo(TimeSpan.FromSeconds(10)));
            });
        }
    }
}
