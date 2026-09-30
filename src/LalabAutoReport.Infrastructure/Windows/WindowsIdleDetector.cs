using System;
using System.Runtime.InteropServices;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.Infrastructure.Windows;

public class WindowsIdleDetector : IIdleDetectionService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public TimeSpan GetIdleTime()
    {
        try
        {
            var lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(lii);

            if (GetLastInputInfo(ref lii))
            {
                uint idleMs = (uint)Environment.TickCount - lii.dwTime;
                return TimeSpan.FromMilliseconds(idleMs);
            }
        }
        catch
        {
            // Fallback if not supported or exception occurs
        }

        return TimeSpan.Zero;
    }
}
