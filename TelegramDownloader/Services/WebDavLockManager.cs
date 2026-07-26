using System.Collections.Concurrent;

namespace TelegramDownloader.Services
{
    /// <summary>
    /// Minimal in-memory WebDAV lock registry (class 2). It grants, refreshes and
    /// releases exclusive write-lock tokens so clients that require the LOCK
    /// handshake before writing (some Hyper Backup / WebDAV configurations) can
    /// proceed.
    ///
    /// Locks are advisory: a conflicting LOCK on an already-locked resource returns
    /// 423, but writes are NOT gated on presenting the token. For a single-writer
    /// backup target that is enough, and it avoids breaking clients on the many
    /// edge cases of strict <c>If:</c>-header enforcement. Registered as a singleton.
    /// </summary>
    public class WebDavLockManager
    {
        private sealed class LockEntry
        {
            public string Token = string.Empty;
            public DateTime ExpiresUtc;
            public string? Owner;
        }

        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromHours(1);
        private static readonly TimeSpan MaxTimeout = TimeSpan.FromHours(24);

        private readonly ConcurrentDictionary<string, LockEntry> _locks = new();

        /// <summary>Keeps a requested timeout within sane bounds (default 1h, max 24h).</summary>
        public TimeSpan ClampTimeout(TimeSpan? requested)
        {
            if (!requested.HasValue || requested.Value <= TimeSpan.Zero) return DefaultTimeout;
            return requested.Value > MaxTimeout ? MaxTimeout : requested.Value;
        }

        /// <summary>
        /// Tries to acquire an exclusive lock on <paramref name="key"/>. Returns the
        /// new token, or null when the resource is already locked by someone else.
        /// </summary>
        public string? TryAcquire(string key, string? owner, TimeSpan timeout)
        {
            var now = DateTime.UtcNow;
            var token = "opaquelocktoken:" + Guid.NewGuid().ToString();
            var entry = new LockEntry { Token = token, ExpiresUtc = now.Add(timeout), Owner = owner };

            while (true)
            {
                if (_locks.TryGetValue(key, out var existing))
                {
                    if (existing.ExpiresUtc > now)
                        return null; // still held by someone else
                    if (_locks.TryUpdate(key, entry, existing)) return token; // replace expired
                    continue; // lost a race, retry
                }
                if (_locks.TryAdd(key, entry)) return token;
            }
        }

        /// <summary>Refreshes an existing lock if the token matches. Returns true on success.</summary>
        public bool Refresh(string key, string token, TimeSpan timeout)
        {
            if (_locks.TryGetValue(key, out var e) && e.Token == token)
            {
                e.ExpiresUtc = DateTime.UtcNow.Add(timeout);
                return true;
            }
            return false;
        }

        /// <summary>Releases a lock if the token matches. Returns true if a lock was removed.</summary>
        public bool Release(string key, string token)
        {
            if (_locks.TryGetValue(key, out var e) && e.Token == token)
                return _locks.TryRemove(key, out _);
            return false;
        }

        /// <summary>Returns the active (unexpired) token for a resource, or null.</summary>
        public string? GetActiveToken(string key)
        {
            if (_locks.TryGetValue(key, out var e) && e.ExpiresUtc > DateTime.UtcNow)
                return e.Token;
            return null;
        }
    }
}
