using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace TelegramDownloader.Services.WebAuth
{
    /// <summary>What a path is, as far as access control is concerned.</summary>
    public enum WebPathKind
    {
        /// <summary>Static assets of the UIs, the new web app shell, the error page.</summary>
        Public,
        /// <summary><c>/api/v1/web-auth/**</c>: always reachable, the controller checks what it needs.</summary>
        WebAuth,
        /// <summary><c>/api/v1/system/ping</c>.</summary>
        Ping,
        /// <summary><c>/api/v1/**</c>, <c>/api/mobile/**</c>, <c>/hubs/**</c>: API key protected even without a password.</summary>
        Api,
        /// <summary>Legacy controllers (<c>/api/file</c>, <c>/api/temp</c>, ...), <c>/local/**</c>, <c>/img/telegram/**</c>.</summary>
        Legacy,
        /// <summary>Swagger UI and documents.</summary>
        Docs,
        /// <summary>The Blazor Server circuit hub.</summary>
        BlazorHub,
        /// <summary>Any other path: rendered by the Blazor host page.</summary>
        BlazorPage,
        /// <summary><c>/webdav/**</c>: keeps its own HTTP Basic authentication.</summary>
        WebDav
    }

    /// <summary>How a request was let in.</summary>
    public enum AccessMethod
    {
        None,
        /// <summary>No password set and nothing else required (pre-password behaviour).</summary>
        Open,
        /// <summary>Valid API key (header, query or bearer).</summary>
        ApiKey,
        /// <summary>Server-side self call from the Blazor UI (per-process secret).</summary>
        Internal,
        /// <summary>Valid web session cookie.</summary>
        Cookie,
        /// <summary>LAN bypass: genuinely local request.</summary>
        Lan
    }

    public enum AccessFailure
    {
        None,
        /// <summary>No password set, API key configured and missing.</summary>
        ApiKeyRequired,
        /// <summary>An API key was provided and it is wrong.</summary>
        InvalidApiKey,
        /// <summary>Password set: sign in (cookie) or provide the API key.</summary>
        LoginRequired,
        /// <summary>Same as <see cref="LoginRequired"/> for a browser navigation to the Blazor UI.</summary>
        RedirectToLogin,
        /// <summary>Cookie/LAN authenticated state-changing request without <c>X-Requested-With: tfm</c>.</summary>
        CsrfHeaderMissing
    }

    /// <summary>The facts about one request the policy decides on.</summary>
    public sealed class WebAccessRequest
    {
        public string Path { get; init; } = "/";
        public string Method { get; init; } = "GET";
        public string Scheme { get; init; } = "http";
        /// <summary>Host header (host name, without port).</summary>
        public string? Host { get; init; }
        public int? Port { get; init; }

        public bool PasswordSet { get; init; }
        public bool LanBypassEnabled { get; init; }
        /// <summary>Effective configured API key (Mongo value, else config.json). Empty = none.</summary>
        public string? ConfiguredApiKey { get; init; }
        /// <summary>Key the client sent (header, <c>apiKey</c>/<c>access_token</c> query or bearer).</summary>
        public string? ProvidedApiKey { get; init; }
        public bool InternalTokenValid { get; init; }
        public bool CookieAuthenticated { get; init; }

        /// <summary>Socket peer address, captured before forwarded headers are applied.</summary>
        public IPAddress? TcpRemoteAddress { get; init; }
        /// <summary>Whether any proxy header was present on arrival.</summary>
        public bool ProxyHeadersPresent { get; init; }
        public string? Origin { get; init; }
        public string? SecFetchSite { get; init; }
        public string? RequestedWith { get; init; }
    }

    public sealed record WebAccessDecision(bool Allowed, AccessMethod Method, AccessFailure Failure, WebPathKind Kind)
    {
        public static WebAccessDecision Allow(AccessMethod method, WebPathKind kind) => new(true, method, AccessFailure.None, kind);
        public static WebAccessDecision Deny(AccessFailure failure, WebPathKind kind) => new(false, AccessMethod.None, failure, kind);
    }

    /// <summary>
    /// The single access policy of the server. Every gate (the middleware in
    /// front of the API, hubs, legacy media, static user files, Swagger and the
    /// Blazor UI, and the web-auth endpoints) asks this class.
    ///
    /// Without a web password everything behaves as before: the Blazor UI and
    /// legacy endpoints are open, <c>/api/v1</c>, <c>/api/mobile</c> and
    /// <c>/hubs</c> need the API key when one is configured.
    ///
    /// With a password a request gets in with (a) a valid session cookie, (b) a
    /// valid API key, or (c) the LAN bypass when enabled and the request is
    /// genuinely local (see <see cref="LanBypassApplies"/>).
    /// </summary>
    public static class WebAccessPolicy
    {
        public const string CsrfHeaderName = "X-Requested-With";
        public const string CsrfHeaderValue = "tfm";

        /// <summary>
        /// Legacy endpoints the Blazor UI calls with plain HTML forms or
        /// Dropzone (uploads, the file manager download form), which cannot add
        /// the CSRF header. They rely on SameSite=Strict (cookie) and on the
        /// same-origin checks of the LAN rule.
        /// </summary>
        private static readonly string[] FormPostPrefixes = { "/api/file", "/api/temp" };

        public static WebPathKind Classify(string path)
        {
            var p = new PathString(string.IsNullOrEmpty(path) ? "/" : path);

            if (p.StartsWithSegments("/api/v1/web-auth", StringComparison.OrdinalIgnoreCase)) return WebPathKind.WebAuth;
            if (p.StartsWithSegments("/api/v1/system/ping", StringComparison.OrdinalIgnoreCase)) return WebPathKind.Ping;
            if (p.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase)
                || p.StartsWithSegments("/api/mobile", StringComparison.OrdinalIgnoreCase)
                || p.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
                return WebPathKind.Api;
            if (p.StartsWithSegments("/webdav", StringComparison.OrdinalIgnoreCase)) return WebPathKind.WebDav;
            if (p.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                || p.StartsWithSegments("/local", StringComparison.OrdinalIgnoreCase)
                || p.StartsWithSegments("/img/telegram", StringComparison.OrdinalIgnoreCase))
                return WebPathKind.Legacy;
            if (p.StartsWithSegments("/api-docs", StringComparison.OrdinalIgnoreCase)
                || p.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase))
                return WebPathKind.Docs;
            if (p.StartsWithSegments("/_blazor", StringComparison.OrdinalIgnoreCase)) return WebPathKind.BlazorHub;
            if (p.StartsWithSegments("/app", StringComparison.OrdinalIgnoreCase)
                || p.StartsWithSegments("/_framework", StringComparison.OrdinalIgnoreCase)
                || p.StartsWithSegments("/_content", StringComparison.OrdinalIgnoreCase)
                || p.StartsWithSegments("/Error", StringComparison.OrdinalIgnoreCase))
                return WebPathKind.Public;

            // Static assets of wwwroot (css, js, images, fonts...). The Blazor
            // fallback route only matches paths without a file extension
            // ({*path:nonfile}), so a path with an extension can never render a
            // page of the UI.
            var last = p.Value!.TrimEnd('/');
            var slash = last.LastIndexOf('/');
            var segment = slash >= 0 ? last[(slash + 1)..] : last;
            if (segment.Contains('.')) return WebPathKind.Public;

            return WebPathKind.BlazorPage;
        }

        public static bool IsSafeMethod(string method) =>
            HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);

        /// <summary>Constant-time comparison of the provided and configured API keys.</summary>
        public static bool ApiKeyMatches(string? configured, string? provided)
        {
            if (string.IsNullOrEmpty(configured) || string.IsNullOrEmpty(provided)) return false;
            var a = Encoding.UTF8.GetBytes(configured);
            var b = Encoding.UTF8.GetBytes(provided);
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        /// <summary>
        /// Whether the LAN bypass lets this request in without the password:
        /// the bypass is enabled (and a password is set), the request is
        /// genuinely local (<see cref="NetworkRules.IsGenuinelyLocal"/>), it is
        /// addressed to a local host name (no DNS rebinding) and it is not a
        /// cross-site request made by some other web page in a LAN browser
        /// (the CORS policy is open, so without this check any site could drive
        /// the API through the bypass).
        /// </summary>
        public static bool LanBypassApplies(WebAccessRequest r)
        {
            if (!r.PasswordSet || !r.LanBypassEnabled) return false;
            if (!NetworkRules.IsGenuinelyLocal(r.TcpRemoteAddress, r.ProxyHeadersPresent)) return false;
            if (!NetworkRules.IsLocalHostName(r.Host)) return false;

            if (!string.IsNullOrEmpty(r.SecFetchSite)
                && !r.SecFetchSite.Equals("same-origin", StringComparison.OrdinalIgnoreCase)
                && !r.SecFetchSite.Equals("none", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(r.Origin) && !IsSameOrigin(r.Origin, r.Scheme, r.Host, r.Port))
                return false;

            return true;
        }

        /// <summary>Whether an <c>Origin</c> header designates this server.</summary>
        public static bool IsSameOrigin(string origin, string scheme, string? host, int? port)
        {
            if (string.IsNullOrEmpty(host)) return false;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            var expectedPort = port ?? (scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);
            var h = host.Trim('[', ']');
            return uri.Host.Trim('[', ']').Equals(h, StringComparison.OrdinalIgnoreCase) && uri.Port == expectedPort;
        }

        public static WebAccessDecision Evaluate(WebAccessRequest r)
        {
            var kind = Classify(r.Path);
            if (kind == WebPathKind.Public || kind == WebPathKind.WebDav)
                return WebAccessDecision.Allow(AccessMethod.Open, kind);

            var keyProvided = !string.IsNullOrEmpty(r.ProvidedApiKey);
            var keyConfigured = !string.IsNullOrEmpty(r.ConfiguredApiKey);
            var keyValid = keyProvided && ApiKeyMatches(r.ConfiguredApiKey, r.ProvidedApiKey);
            var keyInvalid = keyProvided && keyConfigured && !keyValid;

            WebAccessDecision decision;
            if (!r.PasswordSet)
                decision = EvaluateOpenMode(r, kind, keyConfigured, keyValid, keyInvalid);
            else
                decision = EvaluatePasswordMode(r, kind, keyValid, keyInvalid);

            if (!decision.Allowed) return decision;
            return CheckCsrf(r, decision);
        }

        /// <summary>Pre-password behaviour, plus the cookie of the new web app.</summary>
        private static WebAccessDecision EvaluateOpenMode(WebAccessRequest r, WebPathKind kind,
            bool keyConfigured, bool keyValid, bool keyInvalid)
        {
            if (kind != WebPathKind.Api && kind != WebPathKind.Ping && kind != WebPathKind.WebAuth)
                return WebAccessDecision.Allow(AccessMethod.Open, kind);

            if (r.InternalTokenValid) return WebAccessDecision.Allow(AccessMethod.Internal, kind);
            if (keyValid) return WebAccessDecision.Allow(AccessMethod.ApiKey, kind);
            if (r.CookieAuthenticated) return WebAccessDecision.Allow(AccessMethod.Cookie, kind);
            if (kind == WebPathKind.WebAuth || !keyConfigured) return WebAccessDecision.Allow(AccessMethod.Open, kind);

            return WebAccessDecision.Deny(keyInvalid ? AccessFailure.InvalidApiKey : AccessFailure.ApiKeyRequired, kind);
        }

        private static WebAccessDecision EvaluatePasswordMode(WebAccessRequest r, WebPathKind kind, bool keyValid, bool keyInvalid)
        {
            AccessMethod method;
            if (r.InternalTokenValid) method = AccessMethod.Internal;
            else if (keyValid) method = AccessMethod.ApiKey;
            else if (keyInvalid && kind != WebPathKind.WebAuth)
                // A wrong key is reported as such (apps show "invalid API key")
                // even if the request would have passed by another rule.
                return WebAccessDecision.Deny(AccessFailure.InvalidApiKey, kind);
            else if (r.CookieAuthenticated) method = AccessMethod.Cookie;
            else if (LanBypassApplies(r)) method = AccessMethod.Lan;
            else method = AccessMethod.None;

            // The web-auth endpoints and the liveness probe are reachable by
            // anyone; the web-auth controller enforces "authenticated" itself.
            if (kind == WebPathKind.WebAuth || kind == WebPathKind.Ping)
                return WebAccessDecision.Allow(method, kind);

            if (method != AccessMethod.None)
                return WebAccessDecision.Allow(method, kind);

            if (kind == WebPathKind.BlazorPage && (HttpMethods.IsGet(r.Method) || HttpMethods.IsHead(r.Method)))
                return WebAccessDecision.Deny(AccessFailure.RedirectToLogin, kind);

            return WebAccessDecision.Deny(AccessFailure.LoginRequired, kind);
        }

        /// <summary>
        /// CSRF rule: a state-changing request to <c>/api/**</c> or <c>/hubs/**</c>
        /// that relies on ambient browser authority (cookie or LAN bypass), and
        /// every state-changing web-auth call not made with the API key, must
        /// carry <c>X-Requested-With: tfm</c>. Browsers only let a page add a
        /// custom header to a cross-origin request after a CORS preflight, and
        /// with SameSite=Strict the cookie never travels on cross-site requests,
        /// so the pair is a sufficient defence. API-key and internal requests
        /// are exempt (the key itself proves intent).
        /// </summary>
        private static WebAccessDecision CheckCsrf(WebAccessRequest r, WebAccessDecision decision)
        {
            if (IsSafeMethod(r.Method)) return decision;

            var ambient = decision.Method == AccessMethod.Cookie || decision.Method == AccessMethod.Lan
                          || (decision.Kind == WebPathKind.WebAuth
                              && decision.Method != AccessMethod.ApiKey && decision.Method != AccessMethod.Internal);
            if (!ambient) return decision;

            var p = new PathString(r.Path);
            var underApi = p.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                           || p.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase);
            if (!underApi) return decision;
            if (FormPostPrefixes.Any(f => p.StartsWithSegments(f, StringComparison.OrdinalIgnoreCase))) return decision;

            if (string.Equals(r.RequestedWith, CsrfHeaderValue, StringComparison.OrdinalIgnoreCase))
                return decision;

            return WebAccessDecision.Deny(AccessFailure.CsrfHeaderMissing, decision.Kind);
        }
    }
}
