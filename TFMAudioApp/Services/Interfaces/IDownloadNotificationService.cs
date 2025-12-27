namespace TFMAudioApp.Services.Interfaces;

public interface IDownloadNotificationService
{
    void ShowDownloadProgress(string title, int current, int total, double progress);
    void ShowDownloadComplete(string title, int totalDownloaded);
    void ShowDownloadError(string title, string error);
    void CancelNotification();
}

// Default implementation for non-Android platforms
public class DefaultDownloadNotificationService : IDownloadNotificationService
{
    public void ShowDownloadProgress(string title, int current, int total, double progress) { }
    public void ShowDownloadComplete(string title, int totalDownloaded) { }
    public void ShowDownloadError(string title, string error) { }
    public void CancelNotification() { }
}
