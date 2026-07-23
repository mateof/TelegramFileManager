using Microsoft.AspNetCore.SignalR;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;

namespace TelegramDownloader.Hubs
{
    /// <summary>
    /// Real-time channel for download/upload progress, mapped at <c>/hubs/transfers</c>.
    ///
    /// Server to client messages:
    /// <list type="bullet">
    /// <item><c>TransfersSnapshot</c> (<see cref="TransfersSnapshotDto"/>) - full state, sent on connect and whenever transfers change.</item>
    /// <item><c>TransferSummary</c> (<see cref="TransferSummaryDto"/>) - counters and speeds, sent more frequently than the snapshot.</item>
    /// <item><c>SpeedHistoryPoint</c> (<see cref="SpeedPointDto"/>, <see cref="SpeedPointDto"/>) - one download and one upload sample, every few seconds.</item>
    /// </list>
    ///
    /// Client to server methods are declared below and can be invoked at any time.
    /// </summary>
    public class TransferHub : Hub
    {
        /// <summary>Name of the message carrying a full snapshot.</summary>
        public const string SnapshotMessage = "TransfersSnapshot";

        /// <summary>Name of the message carrying counters and speeds.</summary>
        public const string SummaryMessage = "TransferSummary";

        /// <summary>Name of the message carrying one speed-history sample.</summary>
        public const string SpeedPointMessage = "SpeedHistoryPoint";

        /// <summary>Group receiving snapshot messages.</summary>
        public const string SnapshotGroup = "transfers.snapshot";

        /// <summary>Group receiving summary messages.</summary>
        public const string SummaryGroup = "transfers.summary";

        /// <summary>Group receiving speed-history samples.</summary>
        public const string SpeedGroup = "transfers.speed";

        private readonly TransactionInfoService _tis;

        public TransferHub(TransactionInfoService tis)
        {
            _tis = tis;
        }

        /// <summary>
        /// New clients join every group by default and immediately receive a
        /// snapshot, so a mobile app can render the transfer list without an
        /// extra REST round-trip.
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SnapshotGroup);
            await Groups.AddToGroupAsync(Context.ConnectionId, SummaryGroup);
            await Groups.AddToGroupAsync(Context.ConnectionId, SpeedGroup);
            await Clients.Caller.SendAsync(SnapshotMessage, TransferSnapshotBuilder.BuildSnapshot(_tis));
            await base.OnConnectedAsync();
        }

        /// <summary>Returns the current snapshot on demand.</summary>
        public TransfersSnapshotDto GetSnapshot() => TransferSnapshotBuilder.BuildSnapshot(_tis);

        /// <summary>Returns the current counters and speeds on demand.</summary>
        public TransferSummaryDto GetSummary() => TransferSnapshotBuilder.BuildSummary(_tis);

        /// <summary>Returns the retained speed history on demand.</summary>
        public SpeedHistoryDto GetSpeedHistory() => TransferSnapshotBuilder.BuildSpeedHistory(_tis);

        /// <summary>
        /// Stops receiving full snapshots while still receiving summaries. Useful
        /// for a background app that only needs a progress badge.
        /// </summary>
        public Task MuteSnapshots() => Groups.RemoveFromGroupAsync(Context.ConnectionId, SnapshotGroup);

        /// <summary>Resumes full snapshot delivery and pushes one immediately.</summary>
        public async Task UnmuteSnapshots()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SnapshotGroup);
            await Clients.Caller.SendAsync(SnapshotMessage, TransferSnapshotBuilder.BuildSnapshot(_tis));
        }

        /// <summary>Stops receiving speed-history samples.</summary>
        public Task MuteSpeedHistory() => Groups.RemoveFromGroupAsync(Context.ConnectionId, SpeedGroup);

        /// <summary>Resumes speed-history samples.</summary>
        public Task UnmuteSpeedHistory() => Groups.AddToGroupAsync(Context.ConnectionId, SpeedGroup);
    }
}
