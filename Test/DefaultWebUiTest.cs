using Microsoft.AspNetCore.Http;
using TelegramDownloader.Middleware;
using TelegramDownloader.Models;

namespace Test
{
    public class DefaultWebUiTest
    {
        private string _previous = WebUiMode.Classic;

        [SetUp]
        public void SaveConfig() => _previous = GeneralConfigStatic.config.DefaultWebUi;

        [TearDown]
        public void RestoreConfig() => GeneralConfigStatic.config.DefaultWebUi = _previous;

        private static async Task<(HttpContext ctx, bool nextCalled)> Run(string path, string query = "", string? cookie = null, string accept = "text/html,application/xhtml+xml", string method = "GET")
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Method = method;
            ctx.Request.Path = path;
            ctx.Request.QueryString = new QueryString(query);
            ctx.Request.Headers.Accept = accept;
            if (cookie != null) ctx.Request.Headers.Cookie = $"{DefaultWebUiMiddleware.CookieName}={cookie}";
            var nextCalled = false;
            var middleware = new DefaultWebUiMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            await middleware.InvokeAsync(ctx);
            return (ctx, nextCalled);
        }

        [Test]
        public void Mode_values_are_normalized()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WebUiMode.Normalize("APP"), Is.EqualTo(WebUiMode.App));
                Assert.That(WebUiMode.Normalize(null), Is.EqualTo(WebUiMode.Classic));
                Assert.That(WebUiMode.Normalize("other"), Is.EqualTo(WebUiMode.Classic));
                Assert.That(WebUiMode.IsValid("Classic"), Is.True);
                Assert.That(WebUiMode.IsValid("blazor"), Is.False);
            });
        }

        [Test]
        public async Task Classic_default_never_redirects()
        {
            GeneralConfigStatic.config.DefaultWebUi = WebUiMode.Classic;
            var (ctx, next) = await Run("/");
            Assert.That(next, Is.True);
            Assert.That(ctx.Response.StatusCode, Is.EqualTo(200));
        }

        [Test]
        public async Task App_default_redirects_a_browser_at_the_root()
        {
            GeneralConfigStatic.config.DefaultWebUi = WebUiMode.App;
            var (ctx, next) = await Run("/");
            Assert.Multiple(() =>
            {
                Assert.That(next, Is.False);
                Assert.That(ctx.Response.StatusCode, Is.EqualTo(302));
                Assert.That(ctx.Response.Headers.Location.ToString(), Is.EqualTo("/app/"));
            });
        }

        [Test]
        public async Task App_default_leaves_other_requests_alone()
        {
            GeneralConfigStatic.config.DefaultWebUi = WebUiMode.App;
            var otherPage = (await Run("/config")).nextCalled;
            var loginBounce = (await Run("/", "?returnUrl=%2Fconfig")).nextCalled;
            var json = (await Run("/", accept: "application/json")).nextCalled;
            var post = (await Run("/", method: "POST")).nextCalled;
            var classicCookie = (await Run("/", cookie: "classic")).nextCalled;
            Assert.Multiple(() =>
            {
                Assert.That(otherPage, Is.True, "other Blazor pages");
                Assert.That(loginBounce, Is.True, "Blazor login bounce");
                Assert.That(json, Is.True, "non-browser clients");
                Assert.That(post, Is.True, "non-GET");
                Assert.That(classicCookie, Is.True, "classic cookie");
            });
        }

        [Test]
        public async Task Ui_classic_sets_the_cookie_and_serves_blazor()
        {
            GeneralConfigStatic.config.DefaultWebUi = WebUiMode.App;
            var (ctx, next) = await Run("/", "?ui=classic");
            Assert.Multiple(() =>
            {
                Assert.That(next, Is.True);
                Assert.That(ctx.Response.Headers.SetCookie.ToString(), Does.StartWith($"{DefaultWebUiMiddleware.CookieName}=classic"));
            });
        }

        [Test]
        public async Task Ui_app_clears_the_cookie_and_redirects()
        {
            GeneralConfigStatic.config.DefaultWebUi = WebUiMode.Classic;
            var (ctx, next) = await Run("/", "?ui=app", cookie: "classic");
            Assert.Multiple(() =>
            {
                Assert.That(next, Is.False);
                Assert.That(ctx.Response.Headers.Location.ToString(), Is.EqualTo("/app/"));
                Assert.That(ctx.Response.Headers.SetCookie.ToString(), Does.Contain($"{DefaultWebUiMiddleware.CookieName}=;"));
            });
        }
    }
}
