using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Controllers.Api.V1;
using TelegramDownloader.Data;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;
using TL;

namespace Test
{
    /// <summary>Fakes of the media endpoint tests: one channel file backed by one Telegram message.</summary>
    public static class MediaFakes
    {
        public static byte[] Bytes = Array.Empty<byte>();
        public static BsonFileManagerModel? Entry;
        public static Message? TgMessage;
        /// <summary>Milliseconds per 512 KB block, to look like Telegram.</summary>
        public static int Delay = 1;
        /// <summary>When set, reading the channel index fails the test (message endpoints must not need it).</summary>
        public static bool IndexForbidden;

        public static Message VideoMessage(int id, long size, string? fileName, string? mime) => new()
        {
            id = id,
            media = new MessageMediaDocument
            {
                document = new Document
                {
                    id = 1,
                    size = size,
                    mime_type = mime,
                    attributes = fileName == null
                        ? Array.Empty<DocumentAttribute>()
                        : new DocumentAttribute[] { new DocumentAttributeFilename { file_name = fileName } }
                }
            }
        };

        public class Telegram : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? args)
            {
                switch (method!.Name)
                {
                    case "get_IsConfigured": return true;
                    case nameof(ITelegramService.checkUserLogin): return true;
                    case nameof(ITelegramService.getChatName): return "chat";
                    case nameof(ITelegramService.getMessageFile): return Task.FromResult(TgMessage!);
                    case nameof(ITelegramService.DownloadFileStream):
                    {
                        var offset = (long)args![1]!;
                        var limit = (int)args[2]!;
                        var n = (int)Math.Max(0, Math.Min(limit, Bytes.Length - offset));
                        return Task.Run(async () =>
                        {
                            await Task.Delay(Delay);
                            return Bytes.AsSpan((int)offset, n).ToArray();
                        });
                    }
                    case nameof(ITelegramService.DownloadFileStreamChunks):
                        return Chunks((long)args![1]!, (long)args[2]!);
                    default:
                        throw new NotSupportedException(method.Name);
                }
            }

