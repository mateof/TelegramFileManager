using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TFMAudioApp.Models;

/// <summary>
/// Playlist summary
/// </summary>
public class Playlist : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int TrackCount { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime DateModified { get; set; }

    // Cache status
    private bool _isFullyCached;
    public bool IsFullyCached
    {
        get => _isFullyCached;
        set { _isFullyCached = value; OnPropertyChanged(); }
    }

    private bool _isPartiallyCached;
    public bool IsPartiallyCached
    {
        get => _isPartiallyCached;
        set { _isPartiallyCached = value; OnPropertyChanged(); }
    }

    private int _cachedTrackCount;
    public int CachedTrackCount
    {
        get => _cachedTrackCount;
        set { _cachedTrackCount = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Playlist with full track list
/// </summary>
public class PlaylistDetail : Playlist
{
    public List<Track> Tracks { get; set; } = new();
}

/// <summary>
/// Request to create a new playlist
/// </summary>
public class CreatePlaylistRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// Request to update an existing playlist
/// </summary>
public class UpdatePlaylistRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// Request to reorder tracks in a playlist
/// </summary>
public class ReorderTracksRequest
{
    public List<string> OrderedFileIds { get; set; } = new();
}
