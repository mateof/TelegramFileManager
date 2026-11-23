using MongoDB.Bson;
using MongoDB.Driver;
using TelegramDownloader.Models;

namespace TelegramDownloader.Data.db
{
    /// <summary>
    /// Pure helpers to find the ancestor folders of an index entry without
    /// walking the <c>ParentId</c> chain one query at a time, plus indexed
    /// equivalents of the computed path predicates the index used to evaluate
    /// on every document.
    /// </summary>
    /// <remarks>
    /// <c>FilterId</c> holds the ids of all the ancestors of an entry, root
    /// first, each followed by <c>/</c> (<c>"root/a/b/"</c>; <c>""</c> for the
    /// root itself). It is written as <c>parent.FilterId + parent.Id + "/"</c>
    /// everywhere, so it is complete and ordered, but a past bug in move
    /// operations could leave it stale (see <c>RepairFilterPaths</c>). The
    /// callers therefore use it only as a list of candidates and confirm the
    /// real chain against the <c>ParentId</c> values read in one query.
    /// </remarks>
    public static class FolderAncestry
    {
        /// <summary>Length of an ObjectId rendered as hex.</summary>
        public const int ObjectIdHexLength = 24;

        /// <summary>
        /// Ancestor ids stored in a <c>FilterId</c>, root first. Empty segments
        /// and values that are not ObjectIds (corrupted data, backslashes) are
        /// dropped: they can never match an <c>_id</c>.
        /// </summary>
        public static List<string> ParseFilterId(string? filterId)
        {
            var ids = new List<string>();
            if (string.IsNullOrEmpty(filterId))
                return ids;
            foreach (var part in filterId.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (ObjectId.TryParse(part, out _))
                    ids.Add(part);
            }
            return ids;
        }

        /// <summary>
        /// Candidate ancestor ids of <paramref name="folderId"/>: the folder
        /// itself plus the ids in <paramref name="filterIdHint"/> (the folder's
        /// own <c>FilterId</c>, or the <c>FilterId</c> of an entry inside the
        /// folder, which also ends with the folder id). Deduplicated.
        /// </summary>
        public static List<string> CandidateIds(string folderId, string? filterIdHint)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(folderId) && seen.Add(folderId))
                result.Add(folderId);
            foreach (var id in ParseFilterId(filterIdHint))
                if (seen.Add(id))
                    result.Add(id);
            return result;
        }

        /// <summary>Outcome of <see cref="ResolveChain"/>.</summary>
        /// <param name="Chain">Folders found, starting with the folder itself and ending at the root.</param>
        /// <param name="MissingId">
        /// First id of the chain that was not in the lookup, or null when the
        /// chain reached a folder with an empty <c>ParentId</c>. The caller
        /// continues the walk from there with the old one-query-per-level loop.
        /// </param>
        public readonly record struct ChainResult(List<string> Chain, string? MissingId);

        /// <summary>
        /// Walks the <c>ParentId</c> chain from <paramref name="folderId"/> using
        /// only the documents already read. Mirrors the old loop: every id is
        /// added, the walk stops at an empty <c>ParentId</c>. A cycle (only
        /// possible with corrupted data, the old loop would never end) stops it
        /// as well.
        /// </summary>
        public static ChainResult ResolveChain(string? folderId, IReadOnlyDictionary<string, string?> parentById)
        {
            var chain = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var id = folderId;
            while (!string.IsNullOrEmpty(id))
            {
                if (!seen.Add(id))
                    break;
                if (!parentById.TryGetValue(id, out var parentId))
                    return new ChainResult(chain, id);
                chain.Add(id);
                id = parentId;
            }
            return new ChainResult(chain, null);
        }

        /// <summary>
        /// Filter of the single <c>UpdateMany</c> that adds (or subtracts) bytes
        /// to every ancestor: <c>{ _id: { $in: [...] }, IsFile: false }</c>,
        /// the same predicate the old code built with LINQ.
        /// </summary>
        public static FilterDefinition<BsonFileManagerModel> AncestorUpdateFilter(IEnumerable<string> folderIds)
        {
            var b = Builders<BsonFileManagerModel>.Filter;
            return b.In(x => x.Id, folderIds) & b.Eq(x => x.IsFile, false);
        }

        /// <summary>
        /// Indexed form of <c>FilterId + Id.ToString() + "/" == path</c>. The
        /// server renders an ObjectId as 24 lowercase hex characters, so only
        /// the last 24 characters before the trailing slash can be the id and
        /// the rest has to be the <c>FilterId</c>. Returns null when no
        /// document can satisfy the predicate.
        /// </summary>
        public static (string Id, string FilterId)? SplitFolderFilterIdPath(string? path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith('/'))
                return null;
            var s = path.Substring(0, path.Length - 1);
            if (s.Length < ObjectIdHexLength)
                return null;
            var id = s.Substring(s.Length - ObjectIdHexLength);
            if (!ObjectId.TryParse(id, out var oid) || oid.ToString() != id)
                return null;
            return (id, s.Substring(0, s.Length - ObjectIdHexLength));
        }

        /// <summary>
        /// Every way to split <paramref name="path"/> into
        /// <c>FilterPath + Name + "/"</c>: the indexed form of that computed
        /// predicate is an <c>$or</c> of these (FilterPath, Name) pairs, which
        /// the <c>{FilterPath, Name}</c> index answers with point lookups.
        /// Empty when the path does not end with a slash (nothing can match).
        /// </summary>
        public static List<(string FilterPath, string Name)> FilterPathNameSplits(string? path)
        {
            var result = new List<(string, string)>();
            if (string.IsNullOrEmpty(path) || !path.EndsWith('/'))
                return result;
            var s = path.Substring(0, path.Length - 1);
            for (int k = 0; k <= s.Length; k++)
                result.Add((s.Substring(0, k), s.Substring(k)));
            return result;
        }
    }
}
