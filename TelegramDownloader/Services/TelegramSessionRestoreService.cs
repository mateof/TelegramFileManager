using TelegramDownloader.Data;

namespace TelegramDownloader.Services
{
    /// <summary>
    /// Restores the previously authorized Telegram session on application
    /// startup (e.g. after a container restart), so API clients and WebDAV
    /// work without having to open the web UI first. When no previous session
    /// exists this does nothing and the interactive (web) login is still
    /// required.
    /// </summary>
    public class TelegramSessionRestoreService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TelegramSessionRestoreService> _logger;

        public TelegramSessionRestoreService(IServiceProvider serviceProvider, ILogger<TelegramSessionRestoreService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Run in the background so a slow Telegram connection never delays
            // the web server startup.
            _ = Task.Run(async () =>
            {
                try
                {
                    var telegram = _serviceProvider.GetRequiredService<ITelegramService>();
                    if (!telegram.IsConfigured)
                    {
                        _logger.LogInformation("Telegram not configured - skipping automatic session restore");
                        return;
                    }
                    bool restored = await telegram.TryRestoreSessionAsync();
                    if (!restored)
                        _logger.LogInformation("No previous Telegram session to restore - login through the web UI or /api/v1/auth");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Automatic Telegram session restore failed on startup");
                }
            }, cancellationToken);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
