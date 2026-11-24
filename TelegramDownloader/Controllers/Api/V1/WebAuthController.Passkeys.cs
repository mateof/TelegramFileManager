using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Middleware;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services.WebAuth;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Passkeys (WebAuthn): an extra way to sign in while a web password is
    /// set. Login is reachable without credentials; registration and
    /// management need a signed-in browser session (cookie), and registration
    /// also the current password.
    /// </summary>
    public partial class WebAuthController
    {
        /// <summary>Starts adding a passkey: checks the current password and returns the creation options.</summary>
        /// <remarks>
        /// Needs a browser session (cookie): not the API key, not the LAN bypass.
        /// Pass <c>data.options</c> to <c>PublicKeyCredential.parseCreationOptionsFromJSON()</c>
        /// and <c>navigator.credentials.create()</c>, then call <c>register/finish</c>
        /// with <c>data.requestId</c> within 5 minutes.
        /// </remarks>
        /// <response code="400">This host cannot use passkeys, e.g. an IP address (<c>passkey_unsupported</c>).</response>
        /// <response code="401">Wrong current password (<c>invalid_credentials</c>).</response>
        /// <response code="403">No browser session (<c>session_required</c>).</response>
        /// <response code="409">No password is set (<c>password_not_set</c>).</response>
        /// <response code="429">Too many wrong passwords (<c>too_many_attempts</c>).</response>
        [HttpPost("passkeys/register/begin")]
        [ProducesResponseType(typeof(ApiResult<WebPasskeyBeginDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> PasskeyRegisterBegin([FromBody] WebPasskeyRegisterBeginRequest request)
        {
            if (!IsBrowserSession(out var denied)) return denied!;
            if (TooManyAttempts(out var limited)) return limited!;

            var (result, begin) = await _passkeys.BeginRegistrationAsync(request?.CurrentPassword, RelyingParty());
            if (result.Outcome == PasskeyOutcome.InvalidCredentials)
            {
                RegisterFailure("passkey registration");
                return Unauthorized(ApiResult.Fail(ApiErrorCodes.InvalidCredentials, "Wrong current password"));
            }
            if (!result.Succeeded) return PasskeyError(result);
            _auth.RateLimiter.RegisterSuccess(ClientIp);
            return OkResult(ToBeginDto(begin!));
        }

        /// <summary>Finishes adding a passkey: verifies the authenticator response and stores the credential.</summary>
        /// <response code="400">Expired/used request (<c>passkey_challenge_invalid</c>) or a response that fails verification (<c>passkey_invalid</c>).</response>
        /// <response code="403">No browser session (<c>session_required</c>).</response>
        /// <response code="409">Already registered (<c>passkey_exists</c>), or no password (<c>password_not_set</c>).</response>
        [HttpPost("passkeys/register/finish")]
        [ProducesResponseType(typeof(ApiResult<WebPasskeyDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> PasskeyRegisterFinish([FromBody] WebPasskeyRegisterFinishRequest request, CancellationToken ct)
        {
            if (!IsBrowserSession(out var denied)) return denied!;
            if (request == null) return BadRequestResult("Body required");

            var result = await _passkeys.FinishRegistrationAsync(request.RequestId, request.Name, request.Credential, RelyingParty(), ct);
            if (!result.Succeeded) return PasskeyError(result);
            _logger.LogInformation("Passkey registered from {IP}", ClientIp);
            return OkResult(ToDto(result.Passkey!), "Passkey added");
        }

        /// <summary>Starts a passkey sign-in (usernameless: empty <c>allowCredentials</c>).</summary>
        /// <remarks>
        /// Reachable without credentials. Pass <c>data.options</c> to
        /// <c>PublicKeyCredential.parseRequestOptionsFromJSON()</c> and
        /// <c>navigator.credentials.get()</c> (also with <c>mediation: "conditional"</c>
        /// for autofill), then call <c>login/finish</c>.
        /// </remarks>
        /// <response code="400">This host cannot use passkeys (<c>passkey_unsupported</c>).</response>
        /// <response code="409">No password is set (<c>password_not_set</c>).</response>
        /// <response code="429">Too many failed sign-ins (<c>too_many_attempts</c>).</response>
        [HttpPost("passkeys/login/begin")]
        [ProducesResponseType(typeof(ApiResult<WebPasskeyBeginDto>), StatusCodes.Status200OK)]
        public IActionResult PasskeyLoginBegin()
        {
            if (TooManyAttempts(out var limited)) return limited!;
            var (result, begin) = _passkeys.BeginLogin(RelyingParty());
            if (!result.Succeeded) return PasskeyError(result);
            return OkResult(ToBeginDto(begin!));
        }

        /// <summary>Finishes a passkey sign-in and sets the same session cookie as the password login.</summary>
        /// <response code="400">Expired/used request (<c>passkey_challenge_invalid</c>).</response>
        /// <response code="401">Unknown passkey or failed verification (<c>passkey_invalid</c>); counts as a failed sign-in.</response>
        /// <response code="409">No password is set (<c>password_not_set</c>).</response>
        /// <response code="429">Too many failed sign-ins (<c>too_many_attempts</c>).</response>
        [HttpPost("passkeys/login/finish")]
        [ProducesResponseType(typeof(ApiResult<WebAuthStatusDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> PasskeyLoginFinish([FromBody] WebPasskeyLoginFinishRequest request, CancellationToken ct)
        {
            if (TooManyAttempts(out var limited)) return limited!;
            if (request == null) return BadRequestResult("Body required");

            var result = await _passkeys.FinishLoginAsync(request.RequestId, request.Credential, RelyingParty(), ct);
            if (result.Outcome == PasskeyOutcome.Invalid)
            {
                RegisterFailure("passkey sign-in");
                return Unauthorized(ApiResult.Fail(ApiErrorCodes.PasskeyInvalid, "The passkey was not accepted", result.Message));
            }
            if (!result.Succeeded) return PasskeyError(result);

            _auth.RateLimiter.RegisterSuccess(ClientIp);
            await WebAuthSession.SignInAsync(HttpContext, _auth, request.Remember);
            _logger.LogInformation("Web sign-in with a passkey from {IP}", ClientIp);
            return OkResult(BuildStatus(signedIn: true));
        }

        /// <summary>Registered passkeys and the relying party (domain) used for this request.</summary>
        /// <response code="403">No browser session (<c>session_required</c>).</response>
        [HttpGet("passkeys")]
        [ProducesResponseType(typeof(ApiResult<WebPasskeyListDto>), StatusCodes.Status200OK)]
        public IActionResult PasskeyList()
        {
            if (!IsBrowserSession(out var denied)) return denied!;
            return OkResult(BuildPasskeyList());
        }

        /// <summary>Renames a passkey.</summary>
        /// <response code="403">No browser session (<c>session_required</c>).</response>
        /// <response code="404">Unknown passkey.</response>
        [HttpPut("passkeys/{id}")]
        [ProducesResponseType(typeof(ApiResult<WebPasskeyDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> PasskeyRename(string id, [FromBody] WebPasskeyRenameRequest request)
        {
            if (!IsBrowserSession(out var denied)) return denied!;
            var result = await _passkeys.RenameAsync(id, request?.Name);
            if (!result.Succeeded) return PasskeyError(result);
            return OkResult(ToDto(result.Passkey!), "Passkey renamed");
        }

        /// <summary>Removes a passkey. It also has to be removed from the authenticator / password manager.</summary>
        /// <response code="403">No browser session (<c>session_required</c>).</response>
        /// <response code="404">Unknown passkey.</response>
        [HttpDelete("passkeys/{id}")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> PasskeyDelete(string id)
        {
            if (!IsBrowserSession(out var denied)) return denied!;
            var result = await _passkeys.DeleteAsync(id);
            if (!result.Succeeded) return PasskeyError(result);
            _logger.LogWarning("Passkey removed from {IP}", ClientIp);
            return OkEmpty("Passkey removed");
        }

        /// <summary>Sets the relying party override (reverse proxy setups).</summary>
        /// <remarks>
        /// Normally not needed: the RP ID is the host name the browser uses and the
        /// origin is the request origin (behind a proxy listed in <c>TrustedProxies</c>,
        /// from <c>X-Forwarded-Host</c>/<c>-Proto</c>). Passkeys only work on the
        /// RP ID they were created for: changing it makes existing ones unusable.
        /// </remarks>
        /// <response code="400">Invalid RP ID or origin (<c>invalid_request</c>).</response>
        /// <response code="403">No browser session (<c>session_required</c>).</response>
        [HttpPut("passkeys/settings")]
        [ProducesResponseType(typeof(ApiResult<WebPasskeyListDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> PasskeySettings([FromBody] WebPasskeySettingsRequest request)
        {
            if (!IsBrowserSession(out var denied)) return denied!;
            if (request == null) return BadRequestResult("Body required");
            var result = await _passkeys.SetRelyingPartyAsync(request.RpId, request.Origins);
            if (!result.Succeeded) return PasskeyError(result);
            return OkResult(BuildPasskeyList());
        }

        // --- helpers -----------------------------------------------------------

        /// <summary>The relying party of this request (after the trusted forwarded headers).</summary>
        private PasskeyRelyingParty RelyingParty() =>
            PasskeyRelyingParty.Resolve(Request.Scheme, Request.Host.Host, Request.Host.Port, _auth.PasskeyRpId, _auth.PasskeyOrigins);

        /// <summary>
        /// Passkey registration and management need a signed-in browser: a
        /// password-mode session cookie. The API key, the LAN bypass and the
        /// internal token are not enough (an app or a LAN neighbour must not be
        /// able to add a way in).
        /// </summary>
        private bool IsBrowserSession(out IActionResult? denied)
        {
            denied = null;
            if (!_auth.PasswordSet)
            {
                denied = ConflictResult("No web password is set", ApiErrorCodes.PasswordNotSet);
                return false;
            }
            var decision = HttpContext.GetWebAccess();
            var mode = User?.FindFirst(WebAuthSession.ModeClaim)?.Value;
            if (decision is { Allowed: true, Method: AccessMethod.Cookie } && WebAuthSession.IsSignedIn(HttpContext) && mode == "password")
                return true;

            denied = StatusCode(StatusCodes.Status403Forbidden,
                ApiResult.Fail(ApiErrorCodes.SessionRequired, "Sign in to the web app with the password to manage passkeys"));
            return false;
        }

        private IActionResult PasskeyError(PasskeyResult result) => result.Outcome switch
        {
            PasskeyOutcome.PasswordNotSet => ConflictResult(result.Message ?? "No web password is set", ApiErrorCodes.PasswordNotSet),
            PasskeyOutcome.Unsupported => BadRequestResult(result.Message ?? "Passkeys are not available on this host", ApiErrorCodes.PasskeyUnsupported),
            PasskeyOutcome.ChallengeInvalid => BadRequestResult(result.Message ?? "The passkey request expired, try again", ApiErrorCodes.PasskeyChallengeInvalid),
            PasskeyOutcome.AlreadyRegistered => ConflictResult(result.Message ?? "This passkey is already registered", ApiErrorCodes.PasskeyExists),
            PasskeyOutcome.NotFound => NotFoundResult(result.Message ?? "Passkey not found"),
            PasskeyOutcome.InvalidSettings => BadRequestResult(result.Message ?? "Invalid settings"),
            PasskeyOutcome.InvalidCredentials => Unauthorized(ApiResult.Fail(ApiErrorCodes.InvalidCredentials, "Wrong current password")),
            _ => BadRequestResult("The passkey was not accepted", ApiErrorCodes.PasskeyInvalid, result.Message)
        };

        private WebPasskeyListDto BuildPasskeyList()
        {
            var rp = RelyingParty();
            return new WebPasskeyListDto
            {
                Items = _auth.Passkeys.OrderBy(p => p.CreatedUtc).Select(ToDto).ToList(),
                RpId = rp.RpId,
                Origins = rp.Origins.ToList(),
                Supported = rp.Supported,
                RpIdOverride = _auth.PasskeyRpId,
                OriginsOverride = _auth.PasskeyOrigins.ToList()
            };
        }

        private static WebPasskeyBeginDto ToBeginDto(PasskeyBegin begin) => new()
        {
            RequestId = begin.RequestId,
            Options = JsonSerializer.SerializeToElement(begin.Options)
        };

        private static WebPasskeyDto ToDto(WebPasskey p) => new()
        {
            Id = p.CredentialId,
            Name = p.Name,
            CreatedUtc = DateTime.SpecifyKind(p.CreatedUtc, DateTimeKind.Utc),
            LastUsedUtc = p.LastUsedUtc is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null,
            BackupEligible = p.BackupEligible,
            BackedUp = p.BackedUp,
            Transports = p.Transports.ToList(),
            AaGuid = p.AaGuid,
            RpId = p.RpId
        };
    }
}
