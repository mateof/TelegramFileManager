namespace TelegramDownloader.Models
{
    /// <summary>Kinds of messages a chat can be browsed by, as the tabs of the Telegram apps.</summary>
    public enum ChatMessageKind
    {
        /// <summary>Photos and videos together.</summary>
        Media,
        Photos,
        Videos,
        Documents,
        /// <summary>Music files.</summary>
        Audio,
        /// <summary>Voice messages and round videos.</summary>
        Voice,
        Gifs,
        /// <summary>Messages with a link.</summary>
        Links
    }

    public static class ChatMessageKinds
    {
        public static string ToKey(this ChatMessageKind kind) => kind.ToString().ToLowerInvariant();

        /// <summary>Only the names (<c>photos</c>), never the numbers an enum parse would take.</summary>
        public static bool TryParse(string? key, out ChatMessageKind kind)
        {
            foreach (ChatMessageKind k in Enum.GetValues<ChatMessageKind>())
            {
                if (!string.Equals(k.ToKey(), key?.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                kind = k;
                return true;
            }
            kind = default;
            return false;
        }
    }

    /// <summary>One page of messages as Telegram answers it, newest first.</summary>
    public sealed class ChatMessagesPage
    {
        public List<ChatMessages> Messages { get; init; } = new List<ChatMessages>();

        /// <summary>Ids of everything in the page, also the service messages that are not listed.</summary>
        public List<int> Ids { get; init; } = new List<int>();

        /// <summary>Messages that match in the whole chat, as Telegram reports it.</summary>
        public int Total { get; init; }
    }

    /// <summary>Which part of a chat to read: the end, or before, after or around a message.</summary>
    public sealed record ChatWindow(int? Before, int? After, int? Around, int Limit)
    {
        /// <summary>Telegram's <c>offset_id</c> and <c>add_offset</c> for this window.</summary>
        /// <remarks>
        /// Messages go newest first and the list starts at the first one older
        /// than <c>offset_id</c>; a negative <c>add_offset</c> moves that start
        /// towards the newer ones.
        /// </remarks>
        public (int OffsetId, int AddOffset) ToTelegram()
        {
            if (Around is int around) return (around + 1, -(Limit / 2));
            if (Before is int before) return (before, 0);
            if (After is int after) return (after, -Limit);
            return (0, 0);
        }

        /// <summary>Near the end of the chat Telegram fills an "after" page with messages that were not asked for.</summary>
        public bool Contains(int id) => After is not int after || Around != null || Before != null || id > after;

        /// <summary>Whether older / newer messages may exist. An empty page ends that direction.</summary>
        public (bool Older, bool Newer) More(IReadOnlyCollection<int> ids)
        {
            if (Around is int around) return (ids.Any(i => i < around), ids.Any(i => i > around));
            if (Before != null) return (ids.Count > 0, true);
            if (After != null) return (true, ids.Any(Contains));
            return (ids.Count > 0, false);
        }
    }
}
