using System.Net;
using System.Text.RegularExpressions;

namespace TelegramDownloader.Services.WebAuth
{
    /// <summary>
    /// Relying party of one WebAuthn ceremony: the RP ID (a domain, never an IP
    /// address) and the origins whose <c>clientDataJSON.origin</c> is accepted.
    /// </summary>
    public sealed record PasskeyRelyingParty(string RpId, IReadOnlyList<string> Origins)
    {
        public const int MaxOrigins = 10;

        private static readonly Regex HostLabel = new("^(?!-)[a-z0-9-]{1,63}(?<!-)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Whether browsers accept this value as an RP ID: a DNS name (with at
        /// least one label, e.g. <c>localhost</c>, <c>tfm.example.com</c>). IP
        /// addresses are rejected by every browser.
        /// </summary>
        public bool Supported => IsValidRpId(RpId);

        public static bool IsValidRpId(string? rpId)
        {
            if (string.IsNullOrWhiteSpace(rpId) || rpId.Length > 253) return false;
            if (rpId.Contains(':') || IPAddress.TryParse(rpId, out _)) return false;
            var labels = rpId.Split('.');
            return labels.All(l => HostLabel.IsMatch(l)) && !labels[^1].All(char.IsDigit);
        }

        /// <summary><c>scheme://host[:port]</c> as a browser writes it (no default port, lower case).</summary>
        public static string BuildOrigin(string scheme, string host, int? port)
        {
            scheme = (scheme ?? "http").ToLowerInvariant();
            var h = host.ToLowerInvariant();
            if (h.Contains(':') && !h.StartsWith('[')) h = $"[{h}]";
            var defaultPort = scheme == "https" ? 443 : 80;
            return port == null || port == defaultPort ? $"{scheme}://{h}" : $"{scheme}://{h}:{port}";
        }

        /// <summary>
        /// Normalises an origin typed in the settings (<c>https://host[:port]</c>,
        /// nothing else). Null when it is not a valid http(s) origin.
        /// </summary>
        public static string? NormalizeOrigin(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return null;
            if (uri.AbsolutePath != "/") return null;
            return BuildOrigin(uri.Scheme, uri.Host, uri.IsDefaultPort ? null : uri.Port);
        }

        /// <summary>Whether <paramref name="origin"/>'s host is <paramref name="rpId"/> or a subdomain of it.</summary>
        public static bool OriginMatchesRpId(string origin, string rpId)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            var host = uri.Host.ToLowerInvariant();
            var rp = rpId.ToLowerInvariant();
            return host == rp || host.EndsWith("." + rp, StringComparison.Ordinal);
        }

        /// <summary>
        /// The relying party of a request. <paramref name="host"/>,
        /// <paramref name="scheme"/> and <paramref name="port"/> are the values
        /// after the forwarded headers middleware (only trusted proxies can
        /// change them). The optional override replaces the RP ID and/or the
        /// accepted origins.
        /// </summary>
        public static PasskeyRelyingParty Resolve(string scheme, string? host, int? port,
            string? rpIdOverride, IReadOnlyList<string>? originsOverride)
        {
            var requestHost = (host ?? string.Empty).Trim('[', ']').ToLowerInvariant();
            var rpId = string.IsNullOrWhiteSpace(rpIdOverride) ? requestHost : rpIdOverride.Trim().ToLowerInvariant();
            IReadOnlyList<string> origins = originsOverride is { Count: > 0 }
                ? originsOverride.ToList()
                : new[] { BuildOrigin(scheme, requestHost, port) };
            return new PasskeyRelyingParty(rpId, origins);
        }
    }
}
