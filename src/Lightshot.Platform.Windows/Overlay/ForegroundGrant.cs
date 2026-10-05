using System;
using System.Diagnostics;
using System.Threading;
using Lightshot.Platform.Windows.Interop;

namespace Lightshot.Platform.Windows.Overlay;

/// <summary>
/// Ensures an overlay or editor window gains foreground activation and input focus,
/// using the proven AttachThreadInput + simulated Alt-key fallback if needed.
/// </summary>
public static class ForegroundGrant
{
    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static bool GrantForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;

        Win32Window.SetForegroundWindow(hWnd);
        Win32Window.SetFocus(hWnd);

        IntPtr currentFg = Win32Window.GetForegroundWindow();
        if (currentFg == hWnd)
        {
            return true;
        }

        // Fallback: AttachThreadInput to the current foreground window's thread and pulse Alt key
        uint fgThread = Win32Window.GetWindowThreadProcessId(currentFg, out _);
        uint curThread = Win32Window.GetCurrentThreadId();

        if (fgThread != 0 && fgThread != curThread)
        {
            try
            {
                Win32Window.AttachThreadInput(curThread, fgThread, true);
                Win32Window.keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
                Win32Window.SetForegroundWindow(hWnd);
                Win32Window.SetFocus(hWnd);
                Win32Window.keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            finally
            {
                Win32Window.AttachThreadInput(curThread, fgThread, false);
            }
        }
        else
        {
            Win32Window.SetForegroundWindow(hWnd);
            Win32Window.SetFocus(hWnd);
        }

        Win32Window.PumpMessages(2);
        return Win32Window.GetForegroundWindow() == hWnd;
    }
}
