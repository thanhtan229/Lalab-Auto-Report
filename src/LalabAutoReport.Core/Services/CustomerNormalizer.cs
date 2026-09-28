using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LalabAutoReport.Core.Services;

public static class CustomerNormalizer
{
    private static readonly Regex MultipleSpacesRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex PunctuationSpacingRegex = new(@"\s*([.,_\-/])\s*", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes a customer or alias string for strict/canonical matching:
    /// - Trims leading/trailing whitespace
    /// - Normalizes Unicode to FormC
    /// - Normalizes spaces around punctuation (e.g. "A. An" -> "a.an")
    /// - Collapses multiple spaces
    /// - Converts to lower-case invariant
    /// Preserves Vietnamese diacritics.
    /// </summary>
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Unicode normalization
        string normalized = input.Normalize(NormalizationForm.FormC).Trim();

        // Convert to lower-case invariant
        normalized = normalized.ToLowerInvariant();

        // Standardize punctuation spacing: e.g. "a . an" -> "a.an"
        normalized = PunctuationSpacingRegex.Replace(normalized, "$1");

        // Collapse multiple spaces
        normalized = MultipleSpacesRegex.Replace(normalized, " ").Trim();

        return normalized;
    }

    /// <summary>
    /// Removes Vietnamese diacritics for advisory fuzzy search/scoring only.
    /// Never use this for silent automatic identity merging.
    /// </summary>
    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (char c in normalizedString)
        {
            UnicodeCategory unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                // Handle special Vietnamese 'đ' / 'Đ'
                if (c == 'đ')
                {
                    stringBuilder.Append('d');
                }
                else if (c == 'Đ')
                {
                    stringBuilder.Append('D');
                }
                else
                {
                    stringBuilder.Append(c);
                }
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Computes Levenshtein distance for fuzzy suggestion ranking
    /// </summary>
    public static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int[,] d = new int[s.Length + 1, t.Length + 1];

        for (int i = 0; i <= s.Length; i++)
            d[i, 0] = i;

        for (int j = 0; j <= t.Length; j++)
            d[0, j] = j;

        for (int j = 1; j <= t.Length; j++)
        {
            for (int i = 1; i <= s.Length; i++)
            {
                int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost
                );
            }
        }

        return d[s.Length, t.Length];
    }

    /// <summary>
    /// Computes similarity ratio between 0.0 and 1.0
    /// </summary>
    public static double Similarity(string source, string target)
    {
        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(target)) return 1.0;
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0.0;

        string sNorm = RemoveDiacritics(Normalize(source));
        string tNorm = RemoveDiacritics(Normalize(target));

        if (sNorm == tNorm) return 1.0;

        int distance = LevenshteinDistance(sNorm, tNorm);
        int maxLength = Math.Max(sNorm.Length, tNorm.Length);

        return 1.0 - ((double)distance / maxLength);
    }
}
