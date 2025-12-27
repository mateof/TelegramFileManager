using System.Globalization;

namespace TFMAudioApp.Helpers.Converters;

/// <summary>
/// Converts an index value to boolean by comparing with a parameter
/// Returns true if index equals parameter value
/// </summary>
public class IndexEqualConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        // Try to parse both as integers
        if (int.TryParse(value.ToString(), out int index) &&
            int.TryParse(parameter.ToString(), out int compareValue))
        {
            return index == compareValue;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts an index value to boolean by comparing with a parameter
/// Returns true if index does NOT equal parameter value
/// </summary>
public class IndexNotEqualConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return true;

        // Try to parse both as integers
        if (int.TryParse(value.ToString(), out int index) &&
            int.TryParse(parameter.ToString(), out int compareValue))
        {
            return index != compareValue;
        }

        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
