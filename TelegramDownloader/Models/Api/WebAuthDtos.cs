namespace TelegramDownloader.Models.Api
{
    /// <summary>State of the web password protection, as seen by this request.</summary>
    public class WebAuthStatusDto
    {
        /// <summary>A web password is configured (otherwise the web UI is open).</summary>
        public bool PasswordSet { get; set; }

        /// <summary>The request carries a valid web session cookie.</summary>
        public bool Authenticated { get; set; }

        /// <summary>The "no password on the local network" option is on (only honoured while a password is set).</summary>
        public bool LanBypassEnabled { get; set; }

        /// <summary>This very request is let in without the password by the LAN rule.</summary>
        public bool LanBypassActive { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/web-auth/setup</c>.</summary>
    public class WebAuthSetupRequest
    {
        /// <summary>New password, at least 8 characters.</summary>
        public string? Password { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/web-auth/login</c>.</summary>
    public class WebAuthLoginRequest
    {
        public string? Password { get; set; }

        /// <summary>True: persistent cookie valid 30 days (sliding). False: browser-session cookie.</summary>
        public bool Remember { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/web-auth/password</c>.</summary>
    public class WebAuthChangePasswordRequest
    {
        public string? CurrentPassword { get; set; }

        /// <summary>New password, at least 8 characters.</summary>
        public string? NewPassword { get; set; }
    }

    /// <summary>Body of <c>DELETE /api/v1/web-auth/password</c>.</summary>
    public class WebAuthRemovePasswordRequest
    {
        public string? CurrentPassword { get; set; }
    }

    /// <summary>Body of <c>PUT /api/v1/web-auth/settings</c>.</summary>
    public class WebAuthSettingsRequest
    {
        public bool LanBypassEnabled { get; set; }
    }
}
