using System;

namespace LalabAutoReport.Core.Domain;

/// <summary>
/// Lightweight filesystem fingerprint for an order folder tree.
/// Captures newest modification timestamp, subdirectory count, and file count across descendant branches (e.g. Product -> sua -> sua lai).
/// Does not open image pixels or compute cryptographic hashes.
/// </summary>
public record FolderFingerprint(
    string RelativePath,
    long MaxLastWriteTicks,
    int DirectoryCount,
    int FileCount)
{
    public string Value => $"{MaxLastWriteTicks}_{DirectoryCount}_{FileCount}";

    public static FolderFingerprint Empty(string relativePath) => new(relativePath, 0, 0, 0);
}
