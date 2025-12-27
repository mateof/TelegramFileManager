using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TFMAudioApp.Controls;
using TFMAudioApp.Helpers;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsService _settingsService;
    private readonly ICacheService _cacheService;

    [ObservableProperty]
    private string _serverHost = string.Empty;

    [ObservableProperty]
    private string _serverPort = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _cacheSize = "Calculating...";

    [ObservableProperty]
    private int _cachedTrackCount;

    public SettingsViewModel(ISettingsService settingsService, ICacheService cacheService)
    {
        _settingsService = settingsService;
        _cacheService = cacheService;
        Title = "Settings";
    }

    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        var config = await _settingsService.GetServerConfigAsync();
        if (config != null)
        {
            ServerHost = config.Host;
            ServerPort = config.Port.ToString();
            IsConnected = config.IsValid;
        }

        await UpdateCacheInfoAsync();
    }

    private async Task UpdateCacheInfoAsync()
    {
        try
        {
            var sizeBytes = await _cacheService.GetCacheSizeAsync();
            CachedTrackCount = await _cacheService.GetCachedTrackCountAsync();
            CacheSize = FormatFileSize(sizeBytes);
        }
        catch
        {
            CacheSize = "Unable to calculate";
            CachedTrackCount = 0;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        var confirmed = await ConfirmationHelper.ShowConfirmAsync(
            "Disconnect",
            "Are you sure you want to disconnect from the server? This will clear your saved configuration.",
            "Disconnect",
            "Cancel",
            "",
            true);

        if (!confirmed) return;

        await _settingsService.ClearServerConfigAsync();
        await Shell.Current.GoToAsync($"//{Constants.SetupRoute}");
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        if (CachedTrackCount == 0)
        {
            await ConfirmationHelper.ShowAlertAsync("No Cache", "There are no cached files to clear.");
            return;
        }

        var confirmed = await ConfirmationHelper.ShowConfirmAsync(
            "Clear Cache",
            $"Are you sure you want to clear all cached data? {CachedTrackCount} downloaded files ({CacheSize}) will be removed.",
            "Clear",
            "Cancel",
            "",
            true);

        if (!confirmed) return;

        try
        {
            IsBusy = true;
            await _cacheService.ClearCacheAsync();
            await UpdateCacheInfoAsync();
            await ConfirmationHelper.ShowSuccessAsync("Cache Cleared", "All cached data has been removed.");
        }
        catch (Exception ex)
        {
            await ConfirmationHelper.ShowAlertAsync("Error", $"Failed to clear cache: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenServerSetupAsync()
    {
        await Shell.Current.GoToAsync(Constants.SetupRoute);
    }
}
