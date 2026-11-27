using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Controllers.Api.V1;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services.Audio;

namespace Test
{
    /// <summary>
    /// Parameter rules of the audio transcoding endpoints (mobile and v1) and
    /// the answers the v1 endpoints give before FFmpeg or Telegram are involved.
    /// </summary>
    public class AudioTranscodeTest
    {
        private sealed class FakeTranscoder : AudioTranscodeService
        {
            private readonly bool _available;
            public FakeTranscoder(bool available) : base(null!, NullLogger<AudioTranscodeService>.Instance) => _available = available;
            public override bool IsAvailable => _available;
        }

        [TestCase(null, null, "mp3", 192)]
        [TestCase("", null, "mp3", 192)]
        [TestCase("MP3", 128, "mp3", 128)]
        [TestCase(" aac ", 256, "aac", 256)]
        [TestCase("aac", 10, "aac", 64)]
        [TestCase("mp3", 999, "mp3", 320)]
        public void NormalizeAcceptsKnownFormatsAndClampsTheBitrate(string? format, int? bitrate, string expectedFormat, int expectedBitrate)
        {
            Assert.That(AudioTranscodeOptions.TryNormalize(format, bitrate, out var f, out var b, out var error), Is.True, error);
            Assert.That((f, b), Is.EqualTo((expectedFormat, expectedBitrate)));
        }

        [TestCase("ogg")]
        [TestCase("flac")]
        [TestCase("m4a")]
        public void NormalizeRefusesOtherFormats(string format)
        {
            Assert.That(AudioTranscodeOptions.TryNormalize(format, 192, out _, out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo("Unsupported format. Use mp3 or aac."));
        }

        [Test]
        public void TargetNamesAndTypes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AudioTranscodeOptions.ChannelTargetName("-100-42-Song.flac", "mp3", 192), Is.EqualTo("-100-42-Song-mp3192.mp3"));
                Assert.That(AudioTranscodeOptions.ChannelTargetName("-100-42-Song.flac", "aac", 128), Is.EqualTo("-100-42-Song-aac128.m4a"));
                Assert.That(AudioTranscodeOptions.MimeTypeFor("mp3"), Is.EqualTo("audio/mpeg"));
                Assert.That(AudioTranscodeOptions.MimeTypeFor("aac"), Is.EqualTo("audio/mp4"));
                Assert.That(AudioTranscodeOptions.DownloadName("Song.flac", "aac"), Is.EqualTo("Song.m4a"));
            });
        }

        [Test]
        public void LocalTargetNameChangesWithTheFile()
        {
            var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var a = AudioTranscodeOptions.LocalTargetName("/data/local/a.flac", 100, t, "mp3", 192);
            Assert.Multiple(() =>
            {
                Assert.That(a, Does.Match("^local-[0-9A-F]{16}-mp3192\\.mp3$"));
                Assert.That(AudioTranscodeOptions.LocalTargetName("/data/local/A.FLAC", 100, t, "mp3", 192), Is.EqualTo(a));
                Assert.That(AudioTranscodeOptions.LocalTargetName("/data/local/a.flac", 101, t, "mp3", 192), Is.Not.EqualTo(a));
                Assert.That(AudioTranscodeOptions.LocalTargetName("/data/local/a.flac", 100, t.AddSeconds(1), "mp3", 192), Is.Not.EqualTo(a));
            });
        }

        [Test]
        public void InfoListsFormatsOnlyWithFfmpeg()
        {
            var on = ApiAudioTranscodeInfoDto.Create(true);
            var off = ApiAudioTranscodeInfoDto.Create(false);
            Assert.Multiple(() =>
            {
                Assert.That(on.Formats, Is.EqualTo(new[] { "mp3", "aac" }));
                Assert.That(off.Formats, Is.Empty);
                Assert.That(off.FfmpegAvailable, Is.False);
                Assert.That((on.DefaultFormat, on.DefaultBitrate, on.MinBitrate, on.MaxBitrate), Is.EqualTo(("mp3", 192, 64, 320)));
                Assert.That(on.Bitrates, Does.Contain(192));
            });
        }

        private static int? StatusOf(IActionResult result) => result switch
        {
            ObjectResult o => o.StatusCode,
            StatusCodeResult s => s.StatusCode,
            _ => null
        };

        private static string? CodeOf(IActionResult result) => (result as ObjectResult)?.Value is ApiResult<object> r ? r.Error?.Code : null;

        private static LocalFilesController LocalController() => new(NullLogger<LocalFilesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        private static MediaController Media() => new(null!, null!, NullLogger<MediaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        [Test]
        public async Task LocalTranscodedValidatesBeforeTranscoding()
        {
            var controller = LocalController();
            var noPath = await controller.Transcoded(new FakeTranscoder(true), null);
            var badFormat = await controller.Transcoded(new FakeTranscoder(true), "a.flac", "ogg");
            var unavailable = await controller.Transcoded(new FakeTranscoder(false), "a.flac");
            var escaping = await controller.Transcoded(new FakeTranscoder(true), "../../etc/passwd");
            var missing = await controller.Transcoded(new FakeTranscoder(true), "missing-" + Guid.NewGuid() + ".flac");
            Assert.Multiple(() =>
            {
                Assert.That(StatusOf(noPath), Is.EqualTo(400));
                Assert.That(StatusOf(badFormat), Is.EqualTo(400));
                Assert.That(StatusOf(unavailable), Is.EqualTo(501));
                Assert.That(CodeOf(unavailable), Is.EqualTo(ApiErrorCodes.NotSupported));
                Assert.That(StatusOf(escaping), Is.EqualTo(400));
                Assert.That(StatusOf(missing), Is.EqualTo(404));
            });
        }

        [Test]
        public async Task ChannelTranscodedValidatesBeforeTheIndex()
        {
            // The index (IDbService) is null: these answers must not need it.
            var controller = Media();
            var badFormat = await controller.Transcoded(new FakeTranscoder(true), "-100", "abc", "wav");
            var unavailable = await controller.Transcoded(new FakeTranscoder(false), "-100", "abc");
            Assert.Multiple(() =>
            {
                Assert.That(StatusOf(badFormat), Is.EqualTo(400));
                Assert.That(StatusOf(unavailable), Is.EqualTo(501));
            });
        }

        [Test]
        public void MissingFfmpegIs501WithAnEnvelope()
        {
            var result = AudioController.FfmpegUnavailable();
            Assert.Multiple(() =>
            {
                Assert.That(StatusOf(result), Is.EqualTo(StatusCodes.Status501NotImplemented));
                Assert.That(((result as ObjectResult)?.Value as ApiResult<object>)?.Success, Is.False);
            });
        }
    }
}
