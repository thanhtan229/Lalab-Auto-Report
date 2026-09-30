using LalabAutoReport.Core.Domain;

namespace LalabAutoReport.Core.Interfaces;

/// <summary>
/// Service to compute and compare lightweight filesystem fingerprints for order folder trees.
/// </summary>
public interface IFolderFingerprintService
{
    /// <summary>
    /// Computes the filesystem fingerprint of an order directory (including descendant product folders and edit subfolders).
    /// </summary>
    FolderFingerprint ComputeOrderFingerprint(string rootFolder, string orderRelativePath);

    /// <summary>
    /// Checks whether the filesystem state differs from the stored fingerprint string.
    /// </summary>
    bool HasChanged(string? storedFingerprint, FolderFingerprint currentFingerprint);
}
