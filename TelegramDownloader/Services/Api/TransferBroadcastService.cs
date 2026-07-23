using Microsoft.AspNetCore.SignalR;
using TelegramDownloader.Hubs;
using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Services.Api
{
    /// <summary>
    /// Bridges the in-process <see cref="TransactionInfoService"/> events to the
    /// <see cref="TransferHub"/> so REST clients and mobile apps get live
    /// download/upload progress without polling.
    ///
    /// Progress callbacks fire per network chunk, so snapshots are coalesced to
    /// at most one every <see cref="SnapshotThrottle"/> with a guaranteed
    /// trailing push; the much cheaper summary message is sent on every change.
    /// </summary>
    public class TransferBroadcastService : IHostedService, IDisposable
    {
        /// <summary>Minimum interval between two full snapshot pushes.</summary>
        public static readonly TimeSpan SnapshotThrottle = TimeSpan.FromMilliseconds(500);

        private readonly TransactionInfoService _tis;
        private readonly IHubContext<TransferHub> _hub;
        private readonly ILogger<TransferBroadcastService> _logger;

        private readonly object _gate = new();
        private DateTime _lastSnapshotUtc = DateTime.MinValue;
        private bool _trailingScheduled;
        private Timer? _trailingTimer;
        private bool _disposed;

        public TransferBroadcastService(
            TransactionInfoService tis,
            IHubContext<TransferHub> hub,
            ILogger<TransferBroadcastService> logger)
        {
            _tis = tis;
            _hub = hub;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _tis.TransactionsChanged += OnTransactionsChanged;
            _tis.TaskEventChanged += OnTaskEventChanged;
            _tis.NewSpeedHistoryPoint += OnNewSpeedHistoryPoint;
            _logger.LogInformation("TransferBroadcastService started - streaming transfer updates on {Path}", "/hubs/transfers");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _tis.TransactionsChanged -= OnTransactionsChanged;
            _tis.TaskEventChanged -= OnTaskEventChanged;
            _tis.NewSpeedHistoryPoint -= OnNewSpeedHistoryPoint;
            return Task.CompletedTask;
        }

        private void OnTransactionsChanged(object? sender, EventArgs e) => ScheduleSnapshot();

        private void OnTaskEventChanged(object? sender, EventArgs e) => _ = SendSummaryAsync();

        private void OnNewSpeedHistoryPoint(object? sender, SpeedHistoryEventArgs e)
        {
            _ = SafeSend(async () =>
            {
                await _hub.Clients.Group(TransferHub.SpeedGroup).SendAsync(
                    TransferHub.SpeedPointMessage,
                    SpeedPointDto.From(e.DownloadPoint),
                    SpeedPointDto.From(e.UploadPoint));
            });
        }

        /// <summary>
        /// Pushes a snapshot now when the throttle window is open, otherwise
        /// arms a trailing push so the last change in a burst is never lost.
        /// </summary>
        private void ScheduleSnapshot()
        {
            bool sendNow = false;
            lock (_gate)
            {
                if (_disposed) return;
                var now = DateTime.UtcNow;
                if (now - _lastSnapshotUtc >= SnapshotThrottle)
                {
                    _lastSnapshotUtc = now;
                    sendNow = true;
                }
                else if (!_trailingScheduled)
                {
                    _trailingScheduled = true;
                    var delay = SnapshotThrottle - (now - _lastSnapshotUtc);
                    if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                    if (_trailingTimer == null)
                        _trailingTimer = new Timer(_ => SendTrailingSnapshot(), null, delay, Timeout.InfiniteTimeSpan);
                    else
                        _trailingTimer.Change(delay, Timeout.InfiniteTimeSpan);
                }
            }

            if (sendNow)
                _ = SendSnapshotAsync();
        }

        private void SendTrailingSnapshot()
        {
            lock (_gate)
            {
                _trailingScheduled = false;
                _lastSnapshotUtc = DateTime.UtcNow;
            }
            _ = SendSnapshotAsync();
        }

        private Task SendSnapshotAsync() => SafeSend(async () =>
        {
            var snapshot = TransferSnapshotBuilder.BuildSnapshot(_tis);
            await _hub.Clients.Group(TransferHub.SnapshotGroup).SendAsync(TransferHub.SnapshotMessage, snapshot);
            await _hub.Clients.Group(TransferHub.SummaryGroup).SendAsync(TransferHub.SummaryMessage, snapshot.Summary);
        });

        private Task SendSummaryAsync() => SafeSend(async () =>
        {
            var summary = TransferSnapshotBuilder.BuildSummary(_tis);
            await _hub.Clients.Group(TransferHub.SummaryGroup).SendAsync(TransferHub.SummaryMessage, summary);
        });

        private async Task SafeSend(Func<Task> send)
        {
            try
            {
                await send();
            }
            catch (Exception ex)
            {
                // A broken client connection must never break a transfer.
                _logger.LogDebug(ex, "Failed to broadcast a transfer update");
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _trailingTimer?.Dispose();
                _trailingTimer = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
