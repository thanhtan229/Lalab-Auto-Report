using System;
using System.IO;

namespace LalabAutoReport.Tests;

public class TestFileSystemFixture : IDisposable
{
    public string RootPath { get; }

    public TestFileSystemFixture()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "LalabTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string CreateDirectory(string relativePath)
    {
        string fullPath = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public string CreateFile(string relativePath, byte[]? content = null)
    {
        string fullPath = Path.Combine(RootPath, relativePath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(fullPath, content ?? new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // dummy JPEG header
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
