using System.Collections.Concurrent;

namespace TelegramDownloader.Services.WebAuth
{
    /// <summary>
    /// Brute-force protection of the web password, per client IP.
    ///
    /// Up to <see cref="MaxFailures"/> wrong passwords are tolerated within
    /// <see cref="Window"/>. The next one locks the IP out for
    /// <see cref="BaseLockout"/>, doubling with every further lockout up to
    /// <see cref="MaxLockout"/>. A successful login clears the record; a record
    /// with no failure for <see cref="ForgetAfter"/> is forgotten.
    /// </summary>
    public class LoginRateLimiter
    {
        public const int MaxFailures = 5;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan BaseLockout = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan ForgetAfter = TimeSpan.FromHours(1);

        private readonly TimeProvider _clock;
        private readonly ConcurrentDictionary<string, Entry> _entries = new();

        private sealed class Entry
        {
            public readonly List<DateTimeOffset> Failures = new();
            public DateTimeOffset LockedUntil;
            public int Lockouts;
            public DateTimeOffset LastFailure;
        }

        public LoginRateLimiter(TimeProvider? clock = null)
        {
            _clock = clock ?? TimeProvider.System;
        }

        /// <summary>
        /// Whether <paramref name="key"/> may try a password now. When it may
        /// not, <paramref name="retryAfter"/> tells how long to wait.
        /// </summary>
        public bool IsAllowed(string key, out TimeSpan retryAfter)
        {
            retryAfter = TimeSpan.Zero;
            if (!_entries.TryGetValue(key, out var e)) return true;
            var now = _clock.GetUtcNow();
            lock (e)
            {
                if (e.LockedUntil > now)
                {
                    retryAfter = e.LockedUntil - now;
                    return false;
                }
            }
            return true;
        }

        /// <summary>Records a wrong password. Returns true when it triggered a lockout.</summary>
        public bool RegisterFailure(string key)
        {
            var now = _clock.GetUtcNow();
            Cleanup(now);
            var e = _entries.GetOrAdd(key, _ => new Entry());
            lock (e)
            {
                // Lockouts escalate only while the attacker keeps going; a quiet
                // period resets the back-off.
                if (e.LastFailure != default && now - e.LastFailure > ForgetAfter)
                    e.Lockouts = 0;

                e.LastFailure = now;
                e.Failures.RemoveAll(t => now - t > Window);
                e.Failures.Add(now);
                if (e.Failures.Count > MaxFailures)
                {
                    var factor = Math.Pow(2, Math.Min(e.Lockouts, 10));
                    var lockout = TimeSpan.FromTicks((long)Math.Min(BaseLockout.Ticks * factor, MaxLockout.Ticks));
                    e.LockedUntil = now + lockout;
                    e.Lockouts++;
                    e.Failures.Clear();
                    return true;
                }
            }
            return false;
        }

        /// <summary>Clears the record of <paramref name="key"/> after a correct password.</summary>
        public void RegisterSuccess(string key) => _entries.TryRemove(key, out _);

        private void Cleanup(DateTimeOffset now)
        {
            if (_entries.Count < 1000) return;
            foreach (var kv in _entries)
            {
                lock (kv.Value)
                {
                    if (kv.Value.LockedUntil < now && now - kv.Value.LastFailure > ForgetAfter)
                        _entries.TryRemove(kv.Key, out _);
                }
            }
        }
    }
}
