using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Middleware;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services.WebAuth;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Password protection of the web UI: status, first password, sign in/out,
    /// password change/removal and the LAN bypass option.
    /// </summary>
    /// <remarks>
    /// These endpoints never need the API key nor a Telegram session. The
    /// state-changing ones must carry <c>X-Requested-With: tfm</c> unless the
    /// API key is sent. See docs/api/authentication.md, "Web session".
    /// </remarks>
    [Route("api/v1/web-auth")]
    [Tags("Web session")]
    public class WebAuthController : ApiV1ControllerBase
    {
        private readonly IWebAuthService _auth;
        private readonly ILogger<WebAuthController> _logger;

        public WebAuthController(IWebAuthService auth, ILogger<WebAuthController> logger)
        {
            _auth = auth;
            _logger = logger;
        }

        /// <summary>Whether a password is set and how this request is authenticated.</summary>
        /// <remarks>
        /// The first call of the web app. Without a password (open mode) it also
        /// issues a session cookie to a same-origin browser, so the web app can
        /// use the API even when an API key is configured (the open Blazor UI
        /// already gives the same access).
        /// </remarks>
        [HttpGet("status")]
        [ProducesResponseType(typeof(ApiResult<WebAuthStatusDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Status()
        {
            var signedIn = WebAuthSession.IsSignedIn(HttpContext);
            if (!_auth.PasswordSet && !signedIn && IsSameOriginBrowser())
            {
                await WebAuthSession.SignInAsync(HttpContext, _auth, remember: false, openMode: true);
                signedIn = true;
            }
            return OkResult(BuildStatus(signedIn));
        }

        /// <summary>Sets the first web password and signs this browser in.</summary>
        /// <response code="400">The password is shorter than 8 characters (<c>weak_password</c>).</response>
        /// <response code="409">A password is already set (<c>password_already_set</c>).</response>
        [HttpPost("setup")]
        [ProducesResponseType(typeof(ApiResult<WebAuthStatusDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Setup([FromBody] WebAuthSetupRequest request)
        {
            var result = await _auth.SetupAsync(request?.Password);
            switch (result.Outcome)
            {
                case WebAuthOutcome.AlreadySet:
                    return ConflictResult("A web password is already set", ApiErrorCodes.PasswordAlreadySet);
                case WebAuthOutcome.WeakPassword:
                    return BadRequestResult(result.Message ?? "Weak password", ApiErrorCodes.WeakPassword);
            }

            _logger.LogInformation("Web password set from {IP}", ClientIp);
            await WebAuthSession.SignInAsync(HttpContext, _auth, remember: false);
            return OkResult(BuildStatus(signedIn: true), "Password set");
        }

        /// <summary>Signs in with the web password.</summary>
        /// <response code="401">Wrong password (<c>invalid_credentials</c>).</response>
        /// <response code="409">No password is set (<c>password_not_set</c>).</response>
        /// <response code="429">Too many wrong passwords from this IP (<c>too_many_attempts</c>); see <c>Retry-After</c>.</response>
        [HttpPost("login")]
        [ProducesResponseType(typeof(ApiResult<WebAuthStatusDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> Login([FromBody] WebAuthLoginRequest request)
        {
            if (!_auth.PasswordSet)
                return ConflictResult("No web password is set", ApiErrorCodes.PasswordNotSet);

            if (TooManyAttempts(out var limited)) return limited!;

            if (!_auth.VerifyPassword(request?.Password))
            {
                RegisterFailure("login");
                return Unauthorized(ApiResult.Fail(ApiErrorCodes.InvalidCredentials, "Wrong password"));
            }

            _auth.RateLimiter.RegisterSuccess(ClientIp);
            await WebAuthSession.SignInAsync(HttpContext, _auth, request?.Remember == true);
            _logger.LogInformation("Web sign-in from {IP}", ClientIp);
            return OkResult(BuildStatus(signedIn: true));
        }

        /// <summary>Signs this browser out (removes the session cookie).</summary>
        [HttpPost("logout")]
        [ProducesResponseType(typeof(ApiResult<WebAuthStatusDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout()
        {
            await WebAuthSession.SignOutAsync(HttpContext);
            return OkResult(BuildStatus(signedIn: false));
        }

        /// <summary>Changes the web password. Signs out every other session.</summary>
        /// <response code="400">The new password is too short (<c>weak_password</c>).</response>
        /// <response code="401">Not authenticated, or wrong current password (<c>invalid_credentials</c>).</response>
        /// <response code="409">No password is set (<c>password_not_set</c>).</response>
        [HttpPost("password")]
        [ProducesResponseType(typeof(ApiResult<WebAuthStatusDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ChangePassword([FromBody] WebAuthChangePasswordRequest request)
        {
            if (!IsAuthorized()) return NotAuthenticated();
            if (!_auth.PasswordSet) return ConflictResult("No web password is set", ApiErrorCodes.PasswordNotSet);
            if (TooManyAttempts(out var limited)) return limited!;

            var persistent = await CurrentCookieIsPersistent();
            var result = await _auth.ChangePasswordAsync(request?.CurrentPassword, request?.NewPassword);
            switch (result.Outcome)
            {
                case WebAuthOutcome.InvalidCredentials:
                    RegisterFailure("password change");
                    return Unauthorized(ApiResult.Fail(ApiErrorCodes.InvalidCredentials, "Wrong current password"));
                case WebAuthOutcome.WeakPassword:
                    return BadRequestResult(result.Message ?? "Weak password", ApiErrorCodes.WeakPassword);
                case WebAuthOutcome.NotSet:
                    return ConflictResult("No web password is set", ApiErrorCodes.PasswordNotSet);
            }

            _auth.RateLimiter.RegisterSuccess(ClientIp);
            var signedIn = false;
            if (persistent.HasValue)
            {
                // Keep the caller signed in under the new security stamp.
                await WebAuthSession.SignInAsync(HttpContext, _auth, persistent.Value);
                signedIn = true;
            }
            return OkResult(BuildStatus(signedIn), "Password changed");
        }

        /// <summary>Removes the web password: the web UI is open again.</summary>
        /// <response code="401">Not authenticated, or wrong current password (<c>invalid_credentials</c>).</response>
        /// <response code="409">No password is set (<c>password_not_set</c>).</response>
        [HttpDelete("password")]
        [ProducesResponseType(typeof(ApiResult<WebAuthStatusDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> RemovePassword([FromBody] WebAuthRemovePasswordRequest request)
        {
            if (!IsAuthorized()) return NotAuthenticated();
            if (!_auth.PasswordSet) return ConflictResult("No web password is set", ApiErrorCodes.PasswordNotSet);
            if (TooManyAttempts(out var limited)) return limited!;

            var hadCookie = WebAuthSession.IsSignedIn(HttpContext);
            var result = await _auth.RemovePasswordAsync(request?.CurrentPassword);
            if (result.Outcome == WebAuthOutcome.InvalidCredentials)
            {
                RegisterFailure("password removal");
                return Unauthorized(ApiResult.Fail(ApiErrorCodes.InvalidCredentials, "Wrong current password"));
            }
            if (result.Outcome == WebAuthOutcome.NotSet)
                return ConflictResult("No web password is set", ApiErrorCodes.PasswordNotSet);

            _auth.RateLimiter.RegisterSuccess(ClientIp);
            _logger.LogWarning("Web password removed from {IP}", ClientIp);
            if (hadCookie)
                // Open mode: keep this browser able to call the API.
                await WebAuthSession.SignInAsync(HttpContext, _auth, remember: false, openMode: true);
            return OkResult(BuildStatus(hadCookie), "Password removed");
        }

        /// <summary>Turns the "no password on the local network" option on or off.</summary>
        /// <remarks>
        /// The bypass applies only to requests that reach the server directly
        /// from a loopback/private address with no proxy header, addressed to a
        /// local host name (IP, <c>localhost</c>, single label, <c>.local</c>,
        /// <c>.lan</c>...). Through a reverse proxy the password is always needed.
        /// </remarks>
        [HttpPut("settings")]
        [ProducesResponseType(typeof(ApiResult<WebAuthStatusDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Settings([FromBody] WebAuthSettingsRequest request)
        {
            if (!IsAuthorized()) return NotAuthenticated();
            if (request == null) return BadRequestResult("Body required");

            await _auth.SetLanBypassAsync(request.LanBypassEnabled);
            _logger.LogInformation("Web LAN bypass set to {Value} from {IP}", request.LanBypassEnabled, ClientIp);
            return OkResult(BuildStatus(WebAuthSession.IsSignedIn(HttpContext)));
        }

        // --- helpers -----------------------------------------------------------

        private string ClientIp
        {
            get
            {
                var ip = HttpContext.Connection.RemoteIpAddress;
                return ip == null ? "unknown" : NetworkRules.Normalize(ip).ToString();
            }
        }

        private WebAuthStatusDto BuildStatus(bool signedIn)
        {
            var request = HttpContext.GetWebAccessRequest();
            var lanActive = request != null && WebAccessPolicy.LanBypassApplies(new WebAccessRequest
            {
                Path = request.Path,
                Method = request.Method,
                Scheme = request.Scheme,
                Host = request.Host,
                Port = request.Port,
                PasswordSet = _auth.PasswordSet,
                LanBypassEnabled = _auth.LanBypassEnabled,
                TcpRemoteAddress = request.TcpRemoteAddress,
                ProxyHeadersPresent = request.ProxyHeadersPresent,
                Origin = request.Origin,
                SecFetchSite = request.SecFetchSite
            });

            return new WebAuthStatusDto
            {
                PasswordSet = _auth.PasswordSet,
                Authenticated = signedIn,
                LanBypassEnabled = _auth.LanBypassEnabled,
                LanBypassActive = lanActive
            };
        }

        /// <summary>
        /// "Authenticated" for the protected web-auth endpoints: with a password,
        /// any credential of the access policy (cookie, API key, LAN bypass);
        /// without one, the same rule as the rest of the API (cookie or key, or
        /// nothing when no API key is configured).
        /// </summary>
        private bool IsAuthorized()
        {
            var decision = HttpContext.GetWebAccess();
            if (decision == null || !decision.Allowed) return false;
            if (_auth.PasswordSet)
                return decision.Method is AccessMethod.Cookie or AccessMethod.ApiKey or AccessMethod.Lan or AccessMethod.Internal;
            return decision.Method is AccessMethod.Cookie or AccessMethod.ApiKey or AccessMethod.Internal
                   || string.IsNullOrEmpty(WebAccessMiddleware.ConfiguredApiKey());
        }

        private IActionResult NotAuthenticated() =>
            Unauthorized(ApiResult.Fail(ApiErrorCodes.Unauthorized, "Authentication required"));

        private bool TooManyAttempts(out IActionResult? result)
        {
            result = null;
            if (_auth.RateLimiter.IsAllowed(ClientIp, out var retryAfter)) return false;

            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            Response.Headers.RetryAfter = seconds.ToString();
            result = StatusCode(StatusCodes.Status429TooManyRequests,
                ApiResult.Fail(ApiErrorCodes.TooManyAttempts, "Too many wrong passwords, try again later", $"retryAfterSeconds={seconds}"));
            return true;
        }

        private void RegisterFailure(string operation)
        {
            var locked = _auth.RateLimiter.RegisterFailure(ClientIp);
            _logger.LogWarning("Wrong web password ({Operation}) from {IP}{Locked}", operation, ClientIp,
                locked ? "; IP temporarily locked out" : string.Empty);
        }

        /// <summary>Null when the caller has no cookie session, else whether it is persistent.</summary>
        private async Task<bool?> CurrentCookieIsPersistent()
        {
            if (!WebAuthSession.IsSignedIn(HttpContext)) return null;
            var result = await HttpContext.AuthenticateAsync(WebAuthSession.Scheme);
            return result.Succeeded ? result.Properties?.IsPersistent ?? false : null;
        }

        /// <summary>A request made by a page of this server (or typed in the address bar).</summary>
        private bool IsSameOriginBrowser()
        {
            var site = Request.Headers["Sec-Fetch-Site"].ToString();
            if (!string.IsNullOrEmpty(site))
                return site.Equals("same-origin", StringComparison.OrdinalIgnoreCase)
                       || site.Equals("none", StringComparison.OrdinalIgnoreCase);
            var origin = Request.Headers.Origin.ToString();
            return string.IsNullOrEmpty(origin)
                   || WebAccessPolicy.IsSameOrigin(origin, Request.Scheme, Request.Host.Host, Request.Host.Port);
        }
    }
}
