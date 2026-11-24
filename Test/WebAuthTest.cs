using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;
using TelegramDownloader.Services.WebAuth;

namespace Test
{
    public class WebPasswordHasherTest
    {
        [Test]
        public void Hash_verifies_with_the_right_password_only()
        {
            var hash = WebPasswordHasher.Hash("correct horse battery");
            Assert.Multiple(() =>
            {
                Assert.That(hash, Does.Not.Contain("correct horse"));
                Assert.That(WebPasswordHasher.Verify(hash, "correct horse battery", out var rehash), Is.True);
                Assert.That(rehash, Is.False);
                Assert.That(WebPasswordHasher.Verify(hash, "correct horse batterY", out _), Is.False);
                Assert.That(WebPasswordHasher.Verify(hash, "", out _), Is.False);
                Assert.That(WebPasswordHasher.Verify(hash, null, out _), Is.False);
            });
        }

        [Test]
        public void Hashes_are_salted()
        {
            Assert.That(WebPasswordHasher.Hash("same password"), Is.Not.EqualTo(WebPasswordHasher.Hash("same password")));
        }

        [Test]
        public void Missing_or_corrupt_hash_never_verifies()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WebPasswordHasher.Verify(null, "whatever1", out _), Is.False);
                Assert.That(WebPasswordHasher.Verify("", "whatever1", out _), Is.False);
                Assert.That(WebPasswordHasher.Verify("not-base64!!", "whatever1", out _), Is.False);
                Assert.That(WebPasswordHasher.Verify("AQAAAA==", "whatever1", out _), Is.False);
            });
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("1234567", false)]
        [TestCase("12345678", true)]
        public void Minimum_length_is_eight(string? password, bool valid)
        {
            Assert.That(WebPasswordHasher.Validate(password) == null, Is.EqualTo(valid));
        }
    }

    public class NetworkRulesTest
    {
        [TestCase("127.0.0.1", true)]
        [TestCase("127.10.0.1", true)]
        [TestCase("10.1.2.3", true)]
        [TestCase("172.16.0.1", true)]
        [TestCase("172.31.255.254", true)]
        [TestCase("172.32.0.1", false)]
        [TestCase("172.15.255.255", false)]
        [TestCase("192.168.1.20", true)]
        [TestCase("192.169.1.20", false)]
        [TestCase("169.254.10.10", true)]
        [TestCase("8.8.8.8", false)]
        [TestCase("100.64.0.1", false)]
        [TestCase("::1", true)]
        [TestCase("fd12:3456::1", true)]
        [TestCase("fc00::1", true)]
        [TestCase("fe80::1", true)]
        [TestCase("2001:4860:4860::8888", false)]
        [TestCase("::ffff:192.168.1.5", true)]
        [TestCase("::ffff:8.8.8.8", false)]
        public void Private_and_loopback_addresses(string ip, bool expected)
        {
            Assert.That(NetworkRules.IsPrivateOrLoopback(IPAddress.Parse(ip)), Is.EqualTo(expected));
        }

        [Test]
        public void Null_address_is_not_local()
        {
            Assert.That(NetworkRules.IsPrivateOrLoopback(null), Is.False);
        }

        [TestCase("X-Forwarded-For")]
        [TestCase("X-Forwarded-Host")]
        [TestCase("X-Forwarded-Proto")]
        [TestCase("X-Real-IP")]
        [TestCase("Forwarded")]
        [TestCase("x-forwarded-for")]
        public void Any_proxy_header_is_detected(string header)
        {
            var headers = new HeaderDictionary { [header] = "1.2.3.4" };
            Assert.That(NetworkRules.HasProxyHeaders(headers), Is.True);
        }

        [Test]
        public void Ordinary_headers_are_not_proxy_headers()
        {
            var headers = new HeaderDictionary { ["Host"] = "nas:5000", ["User-Agent"] = "x" };
            Assert.That(NetworkRules.HasProxyHeaders(headers), Is.False);
        }

        [Test]
        public void Genuinely_local_needs_private_peer_and_no_proxy_headers()
        {
            Assert.Multiple(() =>
            {
                Assert.That(NetworkRules.IsGenuinelyLocal(IPAddress.Parse("192.168.1.20"), false), Is.True);
                // Through a reverse proxy on the LAN: the peer is private but the request is from anywhere.
                Assert.That(NetworkRules.IsGenuinelyLocal(IPAddress.Parse("192.168.1.2"), true), Is.False);
                Assert.That(NetworkRules.IsGenuinelyLocal(IPAddress.Parse("8.8.8.8"), false), Is.False);
            });
        }

        [TestCase("192.168.1.10", true)]
        [TestCase("[fe80::1]", true)]
        [TestCase("localhost", true)]
        [TestCase("nas", true)]
        [TestCase("nas.local", true)]
        [TestCase("tfm.lan", true)]
        [TestCase("box.home.arpa", true)]
        [TestCase("tfm.example.com", false)]
        [TestCase("evil.com", false)]
        [TestCase("", false)]
        public void Local_host_names(string host, bool expected)
        {
            Assert.That(NetworkRules.IsLocalHostName(host), Is.EqualTo(expected));
        }

        [Test]
        public void Trusted_proxy_list_parses_ips_and_cidrs()
        {
            var invalid = new List<string>();
            var (proxies, networks) = NetworkRules.ParseTrustedProxies(new[] { "10.0.0.5", "172.18.0.0/16", "bogus", "::1", "300.1.1.1/8" }, invalid);
            Assert.Multiple(() =>
            {
                Assert.That(proxies.Select(p => p.ToString()), Is.EquivalentTo(new[] { "10.0.0.5", "::1" }));
                Assert.That(networks, Has.Count.EqualTo(1));
                Assert.That(networks[0].Contains(IPAddress.Parse("172.18.3.4")), Is.True);
                Assert.That(invalid, Is.EquivalentTo(new[] { "bogus", "300.1.1.1/8" }));
            });
        }

        [Test]
        public void Trusted_proxies_default_to_private_ranges()
        {
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
            Assert.That(NetworkRules.ReadTrustedProxyList(config, null), Is.EquivalentTo(NetworkRules.PrivateRanges));
        }

        [Test]
        public void Trusted_proxies_setting_wins_over_config_json()
        {
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["TrustedProxies"] = "10.0.0.5, 172.18.0.0/16" })
                .Build();
            Assert.That(NetworkRules.ReadTrustedProxyList(config, new[] { "1.2.3.4" }),
                Is.EquivalentTo(new[] { "10.0.0.5", "172.18.0.0/16" }));
            var empty = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
            Assert.That(NetworkRules.ReadTrustedProxyList(empty, new[] { "1.2.3.4" }), Is.EquivalentTo(new[] { "1.2.3.4" }));
        }
    }

    public class WebAccessPolicyTest
    {
        private const string Key = "secret-key";

        private static WebAccessRequest Req(
            string path = "/api/v1/channels",
            string method = "GET",
            bool password = true,
            bool lan = false,
            string? configuredKey = Key,
            string? providedKey = null,
            bool cookie = false,
            bool internalToken = false,
            string ip = "203.0.113.7",
            bool proxyHeaders = false,
            string host = "192.168.1.10",
            string? origin = null,
            string? fetchSite = null,
            string? requestedWith = null) => new()
        {
            Path = path,
            Method = method,
            Scheme = "http",
            Host = host,
            Port = 5000,
            PasswordSet = password,
            LanBypassEnabled = lan,
            ConfiguredApiKey = configuredKey,
            ProvidedApiKey = providedKey,
            CookieAuthenticated = cookie,
            InternalTokenValid = internalToken,
            TcpRemoteAddress = IPAddress.Parse(ip),
            ProxyHeadersPresent = proxyHeaders,
            Origin = origin,
            SecFetchSite = fetchSite,
            RequestedWith = requestedWith
        };

        [TestCase("/api/v1/web-auth/status", WebPathKind.WebAuth)]
        [TestCase("/api/v1/system/ping", WebPathKind.Ping)]
        [TestCase("/api/v1/system/info", WebPathKind.Api)]
        [TestCase("/API/V1/channels", WebPathKind.Api)]
        [TestCase("/api/mobile/channels", WebPathKind.Api)]
        [TestCase("/hubs/transfers/negotiate", WebPathKind.Api)]
        [TestCase("/hubs/events", WebPathKind.Api)]
        [TestCase("/api/file/GetFileStream/1/2/a.mp4", WebPathKind.Legacy)]
        [TestCase("/api/temp/Upload", WebPathKind.Legacy)]
        [TestCase("/api/channel/image/123", WebPathKind.Legacy)]
        [TestCase("/api/Log/Download", WebPathKind.Legacy)]
        [TestCase("/local/music/song.mp3", WebPathKind.Legacy)]
        [TestCase("/img/telegram/123.jpg", WebPathKind.Legacy)]
        [TestCase("/api-docs/index.html", WebPathKind.Docs)]
        [TestCase("/swagger/api-v1/swagger.json", WebPathKind.Docs)]
        [TestCase("/_blazor/negotiate", WebPathKind.BlazorHub)]
        [TestCase("/", WebPathKind.BlazorPage)]
        [TestCase("/config", WebPathKind.BlazorPage)]
        [TestCase("/fm/12345", WebPathKind.BlazorPage)]
        [TestCase("/setup", WebPathKind.BlazorPage)]
        [TestCase("/app", WebPathKind.Public)]
        [TestCase("/app/login", WebPathKind.Public)]
        [TestCase("/app/assets/index-abc.js", WebPathKind.Public)]
        [TestCase("/_framework/blazor.server.js", WebPathKind.Public)]
        [TestCase("/css/site.css", WebPathKind.Public)]
        [TestCase("/favicon.ico", WebPathKind.Public)]
        [TestCase("/img/logo.png", WebPathKind.Public)]
        [TestCase("/webdav/channel/file.bin", WebPathKind.WebDav)]
        [TestCase("/localfm/1", WebPathKind.BlazorPage)]
        public void Classify(string path, WebPathKind expected)
        {
            Assert.That(WebAccessPolicy.Classify(path), Is.EqualTo(expected));
        }

        // The browser fetches the manifest (and its icons) without credentials,
        // and the service worker script must load before anyone signs in.
        [TestCase("/app/manifest.webmanifest")]
        [TestCase("/app/sw.js")]
        [TestCase("/app/registerSW.js")]
        [TestCase("/app/icons/icon-192.png")]
        [TestCase("/app/icons/maskable-512.png")]
        [TestCase("/app/icons/apple-touch-icon.png")]
        [TestCase("/app/screenshots/wide.webp")]
        [TestCase("/app/favicon.svg")]
        [TestCase("/app/")]
        public void Pwa_files_of_the_web_app_are_public_even_with_a_password(string path)
        {
            Assert.That(WebAccessPolicy.Classify(path), Is.EqualTo(WebPathKind.Public));
            var d = WebAccessPolicy.Evaluate(Req(path));
            Assert.Multiple(() =>
            {
                Assert.That(d.Allowed, Is.True);
                Assert.That(d.Method, Is.EqualTo(AccessMethod.Open));
            });
        }

        // --- no password: exactly the previous behaviour --------------------

        [TestCase("/api/v1/channels")]
        [TestCase("/api/mobile/channels")]
        [TestCase("/hubs/transfers")]
        [TestCase("/api/v1/system/ping")]
        public void Open_mode_api_needs_the_key_when_configured(string path)
        {
            Assert.Multiple(() =>
            {
                Assert.That(WebAccessPolicy.Evaluate(Req(path, password: false)).Failure, Is.EqualTo(AccessFailure.ApiKeyRequired));
                Assert.That(WebAccessPolicy.Evaluate(Req(path, password: false, providedKey: "wrong")).Failure, Is.EqualTo(AccessFailure.InvalidApiKey));
                Assert.That(WebAccessPolicy.Evaluate(Req(path, password: false, providedKey: Key)).Method, Is.EqualTo(AccessMethod.ApiKey));
            });
        }

        [Test]
        public void Open_mode_without_key_is_open()
        {
            var d = WebAccessPolicy.Evaluate(Req(password: false, configuredKey: null, method: "POST"));
            Assert.That(d.Allowed, Is.True);
            Assert.That(d.Method, Is.EqualTo(AccessMethod.Open));
        }

        [TestCase("/")]
        [TestCase("/config")]
        [TestCase("/_blazor")]
        [TestCase("/api/file/GetFileStream/1/2/a.mp4")]
        [TestCase("/local/a.mp3")]
        [TestCase("/img/telegram/1.jpg")]
        [TestCase("/api-docs")]
        public void Open_mode_ui_and_legacy_are_open(string path)
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(path, password: false)).Allowed, Is.True);
        }

        [Test]
        public void Open_mode_web_app_cookie_reaches_the_api_even_with_a_key()
        {
            var d = WebAccessPolicy.Evaluate(Req(password: false, cookie: true));
            Assert.That(d.Allowed, Is.True);
            Assert.That(d.Method, Is.EqualTo(AccessMethod.Cookie));
        }

        // --- password set ---------------------------------------------------

        [TestCase("/api/v1/channels")]
        [TestCase("/api/mobile/channels")]
        [TestCase("/hubs/events")]
        [TestCase("/api/file/GetFileStreamCached/1/2/a.mp4")]
        [TestCase("/api/channel/image/1")]
        [TestCase("/local/a.mp3")]
        [TestCase("/img/telegram/1.jpg")]
        [TestCase("/api-docs")]
        [TestCase("/_blazor")]
        public void Password_mode_denies_anonymous(string path)
        {
            var d = WebAccessPolicy.Evaluate(Req(path));
            Assert.That(d.Allowed, Is.False);
            Assert.That(d.Failure, Is.EqualTo(AccessFailure.LoginRequired));
        }

        [TestCase("/")]
        [TestCase("/config")]
        [TestCase("/setup")]
        public void Password_mode_redirects_browser_navigation_to_login(string path)
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(path)).Failure, Is.EqualTo(AccessFailure.RedirectToLogin));
            Assert.That(WebAccessPolicy.Evaluate(Req(path, method: "POST")).Failure, Is.EqualTo(AccessFailure.LoginRequired));
        }

        [Test]
        public void Password_mode_without_configured_key_does_not_accept_any_key()
        {
            var d = WebAccessPolicy.Evaluate(Req(configuredKey: null, providedKey: "anything"));
            Assert.That(d.Allowed, Is.False);
        }

        [TestCase("/api/v1/channels")]
        [TestCase("/api/file/GetFileStream/1/2/a.mp4")]
        [TestCase("/local/a.mp3")]
        [TestCase("/")]
        [TestCase("/_blazor")]
        public void Password_mode_cookie_gets_in(string path)
        {
            var d = WebAccessPolicy.Evaluate(Req(path, cookie: true));
            Assert.That(d.Allowed, Is.True);
            Assert.That(d.Method, Is.EqualTo(AccessMethod.Cookie));
        }

        [TestCase("/api/v1/channels")]
        [TestCase("/api/mobile/stream/1/2")]
        [TestCase("/api/file/GetFileByTfmId/a.mp3")]
        [TestCase("/local/a.mp3")]
        [TestCase("/hubs/transfers")]
        public void Password_mode_api_key_gets_in(string path)
        {
            var d = WebAccessPolicy.Evaluate(Req(path, providedKey: Key, method: "POST"));
            Assert.That(d.Allowed, Is.True);
            Assert.That(d.Method, Is.EqualTo(AccessMethod.ApiKey));
        }

        [Test]
        public void Password_mode_wrong_key_is_reported_even_with_a_cookie()
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(providedKey: "wrong", cookie: true)).Failure, Is.EqualTo(AccessFailure.InvalidApiKey));
        }

        [Test]
        public void Internal_token_gets_in()
        {
            var d = WebAccessPolicy.Evaluate(Req("/api/file/FileOperations", method: "POST", internalToken: true));
            Assert.That(d.Method, Is.EqualTo(AccessMethod.Internal));
        }

        [Test]
        public void Web_auth_and_ping_are_reachable_without_credentials()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WebAccessPolicy.Evaluate(Req("/api/v1/web-auth/status")).Allowed, Is.True);
                Assert.That(WebAccessPolicy.Evaluate(Req("/api/v1/web-auth/login", "POST", requestedWith: "tfm")).Allowed, Is.True);
                Assert.That(WebAccessPolicy.Evaluate(Req("/api/v1/system/ping")).Allowed, Is.True);
                // Apps still learn that their key is wrong.
                Assert.That(WebAccessPolicy.Evaluate(Req("/api/v1/system/ping", providedKey: "wrong")).Failure, Is.EqualTo(AccessFailure.InvalidApiKey));
            });
        }

        [TestCase("/app/login")]
        [TestCase("/css/site.css")]
        [TestCase("/webdav/x")]
        public void Public_paths_stay_public(string path)
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(path)).Allowed, Is.True);
        }

        // --- LAN bypass -----------------------------------------------------

        [Test]
        public void Lan_bypass_lets_genuinely_local_requests_in()
        {
            var d = WebAccessPolicy.Evaluate(Req("/config", lan: true, ip: "192.168.1.30"));
            Assert.That(d.Allowed, Is.True);
            Assert.That(d.Method, Is.EqualTo(AccessMethod.Lan));
            Assert.That(WebAccessPolicy.Evaluate(Req("/api/v1/channels", lan: true, ip: "::1", host: "localhost")).Method, Is.EqualTo(AccessMethod.Lan));
        }

        [Test]
        public void Lan_bypass_is_off_unless_enabled()
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(lan: false, ip: "192.168.1.30")).Allowed, Is.False);
        }

        [Test]
        public void Lan_bypass_never_applies_through_a_proxy()
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(lan: true, ip: "172.18.0.2", proxyHeaders: true)).Allowed, Is.False);
        }

        [Test]
        public void Lan_bypass_needs_a_private_peer()
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(lan: true, ip: "198.51.100.4")).Allowed, Is.False);
        }

        [Test]
        public void Lan_bypass_refuses_public_host_names_dns_rebinding()
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(lan: true, ip: "192.168.1.30", host: "evil.example.com")).Allowed, Is.False);
        }

        [Test]
        public void Lan_bypass_refuses_cross_site_requests()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WebAccessPolicy.Evaluate(Req(lan: true, ip: "192.168.1.30", fetchSite: "cross-site")).Allowed, Is.False);
                Assert.That(WebAccessPolicy.Evaluate(Req(lan: true, ip: "192.168.1.30", origin: "https://evil.example.com")).Allowed, Is.False);
                Assert.That(WebAccessPolicy.Evaluate(Req(lan: true, ip: "192.168.1.30", origin: "http://192.168.1.10:5000", fetchSite: "same-origin")).Allowed, Is.True);
            });
        }

        [Test]
        public void Lan_bypass_is_ignored_without_password()
        {
            Assert.That(WebAccessPolicy.LanBypassApplies(Req(password: false, lan: true, ip: "192.168.1.30")), Is.False);
        }

        // --- CSRF -----------------------------------------------------------

        [TestCase("POST")]
        [TestCase("PUT")]
        [TestCase("DELETE")]
        [TestCase("PATCH")]
        public void Cookie_state_changes_need_the_header(string method)
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(method: method, cookie: true)).Failure, Is.EqualTo(AccessFailure.CsrfHeaderMissing));
            Assert.That(WebAccessPolicy.Evaluate(Req("/hubs/events/negotiate", method, cookie: true)).Failure, Is.EqualTo(AccessFailure.CsrfHeaderMissing));
            Assert.That(WebAccessPolicy.Evaluate(Req(method: method, cookie: true, requestedWith: "tfm")).Allowed, Is.True);
        }

        [Test]
        public void Wrong_header_value_is_rejected()
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(method: "POST", cookie: true, requestedWith: "XMLHttpRequest")).Failure,
                Is.EqualTo(AccessFailure.CsrfHeaderMissing));
        }

        [Test]
        public void Cookie_reads_do_not_need_the_header()
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(cookie: true)).Allowed, Is.True);
        }

        [Test]
        public void Lan_state_changes_need_the_header()
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(method: "POST", lan: true, ip: "192.168.1.30")).Failure, Is.EqualTo(AccessFailure.CsrfHeaderMissing));
        }

        [Test]
        public void Api_key_requests_are_exempt()
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(method: "POST", providedKey: Key)).Allowed, Is.True);
            Assert.That(WebAccessPolicy.Evaluate(Req(method: "POST", password: false, providedKey: Key, cookie: true)).Allowed, Is.True);
        }

        [Test]
        public void Web_auth_state_changes_need_the_header_even_anonymous()
        {
            Assert.That(WebAccessPolicy.Evaluate(Req("/api/v1/web-auth/login", "POST")).Failure, Is.EqualTo(AccessFailure.CsrfHeaderMissing));
            Assert.That(WebAccessPolicy.Evaluate(Req("/api/v1/web-auth/setup", "POST", password: false)).Failure, Is.EqualTo(AccessFailure.CsrfHeaderMissing));
        }

        [TestCase("/api/file/FileOperations")]
        [TestCase("/api/file/Upload")]
        [TestCase("/api/temp/Upload")]
        public void Blazor_form_posts_are_exempt(string path)
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(path, "POST", cookie: true)).Allowed, Is.True);
        }

        [Test]
        public void Blazor_circuit_negotiate_is_not_under_the_header_rule()
        {
            Assert.That(WebAccessPolicy.Evaluate(Req("/_blazor/negotiate", "POST", cookie: true)).Allowed, Is.True);
        }

        [Test]
        public void Open_mode_requests_without_cookie_keep_working_without_header()
        {
            Assert.That(WebAccessPolicy.Evaluate(Req(method: "POST", password: false, configuredKey: null)).Allowed, Is.True);
        }

        [Test]
        public void Api_key_comparison()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WebAccessPolicy.ApiKeyMatches("abc", "abc"), Is.True);
                Assert.That(WebAccessPolicy.ApiKeyMatches("abc", "abd"), Is.False);
                Assert.That(WebAccessPolicy.ApiKeyMatches("abc", "abcd"), Is.False);
                Assert.That(WebAccessPolicy.ApiKeyMatches(null, null), Is.False);
                Assert.That(WebAccessPolicy.ApiKeyMatches("", ""), Is.False);
            });
        }
    }

    public class LoginRateLimiterTest
    {
        private sealed class ManualClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Now;
        }

        [Test]
        public void Five_failures_per_minute_are_tolerated_then_locked_out()
        {
            var clock = new ManualClock();
            var limiter = new LoginRateLimiter(clock);
            for (int i = 0; i < LoginRateLimiter.MaxFailures; i++)
            {
                Assert.That(limiter.IsAllowed("1.2.3.4", out _), Is.True);
                Assert.That(limiter.RegisterFailure("1.2.3.4"), Is.False);
            }
            Assert.That(limiter.RegisterFailure("1.2.3.4"), Is.True);
            Assert.That(limiter.IsAllowed("1.2.3.4", out var retry), Is.False);
            Assert.That(retry, Is.EqualTo(LoginRateLimiter.BaseLockout));
            // Other IPs are not affected
            Assert.That(limiter.IsAllowed("5.6.7.8", out _), Is.True);

            clock.Now += LoginRateLimiter.BaseLockout;
            Assert.That(limiter.IsAllowed("1.2.3.4", out _), Is.True);
        }

        [Test]
        public void Failures_outside_the_window_do_not_count()
        {
            var clock = new ManualClock();
            var limiter = new LoginRateLimiter(clock);
            for (int i = 0; i < 20; i++)
            {
                Assert.That(limiter.RegisterFailure("ip"), Is.False);
                clock.Now += TimeSpan.FromSeconds(13); // < 6 per minute
            }
            Assert.That(limiter.IsAllowed("ip", out _), Is.True);
        }

        [Test]
        public void Lockouts_back_off_exponentially_with_a_cap()
        {
            var clock = new ManualClock();
            var limiter = new LoginRateLimiter(clock);
            var expected = LoginRateLimiter.BaseLockout;
            for (int round = 0; round < 8; round++)
            {
                for (int i = 0; i <= LoginRateLimiter.MaxFailures; i++) limiter.RegisterFailure("ip");
                Assert.That(limiter.IsAllowed("ip", out var retry), Is.False);
                Assert.That(retry, Is.EqualTo(expected), $"round {round}");
                clock.Now += retry;
                expected = TimeSpan.FromTicks(Math.Min(expected.Ticks * 2, LoginRateLimiter.MaxLockout.Ticks));
            }
        }

        [Test]
        public void Success_clears_the_record()
        {
            var clock = new ManualClock();
            var limiter = new LoginRateLimiter(clock);
            for (int i = 0; i < LoginRateLimiter.MaxFailures; i++) limiter.RegisterFailure("ip");
            limiter.RegisterSuccess("ip");
            Assert.That(limiter.RegisterFailure("ip"), Is.False);
        }
    }

    public class WebAuthServiceTest
    {
        /// <summary>IDbService stand-in that only implements the web-auth document.</summary>
        public class FakeDb : DispatchProxy
        {
            public WebAuthSettings? Stored;
            public bool Fail;

            protected override object? Invoke(MethodInfo? method, object?[]? args)
            {
                if (Fail) throw new InvalidOperationException("db down");
                switch (method!.Name)
                {
                    case nameof(IDbService.LoadWebAuth):
                        return Task.FromResult(Stored?.Clone());
                    case nameof(IDbService.SaveWebAuth):
                        Stored = ((WebAuthSettings)args![0]!).Clone();
                        return Task.CompletedTask;
                    default:
                        throw new NotSupportedException(method.Name);
                }
            }
        }

        private string _mirror = null!;

        [SetUp]
        public void SetUp() => _mirror = Path.Combine(Path.GetTempPath(), $"tfm-webauth-{Guid.NewGuid():N}.json");

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_mirror)) File.Delete(_mirror);
        }

        private (WebAuthService Service, FakeDb Db) Create(FakeDb? db = null)
        {
            var proxy = db == null ? DispatchProxy.Create<IDbService, FakeDb>() : (IDbService)(object)db;
            return (new WebAuthService(proxy, NullLogger<WebAuthService>.Instance, _mirror), (FakeDb)(object)proxy);
        }

        [Test]
        public async Task Setup_change_and_remove_rotate_the_stamp()
        {
            var (svc, db) = Create();
            await svc.LoadAsync(databaseAvailable: true);
            Assert.That(svc.PasswordSet, Is.False);
            var stamp0 = svc.SecurityStamp;

            Assert.That((await svc.SetupAsync("short")).Outcome, Is.EqualTo(WebAuthOutcome.WeakPassword));
            Assert.That((await svc.SetupAsync("first-password")).Succeeded, Is.True);
            Assert.That(svc.PasswordSet, Is.True);
            Assert.That(db.Stored?.PasswordHash, Is.Not.Null.And.Not.Contain("first-password"));
            var stamp1 = svc.SecurityStamp;
            Assert.That(stamp1, Is.Not.EqualTo(stamp0));

            Assert.That((await svc.SetupAsync("another-password")).Outcome, Is.EqualTo(WebAuthOutcome.AlreadySet));
            Assert.That((await svc.ChangePasswordAsync("wrong-password", "second-password")).Outcome, Is.EqualTo(WebAuthOutcome.InvalidCredentials));
            Assert.That((await svc.ChangePasswordAsync("first-password", "second-password")).Succeeded, Is.True);
            Assert.That(svc.VerifyPassword("second-password"), Is.True);
            Assert.That(svc.VerifyPassword("first-password"), Is.False);
            Assert.That(svc.SecurityStamp, Is.Not.EqualTo(stamp1));

            await svc.SetLanBypassAsync(true);
            Assert.That(svc.LanBypassEnabled, Is.True);

            Assert.That((await svc.RemovePasswordAsync("first-password")).Outcome, Is.EqualTo(WebAuthOutcome.InvalidCredentials));
            Assert.That((await svc.RemovePasswordAsync("second-password")).Succeeded, Is.True);
            Assert.That(svc.PasswordSet, Is.False);
            Assert.That((await svc.RemovePasswordAsync("second-password")).Outcome, Is.EqualTo(WebAuthOutcome.NotSet));
        }

        [Test]
        public async Task Password_survives_an_unreachable_database_fail_closed()
        {
            var (svc, db) = Create();
            await svc.LoadAsync(true);
            await svc.SetupAsync("first-password");

            db.Fail = true;
            var (restarted, _) = Create(db);
            await restarted.LoadAsync(databaseAvailable: true);
            Assert.That(restarted.PasswordSet, Is.True);
            Assert.That(restarted.VerifyPassword("first-password"), Is.True);

            var (noDb, _) = Create(db);
            await noDb.LoadAsync(databaseAvailable: false);
            Assert.That(noDb.PasswordSet, Is.True);
        }

        [Test]
        public async Task Empty_database_does_not_remove_the_password()
        {
            var (svc, db) = Create();
            await svc.LoadAsync(true);
            await svc.SetupAsync("first-password");

            db.Stored = null; // e.g. pointed to a new MongoDB
            var (restarted, _) = Create(db);
            await restarted.LoadAsync(true);
            Assert.That(restarted.PasswordSet, Is.True);
            Assert.That(db.Stored?.PasswordHash, Is.Not.Null, "copied back to the database");
        }

        [Test]
        public async Task Database_wins_when_it_has_a_record()
        {
            var (svc, db) = Create();
            await svc.LoadAsync(true);
            await svc.SetupAsync("first-password");
            db.Stored = new WebAuthSettings { PasswordHash = null };

            var (restarted, _) = Create(db);
            await restarted.LoadAsync(true);
            Assert.That(restarted.PasswordSet, Is.False);
        }

        [Test]
        public void Internal_token_is_random_and_checked()
        {
            var (a, _) = Create();
            var (b, _) = Create();
            Assert.Multiple(() =>
            {
                Assert.That(a.InternalToken, Is.Not.EqualTo(b.InternalToken));
                Assert.That(a.IsInternalToken(a.InternalToken), Is.True);
                Assert.That(a.IsInternalToken(b.InternalToken), Is.False);
                Assert.That(a.IsInternalToken(null), Is.False);
                Assert.That(a.IsInternalToken(""), Is.False);
            });
        }
    }
}

