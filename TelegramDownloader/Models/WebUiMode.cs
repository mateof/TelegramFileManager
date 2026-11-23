namespace TelegramDownloader.Models
{
    /// <summary>
    /// Values of <see cref="GeneralConfig.DefaultWebUi"/>: which interface a
    /// browser gets when it opens the server root (<c>/</c>).
    /// </summary>
    public static class WebUiMode
    {
        /// <summary>The Blazor UI at <c>/</c> (default).</summary>
        public const string Classic = "classic";

        /// <summary>The web app at <c>/app/</c>: a browser opening <c>/</c> is redirected there.</summary>
        public const string App = "app";

        /// <summary>True for <c>classic</c> or <c>app</c> (case-insensitive).</summary>
        public static bool IsValid(string? value) =>
            string.Equals(value, Classic, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, App, StringComparison.OrdinalIgnoreCase);

        /// <summary>The canonical value; anything unknown or empty is <c>classic</c>.</summary>
        public static string Normalize(string? value) =>
            string.Equals(value, App, StringComparison.OrdinalIgnoreCase) ? App : Classic;
    }
}
