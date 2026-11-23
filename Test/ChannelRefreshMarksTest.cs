using TelegramDownloader.Models;
using TelegramDownloader.Services;

namespace Test
{
    /// <summary>The pure per-filter high-water mark rules.</summary>
    public class ChannelRefreshMarksTest
    {
        private static readonly RefreshFilter[] All = Enum.GetValues<RefreshFilter>();

        [Test]
        public void FiltersWithoutAMarkNeedAFullScan()
        {
            var marks = new Dictionary<RefreshFilter, int> { [RefreshFilter.Audio] = 500, [RefreshFilter.Video] = 0 };

            Assert.That(ChannelRefreshPlanner.FiltersNeedingFullScan(All, marks, force: false),
                Is.EqualTo(new[] { RefreshFilter.Documents, RefreshFilter.Video, RefreshFilter.Photos }));
            Assert.That(ChannelRefreshPlanner.ScanFloor(RefreshFilter.Audio, marks, force: false), Is.EqualTo(500));
            Assert.That(ChannelRefreshPlanner.ScanFloor(RefreshFilter.Photos, marks, force: false), Is.EqualTo(0));
        }

        [Test]
        public void ForceScansEverythingFully()
        {
            var marks = All.ToDictionary(f => f, _ => 100);
            Assert.That(ChannelRefreshPlanner.FiltersNeedingFullScan(All, marks, force: true), Is.EqualTo(All));
            Assert.That(ChannelRefreshPlanner.FiltersNeedingFullScan(All, marks, force: false), Is.Empty);
        }

        [Test]
        public void MarkAdvancesToTheNewestWhenEverythingIsIndexed()
        {
            int mark = ChannelRefreshPlanner.AdvanceMark(10, new[] { 15, 12, 30 }, _ => true);
            Assert.That(mark, Is.EqualTo(30));
        }

        [Test]
        public void MarkNeverPassesAGap()
        {
            var indexed = new HashSet<int> { 12, 15, 30 };
            int mark = ChannelRefreshPlanner.AdvanceMark(10, new[] { 30, 20, 15, 12 }, indexed.Contains);
            Assert.That(mark, Is.EqualTo(15), "20 is missing: everything above it must be scanned again");
        }

        [Test]
        public void MarkStaysWhenTheOldestNewMessageIsMissing()
        {
            int mark = ChannelRefreshPlanner.AdvanceMark(10, new[] { 11, 12 }, id => id != 11);
            Assert.That(mark, Is.EqualTo(10));
        }

        [Test]
        public void NothingScannedKeepsTheFloor()
        {
            Assert.That(ChannelRefreshPlanner.AdvanceMark(42, Array.Empty<int>(), _ => false), Is.EqualTo(42));
            Assert.That(ChannelRefreshPlanner.AdvanceMark(0, Array.Empty<int>(), _ => false), Is.EqualTo(0));
        }

        [Test]
        public void IdsAtOrBelowTheFloorAndDuplicatesAreIgnored()
        {
            // An id at or below the floor cannot hold the mark back nor move it.
            int mark = ChannelRefreshPlanner.AdvanceMark(10, new[] { 3, 10, 11, 11, 12 }, id => id > 10);
            Assert.That(mark, Is.EqualTo(12));
        }

        [Test]
        public void FullScanMarkCanEndBelowAnOldMark()
        {
            // A forced rescan finds message 5 missing from the index: the mark is
            // recomputed from 0 and must reflect it, even though it was 100.
            int mark = ChannelRefreshPlanner.AdvanceMark(0, new[] { 1, 5, 100 }, id => id != 5);
            Assert.That(mark, Is.EqualTo(1));
        }
    }
}
