using System.Text.RegularExpressions;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services.Upload
{
    /// <summary>Number and total size of the files a folder upload will send.</summary>
    public readonly record struct UploadTotals(int Count, long Size);

    /// <summary>
    /// Pure helpers behind "upload a server folder to a channel". They hold the
    /// rules the upload pipeline applies, so the bulk paths (one directory walk,
    /// one index query per destination folder) give the same answer as the old
    /// per-file checks.
    /// </summary>
    public static class FolderUploadPlanning
    {
        /// <summary>
        /// Walks <paramref name="directory"/> once and counts the files the
        /// upload task reports in its totals: not hidden, not under a Synology
        /// <c>@eaDir</c> folder and not empty. Same filters as before, but the
        /// tree is streamed and read a single time instead of being listed and
        /// then enumerated twice (once for the count, once for the size).
        /// </summary>
        public static UploadTotals CountUploadableFiles(string directory)
        {
            int count = 0;
            long size = 0;
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.*", SearchOption.AllDirectories))
            {
                if (!IsCountedFile(file.FullName, file.Attributes, file.Length))
                    continue;
                count++;
                size += file.Length;
            }
            return new UploadTotals(count, size);
        }

        /// <summary>Filter used by <see cref="CountUploadableFiles"/>.</summary>
        public static bool IsCountedFile(string fullName, FileAttributes attributes, long length)
            => !attributes.HasFlag(FileAttributes.Hidden)
               && !attributes.HasFlag(FileAttributes.Directory)
               && !fullName.Contains("@eaDir")
               && length > 0;

        /// <summary>
        /// Whether a local file is skipped instead of uploaded: empty files are
        /// never sent, and a file already in the channel index with a newer
        /// modification date than the local copy is kept as is.
        /// </summary>
        public static bool ShouldSkipFile(long localSize, DateTime localModified, BsonFileManagerModel? existing)
            => localSize == 0 || (existing != null && existing.DateModified > localModified);

        /// <summary>
        /// Prefix shared by the index paths of the direct children of
        /// <paramref name="currentPath"/>. It is derived from
        /// <see cref="Path.Combine(string, string)"/> because that is how the
        /// upload builds each child's <c>FilePath</c>, so lookups by key match
        /// the stored values byte for byte (trailing slash or not).
        /// </summary>
        public static string ChildPathPrefix(string currentPath)
        {
            string probe = Path.Combine(currentPath, "x");
            return probe.Substring(0, probe.Length - 1);
        }

        /// <summary>
        /// Anchored MongoDB regex selecting the direct children of a folder by
        /// <c>FilePath</c>. The literal prefix lets MongoDB use the unique
        /// <c>FilePath</c> index as a range scan.
        /// </summary>
        public static string DirectChildrenRegex(string currentPath)
            => "^" + Regex.Escape(ChildPathPrefix(currentPath)) + "[^/]*$";
    }

    /// <summary>
    /// In-memory view of the destination index for one folder-upload run.
    /// Each destination folder is read once (all its direct children in one
    /// query) and kept up to date with the entries the run itself creates or
    /// deletes, replacing one MongoDB lookup per file.
    /// </summary>
    public sealed class UploadDestinationCache
    {
        private readonly Dictionary<string, BsonFileManagerModel> _byPath = new(StringComparer.Ordinal);
        private readonly HashSet<string> _loadedFolders = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BsonFileManagerModel> _parents = new(StringComparer.Ordinal);

        public bool IsFolderLoaded(string currentPath) => _loadedFolders.Contains(currentPath);

        /// <summary>Records the existing children of <paramref name="currentPath"/>.</summary>
        public void LoadFolder(string currentPath, IEnumerable<BsonFileManagerModel> children)
        {
            foreach (var child in children)
            {
                if (child?.FilePath == null) continue;
                // FilePath is unique in the index, keep the first if the data says otherwise.
                _byPath.TryAdd(child.FilePath, child);
            }
            _loadedFolders.Add(currentPath);
        }

        /// <summary>
        /// Existing entry at <paramref name="path"/>. Only meaningful for paths
        /// inside a loaded folder; unknown paths return null.
        /// </summary>
        public BsonFileManagerModel? Get(string path)
            => _byPath.TryGetValue(path, out var entry) ? entry : null;

        public void Set(string path, BsonFileManagerModel entry) => _byPath[path] = entry;

        public void Remove(string path) => _byPath.Remove(path);

        public BsonFileManagerModel? GetParent(string currentPath)
            => _parents.TryGetValue(currentPath, out var parent) ? parent : null;

        public void SetParent(string currentPath, BsonFileManagerModel parent) => _parents[currentPath] = parent;

        public void InvalidateParent(string currentPath) => _parents.Remove(currentPath);
    }
}
