using TFMAudioApp.ViewModels;

namespace TFMAudioApp.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Load data in background - don't block navigation
        _ = _viewModel.LoadSettingsCommand.ExecuteAsync(null);
    }
}
