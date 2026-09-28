using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Core.Services;

public class FolderStructureParser : IFolderStructureParser
{
    private static readonly Regex DatePatternRegex = new(@"^\d{4}[-_.]\d{2}[-_.]\d{2}$", RegexOptions.Compiled);
    private readonly IFileSystemAdapter _fileSystem;
    private readonly ILogger<FolderStructureParser>? _logger;

    public FolderStructureParser(IFileSystemAdapter fileSystem, ILogger<FolderStructureParser>? logger = null)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public IReadOnlyList<string> DiscoverDateFolders(string rootFolder)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || !_fileSystem.DirectoryExists(rootFolder))
        {
            _logger?.LogWarning("Root folder '{Root}' does not exist or is empty.", rootFolder);
            return Array.Empty<string>();
        }

        var results = new List<string>();
        try
        {
            foreach (var dir in _fileSystem.EnumerateDirectories(rootFolder))
            {
                string folderName = _fileSystem.GetFileName(dir);
                if (DatePatternRegex.IsMatch(folderName))
                {
                    results.Add(folderName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to enumerate date folders in '{Root}'", rootFolder);
        }

        return results.OrderByDescending(d => d).ToList();
    }

    public IReadOnlyList<DiscoveredOrder> DiscoverOrdersForDate(string rootFolder, string dateString)
    {
        string dateFolderPath = _fileSystem.Combine(rootFolder, dateString);
        if (!_fileSystem.DirectoryExists(dateFolderPath))
        {
            _logger?.LogWarning("Date folder '{Path}' does not exist.", dateFolderPath);
            return Array.Empty<DiscoveredOrder>();
        }

        var orders = new List<DiscoveredOrder>();
        try
        {
            foreach (var customerDir in _fileSystem.EnumerateDirectories(dateFolderPath))
            {
                string customerFolderName = _fileSystem.GetFileName(customerDir);
                string relativeCustomerPath = _fileSystem.GetRelativePath(rootFolder, customerDir);

                var specs = DiscoverSpecificationsInOrder(rootFolder, customerDir);

                orders.Add(new DiscoveredOrder(
                    Date: dateString,
                    OriginalCustomerFolderName: customerFolderName,
                    FullPath: customerDir,
                    RelativePath: relativeCustomerPath,
                    Specifications: specs
                ));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to enumerate customer folders in date '{DatePath}'", dateFolderPath);
        }

        return orders.OrderBy(o => o.OriginalCustomerFolderName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public DiscoveredOrder? DiscoverSingleOrder(string rootFolder, string orderRelativePath)
    {
        string fullPath = _fileSystem.Combine(rootFolder, orderRelativePath);
        if (!_fileSystem.DirectoryExists(fullPath))
        {
            return null;
        }

        string customerFolderName = _fileSystem.GetFileName(fullPath);
        string parentDir = _fileSystem.GetDirectoryName(fullPath);
        string dateString = _fileSystem.GetFileName(parentDir);

        var specs = DiscoverSpecificationsInOrder(rootFolder, fullPath);

        return new DiscoveredOrder(
            Date: dateString,
            OriginalCustomerFolderName: customerFolderName,
            FullPath: fullPath,
            RelativePath: orderRelativePath,
            Specifications: specs
        );
    }

    public DiscoveredSpecification? DiscoverSingleSpecification(string rootFolder, string specRelativePath)
    {
        string fullPath = _fileSystem.Combine(rootFolder, specRelativePath);
        if (!_fileSystem.DirectoryExists(fullPath))
        {
            return null;
        }

        string specFolderName = _fileSystem.GetFileName(fullPath);

        return new DiscoveredSpecification(
            FolderName: specFolderName,
            FullPath: fullPath,
            RelativePath: specRelativePath
        );
    }

    private List<DiscoveredSpecification> DiscoverSpecificationsInOrder(string rootFolder, string customerDir)
    {
        var specs = new List<DiscoveredSpecification>();
        try
        {
            foreach (var specDir in _fileSystem.EnumerateDirectories(customerDir))
            {
                string specFolderName = _fileSystem.GetFileName(specDir);
                string relativeSpecPath = _fileSystem.GetRelativePath(rootFolder, specDir);

                specs.Add(new DiscoveredSpecification(
                    FolderName: specFolderName,
                    FullPath: specDir,
                    RelativePath: relativeSpecPath
                ));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to enumerate specifications in '{CustomerDir}'", customerDir);
        }

        return specs.OrderBy(s => s.FolderName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
