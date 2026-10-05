using System;
using System.Runtime.InteropServices;
using Lightshot.Platform.Windows.Interop;
using Lightshot.Platform.Windows.Overlay;

namespace Lightshot.Platform.Windows.Windows;

/// <summary>
/// Fronts any application window to the foreground with the Alt-key fallback.
/// </summary>
public static class WindowPresenter
{
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    public static bool Present(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;

        if (IsIconic(hWnd))
        {
            Win32Window.ShowWindow(hWnd, SW_RESTORE);
        }
        else
        {
            Win32Window.ShowWindow(hWnd, SW_SHOW);
        }

        BringWindowToTop(hWnd);
        return ForegroundGrant.GrantForeground(hWnd);
    }
}
