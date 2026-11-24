using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TelegramDownloader.Data.db;
using TelegramDownloader.Models;

namespace TelegramDownloader.Services.WebAuth
{
    public enum WebAuthOutcome
    {
        Ok,
        /// <summary>Setup called while a password already exists.</summary>
        AlreadySet,
        /// <summary>Change/remove called while no password exists.</summary>
        NotSet,
        /// <summary>The current password is wrong.</summary>
        InvalidCredentials,
        /// <summary>The new password does not meet the rules (see <see cref="WebPasswordHasher.Validate"/>).</summary>
        WeakPassword
    }

    public sealed record WebAuthResult(WebAuthOutcome Outcome, string? Message = null)
    {
        public bool Succeeded => Outcome == WebAuthOutcome.Ok;
    }

    /// <summary>
    /// Owner of the web password and the LAN bypass setting. Used by the
    /// web-auth API, the Blazor Config page and the access middleware.
    /// </summary>
    public interface IWebAuthService
    {
        bool PasswordSet { get; }
        bool LanBypassEnabled { get; }
        /// <summary>Changes on every password set/change/removal; embedded in session cookies.</summary>
        string SecurityStamp { get; }
        /// <summary>
        /// Per-process secret the server-side HTTP calls of the Blazor UI (the
        /// legacy EJ2 file manager calls the legacy endpoints from the server,
        /// without the browser's cookie) send in <see cref="InternalTokenHeader"/>.
        /// </summary>
        string InternalToken { get; }
        bool IsInternalToken(string? value);
        LoginRateLimiter RateLimiter { get; }

        /// <summary>Raised after any change, so the UI can refresh.</summary>
        event Action? Changed;

        Task LoadAsync(bool databaseAvailable);
        bool VerifyPassword(string? password);
        Task<WebAuthResult> SetupAsync(string? password);
        Task<WebAuthResult> ChangePasswordAsync(string? currentPassword, string? newPassword);
        Task<WebAuthResult> RemovePasswordAsync(string? currentPassword);
        Task SetLanBypassAsync(bool enabled);

        /// <summary>Registered passkeys (empty while no password is set).</summary>
        IReadOnlyList<WebPasskey> Passkeys { get; }
        /// <summary>Stable WebAuthn user handle (base64url), null until the first passkey.</summary>
        string? PasskeyUserHandle { get; }
        /// <summary>Relying party ID override, null = request host.</summary>
        string? PasskeyRpId { get; }
        /// <summary>Allowed origins override, empty = request origin.</summary>
        IReadOnlyList<string> PasskeyOrigins { get; }

        /// <summary>
        /// Changes the passkey data (list, user handle, relying party override)
        /// under the settings lock and persists it. <paramref name="change"/>
        /// works on a copy and returns false to cancel. Sessions stay valid.
        /// </summary>
        Task<bool> UpdatePasskeysAsync(Func<WebAuthSettings, bool> change);
    }

    public class WebAuthService : IWebAuthService
    {
        public const string InternalTokenHeader = "X-TFM-Internal";

        /// <summary>
        /// Set to <c>true</c>/<c>1</c> to remove a forgotten web password at
        /// startup (the UI is open again until a new one is set).
        /// </summary>
        public const string ResetEnvironmentVariable = "TFM_RESET_WEB_PASSWORD";

        private readonly IDbService _db;
        private readonly ILogger<WebAuthService> _logger;
        private readonly string _mirrorPath;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly byte[] _internalTokenBytes;
        private volatile WebAuthSettings _current = new();
        private bool _databaseAvailable;

        public WebAuthService(IDbService db, ILogger<WebAuthService> logger, string? mirrorPath = null)
        {
            _db = db;
            _logger = logger;
            _mirrorPath = mirrorPath ?? Path.Combine(UserService.USERDATAFOLDER, "web-auth.json");
            InternalToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            _internalTokenBytes = Encoding.UTF8.GetBytes(InternalToken);
        }

        public bool PasswordSet => !string.IsNullOrEmpty(_current.PasswordHash);
        public bool LanBypassEnabled => _current.LanBypassEnabled;
        public string SecurityStamp => _current.SecurityStamp;
        public string InternalToken { get; }
        public LoginRateLimiter RateLimiter { get; } = new();
        public event Action? Changed;

