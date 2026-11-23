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

        public WebAuthSettings Clone() => (WebAuthSettings)MemberwiseClone();
    }
}
