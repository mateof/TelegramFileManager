using TFMAudioApp.Data;
using TFMAudioApp.Helpers;
using TFMAudioApp.Models;
using TFMAudioApp.Services.Interfaces;

namespace TFMAudioApp.Services;

public class SettingsService : ISettingsService
{
    private readonly LocalDatabase _database;

    public SettingsService(LocalDatabase database)
    {
        _database = database;
    }

    public bool IsConfigured
    {
        get => Preferences.Get(Constants.PrefIsConfigured, false);
        private set => Preferences.Set(Constants.PrefIsConfigured, value);
    }

    public double Volume
    {
        get => Preferences.Get(Constants.PrefVolume, Constants.DefaultVolume);
        set => Preferences.Set(Constants.PrefVolume, value);
    }

    public bool ShuffleEnabled
    {
        get => Preferences.Get(Constants.PrefShuffleEnabled, false);
        set => Preferences.Set(Constants.PrefShuffleEnabled, value);
    }

    public int RepeatMode
    {
        get => Preferences.Get(Constants.PrefRepeatMode, 0);
        set => Preferences.Set(Constants.PrefRepeatMode, value);
    }

    public async Task<ServerConfig?> GetServerConfigAsync()
    {
        return await _database.GetServerConfigAsync();
    }

    public async Task SaveServerConfigAsync(ServerConfig config)
    {
        config.LastConnected = DateTime.UtcNow;
        await _database.SaveServerConfigAsync(config);
        IsConfigured = true;
    }

    public async Task ClearServerConfigAsync()
    {
        await _database.ClearServerConfigAsync();
        IsConfigured = false;
    }
}
