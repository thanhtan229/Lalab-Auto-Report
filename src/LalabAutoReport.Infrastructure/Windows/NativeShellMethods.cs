using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LalabAutoReport.Infrastructure.Windows;

[SupportedOSPlatform("windows")]
internal static class NativeShellMethods
{
    private const int SHCNE_UPDATEITEM = 0x00002000;
    private const int SHCNE_UPDATEDIR   = 0x00001000;
    private const int SHCNE_ATTRIBUTES  = 0x00000800;
    private const uint SHCNF_PATHW      = 0x0005;
    private const uint SHCNF_FLUSH      = 0x1000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHFOLDERCUSTOMSETTINGS
    {
        public uint dwSize;
        public uint dwMask;
        public IntPtr pvid;
        public string pszWebViewTemplate;
        public uint cchWebViewTemplate;
        public string pszWebViewTemplateVersion;
        public string pszInfoTip;
        public uint cchInfoTip;
        public IntPtr pclsid;
        public uint dwFlags;
        public string pszIconFile;
        public uint cchIconFile;
        public int iIconIndex;
        public string pszLogo;
        public uint cchLogo;
    }

    private const uint FCSM_ICONFILE = 0x00000010;
    private const uint FCS_FORCEWRITE = 0x00000002;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetSetFolderCustomSettings(
        ref SHFOLDERCUSTOMSETTINGS pfcs,
        string pszPath,
        uint dwReadWrite);

    /// <summary>
    /// Uses Windows Shell native API to apply custom folder icon and invalidate explorer icon cache immediately.
    /// </summary>
    public static bool SetFolderCustomIcon(string folderPath, string iconPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !OperatingSystem.IsWindows()) return false;

        try
        {
            var settings = new SHFOLDERCUSTOMSETTINGS();
            settings.dwSize = (uint)Marshal.SizeOf(typeof(SHFOLDERCUSTOMSETTINGS));
            settings.dwMask = FCSM_ICONFILE;
            settings.pszIconFile = iconPath;
            settings.cchIconFile = 0;
            settings.iIconIndex = 0;

            int hr = SHGetSetFolderCustomSettings(ref settings, folderPath, FCS_FORCEWRITE);
            return hr == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Uses Windows Shell native API to clear custom folder icon settings.
    /// </summary>
    public static bool ClearFolderCustomIcon(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !OperatingSystem.IsWindows()) return false;

        try
        {
            var settings = new SHFOLDERCUSTOMSETTINGS();
            settings.dwSize = (uint)Marshal.SizeOf(typeof(SHFOLDERCUSTOMSETTINGS));
            settings.dwMask = FCSM_ICONFILE;
            settings.pszIconFile = string.Empty;
            settings.cchIconFile = 0;
            settings.iIconIndex = 0;

            int hr = SHGetSetFolderCustomSettings(ref settings, folderPath, FCS_FORCEWRITE);
            return hr == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Flushes Windows Shell notification queue for both the folder and its parent directory,
    /// forcing Explorer to instantly repaint the folder icon without waiting for idle timeout or manual F5.
    /// </summary>
    public static void NotifyFolderChanged(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !OperatingSystem.IsWindows()) return;

        try
        {
            IntPtr folderPtr = Marshal.StringToHGlobalUni(folderPath);
            string? parentDir = Path.GetDirectoryName(folderPath);
            IntPtr parentPtr = !string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir)
                ? Marshal.StringToHGlobalUni(parentDir)
                : IntPtr.Zero;

            try
            {
                uint flags = SHCNF_PATHW | SHCNF_FLUSH;

                // 1. Notify item & attributes changed for the folder itself
                SHChangeNotify(SHCNE_UPDATEITEM | SHCNE_ATTRIBUTES, flags, folderPtr, IntPtr.Zero);

                // 2. Notify directory contents updated for parent folder (so Explorer window repaints the item icon immediately)
                if (parentPtr != IntPtr.Zero)
                {
                    SHChangeNotify(SHCNE_UPDATEDIR, flags, parentPtr, IntPtr.Zero);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(folderPtr);
                if (parentPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(parentPtr);
                }
            }
        }
        catch
        {
            // Best-effort notification; do not crash on shell notify failure
        }
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST      = 0x0000;

    /// <summary>
    /// Notifies Windows Explorer to purge and refresh all cached file associations and context menu icons.
    /// </summary>
    public static void NotifyAssocChanged()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }
}
