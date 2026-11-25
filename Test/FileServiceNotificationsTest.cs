using TelegramDownloader.Data;
using TelegramDownloader.Models;
using TelegramDownloader.Services.Api;

namespace Test
{
    public class FileServiceNotificationsTest
    {
        private static ChannelRefreshResult Result(bool canceled = false, bool failed = false) =>
            new ChannelRefreshResult { Photos = 2, Audios = 3, Canceled = canceled, Failed = failed };

        [Test]
        public void RefreshSuccess()
        {
            var n = FileServiceNotifications.RefreshFinished(Result());
            Assert.Multiple(() =>
            {
                Assert.That(n.type, Is.EqualTo(NotificationTypes.Success));
                Assert.That(n.header, Is.EqualTo("Refresh channel files"));
                Assert.That(n.text, Is.EqualTo("Files channel has been refreshed with 5 new files added."));
                Assert.That(n.isForced, Is.True);
            });
        }

        [Test]
        public void RefreshCanceledWinsOverFailed()
        {
            var n = FileServiceNotifications.RefreshFinished(Result(canceled: true, failed: true));
            Assert.That(n.type, Is.EqualTo(NotificationTypes.Warn));
            Assert.That(n.text, Is.EqualTo("Refresh canceled; 5 new files were added before stopping."));
        }

        [Test]
        public void RefreshFailed()
        {
            var n = FileServiceNotifications.RefreshFinished(Result(failed: true));
            Assert.That(n.type, Is.EqualTo(NotificationTypes.Error));
            Assert.That(n.text, Is.EqualTo("Refresh finished with errors; 5 new files were added. Check the logs."));
        }

        [Test]
        public void AlreadyRunningIsAWarning()
        {
            var n = FileServiceNotifications.RefreshAlreadyRunning();
            Assert.That(n.type, Is.EqualTo(NotificationTypes.Warn));
            Assert.That(n.header, Is.EqualTo("Refresh channel files"));
        }

        [Test]
        public void JoinChatMessages()
        {
            Assert.Multiple(() =>
            {
                Assert.That(FileServiceNotifications.JoinedChat().type, Is.EqualTo(NotificationTypes.Success));
                Assert.That(FileServiceNotifications.AlreadyJoinedChat().type, Is.EqualTo(NotificationTypes.Info));
                Assert.That(FileServiceNotifications.JoinChatFailed(new Exception("x")).type, Is.EqualTo(NotificationTypes.Error));
            });
        }

        [Test]
        public void JoinChatErrorIsEncodedForTheToastAndPlainOnTheHub()
        {
            var n = FileServiceNotifications.JoinChatFailed(new Exception("bad <b>hash</b> & co"));
            Assert.That(n.text, Is.EqualTo("Error: bad &lt;b&gt;hash&lt;/b&gt; &amp; co."));
            Assert.That(EventMapping.ToDto(n, DateTime.UtcNow).Message, Is.EqualTo("Error: bad <b>hash</b> & co."));
        }

        [Test]
        public void AllAreForwardedEvenWithNotificationsOff()
        {
            var all = new[]
            {
                FileServiceNotifications.RefreshAlreadyRunning(),
                FileServiceNotifications.RefreshFinished(Result()),
                FileServiceNotifications.JoinedChat(),
                FileServiceNotifications.JoinChatFailed(new Exception("x")),
                FileServiceNotifications.AlreadyJoinedChat()
            };
            foreach (var n in all)
                Assert.That(EventMapping.ShouldForward(n, shouldNotify: false), Is.True, n.text);
        }
    }
}
