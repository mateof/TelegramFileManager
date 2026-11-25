namespace TelegramDownloader.Services.Transfers
{
    /// <summary>
    /// Helpers for work that is started without being awaited (progress
    /// persistence, queue kicks, UI triggered transfers). A discarded task
    /// swallows its exception silently; these make sure every failure ends up
    /// in the log instead.
    /// </summary>
    public static class TaskObservation
    {
        /// <summary>
        /// Lets <paramref name="task"/> run on its own and logs it if it fails.
        /// </summary>
        public static void Observe(this Task? task, ILogger? logger, string context)
        {
            if (task == null)
                return;
            if (task.IsCompletedSuccessfully)
                return;
            task.ContinueWith(t =>
            {
                Exception? ex = t.Exception?.GetBaseException();
                if (logger != null)
                    logger.LogError(ex, "Background operation failed: {Context}", context);
                else
                    Console.Error.WriteLine($"Background operation failed: {context}: {ex}");
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }
}
