namespace TelegramDownloader.Models
{
    /// <summary>
    /// Options for refreshing channel data, specifying which media types to fetch
    /// </summary>
    public class RefreshChannelOptions
    {
        /// <summary>
        /// Include documents (files with filename like PDFs, ZIPs, etc.)
        /// </summary>
        public bool IncludeDocuments { get; set; } = true;

        /// <summary>
        /// Include audio files (music, voice messages)
        /// </summary>
        public bool IncludeAudio { get; set; } = true;

        /// <summary>
        /// Include video files
        /// </summary>
        public bool IncludeVideo { get; set; } = true;

        /// <summary>
        /// Include photos
        /// </summary>
        public bool IncludePhotos { get; set; } = true;

        /// <summary>
        /// Returns true if at least one media type is selected
        /// </summary>
        public bool HasAnySelection => IncludeDocuments || IncludeAudio || IncludeVideo || IncludePhotos;

        /// <summary>
        /// Returns a summary string of selected types
        /// </summary>
        public string GetSelectionSummary()
        {
            var types = new List<string>();
            if (IncludeDocuments) types.Add("Documents");
            if (IncludeAudio) types.Add("Audio");
            if (IncludeVideo) types.Add("Video");
            if (IncludePhotos) types.Add("Photos");
            return types.Count > 0 ? string.Join(", ", types) : "None";
        }
    }
}
