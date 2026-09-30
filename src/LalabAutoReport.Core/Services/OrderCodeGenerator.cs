using System;
using System.Globalization;

namespace LalabAutoReport.Core.Services;

/// <summary>
/// Utility for generating and parsing standardized human-friendly Order Codes.
/// Format: DH-YYMMDD-XXX (e.g. DH-260929-001)
/// </summary>
public static class OrderCodeGenerator
{
    public const string DefaultPrefix = "DH";

    /// <summary>
    /// Converts a date string (expected "yyyy-MM-dd" or DateTime-compatible) into YYMMDD format.
    /// Example: "2026-09-29" -> "260929"
    /// </summary>
    public static string FormatDatePart(string workDate)
    {
        if (string.IsNullOrWhiteSpace(workDate))
        {
            return DateTime.UtcNow.ToString("yyMMdd");
        }

        if (DateTime.TryParseExact(workDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt.ToString("yyMMdd");
        }

        // Fallback for non-standard formats (e.g. 2026/09/29)
        var cleaned = workDate.Replace("-", "").Replace("/", "").Trim();
        if (cleaned.Length == 8) // e.g. 20260929
        {
            return cleaned.Substring(2, 6);
        }
        if (cleaned.Length >= 6)
        {
            return cleaned.Substring(0, 6);
        }

        return DateTime.UtcNow.ToString("yyMMdd");
    }

    /// <summary>
    /// Generates a standardized order code for a given work date and 1-based sequence number.
    /// Uses 3 digits minimum (D3): 1 -> "001", 999 -> "999", 1000 -> "1000".
    /// </summary>
    public static string Generate(string workDate, int sequence, string prefix = DefaultPrefix)
    {
        if (sequence < 1) sequence = 1;
        var datePart = FormatDatePart(workDate);
        return $"{prefix}-{datePart}-{sequence:D3}";
    }

    /// <summary>
    /// Tries to parse the sequence number from an order code matching the prefix and datePart.
    /// </summary>
    public static bool TryParseSequence(string? orderCode, string workDate, out int sequence, string prefix = DefaultPrefix)
    {
        sequence = 0;
        if (string.IsNullOrWhiteSpace(orderCode)) return false;

        var expectedPrefix = $"{prefix}-{FormatDatePart(workDate)}-";
        if (orderCode.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var seqStr = orderCode.Substring(expectedPrefix.Length);
            return int.TryParse(seqStr, out sequence);
        }

        return false;
    }
}
