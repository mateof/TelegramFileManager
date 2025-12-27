using Refit;
using TFMAudioApp.Helpers;
using TFMAudioApp.Models;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.Services;

/// <summary>
/// Factory for creating configured API service instances
/// </summary>
public class ApiServiceFactory
{
    private IApiService? _apiService;
    private ServerConfig? _currentConfig;
    private readonly ISettingsService _settingsService;

    public ApiServiceFactory(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<IApiService?> GetApiServiceAsync()
    {
        var config = await _settingsService.GetServerConfigAsync();

        if (config == null || !config.IsValid)
            return null;

        // Return cached service if config hasn't changed
        if (_apiService != null && _currentConfig != null &&
            _currentConfig.BaseUrl == config.BaseUrl &&
            _currentConfig.ApiKey == config.ApiKey)
        {
            return _apiService;
        }

        // Create new service
        _apiService = CreateApiService(config);
        _currentConfig = config;

        return _apiService;
    }

    public IApiService CreateApiService(ServerConfig config)
    {
        var httpClient = new HttpClient(new AuthHeaderHandler(config.ApiKey))
        {
            BaseAddress = new Uri(config.BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        return RestService.For<IApiService>(httpClient, new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            })
        });
    }

    /// <summary>
    /// Test connection to server
    /// </summary>
    public async Task<(bool success, string? error)> TestConnectionAsync(ServerConfig config)
    {
        try
        {
            var api = CreateApiService(config);
            var response = await api.GetAllChannelsAsync();

            if (response.Success)
                return (true, null);

            return (false, response.Error ?? "Unknown error");
        }
        catch (Refit.ApiException ex)
        {
            return (false, $"API Error: {ex.StatusCode} - {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Clear cached service (call when config changes)
    /// </summary>
    public void ClearCache()
    {
        _apiService = null;
        _currentConfig = null;
    }
}

/// <summary>
/// Handler to add API key to all requests
/// </summary>
public class AuthHeaderHandler : DelegatingHandler
{
    private readonly string _apiKey;

    public AuthHeaderHandler(string apiKey)
    {
        _apiKey = apiKey;
        InnerHandler = new HttpClientHandler();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add(Constants.ApiKeyHeader, _apiKey);
        return await base.SendAsync(request, cancellationToken);
    }
}