            private static async IAsyncEnumerable<byte[]> Chunks(long offset, long limit, [EnumeratorCancellation] CancellationToken ct = default)
            {
                var pos = offset;
                var end = Math.Min(Bytes.Length, offset + limit);
                while (pos < end)
                {
                    await Task.Delay(Delay, ct);
                    var n = (int)Math.Min(524288, Bytes.Length - pos);
                    yield return Bytes.AsSpan((int)pos, n).ToArray();
                    pos += n;
                }
            }
        }

        public class Db : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? args)
            {
                if (IndexForbidden) throw new InvalidOperationException("the channel index must not be read");
                return method!.Name switch
                {
                    nameof(IDbService.getFileById) => Task.FromResult(Entry!),
                    _ => throw new NotSupportedException(method.Name)
                };
            }
        }

        public class Files : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? args)
            {
                if (IndexForbidden) throw new InvalidOperationException("the channel index must not be read");
                return method!.Name switch
                {
                    nameof(IFileService.getItemById) => Task.FromResult(Entry!),
                    _ => throw new NotSupportedException(method.Name)
                };
            }
        }

        public static ServiceProvider Services()
        {
            var sc = new ServiceCollection();
            sc.AddLogging();
            sc.AddMvcCore();
            sc.AddSingleton(DispatchProxy.Create<ITelegramService, Telegram>());
            sc.AddSingleton(DispatchProxy.Create<IDbService, Db>());
            sc.AddSingleton(DispatchProxy.Create<IFileService, Files>());
            sc.AddSingleton(new TransactionInfoService(NullLogger<IFileService>.Instance));
            sc.AddSingleton<IProgressiveDownloadService, ProgressiveDownloadService>();
            return sc.BuildServiceProvider();
        }
    }

    public class MimeTypeTest
    {
        [Test]
        public void Accepts_extensions_and_file_names_in_any_case()
        {
            Assert.Multiple(() =>
            {
                // The v1 endpoints pass the whole file name: it used to come back as octet-stream.
                Assert.That(FileService.getMimeType("Holiday.mp4"), Is.EqualTo("video/mp4"));
                Assert.That(FileService.getMimeType("Holiday.MP4"), Is.EqualTo("video/mp4"));
                Assert.That(FileService.getMimeType("a.b.c.mkv"), Is.EqualTo("video/x-matroska"));
                Assert.That(FileService.getMimeType("mp3"), Is.EqualTo("audio/mpeg"));
                Assert.That(FileService.getMimeType(".flac"), Is.EqualTo("audio/flac"));
                Assert.That(FileService.getMimeType("clip.webm"), Is.EqualTo("video/webm"));
                Assert.That(FileService.getMimeType("voice.opus"), Is.EqualTo("audio/ogg"));
                Assert.That(FileService.getMimeType("noextension"), Is.EqualTo("application/octet-stream"));
                Assert.That(FileService.getMimeType(""), Is.EqualTo("application/octet-stream"));
            });
        }
    }

    public class MessageAttachmentTest
    {
        [Test]
        public void Describes_documents_and_photos()
        {
            var named = MessageAttachment.From(MediaFakes.VideoMessage(5, 1000, "Clip.MP4", "video/mp4"))!;
            Assert.That(named, Is.EqualTo(new MessageAttachment("Clip.MP4", 1000, "video/mp4", "Video", false)));

            // Telegram videos often have no file name: the type gives the extension.
            var unnamed = MessageAttachment.From(MediaFakes.VideoMessage(6, 10, null, "video/mp4"))!;
            Assert.That(unnamed.Name, Is.EqualTo("file_6.mp4"));
            Assert.That(unnamed.Category, Is.EqualTo("Video"));

            // Generic documents: the extension decides.
            var generic = MessageAttachment.From(MediaFakes.VideoMessage(7, 10, "song.flac", "application/octet-stream"))!;
            Assert.That((generic.MimeType, generic.Category), Is.EqualTo(("audio/flac", "Audio")));

            var voice = MessageAttachment.From(MediaFakes.VideoMessage(8, 10, null, "audio/ogg"))!;
            Assert.That((voice.Name, voice.Category), Is.EqualTo(("file_8.ogg", "Audio")));

            var photo = MessageAttachment.From(new Message { id = 9, media = new MessageMediaPhoto { photo = new Photo() } })!;
            Assert.That((photo.Name, photo.MimeType, photo.IsPhoto), Is.EqualTo(("photo_9.jpg", "image/jpeg", true)));

            Assert.That(MessageAttachment.From(new Message { id = 10 }), Is.Null);
        }

        [Test]
        public void File_names_are_safe_for_the_cache_folder()
        {
            Assert.That(MessageAttachment.SafeFileName("a/b\\c:d?.mp4"), Is.EqualTo("a_b_c_d_.mp4"));
            Assert.That(MessageAttachment.SafeFileName("  "), Is.Null);
            var entry = MessageAttachment.From(MediaFakes.VideoMessage(11, 10, "x.mp4", "video/mp4"))!.ToEntry(11);
            Assert.That((entry.MessageId, entry.Type, entry.Size, entry.IsFile), Is.EqualTo(((int?)11, ".mp4", 10L, true)));
        }
    }

    [NonParallelizable]
    public class MediaContentEndpointTest
    {
        private string _temp = null!;
        private string _oldTemp = null!;
        private ServiceProvider _services = null!;

        [SetUp]
        public void SetUp()
        {
            _oldTemp = FileService.TEMPDIR;
            _temp = Path.Combine(Path.GetTempPath(), "tfm-media-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
            FileService.TEMPDIR = _temp;

            MediaFakes.Delay = 1;
            MediaFakes.IndexForbidden = false;
            MediaFakes.Bytes = new byte[3 * 1024 * 1024 + 1234];
            new Random(1).NextBytes(MediaFakes.Bytes);
            MediaFakes.Entry = new BsonFileManagerModel
            {
                Id = "f1", Name = "Holiday.mp4", Type = ".mp4", IsFile = true, Size = MediaFakes.Bytes.Length, MessageId = 7
            };
            MediaFakes.TgMessage = MediaFakes.VideoMessage(7, MediaFakes.Bytes.Length, "Holiday.mp4", "video/mp4");
            _services = MediaFakes.Services();
        }

        [TearDown]
        public void TearDown()
        {
            MediaFakes.IndexForbidden = false;
            _services.Dispose();
            FileService.TEMPDIR = _oldTemp;
            try { Directory.Delete(_temp, true); } catch { /* a background cache download may still hold a file */ }
        }

        private MediaController Controller(HttpContext ctx)
        {
            var controller = ActivatorUtilities.CreateInstance<MediaController>(_services);
            controller.ControllerContext = new ControllerContext(new ActionContext(ctx, new RouteData(), new ControllerActionDescriptor()));
            return controller;
        }

        private async Task<HttpContext> Send(Func<MediaController, Task<IActionResult>> action, string? range)
        {
            var ctx = new DefaultHttpContext { RequestServices = _services };
            ctx.Request.Method = "GET";
            ctx.Request.Scheme = "http";
            ctx.Request.Host = new HostString("tfm.test");
            if (range != null) ctx.Request.Headers.Range = range;
            ctx.Response.Body = new MemoryStream();

            var controller = Controller(ctx);
            var result = await action(controller);
            await result.ExecuteResultAsync(controller.ControllerContext);
            return ctx;
        }

        private static byte[] Body(HttpContext ctx) => ((MemoryStream)ctx.Response.Body).ToArray();
        private static byte[] Slice(long from, long to) => MediaFakes.Bytes.AsSpan((int)from, (int)(to - from + 1)).ToArray();

        [Test]
        public async Task A_cached_video_is_served_with_its_media_type()
        {
            var folder = Path.Combine(_temp, "_temp");
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(Path.Combine(folder, "-1001-7-Holiday.mp4"), MediaFakes.Bytes);

            var ctx = await Send(c => c.Content("-1001", "f1"), "bytes=0-");

            Assert.That(ctx.Response.StatusCode, Is.EqualTo(206));
            // Was application/octet-stream (the whole name was looked up as an extension).
            Assert.That(ctx.Response.ContentType, Is.EqualTo("video/mp4"));
            Assert.That(Body(ctx), Is.EqualTo(MediaFakes.Bytes));
        }

        [Test]
        public async Task An_uncached_video_streams_ranges_through_the_progressive_cache()
        {
            var ctx = await Send(c => c.Content("-1001", "f1"), "bytes=1048576-1049599");

            Assert.That(ctx.Response.StatusCode, Is.EqualTo(206));
            Assert.That(ctx.Response.ContentType, Is.EqualTo("video/mp4"));
            Assert.That(ctx.Response.Headers.ContentRange.ToString(), Is.EqualTo($"bytes 1048576-1049599/{MediaFakes.Bytes.Length}"));
            Assert.That(ctx.Response.Headers.AcceptRanges.ToString(), Is.EqualTo("bytes"));
            Assert.That(Body(ctx), Is.EqualTo(Slice(1048576, 1049599)));
        }

        [Test]
        public async Task A_message_video_plays_without_reading_the_index()
        {
            MediaFakes.IndexForbidden = true;
            MediaFakes.TgMessage = MediaFakes.VideoMessage(42, MediaFakes.Bytes.Length, null, "video/mp4");

            var first = await Send(c => c.MessageContent("-1001", 42), "bytes=0-1");
            Assert.That(first.Response.StatusCode, Is.EqualTo(206));
            Assert.That(first.Response.ContentType, Is.EqualTo("video/mp4"));
            Assert.That(first.Response.Headers.ContentRange.ToString(), Is.EqualTo($"bytes 0-1/{MediaFakes.Bytes.Length}"));
            Assert.That(Body(first), Is.EqualTo(Slice(0, 1)));
            Assert.That(first.Response.Headers.ContentDisposition.ToString(), Does.StartWith("inline"));

            var seek = await Send(c => c.MessageContent("-1001", 42), $"bytes={MediaFakes.Bytes.Length - 1000}-");
            Assert.That(seek.Response.StatusCode, Is.EqualTo(206));
            Assert.That(Body(seek), Is.EqualTo(Slice(MediaFakes.Bytes.Length - 1000, MediaFakes.Bytes.Length - 1)));
        }

        [Test]
        public async Task A_message_attachment_can_be_downloaded_whole()
        {
            MediaFakes.IndexForbidden = true;
            MediaFakes.TgMessage = MediaFakes.VideoMessage(43, MediaFakes.Bytes.Length, "Report.pdf", "application/pdf");

            var ctx = await Send(c => c.MessageContent("-1001", 43, "attachment"), null);

            Assert.That(ctx.Response.StatusCode, Is.EqualTo(200));
            Assert.That(ctx.Response.ContentType, Is.EqualTo("application/pdf"));
            Assert.That(ctx.Response.Headers.ContentDisposition.ToString(), Does.StartWith("attachment"));
            Assert.That(Body(ctx), Is.EqualTo(MediaFakes.Bytes));
        }

        [Test]
        public async Task A_message_without_a_file_is_a_404()
        {
            MediaFakes.TgMessage = new Message { id = 44 };
            var controller = Controller(new DefaultHttpContext { RequestServices = _services });

            var result = await controller.MessageContent("-1001", 44);

            Assert.That(result, Is.InstanceOf<ObjectResult>());
            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(404));
            Assert.That(await controller.MessageContent("abc", 44), Is.InstanceOf<BadRequestObjectResult>());
        }

        [TestCase(nameof(MediaController.Content))]
        [TestCase(nameof(MediaController.Stream))]
        [TestCase(nameof(MediaController.MessageContent))]
        [TestCase(nameof(MediaController.Thumbnail))]
        public void Media_endpoints_can_still_answer_json_errors(string action)
        {
            // [Produces] restricts the ObjectResult types: with only a binary type
            // MVC finds no formatter for the JSON error envelope and answers 406.
            var method = typeof(MediaController).GetMethods().Single(m => m.Name == action && m.DeclaringType == typeof(MediaController));
            var produces = method.GetCustomAttribute<ProducesAttribute>()!;
            Assert.That(produces.ContentTypes, Does.Contain("application/json"));
        }
    }
}
