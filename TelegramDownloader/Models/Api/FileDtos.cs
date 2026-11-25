using TelegramDownloader.Data;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;

namespace TelegramDownloader.Models.Api
{
    /// <summary>
    /// A file or folder as stored in the channel index (MongoDB) or on the local disk.
    /// </summary>
    public class ApiFileDto
    {
        /// <summary>MongoDB id for remote entries, relative path for local entries.</summary>
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>Folder path this entry lives in, always ending with <c>/</c>.</summary>
        public string Path { get; set; } = "/";

        /// <summary>Id of the parent folder (remote entries only).</summary>
        public string? ParentId { get; set; }

        public bool IsFile { get; set; }
        public bool HasChildren { get; set; }

        public long Size { get; set; }
        public string SizeText { get; set; } = "0 B";

        /// <summary>File extension including the dot, or <c>folder</c>.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Audio, Video, Photo, Document, Archive, Folder...</summary>
        public string Category { get; set; } = string.Empty;

        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }

        /// <summary>Telegram message id backing this file, when not split.</summary>
        public int? MessageId { get; set; }

        /// <summary>The message of the file, or of its first part when it is split: where to find it in the chat.</summary>
        public int? FirstMessageId { get; set; }

        /// <summary>True when the file was uploaded as several Telegram messages.</summary>
        public bool IsSplit { get; set; }

        public string? Md5Hash { get; set; }
        public string? XxHash { get; set; }

        /// <summary>Absolute URL for range-capable streaming, when applicable.</summary>
        public string? StreamUrl { get; set; }

        /// <summary>Absolute URL that downloads the whole file.</summary>
        public string? DownloadUrl { get; set; }

        /// <summary>
        /// v1 URL of the bytes for inline viewing (<c>&lt;img&gt;</c>, PDF viewer,
        /// <c>&lt;audio&gt;</c>), with HTTP range support.
        /// </summary>
        public string? ContentUrl { get; set; }

        /// <summary>Same bytes as <see cref="ContentUrl"/> with <c>Content-Disposition: attachment</c>.</summary>
        public string? ContentDownloadUrl { get; set; }

        /// <summary>
        /// v1 URL to hand to a media element (audio and video only). For video
        /// formats browsers cannot play it points to the transcoding stream.
        /// </summary>
        public string? PlaybackUrl { get; set; }

        /// <summary>v1 URL of a small JPEG preview (photos and videos backed by one message).</summary>
        public string? ThumbnailUrl { get; set; }

        public static ApiFileDto FromBson(BsonFileManagerModel m, string channelId, string baseUrl, string? collectionId = null)
        {
            var type = m.Type ?? string.Empty;
            var category = m.IsFile ? CategoryOf(type) : "Folder";

            string? streamUrl = null;
            string? downloadUrl = null;
            if (m.IsFile)
            {
                downloadUrl = $"{baseUrl}/api/file/GetFileByTfmId/{Uri.EscapeDataString(m.Name)}?idChannel={channelId}&idFile={m.Id}";
                if (category == "Audio" || category == "Video")
                    streamUrl = $"{baseUrl}/api/file/GetFileStreamCached/{channelId}/{m.Id}/{Uri.EscapeDataString(m.Name)}";
            }

            var dto = new ApiFileDto
            {
                Id = m.Id,
                Name = m.Name,
                Path = string.IsNullOrEmpty(m.FilterPath) ? "/" : m.FilterPath.Replace("\\", "/"),
                ParentId = m.ParentId,
                IsFile = m.IsFile,
                HasChildren = !m.IsFile && m.HasChild,
                Size = m.Size,
                SizeText = HelperService.SizeSuffix(m.Size),
                Type = m.IsFile ? type : "folder",
                Category = category,
                DateCreated = m.DateCreated,
                DateModified = m.DateModified,
                MessageId = m.MessageId,
                FirstMessageId = m.IsFile ? m.MessageId ?? m.ListMessageId?.Select(i => (int?)i).FirstOrDefault() : null,
                IsSplit = m.isSplit,
                Md5Hash = m.MD5Hash,
                XxHash = m.XXHash,
                StreamUrl = streamUrl,
                DownloadUrl = downloadUrl
            };

            MediaUrlBuilder.Apply(dto, baseUrl, channelId, collectionId);
            if (!string.IsNullOrEmpty(collectionId) && dto.IsFile)
            {
                // The legacy endpoints only read channel indexes, not shared collections.
                dto.StreamUrl = streamUrl != null ? dto.ContentUrl : null;
                dto.DownloadUrl = dto.ContentDownloadUrl;
            }
            return dto;
        }

