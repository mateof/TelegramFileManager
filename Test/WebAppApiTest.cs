using TelegramDownloader.Controllers.Api.V1;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;

namespace Test
{
    public class EventMappingTest
    {
        [TestCase(NotificationTypes.Error, "error")]
        [TestCase(NotificationTypes.Warn, "warning")]
        [TestCase(NotificationTypes.Success, "success")]
        [TestCase(NotificationTypes.Info, "info")]
        public void MapsNotificationLevels(NotificationTypes type, string expected)
        {
            Assert.That(EventMapping.LevelOf(type), Is.EqualTo(expected));
        }

        [Test]
        public void NotificationBecomesPlainTextDto()
        {
            var at = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);
            var dto = EventMapping.ToDto(new Notification("Upload <b>song.mp3</b> &amp; more<br/>done", "Upload", NotificationTypes.Success), at);
            Assert.Multiple(() =>
            {
                Assert.That(dto.Level, Is.EqualTo("success"));
                Assert.That(dto.Title, Is.EqualTo("Upload"));
                Assert.That(dto.Message, Is.EqualTo("Upload song.mp3 & more done"));
                Assert.That(dto.At, Is.EqualTo(at));
            });
        }

        [Test]
        public void EmptyTitleIsNull()
        {
            var dto = EventMapping.ToDto(new Notification("x", "  ", NotificationTypes.Info), DateTime.UtcNow);
            Assert.That(dto.Title, Is.Null);
        }

        [Test]
        public void ForwardsLikeTheWebToasts()
        {
            var normal = new Notification("a", "b", NotificationTypes.Info);
            var forced = new Notification("a", "b", NotificationTypes.Info, force: true);
            Assert.Multiple(() =>
            {
                Assert.That(EventMapping.ShouldForward(normal, shouldNotify: true), Is.True);
                Assert.That(EventMapping.ShouldForward(normal, shouldNotify: false), Is.False);
                Assert.That(EventMapping.ShouldForward(forced, shouldNotify: false), Is.True);
            });
        }

