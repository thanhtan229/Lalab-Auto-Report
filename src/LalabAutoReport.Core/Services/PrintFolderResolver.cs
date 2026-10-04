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
                ErrorMessage: $"Error traversing directory: {ex.Message}",
                ResolutionMode: BillingFolderResolutionMode.AutoResolved
            );
        }

        // 2. Identify folders directly containing supported image files
        // Map from normalized folder path to image count
        var imageBearingFolders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Include specFolderFullPath itself: if direct images exist and no descendant has images, specFolder itself is final
        try
        {
            int directCount = CountImagesDirectlyInFolder(specFolderFullPath, supportedExtensions);
            if (directCount > 0)
            {
                imageBearingFolders[specFolderFullPath] = directCount;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Cannot access product folder '{Dir}'", specFolderFullPath);
            return Incomplete(specFolderFullPath, ex);
        }

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
                _logger?.LogWarning(ex, "Inaccessible subfolder '{Dir}'", dir);
                return Incomplete(dir, ex);
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
                CandidatePrintFolderRelativePaths: new[] { relativePath },
                ResolutionMode: BillingFolderResolutionMode.AutoResolved
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

            // Check if there is a previously manually selected print folder that is still one of the valid leaf folders and has images
            if (!string.IsNullOrWhiteSpace(previouslySelectedRelativePath))
            {
                string candidateFullPath = _fileSystem.Combine(rootFolderFullPath, previouslySelectedRelativePath);
                var matchedLeaf = leafFolders.FirstOrDefault(f =>
                    string.Equals(f, candidateFullPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_fileSystem.GetRelativePath(rootFolderFullPath, f), previouslySelectedRelativePath, StringComparison.OrdinalIgnoreCase));

                if (matchedLeaf != null && imageBearingFolders.TryGetValue(matchedLeaf, out int count) && count > 0)
                {
                    string matchedRelative = _fileSystem.GetRelativePath(rootFolderFullPath, matchedLeaf);
                    return new PrintFolderResolutionResult(
                        Status: PrintFolderResolutionStatus.Resolved,
                        SelectedPrintFolderFullPath: matchedLeaf,
                        SelectedPrintFolderRelativePath: matchedRelative,
                        PrintCount: count,
                        CandidatePrintFolderFullPaths: candidateFulls,
                        CandidatePrintFolderRelativePaths: candidateRelatives,
                        ResolutionMode: BillingFolderResolutionMode.ManuallySelected
                    );
                }
            }

            return new PrintFolderResolutionResult(
                Status: PrintFolderResolutionStatus.AmbiguousPrintFolder,
                SelectedPrintFolderFullPath: null,
                SelectedPrintFolderRelativePath: null,
                PrintCount: null,
                CandidatePrintFolderFullPaths: candidateFulls,
                CandidatePrintFolderRelativePaths: candidateRelatives,
                ErrorMessage: $"Multiple ({leafFolders.Count}) image-bearing leaf folders found.",
                ResolutionMode: BillingFolderResolutionMode.AutoResolved
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
            ErrorMessage: "No print folder found under specification.",
            ResolutionMode: BillingFolderResolutionMode.AutoResolved
        );
    }

    private static PrintFolderResolutionResult Incomplete(string path, Exception error) => new(
        Status: PrintFolderResolutionStatus.NoPrintFolder,
        SelectedPrintFolderFullPath: null, SelectedPrintFolderRelativePath: null, PrintCount: null,
        CandidatePrintFolderFullPaths: Array.Empty<string>(), CandidatePrintFolderRelativePaths: Array.Empty<string>(),
        ErrorMessage: $"Không đọc đủ dữ liệu tại '{path}': {error.Message}");

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
            throw;
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
