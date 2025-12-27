using TFMAudioApp.Models;

namespace TFMAudioApp.Views;

[QueryProperty(nameof(FolderData), "folder")]
public partial class FolderDetailPage : ContentPage
{
    private ChannelFolder? _folder;

    public string FolderData
    {
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                try
                {
                    _folder = System.Text.Json.JsonSerializer.Deserialize<ChannelFolder>(
                        Uri.UnescapeDataString(value),
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    UpdateUI();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to deserialize folder: {ex.Message}");
                }
            }
        }
    }

    public FolderDetailPage()
    {
        InitializeComponent();
    }

    private void UpdateUI()
    {
        if (_folder == null) return;

        Title = _folder.Title;
        FolderTitleLabel.Text = _folder.Title;
        FolderIconLabel.Text = string.IsNullOrEmpty(_folder.IconEmoji) ? "📁" : _folder.IconEmoji;
        ChannelCountLabel.Text = $"{_folder.ChannelCount} channels";
        ChannelsCollection.ItemsSource = _folder.Channels;
    }

    private async void OnChannelTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject bindable) return;
        if (bindable.BindingContext is not Channel channel) return;

        await Shell.Current.GoToAsync($"channeldetail?id={channel.Id}&name={Uri.EscapeDataString(channel.Name)}");
    }
}
