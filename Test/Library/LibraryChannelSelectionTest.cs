using TelegramDownloader.Models;
using TelegramDownloader.Services.Library;

namespace Test.Library
{
    /// <summary>Which indexed channels a scan covers, from the include/exclude settings.</summary>
    public class LibraryChannelSelectionTest
    {
        private static readonly List<string> Indexed = new() { "100", "200", "300", "-400" };

        [Test]
        public void NoListsMeansEveryIndexedChannel()
        {
            var config = new GeneralConfig();
            Assert.That(LibraryScanService.SelectChannels(config, Indexed), Is.EqualTo(Indexed));
        }

        [Test]
        public void IncludeListRestrictsTheScan()
        {
            var config = new GeneralConfig { LibraryIncludedChannels = new List<long> { 200, -400, 999 } };
            Assert.That(LibraryScanService.SelectChannels(config, Indexed), Is.EqualTo(new List<string> { "200", "-400" }),
                "an included channel without index is simply not there");
        }

        [Test]
        public void ExcludeWinsOverInclude()
        {
            var config = new GeneralConfig
            {
                LibraryIncludedChannels = new List<long> { 100, 200 },
                LibraryExcludedChannels = new List<long> { 200 }
            };
            Assert.That(LibraryScanService.SelectChannels(config, Indexed), Is.EqualTo(new List<string> { "100" }));
        }

        [Test]
        public void ExcludeAloneRemovesFromEverything()
        {
            var config = new GeneralConfig { LibraryExcludedChannels = new List<long> { 100 } };
            Assert.That(LibraryScanService.SelectChannels(config, Indexed), Is.EqualTo(new List<string> { "200", "300", "-400" }));
        }

        [Test]
        public void RefreshHookUsesTheSameRule()
        {
            var config = new GeneralConfig { LibraryIncludedChannels = new List<long> { 100 } };
            Assert.Multiple(() =>
            {
                Assert.That(LibraryScanService.IsChannelSelected(config, "100"), Is.True);
                Assert.That(LibraryScanService.IsChannelSelected(config, "200"), Is.False);
                Assert.That(LibraryScanService.IsChannelSelected(config, "not-a-channel"), Is.False);
            });
        }
    }
}
