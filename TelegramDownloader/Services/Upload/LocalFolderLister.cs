using TelegramDownloader.Models.FileSystem;

namespace TelegramDownloader.Services.Upload
{
    /// <summary>
    /// Lists a folder of the local root the way the server-to-Telegram upload
    /// walks it. It reproduces what the upload used to obtain from the EJ2
    /// <c>PhysicalFileProvider.GetFiles(path, false)</c> of the UI component
    /// library plus the projection the service applied to it (same order, same
    /// fields, same <see cref="FsEntry.FilterPath"/> format), without that
    /// dependency. A test compares both on a real folder.
    /// </summary>
    public static class LocalFolderLister
    {
        /// <summary>Name of the Synology metadata folders the upload skips.</summary>
        public const string SynologyMetadataFolder = "@eaDir";

        /// <summary>
        /// Content of <paramref name="path"/> (relative to <paramref name="rootPath"/>,
        /// with a leading and a trailing slash, e.g. "/music/"): sub-folders first,
        /// then files, both in file-system order, hidden items left out and
        /// Synology <c>@eaDir</c> folders skipped. A folder's size is the total
        /// of the files directly inside it (not recursive).
        /// </summary>
        /// <exception cref="UnauthorizedAccessException">The path escapes the root.</exception>
        public static List<FsEntry> ListForUpload(string rootPath, string path)
        {
            path ??= string.Empty;
            string fullPath = rootPath + path;
            if (Path.GetFullPath(fullPath) != Path.GetDirectoryName(fullPath) + Path.DirectorySeparatorChar)
                throw new UnauthorizedAccessException("Access denied for Directory-traversal");

            var directory = new DirectoryInfo(fullPath);
            string filterPath = GetRelativePath(rootPath, directory.FullName);
            var result = new List<FsEntry>();

            foreach (var sub in directory.GetDirectories())
            {
                if ((sub.Attributes & FileAttributes.Hidden) != 0) continue;
                if (sub.Name == SynologyMetadataFolder) continue;
                result.Add(new FsEntry
                {
                    Name = sub.Name,
                    IsFile = false,
                    Size = GetDirectorySize(sub),
                    DateModified = sub.LastWriteTime,
                    DateCreated = sub.CreationTime,
                    HasChild = HasSubDirectories(sub.FullName),
                    FilterPath = filterPath
                });
            }

            foreach (var file in directory.GetFiles("*"))
            {
                if ((file.Attributes & FileAttributes.Hidden) != 0) continue;
                result.Add(new FsEntry
                {
                    Name = file.Name,
                    IsFile = true,
                    Size = file.Length,
                    DateModified = file.LastWriteTime,
                    DateCreated = file.CreationTime,
                    HasChild = false,
                    FilterPath = filterPath
                });
            }

            return result;
        }

        /// <summary>
        /// Path of <paramref name="fullPath"/> relative to <paramref name="rootPath"/>
        /// with the platform separator, e.g. "\music\" on Windows and "/music/"
        /// on Linux; empty when it is not under the root.
        /// </summary>
        public static string GetRelativePath(string rootPath, string fullPath)
        {
            if (string.IsNullOrEmpty(rootPath) || string.IsNullOrEmpty(fullPath))
                return string.Empty;

            var rootDirectory = new DirectoryInfo(rootPath);
            string separator = Path.DirectorySeparatorChar.ToString();
            if (rootDirectory.FullName.Substring(rootDirectory.FullName.Length - 1) == separator)
            {
                if (fullPath.Contains(rootDirectory.FullName))
                    return fullPath.Substring(rootPath.Length - 1);
            }
            else if (fullPath.Contains(rootDirectory.FullName + separator))
            {
                return separator + fullPath.Substring(rootPath.Length + 1);
            }
            return string.Empty;
        }

        /// <summary>Total length of the files directly inside <paramref name="dir"/>, hidden ones included.</summary>
        private static long GetDirectorySize(DirectoryInfo dir)
        {
            long size = 0;
            foreach (var file in dir.GetFiles())
                size += file.Length;
            return size;
        }

        private static bool HasSubDirectories(string path)
        {
            try
            {
                return new DirectoryInfo(path).GetDirectories().Length != 0;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
