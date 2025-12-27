using System.Globalization;

namespace TFMAudioApp.Helpers.Converters;

public class DurationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            TimeSpan ts => ts.ToDurationString(),
            double seconds => seconds.ToDurationString(),
            int seconds => ((double)seconds).ToDurationString(),
            _ => "--:--"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
