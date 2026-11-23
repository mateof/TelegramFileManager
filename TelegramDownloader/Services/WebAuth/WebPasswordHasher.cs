using Microsoft.AspNetCore.Identity;

namespace TelegramDownloader.Services.WebAuth
{
    /// <summary>
    /// Hashing of the web password with ASP.NET Core Identity's
    /// <see cref="PasswordHasher{TUser}"/>: PBKDF2 with HMAC-SHA512, a random
    /// 128-bit salt and 100 000 iterations (format v3; salt, PRF and iteration
    /// count are stored inside the hash, so they can be raised later without
    /// breaking existing hashes). Verification is constant time.
    /// </summary>
    public static class WebPasswordHasher
    {
        public const int MinLength = 8;
        public const int MaxLength = 256;

        private sealed class WebUser { }

        private static readonly PasswordHasher<WebUser> Hasher = new();
        private static readonly WebUser User = new();

        public static string Hash(string password) => Hasher.HashPassword(User, password);

        /// <summary>
        /// Verifies <paramref name="password"/> against <paramref name="hash"/>.
        /// <paramref name="needsRehash"/> is true when the hash uses weaker
        /// parameters than the current defaults and should be replaced.
        /// </summary>
        public static bool Verify(string? hash, string? password, out bool needsRehash)
        {
            needsRehash = false;
            if (string.IsNullOrEmpty(hash) || password == null) return false;
            try
            {
                var result = Hasher.VerifyHashedPassword(User, hash, password);
                needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
                return result != PasswordVerificationResult.Failed;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>Null when the password is acceptable, otherwise the reason.</summary>
        public static string? Validate(string? password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < MinLength)
                return $"The password must be at least {MinLength} characters long.";
            if (password.Length > MaxLength)
                return $"The password must be at most {MaxLength} characters long.";
            return null;
        }
    }
}
