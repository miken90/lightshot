// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;

namespace Lightshot.Platform.Windows.Interop;

/// <summary>
/// DPI awareness and scaling helpers for Windows PerMonitorV2 mode.
/// </summary>
public static class Dpi
{
    public const int DefaultDpi = 96;

    public enum MONITOR_DPI_TYPE
    {
        MDT_EFFECTIVE_DPI = 0,
        MDT_ANGULAR_DPI = 1,
        MDT_RAW_DPI = 2,
        MDT_DEFAULT = MDT_EFFECTIVE_DPI
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, MONITOR_DPI_TYPE dpiType, out uint dpiX, out uint dpiY);

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);

    /// <summary>
    /// Ensures the current process runs in PerMonitorV2 awareness context.
    /// </summary>
    public static bool EnsurePerMonitorV2()
    {
        try
        {
            return SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets effective DPI for a monitor handle.
    /// </summary>
    public static (uint DpiX, uint DpiY) GetDpiForMonitor(IntPtr hMonitor)
    {
        try
        {
            int hr = GetDpiForMonitor(hMonitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
            if (hr == 0 && dpiX > 0 && dpiY > 0)
            {
                return (dpiX, dpiY);
            }
        }
        catch
        {
        }
        return (DefaultDpi, DefaultDpi);
    }

    /// <summary>
    /// Gets DPI for a specific window HWND.
    /// </summary>
    public static uint GetWindowDpi(IntPtr hWnd)
    {
        try
        {
            uint dpi = GetDpiForWindow(hWnd);
            return dpi > 0 ? dpi : DefaultDpi;
        }
        catch
        {
            return DefaultDpi;
        }
    }

    /// <summary>
    /// Calculates scale factor (e.g. 1.0 for 96 DPI, 1.25 for 120 DPI, 1.5 for 144 DPI).
    /// </summary>
    public static double ScaleFactorFromDpi(uint dpi)
    {
        return dpi / (double)DefaultDpi;
    }
}
