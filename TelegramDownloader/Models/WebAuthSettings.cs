using MongoDB.Bson.Serialization.Attributes;

namespace TelegramDownloader.Models
{
    /// <summary>
    /// Password protection of the web UI (Blazor and the new web app) and the
    /// optional "no password on the local network" rule.
    ///
    /// Stored in the config database (<c>TCCONFIG.webauth</c>) as its own
    /// document and mirrored to <c>USERDATAFOLDER/web-auth.json</c> so the
    /// protection fails closed when MongoDB cannot be reached at startup.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class WebAuthSettings
    {
        public const string DocumentId = "web-auth";

        [BsonId]
        public string Id { get; set; } = DocumentId;

        /// <summary>
        /// PBKDF2 hash produced by ASP.NET Core Identity's <c>PasswordHasher</c>
        /// (format v3: HMAC-SHA512, random salt, 100 000 iterations; salt and
        /// iteration count are embedded in the value). Null = no password, the
        /// UI is open as in previous versions.
        /// </summary>
        public string? PasswordHash { get; set; }

        /// <summary>
        /// When true, requests that genuinely come from the local network (see
        /// <c>NetworkRules.IsGenuinelyLocal</c>) are let in without the password.
        /// Only honoured while a password is set.
        /// </summary>
        public bool LanBypassEnabled { get; set; }

        /// <summary>
        /// Random value embedded in every session cookie. It changes whenever the
        /// password is set, changed or removed, which signs out every existing
        /// session at once.
        /// </summary>
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Passkeys (WebAuthn credentials) registered as an extra sign-in
        /// method. Only meaningful while a password is set: removing or
        /// resetting the password removes them.
        /// </summary>
        public List<WebPasskey> Passkeys { get; set; } = new();

        /// <summary>
        /// WebAuthn user handle of the single web user (base64url of 32 random
        /// bytes), generated with the first passkey and then kept stable.
        /// </summary>
        public string? PasskeyUserHandle { get; set; }

        /// <summary>
        /// Optional relying party ID override (reverse proxy setups). Null =
        /// the host name of each request.
        /// </summary>
        public string? PasskeyRpId { get; set; }

        /// <summary>
        /// Optional list of allowed origins (<c>https://host[:port]</c>). Empty =
        /// the origin of each request.
        /// </summary>
        public List<string> PasskeyOrigins { get; set; } = new();

        public WebAuthSettings Clone()
        {
            var copy = (WebAuthSettings)MemberwiseClone();
            copy.Passkeys = (Passkeys ?? new()).Select(p => p.Clone()).ToList();
            copy.PasskeyOrigins = new List<string>(PasskeyOrigins ?? new());
            return copy;
        }
    }

    /// <summary>One registered passkey. Binary values are base64url strings (same in MongoDB and the JSON mirror).</summary>
    [BsonIgnoreExtraElements]
    public class WebPasskey
    {
        /// <summary>Credential ID (base64url).</summary>
        public string CredentialId { get; set; } = string.Empty;
        /// <summary>COSE-encoded public key (base64url).</summary>
        public string PublicKey { get; set; } = string.Empty;
        /// <summary>Last signature counter seen. Synced passkeys always report 0.</summary>
        public long SignCount { get; set; }
        /// <summary>User handle the credential was created for (base64url).</summary>
        public string UserHandle { get; set; } = string.Empty;
        /// <summary>Authenticator model (all zeros for most password managers).</summary>
        public string AaGuid { get; set; } = Guid.Empty.ToString();
        /// <summary>Transports reported by the browser (usb, nfc, ble, hybrid, internal...).</summary>
        public List<string> Transports { get; set; } = new();
        /// <summary>Backup eligible (BE flag): a synced / multi-device passkey.</summary>
        public bool BackupEligible { get; set; }
        /// <summary>Backed up (BS flag) at the last use.</summary>
        public bool BackedUp { get; set; }
        /// <summary>Attestation statement format (always accepted without trust checks).</summary>
        public string? AttestationFormat { get; set; }
        /// <summary>Relying party ID it was registered for.</summary>
        public string? RpId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsedUtc { get; set; }

        public WebPasskey Clone()
        {
            var copy = (WebPasskey)MemberwiseClone();
            copy.Transports = new List<string>(Transports ?? new());
            return copy;
        }
    }
}
