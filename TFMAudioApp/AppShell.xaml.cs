using TFMAudioApp.Services.Interfaces;
using TFMAudioApp.Views;

namespace TFMAudioApp;

public partial class AppShell : Shell
{
    private IConnectivityService? _connectivityService;

    public AppShell()
    {
        InitializeComponent();

        // Register routes for pages that are navigated to with parameters
        Routing.RegisterRoute("playlistdetail", typeof(PlaylistDetailPage));
        Routing.RegisterRoute("channeldetail", typeof(ChannelDetailPage));
        Routing.RegisterRoute("folderdetail", typeof(FolderDetailPage));
        Routing.RegisterRoute("player", typeof(PlayerPage));
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler != null)
        {
            InitializeConnectivityService();
        }
    }

    private void InitializeConnectivityService()
    {
        _connectivityService = Application.Current?.Handler?.MauiContext?.Services.GetService<IConnectivityService>();

        if (_connectivityService != null)
        {
            _connectivityService.ConnectivityChanged += OnConnectivityChanged;
            UpdateConnectionUI(_connectivityService.IsConnected);
        }
    }

    private void OnConnectivityChanged(object? sender, bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateConnectionUI(isConnected);
        });
    }

    private void UpdateConnectionUI(bool isConnected)
    {
        if (ConnectionIndicator != null && ConnectionText != null)
        {
            if (isConnected)
            {
                ConnectionIndicator.TextColor = Color.FromArgb("#22C55E"); // Green
                ConnectionText.Text = "Online";
            }
            else
            {
                ConnectionIndicator.TextColor = Color.FromArgb("#EF4444"); // Red
                ConnectionText.Text = "Offline";
            }
        }
    }
}
