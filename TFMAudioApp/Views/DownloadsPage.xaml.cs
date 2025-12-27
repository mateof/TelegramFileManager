using TFMAudioApp.ViewModels;

namespace TFMAudioApp.Views;

public partial class DownloadsPage : ContentPage
{
    private readonly DownloadsViewModel _viewModel;

    public DownloadsPage(DownloadsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Load data in background - don't block navigation
        _ = _viewModel.InitializeAsync();
    }

    private void OnDownloadsTabClicked(object sender, EventArgs e)
    {
        _viewModel.SelectedTabIndex = 0;
    }

    private void OnCachedTabClicked(object sender, EventArgs e)
    {
        _viewModel.SelectedTabIndex = 1;
    }
}
