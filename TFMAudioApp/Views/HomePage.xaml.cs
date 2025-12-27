using TFMAudioApp.Helpers;

namespace TFMAudioApp.Views;

public partial class HomePage : ContentPage
{
    public HomePage()
    {
        InitializeComponent();
    }

    private async void OnChannelsTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync(Constants.ChannelsRoute);
    }

    private async void OnPlaylistsTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync(Constants.PlaylistsRoute);
    }

    private async void OnDownloadsTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync(Constants.DownloadsRoute);
    }

    private async void OnSettingsTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync(Constants.SettingsRoute);
    }
}
