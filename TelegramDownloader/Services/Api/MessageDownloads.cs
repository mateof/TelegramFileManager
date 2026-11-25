namespace TelegramDownloader.Services.Api
{
    /// <summary>File names for the attachments of a set of messages saved in one folder.</summary>
    public static class MessageDownloads
    {
        /// <summary>Telegram answers at most this many messages per request.</summary>
        public const int BatchSize = 100;

        /// <summary>
        /// <paramref name="name"/>, or "name (2).ext", "name (3).ext"... when it
        /// is already used: many messages share a name ("video.mp4") and one
        /// would overwrite the other. The result is added to <paramref name="taken"/>.
        /// </summary>
        public static string UniqueName(string name, ISet<string> taken, Func<string, bool> exists)
        {
            string stem = System.IO.Path.GetFileNameWithoutExtension(name);
            string extension = System.IO.Path.GetExtension(name);
            string candidate = name;
            for (int i = 2; taken.Contains(candidate) || exists(candidate); i++)
                candidate = $"{stem} ({i}){extension}";
            taken.Add(candidate);
            return candidate;
        }
    }
}
