using TFMAudioApp.ViewModels;

namespace TFMAudioApp.Views;

public partial class PlaylistDetailPage : ContentPage
{
    public PlaylistDetailPage(PlaylistDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
