using TelegramDownloader.Models;

namespace Test
{
    /// <summary>
    /// The per-type breakdown a channel refresh reports back. The scan itself
    /// needs Telegram, so what is covered here is the tally it builds.
    /// </summary>
    public class ChannelRefreshResultTest
    {
        [Test]
        public void CountsEachMediaTypeInItsOwnBucket()
        {
            ChannelRefreshResult r = new ChannelRefreshResult();
            r.Count(DocumentType.Video);
            r.Count(DocumentType.Video);
            r.Count(DocumentType.Audio);
            r.Count(DocumentType.Photo);
            r.Count(DocumentType.Document);
            r.Count(DocumentType.Document);
            r.Count(DocumentType.Document);

            Assert.Multiple(() =>
            {
                Assert.That(r.Videos, Is.EqualTo(2));
                Assert.That(r.Audios, Is.EqualTo(1));
                Assert.That(r.Photos, Is.EqualTo(1));
                Assert.That(r.Documents, Is.EqualTo(3));
            });
        }

        [Test]
        public void TotalIsTheSumOfTheBuckets()
        {
            ChannelRefreshResult r = new ChannelRefreshResult();
            Assert.That(r.Total, Is.Zero, "a scan that added nothing reports zero, not null");

            r.Count(DocumentType.Video);
            r.Count(DocumentType.Audio);
            Assert.That(r.Total, Is.EqualTo(2));
        }

        [Test]
        public void AnythingThatIsNotPhotoVideoOrAudioCountsAsDocument()
        {
            ChannelRefreshResult r = new ChannelRefreshResult();
            r.Count((DocumentType)999);

            Assert.That(r.Documents, Is.EqualTo(1));
            Assert.That(r.Total, Is.EqualTo(1));
        }
    }
}