namespace Test
{
    using Microsoft.Extensions.Logging.Abstractions;
    using TelegramDownloader.Middleware;
    using TelegramDownloader.Services.WebAuth;
    using TelegramDownloader.Models;

    public class WebAccessMiddlewareTest
    {
        private sealed class StubAuth : IWebAuthService
        {
            public bool PasswordSet { get; set; }
            public bool LanBypassEnabled { get; set; }
            public string SecurityStamp => "stamp";
            public string InternalToken => "internal";
            public bool IsInternalToken(string? value) => value == InternalToken;
            public LoginRateLimiter RateLimiter { get; } = new();
            public event Action? Changed { add { } remove { } }
            public Task LoadAsync(bool databaseAvailable) => Task.CompletedTask;
            public bool VerifyPassword(string? password) => false;
            public Task<WebAuthResult> SetupAsync(string? password) => throw new NotSupportedException();
            public Task<WebAuthResult> ChangePasswordAsync(string? c, string? n) => throw new NotSupportedException();
            public Task<WebAuthResult> RemovePasswordAsync(string? c) => throw new NotSupportedException();
            public Task SetLanBypassAsync(bool enabled) => throw new NotSupportedException();
            public IReadOnlyList<WebPasskey> Passkeys => Array.Empty<WebPasskey>();
            public string? PasskeyUserHandle => null;
            public string? PasskeyRpId => null;
            public IReadOnlyList<string> PasskeyOrigins => Array.Empty<string>();
            public Task<bool> UpdatePasskeysAsync(Func<WebAuthSettings, bool> change) => throw new NotSupportedException();
        }

