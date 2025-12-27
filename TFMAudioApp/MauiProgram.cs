using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using TFMAudioApp.Data;
using TFMAudioApp.Services;
using TFMAudioApp.Services.Interfaces;
using TFMAudioApp.ViewModels;
using TFMAudioApp.Views;

namespace TFMAudioApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register Database
        builder.Services.AddSingleton<LocalDatabase>();

        // Register Platform Services
        builder.Services.AddSingleton<IConnectivity>(Connectivity.Current);

        // Register App Services
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<ApiServiceFactory>();
        builder.Services.AddSingleton<IConnectivityService, ConnectivityService>();
        builder.Services.AddSingleton<ICacheService, CacheService>();
        builder.Services.AddSingleton<IDownloadService, DownloadService>();
        builder.Services.AddSingleton<IAudioPlayerService, AudioPlayerService>();

        // Register Media Notification Service (platform-specific)
#if ANDROID
        builder.Services.AddSingleton<IMediaNotificationService, TFMAudioApp.Platforms.Android.Services.MediaNotificationService>();
        builder.Services.AddSingleton<IDownloadNotificationService, TFMAudioApp.Platforms.Android.Services.DownloadNotificationService>();
#else
        builder.Services.AddSingleton<IMediaNotificationService, MediaNotificationService>();
        builder.Services.AddSingleton<IDownloadNotificationService, DefaultDownloadNotificationService>();
#endif

        // Register ViewModels
        // Singleton for main tab pages (reused frequently, keeps state)
        builder.Services.AddSingleton<ChannelsViewModel>();
        builder.Services.AddSingleton<PlaylistsViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<DownloadsViewModel>();
        builder.Services.AddSingleton<PlayerViewModel>();
        // Transient for pages that need fresh data each time
        builder.Services.AddTransient<SetupViewModel>();
        builder.Services.AddTransient<ChannelDetailViewModel>();
        builder.Services.AddTransient<PlaylistDetailViewModel>();

        // Register Pages
        // Singleton for main tab pages (faster navigation, XAML parsed once)
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<ChannelsPage>();
        builder.Services.AddSingleton<PlaylistsPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<DownloadsPage>();
        builder.Services.AddSingleton<PlayerPage>();
        // Transient for detail pages with parameters
        builder.Services.AddTransient<SetupPage>();
        builder.Services.AddTransient<ChannelDetailPage>();
        builder.Services.AddTransient<PlaylistDetailPage>();
        builder.Services.AddTransient<FolderDetailPage>();

        // Register App
        builder.Services.AddSingleton<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
