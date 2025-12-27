using CommunityToolkit.Maui.Views;
using TFMAudioApp.Controls;
using TFMAudioApp.ViewModels;

namespace TFMAudioApp.Views;

public partial class ChannelDetailPage : ContentPage
{
    private readonly ChannelDetailViewModel _viewModel;

    public ChannelDetailPage(ChannelDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Subscribe to search visibility changes to focus the search bar
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ChannelDetailViewModel.IsSearchVisible) && _viewModel.IsSearchVisible)
            {
                // Focus the search bar when it becomes visible
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(100); // Small delay to ensure UI is updated
                    SearchBarControl.Focus();
                });
            }
        };
    }

    private async void OnSearchTapped(object? sender, TappedEventArgs e)
    {
        _viewModel.ToggleSearchCommand.Execute(null);

        if (_viewModel.IsSearchVisible)
        {
            await Task.Delay(100);
            SearchBarControl.Focus();
        }
    }

    private async void OnFilterTapped(object? sender, TappedEventArgs e)
    {
        await ShowFilterPopupAsync();
    }

    private async Task ShowFilterPopupAsync()
    {
        var currentOptions = new FilterOptions
        {
            ShowFolders = _viewModel.ShowFolders,
            SelectedExtensions = _viewModel.SelectedExtensions?.ToList(),
            SortIndex = _viewModel.SelectedSortIndex,
            SortDescending = _viewModel.SortDescending
        };

        var popup = new FilterPopup(currentOptions);
        var result = await this.ShowPopupAsync(popup) as FilterResult;

        if (result != null)
        {
            await _viewModel.ApplyFiltersAsync(result);
        }
    }
}
