using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace TelegramDownloader.Services.WebAuth
{
    public enum PasskeyCeremony
    {
        Registration,
        Authentication
    }

    /// <summary>
    /// Pending WebAuthn ceremonies, in memory: each <c>begin</c> stores the
    /// options it sent (with the challenge) under a random opaque id, and the
    /// matching <c>finish</c> takes it out. Entries are single use, expire after
    /// <see cref="Lifetime"/> and are bound to their ceremony type.
    /// </summary>
    public sealed class PasskeyChallengeStore
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

        /// <summary>The login ceremony is unauthenticated: cap the memory it can use.</summary>
        public const int MaxEntries = 500;

        public sealed record Entry(PasskeyCeremony Ceremony, object Options, string RpId, DateTimeOffset ExpiresUtc);

        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly TimeProvider _clock;

        public PasskeyChallengeStore(TimeProvider? clock = null)
        {
            _clock = clock ?? TimeProvider.System;
        }

        public int Count => _entries.Count;

        public string Add(PasskeyCeremony ceremony, object options, string rpId)
        {
            var now = _clock.GetUtcNow();
            Prune(now);
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            _entries[id] = new Entry(ceremony, options, rpId, now + Lifetime);
            return id;
        }

        /// <summary>
        /// Removes the entry (whatever the outcome: a challenge is never used
        /// twice) and returns it when it exists, has not expired and belongs to
        /// <paramref name="ceremony"/>.
        /// </summary>
        public bool TryTake(string? id, PasskeyCeremony ceremony, out Entry? entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(id) || !_entries.TryRemove(id, out var e)) return false;
            if (e.Ceremony != ceremony || e.ExpiresUtc <= _clock.GetUtcNow()) return false;
            entry = e;
            return true;
        }

        private void Prune(DateTimeOffset now)
        {
            foreach (var kv in _entries)
                if (kv.Value.ExpiresUtc <= now) _entries.TryRemove(kv.Key, out _);

            if (_entries.Count < MaxEntries) return;
            // Still full of live entries (a flood of login/begin): drop the oldest.
            foreach (var kv in _entries.OrderBy(kv => kv.Value.ExpiresUtc).Take(_entries.Count - MaxEntries + 1))
                _entries.TryRemove(kv.Key, out _);
        }
    }
}
