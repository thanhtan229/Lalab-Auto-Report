using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Infrastructure.Windows;

[SupportedOSPlatform("windows")]
public class FolderVisualMarkerService : IFolderVisualMarkerService
{
    private readonly ILogger<FolderVisualMarkerService>? _logger;
    private static readonly object _iconLock = new();

    public FolderVisualMarkerService(ILogger<FolderVisualMarkerService>? logger = null)
    {
        _logger = logger;
    }

    public string GetOrExtractPrintedFolderIconPath()
    {
        lock (_iconLock)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string assetsDir = Path.Combine(localAppData, "LalabAutoReport", "Assets");
            string targetIconPath = Path.Combine(assetsDir, "folder_printed.ico");

            if (File.Exists(targetIconPath) && new FileInfo(targetIconPath).Length > 0)
            {
                return targetIconPath;
            }

            try
            {
                Directory.CreateDirectory(assetsDir);

                // 1. Try to find folder_printed.ico in AppContext.BaseDirectory or Resources
                string baseDir = AppContext.BaseDirectory;
                string candidate1 = Path.Combine(baseDir, "Resources", "folder_printed.ico");
                string candidate2 = Path.Combine(baseDir, "folder_printed.ico");
                string candidate3 = Path.Combine(baseDir, "quick_bill.ico");

                if (File.Exists(candidate1))
                {
                    File.Copy(candidate1, targetIconPath, true);
                    return targetIconPath;
                }

                if (File.Exists(candidate2))
                {
                    File.Copy(candidate2, targetIconPath, true);
                    return targetIconPath;
                }

                // Fallback: Generate a crisp red-folder 32x32 icon programmatically
                byte[] iconBytes = GenerateRedFolderIco();
                File.WriteAllBytes(targetIconPath, iconBytes);
                _logger?.LogInformation("Generated default red folder icon at {Path}", targetIconPath);
                return targetIconPath;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to create or extract folder_printed.ico; falling back to target path string");
                return targetIconPath;
            }
        }
    }

    public string GetOrExtractPartialFolderIconPath()
    {
        lock (_iconLock)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string assetsDir = Path.Combine(localAppData, "LalabAutoReport", "Assets");
            string targetIconPath = Path.Combine(assetsDir, "folder_blue.ico");

            if (File.Exists(targetIconPath) && new FileInfo(targetIconPath).Length > 0)
            {
                return targetIconPath;
            }

            try
            {
                Directory.CreateDirectory(assetsDir);

                string baseDir = AppContext.BaseDirectory;
                string candidate1 = Path.Combine(baseDir, "Resources", "folder_blue.ico");
                string candidate2 = Path.Combine(baseDir, "folder_blue.ico");

                if (File.Exists(candidate1))
                {
                    File.Copy(candidate1, targetIconPath, true);
                    return targetIconPath;
                }

                if (File.Exists(candidate2))
                {
                    File.Copy(candidate2, targetIconPath, true);
                    return targetIconPath;
                }

                byte[] iconBytes = GenerateBlueFolderIco();
                File.WriteAllBytes(targetIconPath, iconBytes);
                _logger?.LogInformation("Generated default blue partial folder icon at {Path}", targetIconPath);
                return targetIconPath;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to create or extract folder_blue.ico; falling back to target path string");
                return targetIconPath;
            }
        }
    }

    public bool HasVisualMarker(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return false;

        try
        {
            string iniPath = Path.Combine(folderPath, "desktop.ini");
            if (!File.Exists(iniPath)) return false;

            string content = File.ReadAllText(iniPath);
            return content.Contains("folder_printed.ico", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains("folder_blue.ico", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public VisualFolderColor GetVisualMarkerColor(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return VisualFolderColor.DefaultYellow;

        try
        {
            string iniPath = Path.Combine(folderPath, "desktop.ini");
            if (!File.Exists(iniPath)) return VisualFolderColor.DefaultYellow;

            string content = File.ReadAllText(iniPath);
            if (content.Contains("folder_blue.ico", StringComparison.OrdinalIgnoreCase))
                return VisualFolderColor.BluePartial;
            if (content.Contains("folder_printed.ico", StringComparison.OrdinalIgnoreCase))
                return VisualFolderColor.RedPrinted;
            if (content.Contains("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase))
                return VisualFolderColor.RedPrinted;

            return VisualFolderColor.DefaultYellow;
        }
        catch
        {
            return VisualFolderColor.DefaultYellow;
        }
    }

    public bool ApplyVisualMarker(string folderPath, VisualFolderColor color = VisualFolderColor.RedPrinted)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            _logger?.LogWarning("Cannot apply visual marker to non-existent folder: {Path}", folderPath);
            return false;
        }

        if (color == VisualFolderColor.DefaultYellow)
        {
            return RemoveVisualMarker(folderPath);
        }

        try
        {
            string iconPath = color == VisualFolderColor.BluePartial
                ? GetOrExtractPartialFolderIconPath()
                : GetOrExtractPrintedFolderIconPath();

            string iniPath = Path.Combine(folderPath, "desktop.ini");

            // 1. First, attempt official Windows Shell API to set custom folder icon and invalidate cache
            bool shellApiSuccess = NativeShellMethods.SetFolderCustomIcon(folderPath, iconPath);

            // 2. Ensure desktop.ini exists and has valid format pointing to current iconPath
            var sb = new StringBuilder();
            sb.AppendLine("[.ShellClassInfo]");
            sb.AppendLine($"IconResource={iconPath},0");
            sb.AppendLine("[ViewState]");
            sb.AppendLine("Mode=");
            sb.AppendLine("Vid=");
            sb.AppendLine("FolderType=Generic");

            if (File.Exists(iniPath))
            {
                try
                {
                    File.SetAttributes(iniPath, FileAttributes.Normal);
                }
                catch { }
            }

            File.WriteAllText(iniPath, sb.ToString(), Encoding.Unicode);
            File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);

            // 3. Mark folder as ReadOnly (Windows Explorer requires ReadOnly or System on directory to parse desktop.ini)
            var currentFolderAttrs = File.GetAttributes(folderPath);
            if (!currentFolderAttrs.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(folderPath, currentFolderAttrs | FileAttributes.ReadOnly);
            }

            // 4. Notify Explorer with FLUSH to refresh immediately
            RefreshExplorer(folderPath);
            _logger?.LogDebug("Successfully applied visual {Color} folder marker to '{Path}' (ShellApi={Success})", color, folderPath, shellApiSuccess);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to apply visual {Color} folder marker to '{Path}' (non-critical)", color, folderPath);
            return false;
        }
    }

    public bool RemoveVisualMarker(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return false;

        try
        {
            // 1. Tell Shell API to clear customization
            NativeShellMethods.ClearFolderCustomIcon(folderPath);

            string iniPath = Path.Combine(folderPath, "desktop.ini");

            // 2. If desktop.ini exists, remove attributes and delete
            if (File.Exists(iniPath))
            {
                try
                {
                    File.SetAttributes(iniPath, FileAttributes.Normal);
                    File.Delete(iniPath);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Could not delete desktop.ini in '{Path}'", folderPath);
                }
            }

            // 3. Remove ReadOnly attribute from directory
            var currentFolderAttrs = File.GetAttributes(folderPath);
            if (currentFolderAttrs.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(folderPath, currentFolderAttrs & ~FileAttributes.ReadOnly);
            }

            // 4. Notify Explorer with FLUSH to restore default icon immediately
            RefreshExplorer(folderPath);
            _logger?.LogDebug("Successfully removed visual red folder marker from '{Path}'", folderPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to remove visual marker from '{Path}' (non-critical)", folderPath);
            return false;
        }
    }

    public void RefreshExplorer(string folderPath)
    {
        NativeShellMethods.NotifyFolderChanged(folderPath);
    }

    /// <summary>
    /// Generates a valid 32x32 32-bit BMP ICO representing a red folder icon
    /// </summary>
    private static byte[] GenerateRedFolderIco()
    {
        const int width = 32;
        const int height = 32;
        const int bpp = 32;
        int imageSize = 40 + (width * height * 4) + (width * height / 8); // Header + XOR mask + AND mask
        int fileSize = 6 + 16 + imageSize;

        byte[] bytes = new byte[fileSize];
        using var ms = new MemoryStream(bytes);
        using var writer = new BinaryWriter(ms);

        // ICONDIR
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // type 1 = icon
        writer.Write((ushort)1); // count 1 image

        // ICONDIRENTRY
        writer.Write((byte)width);
        writer.Write((byte)height);
        writer.Write((byte)0); // colors
        writer.Write((byte)0); // reserved
        writer.Write((ushort)1); // planes
        writer.Write((ushort)bpp); // bit count
        writer.Write((uint)imageSize); // bytes in res
        writer.Write((uint)22); // image offset

        // BITMAPINFOHEADER (height is doubled for XOR + AND masks)
        writer.Write((uint)40); // biSize
        writer.Write((int)width); // biWidth
        writer.Write((int)(height * 2)); // biHeight (XOR + AND mask height)
        writer.Write((ushort)1); // biPlanes
        writer.Write((ushort)bpp); // biBitCount
        writer.Write((uint)0); // biCompression
        writer.Write((uint)(imageSize - 40)); // biSizeImage
        writer.Write((int)0); // biXPelsPerMeter
        writer.Write((int)0); // biYPelsPerMeter
        writer.Write((uint)0); // biClrUsed
        writer.Write((uint)0); // biClrImportant

        // XOR mask: 32x32 pixels in BGRA (bottom to top)
        // Red folder silhouette: #D32F2F (Crimson Red: B=47, G=47, R=211, A=255)
        // Border: #B71C1C (Dark Red: B=28, G=28, R=183, A=255)
        // Tab at top: rows 24-28 (from bottom: y = 22 to 28), x = 4 to 14
        // Folder body: rows 4 to 22 (from bottom), x = 4 to 28
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isFolderBody = (x >= 4 && x <= 27 && y >= 4 && y <= 21);
                bool isFolderTab = (x >= 4 && x <= 14 && y >= 21 && y <= 26);
                bool isBorder = isFolderBody && (x == 4 || x == 27 || y == 4 || y == 21) ||
                                isFolderTab && (x == 4 || x == 14 || y == 26);

                if (isBorder)
                {
                    writer.Write((byte)28);  // Blue
                    writer.Write((byte)28);  // Green
                    writer.Write((byte)183); // Red
                    writer.Write((byte)255); // Alpha
                }
                else if (isFolderBody || isFolderTab)
                {
                    writer.Write((byte)47);  // Blue
                    writer.Write((byte)47);  // Green
                    writer.Write((byte)211); // Red
                    writer.Write((byte)255); // Alpha
                }
                else
                {
                    writer.Write((uint)0); // Transparent (BGRA = 0,0,0,0)
                }
            }
        }

        // AND mask (1 bit per pixel, 32 pixels = 4 bytes per row)
        // 0 = opaque, 1 = transparent
        for (int y = 0; y < height; y++)
        {
            uint maskRow = 0;
            for (int x = 0; x < width; x++)
            {
                bool isFolderBody = (x >= 4 && x <= 27 && y >= 4 && y <= 21);
                bool isFolderTab = (x >= 4 && x <= 14 && y >= 21 && y <= 26);
                if (!isFolderBody && !isFolderTab)
                {
                    maskRow |= (1u << (31 - x));
                }
            }
            writer.Write(maskRow);
        }

        return bytes;
    }

    /// <summary>
    /// Generates a valid 32x32 32-bit BMP ICO representing a blue partial folder icon
    /// </summary>
    private static byte[] GenerateBlueFolderIco()
    {
        const int width = 32;
        const int height = 32;
        const int bpp = 32;
        int imageSize = 40 + (width * height * 4) + (width * height / 8); // Header + XOR mask + AND mask
        int fileSize = 6 + 16 + imageSize;

        byte[] bytes = new byte[fileSize];
        using var ms = new MemoryStream(bytes);
        using var writer = new BinaryWriter(ms);

        // ICONDIR
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // type 1 = icon
        writer.Write((ushort)1); // count 1 image

        // ICONDIRENTRY
        writer.Write((byte)width);
        writer.Write((byte)height);
        writer.Write((byte)0); // colors
        writer.Write((byte)0); // reserved
        writer.Write((ushort)1); // planes
        writer.Write((ushort)bpp); // bit count
        writer.Write((uint)imageSize); // bytes in res
        writer.Write((uint)22); // image offset

        // BITMAPINFOHEADER (height is doubled for XOR + AND masks)
        writer.Write((uint)40); // biSize
        writer.Write((int)width); // biWidth
        writer.Write((int)(height * 2)); // biHeight (XOR + AND mask height)
        writer.Write((ushort)1); // biPlanes
        writer.Write((ushort)bpp); // biBitCount
        writer.Write((uint)0); // biCompression
        writer.Write((uint)(imageSize - 40)); // biSizeImage
        writer.Write((int)0); // biXPelsPerMeter
        writer.Write((int)0); // biYPelsPerMeter
        writer.Write((uint)0); // biClrUsed
        writer.Write((uint)0); // biClrImportant

        // XOR mask: 32x32 pixels in BGRA (bottom to top)
        // Blue folder silhouette: #1976D2 (Material Blue 700: B=210, G=118, R=25, A=255)
        // Border: #0D47A1 (Material Blue 900: B=161, G=71, R=13, A=255)
        // Tab at top: rows 24-28 (from bottom: y = 22 to 28), x = 4 to 14
        // Folder body: rows 4 to 22 (from bottom), x = 4 to 28
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isFolderBody = (x >= 4 && x <= 27 && y >= 4 && y <= 21);
                bool isFolderTab = (x >= 4 && x <= 14 && y >= 21 && y <= 26);
                bool isBorder = isFolderBody && (x == 4 || x == 27 || y == 4 || y == 21) ||
                                isFolderTab && (x == 4 || x == 14 || y == 26);

                if (isBorder)
                {
                    writer.Write((byte)161); // Blue
                    writer.Write((byte)71);  // Green
                    writer.Write((byte)13);  // Red
                    writer.Write((byte)255); // Alpha
                }
                else if (isFolderBody || isFolderTab)
                {
                    writer.Write((byte)210); // Blue
                    writer.Write((byte)118); // Green
                    writer.Write((byte)25);  // Red
                    writer.Write((byte)255); // Alpha
                }
                else
                {
                    writer.Write((uint)0); // Transparent (BGRA = 0,0,0,0)
                }
            }
        }

        // AND mask (1 bit per pixel, 32 pixels = 4 bytes per row)
        // 0 = opaque, 1 = transparent
        for (int y = 0; y < height; y++)
        {
            uint maskRow = 0;
            for (int x = 0; x < width; x++)
            {
                bool isFolderBody = (x >= 4 && x <= 27 && y >= 4 && y <= 21);
                bool isFolderTab = (x >= 4 && x <= 14 && y >= 21 && y <= 26);
                if (!isFolderBody && !isFolderTab)
                {
                    maskRow |= (1u << (31 - x));
                }
            }
            writer.Write(maskRow);
        }

        return bytes;
    }
}
