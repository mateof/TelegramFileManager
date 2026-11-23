using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;

namespace TelegramDownloader.Services.Api
{
    /// <summary>
    /// Pure translations from the in-process events of the app to the
    /// messages of the <c>/hubs/events</c> hub.
    /// </summary>
    public static class EventMapping
    {
        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);

        /// <summary>Maps a web UI notification level to the hub vocabulary.</summary>
        public static string LevelOf(NotificationTypes type) => type switch
        {
            NotificationTypes.Error => EventLevels.Error,
            NotificationTypes.Warn => EventLevels.Warning,
            NotificationTypes.Success => EventLevels.Success,
            _ => EventLevels.Info
        };

        /// <summary>
        /// Same rule as the web UI toasts: notifications are shown when the user
        /// enabled them (<c>shouldNotify</c>) or when the sender forced them.
        /// </summary>
        public static bool ShouldForward(Notification notification, bool shouldNotify) =>
            notification != null && (shouldNotify || notification.isForced);

        /// <summary>The web UI renders the text as markup; the hub sends plain text.</summary>
        public static string ToPlainText(string? html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var withBreaks = Regex.Replace(html, "<br\\s*/?>", " ", RegexOptions.IgnoreCase);
            return WebUtility.HtmlDecode(Tags.Replace(withBreaks, string.Empty)).Trim();
        }

        public static NotificationEventDto ToDto(Notification n, DateTime utcNow) => new NotificationEventDto
        {
            Level = LevelOf(n.type),
            Title = string.IsNullOrWhiteSpace(n.header) ? null : ToPlainText(n.header),
            Message = ToPlainText(n.text),
            At = utcNow
        };

        /// <summary>Maps one progress tick of a <see cref="GenericNotificationProgressModel"/>.</summary>
        public static ImportProgressEventDto ToImportProgress(string id, string kind, NotificationProgressEvent e)
        {
            var total = Math.Max(0, e.TotalItems);
            var completed = Math.Max(0, e.CompletedItems);
            var percent = total == 0 ? (e.Finished ? 100 : 0) : (int)Math.Clamp(completed * 100L / total, 0, 100);
            return new ImportProgressEventDto
            {
                Id = id,
                Kind = kind,
                Total = total,
                Completed = completed,
                Percent = percent,
                Done = e.Finished,
                Message = $"{completed}/{total} items"
            };
        }
    }

    /// <summary>
    /// Per-key rate limiter: lets one message per key through every
    /// <c>interval</c>, and always lets a final message through (then forgets
    /// the key). Thread-safe.
    /// </summary>
    public sealed class KeyedThrottle
    {
        private readonly TimeSpan _interval;
        private readonly Func<DateTime> _clock;
        private readonly ConcurrentDictionary<string, DateTime> _last = new(StringComparer.Ordinal);

        public KeyedThrottle(TimeSpan interval, Func<DateTime>? clock = null)
        {
            _interval = interval;
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>True when a message for <paramref name="key"/> may be sent now.</summary>
        /// <param name="key">Stream the message belongs to.</param>
        /// <param name="final">Last message of the stream: always sent, and the key is released.</param>
        public bool ShouldSend(string key, bool final = false)
        {
            if (final)
            {
                _last.TryRemove(key, out _);
                return true;
            }

            var now = _clock();
            var allowed = false;
            _last.AddOrUpdate(key,
                _ => { allowed = true; return now; },
                (_, previous) =>
                {
                    if (now - previous >= _interval)
                    {
                        allowed = true;
                        return now;
                    }
                    allowed = false;
                    return previous;
                });
            return allowed;
        }

        /// <summary>Keys currently tracked (for tests and diagnostics).</summary>
        public int Count => _last.Count;
    }

    /// <summary>
    /// The connections subscribed to a hub stream. Reports the empty/non-empty
    /// transitions so the producer can be started and stopped on demand.
    /// </summary>
    public sealed class SubscriberRegistry
    {
        private readonly object _gate = new();
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

        /// <summary>Adds a subscriber; true when it is the first one.</summary>
        public bool Add(string connectionId)
        {
            lock (_gate)
            {
                return _ids.Add(connectionId) && _ids.Count == 1;
            }
        }

        /// <summary>Removes a subscriber; true when it was the last one.</summary>
        public bool Remove(string connectionId)
        {
            lock (_gate)
            {
                return _ids.Remove(connectionId) && _ids.Count == 0;
            }
        }

        public int Count
        {
            get { lock (_gate) return _ids.Count; }
        }
    }

    /// <summary>
    /// Remembers the last value of a polled state and reports when it changed.
    /// </summary>
    public sealed class ChangeDetector<T>
    {
        private readonly object _gate = new();
        private bool _hasValue;
        private T? _value;

        /// <summary>Stores <paramref name="value"/>; true when it differs from the previous one (or is the first).</summary>
        public bool Update(T value)
        {
            lock (_gate)
            {
                if (_hasValue && EqualityComparer<T>.Default.Equals(_value, value))
                    return false;
                _hasValue = true;
                _value = value;
                return true;
            }
        }
    }
}
