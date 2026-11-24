using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramDownloader.Controllers.Api.V1;
using TelegramDownloader.Data.db;
using TelegramDownloader.Middleware;
using TelegramDownloader.Models;
using TelegramDownloader.Models.Api;
using TelegramDownloader.Services.WebAuth;

namespace Test
{
    /// <summary>
    /// A minimal WebAuthn authenticator in software: ES256 key, "none"
    /// attestation, configurable flags, counter, origin and RP ID. Behaves like
    /// a synced passkey (Bitwarden, iCloud...) by default: BE+BS set, counter 0.
    /// </summary>
    internal sealed class SoftwareAuthenticator : IDisposable
    {
        public const byte UP = 0x01, UV = 0x04, BE = 0x08, BS = 0x10, AT = 0x40;

        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(16);
        public byte[]? UserHandle { get; private set; }
        public uint Counter { get; set; }
        public byte Flags { get; set; } = UP | UV | BE | BS;

        public static string B64(byte[] b) => Base64Url.EncodeToString(b);

        public JsonElement Create(JsonObject options, string origin, string? rpIdForHash = null)
        {
            var rpId = rpIdForHash ?? options["rp"]!["id"]!.GetValue<string>();
            UserHandle = Base64Url.DecodeFromChars(options["user"]!["id"]!.GetValue<string>());
            var clientData = ClientData("webauthn.create", options["challenge"]!.GetValue<string>(), origin);

            var p = _key.ExportParameters(false);
            var cose = new CborWriter();
            cose.WriteStartMap(5);
            cose.WriteInt32(1); cose.WriteInt32(2);     // kty: EC2
            cose.WriteInt32(3); cose.WriteInt32(-7);    // alg: ES256
            cose.WriteInt32(-1); cose.WriteInt32(1);    // crv: P-256
            cose.WriteInt32(-2); cose.WriteByteString(p.Q.X!);
            cose.WriteInt32(-3); cose.WriteByteString(p.Q.Y!);
            cose.WriteEndMap();

            var authData = new List<byte>();
            authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
            authData.Add((byte)(Flags | AT));
            authData.AddRange(Counter32());
            authData.AddRange(new byte[16]); // AAGUID: zeros, like most password managers
            var len = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)CredentialId.Length);
            authData.AddRange(len);
            authData.AddRange(CredentialId);
            authData.AddRange(cose.Encode());

            var att = new CborWriter();
            att.WriteStartMap(3);
            att.WriteTextString("fmt"); att.WriteTextString("none");
            att.WriteTextString("attStmt"); att.WriteStartMap(0); att.WriteEndMap();
            att.WriteTextString("authData"); att.WriteByteString(authData.ToArray());
            att.WriteEndMap();

