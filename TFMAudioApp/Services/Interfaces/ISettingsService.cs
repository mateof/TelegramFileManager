using TFMAudioApp.Models;

namespace TFMAudioApp.Services.Interfaces;

/// <summary>
/// Service for managing application settings
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Check if the app has been configured with server settings
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Get the current server configuration
    /// </summary>
    Task<ServerConfig?> GetServerConfigAsync();

    /// <summary>
    /// Save server configuration
    /// </summary>
    Task SaveServerConfigAsync(ServerConfig config);

    /// <summary>
    /// Clear server configuration
    /// </summary>
    Task ClearServerConfigAsync();

    /// <summary>
    /// Get player volume (0.0 - 1.0)
    /// </summary>
    double Volume { get; set; }

    /// <summary>
    /// Get/set shuffle mode
    /// </summary>
    bool ShuffleEnabled { get; set; }

    /// <summary>
    /// Get/set repeat mode (0 = none, 1 = all, 2 = one)
    /// </summary>
    int RepeatMode { get; set; }
}
