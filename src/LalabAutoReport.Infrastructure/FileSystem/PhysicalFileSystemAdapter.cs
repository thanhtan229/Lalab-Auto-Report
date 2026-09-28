using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.Infrastructure.FileSystem;

public class PhysicalFileSystemAdapter : IFileSystemAdapter
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        return Directory.EnumerateDirectories(path);
    }

    public IEnumerable<string> EnumerateFiles(string path)
    {
        return Directory.EnumerateFiles(path);
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