            return Json(new JsonObject
            {
                ["id"] = B64(CredentialId),
                ["rawId"] = B64(CredentialId),
                ["type"] = "public-key",
                ["authenticatorAttachment"] = "cross-platform",
                ["clientExtensionResults"] = new JsonObject { ["credProps"] = new JsonObject { ["rk"] = true } },
                ["response"] = new JsonObject
                {
                    ["clientDataJSON"] = B64(clientData),
                    ["attestationObject"] = B64(att.Encode()),
                    ["transports"] = new JsonArray("hybrid", "internal", "some-future-transport")
                }
            });
        }

        public JsonElement Get(JsonObject options, string origin, string? rpIdForHash = null, byte[]? userHandle = null)
        {
            var rpId = rpIdForHash ?? options["rpId"]!.GetValue<string>();
            var clientData = ClientData("webauthn.get", options["challenge"]!.GetValue<string>(), origin);
            var authData = new List<byte>();
            authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
            authData.Add(Flags);
            authData.AddRange(Counter32());
            var signed = authData.Concat(SHA256.HashData(clientData)).ToArray();
            var signature = _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

            return Json(new JsonObject
            {
                ["id"] = B64(CredentialId),
                ["rawId"] = B64(CredentialId),
                ["type"] = "public-key",
                ["clientExtensionResults"] = new JsonObject(),
                ["response"] = new JsonObject
                {
                    ["clientDataJSON"] = B64(clientData),
                    ["authenticatorData"] = B64(authData.ToArray()),
                    ["signature"] = B64(signature),
                    ["userHandle"] = B64(userHandle ?? UserHandle!)
                }
            });
        }

        private byte[] Counter32()
        {
            var c = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(c, Counter);
            return c;
        }

        private static byte[] ClientData(string type, string challenge, string origin) =>
            Encoding.UTF8.GetBytes(new JsonObject
            {
                ["type"] = type,
                ["challenge"] = challenge,
                ["origin"] = origin,
                ["crossOrigin"] = false
            }.ToJsonString());

        private static JsonElement Json(JsonObject o) => JsonDocument.Parse(o.ToJsonString()).RootElement.Clone();

        public void Dispose() => _key.Dispose();
    }

    public class PasskeyServiceTest
    {
        private const string Password = "first-password";
        private static readonly PasskeyRelyingParty Rp = new("tfm.example.com", new[] { "https://tfm.example.com" });
        private const string Origin = "https://tfm.example.com";

        private sealed class ManualClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Now;
        }

        private string _mirror = null!;
        private ManualClock _clock = null!;

        [SetUp]
        public void SetUp()
        {
            _mirror = Path.Combine(Path.GetTempPath(), $"tfm-passkey-{Guid.NewGuid():N}.json");
            _clock = new ManualClock();
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(WebAuthService.ResetEnvironmentVariable, null);
            if (File.Exists(_mirror)) File.Delete(_mirror);
        }

        private async Task<(WebAuthService Auth, PasskeyService Passkeys, WebAuthServiceTest.FakeDb Db)> Create(bool withPassword = true,
            WebAuthServiceTest.FakeDb? db = null)
        {
            var proxy = db == null ? DispatchProxy.Create<IDbService, WebAuthServiceTest.FakeDb>() : (IDbService)(object)db;
            var auth = new WebAuthService(proxy, NullLogger<WebAuthService>.Instance, _mirror);
            await auth.LoadAsync(databaseAvailable: true);
            if (withPassword && !auth.PasswordSet) await auth.SetupAsync(Password);
            return (auth, new PasskeyService(auth, NullLogger<PasskeyService>.Instance, _clock), (WebAuthServiceTest.FakeDb)(object)proxy);
        }

        private static async Task<SoftwareAuthenticator> Register(PasskeyService svc, string name = "Bitwarden")
        {
            var (result, begin) = await svc.BeginRegistrationAsync(Password, Rp);
            Assert.That(result.Succeeded, Is.True, result.Message);
            var authenticator = new SoftwareAuthenticator();
            var finish = await svc.FinishRegistrationAsync(begin!.RequestId, name, authenticator.Create(begin.Options, Origin), Rp);
            Assert.That(finish.Succeeded, Is.True, finish.Message);
            return authenticator;
        }

        private static async Task<PasskeyResult> Login(PasskeyService svc, SoftwareAuthenticator a, string origin = Origin, string? rpIdForHash = null)
        {
            var (result, begin) = svc.BeginLogin(Rp);
            Assert.That(result.Succeeded, Is.True, result.Message);
            return await svc.FinishLoginAsync(begin!.RequestId, a.Get(begin.Options, origin, rpIdForHash), Rp);
        }

        [Test]
        public async Task Creation_options_suit_synced_passkeys()
        {
            var (_, svc, _) = await Create();
            var (_, begin) = await svc.BeginRegistrationAsync(Password, Rp);
            var o = begin!.Options;
            var sel = o["authenticatorSelection"]!.AsObject();
            Assert.Multiple(() =>
            {
                Assert.That(o["rp"]!["id"]!.GetValue<string>(), Is.EqualTo("tfm.example.com"));
                Assert.That(o["user"]!["name"]!.GetValue<string>(), Is.EqualTo("TFM"));
                Assert.That(o["user"]!["displayName"]!.GetValue<string>(), Is.EqualTo("Telegram File Manager"));
                Assert.That(Base64Url.DecodeFromChars(o["user"]!["id"]!.GetValue<string>()), Has.Length.EqualTo(32));
                Assert.That(sel["residentKey"]!.GetValue<string>(), Is.EqualTo("required"));
                Assert.That(sel["requireResidentKey"]!.GetValue<bool>(), Is.True);
                Assert.That(sel["userVerification"]!.GetValue<string>(), Is.EqualTo("preferred"));
                Assert.That(sel.ContainsKey("authenticatorAttachment"), Is.False);
                Assert.That(o["attestation"]!.GetValue<string>(), Is.EqualTo("none"));
                Assert.That(o["pubKeyCredParams"]!.AsArray().Select(p => p!["alg"]!.GetValue<int>()), Is.EqualTo(new[] { -7, -8, -257 }));
                Assert.That(o.ToJsonString(), Does.Not.Contain("null"));
            });
        }

        [Test]
        public async Task Register_and_sign_in_with_a_synced_passkey_counter_zero()
        {
            var (auth, svc, db) = await Create();
            using var a = await Register(svc);

            var stored = auth.Passkeys.Single();
            Assert.Multiple(() =>
            {
                Assert.That(stored.Name, Is.EqualTo("Bitwarden"));
                Assert.That(stored.CredentialId, Is.EqualTo(SoftwareAuthenticator.B64(a.CredentialId)));
                Assert.That(stored.BackupEligible, Is.True);
                Assert.That(stored.BackedUp, Is.True);
                Assert.That(stored.SignCount, Is.EqualTo(0));
                Assert.That(stored.UserHandle, Is.EqualTo(auth.PasskeyUserHandle));
                Assert.That(stored.Transports, Is.EqualTo(new[] { "hybrid", "internal", "some-future-transport" }));
                Assert.That(stored.AaGuid, Is.EqualTo(Guid.Empty.ToString()));
                Assert.That(stored.RpId, Is.EqualTo("tfm.example.com"));
                Assert.That(db.Stored!.Passkeys, Has.Count.EqualTo(1), "persisted to MongoDB");
                Assert.That(File.ReadAllText(_mirror), Does.Contain(stored.CredentialId), "and to the local copy");
            });

            // Counter 0 on every use (synced passkeys) is fine.
            for (var i = 0; i < 3; i++)
            {
                var login = await Login(svc, a);
                Assert.That(login.Succeeded, Is.True, login.Message);
            }
            Assert.That(auth.Passkeys.Single().LastUsedUtc, Is.EqualTo(_clock.Now.UtcDateTime));

            // Survives a restart with MongoDB down (local copy).
            db.Fail = true;
            var restarted = new WebAuthService(DispatchProxy.Create<IDbService, WebAuthServiceTest.FakeDb>(), NullLogger<WebAuthService>.Instance, _mirror);
            await restarted.LoadAsync(databaseAvailable: false);
            Assert.That(restarted.Passkeys.Single().CredentialId, Is.EqualTo(stored.CredentialId));
            Assert.That(restarted.PasskeyUserHandle, Is.EqualTo(auth.PasskeyUserHandle));
        }

        [Test]
        public async Task Second_registration_excludes_known_credentials_and_rejects_duplicates()
        {
            var (auth, svc, _) = await Create();
            using var a = await Register(svc);
            var handle = auth.PasskeyUserHandle;

            var (_, begin) = await svc.BeginRegistrationAsync(Password, Rp);
            var excluded = begin!.Options["excludeCredentials"]!.AsArray().Select(c => c!["id"]!.GetValue<string>());
            Assert.That(excluded, Is.EqualTo(new[] { SoftwareAuthenticator.B64(a.CredentialId) }));
            Assert.That(begin.Options["user"]!["id"]!.GetValue<string>(), Is.EqualTo(handle), "stable user handle");

            var dup = await svc.FinishRegistrationAsync(begin.RequestId, "again", a.Create(begin.Options, Origin), Rp);
            Assert.That(dup.Outcome, Is.EqualTo(PasskeyOutcome.AlreadyRegistered));
            Assert.That(auth.Passkeys, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task Registration_needs_the_current_password_and_a_password()
        {
            var (_, open, _) = await Create(withPassword: false);
            Assert.That((await open.BeginRegistrationAsync(Password, Rp)).Result.Outcome, Is.EqualTo(PasskeyOutcome.PasswordNotSet));
            Assert.That(open.BeginLogin(Rp).Result.Outcome, Is.EqualTo(PasskeyOutcome.PasswordNotSet));

            var (_, svc, _) = await Create();
            Assert.That((await svc.BeginRegistrationAsync("wrong-password", Rp)).Result.Outcome, Is.EqualTo(PasskeyOutcome.InvalidCredentials));
            Assert.That((await svc.BeginRegistrationAsync(null, Rp)).Result.Outcome, Is.EqualTo(PasskeyOutcome.InvalidCredentials));
        }

        [Test]
        public async Task Wrong_origin_is_rejected_on_both_ceremonies()
        {
            var (auth, svc, _) = await Create();
            var (_, begin) = await svc.BeginRegistrationAsync(Password, Rp);
            using var evil = new SoftwareAuthenticator();
            var reg = await svc.FinishRegistrationAsync(begin!.RequestId, "x", evil.Create(begin.Options, "https://evil.example.net"), Rp);
            Assert.That(reg.Outcome, Is.EqualTo(PasskeyOutcome.Invalid));
            Assert.That(auth.Passkeys, Is.Empty);

            using var a = await Register(svc);
            Assert.That((await Login(svc, a, origin: "https://evil.example.net")).Outcome, Is.EqualTo(PasskeyOutcome.Invalid));
            Assert.That((await Login(svc, a, origin: "http://tfm.example.com")).Outcome, Is.EqualTo(PasskeyOutcome.Invalid), "scheme is part of the origin");
        }

        [Test]
        public async Task Wrong_rp_id_hash_is_rejected_on_both_ceremonies()
        {
            var (auth, svc, _) = await Create();
            var (_, begin) = await svc.BeginRegistrationAsync(Password, Rp);
            using var evil = new SoftwareAuthenticator();
            var reg = await svc.FinishRegistrationAsync(begin!.RequestId, "x", evil.Create(begin.Options, Origin, rpIdForHash: "example.com"), Rp);
            Assert.That(reg.Outcome, Is.EqualTo(PasskeyOutcome.Invalid));
            Assert.That(auth.Passkeys, Is.Empty);

            using var a = await Register(svc);
            Assert.That((await Login(svc, a, rpIdForHash: "other.example.com")).Outcome, Is.EqualTo(PasskeyOutcome.Invalid));
        }

        [Test]
        public async Task Challenges_are_single_use_expire_and_are_bound_to_the_ceremony()
        {
            var (_, svc, _) = await Create();
            using var a = await Register(svc);

            // Replay of a used challenge.
            var (_, begin) = svc.BeginLogin(Rp);
            var assertion = a.Get(begin!.Options, Origin);
            Assert.That((await svc.FinishLoginAsync(begin.RequestId, assertion, Rp)).Succeeded, Is.True);
            Assert.That((await svc.FinishLoginAsync(begin.RequestId, assertion, Rp)).Outcome, Is.EqualTo(PasskeyOutcome.ChallengeInvalid));

            // Expired (5 minutes).
            var (_, late) = svc.BeginLogin(Rp);
            _clock.Now += TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1);
            Assert.That((await svc.FinishLoginAsync(late!.RequestId, a.Get(late.Options, Origin), Rp)).Outcome, Is.EqualTo(PasskeyOutcome.ChallengeInvalid));

            // A registration challenge cannot finish a login, and is burnt by the attempt.
            var (_, reg) = await svc.BeginRegistrationAsync(Password, Rp);
            var asLogin = a.Get(new JsonObject { ["rpId"] = "tfm.example.com", ["challenge"] = reg!.Options["challenge"]!.GetValue<string>() }, Origin);
            Assert.That((await svc.FinishLoginAsync(reg.RequestId, asLogin, Rp)).Outcome, Is.EqualTo(PasskeyOutcome.ChallengeInvalid));
            using var b = new SoftwareAuthenticator();
            Assert.That((await svc.FinishRegistrationAsync(reg.RequestId, "b", b.Create(reg.Options, Origin), Rp)).Outcome,
                Is.EqualTo(PasskeyOutcome.ChallengeInvalid));

            // Another RP ID than the one of the begin call.
            var (_, other) = svc.BeginLogin(Rp);
            var otherRp = new PasskeyRelyingParty("example.org", new[] { "https://example.org" });
            Assert.That((await svc.FinishLoginAsync(other!.RequestId, a.Get(other.Options, Origin), otherRp)).Outcome,
                Is.EqualTo(PasskeyOutcome.ChallengeInvalid));

            // Unknown ids.
            Assert.That((await svc.FinishLoginAsync("nope", assertion, Rp)).Outcome, Is.EqualTo(PasskeyOutcome.ChallengeInvalid));
            Assert.That((await svc.FinishLoginAsync(null, assertion, Rp)).Outcome, Is.EqualTo(PasskeyOutcome.ChallengeInvalid));
        }

        [Test]
        public async Task Counter_must_grow_once_the_authenticator_uses_one()
        {
            var (auth, svc, _) = await Create();
            using var a = await Register(svc);
            a.Flags = SoftwareAuthenticator.UP | SoftwareAuthenticator.UV; // a security key: not synced

            a.Counter = 5;
            Assert.That((await Login(svc, a)).Succeeded, Is.True);
            Assert.That(auth.Passkeys.Single().SignCount, Is.EqualTo(5));
            Assert.That(auth.Passkeys.Single().BackedUp, Is.False, "BS flag refreshed on use");

            Assert.That((await Login(svc, a)).Outcome, Is.EqualTo(PasskeyOutcome.Invalid), "same counter: possible clone");
            a.Counter = 3;
            Assert.That((await Login(svc, a)).Outcome, Is.EqualTo(PasskeyOutcome.Invalid));
            a.Counter = 6;
            Assert.That((await Login(svc, a)).Succeeded, Is.True);
            a.Counter = 0;
            Assert.That((await Login(svc, a)).Succeeded, Is.True, "0 means the authenticator has no counter");
            Assert.That(auth.Passkeys.Single().SignCount, Is.EqualTo(6));

            Assert.Multiple(() =>
            {
                Assert.That(PasskeyService.SignCountAcceptable(0, 0), Is.True);
                Assert.That(PasskeyService.SignCountAcceptable(0, 7), Is.True);
                Assert.That(PasskeyService.SignCountAcceptable(7, 0), Is.True);
                Assert.That(PasskeyService.SignCountAcceptable(7, 8), Is.True);
                Assert.That(PasskeyService.SignCountAcceptable(7, 7), Is.False);
                Assert.That(PasskeyService.SignCountAcceptable(7, 2), Is.False);
            });
        }

        [Test]
        public async Task Unknown_credential_tampered_signature_and_foreign_user_handle_are_rejected()
        {
            var (_, svc, _) = await Create();
            using var a = await Register(svc);
            using var stranger = new SoftwareAuthenticator();
            var (_, begin) = await svc.BeginRegistrationAsync(Password, Rp);
            stranger.Create(begin!.Options, Origin); // never finished: unknown to the server
            Assert.That((await Login(svc, stranger)).Outcome, Is.EqualTo(PasskeyOutcome.Invalid));

            var (_, b2) = svc.BeginLogin(Rp);
            var wrongUser = a.Get(b2!.Options, Origin, userHandle: RandomNumberGenerator.GetBytes(32));
            Assert.That((await svc.FinishLoginAsync(b2.RequestId, wrongUser, Rp)).Outcome, Is.EqualTo(PasskeyOutcome.Invalid));

            var (_, b3) = svc.BeginLogin(Rp);
            var node = JsonNode.Parse(a.Get(b3!.Options, Origin).GetRawText())!;
            var sig = Base64Url.DecodeFromChars(node["response"]!["signature"]!.GetValue<string>());
            sig[^1] ^= 0xFF;
            node["response"]!["signature"] = Base64Url.EncodeToString(sig);
            var tampered = JsonDocument.Parse(node.ToJsonString()).RootElement;
            Assert.That((await svc.FinishLoginAsync(b3.RequestId, tampered, Rp)).Outcome, Is.EqualTo(PasskeyOutcome.Invalid));

            var (_, b4) = svc.BeginLogin(Rp);
            Assert.That((await svc.FinishLoginAsync(b4!.RequestId, JsonDocument.Parse("\"junk\"").RootElement, Rp)).Outcome,
                Is.EqualTo(PasskeyOutcome.Invalid));
        }

        [Test]
        public async Task Removing_the_password_removes_the_passkeys()
        {
            var (auth, svc, db) = await Create();
            using var a = await Register(svc);
            Assert.That((await auth.RemovePasswordAsync(Password)).Succeeded, Is.True);
            Assert.That(auth.Passkeys, Is.Empty);
            Assert.That(db.Stored!.Passkeys, Is.Empty);
            Assert.That(svc.BeginLogin(Rp).Result.Outcome, Is.EqualTo(PasskeyOutcome.PasswordNotSet));
        }

        [Test]
        public async Task Reset_variable_removes_the_passkeys_too()
        {
            var (_, svc, db) = await Create();
            using var a = await Register(svc);

            Environment.SetEnvironmentVariable(WebAuthService.ResetEnvironmentVariable, "true");
            var (restarted, _, _) = await Create(withPassword: false, db: db);
            Assert.That(restarted.PasswordSet, Is.False);
            Assert.That(restarted.Passkeys, Is.Empty);
            Assert.That(db.Stored!.Passkeys, Is.Empty);
            Assert.That(File.ReadAllText(_mirror), Does.Not.Contain(SoftwareAuthenticator.B64(a.CredentialId)));
        }

        [Test]
        public async Task Changing_the_password_keeps_the_passkeys()
        {
            var (auth, svc, _) = await Create();
            using var a = await Register(svc);
            Assert.That((await auth.ChangePasswordAsync(Password, "second-password")).Succeeded, Is.True);
            Assert.That(auth.Passkeys, Has.Count.EqualTo(1));
            Assert.That((await Login(svc, a)).Succeeded, Is.True);
        }

        [Test]
        public async Task Rename_and_delete()
        {
            var (auth, svc, _) = await Create();
            using var a = await Register(svc);
            var id = SoftwareAuthenticator.B64(a.CredentialId);

            Assert.That((await svc.RenameAsync(id, "  Phone  ")).Passkey!.Name, Is.EqualTo("Phone"));
            Assert.That(auth.Passkeys.Single().Name, Is.EqualTo("Phone"));
            Assert.That((await svc.RenameAsync(id, "   ")).Outcome, Is.EqualTo(PasskeyOutcome.Invalid));
            Assert.That((await svc.RenameAsync("missing", "x")).Outcome, Is.EqualTo(PasskeyOutcome.NotFound));
            Assert.That((await svc.RenameAsync(id, new string('n', 100))).Passkey!.Name, Has.Length.EqualTo(PasskeyService.MaxNameLength));

            Assert.That((await svc.DeleteAsync("missing")).Outcome, Is.EqualTo(PasskeyOutcome.NotFound));
            Assert.That((await svc.DeleteAsync(id)).Succeeded, Is.True);
            Assert.That(auth.Passkeys, Is.Empty);
            Assert.That((await Login(svc, a)).Outcome, Is.EqualTo(PasskeyOutcome.Invalid));
        }

        [Test]
        public async Task Relying_party_override_is_validated_and_used()
        {
            var (auth, svc, _) = await Create();
            Assert.That((await svc.SetRelyingPartyAsync("https://example.com", null)).Outcome, Is.EqualTo(PasskeyOutcome.InvalidSettings));
            Assert.That((await svc.SetRelyingPartyAsync("192.168.1.10", null)).Outcome, Is.EqualTo(PasskeyOutcome.InvalidSettings));
            Assert.That((await svc.SetRelyingPartyAsync("example.com", new[] { "https://other.org" })).Outcome, Is.EqualTo(PasskeyOutcome.InvalidSettings));
            Assert.That((await svc.SetRelyingPartyAsync("example.com", new[] { "ftp://tfm.example.com" })).Outcome, Is.EqualTo(PasskeyOutcome.InvalidSettings));
            Assert.That((await svc.SetRelyingPartyAsync("example.com", new[] { "https://tfm.example.com/app" })).Outcome, Is.EqualTo(PasskeyOutcome.InvalidSettings));

            var ok = await svc.SetRelyingPartyAsync(" Example.COM ", new[] { "https://TFM.example.com:443/", "https://tfm.example.com:8443", "", "https://tfm.example.com" });
            Assert.That(ok.Succeeded, Is.True, ok.Message);
            Assert.That(auth.PasskeyRpId, Is.EqualTo("example.com"));
            Assert.That(auth.PasskeyOrigins, Is.EqualTo(new[] { "https://tfm.example.com", "https://tfm.example.com:8443" }));

            // A request arriving as http://tfm:5000 (proxy not forwarding the host) uses the override.
            var rp = PasskeyRelyingParty.Resolve("http", "tfm", 5000, auth.PasskeyRpId, auth.PasskeyOrigins);
            Assert.That(rp.RpId, Is.EqualTo("example.com"));
            var (_, begin) = await svc.BeginRegistrationAsync(Password, rp);
            using var a = new SoftwareAuthenticator();
            Assert.That((await svc.FinishRegistrationAsync(begin!.RequestId, "p", a.Create(begin.Options, "https://tfm.example.com:8443"), rp)).Succeeded, Is.True);

            Assert.That((await svc.SetRelyingPartyAsync(null, null)).Succeeded, Is.True);
            Assert.That(auth.PasskeyRpId, Is.Null);
            Assert.That(auth.PasskeyOrigins, Is.Empty);
        }

        [Test]
        public void Relying_party_from_the_request()
        {
            Assert.Multiple(() =>
            {
                var rp = PasskeyRelyingParty.Resolve("https", "TFM.Example.com", null, null, null);
                Assert.That(rp.RpId, Is.EqualTo("tfm.example.com"));
                Assert.That(rp.Origins, Is.EqualTo(new[] { "https://tfm.example.com" }));
                Assert.That(rp.Supported, Is.True);

                Assert.That(PasskeyRelyingParty.Resolve("https", "tfm.example.com", 443, null, null).Origins.Single(), Is.EqualTo("https://tfm.example.com"));
                Assert.That(PasskeyRelyingParty.Resolve("http", "localhost", 5000, null, null).Origins.Single(), Is.EqualTo("http://localhost:5000"));
                Assert.That(PasskeyRelyingParty.Resolve("http", "localhost", 5000, null, null).Supported, Is.True);
                Assert.That(PasskeyRelyingParty.Resolve("http", "nas", 80, null, null).Supported, Is.True);
                Assert.That(PasskeyRelyingParty.Resolve("http", "192.168.1.10", 5000, null, null).Supported, Is.False);
                Assert.That(PasskeyRelyingParty.Resolve("http", "[::1]", 5000, null, null).Supported, Is.False);
                Assert.That(PasskeyRelyingParty.Resolve("http", "[::1]", 5000, null, null).Origins.Single(), Is.EqualTo("http://[::1]:5000"));
                Assert.That(PasskeyRelyingParty.IsValidRpId("bad_host.com"), Is.False);
                Assert.That(PasskeyRelyingParty.IsValidRpId(""), Is.False);
                Assert.That(PasskeyRelyingParty.OriginMatchesRpId("https://a.example.com", "example.com"), Is.True);
                Assert.That(PasskeyRelyingParty.OriginMatchesRpId("https://badexample.com", "example.com"), Is.False);
            });
        }

        [Test]
        public void Challenge_store_is_bounded()
        {
            var store = new PasskeyChallengeStore(_clock);
            for (var i = 0; i < PasskeyChallengeStore.MaxEntries + 50; i++) store.Add(PasskeyCeremony.Authentication, new object(), "x");
            Assert.That(store.Count, Is.LessThanOrEqualTo(PasskeyChallengeStore.MaxEntries));
            _clock.Now += TimeSpan.FromMinutes(6);
            store.Add(PasskeyCeremony.Authentication, new object(), "x");
            Assert.That(store.Count, Is.EqualTo(1));
        }

        [Test]
        public void Passkey_endpoints_are_reachable_signed_out_but_need_the_csrf_header()
        {
            WebAccessDecision Eval(string path, string? requestedWith, bool cookie = false, string? key = null) => WebAccessPolicy.Evaluate(new WebAccessRequest
            {
                Path = path,
                Method = "POST",
                Scheme = "https",
                Host = "tfm.example.com",
                PasswordSet = true,
                CookieAuthenticated = cookie,
                ConfiguredApiKey = "k1",
                ProvidedApiKey = key,
                TcpRemoteAddress = System.Net.IPAddress.Parse("203.0.113.9"),
                RequestedWith = requestedWith
            });

            Assert.Multiple(() =>
            {
                Assert.That(Eval("/api/v1/web-auth/passkeys/login/begin", "tfm").Allowed, Is.True);
                Assert.That(Eval("/api/v1/web-auth/passkeys/login/finish", "tfm").Method, Is.EqualTo(AccessMethod.None));
                Assert.That(Eval("/api/v1/web-auth/passkeys/login/finish", null).Failure, Is.EqualTo(AccessFailure.CsrfHeaderMissing));
                Assert.That(Eval("/api/v1/web-auth/passkeys/register/begin", null, cookie: true).Failure, Is.EqualTo(AccessFailure.CsrfHeaderMissing));
                Assert.That(Eval("/api/v1/web-auth/passkeys/register/begin", "tfm", cookie: true).Method, Is.EqualTo(AccessMethod.Cookie));
                // The API key skips the CSRF rule, but the controller then refuses it (session_required).
                Assert.That(Eval("/api/v1/web-auth/passkeys/register/begin", null, key: "k1").Method, Is.EqualTo(AccessMethod.ApiKey));
            });
        }
    }

    /// <summary>The passkey endpoints: who may call them, and that a passkey sign-in issues the normal session cookie.</summary>
    public class PasskeyControllerTest
    {
        private const string Password = "first-password";
        private string _mirror = null!;
        private WebAuthService _auth = null!;
        private PasskeyService _passkeys = null!;
        private ServiceProvider _services = null!;

        [SetUp]
        public async Task SetUp()
        {
            _mirror = Path.Combine(Path.GetTempPath(), $"tfm-passkey-ctl-{Guid.NewGuid():N}.json");
            _auth = new WebAuthService(DispatchProxy.Create<IDbService, WebAuthServiceTest.FakeDb>(), NullLogger<WebAuthService>.Instance, _mirror);
            await _auth.LoadAsync(true);
            await _auth.SetupAsync(Password);
            _passkeys = new PasskeyService(_auth, NullLogger<PasskeyService>.Instance);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddSingleton<IWebAuthService>(_auth);
            WebAuthSession.AddWebAuthentication(services);
            _services = services.BuildServiceProvider();
        }

        [TearDown]
        public void TearDown()
        {
            _services.Dispose();
            if (File.Exists(_mirror)) File.Delete(_mirror);
        }

        private WebAuthController Controller(AccessMethod method, bool cookie, string mode = "password")
        {
            var ctx = new DefaultHttpContext { RequestServices = _services };
            ctx.Request.Scheme = "https";
            ctx.Request.Host = new HostString("tfm.example.com");
            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
            ctx.Items[WebAccessMiddleware.DecisionItemKey] = WebAccessDecision.Allow(method, WebPathKind.WebAuth);
            if (cookie)
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, "web"),
                    new Claim(WebAuthSession.StampClaim, _auth.SecurityStamp),
                    new Claim(WebAuthSession.ModeClaim, mode)
                }, WebAuthSession.Scheme));
            return new WebAuthController(_auth, _passkeys, NullLogger<WebAuthController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = ctx }
            };
        }

        private static int Status(IActionResult r) => r switch
        {
            ObjectResult o => o.StatusCode ?? 200,
            StatusCodeResult s => s.StatusCode,
            _ => throw new InvalidOperationException(r.GetType().Name)
        };

        private static string? Code(IActionResult r) =>
            r is ObjectResult { Value: ApiResult res } ? res.Error?.Code : null;

        private static T Data<T>(IActionResult r) => ((ApiResult<T>)((ObjectResult)r).Value!).Data!;

        [Test]
        public async Task Registration_and_management_need_a_password_cookie_session()
        {
            var body = new WebPasskeyRegisterBeginRequest { CurrentPassword = Password };
            foreach (var (method, cookie) in new[] { (AccessMethod.ApiKey, false), (AccessMethod.Lan, false), (AccessMethod.Internal, false), (AccessMethod.None, false), (AccessMethod.ApiKey, true) })
            {
                var c = Controller(method, cookie);
                var r = await c.PasskeyRegisterBegin(body);
                Assert.That(Status(r), Is.EqualTo(403), $"{method}/{cookie}");
                Assert.That(Code(r), Is.EqualTo(ApiErrorCodes.SessionRequired));
                Assert.That(Status(c.PasskeyList()), Is.EqualTo(403));
                Assert.That(Status(await c.PasskeyDelete("x")), Is.EqualTo(403));
                Assert.That(Status(await c.PasskeyRename("x", new WebPasskeyRenameRequest { Name = "n" })), Is.EqualTo(403));
                Assert.That(Status(await c.PasskeySettings(new WebPasskeySettingsRequest())), Is.EqualTo(403));
                Assert.That(Status(await c.PasskeyRegisterFinish(new WebPasskeyRegisterFinishRequest(), CancellationToken.None)), Is.EqualTo(403));
            }
            Assert.That(Status(await Controller(AccessMethod.Cookie, true, mode: "open").PasskeyRegisterBegin(body)), Is.EqualTo(403));

            var wrong = await Controller(AccessMethod.Cookie, true).PasskeyRegisterBegin(new WebPasskeyRegisterBeginRequest { CurrentPassword = "nope-nope" });
            Assert.That(Status(wrong), Is.EqualTo(401));
            Assert.That(Code(wrong), Is.EqualTo(ApiErrorCodes.InvalidCredentials));

            var ok = await Controller(AccessMethod.Cookie, true).PasskeyRegisterBegin(body);
            Assert.That(Status(ok), Is.EqualTo(200));
            Assert.That(Data<WebPasskeyBeginDto>(ok).Options.GetProperty("rp").GetProperty("id").GetString(), Is.EqualTo("tfm.example.com"));
        }

        [Test]
        public async Task Passkey_sign_in_issues_the_session_cookie_and_failures_count_for_the_rate_limiter()
        {
            // Register through the endpoints.
            var reg = Controller(AccessMethod.Cookie, true);
            var begin = Data<WebPasskeyBeginDto>(await reg.PasskeyRegisterBegin(new WebPasskeyRegisterBeginRequest { CurrentPassword = Password }));
            using var a = new SoftwareAuthenticator();
            var options = JsonNode.Parse(begin.Options.GetRawText())!.AsObject();
            var fin = await Controller(AccessMethod.Cookie, true).PasskeyRegisterFinish(new WebPasskeyRegisterFinishRequest
            {
                RequestId = begin.RequestId,
                Name = "Bitwarden",
                Credential = a.Create(options, "https://tfm.example.com")
            }, CancellationToken.None);
            Assert.That(Status(fin), Is.EqualTo(200));
            Assert.That(Data<WebPasskeyDto>(fin).BackupEligible, Is.True);

            var list = Data<WebPasskeyListDto>(Controller(AccessMethod.Cookie, true).PasskeyList());
            Assert.That(list.Items.Single().Name, Is.EqualTo("Bitwarden"));
            Assert.That(list.RpId, Is.EqualTo("tfm.example.com"));
            Assert.That(list.Supported, Is.True);

            // Status advertises the passkeys to the login page.
            var anon = Controller(AccessMethod.None, false);
            var status = Data<WebAuthStatusDto>(await anon.Status());
            Assert.That(status.PasskeyCount, Is.EqualTo(1));
            Assert.That(status.PasskeysAvailable, Is.True);

            // Unauthenticated sign-in.
            var loginBegin = Data<WebPasskeyBeginDto>(anon.PasskeyLoginBegin());
            var loginOptions = JsonNode.Parse(loginBegin.Options.GetRawText())!.AsObject();
            Assert.That(loginOptions["allowCredentials"]?.AsArray().Count ?? 0, Is.EqualTo(0), "usernameless");
            var login = Controller(AccessMethod.None, false);
            var result = await login.PasskeyLoginFinish(new WebPasskeyLoginFinishRequest
            {
                RequestId = loginBegin.RequestId,
                Credential = a.Get(loginOptions, "https://tfm.example.com"),
                Remember = true
            }, CancellationToken.None);
            Assert.That(Status(result), Is.EqualTo(200));
            Assert.That(Data<WebAuthStatusDto>(result).Authenticated, Is.True);
            var setCookie = login.HttpContext.Response.Headers.SetCookie.ToString();
            Assert.That(setCookie, Does.Contain(WebAuthSession.CookieName + "="));
            Assert.That(setCookie, Does.Contain("samesite=strict").IgnoreCase);
            Assert.That(setCookie, Does.Contain("expires=").IgnoreCase, "remember = persistent cookie");

            // Failures (wrong origin) feed the login rate limiter: the 6th locks the IP out.
            for (var i = 0; i < LoginRateLimiter.MaxFailures + 1; i++)
            {
                var c = Controller(AccessMethod.None, false);
                var b = Data<WebPasskeyBeginDto>(c.PasskeyLoginBegin());
                var o = JsonNode.Parse(b.Options.GetRawText())!.AsObject();
                var r = await c.PasskeyLoginFinish(new WebPasskeyLoginFinishRequest { RequestId = b.RequestId, Credential = a.Get(o, "https://evil.example.net") }, CancellationToken.None);
                Assert.That(Status(r), Is.EqualTo(401));
                Assert.That(Code(r), Is.EqualTo(ApiErrorCodes.PasskeyInvalid));
            }
            Assert.That(Status(Controller(AccessMethod.None, false).PasskeyLoginBegin()), Is.EqualTo(429));
        }

        [Test]
        public async Task Ip_hosts_cannot_use_passkeys()
        {
            var c = Controller(AccessMethod.None, false);
            c.HttpContext.Request.Host = new HostString("192.168.1.10", 5000);
            var r = c.PasskeyLoginBegin();
            Assert.That(Status(r), Is.EqualTo(400));
            Assert.That(Code(r), Is.EqualTo(ApiErrorCodes.PasskeyUnsupported));
        }
    }
}