        private static async Task<(Microsoft.AspNetCore.Http.HttpContext Ctx, bool NextCalled)> Run(
            string path, StubAuth auth, string method = "GET", string remoteIp = "203.0.113.9",
            Action<Microsoft.AspNetCore.Http.HttpRequest>? setup = null)
        {
            var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            ctx.Request.Method = method;
            ctx.Request.Scheme = "http";
            ctx.Request.Host = new Microsoft.AspNetCore.Http.HostString("192.168.1.10", 5000);
            var q = path.IndexOf('?');
            ctx.Request.Path = q < 0 ? path : path[..q];
            if (q >= 0) ctx.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString(path[q..]);
            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
            ctx.Response.Body = new MemoryStream();
            setup?.Invoke(ctx.Request);

            var next = false;
            await new ConnectionInfoCaptureMiddleware(_ => Task.CompletedTask).InvokeAsync(ctx);
            var mw = new WebAccessMiddleware(_ => { next = true; return Task.CompletedTask; }, NullLogger<WebAccessMiddleware>.Instance);
            await mw.InvokeAsync(ctx, auth);
            return (ctx, next);
        }

        private static string Body(Microsoft.AspNetCore.Http.HttpContext ctx)
        {
            ctx.Response.Body.Position = 0;
            return new StreamReader(ctx.Response.Body).ReadToEnd();
        }

