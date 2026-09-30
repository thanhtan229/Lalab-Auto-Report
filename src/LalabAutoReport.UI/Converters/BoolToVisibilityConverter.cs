using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LalabAutoReport.UI.Converters;

/// <summary>
/// Converts boolean values to Visibility. Supports "Inverse", "Invert", "Not", "!" parameter.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public Visibility TrueValue { get; set; } = Visibility.Visible;
    public Visibility FalseValue { get; set; } = Visibility.Collapsed;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = false;
        if (value is bool b)
        {
            flag = b;
        }

        if (parameter is string paramStr)
        {
            if (string.Equals(paramStr, "Inverse", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(paramStr, "Invert", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(paramStr, "Not", StringComparison.OrdinalIgnoreCase) ||
                paramStr == "!")
            {
                flag = !flag;
            }
        }
        else if (parameter is bool paramBool && paramBool)
        {
            flag = !flag;
        }

        return flag ? TrueValue : FalseValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Visibility vis)
        {
            bool flag = (vis == TrueValue);
            if (parameter is string paramStr)
            {
                if (string.Equals(paramStr, "Inverse", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(paramStr, "Invert", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(paramStr, "Not", StringComparison.OrdinalIgnoreCase) ||
                    paramStr == "!")
                {
                    flag = !flag;
                }
            }
            return flag;
        }
        return false;
    }
}
