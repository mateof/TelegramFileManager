using System.Diagnostics;

namespace TelegramDownloader.Services.Api
{
    /// <summary>
    /// Whether FFmpeg can be run (it is needed for video transcoding) and its
    /// version. The answer is cached for a few minutes: starting a process on
    /// every settings page load is wasteful.
    /// </summary>
    public static class FfmpegProbe
    {
        private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
        private static readonly object Gate = new();
        private static (bool Available, string? Version, DateTime At)? _cached;

        public static (bool Available, string? Version) Probe(bool refresh = false)
        {
            lock (Gate)
            {
                if (!refresh && _cached is { } c && DateTime.UtcNow - c.At < CacheFor)
                    return (c.Available, c.Version);
            }

            bool available = false;
            string? version = null;
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = "-version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var firstLine = process.StandardOutput.ReadLine();
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(true); } catch { }
                }
                else
                {
                    available = process.ExitCode == 0;
                    version = ParseVersion(firstLine);
                }
            }
            catch
            {
                available = false;
            }

            lock (Gate) _cached = (available, version, DateTime.UtcNow);
            return (available, version);
        }

        /// <summary>
        /// Extracts the version from the first line of <c>ffmpeg -version</c>,
        /// e.g. <c>ffmpeg version 6.1.1-3ubuntu5 Copyright (c) 2000-2023</c> gives
        /// <c>6.1.1-3ubuntu5</c>.
        /// </summary>
        public static string? ParseVersion(string? firstLine)
        {
            if (string.IsNullOrWhiteSpace(firstLine)) return null;
            const string marker = "version ";
            var i = firstLine.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            var rest = firstLine[(i + marker.Length)..].Trim();
            var end = rest.IndexOf(' ');
            var version = end < 0 ? rest : rest[..end];
            return version.Length == 0 ? null : version;
        }
    }
}
