namespace TelegramDownloader.Models
{
    /// <summary>
    /// Pure helpers for appending many tracks to a playlist at once: dedupe against
    /// the tracks already stored and inside the incoming batch, and assign
    /// consecutive <see cref="PlaylistTrackModel.Order"/> values.
    /// </summary>
    public static class PlaylistTrackMerger
    {
        /// <summary>
        /// Returns the incoming tracks that are not already in <paramref name="existing"/>
        /// (by <see cref="PlaylistTrackModel.FileId"/>) and not repeated earlier in the
        /// batch itself, keeping the request order. <paramref name="skipped"/> counts the rest.
        /// </summary>
        public static List<PlaylistTrackModel> SelectNew(
            IEnumerable<PlaylistTrackModel>? existing,
            IEnumerable<PlaylistTrackModel> incoming,
            out int skipped)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (existing != null)
            {
                foreach (var t in existing)
                {
                    if (t?.FileId != null) seen.Add(t.FileId);
                }
            }

            var result = new List<PlaylistTrackModel>();
            skipped = 0;
            foreach (var track in incoming)
            {
                if (seen.Add(track.FileId))
                    result.Add(track);
                else
                    skipped++;
            }
            return result;
        }

        /// <summary>
        /// First free order value after the current tracks: the track count, or one past
        /// the highest stored order if the stored orders have gaps or are out of sync.
        /// </summary>
        public static int NextOrder(IReadOnlyCollection<PlaylistTrackModel>? existing)
        {
            if (existing == null || existing.Count == 0) return 0;
            return Math.Max(existing.Count, existing.Max(t => t.Order) + 1);
        }

        /// <summary>
        /// Sets consecutive orders starting at <paramref name="startOrder"/> and stamps
        /// every track with <paramref name="dateAdded"/>.
        /// </summary>
        public static void AssignOrder(IList<PlaylistTrackModel> tracks, int startOrder, DateTime dateAdded)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                tracks[i].Order = startOrder + i;
                tracks[i].DateAdded = dateAdded;
            }
        }
    }
}
