using System;
using System.Globalization;
using System.Windows.Data;

namespace LalabAutoReport.UI.Converters;

/// <summary>
/// Inverts a boolean value.
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        return true;
    }
}
