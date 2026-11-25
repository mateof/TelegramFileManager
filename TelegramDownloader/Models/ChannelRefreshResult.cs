namespace TelegramDownloader.Models
{
    /// <summary>
    /// What the last finished index refresh of a channel added, broken down by
    /// media type. The scan is fire-and-forget, so the client that started it
    /// has no other way to learn what came of it.
    /// </summary>
    public class ChannelRefreshResult
    {
        public string ChannelId { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }

        /// <summary>The scan threw; the counts below are what it managed before failing.</summary>
        public bool Failed { get; set; }

        /// <summary>
        /// The refresh was canceled. Batches written before the cancel stay in
        /// the index; the counts below are exactly those.
        /// </summary>
        public bool Canceled { get; set; }

        /// <summary>Why it failed (the first error), when it did.</summary>
        public string? Error { get; set; }

        public void Fail(string error)
        {
            Failed = true;
            Error ??= error;
        }

        public int Photos { get; set; }
        public int Videos { get; set; }
        public int Audios { get; set; }
        public int Documents { get; set; }

        public int Total => Photos + Videos + Audios + Documents;

        public void Count(DocumentType type)
        {
            switch (type)
            {
                case DocumentType.Photo: Photos++; break;
                case DocumentType.Video: Videos++; break;
                case DocumentType.Audio: Audios++; break;
                default: Documents++; break;
            }
        }
    }
}
