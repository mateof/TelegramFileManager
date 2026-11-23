using TelegramDownloader.Data;
using TelegramDownloader.Services.Transfers;

namespace TelegramDownloader.Services
{
    /// <summary>
    /// Resumes the persisted transfers once per process. It waits for the
    /// Telegram session to be usable (restored on startup by
    /// <see cref="TelegramSessionRestoreService"/>, or after an interactive
    /// login) - nothing can be resolved or transferred before that - and then
    /// hands over to <see cref="TaskResumeCoordinator"/>, the same code path the
    /// manual "Resume" button uses.
    /// </summary>
    public class TaskResumeService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TaskResumeService> _logger;

        public TaskResumeService(IServiceProvider serviceProvider, ILogger<TaskResumeService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("TaskResumeService waiting for the Telegram session");
                await TelegramService.WhenSessionReady.WaitAsync(stoppingToken);

                var telegram = _serviceProvider.GetRequiredService<ITelegramService>();
                try
                {
                    // Channels, folders and favourites, so API clients and the
                    // resumed transfers can resolve channels right away.
                    await telegram.PreloadChannelsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error preloading channels: {Message}", ex.Message);
                }

                var coordinator = _serviceProvider.GetRequiredService<TaskResumeCoordinator>();
                var results = await coordinator.ResumeOnStartupAsync(stoppingToken);
                int resumed = results.Count(r => r.Resumed);
                int failed = results.Count(r => r.Action == ResumeActionKind.Fail);
                _logger.LogInformation("Startup task resume finished - Resumed: {Resumed}, Failed: {Failed}, Skipped: {Skipped}",
                    resumed, failed, results.Count - resumed - failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down before a session was available.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resuming tasks on startup: {Message}", ex.Message);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
            try
            {
                // Progress held back by the debounce is written on a clean shutdown.
                await _serviceProvider.GetRequiredService<ITaskPersistenceService>().FlushPendingUpdates();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not flush the pending task progress on shutdown");
            }
        }
    }
}
