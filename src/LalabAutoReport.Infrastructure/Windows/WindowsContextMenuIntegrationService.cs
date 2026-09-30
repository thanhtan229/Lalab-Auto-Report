using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Win32;

namespace LalabAutoReport.Infrastructure.Windows;

[SupportedOSPlatform("windows")]
public class WindowsContextMenuIntegrationService : IContextMenuIntegrationService
{
    // Quick Bill Menu
    public const string QuickBillMenuKeyName = "LalabQuickBill";
    public const string QuickBillMenuTitle = "⚡ QUICK BILL (Lalab)";
    public const string QuickBillDirShellKeyPath = @"Software\Classes\Directory\shell\" + QuickBillMenuKeyName;
    public const string QuickBillDirBgShellKeyPath = @"Software\Classes\Directory\Background\shell\" + QuickBillMenuKeyName;

    // Toggle Printed Menu
    public const string TogglePrintedMenuKeyName = "LalabTogglePrinted";
    public const string TogglePrintedMenuTitle = "🏷️ ĐÃ IN (Lalab)";
    public const string TogglePrintedDirShellKeyPath = @"Software\Classes\Directory\shell\" + TogglePrintedMenuKeyName;
    public const string TogglePrintedDirBgShellKeyPath = @"Software\Classes\Directory\Background\shell\" + TogglePrintedMenuKeyName;

    // Backward-compatibility aliases for legacy code/tests
    public const string MenuKeyName = QuickBillMenuKeyName;
    public const string MenuTitle = QuickBillMenuTitle;
    public const string DirShellKeyPath = QuickBillDirShellKeyPath;
    public const string DirBgShellKeyPath = QuickBillDirBgShellKeyPath;

