using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Models.Library;
using TelegramDownloader.Services.Library;

namespace Test.Library
{
    /// <summary>The TMDB and OMDb clients against canned responses.</summary>
    public class ProvidersTest
    {
        private class CannedHandler : HttpMessageHandler
        {
            public List<string> Requests { get; } = new();
            public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request.RequestUri!.ToString());
                return Task.FromResult(Respond(request));
            }
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

        [Test]
        public async Task TmdbSearchAndDetails()
        {
            var handler = new CannedHandler();
            handler.Respond = req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.Contains("/search/movie"))
                    return Json("""{"results":[{"id":157336,"title":"Interstellar","original_title":"Interstellar","release_date":"2014-11-05","overview":"Un grupo...","poster_path":"/abc.jpg","popularity":120.5}]}""");
                if (url.Contains("/movie/157336"))
                    return Json("""{"id":157336,"title":"Interstellar","original_title":"Interstellar","release_date":"2014-11-05","overview":"Un grupo...","tagline":"Tag","status":"Released","genres":[{"id":1,"name":"Ciencia ficción"}],"vote_average":8.43,"vote_count":30000,"runtime":169,"poster_path":"/abc.jpg","backdrop_path":"/bd.jpg","external_ids":{"imdb_id":"tt0816692"}}""");
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };
            var provider = new TmdbProvider(new HttpClient(handler), "v3key", "es-ES", NullLogger.Instance);

            var hits = await provider.SearchAsync(LibraryKind.Movie, "Interstellar", 2014, CancellationToken.None);
            Assert.That(hits, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(hits[0].ProviderId, Is.EqualTo("157336"));
                Assert.That(hits[0].Year, Is.EqualTo(2014));
                Assert.That(hits[0].PosterUrl, Is.EqualTo("https://image.tmdb.org/t/p/original/abc.jpg"));
                Assert.That(handler.Requests[0], Does.Contain("api_key=v3key").And.Contain("year=2014").And.Contain("language=es-ES"));
            });

            var item = await provider.GetItemAsync(LibraryKind.Movie, "157336", CancellationToken.None);
            Assert.That(item, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(item!.Title, Is.EqualTo("Interstellar"));
                Assert.That(item.Runtime, Is.EqualTo(169));
                Assert.That(item.Rating, Is.EqualTo(8.4));
                Assert.That(item.Genres, Is.EqualTo(new List<string> { "Ciencia ficción" }));
                Assert.That(item.ImdbId, Is.EqualTo("tt0816692"));
                Assert.That(item.ExternalIds["tmdb"], Is.EqualTo("157336"));
                Assert.That(item.BackdropUrl, Is.EqualTo("https://image.tmdb.org/t/p/original/bd.jpg"));
                Assert.That(item.SortTitle, Is.EqualTo("interstellar"));
            });
        }

        [Test]
        public async Task TmdbSeriesSeasonsAndEpisodes()
        {
            var handler = new CannedHandler();
            handler.Respond = req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.Contains("/tv/1396/season/1"))
                    return Json("""{"episodes":[{"episode_number":1,"season_number":1,"name":"Piloto","overview":"...","still_path":"/s1.jpg","air_date":"2008-01-20","runtime":58,"vote_average":8.3}]}""");
                if (url.Contains("/tv/1396"))
                    return Json("""{"id":1396,"name":"Breaking Bad","original_name":"Breaking Bad","first_air_date":"2008-01-20","overview":"","episode_run_time":[45],"seasons":[{"season_number":0,"name":"Especiales","episode_count":3},{"season_number":1,"name":"Temporada 1","episode_count":7,"poster_path":"/p1.jpg","air_date":"2008-01-20"}],"external_ids":{"imdb_id":"tt0903747","tvdb_id":81189}}""");
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };
            var provider = new TmdbProvider(new HttpClient(handler), "v3key", "es-ES", NullLogger.Instance);

            var item = await provider.GetItemAsync(LibraryKind.Series, "1396", CancellationToken.None);
            Assert.That(item, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(item!.Seasons.Select(s => s.Number), Is.EqualTo(new[] { 0, 1 }));
                Assert.That(item.Runtime, Is.EqualTo(45));
                Assert.That(item.ExternalIds["tvdb"], Is.EqualTo("81189"));
                Assert.That(handler.Requests.Count(u => u.Contains("language=en-US")), Is.EqualTo(1), "empty Spanish overview falls back to English");
            });

            var episodes = await provider.GetSeasonAsync("1396", 1, CancellationToken.None);
            Assert.That(episodes, Has.Count.EqualTo(1));
            Assert.That(episodes[0].Title, Is.EqualTo("Piloto"));
            Assert.That(episodes[0].StillUrl, Is.EqualTo("https://image.tmdb.org/t/p/original/s1.jpg"));
        }

        [Test]
        public async Task TmdbBearerTokenGoesInTheHeader()
        {
            var handler = new CannedHandler();
            string? auth = null;
            handler.Respond = req =>
            {
                auth = req.Headers.Authorization?.ToString();
                return Json("""{"results":[]}""");
            };
            var token = "eyJ" + new string('x', 100);
            var provider = new TmdbProvider(new HttpClient(handler), token, "es-ES", NullLogger.Instance);
            await provider.SearchAsync(LibraryKind.Movie, "x", null, CancellationToken.None);
            Assert.That(auth, Is.EqualTo("Bearer " + token));
            Assert.That(handler.Requests[0], Does.Not.Contain("api_key"));
        }

        [Test]
        public void TmdbRejectedKeyIsAProviderError()
        {
            var handler = new CannedHandler { Respond = _ => Json("""{"status_message":"Invalid API key"}""", HttpStatusCode.Unauthorized) };
            var provider = new TmdbProvider(new HttpClient(handler), "bad", "es-ES", NullLogger.Instance);
            var ex = Assert.ThrowsAsync<ProviderException>(() => provider.SearchAsync(LibraryKind.Movie, "x", null, CancellationToken.None));
            Assert.That(ex!.StatusCode, Is.EqualTo(401));
        }

        [Test]
        public async Task OmdbSearchDetailsAndSeason()
        {
            var handler = new CannedHandler();
            handler.Respond = req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.Contains("s=Interstellar"))
                    return Json("""{"Search":[{"Title":"Interstellar","Year":"2014","imdbID":"tt0816692","Type":"movie","Poster":"https://m.media-amazon.com/images/x.jpg"}],"totalResults":"1","Response":"True"}""");
                if (url.Contains("i=tt0816692") && !url.Contains("Season"))
                    return Json("""{"Title":"Interstellar","Year":"2014","Runtime":"169 min","Genre":"Adventure, Drama, Sci-Fi","Plot":"A team...","Poster":"https://m.media-amazon.com/images/x.jpg","imdbRating":"8.7","imdbVotes":"2,100,000","imdbID":"tt0816692","Type":"movie","Response":"True"}""");
                if (url.Contains("i=tt0903747") && url.Contains("Season=1"))
                    return Json("""{"Title":"Breaking Bad","Season":"1","totalSeasons":"5","Episodes":[{"Title":"Pilot","Released":"2008-01-20","Episode":"1","imdbRating":"9.0","imdbID":"tt0959621"}],"Response":"True"}""");
                if (url.Contains("s=Nothing"))
                    return Json("""{"Response":"False","Error":"Movie not found!"}""");
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            };
            var provider = new OmdbProvider(new HttpClient(handler), "key", NullLogger.Instance);

            var hits = await provider.SearchAsync(LibraryKind.Movie, "Interstellar", 2014, CancellationToken.None);
            Assert.That(hits, Has.Count.EqualTo(1));
            Assert.That(hits[0].ImdbId, Is.EqualTo("tt0816692"));

            var item = await provider.GetItemAsync(LibraryKind.Movie, "tt0816692", CancellationToken.None);
            Assert.That(item, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(item!.Rating, Is.EqualTo(8.7));
                Assert.That(item.VoteCount, Is.EqualTo(2100000));
                Assert.That(item.Runtime, Is.EqualTo(169));
                Assert.That(item.Genres, Has.Count.EqualTo(3));
                Assert.That(item.ImdbId, Is.EqualTo("tt0816692"));
            });

            var episodes = await provider.GetSeasonAsync("tt0903747", 1, CancellationToken.None);
            Assert.That(episodes, Has.Count.EqualTo(1));
            Assert.That(episodes[0].Rating, Is.EqualTo(9.0));

            var none = await provider.SearchAsync(LibraryKind.Movie, "Nothing", null, CancellationToken.None);
            Assert.That(none, Is.Empty);
        }

        [Test]
        public void OmdbInvalidKeyIsAProviderError()
        {
            var handler = new CannedHandler { Respond = _ => Json("""{"Response":"False","Error":"Invalid API key!"}""") };
            var provider = new OmdbProvider(new HttpClient(handler), "bad", NullLogger.Instance);
            var ex = Assert.ThrowsAsync<ProviderException>(() => provider.SearchAsync(LibraryKind.Movie, "x", null, CancellationToken.None));
            Assert.That(ex!.StatusCode, Is.EqualTo(401));
        }

        [Test]
        public void ImageProxyKeysRoundTripAndOnlyKnownHostsAreServed()
        {
            var url = "https://image.tmdb.org/t/p/original/abc.jpg";
            var key = LibraryImageService.Encode(url);
            Assert.Multiple(() =>
            {
                Assert.That(LibraryImageService.Decode(key), Is.EqualTo(url));
                Assert.That(LibraryImageService.IsAllowed(url), Is.True);
                Assert.That(LibraryImageService.IsAllowed("https://evil.example/x.jpg"), Is.False);
                Assert.That(LibraryImageService.IsAllowed("http://image.tmdb.org/t/p/original/abc.jpg"), Is.False, "https only");
                Assert.That(LibraryImageService.SizedUrl(url, "w342"), Is.EqualTo("https://image.tmdb.org/t/p/w342/abc.jpg"));
                Assert.That(LibraryImageService.SizedUrl("https://m.media-amazon.com/x.jpg", "w342"), Is.EqualTo("https://m.media-amazon.com/x.jpg"));
                Assert.That(LibraryImageService.ProxyUrl("http://host", url, "w342"), Does.StartWith("http://host/api/v1/library/images/").And.EndWith("?size=w342"));
            });
        }
    }
}
