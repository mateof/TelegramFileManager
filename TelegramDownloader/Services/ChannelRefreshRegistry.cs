using System.Collections.Concurrent;

namespace TelegramDownloader.Services
{
    /// <summary>
    /// The channels with a refresh in flight. Claiming a channel is atomic, so
    /// two simultaneous requests cannot both start a scan of the same channel.
    /// </summary>
    public class ChannelRefreshRegistry
    {
        private readonly ConcurrentDictionary<string, byte> _running = new ConcurrentDictionary<string, byte>();

        /// <summary>Marks the channel as refreshing; false if it already was.</summary>
        public bool TryBegin(string channelId) => _running.TryAdd(channelId, 0);

        public void End(string channelId) => _running.TryRemove(channelId, out _);

        public bool IsRunning(string channelId) => _running.ContainsKey(channelId);
    }
}
