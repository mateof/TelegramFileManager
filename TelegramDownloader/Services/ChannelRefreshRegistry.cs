using System.Collections.Concurrent;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services
{
    /// <summary>
    /// The channels with a refresh in flight, each with its live progress and
    /// cancellation token. Claiming a channel is atomic, so two simultaneous
    /// requests cannot both start a scan of the same channel.
    /// </summary>
    public class ChannelRefreshRegistry
    {
        private readonly ConcurrentDictionary<string, ChannelRefreshProgress> _running = new ConcurrentDictionary<string, ChannelRefreshProgress>();

        /// <summary>Marks the channel as refreshing; false if it already was.</summary>
        public bool TryBegin(string channelId) => TryBegin(channelId, out _);

        /// <summary>Marks the channel as refreshing and hands back its progress; false if it already was.</summary>
        public bool TryBegin(string channelId, out ChannelRefreshProgress progress)
        {
            ChannelRefreshProgress candidate = new ChannelRefreshProgress(channelId);
            if (_running.TryAdd(channelId, candidate))
            {
                progress = candidate;
                return true;
            }
            progress = null!;
            return false;
        }

        /// <summary>Releases the channel, whoever claimed it.</summary>
        public void End(string channelId) => _running.TryRemove(channelId, out _);

        /// <summary>Releases the channel only if <paramref name="progress"/> is still its entry.</summary>
        public void End(string channelId, ChannelRefreshProgress progress) =>
            _running.TryRemove(new KeyValuePair<string, ChannelRefreshProgress>(channelId, progress));

        public bool IsRunning(string channelId) => _running.ContainsKey(channelId);

        /// <summary>The live progress of the channel's refresh, or null when none runs.</summary>
        public ChannelRefreshProgress? Get(string channelId) =>
            _running.TryGetValue(channelId, out ChannelRefreshProgress? progress) ? progress : null;

        /// <summary>A snapshot of every refresh in flight, ordered by channel id.</summary>
        public IReadOnlyList<ChannelRefreshProgressSnapshot> Snapshots() =>
            _running.Values
                .Select(p => p.Snapshot())
                .OrderBy(s => s.ChannelId, StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// Asks the running refresh to stop; false when nothing runs. The entry
        /// is released by the refresh itself once it has unwound (after the
        /// Telegram page or insert batch in flight), so a new refresh cannot
        /// overlap the one that is stopping.
        /// </summary>
        public bool TryCancel(string channelId)
        {
            ChannelRefreshProgress? progress = Get(channelId);
            if (progress == null) return false;
            progress.Cancel();
            return true;
        }
    }
}
