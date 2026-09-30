using System;
using System.IO;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Core.Services;

public class FolderFingerprintService : IFolderFingerprintService
{
    private readonly IFileSystemAdapter _fileSystem;
    private readonly ILogger<FolderFingerprintService>? _logger;

    public FolderFingerprintService(IFileSystemAdapter fileSystem, ILogger<FolderFingerprintService>? logger = null)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public FolderFingerprint ComputeOrderFingerprint(string rootFolder, string orderRelativePath)
    {
        string fullPath = _fileSystem.Combine(rootFolder, orderRelativePath);
        if (!_fileSystem.DirectoryExists(fullPath))
        {
            return FolderFingerprint.Empty(orderRelativePath);
        }

        try
        {
            var dirInfo = new DirectoryInfo(fullPath);
            long maxTicks = dirInfo.LastWriteTimeUtc.Ticks;
            int dirCount = 0;
            int fileCount = 0;

            // Enumerate files across all subfolders (Product -> sua -> sua lai)
            var allFiles = Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories);
            foreach (var _ in allFiles)
            {
                fileCount++;
            }

            // Enumerate all subdirectories and track newest LastWriteTimeUtc
            var allDirs = Directory.EnumerateDirectories(fullPath, "*", SearchOption.AllDirectories);
            foreach (var dir in allDirs)
            {
                dirCount++;
                try
                {
                    var subInfo = new DirectoryInfo(dir);
                    if (subInfo.LastWriteTimeUtc.Ticks > maxTicks)
                    {
                        maxTicks = subInfo.LastWriteTimeUtc.Ticks;
                    }
                }
                catch
                {
                    // Ignore transient/inaccessible subdirectories
                }
            }

            return new FolderFingerprint(orderRelativePath, maxTicks, dirCount, fileCount);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to compute fingerprint for '{Path}'", orderRelativePath);
            return FolderFingerprint.Empty(orderRelativePath);
        }
    }

    public bool HasChanged(string? storedFingerprint, FolderFingerprint currentFingerprint)
    {
        if (string.IsNullOrWhiteSpace(storedFingerprint))
        {
            return true; // Never recorded or empty -> treat as changed
        }

        return !string.Equals(storedFingerprint.Trim(), currentFingerprint.Value, StringComparison.Ordinal);
    }
}
