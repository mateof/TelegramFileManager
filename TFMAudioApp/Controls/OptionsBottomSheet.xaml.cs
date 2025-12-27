namespace TFMAudioApp.Controls;

public partial class OptionsBottomSheet : ContentView
{
    public event EventHandler<string>? OptionSelected;
    public event EventHandler? Dismissed;

    public OptionsBottomSheet()
    {
        InitializeComponent();
    }

    private void OnBackgroundTapped(object? sender, TappedEventArgs e)
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void OnAddToPlaylistTapped(object? sender, TappedEventArgs e)
    {
        OptionSelected?.Invoke(this, "AddToPlaylist");
    }

    private void OnViewQueueTapped(object? sender, TappedEventArgs e)
    {
        OptionSelected?.Invoke(this, "ViewQueue");
    }

    private void OnShareTapped(object? sender, TappedEventArgs e)
    {
        OptionSelected?.Invoke(this, "Share");
    }

    private void OnStopTapped(object? sender, TappedEventArgs e)
    {
        OptionSelected?.Invoke(this, "Stop");
    }

    private void OnCancelTapped(object? sender, TappedEventArgs e)
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