        public IReadOnlyList<WebPasskey> Passkeys => (_current.Passkeys ?? new()).AsReadOnly();
        public string? PasskeyUserHandle => _current.PasskeyUserHandle;
        public string? PasskeyRpId => _current.PasskeyRpId;
        public IReadOnlyList<string> PasskeyOrigins => (_current.PasskeyOrigins ?? new()).AsReadOnly();

        public async Task<bool> UpdatePasskeysAsync(Func<WebAuthSettings, bool> change)
        {
            var applied = false;
            await MutateAsync(s => applied = change(s), newStamp: false);
            return applied;
        }

        public bool IsInternalToken(string? value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), _internalTokenBytes);
        }

        /// <summary>
        /// Loads the settings. MongoDB is authoritative; the local mirror is
        /// used when the database is unreachable, or when it holds no record
        /// while the mirror has a password (a new/emptied database must not
        /// silently remove the protection: fail closed).
        /// </summary>
        public async Task LoadAsync(bool databaseAvailable)
        {
            await _lock.WaitAsync();
            try
            {
                _databaseAvailable = databaseAvailable;
                var mirror = ReadMirror();
                WebAuthSettings? stored = null;
                var dbOk = false;
                if (databaseAvailable)
                {
                    try
                    {
                        stored = await _db.LoadWebAuth();
                        dbOk = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not load the web authentication settings from MongoDB, using the local copy");
                    }
                }

                WebAuthSettings current;
                if (dbOk && stored != null)
                {
                    current = stored;
                }
                else if (mirror != null)
                {
                    current = mirror;
                    if (dbOk && !string.IsNullOrEmpty(mirror.PasswordHash))
                    {
                        _logger.LogWarning("The database has no web password but the local copy does; keeping the password");
                        await TrySaveToDb(mirror);
                    }
                }
                else
                {
                    current = new WebAuthSettings { PasswordHash = null };
                }

                var reset = Environment.GetEnvironmentVariable(ResetEnvironmentVariable);
                if (!string.IsNullOrEmpty(current.PasswordHash)
                    && (reset == "1" || string.Equals(reset, "true", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("{Var} is set: removing the web password. Unset it and set a new password.", ResetEnvironmentVariable);
                    current = current.Clone();
                    current.PasswordHash = null;
                    if (current.Passkeys.Count > 0)
                        _logger.LogWarning("{Var} is set: removing {Count} passkey(s) too", ResetEnvironmentVariable, current.Passkeys.Count);
                    current.Passkeys = new();
                    current.SecurityStamp = Guid.NewGuid().ToString("N");
                    current.UpdatedUtc = DateTime.UtcNow;
                    if (dbOk) await TrySaveToDb(current);
                }

                current.Passkeys ??= new();
                current.PasskeyOrigins ??= new();
                if (string.IsNullOrEmpty(current.PasswordHash) && current.Passkeys.Count > 0)
                {
                    // Passkeys are an extra way in to a password-protected UI only.
                    _logger.LogWarning("Removing {Count} passkey(s) left without a web password", current.Passkeys.Count);
                    current = current.Clone();
                    current.Passkeys = new();
                    if (dbOk) await TrySaveToDb(current);
                }

                _current = current;
                WriteMirror(current);
            }
            finally
            {
                _lock.Release();
            }

            _logger.LogInformation("Web authentication: {Mode}", PasswordSet
                ? (LanBypassEnabled ? "password set, LAN bypass enabled" : "password set")
                : "no password (open web UI)");
        }

        public bool VerifyPassword(string? password)
        {
            var settings = _current;
            if (!WebPasswordHasher.Verify(settings.PasswordHash, password, out var needsRehash)) return false;
            if (needsRehash && password != null)
            {
                // Upgrade the hash parameters transparently.
                _ = Task.Run(() => MutateAsync(s => { s.PasswordHash = WebPasswordHasher.Hash(password); return true; }, newStamp: false));
            }
            return true;
        }

        public async Task<WebAuthResult> SetupAsync(string? password)
        {
            var invalid = WebPasswordHasher.Validate(password);
            if (invalid != null) return new(WebAuthOutcome.WeakPassword, invalid);

            WebAuthOutcome outcome = WebAuthOutcome.Ok;
            await MutateAsync(s =>
            {
                if (!string.IsNullOrEmpty(s.PasswordHash)) { outcome = WebAuthOutcome.AlreadySet; return false; }
                s.PasswordHash = WebPasswordHasher.Hash(password!);
                return true;
            }, newStamp: true);
            if (outcome == WebAuthOutcome.Ok) _logger.LogInformation("Web password set");
            return new(outcome);
        }

        public async Task<WebAuthResult> ChangePasswordAsync(string? currentPassword, string? newPassword)
        {
            if (!PasswordSet) return new(WebAuthOutcome.NotSet);
            if (!VerifyPassword(currentPassword)) return new(WebAuthOutcome.InvalidCredentials);
            var invalid = WebPasswordHasher.Validate(newPassword);
            if (invalid != null) return new(WebAuthOutcome.WeakPassword, invalid);

            await MutateAsync(s => { s.PasswordHash = WebPasswordHasher.Hash(newPassword!); return true; }, newStamp: true);
            _logger.LogInformation("Web password changed; existing sessions signed out");
            return new(WebAuthOutcome.Ok);
        }

        public async Task<WebAuthResult> RemovePasswordAsync(string? currentPassword)
        {
            if (!PasswordSet) return new(WebAuthOutcome.NotSet);
            if (!VerifyPassword(currentPassword)) return new(WebAuthOutcome.InvalidCredentials);

            var passkeys = 0;
            await MutateAsync(s =>
            {
                passkeys = s.Passkeys.Count;
                s.PasswordHash = null;
                // Passkeys without a password make no sense here: remove them too.
                s.Passkeys = new();
                return true;
            }, newStamp: true);
            _logger.LogWarning("Web password removed: the web UI is open again");
            if (passkeys > 0) _logger.LogWarning("Removed {Count} passkey(s) with the web password", passkeys);
            return new(WebAuthOutcome.Ok);
        }

        public async Task SetLanBypassAsync(bool enabled)
        {
            await MutateAsync(s =>
            {
                if (s.LanBypassEnabled == enabled) return false;
                s.LanBypassEnabled = enabled;
                return true;
            }, newStamp: false);
            _logger.LogInformation("Web LAN bypass {State}", enabled ? "enabled" : "disabled");
        }

        /// <summary>Applies <paramref name="change"/> to a copy, persists it, then publishes it.</summary>
        private async Task MutateAsync(Func<WebAuthSettings, bool> change, bool newStamp)
        {
            await _lock.WaitAsync();
            try
            {
                var next = _current.Clone();
                if (!change(next)) return;
                if (newStamp) next.SecurityStamp = Guid.NewGuid().ToString("N");
                next.UpdatedUtc = DateTime.UtcNow;

                // The database first: when it fails nothing changes.
                if (_databaseAvailable) await _db.SaveWebAuth(next);
                WriteMirror(next);
                _current = next;
            }
            finally
            {
                _lock.Release();
            }

            try { Changed?.Invoke(); } catch (Exception ex) { _logger.LogDebug(ex, "WebAuth Changed handler failed"); }
        }

        private async Task TrySaveToDb(WebAuthSettings settings)
        {
            try { await _db.SaveWebAuth(settings); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not save the web authentication settings to MongoDB"); }
        }

        private WebAuthSettings? ReadMirror()
        {
            try
            {
                if (!File.Exists(_mirrorPath)) return null;
                return JsonSerializer.Deserialize<WebAuthSettings>(File.ReadAllText(_mirrorPath));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read {Path}", _mirrorPath);
                return null;
            }
        }

        private void WriteMirror(WebAuthSettings settings)
        {
            try
            {
                var dir = Path.GetDirectoryName(_mirrorPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = _mirrorPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(settings));
                File.Move(tmp, _mirrorPath, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not write {Path}", _mirrorPath);
            }
        }
    }
}
