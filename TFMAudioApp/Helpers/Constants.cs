namespace TFMAudioApp.Helpers;

public static class Constants
{
    // API Headers
    public const string ApiKeyHeader = "X-Api-Key";

    // Preferences Keys
    public const string PrefServerHost = "server_host";
    public const string PrefServerPort = "server_port";
    public const string PrefApiKey = "api_key";
    public const string PrefUseHttps = "use_https";
    public const string PrefIsConfigured = "is_configured";
    public const string PrefVolume = "player_volume";
    public const string PrefRepeatMode = "repeat_mode";
    public const string PrefShuffleEnabled = "shuffle_enabled";

    // Default values
    public const int DefaultPort = 5000;
    public const double DefaultVolume = 0.8;

    // File categories - all supported audio formats
    public static readonly string[] AudioExtensions = {
        ".mp3", ".ogg", ".flac", ".aac", ".wav", ".m4a", ".wma", ".opus",
        ".ape", ".alac", ".dsd", ".dsf", ".dff", ".mpc", ".mka", ".tak",
        ".tta", ".wv", ".aiff", ".aif", ".au", ".ra", ".rm", ".mid", ".midi"
    };
    public static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v" };
    public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg" };

    // Cache settings
    public const string CacheFolder = "AudioCache";
    public const long MaxCacheSizeBytes = 5L * 1024 * 1024 * 1024; // 5 GB default

    // Routes
    public const string SetupRoute = "//setup";
    public const string HomeRoute = "//home";
    public const string ChannelsRoute = "//channels";
    public const string PlaylistsRoute = "//playlists";
    public const string DownloadsRoute = "//downloads";
    public const string SettingsRoute = "//settings";
    public const string PlayerRoute = "player";
    public const string ChannelDetailRoute = "channeldetail";
    public const string PlaylistDetailRoute = "playlistdetail";
}
