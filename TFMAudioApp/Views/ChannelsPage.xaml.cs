using TFMAudioApp.ViewModels;

namespace TFMAudioApp.Views;

public partial class ChannelsPage : ContentPage
{
    private readonly ChannelsViewModel _viewModel;

    // Cache colors to avoid repeated resource lookups
    private static Color? _primaryColor;
    private static Color? _grayColor;

    public ChannelsPage(ChannelsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        // Cache colors once
        _primaryColor ??= (Color)Application.Current!.Resources["Primary"];
        _grayColor ??= (Color)Application.Current!.Resources["Gray600"];
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Update UI immediately (don't wait for data)
        UpdateTabColors();

        // Load data in background - don't block navigation
        _ = _viewModel.LoadDataCommand.ExecuteAsync(null);
    }

    private void OnAllTabClicked(object sender, EventArgs e)
    {
        _viewModel.SelectedTabIndex = 0;
        UpdateTabColors();
    }

    private void OnFavoritesTabClicked(object sender, EventArgs e)
    {
        _viewModel.SelectedTabIndex = 1;
        UpdateTabColors();
    }

    private void OnOwnedTabClicked(object sender, EventArgs e)
    {
        _viewModel.SelectedTabIndex = 2;
        UpdateTabColors();
    }

    private void OnFoldersTabClicked(object sender, EventArgs e)
    {
        _viewModel.SelectedTabIndex = 3;
        UpdateTabColors();
    }

    private void UpdateTabColors()
    {
        AllTabButton.BackgroundColor = _viewModel.SelectedTabIndex == 0 ? _primaryColor : _grayColor;
        FavoritesTabButton.BackgroundColor = _viewModel.SelectedTabIndex == 1 ? _primaryColor : _grayColor;
        OwnedTabButton.BackgroundColor = _viewModel.SelectedTabIndex == 2 ? _primaryColor : _grayColor;
        FoldersTabButton.BackgroundColor = _viewModel.SelectedTabIndex == 3 ? _primaryColor : _grayColor;
    }
}
