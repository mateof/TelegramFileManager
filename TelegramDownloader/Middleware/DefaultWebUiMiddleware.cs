using TelegramDownloader.Models;

namespace TelegramDownloader.Middleware
{
    /// <summary>
    /// Applies the "default web interface" setting
    /// (<see cref="GeneralConfig.DefaultWebUi"/>) to the server root only:
    /// when it is <c>app</c>, a browser navigation to <c>/</c> is redirected to
    /// <c>/app/</c>. Other Blazor pages (<c>/config</c>, <c>/fm/…</c>) are never
    /// redirected.
    /// <list type="bullet">
    /// <item><c>/?ui=classic</c> stores the <c>tfm_ui=classic</c> cookie and
    /// serves the Blazor UI; that browser keeps getting it at <c>/</c>.</item>
    /// <item><c>/?ui=app</c> removes the cookie and goes to <c>/app/</c>.</item>
    /// </list>
    /// </summary>
    public class DefaultWebUiMiddleware
    {
        public const string CookieName = "tfm_ui";
        public const string QueryName = "ui";
        public const string AppPath = "/app/";

        private readonly RequestDelegate _next;

        public DefaultWebUiMiddleware(RequestDelegate next) => _next = next;

        public Task InvokeAsync(HttpContext context)
        {
            var request = context.Request;
            if (!IsRootNavigation(request))
                return _next(context);

            var choice = request.Query[QueryName].ToString();
            if (string.Equals(choice, WebUiMode.Classic, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Cookies.Append(CookieName, WebUiMode.Classic, new CookieOptions
                {
                    Path = "/",
                    HttpOnly = true,
                    Secure = request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    MaxAge = TimeSpan.FromDays(365)
                });
                return _next(context);
            }

            if (string.Equals(choice, WebUiMode.App, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
                return RedirectToApp(context);
            }

            // Only the bare root: "/?returnUrl=..." is the Blazor login bounce of
            // a Blazor page the user asked for, which must stay in Blazor.
            if (!request.QueryString.HasValue &&
                ShouldRedirect(GeneralConfigStatic.config?.DefaultWebUi, request.Cookies[CookieName], request.Headers.Accept.ToString()))
                return RedirectToApp(context);

            return _next(context);
        }

        /// <summary>GET/HEAD of exactly <c>/</c>.</summary>
        public static bool IsRootNavigation(HttpRequest request) =>
            (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) &&
            (request.Path == "/" || !request.Path.HasValue);

        /// <summary>
        /// Redirect when the default is <c>app</c>, the browser did not opt into
        /// the classic UI and the request is a page navigation (accepts HTML),
        /// so health checks and API clients probing <c>/</c> are left alone.
        /// </summary>
        public static bool ShouldRedirect(string? defaultWebUi, string? cookie, string? accept) =>
            WebUiMode.Normalize(defaultWebUi) == WebUiMode.App &&
            !string.Equals(cookie, WebUiMode.Classic, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(accept) &&
            accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);

        private static Task RedirectToApp(HttpContext context)
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Redirect(context.Request.PathBase + AppPath);
            return Task.CompletedTask;
        }
    }
}
