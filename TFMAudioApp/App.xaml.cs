using TFMAudioApp.Helpers;
using TFMAudioApp.Services.Interfaces;
using TFMAudioApp.Views;

namespace TFMAudioApp;

public partial class App : Application
{
    private readonly ISettingsService _settingsService;
    private readonly IAudioPlayerService _audioPlayerService;

    public App(ISettingsService settingsService, IAudioPlayerService audioPlayerService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _audioPlayerService = audioPlayerService;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());

        // Handle window closing (app terminated)
        window.Destroying += OnWindowDestroying;

        return window;
    }

    protected override async void OnStart()
    {
        base.OnStart();
        await NavigateToStartPageAsync();
    }

    private void OnWindowDestroying(object? sender, EventArgs e)
    {
        // Stop playback when app window is destroyed
        System.Diagnostics.Debug.WriteLine("[App] Window destroying - stopping audio player");
        try
        {
            _audioPlayerService.StopAsync().ConfigureAwait(false);

            // Dispose the player to clean up resources
            if (_audioPlayerService is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Error stopping player on destroy: {ex.Message}");
        }
    }

    private async Task NavigateToStartPageAsync()
    {
        var config = await _settingsService.GetServerConfigAsync();

        if (config == null || !config.IsValid)
        {
            await Shell.Current.GoToAsync($"//{Constants.SetupRoute}");
        }
        else
        {
            await Shell.Current.GoToAsync($"//{Constants.HomeRoute}");
        }
    }
}
