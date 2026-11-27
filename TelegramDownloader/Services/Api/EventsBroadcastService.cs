using Microsoft.AspNetCore.SignalR;
using TelegramDownloader.Data;
using TelegramDownloader.Hubs;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services.Deletes;
using TelegramDownloader.Services.Library;
using TelegramDownloader.Services.Player;

namespace TelegramDownloader.Services.Api
{
    /// <summary>
    /// Bridges the in-process events of the app to the <see cref="EventsHub"/>,
    /// the same way <see cref="TransferBroadcastService"/> does for transfers.
    ///
    /// Push sources: <see cref="NotificationModel.EventNotification"/>,
    /// <see cref="FileService.ChannelRefreshFinished"/>,
    /// <see cref="ImportProgressTracker.Progress"/>,
    /// <see cref="TelegramService.OnUserLoggedIn"/>,
    /// <see cref="DeleteOperationRegistry.Changed"/> (<c>DeleteProgress</c>, throttled
    /// to <see cref="DeleteThrottle"/>; the ticker sends a change the throttle held back) and
    /// <see cref="ISystemMetricsService.OnMetricsUpdated"/> and
    /// <see cref="PlayerDataService.Changed"/> (<c>PlayerDataChanged</c>). Refresh progress,
    /// the library scan and the Telegram session state have no events, so a
    /// one-second ticker polls them (which is also the throttle of
    /// <c>ChannelRefreshProgress</c>).
    /// </summary>
    public sealed class EventsBroadcastService : IHostedService, IDisposable
    {
        /// <summary>Poll period of refresh progress, library scan and session state.</summary>
        public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

        /// <summary>Minimum interval between two <c>ImportProgress</c> messages of the same import.</summary>
        public static readonly TimeSpan ImportThrottle = TimeSpan.FromMilliseconds(250);

        private readonly IHubContext<EventsHub> _hub;
        private readonly EventsHubState _state;
        private readonly ImportProgressTracker _imports;
        private readonly ISystemMetricsService _metrics;
        private readonly LibraryScanService _scan;
        private readonly ILogger<EventsBroadcastService> _logger;

        /// <summary>Minimum interval between two <c>DeleteProgress</c> messages of the same operation (4 per second).</summary>
        public static readonly TimeSpan DeleteThrottle = TimeSpan.FromMilliseconds(250);

        private readonly KeyedThrottle _importThrottle = new KeyedThrottle(ImportThrottle);
        private readonly KeyedThrottle _deleteThrottle = new KeyedThrottle(DeleteThrottle);
        private readonly DeleteProgressSendState _deleteSent = new DeleteProgressSendState();
        private readonly DeleteOperationRegistry _deletes;
        private readonly PlayerDataService _player;
        private readonly ChangeDetector<bool> _authState = new ChangeDetector<bool>();
        private bool _scanWasRunning;
        private int _ticking;
        private Timer? _timer;

        public EventsBroadcastService(
            IHubContext<EventsHub> hub,
            EventsHubState state,
            ImportProgressTracker imports,
            ISystemMetricsService metrics,
            LibraryScanService scan,
            DeleteOperationRegistry deletes,
            PlayerDataService player,
            ILogger<EventsBroadcastService> logger)
        {
            _player = player;
            _deletes = deletes;
            _hub = hub;
            _state = state;
            _imports = imports;
            _metrics = metrics;
            _scan = scan;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            NotificationModel.EventNotification += OnNotification;
            FileService.ChannelRefreshFinished += OnChannelRefreshFinished;
            TelegramService.OnUserLoggedIn += OnUserLoggedIn;
            _imports.Progress += OnImportProgress;
            _metrics.OnMetricsUpdated += OnMetricsUpdated;
            _deletes.Changed += OnDeleteChanged;
            _player.Changed += OnPlayerDataChanged;
            _timer = new Timer(_ => Tick(), null, TickInterval, TickInterval);
            _logger.LogInformation("EventsBroadcastService started - streaming application events on {Path}", "/hubs/events");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            NotificationModel.EventNotification -= OnNotification;
            FileService.ChannelRefreshFinished -= OnChannelRefreshFinished;
            TelegramService.OnUserLoggedIn -= OnUserLoggedIn;
            _imports.Progress -= OnImportProgress;
            _metrics.OnMetricsUpdated -= OnMetricsUpdated;
            _deletes.Changed -= OnDeleteChanged;
            _player.Changed -= OnPlayerDataChanged;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            return Task.CompletedTask;
        }

