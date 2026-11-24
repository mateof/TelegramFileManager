using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fido2NetLib;
using Fido2NetLib.Objects;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services.WebAuth
{
    public enum PasskeyOutcome
    {
        Ok,
        /// <summary>No web password is set: passkeys are not offered.</summary>
        PasswordNotSet,
        /// <summary>The RP ID of this request is not a domain (e.g. opened by IP address).</summary>
        Unsupported,
        /// <summary>Registration: the current password is wrong.</summary>
        InvalidCredentials,
        /// <summary>Unknown, expired, already used or foreign challenge.</summary>
        ChallengeInvalid,
        /// <summary>The authenticator response failed verification.</summary>
        Invalid,
        /// <summary>Registration: this credential is already registered.</summary>
        AlreadyRegistered,
        NotFound,
        /// <summary>Settings: invalid RP ID / origins.</summary>
        InvalidSettings
    }

    public sealed record PasskeyResult(PasskeyOutcome Outcome, string? Message = null, WebPasskey? Passkey = null)
    {
        public bool Succeeded => Outcome == PasskeyOutcome.Ok;
    }

    /// <summary>Result of a <c>begin</c>: the opaque id of the pending ceremony and the options for the browser.</summary>
    public sealed record PasskeyBegin(string RequestId, JsonObject Options);

    public interface IPasskeyService
    {
        Task<(PasskeyResult Result, PasskeyBegin? Begin)> BeginRegistrationAsync(string? currentPassword, PasskeyRelyingParty rp);
        Task<PasskeyResult> FinishRegistrationAsync(string? requestId, string? name, JsonElement credential, PasskeyRelyingParty rp, CancellationToken ct = default);
        (PasskeyResult Result, PasskeyBegin? Begin) BeginLogin(PasskeyRelyingParty rp);
        Task<PasskeyResult> FinishLoginAsync(string? requestId, JsonElement credential, PasskeyRelyingParty rp, CancellationToken ct = default);
        Task<PasskeyResult> RenameAsync(string? credentialId, string? name);
        Task<PasskeyResult> DeleteAsync(string? credentialId);
        Task<PasskeyResult> SetRelyingPartyAsync(string? rpId, IEnumerable<string>? origins);
    }

    /// <summary>
    /// Passkey (WebAuthn) sign-in, built on Fido2NetLib. Single-user: every
    /// credential belongs to the one web user, identified by a fixed random
    /// user handle. Tuned for synced passkeys / password managers (Bitwarden,
    /// iCloud Keychain, Google Password Manager) as well as security keys:
    /// discoverable credentials, attestation "none", counter 0 accepted,
    /// backup flags accepted and stored.
    /// </summary>
    public class PasskeyService : IPasskeyService
    {
        public const string UserName = "TFM";
        public const string UserDisplayName = "Telegram File Manager";
        public const string ServerName = "Telegram File Manager";
        public const int MaxNameLength = 64;
        public const int MaxPasskeys = 50;

        private readonly IWebAuthService _auth;
        private readonly ILogger<PasskeyService> _logger;
        private readonly TimeProvider _clock;

        public PasskeyService(IWebAuthService auth, ILogger<PasskeyService> logger, TimeProvider? clock = null)
        {
            _auth = auth;
            _logger = logger;
            _clock = clock ?? TimeProvider.System;
            Challenges = new PasskeyChallengeStore(_clock);
        }

        public PasskeyChallengeStore Challenges { get; }

        // --- registration ----------------------------------------------------

        public async Task<(PasskeyResult Result, PasskeyBegin? Begin)> BeginRegistrationAsync(string? currentPassword, PasskeyRelyingParty rp)
        {
            if (!_auth.PasswordSet) return (new(PasskeyOutcome.PasswordNotSet, "No web password is set"), null);
            if (!rp.Supported) return (Unsupported(rp), null);
            if (!_auth.VerifyPassword(currentPassword)) return (new(PasskeyOutcome.InvalidCredentials, "Wrong current password"), null);
            if (_auth.Passkeys.Count >= MaxPasskeys) return (new(PasskeyOutcome.Invalid, $"At most {MaxPasskeys} passkeys"), null);

            var handle = await EnsureUserHandleAsync();
            var fido = CreateFido2(rp);
            var options = fido.RequestNewCredential(new RequestNewCredentialParams
            {
                User = new Fido2User { Id = handle, Name = UserName, DisplayName = UserDisplayName },
                ExcludeCredentials = _auth.Passkeys
                    .Select(p => new PublicKeyCredentialDescriptor(Decode(p.CredentialId)))
                    .ToList(),
                AuthenticatorSelection = new AuthenticatorSelection
                {
                    // Discoverable credential (usernameless login), any attachment
                    // (platform, security key, phone, password manager).
                    ResidentKey = ResidentKeyRequirement.Required,
                    UserVerification = UserVerificationRequirement.Preferred
                },
                AttestationPreference = AttestationConveyancePreference.None,
                PubKeyCredParams = new[] { PubKeyCredParam.ES256, PubKeyCredParam.Ed25519, PubKeyCredParam.RS256 }
            });

            var id = Challenges.Add(PasskeyCeremony.Registration, options, rp.RpId);
            return (new(PasskeyOutcome.Ok), new PasskeyBegin(id, ToClientJson(options.ToJson())));
        }

        public async Task<PasskeyResult> FinishRegistrationAsync(string? requestId, string? name, JsonElement credential,
            PasskeyRelyingParty rp, CancellationToken ct = default)
        {
            if (!Challenges.TryTake(requestId, PasskeyCeremony.Registration, out var entry) || entry!.RpId != rp.RpId)
                return ChallengeInvalid();
            if (!_auth.PasswordSet) return new(PasskeyOutcome.PasswordNotSet, "No web password is set");

            var options = (CredentialCreateOptions)entry.Options;
            if (!TryParse<AuthenticatorAttestationRawResponse>(credential, out var raw, out var transports) || raw!.RawId is not { Length: > 0 })
                return new(PasskeyOutcome.Invalid, "Malformed passkey response");

            var credentialId = Encode(raw.RawId);
            if (_auth.Passkeys.Any(p => p.CredentialId == credentialId))
                return new(PasskeyOutcome.AlreadyRegistered, "This passkey is already registered");

            RegisteredPublicKeyCredential made;
            try
            {
                made = await CreateFido2(rp).MakeNewCredentialAsync(new MakeNewCredentialParams
                {
                    AttestationResponse = raw,
                    OriginalOptions = options,
                    IsCredentialIdUniqueToUserCallback = (args, _) =>
                        Task.FromResult(_auth.Passkeys.All(p => p.CredentialId != Encode(args.CredentialId)))
                }, ct);
            }
            catch (Exception ex) when (ex is Fido2VerificationException or ArgumentException or FormatException or InvalidOperationException or CryptographicException)
            {
                _logger.LogWarning("Passkey registration rejected: {Reason}", ex.Message);
                return new(PasskeyOutcome.Invalid, ex.Message);
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            var passkey = new WebPasskey
            {
                CredentialId = Encode(made.Id),
                PublicKey = Encode(made.PublicKey),
                SignCount = made.SignCount,
                UserHandle = Encode(options.User.Id),
                AaGuid = made.AaGuid.ToString(),
                Transports = transports,
                BackupEligible = made.IsBackupEligible,
                BackedUp = made.IsBackedUp,
                AttestationFormat = made.AttestationFormat,
                RpId = rp.RpId,
                Name = CleanName(name) ?? DefaultName(),
                CreatedUtc = now
            };

            var outcome = PasskeyOutcome.Ok;
            await _auth.UpdatePasskeysAsync(s =>
            {
                if (string.IsNullOrEmpty(s.PasswordHash)) { outcome = PasskeyOutcome.PasswordNotSet; return false; }
                if (s.Passkeys.Any(p => p.CredentialId == passkey.CredentialId)) { outcome = PasskeyOutcome.AlreadyRegistered; return false; }
                if (s.PasskeyUserHandle != passkey.UserHandle) { outcome = PasskeyOutcome.ChallengeInvalid; return false; }
                s.Passkeys.Add(passkey);
                return true;
            });
            if (outcome != PasskeyOutcome.Ok) return new(outcome);

            _logger.LogInformation("Passkey \"{Name}\" registered for {RpId} (synced: {Synced})", passkey.Name, rp.RpId, passkey.BackupEligible);
            return new(PasskeyOutcome.Ok, Passkey: passkey);
        }

        // --- authentication ----------------------------------------------------

        public (PasskeyResult Result, PasskeyBegin? Begin) BeginLogin(PasskeyRelyingParty rp)
        {
            if (!_auth.PasswordSet) return (new(PasskeyOutcome.PasswordNotSet, "No web password is set"), null);
            if (!rp.Supported) return (Unsupported(rp), null);

            var options = CreateFido2(rp).GetAssertionOptions(new GetAssertionOptionsParams
            {
                // Usernameless: the authenticator picks one of its discoverable credentials.
                AllowedCredentials = Array.Empty<PublicKeyCredentialDescriptor>(),
                UserVerification = UserVerificationRequirement.Preferred
            });
            var id = Challenges.Add(PasskeyCeremony.Authentication, options, rp.RpId);
            return (new(PasskeyOutcome.Ok), new PasskeyBegin(id, ToClientJson(options.ToJson())));
        }

        public async Task<PasskeyResult> FinishLoginAsync(string? requestId, JsonElement credential, PasskeyRelyingParty rp, CancellationToken ct = default)
        {
            if (!Challenges.TryTake(requestId, PasskeyCeremony.Authentication, out var entry) || entry!.RpId != rp.RpId)
                return ChallengeInvalid();
            if (!_auth.PasswordSet) return new(PasskeyOutcome.PasswordNotSet, "No web password is set");

            if (!TryParse<AuthenticatorAssertionRawResponse>(credential, out var raw, out _) || raw!.RawId is not { Length: > 0 })
                return new(PasskeyOutcome.Invalid, "Malformed passkey response");

            var credentialId = Encode(raw.RawId);
            var stored = _auth.Passkeys.FirstOrDefault(p => p.CredentialId == credentialId);
            if (stored == null) return new(PasskeyOutcome.Invalid, "Unknown passkey");

            VerifyAssertionResult verified;
            try
            {
                verified = await CreateFido2(rp).MakeAssertionAsync(new MakeAssertionParams
                {
                    AssertionResponse = raw,
                    OriginalOptions = (AssertionOptions)entry.Options,
                    StoredPublicKey = Decode(stored.PublicKey),
                    // The counter rule is applied below (synced passkeys always send 0).
                    StoredSignatureCounter = 0,
                    IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(
                        Encode(args.UserHandle) == stored.UserHandle && Encode(args.CredentialId) == stored.CredentialId)
                }, ct);
            }
            catch (Exception ex) when (ex is Fido2VerificationException or ArgumentException or FormatException or InvalidOperationException or CryptographicException)
            {
                _logger.LogWarning("Passkey sign-in rejected: {Reason}", ex.Message);
                return new(PasskeyOutcome.Invalid, ex.Message);
            }

            if (!SignCountAcceptable(stored.SignCount, verified.SignCount))
            {
                _logger.LogWarning("Passkey \"{Name}\" sent signature counter {Received} <= {Stored}: possibly cloned, rejected",
                    stored.Name, verified.SignCount, stored.SignCount);
                return new(PasskeyOutcome.Invalid, "Signature counter did not increase");
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            await _auth.UpdatePasskeysAsync(s =>
            {
                var p = s.Passkeys.FirstOrDefault(x => x.CredentialId == credentialId);
                if (p == null) return false;
                p.SignCount = Math.Max(p.SignCount, verified.SignCount);
                p.BackedUp = verified.IsBackedUp;
                p.LastUsedUtc = now;
                return true;
            });
            _logger.LogInformation("Passkey sign-in with \"{Name}\"", stored.Name);
            return new(PasskeyOutcome.Ok, Passkey: stored);
        }

        /// <summary>
        /// Clone detection that tolerates synced passkeys: reject only when both
        /// the stored and the received counters are non-zero and the received
        /// one did not grow.
        /// </summary>
        public static bool SignCountAcceptable(long stored, long received) =>
            !(stored > 0 && received > 0 && received <= stored);

        // --- management ----------------------------------------------------------

        public async Task<PasskeyResult> RenameAsync(string? credentialId, string? name)
        {
            var clean = CleanName(name);
            if (clean == null) return new(PasskeyOutcome.Invalid, "A name is required");
            WebPasskey? updated = null;
            await _auth.UpdatePasskeysAsync(s =>
            {
                var p = s.Passkeys.FirstOrDefault(x => x.CredentialId == credentialId);
                if (p == null) return false;
                p.Name = clean;
                updated = p.Clone();
                return true;
            });
            return updated == null ? new(PasskeyOutcome.NotFound, "Passkey not found") : new(PasskeyOutcome.Ok, Passkey: updated);
        }

        public async Task<PasskeyResult> DeleteAsync(string? credentialId)
        {
            WebPasskey? removed = null;
            await _auth.UpdatePasskeysAsync(s =>
            {
                removed = s.Passkeys.FirstOrDefault(x => x.CredentialId == credentialId);
                return removed != null && s.Passkeys.Remove(removed);
            });
            if (removed == null) return new(PasskeyOutcome.NotFound, "Passkey not found");
            _logger.LogWarning("Passkey \"{Name}\" removed", removed.Name);
            return new(PasskeyOutcome.Ok, Passkey: removed);
        }

        public async Task<PasskeyResult> SetRelyingPartyAsync(string? rpId, IEnumerable<string>? origins)
        {
            var id = string.IsNullOrWhiteSpace(rpId) ? null : rpId.Trim().ToLowerInvariant();
            if (id != null && !PasskeyRelyingParty.IsValidRpId(id))
                return new(PasskeyOutcome.InvalidSettings, "The RP ID must be a domain name (no scheme, port or IP address)");

            var list = new List<string>();
            foreach (var o in origins ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(o)) continue;
                var normalized = PasskeyRelyingParty.NormalizeOrigin(o);
                if (normalized == null) return new(PasskeyOutcome.InvalidSettings, $"Invalid origin: {o.Trim()}");
                if (id != null && !PasskeyRelyingParty.OriginMatchesRpId(normalized, id))
                    return new(PasskeyOutcome.InvalidSettings, $"The origin {normalized} is not on the domain {id}");
                if (!list.Contains(normalized)) list.Add(normalized);
            }
            if (list.Count > PasskeyRelyingParty.MaxOrigins)
                return new(PasskeyOutcome.InvalidSettings, $"At most {PasskeyRelyingParty.MaxOrigins} origins");

            await _auth.UpdatePasskeysAsync(s =>
            {
                if (s.PasskeyRpId == id && s.PasskeyOrigins.SequenceEqual(list)) return false;
                s.PasskeyRpId = id;
                s.PasskeyOrigins = list;
                return true;
            });
            _logger.LogInformation("Passkey relying party set to {RpId} / {Origins}", id ?? "(request host)",
                list.Count == 0 ? "(request origin)" : string.Join(", ", list));
            return new(PasskeyOutcome.Ok);
        }

        // --- helpers ---------------------------------------------------------------

        private async Task<byte[]> EnsureUserHandleAsync()
        {
            if (_auth.PasskeyUserHandle is { Length: > 0 } existing) return Decode(existing);
            await _auth.UpdatePasskeysAsync(s =>
            {
                if (!string.IsNullOrEmpty(s.PasskeyUserHandle)) return false;
                s.PasskeyUserHandle = Encode(RandomNumberGenerator.GetBytes(32));
                return true;
            });
            return Decode(_auth.PasskeyUserHandle!);
        }

        private static Fido2 CreateFido2(PasskeyRelyingParty rp) => new(new Fido2Configuration
        {
            ServerDomain = rp.RpId,
            ServerName = ServerName,
            Origins = new HashSet<string>(rp.Origins, StringComparer.OrdinalIgnoreCase),
            Timeout = (uint)PasskeyChallengeStore.Lifetime.TotalMilliseconds,
            BackupEligibleCredentialPolicy = Fido2Configuration.CredentialBackupPolicy.Allowed,
            BackedUpCredentialPolicy = Fido2Configuration.CredentialBackupPolicy.Allowed
        });

        /// <summary>
        /// Parses the browser's <c>PublicKeyCredential.toJSON()</c>. Fields that
        /// only describe the authenticator (transports, attachment, extension
        /// outputs) are removed before handing it to Fido2NetLib, so a value it
        /// does not know (a new transport, a password-manager specific extension
        /// output) can never make a valid passkey fail; transports are returned
        /// as plain strings.
        /// </summary>
        private static bool TryParse<T>(JsonElement credential, out T? value, out List<string> transports) where T : class
        {
            value = null;
            transports = new List<string>();
            try
            {
                if (credential.ValueKind != JsonValueKind.Object) return false;
                var node = JsonNode.Parse(credential.GetRawText())!.AsObject();
                node.Remove("authenticatorAttachment");
                node["clientExtensionResults"] = new JsonObject();
                if (node["response"] is JsonObject response && response["transports"] is JsonArray arr)
                {
                    foreach (var t in arr)
                        if (t is JsonValue v && v.TryGetValue<string>(out var s) && s.Length is > 0 and <= 32 && !transports.Contains(s))
                            transports.Add(s);
                    response.Remove("transports");
                }
                value = JsonSerializer.Deserialize<T>(node.ToJsonString());
                return value != null;
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or ArgumentException)
            {
                return false;
            }
        }

        /// <summary>Options JSON for the browser, without null members (some parsers reject them).</summary>
        private static JsonObject ToClientJson(string json)
        {
            var node = JsonNode.Parse(json)!.AsObject();
            StripNulls(node);
            return node;
        }

        private static void StripNulls(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var key in obj.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList()) obj.Remove(key);
                    foreach (var kv in obj) StripNulls(kv.Value);
                    break;
                case JsonArray arr:
                    foreach (var item in arr) StripNulls(item);
                    break;
            }
        }

        private static string? CleanName(string? name)
        {
            var n = (name ?? string.Empty).Trim();
            if (n.Length == 0) return null;
            n = new string(n.Where(c => !char.IsControl(c)).ToArray());
            return n.Length > MaxNameLength ? n[..MaxNameLength] : n;
        }

        private string DefaultName() => $"Passkey {_auth.Passkeys.Count + 1}";

        private static PasskeyResult ChallengeInvalid() =>
            new(PasskeyOutcome.ChallengeInvalid, "The passkey request expired or was already used, try again");

        private static PasskeyResult Unsupported(PasskeyRelyingParty rp) =>
            new(PasskeyOutcome.Unsupported, $"Passkeys need a domain name, not \"{rp.RpId}\" (open the web UI by its host name)");

        public static string Encode(byte[] bytes) => Base64Url.EncodeToString(bytes);
        public static byte[] Decode(string value) => Base64Url.DecodeFromChars(value);
    }
}
