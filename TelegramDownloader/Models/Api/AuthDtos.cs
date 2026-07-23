namespace TelegramDownloader.Models.Api
{
    /// <summary>
    /// Step of the Telegram login state machine the server is currently waiting for.
    /// </summary>
    public static class AuthStep
    {
        /// <summary>Server needs a phone number.</summary>
        public const string Phone = "phone";
        /// <summary>Server needs the verification code sent by Telegram.</summary>
        public const string VerificationCode = "vc";
        /// <summary>Server needs the two-factor password.</summary>
        public const string Password = "pass";
        /// <summary>Session is authenticated.</summary>
        public const string Authenticated = "ok";
        /// <summary>The application has not been configured yet (see /api/v1/system/setup).</summary>
        public const string SetupRequired = "setup_required";
    }

    /// <summary>Current authentication state of the Telegram session.</summary>
    public class AuthStatusDto
    {
        /// <summary>One of the values in <see cref="AuthStep"/>.</summary>
        public string Step { get; set; } = AuthStep.Phone;

        /// <summary>True when the session is fully authenticated.</summary>
        public bool IsAuthenticated { get; set; }

        /// <summary>True when API id/hash and MongoDB are configured.</summary>
        public bool IsConfigured { get; set; }

        /// <summary>Signed-in Telegram user, when authenticated.</summary>
        public TelegramUserDto? User { get; set; }
    }

    /// <summary>Signed-in Telegram user.</summary>
    public class TelegramUserDto
    {
        public long Id { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public bool IsPremium { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/auth/login</c>.</summary>
    public class LoginStepRequest
    {
        /// <summary>
        /// Value for the current step: the phone number, the verification code
        /// or the two-factor password.
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// Set to true when <see cref="Value"/> is a phone number, so the server
        /// starts a new login instead of continuing the pending one.
        /// </summary>
        public bool IsPhone { get; set; }
    }

    /// <summary>QR login session created by <c>POST /api/v1/auth/qr</c>.</summary>
    public class QrLoginDto
    {
        /// <summary>Identifier used to poll or cancel the QR session.</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>The <c>tg://login?token=...</c> URL to render as a QR code.</summary>
        public string? LoginUrl { get; set; }

        /// <summary>PNG QR image, base64 encoded, ready to be shown as-is.</summary>
        public string? QrImageBase64 { get; set; }

        /// <summary>
        /// <c>waiting</c>, <c>password_required</c>, <c>authenticated</c>,
        /// <c>cancelled</c> or <c>error</c>.
        /// </summary>
        public string Status { get; set; } = "waiting";

        /// <summary>Error detail when <see cref="Status"/> is <c>error</c>.</summary>
        public string? Error { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/auth/qr/{sessionId}/password</c>.</summary>
    public class QrPasswordRequest
    {
        public string Password { get; set; } = string.Empty;
    }
}
