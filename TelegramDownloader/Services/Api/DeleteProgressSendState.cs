using System.Collections.Concurrent;

namespace TelegramDownloader.Services.Api
{
    /// <summary>
    /// The last <c>DeleteProgress</c> version sent per operation, so the
    /// ticker only re-sends an operation whose change the throttle held back.
    /// </summary>
    public sealed class DeleteProgressSendState
    {
        private readonly ConcurrentDictionary<string, long> _sent = new(StringComparer.Ordinal);

        /// <summary>True when <paramref name="version"/> was not sent yet.</summary>
        public bool IsNewer(string id, long version) => !_sent.TryGetValue(id, out var last) || version > last;

        /// <summary>Records a send; after the final one nothing else is sent for the operation.</summary>
        public void MarkSent(string id, long version, bool final)
        {
            var value = final ? long.MaxValue : version;
            _sent.AddOrUpdate(id, value, (_, last) => Math.Max(last, value));
        }
    }
}
