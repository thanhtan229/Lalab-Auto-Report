using System;
using System.Globalization;
using System.Windows.Data;

namespace LalabAutoReport.UI.Converters;

/// <summary>
/// Converts an enum value to a boolean based on the parameter, supporting two-way binding for radio buttons.
/// </summary>
public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        string checkValue = value.ToString()!;
        string targetValue = parameter.ToString()!;
        return checkValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter != null)
        {
            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, parameter.ToString()!);
            }
            if (Nullable.GetUnderlyingType(targetType)?.IsEnum == true)
            {
                return Enum.Parse(Nullable.GetUnderlyingType(targetType)!, parameter.ToString()!);
            }
        }
        return Binding.DoNothing;
    }
}
