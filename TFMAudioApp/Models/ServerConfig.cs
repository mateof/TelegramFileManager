using SQLite;

namespace TFMAudioApp.Models;

/// <summary>
/// Server connection configuration
/// </summary>
[Table("ServerConfig")]
public class ServerConfig
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5000;
    public string ApiKey { get; set; } = string.Empty;
    public bool UseHttps { get; set; }
    public DateTime? LastConnected { get; set; }

    /// <summary>
    /// Full base URL for API calls
    /// </summary>
    [Ignore]
    public string BaseUrl => $"{(UseHttps ? "https" : "http")}://{Host}:{Port}";

    /// <summary>
    /// Check if configuration is valid
    /// </summary>
    [Ignore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Host) &&
                           Port > 0 &&
                           !string.IsNullOrWhiteSpace(ApiKey);
}
