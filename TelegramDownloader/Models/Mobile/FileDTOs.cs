using System.ComponentModel.DataAnnotations;

namespace TelegramDownloader.Models.Mobile
{
    #region Response DTOs

    /// <summary>
    /// Generic file/folder item for navigation
    /// </summary>
    public class FileItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public bool HasChildren { get; set; }
        public long Size { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public DateTime DateModified { get; set; }
        public DateTime DateCreated { get; set; }
        public string? StreamUrl { get; set; }
        public string? DownloadUrl { get; set; }

        public static FileItemDto FromLocalFile(FileInfo file, string relativePath, string baseUrl = "")
        {
            var ext = file.Extension.ToLowerInvariant();
            var isAudio = IsAudioFile(ext);
            var isVideo = IsVideoFile(ext);

            string? streamUrl = null;
            if (isAudio)
            {
                streamUrl = $"{baseUrl}/api/mobile/stream/local?path={Uri.EscapeDataString(relativePath)}";
            }
            else if (isVideo)
            {
                streamUrl = $"{baseUrl}/api/localvideo/stream?path={Uri.EscapeDataString(relativePath)}";
            }

            return new FileItemDto
            {
                Id = relativePath,
                Name = file.Name,
                Path = relativePath,
                IsFolder = false,
                HasChildren = false,
                Size = file.Length,
                Type = ext,
                Category = GetCategory(ext),
                DateModified = file.LastWriteTimeUtc,
                DateCreated = file.CreationTimeUtc,
                StreamUrl = streamUrl,
                DownloadUrl = $"{baseUrl}/local/{Uri.EscapeDataString(relativePath)}"
            };
        }

        public static FileItemDto FromLocalDirectory(DirectoryInfo dir, string relativePath)
        {
            return new FileItemDto
            {
                Id = relativePath,
                Name = dir.Name,
                Path = relativePath,
                IsFolder = true,
                HasChildren = dir.EnumerateFileSystemInfos().Any(),
                Size = 0,
                Type = "folder",
                Category = "Folder",
                DateModified = dir.LastWriteTimeUtc,
                DateCreated = dir.CreationTimeUtc
            };
        }

        private static bool IsAudioFile(string ext)
        {
            var audioExtensions = new[] { ".mp3", ".ogg", ".flac", ".aac", ".wav", ".m4a", ".wma", ".opus" };
            return audioExtensions.Contains(ext);
        }

        private static bool IsVideoFile(string ext)
        {
            var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v" };
            return videoExtensions.Contains(ext);
        }

        private static string GetCategory(string ext)
        {
            if (IsAudioFile(ext)) return "Audio";
            if (IsVideoFile(ext)) return "Video";

            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg" };
            if (imageExtensions.Contains(ext)) return "Photo";

            var documentExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt" };
            if (documentExtensions.Contains(ext)) return "Document";

            return "Document";
        }
    }

    /// <summary>
    /// Contents of a folder with navigation info
    /// </summary>
    public class FolderContentsDto
    {
        public string CurrentPath { get; set; } = string.Empty;
        public string CurrentFolderId { get; set; } = string.Empty;
        public string? ParentPath { get; set; }
        public string? ParentFolderId { get; set; }
        public string FolderName { get; set; } = string.Empty;
        public List<FileItemDto> Items { get; set; } = new();
        public int TotalItems { get; set; }
        public FolderStatsDto? Stats { get; set; }
    }

    /// <summary>
    /// Statistics about folder contents
    /// </summary>
    public class FolderStatsDto
    {
        public int FolderCount { get; set; }
        public int FileCount { get; set; }
        public int AudioCount { get; set; }
        public int VideoCount { get; set; }
        public int DocumentCount { get; set; }
        public int PhotoCount { get; set; }
        public long TotalSize { get; set; }
    }

    /// <summary>
    /// Audio file information
    /// </summary>
    public class AudioInfoDto
    {
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public double? Duration { get; set; }
        public int? Bitrate { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public bool SupportsStreaming { get; set; } = true;
    }

    #endregion

    #region Request DTOs

    /// <summary>
    /// Parameters for browsing files/folders
    /// </summary>
    public class BrowseRequest
    {
        /// <summary>
        /// Folder ID to browse (root if empty)
        /// </summary>
        public string? FolderId { get; set; }

        /// <summary>
        /// Path to browse (alternative to FolderId)
        /// </summary>
        public string? Path { get; set; }

        [Range(1, int.MaxValue)]
        public int Page { get; set; } = 1;

        [Range(1, 500)]
        public int PageSize { get; set; } = 100;

        /// <summary>
        /// Filter by category: audio, video, documents, photos, all
        /// </summary>
        public string? Filter { get; set; }

        /// <summary>
        /// Sort by: name, date, size
        /// </summary>
        public string? SortBy { get; set; } = "name";

        public bool SortDescending { get; set; } = false;

        /// <summary>
        /// Search text to filter files
        /// </summary>
        public string? SearchText { get; set; }
    }

    #endregion
}
