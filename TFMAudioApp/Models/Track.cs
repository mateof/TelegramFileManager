using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TFMAudioApp.Models;

/// <summary>
/// Individual track in a playlist or queue
/// </summary>
public class Track : INotifyPropertyChanged
{
    public string FileId { get; set; } = string.Empty;
    public int? MessageId { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int Order { get; set; }
    public DateTime DateAdded { get; set; }
    public bool IsLocalFile { get; set; }
    public string? DirectUrl { get; set; }
    public string StreamUrl { get; set; } = string.Empty;

    // Metadata (from AudioInfo when available)
    private double? _duration;
    public double? Duration
    {
        get => _duration;
        set { _duration = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationFormatted)); }
    }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }

    // Cache status
    private bool _isCached;
    public bool IsCached
    {
        get => _isCached;
        set { _isCached = value; OnPropertyChanged(); OnPropertyChanged(nameof(TrackInfo)); }
    }

    // Download progress
    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set { _isDownloading = value; OnPropertyChanged(); }
    }

    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        set { _downloadProgress = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Display name - uses Title if available, otherwise FileName
    /// </summary>
    public string DisplayName => !string.IsNullOrEmpty(Title) ? Title : System.IO.Path.GetFileNameWithoutExtension(FileName);

    /// <summary>
    /// Display artist - uses Artist if available, otherwise ChannelName
    /// </summary>
    public string DisplayArtist => !string.IsNullOrEmpty(Artist) ? Artist : ChannelName;

    /// <summary>
    /// Track info for display (channel name + offline status)
    /// </summary>
    public string TrackInfo => IsCached ? $"{ChannelName} • Offline" : ChannelName;

    /// <summary>
    /// Duration formatted as mm:ss or h:mm:ss
    /// </summary>
    public string DurationFormatted
    {
        get
        {
            if (!Duration.HasValue || Duration.Value <= 0) return "--:--";
            var ts = TimeSpan.FromSeconds(Duration.Value);
            return ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"m\:ss");
        }
    }
}

/// <summary>
/// Request to add a track to a playlist
/// </summary>
public class AddTrackRequest
{
    public string FileId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = "audio/mpeg";
    public long FileSize { get; set; }
    public string? DirectUrl { get; set; }
}

/// <summary>
/// Audio file metadata
/// </summary>
public class AudioInfo
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string MimeType { get; set; } = "audio/mpeg";
    public double? Duration { get; set; }
    public int? Bitrate { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public bool SupportsStreaming { get; set; } = true;
}