        public static ApiFileDto FromLocalFile(FileInfo file, string relativePath, string baseUrl)
        {
            var ext = file.Extension.ToLowerInvariant();
            var category = CategoryOf(ext);
            string? streamUrl = null;
            if (category == "Video")
                streamUrl = $"{baseUrl}/api/localvideo/stream?path={Uri.EscapeDataString(relativePath)}";
            else if (category == "Audio")
                streamUrl = $"{baseUrl}/local/{EscapePath(relativePath)}";

            return new ApiFileDto
            {
                Id = relativePath,
                Name = file.Name,
                Path = NormalizeFolder(System.IO.Path.GetDirectoryName(relativePath)),
                IsFile = true,
                HasChildren = false,
                Size = file.Length,
                SizeText = HelperService.SizeSuffix(file.Length),
                Type = ext,
                Category = category,
                DateCreated = file.CreationTimeUtc,
                DateModified = file.LastWriteTimeUtc,
                StreamUrl = streamUrl,
                DownloadUrl = $"{baseUrl}/local/{EscapePath(relativePath)}",
                ContentUrl = MediaUrlBuilder.LocalContent(baseUrl, relativePath, attachment: false),
                ContentDownloadUrl = MediaUrlBuilder.LocalContent(baseUrl, relativePath, attachment: true),
                PlaybackUrl = category == "Audio" || category == "Video"
                    ? MediaUrlBuilder.LocalContent(baseUrl, relativePath, attachment: false)
                    : null
            };
        }

        public static ApiFileDto FromLocalDirectory(DirectoryInfo dir, string relativePath)
        {
            return new ApiFileDto
            {
                Id = relativePath,
                Name = dir.Name,
                Path = NormalizeFolder(System.IO.Path.GetDirectoryName(relativePath)),
                IsFile = false,
                HasChildren = dir.EnumerateFileSystemInfos().Any(),
                Size = 0,
                SizeText = "0 B",
                Type = "folder",
                Category = "Folder",
                DateCreated = dir.CreationTimeUtc,
                DateModified = dir.LastWriteTimeUtc
            };
        }

        private static string EscapePath(string relativePath) =>
            string.Join('/', relativePath.Replace("\\", "/").Split('/').Select(Uri.EscapeDataString));

        private static string NormalizeFolder(string? dir)
        {
            if (string.IsNullOrEmpty(dir)) return "/";
            var p = dir.Replace("\\", "/");
            if (!p.StartsWith('/')) p = "/" + p;
            if (!p.EndsWith('/')) p += "/";
            return p;
        }

