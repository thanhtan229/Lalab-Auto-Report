using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Core.Services;

public class PrintFolderResolver : IPrintFolderResolver
{
    private readonly IFileSystemAdapter _fileSystem;
    private readonly ILogger<PrintFolderResolver>? _logger;

    public PrintFolderResolver(IFileSystemAdapter fileSystem, ILogger<PrintFolderResolver>? logger = null)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public PrintFolderResolutionResult ResolvePrintFolder(
        string specFolderFullPath,
        string rootFolderFullPath,
        IReadOnlySet<string> supportedExtensions,
        string? previouslySelectedRelativePath = null)
    {
        if (!_fileSystem.DirectoryExists(specFolderFullPath))
        {
            return new PrintFolderResolutionResult(
                Status: PrintFolderResolutionStatus.NoPrintFolder,
                SelectedPrintFolderFullPath: null,
                SelectedPrintFolderRelativePath: null,
                PrintCount: null,
                CandidatePrintFolderFullPaths: Array.Empty<string>(),
                CandidatePrintFolderRelativePaths: Array.Empty<string>(),
                ErrorMessage: $"Specification folder '{specFolderFullPath}' does not exist."
            );
        }

        // Check if there is a previously selected print folder that still exists
        if (!string.IsNullOrWhiteSpace(previouslySelectedRelativePath))
        {
            string candidateFullPath = _fileSystem.Combine(rootFolderFullPath, previouslySelectedRelativePath);
            if (_fileSystem.DirectoryExists(candidateFullPath))
            {
                int count = CountImagesDirectlyInFolder(candidateFullPath, supportedExtensions);
                return new PrintFolderResolutionResult(
                    Status: PrintFolderResolutionStatus.Resolved,
                    SelectedPrintFolderFullPath: candidateFullPath,
                    SelectedPrintFolderRelativePath: previouslySelectedRelativePath,
                    PrintCount: count,
                    CandidatePrintFolderFullPaths: new[] { candidateFullPath },
                    CandidatePrintFolderRelativePaths: new[] { previouslySelectedRelativePath }
                );
            }
            _logger?.LogWarning("Previously selected print folder '{Path}' no longer exists; rescanning hierarchy.", previouslySelectedRelativePath);
        }

        // 1. Traverse all descendant directories
        var allDescendants = new List<string>();
        try
        {
            TraverseDescendantDirectories(specFolderFullPath, allDescendants);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to traverse descendants of '{Path}'", specFolderFullPath);
            return new PrintFolderResolutionResult(
                Status: PrintFolderResolutionStatus.NoPrintFolder,
                SelectedPrintFolderFullPath: null,
                SelectedPrintFolderRelativePath: null,
                PrintCount: null,
                CandidatePrintFolderFullPaths: Array.Empty<string>(),
                CandidatePrintFolderRelativePaths: Array.Empty<string>(),
                ErrorMessage: $"Error traversing directory: {ex.Message}"
            );
        }

        // 2. Identify folders directly containing supported image files
        // Map from normalized folder path to image count
        var imageBearingFolders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in allDescendants)
        {
            try
            {
                int count = CountImagesDirectlyInFolder(dir, supportedExtensions);
                if (count > 0)
                {
                    imageBearingFolders[dir] = count;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Inaccessible subfolder '{Dir}': skipping", dir);
            }
        }

        // 3. Find image-bearing leaf folders:
        // A folder F in imageBearingFolders is a leaf iff NO descendant folder of F is in imageBearingFolders.
        var leafFolders = new List<string>();

        foreach (var candidate in imageBearingFolders.Keys)
        {
            string prefix = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            bool hasImageBearingDescendant = imageBearingFolders.Keys.Any(other =>
                !string.Equals(other, candidate, StringComparison.OrdinalIgnoreCase) &&
                other.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            if (!hasImageBearingDescendant)
            {
                leafFolders.Add(candidate);
            }
        }

        // 4. Formulate result based on leaf count
        if (leafFolders.Count == 1)
        {
            string singleLeaf = leafFolders[0];
            string relativePath = _fileSystem.GetRelativePath(rootFolderFullPath, singleLeaf);
            int printCount = imageBearingFolders[singleLeaf];

            return new PrintFolderResolutionResult(
                Status: PrintFolderResolutionStatus.Resolved,
                SelectedPrintFolderFullPath: singleLeaf,
                SelectedPrintFolderRelativePath: relativePath,
                PrintCount: printCount,
                CandidatePrintFolderFullPaths: new[] { singleLeaf },
                CandidatePrintFolderRelativePaths: new[] { relativePath }
            );
        }

        if (leafFolders.Count > 1)
        {
            var candidateRelatives = leafFolders
                .Select(f => _fileSystem.GetRelativePath(rootFolderFullPath, f))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidateFulls = leafFolders
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new PrintFolderResolutionResult(
                Status: PrintFolderResolutionStatus.AmbiguousPrintFolder,
                SelectedPrintFolderFullPath: null,
                SelectedPrintFolderRelativePath: null,
                PrintCount: null,
                CandidatePrintFolderFullPaths: candidateFulls,
                CandidatePrintFolderRelativePaths: candidateRelatives,
                ErrorMessage: $"Multiple ({leafFolders.Count}) image-bearing leaf folders found."
            );
        }

        // leafFolders.Count == 0: no child folder contained images
        return new PrintFolderResolutionResult(
            Status: PrintFolderResolutionStatus.NoPrintFolder,
            SelectedPrintFolderFullPath: null,
            SelectedPrintFolderRelativePath: null,
            PrintCount: null,
            CandidatePrintFolderFullPaths: Array.Empty<string>(),
            CandidatePrintFolderRelativePaths: Array.Empty<string>(),
            ErrorMessage: "No print folder found under specification."
        );
    }

    private void TraverseDescendantDirectories(string currentDir, List<string> accumulator)
    {
        IEnumerable<string> subDirs;
        try
        {
            subDirs = _fileSystem.EnumerateDirectories(currentDir);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Cannot access subdirectories of '{Dir}'", currentDir);
            return;
        }

        foreach (var subDir in subDirs)
        {
            accumulator.Add(subDir);
            TraverseDescendantDirectories(subDir, accumulator);
        }
    }

    private int CountImagesDirectlyInFolder(string folderPath, IReadOnlySet<string> supportedExtensions)
    {
        int count = 0;
        foreach (var file in _fileSystem.EnumerateFiles(folderPath))
        {
            string ext = _fileSystem.GetExtension(file);
            if (supportedExtensions.Contains(ext))
            {
                count++;
            }
        }
        return count;
    }
}
