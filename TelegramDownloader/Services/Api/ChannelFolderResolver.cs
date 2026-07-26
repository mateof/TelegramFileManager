using Syncfusion.Blazor.FileManager;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services.Api
{
    /// <summary>
    /// Translates between the paths a REST client uses and the two path spaces
    /// the channel index stores.
    ///
    /// Every indexed entry carries:
    /// <list type="bullet">
    /// <item><c>FilterPath</c> - the folder it lives in, ending with <c>/</c> (<c>/music/rock/</c>).</item>
    /// <item><c>FilePath</c> - its own full path, without a trailing slash (<c>/music/rock/song.mp3</c>).</item>
    /// </list>
    /// The root document is special: it is named <c>Files</c> and has all three
    /// path fields empty, while its children use <c>/</c> as their folder path.
    /// </summary>
    public class ChannelFolderResolver
    {
        private readonly IDbService _db;

        public ChannelFolderResolver(IDbService db)
        {
            _db = db;
        }

        /// <summary>Normalises a client-supplied folder path to the stored form.</summary>
        public static string NormalizeFolderPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "/";
            var p = path.Replace("\\", "/").Trim();
            if (!p.StartsWith('/')) p = "/" + p;
            if (!p.EndsWith('/')) p += "/";
            while (p.Contains("//")) p = p.Replace("//", "/");
            return p;
        }

        /// <summary>
        /// Resolves the folder addressed by an id or a path. Returns the root
        /// document when neither is supplied.
        /// </summary>
        public async Task<BsonFileManagerModel?> ResolveFolder(string channelId, string? folderId, string? path, string? collectionId = null)
        {
            if (!string.IsNullOrWhiteSpace(folderId))
            {
                var byId = await _db.getFileById(channelId, folderId, collectionId ?? "directory");
                if (byId != null && !byId.IsFile) return byId;
                return byId; // caller decides how to treat a file id
            }

            var folderPath = NormalizeFolderPath(path);
            if (folderPath == "/")
                return await _db.getRootFolder(channelId, collectionId ?? "directory");

            // A folder's own FilePath has no trailing slash.
            return await _db.getFileByPath(channelId, folderPath.TrimEnd('/'), collectionId ?? "directory");
        }

        /// <summary>
        /// Folder path used by the children of <paramref name="folder"/>, i.e.
        /// the value stored in their <c>FilterPath</c>.
        /// </summary>
        public static string ChildFolderPath(BsonFileManagerModel folder)
        {
            if (string.IsNullOrEmpty(folder.FilePath)) return "/";
            return folder.FilePath.EndsWith('/') ? folder.FilePath : folder.FilePath + "/";
        }

        /// <summary>
        /// Value to pass as the <c>path</c> argument when creating a child of
        /// <paramref name="folder"/>.
        /// </summary>
        public static string CreateChildPath(BsonFileManagerModel folder) =>
            string.IsNullOrEmpty(folder.FilePath) ? "/" : folder.FilePath;

        /// <summary>Lists the direct children of a folder.</summary>
        public async Task<List<BsonFileManagerModel>> ListChildren(string channelId, BsonFileManagerModel folder, string? collectionId = null)
        {
            var childPath = ChildFolderPath(folder);
            var items = await _db.getAllFilesInDirectoryPath(channelId, childPath, collectionId ?? "directory");
            return items ?? new List<BsonFileManagerModel>();
        }

        /// <summary>
        /// Builds the breadcrumb from the channel root down to
        /// <paramref name="folder"/>, inclusive.
        /// </summary>
        public static List<(string Name, string Path)> Breadcrumbs(BsonFileManagerModel folder)
        {
            var crumbs = new List<(string, string)> { ("Files", "/") };
            var path = ChildFolderPath(folder);
            if (path == "/") return crumbs.Select(c => (c.Item1, c.Item2)).ToList();

            var acc = "/";
            foreach (var segment in path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                acc += segment + "/";
                crumbs.Add((segment, acc));
            }
            return crumbs;
        }

        /// <summary>
        /// Converts a stored entry into the Syncfusion shape the existing
        /// <c>IFileService</c> mutation methods expect.
        /// </summary>
        public static FileManagerDirectoryContent ToContent(BsonFileManagerModel m) => m.toFileManagerContent();
    }
}
