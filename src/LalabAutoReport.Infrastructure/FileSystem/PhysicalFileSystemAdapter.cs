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
        try
        {
            if (!Directory.Exists(path))
            {
                return Array.Empty<string>();
            }
            return Directory.EnumerateDirectories(path).ToList();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "Access denied while enumerating directories in '{Path}'", path);
            return Array.Empty<string>();
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger?.LogWarning(ex, "Directory not found while enumerating directories in '{Path}'", path);
            return Array.Empty<string>();
        }
        catch (PathTooLongException ex)
        {
            _logger?.LogWarning(ex, "Path too long while enumerating directories in '{Path}'", path);
            return Array.Empty<string>();
        }
        catch (IOException ex)
        {
            _logger?.LogWarning(ex, "I/O error while enumerating directories in '{Path}'", path);
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error enumerating directories in '{Path}'", path);
            return Array.Empty<string>();
        }
    }

    public IEnumerable<string> EnumerateFiles(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return Array.Empty<string>();
            }
            return Directory.EnumerateFiles(path).ToList();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "Access denied while enumerating files in '{Path}'", path);
            return Array.Empty<string>();
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger?.LogWarning(ex, "Directory not found while enumerating files in '{Path}'", path);
            return Array.Empty<string>();
        }
        catch (PathTooLongException ex)
        {
            _logger?.LogWarning(ex, "Path too long while enumerating files in '{Path}'", path);
            return Array.Empty<string>();
        }
        catch (IOException ex)
        {
            _logger?.LogWarning(ex, "I/O error while enumerating files in '{Path}'", path);
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error enumerating files in '{Path}'", path);
            return Array.Empty<string>();
        }
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
