using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Controllers.Api.V1;
using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace Test.Library
{
    /// <summary>
    /// The library image cache against a fake image host: one download per
    /// image, bounded downloads, failures not hammered, disk budget, and the
    /// HTTP answer of the image endpoint.
    /// </summary>
    public class LibraryImageServiceTest
    {
        private const string Poster = "https://image.tmdb.org/t/p/original/abc.jpg";

        private sealed class FakeImageHost : HttpMessageHandler
        {
            public ConcurrentQueue<string> Requests { get; } = new();
            /// <summary>Completed by the test to let the pending responses go.</summary>
            public TaskCompletionSource Gate { get; set; } = CompletedGate();
            public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => Image(1000);
            private int _active;
            public int MaxActive;

            public static TaskCompletionSource CompletedGate()
            {
                var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                t.SetResult();
                return t;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Enqueue(request.RequestUri!.ToString());
                var now = Interlocked.Increment(ref _active);
                lock (this) MaxActive = Math.Max(MaxActive, now);
                try
                {
                    await Gate.Task.WaitAsync(cancellationToken);
                    return Respond(request);
                }
                finally
                {
                    Interlocked.Decrement(ref _active);
                }
            }
        }

        private sealed class Factory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;
            public Factory(HttpMessageHandler handler) => _handler = handler;
            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }

        private static HttpResponseMessage Image(int bytes, string type = "image/jpeg")
        {
            var content = new ByteArrayContent(Enumerable.Repeat((byte)7, bytes).ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue(type);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        private string _folder = null!;
        private FakeImageHost _host = null!;
        private LibraryImageService _images = null!;

        private LibraryImageService Create(Action<LibraryImageOptions>? configure = null)
        {
            var options = new LibraryImageOptions { CacheFolder = _folder };
            configure?.Invoke(options);
            return new LibraryImageService(new Factory(_host), NullLogger<LibraryImageService>.Instance, options);
        }

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "tfm-libimg-" + Guid.NewGuid().ToString("N"));
            _host = new FakeImageHost();
            _images = Create();
        }

        [TearDown]
        public void TearDown()
        {
            _images.Dispose();
            _host.Dispose();
            try { Directory.Delete(_folder, true); } catch { }
        }

        [Test]
        public async Task DownloadsTheSizedImageOnceThenServesItFromDisk()
        {
            var first = await _images.GetAsync(Poster, "w342", CancellationToken.None);
            var second = await _images.GetAsync(Poster, "w342", CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(_host.Requests, Is.EqualTo(new[] { "https://image.tmdb.org/t/p/w342/abc.jpg" }));
                Assert.That(first, Is.Not.Null);
                Assert.That(File.Exists(first!.Path), Is.True);
                Assert.That(Path.GetDirectoryName(first.Path), Is.EqualTo(Path.GetFullPath(_folder)));
                Assert.That(first.ContentType, Is.EqualTo("image/jpeg"));
                Assert.That(first.Length, Is.EqualTo(1000));
                Assert.That(second!.ETag, Is.EqualTo(first.ETag));
                Assert.That(first.ETag, Does.StartWith("\"").And.EndWith("\""));
                Assert.That(_images.IsCached(Poster, "w342"), Is.True);
                Assert.That(_images.IsCached(Poster, "w92"), Is.False);
            });
        }

        [Test]
        public async Task StoresWithTheExtensionOfTheContentType()
        {
            _host.Respond = _ => Image(10, "image/webp");
            var image = await _images.GetAsync(Poster, "w92", CancellationToken.None);
            Assert.That(image!.ContentType, Is.EqualTo("image/webp"));
            Assert.That(image.Path, Does.EndWith(".webp"));
        }

        [Test]
        public async Task ConcurrentRequestsShareOneDownload()
        {
            _host.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requests = Enumerable.Range(0, 20).Select(_ => _images.GetAsync(Poster, "w342", CancellationToken.None)).ToList();
            await Task.Delay(100);
            _host.Gate.SetResult();
            var results = await Task.WhenAll(requests);

            Assert.That(_host.Requests, Has.Count.EqualTo(1));
            Assert.That(results.Select(r => r?.Path).Distinct().Count(), Is.EqualTo(1));
            Assert.That(results.All(r => r != null), Is.True);
        }

        [Test]
        public async Task DownloadsAreBounded()
        {
            _images.Dispose();
            _images = Create(o => o.MaxConcurrentDownloads = 2);
            _host.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requests = Enumerable.Range(0, 8)
                .Select(i => _images.GetAsync($"https://image.tmdb.org/t/p/original/p{i}.jpg", "w185", CancellationToken.None)).ToList();
            await Task.Delay(150);
            Assert.That(_host.Requests, Has.Count.EqualTo(2), "only two downloads start");
            _host.Gate.SetResult();
            await Task.WhenAll(requests);

            Assert.That(_host.Requests, Has.Count.EqualTo(8));
            Assert.That(_host.MaxActive, Is.EqualTo(2));
        }

        [Test]
        public async Task ACancelledClientDoesNotCancelTheDownload()
        {
            _host.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cts = new CancellationTokenSource();
            var request = _images.GetAsync(Poster, "w500", cts.Token);
            await Task.Delay(50);
            cts.Cancel();
            Assert.That(async () => await request, Throws.InstanceOf<OperationCanceledException>());

            _host.Gate.SetResult();
            var image = await _images.GetAsync(Poster, "w500", CancellationToken.None);
            Assert.That(image, Is.Not.Null);
            Assert.That(_host.Requests, Has.Count.EqualTo(1), "the second request joined the first download");
        }

        [Test]
        public async Task FailuresAreRememberedForAWhile()
        {
            _host.Respond = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            Assert.That(await _images.GetAsync(Poster, "w342", CancellationToken.None), Is.Null);
            Assert.That(await _images.GetAsync(Poster, "w342", CancellationToken.None), Is.Null);
            Assert.That(_host.Requests, Has.Count.EqualTo(1), "the provider is not asked again right away");

            _host.Respond = _ => Image(100);
            _images.ResetFailures();
            Assert.That(await _images.GetAsync(Poster, "w342", CancellationToken.None), Is.Not.Null);
            Assert.That(_host.Requests, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task FailuresExpire()
        {
            _images.Dispose();
            _images = Create(o => o.FailureTtl = TimeSpan.Zero);
            _host.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadGateway);
            await _images.GetAsync(Poster, "w342", CancellationToken.None);
            _host.Respond = _ => Image(100);
            Assert.That(await _images.GetAsync(Poster, "w342", CancellationToken.None), Is.Not.Null);
        }

        [Test]
        public async Task RefusesWhatIsNotAnImageOrTooLarge()
        {
            _images.Dispose();
            _images = Create(o => o.MaxImageBytes = 500);
            _host.Respond = req => req.RequestUri!.AbsolutePath.Contains("html")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>captive portal</html>", System.Text.Encoding.UTF8, "text/html") }
                : Image(2000);

            Assert.That(await _images.GetAsync("https://image.tmdb.org/t/p/original/html.jpg", "w92", CancellationToken.None), Is.Null);
            Assert.That(await _images.GetAsync("https://image.tmdb.org/t/p/original/big.jpg", "w92", CancellationToken.None), Is.Null);
            Assert.That(Directory.Exists(_folder) ? Directory.GetFiles(_folder) : Array.Empty<string>(), Is.Empty, "nothing (not even a partial file) is left");
        }

        [TestCase("http://image.tmdb.org/t/p/original/a.jpg")]
        [TestCase("https://evil.example.com/t/p/original/a.jpg")]
        [TestCase("https://image.tmdb.org:8443/t/p/original/a.jpg")]
        [TestCase("https://user:pw@image.tmdb.org/t/p/original/a.jpg")]
        [TestCase("file:///etc/passwd")]
        [TestCase("../../etc/passwd")]
        public async Task OnlyKnownImageHosts(string url)
        {
            Assert.That(await _images.GetAsync(url, "w342", CancellationToken.None), Is.Null);
            Assert.That(_host.Requests, Is.Empty);
        }

        [Test]
        public async Task SizesOnlyApplyToTmdb()
        {
            const string omdb = "https://m.media-amazon.com/images/M/MV5B@._V1_SX300.jpg";
            var a = await _images.GetAsync(omdb, "w92", CancellationToken.None);
            var b = await _images.GetAsync(omdb, "w500", CancellationToken.None);
            Assert.That(a!.Path, Is.EqualTo(b!.Path));
            Assert.That(_host.Requests, Is.EqualTo(new[] { omdb }));
        }

        [TestCase(null, true, "original")]
        [TestCase("", true, "original")]
        [TestCase("W342", true, "w342")]
        [TestCase("w1280", true, "w1280")]
        [TestCase("w999", false, "original")]
        [TestCase("../x", false, "original")]
        public void SizesAreAllowListed(string? size, bool valid, string expected)
        {
            Assert.That(LibraryImageService.TryNormalizeSize(size, out var normalized), Is.EqualTo(valid));
            Assert.That(normalized, Is.EqualTo(expected));
        }

        [Test]
        public void KeysRoundTrip()
        {
            var key = LibraryImageService.Encode(Poster);
            Assert.That(key, Does.Match("^[A-Za-z0-9_-]+$"));
            Assert.That(LibraryImageService.Decode(key), Is.EqualTo(Poster));
            Assert.That(LibraryImageService.Decode("%%%"), Is.Null);
            Assert.That(LibraryImageService.Decode(new string('a', 5000)), Is.Null);
        }

        [Test]
        public async Task TrimDeletesTheLeastRecentlyServedPastTheBudget()
        {
            _images.Dispose();
            _images = Create(o => o.MaxCacheBytes = 10_000);
            for (var i = 0; i < 4; i++)
                await _images.GetAsync($"https://image.tmdb.org/t/p/original/t{i}.jpg", "w92", CancellationToken.None);
            await Task.Delay(200); // let the background trims of those downloads finish
            // t0 is the oldest, t3 the most recently served
            var files = Directory.GetFiles(_folder).Select(f => new FileInfo(f)).ToList();
            Assert.That(files, Has.Count.EqualTo(4));
            for (var i = 0; i < 4; i++)
            {
                var path = (await _images.GetAsync($"https://image.tmdb.org/t/p/original/t{i}.jpg", "w92", CancellationToken.None))!.Path;
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-10 + i));
                File.SetLastAccessTimeUtc(path, DateTime.UtcNow.AddDays(-10 + i));
            }
            File.WriteAllBytes(Path.Combine(_folder, "big.jpg"), new byte[8000]);
            File.SetLastAccessTimeUtc(Path.Combine(_folder, "big.jpg"), DateTime.UtcNow);
            var stale = Path.Combine(_folder, "x.123.part");
            File.WriteAllBytes(stale, new byte[10]);
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));

            var deleted = _images.Trim();

            Assert.Multiple(() =>
            {
                Assert.That(deleted, Is.EqualTo(3), "12000 bytes down to 9000 or less: the three oldest posters go");
                Assert.That(_images.IsCached("https://image.tmdb.org/t/p/original/t0.jpg", "w92"), Is.False);
                Assert.That(_images.IsCached("https://image.tmdb.org/t/p/original/t3.jpg", "w92"), Is.True);
                Assert.That(File.Exists(stale), Is.False, "stale partial downloads are removed");
                Assert.That(LibraryImageService.Usage(_folder), Is.EqualTo((2, 9000L)));
            });
        }

        [Test]
        public async Task PrefetchDownloadsOnlyWhatIsMissing()
        {
            await _images.GetAsync(Poster, "w342", CancellationToken.None);
            var items = new[]
            {
                new LibraryItem { PosterUrl = Poster },
                new LibraryItem { PosterUrl = "https://image.tmdb.org/t/p/original/other.jpg" },
                new LibraryItem { PosterUrl = null }
            };

            var fetched = await _images.PrefetchAsync(LibraryImagePrefetcher.ImagesOf(items), 2, CancellationToken.None);

            Assert.That(fetched, Is.EqualTo(3), "w92 of the first, w342 and w92 of the second");
            Assert.That(_host.Requests, Has.Count.EqualTo(4));
        }

        [Test]
        public async Task PrefetcherLoadsTheItemsOfAScan()
        {
            var db = new FakeLibraryDb();
            var item = new LibraryItem { Title = "Interstellar", PosterUrl = Poster };
            db.Items[item.Id] = item;
            var scan = new LibraryScanService(db, new FakeProviderSource(), NullLogger<LibraryScanService>.Instance);
            var prefetcher = new LibraryImagePrefetcher(scan, db, _images, NullLogger<LibraryImagePrefetcher>.Instance);

            var fetched = await prefetcher.PrefetchAsync(new[] { item.Id, "missing" }, CancellationToken.None);

            Assert.That(fetched, Is.EqualTo(2));
            Assert.That(_images.IsCached(Poster, "w342") && _images.IsCached(Poster, "w92"), Is.True);
        }

        #region Endpoint

        private LibraryController Controller(HttpContext? context = null)
        {
            var controller = new LibraryController(null!, null!, _images, null!, NullLogger<LibraryController>.Instance);
            controller.ControllerContext = new ControllerContext { HttpContext = context ?? new DefaultHttpContext() };
            return controller;
        }

        [Test]
        public async Task EndpointServesTheFileWithLongLivedValidators()
        {
            var controller = Controller();
            var result = await controller.Image(LibraryImageService.Encode(Poster), "w342");

            Assert.That(result, Is.InstanceOf<PhysicalFileResult>());
            var file = (PhysicalFileResult)result;
            var headers = controller.Response.Headers;
            Assert.Multiple(() =>
            {
                Assert.That(file.ContentType, Is.EqualTo("image/jpeg"));
                Assert.That(file.EntityTag, Is.Not.Null);
                Assert.That(file.LastModified, Is.Not.Null);
                Assert.That(headers.CacheControl.ToString(), Is.EqualTo("private, max-age=31536000, immutable"));
                Assert.That(headers.XContentTypeOptions.ToString(), Is.EqualTo("nosniff"));
            });
        }

        [Test]
        public async Task EndpointErrorsAreNotCached()
        {
            _host.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
            var controller = Controller();

            var missing = await controller.Image(LibraryImageService.Encode(Poster), "w342");
            Assert.That(missing, Is.InstanceOf<ObjectResult>().With.Property("StatusCode").EqualTo(404));
            Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));

            var badSize = await Controller().Image(LibraryImageService.Encode(Poster), "w9999");
            Assert.That(badSize, Is.InstanceOf<ObjectResult>().With.Property("StatusCode").EqualTo(400));

            var badHost = await Controller().Image(LibraryImageService.Encode("https://evil.example.com/a.jpg"), "w342");
            Assert.That(badHost, Is.InstanceOf<ObjectResult>().With.Property("StatusCode").EqualTo(400));
        }

        #endregion
    }
}
