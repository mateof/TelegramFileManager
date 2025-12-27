using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.Services;

public class ConnectivityService : IConnectivityService, IDisposable
{
    private readonly IConnectivity _connectivity;
    private readonly ISettingsService _settingsService;
    private bool _isConnected;

    public event EventHandler<bool>? ConnectivityChanged;

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                ConnectivityChanged?.Invoke(this, value);
            }
        }
    }

    public ConnectivityService(IConnectivity connectivity, ISettingsService settingsService)
    {
        _connectivity = connectivity;
        _settingsService = settingsService;
        _isConnected = _connectivity.NetworkAccess == NetworkAccess.Internet;

        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        IsConnected = e.NetworkAccess == NetworkAccess.Internet;
    }

    public async Task<bool> IsServerReachableAsync()
    {
        if (!IsConnected) return false;

        try
        {
            var config = await _settingsService.GetServerConfigAsync();
            if (config == null || !config.IsValid) return false;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync($"{config.BaseUrl}/api-docs");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
    }
}
