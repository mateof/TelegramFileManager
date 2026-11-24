using System.Threading.Channels;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models.Library;

namespace TelegramDownloader.Services.Library
{
    /// <summary>
    /// After a library scan, downloads into the image cache the posters of the
    /// items the scan touched (the sizes the web app shows in the grids), so
    /// opening the library does not wait for TMDB. Runs in the background, a
    /// few images at a time, and never fails a scan.
    /// </summary>
    public class LibraryImagePrefetcher : BackgroundService
    {
        /// <summary>Poster sizes of the catalogue grid (<c>w342</c>) and of its blurred placeholder (<c>w92</c>).</summary>
        public static readonly string[] PosterSizes = { "w342", "w92" };
        public const int Parallelism = 3;

        private readonly LibraryScanService _scan;
        private readonly ILibraryDbService _lib;
        private readonly LibraryImageService _images;
        private readonly ILogger<LibraryImagePrefetcher> _logger;
        private readonly Channel<IReadOnlyCollection<string>> _queue =
            Channel.CreateBounded<IReadOnlyCollection<string>>(new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropOldest });

        public LibraryImagePrefetcher(LibraryScanService scan, ILibraryDbService lib, LibraryImageService images, ILogger<LibraryImagePrefetcher> logger)
        {
            _scan = scan;
            _lib = lib;
            _images = images;
            _logger = logger;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _scan.ScanFinished += OnScanFinished;
            return base.StartAsync(cancellationToken);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _scan.ScanFinished -= OnScanFinished;
            return base.StopAsync(cancellationToken);
        }

        private void OnScanFinished(IReadOnlyCollection<string> itemIds) => _queue.Writer.TryWrite(itemIds);

        /// <summary>The images to prefetch for some items: their posters in every grid size.</summary>
        public static IEnumerable<(string Url, string Size)> ImagesOf(IEnumerable<LibraryItem> items)
        {
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.PosterUrl)) continue;
                foreach (var size in PosterSizes)
                    yield return (item.PosterUrl, size);
            }
        }

        public async Task<int> PrefetchAsync(IReadOnlyCollection<string> itemIds, CancellationToken ct)
        {
            if (itemIds.Count == 0) return 0;
            var items = await _lib.GetItems(itemIds);
            var fetched = await _images.PrefetchAsync(ImagesOf(items), Parallelism, ct);
            if (fetched > 0)
                _logger.LogInformation("Library: {Count} posters cached after the scan", fetched);
            return fetched;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var ids in _queue.Reader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        await PrefetchAsync(ids, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Library poster prefetch failed");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutting down
            }
        }
    }
}
