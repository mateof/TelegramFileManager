using System.Net.Sockets;
using TelegramDownloader.Services.Upload;

namespace TelegramDownloader.Services
{
    /// <summary>
    /// Runs one Telegram request of a long scan: a FLOOD_WAIT is waited for and
    /// the same request sent again, so a limit in the middle of a channel does
    /// not throw away everything read before it.
    /// </summary>
    public static class TelegramRetry
    {
        /// <summary>FLOOD_WAIT retries of one request.</summary>
        public const int MaxFloodWaits = 10;

        /// <summary>A longer wait than this is reported as a failure.</summary>
        public static readonly TimeSpan MaxFloodWait = TimeSpan.FromHours(1);

        /// <summary>Retries of one request after a network or Telegram server error.</summary>
        public const int MaxServerErrors = 3;

        /// <param name="call">The request; it must be safe to send again.</param>
        /// <param name="waiting">Told when a FLOOD_WAIT ends, and null once it is over.</param>
        public static async Task<T> RunAsync<T>(Func<Task<T>> call, Action<DateTime?>? waiting, CancellationToken cancellationToken,
            ILogger? logger = null, Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTime>? clock = null)
        {
            delay ??= Task.Delay;
            clock ??= () => DateTime.UtcNow;
            int floods = 0;
            int errors = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan pause;
                bool flood;
                try
                {
                    return await call();
                }
                catch (Exception ex) when (UploadRetryPolicy.GetTelegramWait(ex) is TimeSpan wait && wait <= MaxFloodWait && floods < MaxFloodWaits)
                {
                    floods++;
                    flood = true;
                    pause = wait + TimeSpan.FromSeconds(1);
                    logger?.LogInformation("Telegram asked to wait {Seconds} s ({Message})", wait.TotalSeconds, ex.Message);
                }
                catch (Exception ex) when (IsServerError(ex) && errors < MaxServerErrors)
                {
                    errors++;
                    flood = false;
                    pause = UploadRetryPolicy.GetBackoff(errors, UploadRetryPolicy.BaseDelay, UploadRetryPolicy.MaxDelay);
                    logger?.LogInformation("Telegram request failed ({Message}): attempt {Attempt} in {Seconds} s", ex.Message, errors + 1, pause.TotalSeconds);
                }
                if (flood) waiting?.Invoke(clock() + pause);
                try
                {
                    await delay(pause, cancellationToken);
                }
                finally
                {
                    if (flood) waiting?.Invoke(null);
                }
            }
        }

        /// <summary>A failure of the network or of Telegram's side, worth another attempt.</summary>
        public static bool IsServerError(Exception ex) => ex switch
        {
            // 420 is a flood: either waited for above, or too long to wait.
            TL.RpcException rpc => rpc.Code < 0 || rpc.Code >= 500,
            IOException or SocketException or TimeoutException => true,
            _ => false
        };
    }
}
