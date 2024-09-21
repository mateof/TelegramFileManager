using System.ComponentModel;
using TelegramDownloader.Services;

namespace TelegramDownloader.Models
{
    public class NotificationModel
    {
        public static event EventHandler<Notification> EventNotification;

        public void sendEvent(Notification notification)
        {
            EventNotification?.Invoke(this, notification);
        }

        public void sendMessage(string title, string message, NotificationTypes type = NotificationTypes.Info, bool force = false)
        {
            EventNotification?.Invoke(this, new Notification(message, title, type, force));
        }
    }

    public class Notification: EventArgs
    {
        public string text { get; set; }
        public string header { get; set; }
        public NotificationTypes type { get; set; }
        public bool isForced { get; set; }

        public Notification(string text, string header, NotificationTypes type, bool force = false)
        {
            this.text = text;
            this.header = header;
            this.type = type;
            this.isForced = force;
        }
    }

    public enum NotificationTypes
    {
        [Description("Error")]
        Error,
        [Description("Info")]
        Info,
        [Description("Warn")]
        Warn,
        [Description("Success")]
        Success
    }


    public class GenericNotificationProgressModel
    {
        public event EventHandler<NotificationProgressEvent> EventNotification;

        public void sendMessage(int totalItems, int completedItems)
        {
            EventNotification?.Invoke(this, new NotificationProgressEvent(totalItems, completedItems));
        }
    }

    public class NotificationProgressEvent : EventArgs
    {
        public int TotalItems { get; set; }
        public int CompletedItems { get; set; }
        public int Percent { get; set; } = 0;
        public bool Finished { get; set; } = false;

        public NotificationProgressEvent(int totalItems, int completedItems)
        {
            this.TotalItems = totalItems;
            this.CompletedItems = completedItems;
            this.Percent = completedItems > 0 ? Convert.ToInt32(completedItems * 100 / totalItems) : 0;
            if (totalItems == completedItems)
                Finished = true;
        }
    }
}