        [TearDown]
        public void Reset() => GeneralConfigStatic.config.MobileApiKey = null;

        [Test]
        public async Task Blazor_navigation_is_redirected_to_the_login_page()
        {
            var (ctx, next) = await Run("/config?tab=1", new StubAuth { PasswordSet = true });
            Assert.That(next, Is.False);
            Assert.That(ctx.Response.StatusCode, Is.EqualTo(302));
            Assert.That(ctx.Response.Headers.Location.ToString(), Is.EqualTo("/app/login?returnUrl=%2Fconfig%3Ftab%3D1"));
        }

        [Test]
        public async Task Legacy_media_accepts_the_key_in_the_query()
        {
            GeneralConfigStatic.config.MobileApiKey = "k1";
            var (_, next) = await Run("/api/file/GetFileStream/1/2/a.mp4?apiKey=k1", new StubAuth { PasswordSet = true });
            Assert.That(next, Is.True);
            var (denied, next2) = await Run("/local/a.mp3", new StubAuth { PasswordSet = true });
            Assert.That(next2, Is.False);
            Assert.That(denied.Response.StatusCode, Is.EqualTo(401));
            Assert.That(Body(denied), Does.Contain("\"code\":\"unauthorized\""));
        }

        [Test]
        public async Task Mobile_api_keeps_its_flat_error_shape()
        {
            GeneralConfigStatic.config.MobileApiKey = "k1";
            var (ctx, _) = await Run("/api/mobile/channels", new StubAuth());
            Assert.That(ctx.Response.StatusCode, Is.EqualTo(401));
            Assert.That(Body(ctx), Does.Contain("\"error\":\"API key required\""));
        }

        [Test]
        public async Task Forwarded_request_from_a_lan_proxy_does_not_get_the_lan_bypass()
        {
            var auth = new StubAuth { PasswordSet = true, LanBypassEnabled = true };
            var (_, direct) = await Run("/api/v1/channels", auth, remoteIp: "192.168.1.30");
            Assert.That(direct, Is.True);
            var (_, proxied) = await Run("/api/v1/channels", auth, remoteIp: "192.168.1.2",
                setup: r => r.Headers["X-Forwarded-For"] = "198.51.100.1");
            Assert.That(proxied, Is.False);
        }

        [Test]
        public async Task Bearer_and_access_token_are_accepted_for_hubs()
        {
            GeneralConfigStatic.config.MobileApiKey = "k1";
            var auth = new StubAuth { PasswordSet = true };
            Assert.That((await Run("/hubs/transfers?access_token=k1", auth)).NextCalled, Is.True);
            Assert.That((await Run("/hubs/transfers/negotiate", auth, "POST",
                setup: r => r.Headers.Authorization = "Bearer k1")).NextCalled, Is.True);
        }
    }
}
