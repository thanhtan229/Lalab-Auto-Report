using System;
using System.IO;

namespace LalabAutoReport.Core.Services;

/// <summary>
/// Robust path normalization utility for Windows filesystem paths to detect duplicates reliably
/// </summary>
public static class PathNormalizer
{
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string trimmed = path.Trim();

        try
        {
            string full = Path.GetFullPath(trimmed);
            // Normalize separators and remove trailing slashes
            string standard = full.Replace('/', '\\').TrimEnd('\\');
            // Windows paths are case-insensitive
            return standard.ToLowerInvariant();
        }
        catch
        {
            // Fallback if path contains illegal characters
            return trimmed.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        }
    }

    public static bool AreEqual(string? path1, string? path2)
    {
        return string.Equals(Normalize(path1), Normalize(path2), StringComparison.Ordinal);
    }

    public static string NormalizePath(string? path) => Normalize(path);
}
