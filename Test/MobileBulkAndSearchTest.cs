using TelegramDownloader.Controllers.Mobile;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Mobile;

namespace Test
{
    /// <summary>
    /// Pure pieces behind the mobile bulk-add, global search and capabilities
    /// endpoints. Mongo and Telegram are not involved.
    /// </summary>
    public class MobileBulkAndSearchTest
    {
        private static PlaylistTrackModel T(string fileId, int order = 0) =>
            new PlaylistTrackModel { FileId = fileId, FileName = fileId + ".mp3", Order = order };

        [Test]
        public void SelectNewSkipsExistingAndRepeatedFileIdsKeepingRequestOrder()
        {
            var existing = new List<PlaylistTrackModel> { T("a", 0), T("b", 1) };
            var incoming = new[] { T("c"), T("a"), T("d"), T("c"), T("e"), T("b") };

            var added = PlaylistTrackMerger.SelectNew(existing, incoming, out var skipped);

            Assert.Multiple(() =>
            {
                Assert.That(added.Select(t => t.FileId), Is.EqualTo(new[] { "c", "d", "e" }));
                Assert.That(skipped, Is.EqualTo(3));
            });
        }

        [Test]
        public void SelectNewHandlesEmptyPlaylist()
        {
            var added = PlaylistTrackMerger.SelectNew(null, new[] { T("x"), T("x") }, out var skipped);

            Assert.Multiple(() =>
            {
                Assert.That(added, Has.Count.EqualTo(1));
                Assert.That(skipped, Is.EqualTo(1));
            });
        }

        [Test]
        public void NextOrderContinuesAfterCurrentTracks()
        {
            Assert.Multiple(() =>
            {
                Assert.That(PlaylistTrackMerger.NextOrder(new List<PlaylistTrackModel>()), Is.EqualTo(0));
                Assert.That(PlaylistTrackMerger.NextOrder(new List<PlaylistTrackModel> { T("a", 0), T("b", 1) }), Is.EqualTo(2));
                // Gaps / stale orders never produce a collision
                Assert.That(PlaylistTrackMerger.NextOrder(new List<PlaylistTrackModel> { T("a", 0), T("b", 7) }), Is.EqualTo(8));
            });
        }

        [Test]
        public void AssignOrderNumbersConsecutivelyAndStampsDate()
        {
            var when = new DateTime(2026, 1, 2, 3, 4, 5);
            var tracks = new List<PlaylistTrackModel> { T("a"), T("b"), T("c") };

            PlaylistTrackMerger.AssignOrder(tracks, 5, when);

            Assert.Multiple(() =>
            {
                Assert.That(tracks.Select(t => t.Order), Is.EqualTo(new[] { 5, 6, 7 }));
                Assert.That(tracks.All(t => t.DateAdded == when), Is.True);
            });
        }

        private static ChannelSearchGroupDto G(string id, string name, int files) => new()
        {
            ChannelId = id,
            ChannelName = name,
            Files = Enumerable.Range(0, files).Select(i => new ChannelFileDto { Id = $"{id}-{i}", Name = $"f{i}" }).ToList()
        };

        [Test]
        public void CapGroupsKeepsLimitAcrossChannelsInNameOrder()
        {
            var (groups, truncated) = MobileSearchController.CapGroups(
                new[] { G("2", "Beta", 3), G("1", "alpha", 2), G("3", "Gamma", 4) }, 4);

            Assert.Multiple(() =>
            {
                Assert.That(truncated, Is.True);
                Assert.That(groups.Select(g => g.ChannelId), Is.EqualTo(new[] { "1", "2" }));
                Assert.That(groups.Sum(g => g.Files.Count), Is.EqualTo(4));
            });
        }

        [Test]
        public void CapGroupsUnderLimitIsNotTruncated()
        {
            var (groups, truncated) = MobileSearchController.CapGroups(new[] { G("1", "a", 2), G("2", "b", 2) }, 4);

            Assert.Multiple(() =>
            {
                Assert.That(truncated, Is.False);
                Assert.That(groups, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void SearchLocalFilesMatchesNameFiltersAudioAndCaps()
        {
            var root = Path.Combine(Path.GetTempPath(), "tfm-search-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Album", "CD1"));
                File.WriteAllText(Path.Combine(root, "Album", "CD1", "01 Song One.mp3"), "x");
                File.WriteAllText(Path.Combine(root, "Album", "02 song two.flac"), "x");
                File.WriteAllText(Path.Combine(root, "Album", "song cover.jpg"), "x");
                File.WriteAllText(Path.Combine(root, "other.mp3"), "x");

                var (audio, audioTrunc) = MobileSearchController.SearchLocalFiles(root, "SONG", true, 10, "http://h");
                var (all, _) = MobileSearchController.SearchLocalFiles(root, "song", false, 10, "http://h");
                var (capped, cappedTrunc) = MobileSearchController.SearchLocalFiles(root, "song", false, 2, "http://h");

                Assert.Multiple(() =>
                {
                    Assert.That(audio.Select(f => f.Path), Is.EquivalentTo(new[] { "Album/CD1/01 Song One.mp3", "Album/02 song two.flac" }));
                    Assert.That(audio.All(f => f.Category == "Audio" && f.StreamUrl != null), Is.True);
                    Assert.That(audioTrunc, Is.False);
                    Assert.That(all, Has.Count.EqualTo(3));
                    Assert.That(capped, Has.Count.EqualTo(2));
                    Assert.That(cappedTrunc, Is.True);
                });
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void SearchLocalFilesMissingRootIsEmpty()
        {
            var (files, truncated) = MobileSearchController.SearchLocalFiles(
                Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()), "ab", true, 10, "");
            Assert.Multiple(() =>
            {
                Assert.That(files, Is.Empty);
                Assert.That(truncated, Is.False);
            });
        }

        [Test]
        public void CapabilitiesAdvertiseExactFeatureSet()
        {
            Assert.That(MobileInfoController.Features, Is.EquivalentTo(new[]
            {
                "playlist.bulkAdd", "search.global", "channels.refresh", "channels.refreshProgress", "stream.localTranscode",
                "events.hub", "search.v1", "files.thumbnails", "files.content", "files.preload", "local.copyMove",
                "shares.browse", "setup.write", "transfers.remove", "system.admin", "webAuth.passkeys", "files.deleteOperations"
            }));
        }
    }
}
