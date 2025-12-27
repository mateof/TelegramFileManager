using System.Globalization;

namespace TFMAudioApp.Helpers.Converters;

/// <summary>
/// Converts file information to an appropriate icon emoji.
/// Checks both Category and file extension for proper audio detection.
/// Parameters: Pass file name as ConverterParameter.
/// </summary>
public class FileIconConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 3)
            return "📄";

        var isFile = values[0] as bool? ?? true;
        var category = values[1] as string ?? string.Empty;
        var fileName = values[2] as string ?? string.Empty;

        // Folder
        if (!isFile)
            return "📁";

        // Check category first
        if (!string.IsNullOrEmpty(category))
        {
            switch (category.ToLowerInvariant())
            {
                case "audio":
                    return "🎵";
                case "video":
                    return "🎬";
                case "photo":
                case "image":
                    return "🖼️";
            }
        }

        // Fallback: check file extension
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(ext))
        {
            // Audio extensions
            if (Constants.AudioExtensions.Contains(ext))
                return "🎵";

            // Video extensions
            if (Constants.VideoExtensions.Contains(ext))
                return "🎬";

            // Image extensions
            if (Constants.ImageExtensions.Contains(ext))
                return "🖼️";
        }

        // Default document icon
        return "📄";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
