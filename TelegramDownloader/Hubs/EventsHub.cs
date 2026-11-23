using Microsoft.AspNetCore.SignalR;
using TelegramDownloader.Data;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;
using TelegramDownloader.Services.Library;

namespace TelegramDownloader.Hubs
{
    /// <summary>
    /// Application events for browser and app clients, mapped at <c>/hubs/events</c>
    /// and protected exactly like <c>/hubs/transfers</c>.
    ///
    /// Server to client messages (see <c>docs/api/signalr.md</c>):
    /// <list type="bullet">
    /// <item><c>Notification</c> (<see cref="NotificationEventDto"/>) - the toasts of the web UI.</item>
    /// <item><c>ChannelRefreshed</c> (<see cref="ChannelRefreshedEventDto"/>) - a channel refresh ended.</item>
    /// <item><c>ChannelRefreshProgress</c> (<see cref="ChannelRefreshProgressEventDto"/>) - live refresh counters, at most once per second per channel.</item>
    /// <item><c>ImportProgress</c> (<see cref="ImportProgressEventDto"/>) - channel index / shared collection import progress.</item>
    /// <item><c>AuthState</c> (<see cref="AuthStateEventDto"/>) - Telegram session state, on connect and on change.</item>
    /// <item><c>LibraryScan</c> (<see cref="LibraryScanEventDto"/>) - media library scan state.</item>
    /// <item><c>Metrics</c> (<see cref="SystemMetricsDto"/>) - every 2 s, only after <see cref="SubscribeMetrics"/>.</item>
    /// </list>
    /// </summary>
    public class EventsHub : Hub
    {
        public const string NotificationMessage = "Notification";
        public const string ChannelRefreshedMessage = "ChannelRefreshed";
        public const string ChannelRefreshProgressMessage = "ChannelRefreshProgress";
        public const string ImportProgressMessage = "ImportProgress";
        public const string AuthStateMessage = "AuthState";
        public const string MetricsMessage = "Metrics";
        public const string LibraryScanMessage = "LibraryScan";

        /// <summary>Group of the connections that asked for <c>Metrics</c>.</summary>
        public const string MetricsGroup = "events.metrics";

        private readonly EventsHubState _state;
        private readonly ISystemMetricsService _metrics;
        private readonly LibraryScanService _scan;

        public EventsHub(EventsHubState state, ISystemMetricsService metrics, LibraryScanService scan)
        {
            _state = state;
            _metrics = metrics;
            _scan = scan;
        }

        /// <summary>
        /// Sends the current Telegram session state, the refreshes in flight
        /// and a running library scan, so a client renders correctly without
        /// waiting for the next change.
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync(AuthStateMessage, GetAuthState());

            foreach (var snapshot in FileService.GetRunningRefreshes())
            {
                await Clients.Caller.SendAsync(ChannelRefreshProgressMessage, new ChannelRefreshProgressEventDto
                {
                    ChannelId = snapshot.ChannelId,
                    Progress = ApiChannelRefreshProgressDto.From(snapshot)
                });
            }

            if (_scan.Current is { Running: true } scan)
            {
                await Clients.Caller.SendAsync(LibraryScanMessage, new LibraryScanEventDto
                {
                    Running = true,
                    Progress = ApiLibraryScanStateDto.From(scan.Clone())
                });
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (_state.MetricsSubscribers.Remove(Context.ConnectionId))
                _metrics.StopMonitoring();
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>Returns the current Telegram session state on demand.</summary>
        public AuthStateEventDto GetAuthState() => new AuthStateEventDto { TelegramLoggedIn = _state.IsTelegramLoggedIn() };

        /// <summary>
        /// Starts receiving <c>Metrics</c> every 2 seconds. The server samples
        /// CPU, memory and disk only while at least one connection subscribed.
        /// </summary>
        public async Task SubscribeMetrics()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, MetricsGroup);
            if (_state.MetricsSubscribers.Add(Context.ConnectionId))
                _metrics.StartMonitoring(EventsHubState.MetricsIntervalMs);
            await Clients.Caller.SendAsync(MetricsMessage, SystemMetricsDto.From(await _metrics.GetMetricsAsync()));
        }

        /// <summary>Stops receiving <c>Metrics</c>.</summary>
        public async Task UnsubscribeMetrics()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, MetricsGroup);
            if (_state.MetricsSubscribers.Remove(Context.ConnectionId))
                _metrics.StopMonitoring();
        }
    }

    /// <summary>Process-wide state shared by <see cref="EventsHub"/> instances and the bridge.</summary>
    public sealed class EventsHubState
    {
        public const int MetricsIntervalMs = 2000;

        private readonly ITelegramService _telegram;

        public EventsHubState(ITelegramService telegram)
        {
            _telegram = telegram;
        }

        public SubscriberRegistry MetricsSubscribers { get; } = new SubscriberRegistry();

        public bool IsTelegramLoggedIn()
        {
            try
            {
                return _telegram.IsConfigured && _telegram.checkUserLogin();
            }
            catch
            {
                return false;
            }
        }
    }
}
