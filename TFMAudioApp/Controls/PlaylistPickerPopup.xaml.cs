using CommunityToolkit.Maui.Views;
using TFMAudioApp.Models;

namespace TFMAudioApp.Controls;

public partial class PlaylistPickerPopup : Popup
{
    private readonly List<Playlist> _allPlaylists;
    private Playlist? _selectedPlaylist;

    public new PlaylistPickerResult? Result { get; private set; }

    public PlaylistPickerPopup(List<Playlist> playlists, string trackName)
    {
        InitializeComponent();

        _allPlaylists = playlists;
        TrackNameLabel.Text = trackName;
        PlaylistsCollection.ItemsSource = playlists;

        if (playlists.Count == 0)
        {
            EmptyLabel.Text = "No playlists yet. Create one!";
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var searchText = e.NewTextValue?.ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            PlaylistsCollection.ItemsSource = _allPlaylists;
        }
        else
        {
            PlaylistsCollection.ItemsSource = _allPlaylists
                .Where(p => p.Name.ToLowerInvariant().Contains(searchText))
                .ToList();
        }
    }

    private void OnPlaylistTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Playlist playlist)
        {
            _selectedPlaylist = playlist;
            Result = new PlaylistPickerResult
            {
                SelectedPlaylist = playlist,
                CreateNew = false
            };
            Close(Result);
        }
    }

    private void OnCloseClicked(object? sender, EventArgs e)
    {
        Close(null);
    }

    private void OnCreatePlaylistClicked(object? sender, EventArgs e)
    {
        Result = new PlaylistPickerResult
        {
            CreateNew = true
        };
        Close(Result);
    }
}

public class PlaylistPickerResult
{
    public Playlist? SelectedPlaylist { get; set; }
    public bool CreateNew { get; set; }
}
