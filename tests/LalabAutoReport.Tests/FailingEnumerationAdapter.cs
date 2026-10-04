using System;
using System.Collections.Generic;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Infrastructure.FileSystem;

namespace LalabAutoReport.Tests;

internal sealed class FailingEnumerationAdapter : IFileSystemAdapter
{
    private readonly PhysicalFileSystemAdapter _inner = new();
    public string? FailedPath { get; set; }
    public bool FailFiles { get; set; }
    public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
    public bool FileExists(string path) => _inner.FileExists(path);
    public IEnumerable<string> EnumerateDirectories(string path)
        => !FailFiles && path == FailedPath ? throw new UnauthorizedAccessException($"Access denied: {path}") : _inner.EnumerateDirectories(path);
    public IEnumerable<string> EnumerateFiles(string path)
        => FailFiles && path == FailedPath ? throw new UnauthorizedAccessException($"Access denied: {path}") : _inner.EnumerateFiles(path);
    public string GetRelativePath(string root, string path) => _inner.GetRelativePath(root, path);
    public string Combine(params string[] paths) => _inner.Combine(paths);
    public string GetFileName(string path) => _inner.GetFileName(path);
    public string GetDirectoryName(string path) => _inner.GetDirectoryName(path);
    public string GetExtension(string path) => _inner.GetExtension(path);
    public void OpenDirectoryInShell(string path) => throw new NotSupportedException();
}
