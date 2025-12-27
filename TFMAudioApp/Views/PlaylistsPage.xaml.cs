using TFMAudioApp.ViewModels;

namespace TFMAudioApp.Views;

public partial class PlaylistsPage : ContentPage
{
    private readonly PlaylistsViewModel _viewModel;

    public PlaylistsPage(PlaylistsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Load data in background - don't block navigation
        _ = _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
    }
}
