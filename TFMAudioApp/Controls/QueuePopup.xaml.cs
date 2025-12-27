using CommunityToolkit.Maui.Views;
using TFMAudioApp.Models;

namespace TFMAudioApp.Controls;

public partial class QueuePopup : Popup
{
    public event EventHandler? ClearQueueRequested;
    public event EventHandler? ShuffleRequested;

    public QueuePopup(IList<Track> queue, Track? currentTrack, int currentIndex)
    {
        InitializeComponent();

        // Set current track info
        if (currentTrack != null)
        {
            CurrentTrackLabel.Text = currentTrack.DisplayName;
            CurrentArtistLabel.Text = currentTrack.DisplayArtist;
        }
        else
        {
            CurrentTrackLabel.Text = "No track playing";
            CurrentArtistLabel.Text = "";
        }

        // Set queue count
        QueueCountLabel.Text = queue.Count > 0
            ? $"{queue.Count} tracks • Next: {queue.Count - currentIndex - 1} remaining"
            : "No tracks in queue";

        // Set order numbers and show upcoming tracks
        var upcomingTracks = queue.Skip(currentIndex + 1).ToList();
        for (int i = 0; i < upcomingTracks.Count; i++)
        {
            upcomingTracks[i].Order = i + 1;
        }

        QueueList.ItemsSource = upcomingTracks;
    }

    private void OnCloseClicked(object? sender, EventArgs e)
    {
        Close(null);
    }

    private void OnClearQueueClicked(object? sender, EventArgs e)
    {
        ClearQueueRequested?.Invoke(this, EventArgs.Empty);
        Close("clear");
    }

    private void OnShuffleClicked(object? sender, EventArgs e)
    {
        ShuffleRequested?.Invoke(this, EventArgs.Empty);
        Close("shuffle");
    }
}
