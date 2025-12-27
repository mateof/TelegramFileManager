using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TFMAudioApp.Helpers;
using TFMAudioApp.Models;
using TFMAudioApp.Services;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.ViewModels;

public partial class SetupViewModel : BaseViewModel
{
    private readonly ISettingsService _settingsService;
    private readonly ApiServiceFactory _apiFactory;

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private string _port = "5000";

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private bool _useHttps;

    [ObservableProperty]
    private string _connectionStatus = "Not configured";

    [ObservableProperty]
    private bool _isConnectionTested;

    [ObservableProperty]
    private bool _canConnect;

    public SetupViewModel(ISettingsService settingsService, ApiServiceFactory apiFactory)
    {
        _settingsService = settingsService;
        _apiFactory = apiFactory;
        Title = "Server Setup";

        // Load existing config if any
        LoadExistingConfigAsync().ConfigureAwait(false);
    }

    private async Task LoadExistingConfigAsync()
    {
        var config = await _settingsService.GetServerConfigAsync();
        if (config != null)
        {
            Host = config.Host;
            Port = config.Port.ToString();
            ApiKey = config.ApiKey;
            UseHttps = config.UseHttps;
        }
    }

    partial void OnHostChanged(string value) => ValidateForm();
    partial void OnPortChanged(string value) => ValidateForm();
    partial void OnApiKeyChanged(string value) => ValidateForm();

    private void ValidateForm()
    {
        CanConnect = !string.IsNullOrWhiteSpace(Host) &&
                     !string.IsNullOrWhiteSpace(Port) &&
                     int.TryParse(Port, out var portNum) && portNum > 0 &&
                     !string.IsNullOrWhiteSpace(ApiKey);

        IsConnectionTested = false;
        ConnectionStatus = CanConnect ? "Ready to test" : "Fill all fields";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!CanConnect) return;

        await ExecuteAsync(async () =>
        {
            ConnectionStatus = "Testing connection...";

            var config = CreateConfig();
            var (success, error) = await _apiFactory.TestConnectionAsync(config);

            if (success)
            {
                ConnectionStatus = "Connection successful!";
                IsConnectionTested = true;
            }
            else
            {
                ConnectionStatus = $"Failed: {error}";
                IsConnectionTested = false;
            }
        }, "Connection test failed");
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (!IsConnectionTested)
        {
            await TestConnectionAsync();
            if (!IsConnectionTested) return;
        }

        await ExecuteAsync(async () =>
        {
            var config = CreateConfig();
            await _settingsService.SaveServerConfigAsync(config);
            _apiFactory.ClearCache();

            // Navigate to main app
            await Shell.Current.GoToAsync(Constants.HomeRoute);
        }, "Failed to save configuration");
    }

    private ServerConfig CreateConfig()
    {
        return new ServerConfig
        {
            Host = Host.Trim(),
            Port = int.Parse(Port),
            ApiKey = ApiKey.Trim(),
            UseHttps = UseHttps
        };
    }
}
