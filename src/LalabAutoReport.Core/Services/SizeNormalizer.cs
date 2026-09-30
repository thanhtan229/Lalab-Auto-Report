using System;
using System.Text.RegularExpressions;

namespace LalabAutoReport.Core.Services;

public record DimensionMatch(
    int Width,
    int Height,
    int MinDimension,
    int MaxDimension,
    string CanonicalSize,
    string? Unit,
    int Index,
    int Length
);

/// <summary>
/// Service for extracting and canonicalizing dimensions without orientation sensitivity.
/// Locked rule: 20x30 and 30x20 are identical (canonical: min x max).
/// </summary>
public static class SizeNormalizer
{
    private static readonly Regex DimensionPattern = new(
        @"(?<!\d)(?<w>\d{1,3})\s*(?:[xX\u00D7*]|(?<=[0-9\s])[_\-](?=[0-9\s]))\s*(?<h>\d{1,3})\s*(?<unit>cm|mm)?(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CleanDelimitersRegex = new(
        @"^[\s._\-/]+|[\s._\-/]+$", RegexOptions.Compiled);

    private static readonly Regex MultipleSpacesRegex = new(
        @"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Attempts to extract dimension numbers from folder name or string,
    /// canonicalize them to min(w, h) + "x" + max(w, h), and return the remaining text.
    /// </summary>
    public static bool TryExtractDimensions(
        string input,
        out string canonicalSize,
        out string remainingText,
        out DimensionMatch? match)
    {
        canonicalSize = string.Empty;
        remainingText = string.Empty;
        match = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string normalizedInput = input.Normalize(System.Text.NormalizationForm.FormC).Trim();
        var regexMatch = DimensionPattern.Match(normalizedInput);

        if (!regexMatch.Success)
        {
            return false;
        }

        if (!int.TryParse(regexMatch.Groups["w"].Value, out int w) ||
            !int.TryParse(regexMatch.Groups["h"].Value, out int h) ||
            w <= 0 || h <= 0)
        {
            return false;
        }

        int min = Math.Min(w, h);
        int max = Math.Max(w, h);
        canonicalSize = $"{min}x{max}";

        string? unit = regexMatch.Groups["unit"].Success ? regexMatch.Groups["unit"].Value : null;

        match = new DimensionMatch(
            Width: w,
            Height: h,
            MinDimension: min,
            MaxDimension: max,
            CanonicalSize: canonicalSize,
            Unit: unit,
            Index: regexMatch.Index,
            Length: regexMatch.Length
        );

        // Remove dimension token from input to get remainder
        string before = normalizedInput[..regexMatch.Index];
        string after = normalizedInput[(regexMatch.Index + regexMatch.Length)..];
        string combined = $"{before} {after}";

        // Clean delimiters and collapse spaces
        string cleaned = CleanDelimitersRegex.Replace(combined, string.Empty);
        cleaned = MultipleSpacesRegex.Replace(cleaned, " ").Trim();

        remainingText = cleaned;
        return true;
    }

    /// <summary>
    /// Normalizes a size string (e.g. "30x20", "20 x 30", "30-20", "20x30cm") to canonical form "20x30".
    /// If input cannot be parsed as dimension, returns normalized trimmed input.
    /// </summary>
    public static string NormalizeSize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        if (TryExtractDimensions(input, out string canonical, out _, out _))
        {
            return canonical;
        }

        return CustomerNormalizer.Normalize(input);
    }

    /// <summary>
    /// Returns true if two size strings represent the exact same dimension regardless of orientation or formatting.
    /// </summary>
    public static bool AreSizesEqual(string sizeA, string sizeB)
    {
        string normA = NormalizeSize(sizeA);
        string normB = NormalizeSize(sizeB);

        return !string.IsNullOrEmpty(normA) &&
               string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase);
    }
}