        /// <summary>Maps a file extension to the category used across the API.</summary>
        public static string CategoryOf(string? extension)
        {
            var ext = extension?.ToLowerInvariant() ?? string.Empty;
            if (FileExtensionTypeTest.isAudioExtension(ext)) return "Audio";
            if (FileExtensionTypeTest.isVideoExtension(ext)) return "Video";
            return FileTypeInfo.GetCategory(ext) switch
            {
                "Images" => "Photo",
                "Documents" => "Document",
                "Archives" => "Archive",
                "Applications" => "Application",
                "Video" => "Video",
                "Audio" => "Audio",
                _ => "Other"
            };
        }
    }

    /// <summary>Listing of a folder plus navigation and aggregate information.</summary>
    public class ApiFolderContentsDto
    {
        /// <summary>Channel id for remote listings, null for local listings.</summary>
        public string? ChannelId { get; set; }

        public string CurrentPath { get; set; } = "/";
        public string? CurrentFolderId { get; set; }
        public string? ParentPath { get; set; }
        public string? ParentFolderId { get; set; }
        public string FolderName { get; set; } = string.Empty;

        public List<ApiFileDto> Items { get; set; } = new();
        public ApiFolderStatsDto Stats { get; set; } = new();

        /// <summary>Breadcrumb from the root down to the current folder.</summary>
        public List<ApiBreadcrumbDto> Breadcrumbs { get; set; } = new();
    }

    /// <summary>One breadcrumb hop.</summary>
    public class ApiBreadcrumbDto
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = "/";
        public string? FolderId { get; set; }
    }

    /// <summary>Aggregate counters for a folder listing.</summary>
    public class ApiFolderStatsDto
    {
        public int FolderCount { get; set; }
        public int FileCount { get; set; }
        public int AudioCount { get; set; }
        public int VideoCount { get; set; }
        public int PhotoCount { get; set; }
        public int DocumentCount { get; set; }
        public long TotalSize { get; set; }
        public string TotalSizeText { get; set; } = "0 B";
    }

    /// <summary>Query string for browse/search endpoints.</summary>
    public class BrowseQuery : PagedQuery
    {
        /// <summary>Folder id to list (remote listings). Empty means the channel root.</summary>
        public string? FolderId { get; set; }

        /// <summary>Folder path to list. Used when <see cref="FolderId"/> is not supplied.</summary>
        public string? Path { get; set; }

        /// <summary>Restrict to a category: <c>audio</c>, <c>video</c>, <c>photo</c>, <c>document</c>, <c>archive</c>, <c>all</c>.</summary>
        public string? Filter { get; set; }

        /// <summary>Case-insensitive substring match on the file name.</summary>
        public string? Search { get; set; }

        /// <summary>Hide folders and return only files.</summary>
        public bool FilesOnly { get; set; }

        /// <summary>
        /// Browse an imported shared collection instead of the channel index.
        /// The route <c>channelId</c> must then be the collection's channel
        /// (<c>channelId</c> of <c>GET /api/v1/shares/{id}</c>), which is where
        /// its Telegram messages live. Ignored by the local endpoints.
        /// </summary>
        public string? CollectionId { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/channels/{channelId}/files/folders</c>.</summary>
    public class CreateFolderRequest
    {
        /// <summary>Parent folder path, e.g. <c>/music/</c>. Defaults to the root.</summary>
        public string Path { get; set; } = "/";

        /// <summary>Name of the new folder.</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Body of <c>PUT /api/v1/channels/{channelId}/files/{fileId}/name</c>.</summary>
    public class RenameRequest
    {
        public string NewName { get; set; } = string.Empty;
    }

    /// <summary>Body of the delete/copy/move endpoints.</summary>
    public class FileIdsRequest
    {
        /// <summary>Ids of the entries to operate on.</summary>
        public List<string> Ids { get; set; } = new();
    }

    /// <summary>Body of <c>POST /api/v1/channels/{channelId}/files/copy</c> and <c>/move</c>.</summary>
    public class CopyMoveRequest : FileIdsRequest
    {
        /// <summary>Destination folder path, e.g. <c>/backup/</c>.</summary>
        public string TargetPath { get; set; } = "/";

        /// <summary>Destination folder id. Takes precedence over <see cref="TargetPath"/>.</summary>
        public string? TargetFolderId { get; set; }
    }

    /// <summary>Body of the local file-system mutation endpoints.</summary>
    public class LocalPathRequest
    {
        /// <summary>Path relative to the local root, e.g. <c>music/rock</c>.</summary>
        public string Path { get; set; } = string.Empty;
    }

    /// <summary>Body of <c>POST /api/v1/local/folders</c>.</summary>
    public class LocalCreateFolderRequest : LocalPathRequest
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Body of <c>POST /api/v1/local/rename</c>.</summary>
    public class LocalRenameRequest : LocalPathRequest
    {
        public string NewName { get; set; } = string.Empty;
    }

    /// <summary>Body of <c>POST /api/v1/local/copy</c> and <c>/local/move</c>.</summary>
    public class LocalCopyMoveRequest
    {
        /// <summary>Files or folders to copy/move, relative to the local root.</summary>
        public List<string> Paths { get; set; } = new();

        /// <summary>Destination folder, relative to the local root. Created when missing.</summary>
        public string TargetPath { get; set; } = string.Empty;

        /// <summary>Replace entries with the same name in the destination; otherwise they are skipped.</summary>
        public bool Overwrite { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/channels/{channelId}/files/preload</c>.</summary>
    public class PreloadFilesRequest
    {
        /// <summary>File or folder ids; folders are expanded recursively.</summary>
        public List<string> FileIds { get; set; } = new();
    }

    /// <summary>Body of <c>POST /api/v1/local/delete</c>.</summary>
    public class LocalDeleteRequest
    {
        /// <summary>Paths relative to the local root.</summary>
        public List<string> Paths { get; set; } = new();
    }
}
