using TelegramDownloader.Models;

namespace TelegramDownloader.Services.Upload
{
    /// <summary>
    /// Retry rules shared by the upload loops of a server-folder upload.
    /// </summary>
    public static class UploadRetryPolicy
    {
        /// <summary>Attempts per file or part before the upload is marked as failed.</summary>
        public const int MaxAttempts = 3;

        /// <summary>First back-off when Telegram gives no FLOOD_WAIT value.</summary>
        public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);

        /// <summary>Upper bound of the exponential back-off (not of FLOOD_WAIT).</summary>
        public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Whether the loop sends (again): attempts left and the upload not
        /// canceled. A cancel always ends the loop, even right after a success.
        /// </summary>
        public static bool ShouldAttempt(int attemptsLeft, StateTask state)
            => attemptsLeft > 0 && state != StateTask.Canceled;

        /// <summary>
        /// Seconds Telegram asked to wait (<c>FLOOD_WAIT_X</c>, <c>FLOOD_PREMIUM_WAIT_X</c>,
        /// <c>SLOWMODE_WAIT_X</c>), looking through wrapped exceptions; null when
        /// the error carries no wait.
        /// </summary>
        public static TimeSpan? GetTelegramWait(Exception? ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is TL.RpcException rpc && rpc.X > 0
                    && (rpc.Code == 420 || rpc.Message.Contains("_WAIT_", StringComparison.Ordinal)))
                    return TimeSpan.FromSeconds(rpc.X);
                if (e is AggregateException agg)
                {
                    foreach (var inner in agg.InnerExceptions)
                    {
                        var wait = GetTelegramWait(inner);
                        if (wait != null) return wait;
                    }
                }
            }
            return null;
        }

        /// <summary>Capped exponential back-off: base, 2x base, 4x base ... up to max.</summary>
        public static TimeSpan GetBackoff(int failedAttempts, TimeSpan baseDelay, TimeSpan maxDelay)
        {
            if (failedAttempts < 1) failedAttempts = 1;
            int shift = Math.Min(failedAttempts - 1, 20);
            double ms = baseDelay.TotalMilliseconds * (1L << shift);
            return TimeSpan.FromMilliseconds(Math.Min(ms, maxDelay.TotalMilliseconds));
        }

        /// <summary>
        /// Delay before the next attempt: Telegram's requested wait when the
        /// error carries one, otherwise the capped exponential back-off.
        /// </summary>
        public static TimeSpan GetRetryDelay(Exception? ex, int failedAttempts)
            => GetRetryDelay(ex, failedAttempts, BaseDelay, MaxDelay);

        public static TimeSpan GetRetryDelay(Exception? ex, int failedAttempts, TimeSpan baseDelay, TimeSpan maxDelay)
            => GetTelegramWait(ex) ?? GetBackoff(failedAttempts, baseDelay, maxDelay);
    }
}