        private void OnNotification(object? sender, Notification n)
        {
            if (!EventMapping.ShouldForward(n, GeneralConfigStatic.config?.ShouldNotify ?? false)) return;
            var dto = EventMapping.ToDto(n, DateTime.UtcNow);
            _ = SafeSend(() => _hub.Clients.All.SendAsync(EventsHub.NotificationMessage, dto));
        }

        private void OnChannelRefreshFinished(ChannelRefreshResult result)
        {
            var dto = new ChannelRefreshedEventDto
            {
                ChannelId = result.ChannelId,
                Result = ApiChannelRefreshResultDto.From(result)
            };
            _ = SafeSend(() => _hub.Clients.All.SendAsync(EventsHub.ChannelRefreshedMessage, dto));
        }

        private void OnUserLoggedIn(object? sender, EventArgs e)
        {
            if (_authState.Update(true))
                _ = SendAuthState(true);
        }

        private void OnImportProgress(ImportProgressEventDto dto)
        {
            if (!_importThrottle.ShouldSend(dto.Id, dto.Done)) return;
            _ = SafeSend(() => _hub.Clients.All.SendAsync(EventsHub.ImportProgressMessage, dto));
        }

        private void OnDeleteChanged(DeleteOperation operation) => SendDeleteProgress(operation);

        private void OnPlayerDataChanged(PlayerDataChangedEventDto dto) =>
            _ = SafeSend(() => _hub.Clients.All.SendAsync(EventsHub.PlayerDataChangedMessage, dto));

        /// <summary>Sends the operation unless the throttle holds it back (the final state always goes).</summary>
        private void SendDeleteProgress(DeleteOperation operation)
        {
            var snapshot = operation.Snapshot();
            var final = DeletePhases.IsFinal(snapshot.Phase);
            if (!_deleteSent.IsNewer(snapshot.Id, snapshot.Version)) return;
            if (!_deleteThrottle.ShouldSend(snapshot.Id, final)) return;
            _deleteSent.MarkSent(snapshot.Id, snapshot.Version, final);
            var dto = ApiDeleteOperationDto.From(snapshot);
            _ = SafeSend(() => _hub.Clients.All.SendAsync(EventsHub.DeleteProgressMessage, dto));
        }

        private void OnMetricsUpdated(object? sender, SystemMetrics metrics)
        {
            if (_state.MetricsSubscribers.Count == 0) return;
            var dto = SystemMetricsDto.From(metrics);
            _ = SafeSend(() => _hub.Clients.Group(EventsHub.MetricsGroup).SendAsync(EventsHub.MetricsMessage, dto));
        }

        /// <summary>One poll of the states that have no events. Never overlaps itself.</summary>
        private void Tick()
        {
            if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
            try
            {
                var loggedIn = _state.IsTelegramLoggedIn();
                if (_authState.Update(loggedIn))
                    _ = SendAuthState(loggedIn);

                foreach (var snapshot in FileService.GetRunningRefreshes())
                {
                    var dto = new ChannelRefreshProgressEventDto
                    {
                        ChannelId = snapshot.ChannelId,
                        Progress = ApiChannelRefreshProgressDto.From(snapshot)
                    };
                    _ = SafeSend(() => _hub.Clients.All.SendAsync(EventsHub.ChannelRefreshProgressMessage, dto));
                }

                // Changes the throttle held back (e.g. the start of a FLOOD_WAIT).
                foreach (var operation in _deletes.Running())
                    SendDeleteProgress(operation);

                var scan = _scan.Current;
                var running = scan?.Running == true;
                if (running || _scanWasRunning)
                {
                    var dto = new LibraryScanEventDto
                    {
                        Running = running,
                        Progress = scan == null ? null : ApiLibraryScanStateDto.From(scan.Clone())
                    };
                    _ = SafeSend(() => _hub.Clients.All.SendAsync(EventsHub.LibraryScanMessage, dto));
                }
                _scanWasRunning = running;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Events hub tick failed");
            }
            finally
            {
                Volatile.Write(ref _ticking, 0);
            }
        }

        private Task SendAuthState(bool loggedIn) =>
            SafeSend(() => _hub.Clients.All.SendAsync(EventsHub.AuthStateMessage, new AuthStateEventDto { TelegramLoggedIn = loggedIn }));

        private async Task SafeSend(Func<Task> send)
        {
            try
            {
                await send();
            }
            catch (Exception ex)
            {
                // A broken client connection must never break the operation that raised the event.
                _logger.LogDebug(ex, "Failed to broadcast an application event");
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
