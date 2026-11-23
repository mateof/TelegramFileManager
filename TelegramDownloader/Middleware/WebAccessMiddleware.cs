using System.Net;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services.WebAuth;

namespace TelegramDownloader.Middleware
{
    /// <summary>
    /// What the request looked like on arrival, before
    /// <c>UseForwardedHeaders</c> rewrote the remote address and removed the
    /// proxy headers it consumed. The LAN rule must see these original values.
    /// </summary>
    public sealed record OriginalConnectionInfo(IPAddress? RemoteAddress, bool ProxyHeadersPresent);

    /// <summary>
    /// Captures <see cref="OriginalConnectionInfo"/>. Must run before
    /// <c>UseForwardedHeaders</c>.
    /// </summary>
    public class ConnectionInfoCaptureMiddleware
    {
        public const string ItemKey = "tfm.originalConnection";
        private readonly RequestDelegate _next;

        public ConnectionInfoCaptureMiddleware(RequestDelegate next) => _next = next;

        public Task InvokeAsync(HttpContext context)
        {
            context.Items[ItemKey] = new OriginalConnectionInfo(
                context.Connection.RemoteIpAddress,
                NetworkRules.HasProxyHeaders(context.Request.Headers));
            return _next(context);
        }
    }

    /// <summary>
    /// The access gate of the whole server (it replaces the former
    /// ApiKeyMiddleware). It asks <see cref="WebAccessPolicy"/> and, when the
    /// request is not allowed, answers 401/403 or redirects a browser
    /// navigation of the Blazor UI to the web app login page.
    /// </summary>
    public class WebAccessMiddleware
    {
        public const string DecisionItemKey = "tfm.webAccess";
        public const string RequestItemKey = "tfm.webAccessRequest";
        public const string ApiKeyHeader = "X-Api-Key";
        public const string LoginPath = "/app/login";

        private readonly RequestDelegate _next;
        private readonly ILogger<WebAccessMiddleware> _logger;

