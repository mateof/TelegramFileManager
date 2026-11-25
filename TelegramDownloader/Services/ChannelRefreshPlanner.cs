using TelegramDownloader.Models;

namespace TelegramDownloader.Services
{
    /// <summary>
    /// Pure, allocation-friendly steps of a channel refresh, kept apart from
    /// Telegram and Mongo so they can be unit tested. Each one reproduces the
    /// exact result of the original (quadratic) inline code.
    /// </summary>
    public static class ChannelRefreshPlanner
    {
        /// <summary>Documents per InsertMany round trip.</summary>
        public const int InsertBatchSize = 1000;

        /// <summary>
        /// Merges the per-filter search results in phase order. A message found
        /// by an earlier phase wins, exactly as when the phases ran one after
        /// another sharing a single "already added" set.
        /// </summary>
        public static List<TelegramChatDocuments> MergePhaseResults(IEnumerable<IReadOnlyList<TelegramChatDocuments>> phases)
        {
            List<TelegramChatDocuments> merged = new List<TelegramChatDocuments>();
            HashSet<int> seen = new HashSet<int>();
            foreach (IReadOnlyList<TelegramChatDocuments> phase in phases)
            {
                foreach (TelegramChatDocuments doc in phase)
                {
                    if (seen.Add(doc.id))
                        merged.Add(doc);
                }
            }
            return merged;
        }

        /// <summary>
        /// Renames <paramref name="docs"/> in place so no name collides
        /// (case-insensitively) with <paramref name="existingNames"/> or with an
        /// earlier doc: "a.pdf", "a.pdf(1)", "a.pdf(2)"... Every doc consumes a
        /// name, including the ones that end up not being inserted, as before.
        /// </summary>
        /// <remarks>
        /// Names are only ever added, so a suffix found taken stays taken; the
        /// next free suffix per base name is remembered instead of probing from
        /// 1 each time (which was O(k^2) for k files sharing a name).
        /// </remarks>
        public static void AssignUniqueNames(IEnumerable<string> existingNames, IEnumerable<TelegramChatDocuments> docs)
        {
            HashSet<string> taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> nextSuffix = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (TelegramChatDocuments doc in docs)
            {
                string baseName = doc.name;
                string name = baseName;
                if (taken.Contains(name))
                {
                    int count = nextSuffix.TryGetValue(baseName, out int c) ? c : 1;
                    do
                    {
                        name = $"{baseName}({count})";
                        count++;
                    } while (taken.Contains(name));
                    nextSuffix[baseName] = count;
                }

                doc.name = name;
                taken.Add(name);
            }
        }

        /// <summary>The docs whose message is not indexed yet, in scan order.</summary>
        public static List<TelegramChatDocuments> SelectNew(IEnumerable<TelegramChatDocuments> docs, IReadOnlySet<int> presentIds)
        {
            return docs.Where(d => !presentIds.Contains(d.id)).ToList();
        }

        /// <summary>
        /// The message id a filter's search stops at (exclusive): its stored
        /// high-water mark, or 0 (scan everything) when the filter has no mark
        /// yet or the refresh is forced.
        /// </summary>
        public static int ScanFloor(RefreshFilter filter, IReadOnlyDictionary<RefreshFilter, int> marks, bool force)
        {
            if (force) return 0;
            return marks.TryGetValue(filter, out int mark) && mark > 0 ? mark : 0;
        }

        /// <summary>The selected filters that will be scanned from the very first message.</summary>
        public static List<RefreshFilter> FiltersNeedingFullScan(IEnumerable<RefreshFilter> selected, IReadOnlyDictionary<RefreshFilter, int> marks, bool force)
        {
            return selected.Where(f => ScanFloor(f, marks, force) == 0).ToList();
        }

        /// <summary>
        /// The new high-water mark of a filter whose search ran to completion
        /// down to <paramref name="floor"/>.
        /// </summary>
        /// <remarks>
        /// A mark M promises "every message of this filter with id &lt;= M is in
        /// the index". Walking the scanned ids upwards from the floor, the mark
        /// follows them while each one is indexed and stops at the first that
        /// is not (failed insert, canceled before its batch), so the next
        /// refresh reaches that message again. It never moves past a gap. Only
        /// call this for a search that finished: a search cut short has not
        /// seen the oldest messages above the floor, so its mark must stay.
        /// </remarks>
        /// <param name="floor">Where the search stopped: the previous mark, or 0 for a full scan.</param>
        /// <param name="scannedIds">The ids the search returned (any order).</param>
        /// <param name="isIndexed">True when the id is in the index now (already present or inserted by this run).</param>
        public static int AdvanceMark(int floor, IEnumerable<int> scannedIds, Func<int, bool> isIndexed)
        {
            int mark = floor;
            foreach (int id in scannedIds.Where(id => id > floor).Distinct().OrderBy(id => id))
            {
                if (!isIndexed(id)) break;
                mark = id;
            }
            return mark;
        }

        /// <summary>The directory entry a refresh writes for a message at the channel root.</summary>
        public static BsonFileManagerModel ToRootFileEntry(TelegramChatDocuments tcd, string rootFolderId)
        {
            BsonFileManagerModel model = new BsonFileManagerModel();
            model.Size = tcd.fileSize;
            model.MessageId = tcd.id;
            model.Name = tcd.name;
            model.IsFile = true;
            model.HasChild = false;
            model.DateCreated = tcd.creationDate;
            model.DateModified = tcd.modifiedDate;
            model.FilterPath = "/";
            model.FilterId = rootFolderId + "/";
            model.ParentId = rootFolderId;
            model.FilePath = "/" + tcd.name;
            model.Type = tcd.extension;
            model.isSplit = false;
            model.isEncrypted = false;
            return model;
        }
    }
}