        [Test]
        public void ImportProgressPercentAndDone()
        {
            var mid = EventMapping.ToImportProgress("id", ImportKinds.Share, new NotificationProgressEvent(400, 100));
            var end = EventMapping.ToImportProgress("id", ImportKinds.Share, new NotificationProgressEvent(400, 400));
            var empty = EventMapping.ToImportProgress("id", ImportKinds.Index, new NotificationProgressEvent(0, 0));
            Assert.Multiple(() =>
            {
                Assert.That(mid.Percent, Is.EqualTo(25));
                Assert.That(mid.Done, Is.False);
                Assert.That(mid.Message, Is.EqualTo("100/400 items"));
                Assert.That(mid.Kind, Is.EqualTo("share"));
                Assert.That(end.Percent, Is.EqualTo(100));
                Assert.That(end.Done, Is.True);
                Assert.That(empty.Done, Is.True);
                Assert.That(empty.Percent, Is.EqualTo(100));
            });
        }
    }

    public class KeyedThrottleTest
    {
        [Test]
        public void LetsOneMessagePerIntervalPerKey()
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var throttle = new KeyedThrottle(TimeSpan.FromSeconds(1), () => now);

            Assert.That(throttle.ShouldSend("a"), Is.True);
            Assert.That(throttle.ShouldSend("a"), Is.False);
            Assert.That(throttle.ShouldSend("b"), Is.True, "keys are independent");

            now = now.AddMilliseconds(999);
            Assert.That(throttle.ShouldSend("a"), Is.False);
            now = now.AddMilliseconds(1);
            Assert.That(throttle.ShouldSend("a"), Is.True);
        }

        [Test]
        public void FinalMessageAlwaysPassesAndReleasesTheKey()
        {
            var now = DateTime.UtcNow;
            var throttle = new KeyedThrottle(TimeSpan.FromSeconds(10), () => now);
            throttle.ShouldSend("a");
            Assert.That(throttle.ShouldSend("a", final: true), Is.True);
            Assert.That(throttle.Count, Is.EqualTo(0));
            Assert.That(throttle.ShouldSend("a"), Is.True);
        }
    }

    public class SubscriberRegistryTest
    {
        [Test]
        public void ReportsFirstAndLastSubscriber()
        {
            var registry = new SubscriberRegistry();
            Assert.Multiple(() =>
            {
                Assert.That(registry.Add("c1"), Is.True, "first");
                Assert.That(registry.Add("c2"), Is.False);
                Assert.That(registry.Add("c2"), Is.False, "duplicate");
                Assert.That(registry.Remove("c1"), Is.False);
                Assert.That(registry.Remove("unknown"), Is.False);
                Assert.That(registry.Remove("c2"), Is.True, "last");
                Assert.That(registry.Count, Is.EqualTo(0));
            });
        }

        [Test]
        public void ChangeDetectorReportsOnlyChanges()
        {
            var detector = new ChangeDetector<bool>();
            Assert.Multiple(() =>
            {
                Assert.That(detector.Update(false), Is.True, "first value");
                Assert.That(detector.Update(false), Is.False);
                Assert.That(detector.Update(true), Is.True);
            });
        }
    }

    public class ImportProgressTrackerTest
    {
        [Test]
        public void ForwardsTicksAndEndsOnce()
        {
            var tracker = new ImportProgressTracker();
            var seen = new List<ImportProgressEventDto>();
            tracker.Progress += seen.Add;

            var (id, model) = tracker.Begin(ImportKinds.Index);
            model.sendMessage(2, 1);
            model.sendMessage(2, 2);
            tracker.Complete(id); // already done by the last tick: ignored

            Assert.Multiple(() =>
            {
                Assert.That(seen.Select(s => s.Percent), Is.EqualTo(new[] { 0, 50, 100 }));
                Assert.That(seen.Count(s => s.Done), Is.EqualTo(1));
                Assert.That(seen.All(s => s.Id == id && s.Kind == "index"), Is.True);
                Assert.That(tracker.ActiveIds, Is.Empty);
            });
        }

        [Test]
        public void FailureSendsTheError()
        {
            var tracker = new ImportProgressTracker();
            ImportProgressEventDto? last = null;
            tracker.Progress += e => last = e;
            var (id, _) = tracker.Begin(ImportKinds.Share);
            tracker.Fail(id, "boom");
            Assert.That(last!.Done, Is.True);
            Assert.That(last.Error, Is.EqualTo("boom"));
            Assert.That(last.Kind, Is.EqualTo("share"));
        }
    }

    public class HttpRangeParserTest
    {
        [TestCase(null, RangeKind.None, 0, 999)]
        [TestCase("bytes=0-99", RangeKind.Partial, 0, 99)]
        [TestCase("bytes=900-", RangeKind.Partial, 900, 999)]
        [TestCase("bytes=900-5000", RangeKind.Partial, 900, 999)]
        [TestCase("bytes=-100", RangeKind.Partial, 900, 999)]
        [TestCase("bytes=-5000", RangeKind.Partial, 0, 999)]
        [TestCase("bytes=1000-", RangeKind.Unsatisfiable, 0, 999)]
        [TestCase("bytes=0-1,5-6", RangeKind.None, 0, 999)]
        [TestCase("bytes=50-10", RangeKind.None, 0, 999)]
        [TestCase("items=0-10", RangeKind.None, 0, 999)]
        [TestCase("bytes=abc", RangeKind.None, 0, 999)]
        public void Parses(string? header, RangeKind kind, long from, long to)
        {
            var result = HttpRangeParser.Parse(header, 1000, out var range);
            Assert.That(result, Is.EqualTo(kind));
            if (kind != RangeKind.Unsatisfiable)
                Assert.That(range, Is.EqualTo(new ByteRange(from, to)));
        }

        [Test]
        public void LengthIsInclusive() => Assert.That(new ByteRange(10, 19).Length, Is.EqualTo(10));
    }

    public class SplitFileLayoutTest
    {
        [Test]
        public void LocatesTheRightPart()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SplitFileLayout.Locate(0, 100, 3), Is.EqualTo((0, 0L)));
                Assert.That(SplitFileLayout.Locate(99, 100, 3), Is.EqualTo((0, 0L)));
                Assert.That(SplitFileLayout.Locate(100, 100, 3), Is.EqualTo((1, 100L)));
                Assert.That(SplitFileLayout.Locate(250, 100, 3), Is.EqualTo((2, 200L)));
                Assert.That(SplitFileLayout.Locate(999, 100, 3), Is.EqualTo((2, 200L)), "last part may be longer than reported");
                Assert.That(SplitFileLayout.Locate(5000, 100, 1), Is.EqualTo((0, 0L)));
            });
        }

        [Test]
        public void AlignsToTelegramBlocks()
        {
            Assert.That(SplitFileLayout.Align(0), Is.EqualTo((0L, 0L)));
            Assert.That(SplitFileLayout.Align(524288 + 10), Is.EqualTo((524288L, 10L)));
            Assert.That(SplitFileLayout.Align(524287), Is.EqualTo((0L, 524287L)));
        }
    }

    public class MediaUrlBuilderTest
    {
        private const string Base = "https://tfm.example";

        private static ApiFileDto File(string name, string category, int? messageId = 5, bool split = false) => new ApiFileDto
        {
            Id = "abc",
            Name = name,
            IsFile = true,
            Category = category,
            MessageId = messageId,
            IsSplit = split
        };

        [Test]
        public void BuildsContentUrls()
        {
            Assert.Multiple(() =>
            {
                Assert.That(MediaUrlBuilder.Content(Base, "-100", "abc"), Is.EqualTo("https://tfm.example/api/v1/channels/-100/files/abc/content"));
                Assert.That(MediaUrlBuilder.Content(Base, "-100", "abc", attachment: true), Is.EqualTo("https://tfm.example/api/v1/channels/-100/files/abc/content?disposition=attachment"));
                Assert.That(MediaUrlBuilder.Content(Base, "1", "abc", true, "c 1"), Is.EqualTo("https://tfm.example/api/v1/channels/1/files/abc/content?disposition=attachment&collectionId=c%201"));
                Assert.That(MediaUrlBuilder.Thumbnail(Base, "1", 42), Is.EqualTo("https://tfm.example/api/v1/channels/1/messages/42/thumbnail"));
                Assert.That(MediaUrlBuilder.LocalContent(Base, "music\\a b.mp3", attachment: false), Is.EqualTo("https://tfm.example/api/v1/local/download?path=music%2Fa%20b.mp3&disposition=inline"));
            });
        }

        [Test]
        public void VideoNeedingTranscodeUsesStream()
        {
            var mkv = File("movie.mkv", "Video");
            var mp4 = File("movie.mp4", "Video");
            MediaUrlBuilder.Apply(mkv, Base, "1");
            MediaUrlBuilder.Apply(mp4, Base, "1");
            Assert.Multiple(() =>
            {
                Assert.That(mkv.PlaybackUrl, Is.EqualTo("https://tfm.example/api/v1/channels/1/files/abc/stream"));
                Assert.That(mp4.PlaybackUrl, Is.EqualTo(mp4.ContentUrl));
                Assert.That(mkv.ThumbnailUrl, Is.EqualTo("https://tfm.example/api/v1/channels/1/messages/5/thumbnail"));
            });
        }

        [Test]
        public void ThumbnailsOnlyForSingleMessagePhotosAndVideos()
        {
            var photo = File("a.jpg", "Photo");
            var audio = File("a.mp3", "Audio");
            var split = File("a.mp4", "Video", split: true);
            var noMessage = File("b.jpg", "Photo", messageId: null);
            foreach (var f in new[] { photo, audio, split, noMessage }) MediaUrlBuilder.Apply(f, Base, "1");
            Assert.Multiple(() =>
            {
                Assert.That(photo.ThumbnailUrl, Is.Not.Null);
                Assert.That(photo.PlaybackUrl, Is.Null);
                Assert.That(audio.ThumbnailUrl, Is.Null);
                Assert.That(audio.PlaybackUrl, Is.EqualTo(audio.ContentUrl));
                Assert.That(split.ThumbnailUrl, Is.Null);
                Assert.That(noMessage.ThumbnailUrl, Is.Null);
            });
        }

        [Test]
        public void SharedCollectionsNeverTranscodeAndCarryTheCollection()
        {
            var mkv = File("movie.mkv", "Video");
            MediaUrlBuilder.Apply(mkv, Base, "1", "col");
            Assert.That(mkv.PlaybackUrl, Is.EqualTo("https://tfm.example/api/v1/channels/1/files/abc/content?collectionId=col"));
        }

        [Test]
        public void FoldersGetNoUrls()
        {
            var folder = new ApiFileDto { Id = "f", IsFile = false, Category = "Folder" };
            MediaUrlBuilder.Apply(folder, Base, "1");
            Assert.That(folder.ContentUrl, Is.Null);
        }
    }

    public class LocalPathGuardTest
    {
        private string _root = null!;

        [SetUp]
        public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "tfm-guard-" + Guid.NewGuid().ToString("N"), "local");

        [TestCase("", "")]
        [TestCase("/", "")]
        [TestCase("music/rock", "music/rock")]
        [TestCase("\\music\\rock\\", "music/rock")]
        [TestCase("music/../video", "video")]
        public void AcceptsPathsInsideTheRoot(string input, string relative)
        {
            Assert.That(LocalPathGuard.TryResolve(_root, input, out var absolute, out var rel, out var error), Is.True, error);
            Assert.That(rel, Is.EqualTo(relative));
            Assert.That(LocalPathGuard.IsSameOrInside(_root, absolute), Is.True);
        }

        [TestCase("../outside")]
        [TestCase("music/../../outside")]
        [TestCase("../local2/x")]
        public void RejectsEscapes(string input)
        {
            Assert.That(LocalPathGuard.TryResolve(_root, input, out _, out _, out var error), Is.False);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void RejectsAbsolutePaths()
        {
            var absolute = OperatingSystem.IsWindows() ? "C:\\Windows" : "/etc";
            // A leading slash is treated as "relative to the root", so use a drive on Windows.
            if (OperatingSystem.IsWindows())
                Assert.That(LocalPathGuard.TryResolve(_root, absolute, out _, out _, out _), Is.False);
            else
                Assert.That(LocalPathGuard.TryResolve(_root, absolute, out var abs, out _, out _) && LocalPathGuard.IsSameOrInside(_root, abs), Is.True);
        }

        [Test]
        public void SiblingWithSamePrefixIsNotInside()
        {
            Assert.That(LocalPathGuard.IsSameOrInside(_root, _root + "2"), Is.False);
            Assert.That(LocalPathGuard.IsSameOrInside(_root, Path.Combine(_root, "a")), Is.True);
        }

        [Test]
        public void CopyMoveRefusesSelfAndDescendants()
        {
            var folder = Path.Combine(_root, "a");
            Assert.Multiple(() =>
            {
                Assert.That(LocalCopyMovePlanner.CanPlace(folder, folder, true), Is.False);
                Assert.That(LocalCopyMovePlanner.CanPlace(folder, Path.Combine(folder, "b", "a"), true), Is.False);
                Assert.That(LocalCopyMovePlanner.CanPlace(folder, Path.Combine(_root, "b", "a"), true), Is.True);
                Assert.That(LocalCopyMovePlanner.CanPlace(Path.Combine(_root, "f.txt"), Path.Combine(_root, "b", "f.txt"), false), Is.True);
            });
        }
    }

    public class SpaHostingTest
    {
        [TestCase("/app/assets/index-AbC123.js", SpaCachePolicy.Immutable)]
        [TestCase("/app/assets/fonts/inter.woff2", SpaCachePolicy.Immutable)]
        [TestCase("/app/index.html", SpaCachePolicy.NoCache)]
        [TestCase("/app/", SpaCachePolicy.NoCache)]
        [TestCase("/app/files/123", SpaCachePolicy.NoCache)]
        [TestCase("/app/sw.js", SpaCachePolicy.NoCache)]
        [TestCase("/app/manifest.webmanifest", SpaCachePolicy.NoCache)]
        [TestCase("/app/favicon.svg", SpaCachePolicy.Short)]
        public void CachePolicy(string path, string expected) => Assert.That(SpaCachePolicy.For(path), Is.EqualTo(expected));
    }

    public class MiscWebAppApiTest
    {
        private sealed class Size
        {
            public Size(string type) => Type = type;
            public string Type { get; }
        }

        [Test]
        public void PicksAMediumThumbnail()
        {
            var sizes = new[] { new Size("i"), new Size("s"), new Size("x"), new Size("m"), new Size("y") };
            Assert.That(ThumbnailSizePicker.Pick(sizes, s => s.Type)!.Type, Is.EqualTo("m"));
            Assert.That(ThumbnailSizePicker.Pick(new[] { new Size("i"), new Size("j") }, s => s.Type), Is.Null);
            Assert.That(ThumbnailSizePicker.Pick(new[] { new Size("i"), new Size("z") }, s => s.Type)!.Type, Is.EqualTo("z"));
            Assert.That(ThumbnailSizePicker.Pick<Size>(null, s => s.Type), Is.Null);
        }

        [TestCase("ffmpeg version 6.1.1-3ubuntu5 Copyright (c) 2000-2023 the FFmpeg developers", "6.1.1-3ubuntu5")]
        [TestCase("ffmpeg version n7.0", "n7.0")]
        [TestCase("something else", null)]
        [TestCase(null, null)]
        public void ParsesFfmpegVersion(string? line, string? expected) =>
            Assert.That(FfmpegProbe.ParseVersion(line), Is.EqualTo(expected));

        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase("short", "****")]
        [TestCase("a-long-api-key-1234", "****1234")]
        public void MasksSecrets(string? secret, string? expected) =>
            Assert.That(AppConfigDto.MaskSecret(secret), Is.EqualTo(expected));

        [Test]
        public void OnlyRunningTransfersCannotBeRemoved()
        {
            foreach (var state in Enum.GetValues<StateTask>())
                Assert.That(TransferSnapshotBuilder.CanRemove(state), Is.EqualTo(state != StateTask.Working), state.ToString());
        }

        [TestCase(null, true, null)]
        [TestCase("all", true, null)]
        [TestCase("Audio", true, "Audio")]
        [TestCase("images", true, "Photo")]
        [TestCase("documents", true, "Document")]
        [TestCase("bogus", false, null)]
        public void ParsesSearchFilter(string? filter, bool ok, string? category)
        {
            Assert.That(SearchController.TryParseFilter(filter, out var parsed), Is.EqualTo(ok));
            Assert.That(parsed, Is.EqualTo(category));
        }

        [Test]
        public void CapsSearchGroupsAcrossChannels()
        {
            ApiSearchChannelGroupDto Group(string name, int files) => new ApiSearchChannelGroupDto
            {
                ChannelId = name,
                ChannelName = name,
                Files = Enumerable.Range(0, files).Select(i => new ApiFileDto { Id = name + i }).ToList()
            };

            var (groups, truncated) = SearchController.CapGroups(new[] { Group("b", 3), Group("a", 2), Group("c", 1) }, 4);
            Assert.Multiple(() =>
            {
                Assert.That(groups.Select(g => g.ChannelName), Is.EqualTo(new[] { "a", "b" }));
                Assert.That(groups.Sum(g => g.Files.Count), Is.EqualTo(4));
                Assert.That(truncated, Is.True);
            });
        }

        [Test]
        public void RegistryListsRunningRefreshes()
        {
            var registry = new ChannelRefreshRegistry();
            registry.TryBegin("b");
            registry.TryBegin("a");
            Assert.That(registry.Snapshots().Select(s => s.ChannelId), Is.EqualTo(new[] { "a", "b" }));
            registry.End("a");
            Assert.That(registry.Snapshots().Select(s => s.ChannelId), Is.EqualTo(new[] { "b" }));
        }
    }
}
