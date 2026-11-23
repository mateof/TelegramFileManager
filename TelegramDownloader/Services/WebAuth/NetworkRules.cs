using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace TelegramDownloader.Services.WebAuth
{
    /// <summary>
    /// Network helpers of the web authentication: the "genuinely local" rule of
    /// the LAN bypass and the parsing of the trusted reverse proxy list.
    /// </summary>
    public static class NetworkRules
    {
        /// <summary>
        /// Headers that reveal the request went through a reverse proxy (or that
        /// the client pretends it did). Their mere presence disables the LAN
        /// bypass for the request.
        /// </summary>
        public static readonly string[] ProxyHeaders =
        {
            "X-Forwarded-For",
            "X-Forwarded-Host",
            "X-Forwarded-Proto",
            "X-Real-IP",
            "Forwarded"
        };

        /// <summary>Loopback, RFC 1918, link-local and IPv6 unique-local/link-local ranges.</summary>
        public static readonly string[] PrivateRanges =
        {
            "127.0.0.0/8",
            "10.0.0.0/8",
            "172.16.0.0/12",
            "192.168.0.0/16",
            "169.254.0.0/16",
            "::1/128",
            "fc00::/7",
            "fe80::/10"
        };

        private static readonly IPNetwork[] PrivateNetworks = PrivateRanges.Select(IPNetwork.Parse).ToArray();

        /// <summary>Normalises IPv4-mapped IPv6 addresses (<c>::ffff:192.168.1.2</c>) to IPv4.</summary>
        public static IPAddress Normalize(IPAddress address) =>
            address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        /// <summary>True for loopback and private/link-local addresses.</summary>
        public static bool IsPrivateOrLoopback(IPAddress? address)
        {
            if (address == null) return false;
            var ip = Normalize(address);
            if (IPAddress.IsLoopback(ip)) return true;
            return PrivateNetworks.Any(n => n.BaseAddress.AddressFamily == ip.AddressFamily && n.Contains(ip));
        }

        /// <summary>True when any reverse proxy header is present.</summary>
        public static bool HasProxyHeaders(IHeaderDictionary headers) =>
            ProxyHeaders.Any(h => headers.ContainsKey(h));

        /// <summary>
        /// The LAN rule. A request is "genuinely local" when the TCP peer (the
        /// socket address, captured before forwarded headers are applied) is a
        /// loopback or private address AND no proxy header is present.
        ///
        /// Why both: a reverse proxy usually runs on the same host or LAN, so
        /// every request it relays from the internet arrives from a private
        /// address. Without the header check the LAN bypass would let the whole
        /// internet in through the proxy. Proxies add X-Forwarded-* / Forwarded /
        /// X-Real-IP, so their presence marks the request as "not local" and the
        /// password is always required through a proxy. A client cannot abuse
        /// the headers either: adding them only removes the bypass.
        /// </summary>
        public static bool IsGenuinelyLocal(IPAddress? tcpRemoteAddress, bool proxyHeadersPresent) =>
            !proxyHeadersPresent && IsPrivateOrLoopback(tcpRemoteAddress);

        /// <summary>
        /// Host names accepted for the LAN bypass: IP literals, <c>localhost</c>,
        /// single-label names (<c>nas</c>) and the usual local suffixes. A public
        /// domain name is refused because a page on that domain could be made to
        /// resolve to the LAN address (DNS rebinding) and would then read the API
        /// from the victim's browser as a same-origin page.
        /// </summary>
        public static bool IsLocalHostName(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            var h = host.Trim().TrimEnd('.').ToLowerInvariant();
            if (h.StartsWith('[') && h.EndsWith(']')) h = h[1..^1];
            if (IPAddress.TryParse(h, out _)) return true;
            if (h == "localhost" || h.EndsWith(".localhost")) return true;
            if (!h.Contains('.')) return true;
            string[] suffixes = { ".local", ".lan", ".home", ".internal", ".home.arpa", ".localdomain" };
            return suffixes.Any(s => h.EndsWith(s));
        }

        /// <summary>
        /// Parses a list of IPs and CIDR ranges. Invalid entries are reported
        /// through <paramref name="invalid"/> and skipped.
        /// </summary>
        public static (List<IPAddress> Proxies, List<IPNetwork> Networks) ParseTrustedProxies(
            IEnumerable<string>? entries, List<string>? invalid = null)
        {
            var proxies = new List<IPAddress>();
            var networks = new List<IPNetwork>();
            foreach (var raw in entries ?? Array.Empty<string>())
            {
                var entry = raw?.Trim();
                if (string.IsNullOrEmpty(entry)) continue;
                if (entry.Contains('/'))
                {
                    if (IPNetwork.TryParse(entry, out var network)) networks.Add(network);
                    else invalid?.Add(entry);
                }
                else if (IPAddress.TryParse(entry, out var ip))
                {
                    proxies.Add(Normalize(ip));
                }
                else
                {
                    invalid?.Add(entry);
                }
            }
            return (proxies, networks);
        }

        /// <summary>
        /// Reads <c>TrustedProxies</c> from the configuration: a comma/semicolon
        /// separated string (<c>TrustedProxies=10.0.0.5,172.18.0.0/16</c>, also
        /// as environment variable) or an array section. Falls back to
        /// <paramref name="fallback"/> (config.json <c>trusted_proxies</c>) and
        /// then to <see cref="PrivateRanges"/>.
        /// </summary>
        public static List<string> ReadTrustedProxyList(Microsoft.Extensions.Configuration.IConfiguration configuration, IEnumerable<string>? fallback)
        {
            var section = configuration.GetSection("TrustedProxies");
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(section.Value))
                list.AddRange(section.Value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries));
            list.AddRange(section.GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v))!);
            if (list.Count == 0 && fallback != null)
                list.AddRange(fallback.Where(v => !string.IsNullOrWhiteSpace(v)));
            if (list.Count == 0)
                list.AddRange(PrivateRanges);
            return list;
        }
    }
}
