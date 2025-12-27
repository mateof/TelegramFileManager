namespace TFMAudioApp.Services.Interfaces;

/// <summary>
/// Service for monitoring network connectivity
/// </summary>
public interface IConnectivityService
{
    /// <summary>
    /// Current connectivity status
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Event raised when connectivity changes
    /// </summary>
    event EventHandler<bool> ConnectivityChanged;

    /// <summary>
    /// Check if server is reachable
    /// </summary>
    Task<bool> IsServerReachableAsync();
}
