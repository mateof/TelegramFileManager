namespace TFMAudioApp.Helpers;

public static class Extensions
{
    /// <summary>
    /// Format bytes to human readable string
    /// </summary>
    public static string ToFileSizeString(this long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Format TimeSpan to mm:ss or hh:mm:ss
    /// </summary>
    public static string ToDurationString(this TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return duration.ToString(@"h\:mm\:ss");
        }
        return duration.ToString(@"m\:ss");
    }

    /// <summary>
    /// Format seconds to duration string
    /// </summary>
    public static string ToDurationString(this double seconds)
    {
        return TimeSpan.FromSeconds(seconds).ToDurationString();
    }

    /// <summary>
    /// Format nullable seconds to duration string
    /// </summary>
    public static string ToDurationString(this double? seconds)
    {
        if (!seconds.HasValue) return "--:--";
        return TimeSpan.FromSeconds(seconds.Value).ToDurationString();
    }

    /// <summary>
    /// Check if string is a valid audio file extension
    /// </summary>
    public static bool IsAudioFile(this string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return !string.IsNullOrEmpty(ext) && Constants.AudioExtensions.Contains(ext);
    }

    /// <summary>
    /// Check if string is a valid video file extension
    /// </summary>
    public static bool IsVideoFile(this string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return !string.IsNullOrEmpty(ext) && Constants.VideoExtensions.Contains(ext);
    }

    /// <summary>
    /// Truncate string to max length with ellipsis
    /// </summary>
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }

    /// <summary>
    /// Format DateTime to relative string (e.g., "2 hours ago")
    /// </summary>
    public static string ToRelativeTimeString(this DateTime dateTime)
    {
        var timeSpan = DateTime.Now - dateTime;

        if (timeSpan.TotalMinutes < 1) return "just now";
        if (timeSpan.TotalMinutes < 60) return $"{(int)timeSpan.TotalMinutes}m ago";
        if (timeSpan.TotalHours < 24) return $"{(int)timeSpan.TotalHours}h ago";
        if (timeSpan.TotalDays < 7) return $"{(int)timeSpan.TotalDays}d ago";
        if (timeSpan.TotalDays < 30) return $"{(int)(timeSpan.TotalDays / 7)}w ago";
        if (timeSpan.TotalDays < 365) return $"{(int)(timeSpan.TotalDays / 30)}mo ago";
        return $"{(int)(timeSpan.TotalDays / 365)}y ago";
    }
}
