using System.Net;
using TelegramDownloader.Models;

namespace TelegramDownloader.Data
{
    /// <summary>
    /// The messages <see cref="FileService"/> shows to the user. They go through
    /// <see cref="NotificationModel"/>, so the Blazor <c>ToastNotify</c> shows them
    /// and <c>/hubs/events</c> forwards them to the web app as <c>Notification</c>.
    /// All are forced: they answer an action of the user, so they are shown even
    /// when the general notifications setting is off (as the old toasts were).
    /// </summary>
    public static class FileServiceNotifications
    {
        public const string RefreshTitle = "Refresh channel files";
        public const string JoinChatTitle = "Join chat";

        public static Notification RefreshAlreadyRunning() =>
            new Notification("This channel is already being refreshed.", RefreshTitle, NotificationTypes.Warn, force: true);

        public static Notification RefreshFinished(ChannelRefreshResult result)
        {
            if (result.Canceled)
                return new Notification($"Refresh canceled; {result.Total} new files were added before stopping.",
                    RefreshTitle, NotificationTypes.Warn, force: true);
            if (result.Failed)
                return new Notification($"Refresh finished with errors; {result.Total} new files were added. Check the logs.",
                    RefreshTitle, NotificationTypes.Error, force: true);
            return new Notification($"Files channel has been refreshed with {result.Total} new files added.",
                RefreshTitle, NotificationTypes.Success, force: true);
        }

        public static Notification JoinedChat() =>
            new Notification("Joined to chat", JoinChatTitle, NotificationTypes.Success, force: true);

        /// <summary>The Blazor toast renders the text as markup, so the exception text is encoded.</summary>
        public static Notification JoinChatFailed(Exception ex) =>
            new Notification($"Error: {WebUtility.HtmlEncode(ex.Message)}.", JoinChatTitle, NotificationTypes.Error, force: true);

        public static Notification AlreadyJoinedChat() =>
            new Notification("You were already joined the chat", JoinChatTitle, NotificationTypes.Info, force: true);
    }
}
