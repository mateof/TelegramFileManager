namespace TFMAudioApp.Models;

/// <summary>
/// Telegram channel information
/// </summary>
public class Channel
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Title => Name; // Alias for convenience
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool CanPost { get; set; }
    public bool IsFavorite { get; set; }
    public string Type { get; set; } = string.Empty; // channel, group, chat
    public int FileCount { get; set; }
}

/// <summary>
/// Channel with detailed statistics
/// </summary>
public class ChannelDetail : Channel
{
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
    public DateTime? LastRefreshed { get; set; }
    public int AudioCount { get; set; }
    public int VideoCount { get; set; }
    public int DocumentCount { get; set; }
}

/// <summary>
/// Telegram folder containing channels
/// </summary>
public class ChannelFolder
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string IconEmoji { get; set; } = string.Empty;
    public int ChannelCount { get; set; }
    public List<Channel> Channels { get; set; } = new();
}

/// <summary>
/// All channels organized by folders
/// </summary>
public class ChannelsWithFolders
{
    public List<ChannelFolder> Folders { get; set; } = new();
    public List<Channel> UngroupedChannels { get; set; } = new();
    public int TotalChannels { get; set; }
}

/// <summary>
/// File item within a channel
/// </summary>
public class ChannelFile : System.ComponentModel.INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // Audio, Video, Document, Photo, Folder
    public DateTime DateCreated { get; set; }
    public DateTime DateModified { get; set; }
    public int? MessageId { get; set; }
    public bool IsFile { get; set; }
    public bool HasChildren { get; set; }
    public string? StreamUrl { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ThumbnailUrl { get; set; }

    private bool _isCached;
    public bool IsCached
    {
        get => _isCached;
        set
        {
            if (_isCached != value)
            {
                _isCached = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsCached)));
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    // Computed properties for optimized binding
    public bool IsFolder => !IsFile;

    private bool IsAudioExtension
    {
        get
        {
            var ext = System.IO.Path.GetExtension(Name)?.ToLowerInvariant();
            return ext is ".mp3" or ".flac" or ".ogg" or ".opus" or ".aac" or ".wav" or ".m4a" or ".wma" or ".ape";
        }
    }

    public bool IsAudioFile => IsFile && (Category?.ToLowerInvariant() == "audio" || IsAudioExtension);

    public string IconEmoji => IsFile
        ? Category?.ToLowerInvariant() switch
        {
            "audio" => "🎵",
            "video" => "🎬",
            "photo" or "image" => "🖼️",
            "document" => "📄",
            _ => GetIconByExtension()
        }
        : "📁";

    public string FileInfo => IsFile
        ? $"{FormatFileSize(Size)} • {Type?.TrimStart('.').ToUpperInvariant()}"
        : string.Empty;

    private string GetIconByExtension()
    {
        var ext = System.IO.Path.GetExtension(Name)?.ToLowerInvariant();
        return ext switch
        {
            ".mp3" or ".flac" or ".ogg" or ".opus" or ".aac" or ".wav" or ".m4a" or ".wma" or ".ape" => "🎵",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => "🎬",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" => "🖼️",
            ".pdf" => "📕",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "📦",
            _ => "📄"
        };
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
