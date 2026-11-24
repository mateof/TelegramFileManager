using System.Text.Json;

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

        /// <summary>Number of registered passkeys (0 while no password is set).</summary>
        public int PasskeyCount { get; set; }

        /// <summary>
        /// The login page may offer "Sign in with a passkey": a password is set,
        /// at least one passkey is registered and this request's host name can be
        /// an RP ID (not an IP address).
        /// </summary>
        public bool PasskeysAvailable { get; set; }
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

    /// <summary>One registered passkey (no key material).</summary>
    public class WebPasskeyDto
    {
        /// <summary>Credential ID (base64url); the id of the rename/delete endpoints.</summary>
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime? LastUsedUtc { get; set; }
        /// <summary>BE flag: a synced / multi-device passkey (password manager, iCloud, Google...).</summary>
        public bool BackupEligible { get; set; }
        /// <summary>BS flag at the last use: currently backed up.</summary>
        public bool BackedUp { get; set; }
        public List<string> Transports { get; set; } = new();
        public string AaGuid { get; set; } = string.Empty;
        /// <summary>RP ID it was registered for; it only works on that domain.</summary>
        public string? RpId { get; set; }
    }

    /// <summary>Payload of <c>GET /api/v1/web-auth/passkeys</c>.</summary>
    public class WebPasskeyListDto
    {
        public List<WebPasskeyDto> Items { get; set; } = new();
        /// <summary>RP ID used for this request (override or request host).</summary>
        public string RpId { get; set; } = string.Empty;
        /// <summary>Origins accepted for this request.</summary>
        public List<string> Origins { get; set; } = new();
        /// <summary>False when <see cref="RpId"/> cannot be used (an IP address).</summary>
        public bool Supported { get; set; }
        /// <summary>Configured RP ID override (null = request host).</summary>
        public string? RpIdOverride { get; set; }
        /// <summary>Configured origins override (empty = request origin).</summary>
        public List<string> OriginsOverride { get; set; } = new();
    }

    /// <summary>Result of a passkey <c>begin</c> call.</summary>
    public class WebPasskeyBeginDto
    {
        /// <summary>Opaque id of the pending ceremony; send it back to <c>finish</c> within 5 minutes.</summary>
        public string RequestId { get; set; } = string.Empty;
        /// <summary>
        /// WebAuthn options in the JSON form of the spec (binary values base64url):
        /// <c>PublicKeyCredentialCreationOptionsJSON</c> or <c>PublicKeyCredentialRequestOptionsJSON</c>.
        /// </summary>
        public JsonElement Options { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/web-auth/passkeys/register/begin</c>.</summary>
    public class WebPasskeyRegisterBeginRequest
    {
        public string? CurrentPassword { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/web-auth/passkeys/register/finish</c>.</summary>
    public class WebPasskeyRegisterFinishRequest
    {
        public string? RequestId { get; set; }
        /// <summary>Friendly name (max 64 characters).</summary>
        public string? Name { get; set; }
        /// <summary><c>PublicKeyCredential.toJSON()</c> of <c>navigator.credentials.create()</c>.</summary>
        public JsonElement Credential { get; set; }
    }

    /// <summary>Body of <c>POST /api/v1/web-auth/passkeys/login/finish</c>.</summary>
    public class WebPasskeyLoginFinishRequest
    {
        public string? RequestId { get; set; }
        /// <summary><c>PublicKeyCredential.toJSON()</c> of <c>navigator.credentials.get()</c>.</summary>
        public JsonElement Credential { get; set; }
        /// <summary>Same as the password login: persistent 30-day cookie.</summary>
        public bool Remember { get; set; }
    }

    /// <summary>Body of <c>PUT /api/v1/web-auth/passkeys/{id}</c>.</summary>
    public class WebPasskeyRenameRequest
    {
        public string? Name { get; set; }
    }

    /// <summary>Body of <c>PUT /api/v1/web-auth/passkeys/settings</c> (reverse proxy setups).</summary>
    public class WebPasskeySettingsRequest
    {
        /// <summary>RP ID override (a domain); null/empty = the request host.</summary>
        public string? RpId { get; set; }
        /// <summary>Accepted origins (<c>https://host[:port]</c>), on the RP ID domain; empty = the request origin.</summary>
        public List<string>? Origins { get; set; }
    }
}