    public virtual string GetExecutablePath()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || exePath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var baseDir = AppContext.BaseDirectory;
            var candidate = Path.Combine(baseDir, "LalabAutoReport.UI.exe");
            if (File.Exists(candidate))
            {
                exePath = candidate;
            }
            else
            {
                try
                {
                    exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                }
                catch
                {
                    exePath = candidate;
                }
            }
        }
        return exePath;
    }

    public virtual string GetIconPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string sharedIcon = Path.Combine(localAppData, "LalabAutoReport", "Assets", "quick_bill.ico");
        if (File.Exists(sharedIcon)) return sharedIcon;

        string? exePath = GetExecutablePath();
        string baseDir = !string.IsNullOrWhiteSpace(exePath) ? (Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory) : AppContext.BaseDirectory;

        // Check Resources\quick_bill.ico
        string resPath = Path.Combine(baseDir, "Resources", "quick_bill.ico");
        if (File.Exists(resPath)) return resPath;

        // Check root quick_bill.ico
        string rootPath = Path.Combine(baseDir, "quick_bill.ico");
        if (File.Exists(rootPath)) return rootPath;

        return string.Empty;
    }

    public virtual string GetPrintedIconPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string sharedIcon = Path.Combine(localAppData, "LalabAutoReport", "Assets", "folder_printed.ico");
        if (File.Exists(sharedIcon)) return sharedIcon;

        string? exePath = GetExecutablePath();
        string baseDir = !string.IsNullOrWhiteSpace(exePath) ? (Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory) : AppContext.BaseDirectory;

        string resPath = Path.Combine(baseDir, "Resources", "folder_printed.ico");
        if (File.Exists(resPath)) return resPath;

        return string.Empty;
    }

    public bool IsContextMenuRegistered()
    {
        try
        {
            using var dirCmd = Registry.CurrentUser.OpenSubKey(QuickBillDirShellKeyPath + @"\command");
            using var bgCmd = Registry.CurrentUser.OpenSubKey(QuickBillDirBgShellKeyPath + @"\command");
            using var printDirCmd = Registry.CurrentUser.OpenSubKey(TogglePrintedDirShellKeyPath + @"\command");
            using var printBgCmd = Registry.CurrentUser.OpenSubKey(TogglePrintedDirBgShellKeyPath + @"\command");

            if (dirCmd == null || bgCmd == null || printDirCmd == null || printBgCmd == null)
            {
                return false;
            }

            var dirVal = dirCmd.GetValue(string.Empty) as string;
            var bgVal = bgCmd.GetValue(string.Empty) as string;
            var printDirVal = printDirCmd.GetValue(string.Empty) as string;
            var printBgVal = printBgCmd.GetValue(string.Empty) as string;

            return !string.IsNullOrWhiteSpace(dirVal) &&
                   !string.IsNullOrWhiteSpace(bgVal) &&
                   !string.IsNullOrWhiteSpace(printDirVal) &&
                   !string.IsNullOrWhiteSpace(printBgVal);
        }
        catch
        {
            return false;
        }
    }

    public void RegisterContextMenu()
    {
        string exePath = GetExecutablePath();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            throw new InvalidOperationException("Không thể xác định đường dẫn tệp thực thi của ứng dụng.");
        }

        string quickBillDirCommand = $"\"{exePath}\" --quick-bill \"%1\"";
        string quickBillBgCommand = $"\"{exePath}\" --quick-bill \"%V\"";

        string togglePrintedDirCommand = $"\"{exePath}\" --toggle-printed \"%1\"";
        string togglePrintedBgCommand = $"\"{exePath}\" --toggle-printed \"%V\"";

        string quickBillIconPath = GetIconPath();
        string quickBillIconVal = !string.IsNullOrEmpty(quickBillIconPath) ? $"\"{quickBillIconPath}\"" : $"\"{exePath}\",0";

        string printedIconPath = GetPrintedIconPath();
        string printedIconVal = !string.IsNullOrEmpty(printedIconPath) ? $"\"{printedIconPath}\"" : $"\"{exePath}\",0";

        // 1. QUICK BILL - Directory
        using (var key = Registry.CurrentUser.CreateSubKey(QuickBillDirShellKeyPath))
        {
            key.SetValue(string.Empty, QuickBillMenuTitle);
            key.SetValue("Icon", quickBillIconVal);
            using var cmdKey = key.CreateSubKey("command");
            cmdKey.SetValue(string.Empty, quickBillDirCommand);
        }

        // 2. QUICK BILL - Directory Background
        using (var bgKey = Registry.CurrentUser.CreateSubKey(QuickBillDirBgShellKeyPath))
        {
            bgKey.SetValue(string.Empty, QuickBillMenuTitle);
            bgKey.SetValue("Icon", quickBillIconVal);
            using var cmdKey = bgKey.CreateSubKey("command");
            cmdKey.SetValue(string.Empty, quickBillBgCommand);
        }

        // 3. TOGGLE PRINTED (ĐÃ IN) - Directory
        using (var key = Registry.CurrentUser.CreateSubKey(TogglePrintedDirShellKeyPath))
        {
            key.SetValue(string.Empty, TogglePrintedMenuTitle);
            key.SetValue("Icon", printedIconVal);
            using var cmdKey = key.CreateSubKey("command");
            cmdKey.SetValue(string.Empty, togglePrintedDirCommand);
        }

        // 4. TOGGLE PRINTED (ĐÃ IN) - Directory Background
        using (var bgKey = Registry.CurrentUser.CreateSubKey(TogglePrintedDirBgShellKeyPath))
        {
            bgKey.SetValue(string.Empty, TogglePrintedMenuTitle);
            bgKey.SetValue("Icon", printedIconVal);
            using var cmdKey = bgKey.CreateSubKey("command");
            cmdKey.SetValue(string.Empty, togglePrintedBgCommand);
        }

        // Notify shell to flush context menu icon cache
        NativeShellMethods.NotifyAssocChanged();
    }

    public void UnregisterContextMenu()
    {
        // 1. Unregister QUICK BILL
        try { Registry.CurrentUser.DeleteSubKeyTree(QuickBillDirShellKeyPath, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(QuickBillDirBgShellKeyPath, false); } catch { }

        // 2. Unregister TOGGLE PRINTED
        try { Registry.CurrentUser.DeleteSubKeyTree(TogglePrintedDirShellKeyPath, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(TogglePrintedDirBgShellKeyPath, false); } catch { }

        // Notify shell to flush context menu icon cache
        NativeShellMethods.NotifyAssocChanged();
    }

    public void EnsureContextMenuRegistered()
    {
        RegisterContextMenu();
    }

    public string GenerateRegFileContent()
    {
        string exePath = GetExecutablePath();
        string escapedExePath = exePath.Replace(@"\", @"\\");

        string iconPath = GetIconPath();
        string escapedIconVal = !string.IsNullOrEmpty(iconPath)
            ? $"\\\"{iconPath.Replace(@"\", @"\\")}\\\""
            : $"\\\"{escapedExePath}\\\",0";

        string printedIconPath = GetPrintedIconPath();
        string escapedPrintedIconVal = !string.IsNullOrEmpty(printedIconPath)
            ? $"\\\"{printedIconPath.Replace(@"\", @"\\")}\\\""
            : $"\\\"{escapedExePath}\\\",0";

        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine();
        sb.AppendLine("; Tích hợp menu chuột phải QUICK BILL & ĐÃ IN cho Lalab Auto Report");
        sb.AppendLine("; Hỗ trợ Windows 10 và Windows 11");
        sb.AppendLine();

        // QUICK BILL
        sb.AppendLine(@"[HKEY_CURRENT_USER\Software\Classes\Directory\shell\LalabQuickBill]");
        sb.AppendLine($"@=\"{QuickBillMenuTitle}\"");
        sb.AppendLine($"\"Icon\"=\"{escapedIconVal}\"");
        sb.AppendLine();
        sb.AppendLine(@"[HKEY_CURRENT_USER\Software\Classes\Directory\shell\LalabQuickBill\command]");
        sb.AppendLine($"@=\"\\\"{escapedExePath}\\\" --quick-bill \\\"%1\\\"\"");
        sb.AppendLine();
        sb.AppendLine(@"[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\LalabQuickBill]");
        sb.AppendLine($"@=\"{QuickBillMenuTitle}\"");
        sb.AppendLine($"\"Icon\"=\"{escapedIconVal}\"");
        sb.AppendLine();
        sb.AppendLine(@"[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\LalabQuickBill\command]");
        sb.AppendLine($"@=\"\\\"{escapedExePath}\\\" --quick-bill \\\"%V\\\"\"");
        sb.AppendLine();

        // TOGGLE PRINTED
        sb.AppendLine(@"[HKEY_CURRENT_USER\Software\Classes\Directory\shell\LalabTogglePrinted]");
        sb.AppendLine($"@=\"{TogglePrintedMenuTitle}\"");
        sb.AppendLine($"\"Icon\"=\"{escapedPrintedIconVal}\"");
        sb.AppendLine();
        sb.AppendLine(@"[HKEY_CURRENT_USER\Software\Classes\Directory\shell\LalabTogglePrinted\command]");
        sb.AppendLine($"@=\"\\\"{escapedExePath}\\\" --toggle-printed \\\"%1\\\"\"");
        sb.AppendLine();
        sb.AppendLine(@"[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\LalabTogglePrinted]");
        sb.AppendLine($"@=\"{TogglePrintedMenuTitle}\"");
        sb.AppendLine($"\"Icon\"=\"{escapedPrintedIconVal}\"");
        sb.AppendLine();
        sb.AppendLine(@"[HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\LalabTogglePrinted\command]");
        sb.AppendLine($"@=\"\\\"{escapedExePath}\\\" --toggle-printed \\\"%V\\\"\"");
        sb.AppendLine();

        return sb.ToString();
    }
}
