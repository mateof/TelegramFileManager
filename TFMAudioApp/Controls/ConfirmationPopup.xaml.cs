using CommunityToolkit.Maui.Views;

namespace TFMAudioApp.Controls;

public partial class ConfirmationPopup : Popup
{
    public new bool? Result { get; private set; }

    public ConfirmationPopup(
        string title,
        string message,
        string acceptText = "OK",
        string? cancelText = null,
        string icon = "",
        bool isDanger = false)
    {
        InitializeComponent();

        TitleLabel.Text = title;
        MessageLabel.Text = message;
        AcceptButton.Text = acceptText;

        if (!string.IsNullOrEmpty(icon))
        {
            IconLabel.Text = icon;
            IconLabel.IsVisible = true;
        }
        else
        {
            IconLabel.IsVisible = false;
        }

        if (isDanger)
        {
            AcceptButton.BackgroundColor = Color.FromArgb("#FF5252");
        }

        if (string.IsNullOrEmpty(cancelText))
        {
            // Single button mode - hide cancel and make accept full width
            CancelButton.IsVisible = false;
            Grid.SetColumnSpan(AcceptButton, 3);
            Grid.SetColumn(AcceptButton, 0);
        }
        else
        {
            CancelButton.Text = cancelText;
        }
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        Result = false;
        Close(false);
    }

    private void OnAcceptClicked(object? sender, EventArgs e)
    {
        Result = true;
        Close(true);
    }
}

/// <summary>
/// Helper class to show confirmation popups easily
/// </summary>
public static class ConfirmationHelper
{
    /// <summary>
    /// Show a confirmation dialog with Accept/Cancel buttons
    /// </summary>
    public static async Task<bool> ShowConfirmAsync(
        string title,
        string message,
        string acceptText = "Yes",
        string cancelText = "Cancel",
        string icon = "",
        bool isDanger = false)
    {
        var popup = new ConfirmationPopup(title, message, acceptText, cancelText, icon, isDanger);
        var result = await Shell.Current.ShowPopupAsync(popup);
        return result is true;
    }

    /// <summary>
    /// Show an alert dialog with a single OK button
    /// </summary>
    public static async Task ShowAlertAsync(
        string title,
        string message,
        string buttonText = "OK",
        string icon = "")
    {
        var popup = new ConfirmationPopup(title, message, buttonText, null, icon);
        await Shell.Current.ShowPopupAsync(popup);
    }

    /// <summary>
    /// Show an error dialog
    /// </summary>
    public static async Task ShowErrorAsync(string title, string message)
    {
        await ShowAlertAsync(title, message, "OK", "");
    }

    /// <summary>
    /// Show a success dialog
    /// </summary>
    public static async Task ShowSuccessAsync(string title, string message)
    {
        await ShowAlertAsync(title, message, "OK", "");
    }
}
