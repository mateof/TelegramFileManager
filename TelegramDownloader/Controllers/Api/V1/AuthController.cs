using Microsoft.AspNetCore.Mvc;
using TelegramDownloader.Data;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services;
using TelegramDownloader.Services.Api;

namespace TelegramDownloader.Controllers.Api.V1
{
    /// <summary>
    /// Telegram session lifecycle: sign in with a phone number or a QR code,
    /// inspect the current session and sign out.
    ///
    /// The Telegram session lives on the server and is shared by the web UI and
    /// every API client: signing in here also signs in the web UI, and signing
    /// out terminates both.
    /// </summary>
    [Route("api/v1/auth")]
    [Tags("Auth")]
    public class AuthController : ApiV1ControllerBase
    {
        private readonly ITelegramService _telegram;
        private readonly ISetupService _setup;
        private readonly QrLoginSessionManager _qr;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            ITelegramService telegram,
            ISetupService setup,
            QrLoginSessionManager qr,
            ILogger<AuthController> logger)
        {
            _telegram = telegram;
            _setup = setup;
            _qr = qr;
            _logger = logger;
        }

        /// <summary>Current authentication state.</summary>
        /// <remarks>
        /// Call this first. <c>Step</c> tells you what the server expects next:
        /// <c>phone</c>, <c>vc</c> (verification code), <c>pass</c> (2FA
        /// password), <c>ok</c> (already signed in) or <c>setup_required</c>
        /// when the application has not been configured yet.
        /// </remarks>
        [HttpGet("status")]
        [ProducesResponseType(typeof(ApiResult<AuthStatusDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Status()
        {
            try
            {
                var dto = new AuthStatusDto { IsConfigured = _telegram.IsConfigured };

                if (!_telegram.IsConfigured)
                {
                    try
                    {
                        _telegram.InitializeClient();
                        dto.IsConfigured = _telegram.IsConfigured;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Telegram client could not be initialized");
                    }
                }

                if (!dto.IsConfigured)
                {
                    dto.Step = AuthStep.SetupRequired;
                    return OkResult(dto);
                }

                dto.Step = await _telegram.checkAuth(null) ?? AuthStep.Phone;
                dto.IsAuthenticated = dto.Step == AuthStep.Authenticated;

                if (dto.IsAuthenticated)
                    dto.User = await BuildUserAsync();

                return OkResult(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading auth status");
                return ErrorResult("Could not read the authentication status", ex);
            }
        }

        /// <summary>Signed-in Telegram user.</summary>
        [HttpGet("me")]
        [ProducesResponseType(typeof(ApiResult<TelegramUserDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Me()
        {
            if (!_telegram.IsConfigured || !_telegram.checkUserLogin())
                return StatusCode(StatusCodes.Status401Unauthorized,
                    ApiResult.Fail(ApiErrorCodes.NotLoggedIn, "No Telegram session is active"));

            var user = await BuildUserAsync();
            if (user == null)
                return NotFoundResult("The Telegram user could not be resolved");

            return OkResult(user);
        }

        /// <summary>Advances the phone login flow one step.</summary>
        /// <remarks>
        /// Post the phone number with <c>isPhone: true</c> to start. The response
        /// tells you the next step; post the verification code (and then, when
        /// required, the two-factor password) with <c>isPhone: false</c>.
        ///
        /// Sample sequence:
        /// <code>
        /// POST /api/v1/auth/login  { "value": "+34600000000", "isPhone": true }  -> step "vc"
        /// POST /api/v1/auth/login  { "value": "12345" }                          -> step "pass" or "ok"
        /// POST /api/v1/auth/login  { "value": "my-2fa-password" }                -> step "ok"
        /// </code>
        /// </remarks>
        [HttpPost("login")]
        [ProducesResponseType(typeof(ApiResult<AuthStatusDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Login([FromBody] LoginStepRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Value))
                return BadRequestResult("A value is required for the current login step");

            try
            {
                if (!_telegram.IsConfigured)
                    _telegram.InitializeClient();

                var step = await _telegram.checkAuth(request.Value.Trim(), request.IsPhone) ?? AuthStep.Phone;

                var dto = new AuthStatusDto
                {
                    Step = step,
                    IsConfigured = _telegram.IsConfigured,
                    IsAuthenticated = step == AuthStep.Authenticated
                };
                if (dto.IsAuthenticated)
                    dto.User = await BuildUserAsync();

                return OkResult(dto);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Login step failed");
                return BadRequestResult("The login step was rejected by Telegram", ApiErrorCodes.InvalidRequest, ex.Message);
            }
        }

        /// <summary>Starts a QR login session.</summary>
        /// <remarks>
        /// Render <c>qrImageBase64</c> (a PNG) or encode <c>loginUrl</c> yourself,
        /// then poll <c>GET /api/v1/auth/qr/{sessionId}</c>. Telegram rotates the
        /// token every ~30 seconds, so keep repainting the QR from the polled
        /// value. When the status turns <c>password_required</c>, post the 2FA
        /// password to <c>/api/v1/auth/qr/{sessionId}/password</c>.
        /// </remarks>
        /// <param name="logoutFirst">Terminate any existing session before starting.</param>
        [HttpPost("qr")]
        [ProducesResponseType(typeof(ApiResult<QrLoginDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> StartQr([FromQuery] bool logoutFirst = false)
        {
            try
            {
                if (!_telegram.IsConfigured)
                    _telegram.InitializeClient();

                var session = await _qr.StartAsync(_telegram, logoutFirst);
                return OkResult(session);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not start a QR login session");
                return ErrorResult("Could not start a QR login session", ex);
            }
        }

        /// <summary>Polls the state of a QR login session.</summary>
        [HttpGet("qr/{sessionId}")]
        [ProducesResponseType(typeof(ApiResult<QrLoginDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult PollQr(string sessionId)
        {
            var session = _qr.Get(sessionId);
            if (session == null)
                return NotFoundResult("Unknown or expired QR login session");
            return OkResult(session);
        }

        /// <summary>Supplies the two-factor password a QR session is waiting for.</summary>
        [HttpPost("qr/{sessionId}/password")]
        [ProducesResponseType(typeof(ApiResult<QrLoginDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult ProvideQrPassword(string sessionId, [FromBody] QrPasswordRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Password))
                return BadRequestResult("A password is required");

            if (!_qr.ProvidePassword(sessionId, _telegram, request.Password))
                return NotFoundResult("Unknown or expired QR login session");

            return OkResult(_qr.Get(sessionId)!);
        }

        /// <summary>Cancels a pending QR login session.</summary>
        [HttpDelete("qr/{sessionId}")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status404NotFound)]
        public IActionResult CancelQr(string sessionId)
        {
            if (!_qr.Cancel(sessionId))
                return NotFoundResult("Unknown or expired QR login session");
            return OkEmpty("QR login session cancelled");
        }

        /// <summary>Signs out of Telegram.</summary>
        /// <remarks>
        /// This terminates the shared server session: the web UI is signed out
        /// too and every client has to authenticate again.
        /// </remarks>
        [HttpPost("logout")]
        [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout()
        {
            try
            {
                await _telegram.logOff();
                return OkEmpty("Signed out");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error signing out");
                return ErrorResult("Could not sign out", ex);
            }
        }

        private async Task<TelegramUserDto?> BuildUserAsync()
        {
            try
            {
                var user = await _telegram.GetUser();
                if (user == null) return null;
                return new TelegramUserDto
                {
                    Id = user.id,
                    Username = user.username,
                    FirstName = user.first_name,
                    LastName = user.last_name,
                    Phone = user.phone,
                    IsPremium = TelegramService.isPremium
                };
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not resolve the Telegram user");
                return null;
            }
        }
    }
}