        public WebAccessMiddleware(RequestDelegate next, ILogger<WebAccessMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>Effective API key: the value managed from the Config UI (Mongo), else config.json.</summary>
        public static string? ConfiguredApiKey() =>
            !string.IsNullOrEmpty(GeneralConfigStatic.config?.MobileApiKey)
                ? GeneralConfigStatic.config.MobileApiKey
                : GeneralConfigStatic.tlconfig?.mobile_api_key;

        /// <summary>
        /// API key sent by the client: <c>X-Api-Key</c> header, then the
        /// <c>apiKey</c> query parameter (media elements, download managers),
        /// <c>access_token</c> (SignalR WebSocket) and a Bearer header (SignalR
        /// negotiate).
        /// </summary>
        public static string? ProvidedApiKey(HttpRequest request)
        {
            if (request.Headers.TryGetValue(ApiKeyHeader, out var header) && !string.IsNullOrEmpty(header))
                return header.ToString();
            if (request.Query.TryGetValue("apiKey", out var query) && !string.IsNullOrEmpty(query))
                return query.ToString();
            if (request.Query.TryGetValue("access_token", out var token) && !string.IsNullOrEmpty(token))
                return token.ToString();
            if (request.Headers.TryGetValue("Authorization", out var auth))
            {
                var value = auth.ToString();
                if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return value["Bearer ".Length..].Trim();
            }
            return null;
        }

        public static WebAccessRequest BuildRequest(HttpContext context, IWebAuthService auth)
        {
            var original = context.Items.TryGetValue(ConnectionInfoCaptureMiddleware.ItemKey, out var o) && o is OriginalConnectionInfo info
                ? info
                // Capture middleware missing: be conservative, never local.
                : new OriginalConnectionInfo(context.Connection.RemoteIpAddress, true);

            var req = context.Request;
            return new WebAccessRequest
            {
                Path = (req.PathBase + req.Path).Value ?? "/",
                Method = req.Method,
                Scheme = req.Scheme,
                Host = req.Host.Host,
                Port = req.Host.Port,
                PasswordSet = auth.PasswordSet,
                LanBypassEnabled = auth.LanBypassEnabled,
                ConfiguredApiKey = ConfiguredApiKey(),
                ProvidedApiKey = ProvidedApiKey(req),
                InternalTokenValid = auth.IsInternalToken(req.Headers[WebAuthService.InternalTokenHeader].ToString()),
                CookieAuthenticated = WebAuthSession.IsSignedIn(context),
                TcpRemoteAddress = original.RemoteAddress,
                ProxyHeadersPresent = original.ProxyHeadersPresent,
                Origin = req.Headers.Origin.ToString(),
                SecFetchSite = req.Headers["Sec-Fetch-Site"].ToString(),
                RequestedWith = req.Headers[WebAccessPolicy.CsrfHeaderName].ToString()
            };
        }

        public async Task InvokeAsync(HttpContext context, IWebAuthService auth)
        {
            var request = BuildRequest(context, auth);
            var decision = WebAccessPolicy.Evaluate(request);
            context.Items[DecisionItemKey] = decision;
            context.Items[RequestItemKey] = request;

            if (decision.Allowed)
            {
                await _next(context);
                return;
            }

            var ip = context.Connection.RemoteIpAddress;
            switch (decision.Failure)
            {
                case AccessFailure.RedirectToLogin:
                    var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                    context.Response.Redirect($"{LoginPath}?returnUrl={Uri.EscapeDataString(returnUrl)}");
                    return;

                case AccessFailure.CsrfHeaderMissing:
                    _logger.LogWarning("Rejected {Method} {Path} from {IP}: missing {Header} header",
                        context.Request.Method, context.Request.Path, ip, WebAccessPolicy.CsrfHeaderName);
                    await WriteError(context, StatusCodes.Status403Forbidden, ApiErrorCodes.CsrfHeaderMissing,
                        "Missing anti-forgery header",
                        $"Send '{WebAccessPolicy.CsrfHeaderName}: {WebAccessPolicy.CsrfHeaderValue}' on state-changing requests");
                    return;

                case AccessFailure.InvalidApiKey:
                    _logger.LogWarning("Invalid API key attempt from {IP} on {Path}", ip, context.Request.Path);
                    await WriteError(context, StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "Invalid API key", null);
                    return;

                case AccessFailure.ApiKeyRequired:
                    _logger.LogWarning("API request without API key from {IP}", ip);
                    await WriteError(context, StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "API key required",
                        $"Provide your API key in the {ApiKeyHeader} header, or in the apiKey/access_token query parameter");
                    return;

                default:
                    _logger.LogDebug("Unauthenticated request to {Path} from {IP}", context.Request.Path, ip);
                    await WriteError(context, StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "Authentication required",
                        $"Sign in through /api/v1/web-auth/login, or provide your API key in the {ApiKeyHeader} header or the apiKey query parameter");
                    return;
            }
        }

        /// <summary>
        /// The v1 API and everything else use the v1 envelope (<c>error</c> is an
        /// object with a machine-readable <c>code</c>); <c>/api/mobile</c> keeps
        /// its original flat shape so the existing audio app is not broken.
        /// </summary>
        private static async Task WriteError(HttpContext context, int status, string code, string error, string? detail)
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";

            if (context.Request.Path.StartsWithSegments("/api/mobile"))
            {
                await context.Response.WriteAsJsonAsync(new { success = false, error, message = detail });
                return;
            }

            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = new { code, message = error, detail }
            });
        }
    }

    public static class WebAccessHttpContextExtensions
    {
        /// <summary>The decision the access middleware took for this request (null if it did not run).</summary>
        public static WebAccessDecision? GetWebAccess(this HttpContext context) =>
            context.Items.TryGetValue(WebAccessMiddleware.DecisionItemKey, out var d) ? d as WebAccessDecision : null;

        public static WebAccessRequest? GetWebAccessRequest(this HttpContext context) =>
            context.Items.TryGetValue(WebAccessMiddleware.RequestItemKey, out var r) ? r as WebAccessRequest : null;
    }
}
