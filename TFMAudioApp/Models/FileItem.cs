namespace TFMAudioApp.Models;

/// <summary>
/// Generic file/folder item for navigation
/// </summary>
public class FileItem
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

    /// <summary>
    /// Icon based on category/type
    /// </summary>
    public string Icon => Category switch
    {
        "Folder" => "📁",
        "Audio" => "🎵",
        "Video" => "🎬",
        "Photo" => "🖼️",
        "Document" => "📄",
        _ => "📎"
    };
}

/// <summary>
/// Folder contents with navigation info
/// </summary>
public class FolderContents
{
    public string CurrentPath { get; set; } = string.Empty;
    public string CurrentFolderId { get; set; } = string.Empty;
    public string? ParentPath { get; set; }
    public string? ParentFolderId { get; set; }
    public string FolderName { get; set; } = string.Empty;
    public List<FileItem> Items { get; set; } = new();
    public int TotalItems { get; set; }
    public FolderStats? Stats { get; set; }
}

/// <summary>
/// Folder statistics
/// </summary>
public class FolderStats
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
/// Browse request parameters
/// </summary>
public class BrowseRequest
{
    public string? FolderId { get; set; }
    public string? Path { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public string? Filter { get; set; }
    public string? SortBy { get; set; } = "name";
    public bool SortDescending { get; set; }
    public string? SearchText { get; set; }
}

/// <summary>
/// Channel files request parameters
/// </summary>
public class ChannelFilesRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? Filter { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
    public string? FolderId { get; set; }
    public string? SearchText { get; set; }
}
