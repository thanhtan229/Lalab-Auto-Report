using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Infrastructure.FileSystem;

public class PhysicalFileSystemAdapter : IFileSystemAdapter
{
    private readonly ILogger<PhysicalFileSystemAdapter>? _logger;

    public PhysicalFileSystemAdapter(ILogger<PhysicalFileSystemAdapter>? logger = null)
    {
        _logger = logger;
    }

    public bool DirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error checking directory existence for '{Path}'", path);
            return false;
        }
    }

    public bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error checking file existence for '{Path}'", path);
            return false;
        }
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        // Enumeration failure is different from an empty directory. Materialize here
        // so deferred I/O exceptions reach the scoped scanner/resolver boundary.
        try { return Directory.EnumerateDirectories(path).ToList(); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        { _logger?.LogWarning(ex, "Cannot enumerate directories in {Path}", path); throw; }
    }

    public IEnumerable<string> EnumerateFiles(string path)
    {
        try { return Directory.EnumerateFiles(path).ToList(); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        { _logger?.LogWarning(ex, "Cannot enumerate files in {Path}", path); throw; }
    }

    public string GetRelativePath(string relativeTo, string path)
    {
        return Path.GetRelativePath(relativeTo, path);
    }

    public string Combine(params string[] paths)
    {
        return Path.Combine(paths);
    }

    public string GetFileName(string path)
    {
        return Path.GetFileName(path);
    }

    public string GetDirectoryName(string path)
    {
        return Path.GetDirectoryName(path) ?? string.Empty;
    }

    public string GetExtension(string path)
    {
        return Path.GetExtension(path);
    }

    public void OpenDirectoryInShell(string path)
    {
        if (DirectoryExists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            });
        }
    }
}
