using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace LalabAutoReport.UI.Converters;

[ValueConversion(typeof(long), typeof(string))]
public class NumberThousandsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return string.Empty;

        bool zeroAsEmpty = string.Equals(parameter?.ToString(), "ZeroAsEmpty", StringComparison.OrdinalIgnoreCase);

        if (value is long l)
        {
            if (zeroAsEmpty && l == 0) return string.Empty;
            return l.ToString("N0", CultureInfo.InvariantCulture);
        }
        if (value is int i)
        {
            if (zeroAsEmpty && i == 0) return string.Empty;
            return i.ToString("N0", CultureInfo.InvariantCulture);
        }
        if (value is decimal d)
        {
            if (zeroAsEmpty && d == 0) return string.Empty;
            return d.ToString("N0", CultureInfo.InvariantCulture);
        }
        if (value is double dbl)
        {
            if (zeroAsEmpty && dbl == 0) return string.Empty;
            return dbl.ToString("N0", CultureInfo.InvariantCulture);
        }
        if (long.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out long parsed))
        {
            if (zeroAsEmpty && parsed == 0) return string.Empty;
            return parsed.ToString("N0", CultureInfo.InvariantCulture);
        }
        return value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            string clean = new string(s.Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(clean))
            {
                if (targetType == typeof(int) || targetType == typeof(int?))
                    return 0;
                if (targetType == typeof(decimal) || targetType == typeof(decimal?))
                    return 0m;
                if (targetType == typeof(double) || targetType == typeof(double?))
                    return 0.0;
                return 0L;
            }

            if (long.TryParse(clean, NumberStyles.None, CultureInfo.InvariantCulture, out long result))
            {
                if (targetType == typeof(int) || targetType == typeof(int?))
                {
                    return (int)Math.Min(int.MaxValue, result);
                }
                if (targetType == typeof(decimal) || targetType == typeof(decimal?))
                {
                    return (decimal)result;
                }
                if (targetType == typeof(double) || targetType == typeof(double?))
                {
                    return (double)result;
                }
                return result;
            }
        }

        if (targetType == typeof(int) || targetType == typeof(int?))
            return 0;
        if (targetType == typeof(decimal) || targetType == typeof(decimal?))
            return 0m;
        if (targetType == typeof(double) || targetType == typeof(double?))
            return 0.0;
        return 0L;
    }
}
