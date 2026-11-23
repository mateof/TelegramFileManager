using TelegramDownloader.Models;
using TelegramDownloader.Services;

namespace Test
{
    /// <summary>Live progress bookkeeping and cancellation through the registry.</summary>
    public class ChannelRefreshProgressTest
    {
        [Test]
        public void FilterCountersAccumulatePagesAndKeepTheFirstTotal()
        {
            var p = new ChannelRefreshProgress("1");
            RefreshFilterProgress docs = p.Filter(RefreshFilter.Documents);

            Assert.That(docs.Total, Is.Null);
            docs.ReportPage(100, 250);
            docs.ReportPage(100, 999); // later pages may report another count: the first one wins
            docs.ReportPage(50, null);

            Assert.That(docs.Fetched, Is.EqualTo(250));
            Assert.That(docs.Total, Is.EqualTo(250));
            Assert.That(docs.Done, Is.False);
            docs.MarkDone();
            Assert.That(docs.Done, Is.True);
            Assert.That(p.Filter(RefreshFilter.Documents), Is.SameAs(docs), "a filter is registered once");
        }

        [Test]
        public void ConcurrentPagesAreAllCounted()
        {
            var p = new ChannelRefreshProgress("1");
            Parallel.For(0, 4, i =>
            {
                RefreshFilterProgress f = p.Filter((RefreshFilter)i);
                for (int page = 0; page < 1000; page++)
                    f.ReportPage(1, 1000);
            });
            Parallel.For(0, 1000, _ => p.AddIndexed(2));

            ChannelRefreshProgressSnapshot s = p.Snapshot();
            Assert.That(s.Filters, Has.Count.EqualTo(4));
            Assert.That(s.Filters.Select(f => f.Fetched), Is.All.EqualTo(1000));
            Assert.That(s.Indexed, Is.EqualTo(2000));
        }

        [Test]
        public void SnapshotCopiesTheCurrentState()
        {
            var started = new DateTime(2026, 1, 2, 3, 4, 5);
            var p = new ChannelRefreshProgress("42", started);
            p.Filter(RefreshFilter.Audio).ReportPage(10, 40);
            p.Phase = RefreshPhase.Indexing;
            p.NewFiles = 7;
            p.AddIndexed(3);

            ChannelRefreshProgressSnapshot s = p.Snapshot();
            p.AddIndexed(4); // later changes do not leak into an earlier snapshot

            Assert.That(s.ChannelId, Is.EqualTo("42"));
            Assert.That(s.StartedAt, Is.EqualTo(started));
            Assert.That(s.Phase, Is.EqualTo(RefreshPhase.Indexing));
            Assert.That(s.NewFiles, Is.EqualTo(7));
            Assert.That(s.Indexed, Is.EqualTo(3));
            Assert.That(s.CancelRequested, Is.False);
            Assert.That(s.Filters.Single(), Is.EqualTo(new RefreshFilterProgressSnapshot(RefreshFilter.Audio, 10, 40, false)));
        }

        [Test]
        public void RegistryHandsOutProgressAndCancelsTheRunningRefresh()
        {
            var registry = new ChannelRefreshRegistry();
            Assert.That(registry.TryCancel("1"), Is.False, "nothing to cancel");
            Assert.That(registry.Get("1"), Is.Null);

            Assert.That(registry.TryBegin("1", out ChannelRefreshProgress p), Is.True);
            Assert.That(registry.Get("1"), Is.SameAs(p));
            Assert.That(p.Token.IsCancellationRequested, Is.False);

            Assert.That(registry.TryCancel("1"), Is.True);
            Assert.That(p.Token.IsCancellationRequested, Is.True);
            Assert.That(p.Snapshot().CancelRequested, Is.True);
            // Still claimed until the refresh unwinds, so a new one cannot overlap it.
            Assert.That(registry.IsRunning("1"), Is.True);
            Assert.That(registry.TryBegin("1"), Is.False);
            Assert.That(registry.TryCancel("1"), Is.True, "cancelling twice is harmless");

            registry.End("1", p);
            Assert.That(registry.IsRunning("1"), Is.False);
            Assert.That(registry.TryCancel("1"), Is.False);
        }

        [Test]
        public void EndingWithAStaleProgressDoesNotReleaseANewerRefresh()
        {
            var registry = new ChannelRefreshRegistry();
            registry.TryBegin("1", out ChannelRefreshProgress first);
            registry.End("1");
            registry.TryBegin("1", out ChannelRefreshProgress second);

            registry.End("1", first);

            Assert.That(registry.Get("1"), Is.SameAs(second));
        }

        [Test]
        public void FilterKeysRoundTrip()
        {
            foreach (RefreshFilter f in Enum.GetValues<RefreshFilter>())
            {
                Assert.That(RefreshFilterNames.TryParse(f.ToKey(), out RefreshFilter parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(f));
            }
            Assert.That(RefreshFilterNames.TryParse("nope", out _), Is.False);
            Assert.That(new RefreshChannelOptions { IncludeAudio = false }.SelectedFilters(),
                Is.EqualTo(new[] { RefreshFilter.Documents, RefreshFilter.Video, RefreshFilter.Photos }));
        }
    }
}
