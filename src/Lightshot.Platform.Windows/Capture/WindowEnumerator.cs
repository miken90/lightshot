// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Capture;

/// <summary>
/// A candidate window detected in the desktop z-order.
/// </summary>
public record WindowCandidate(
    IntPtr Handle,
    uint Id,
    Rect Bounds,
    string Title,
    uint ProcessId,
    int ZOrder
);

/// <summary>
/// Enumerates visible top-level windows in z-order, obtaining visible DWM frame bounds
/// (minus drop shadow) and excluding own, minimised, and cloaked windows.
/// </summary>
public static class WindowEnumerator
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    /// <summary>
    /// Enumerates candidate top-level windows in z-order (topmost first).
    /// </summary>
    public static IReadOnlyList<WindowCandidate> EnumerateWindows(uint? excludeProcessId = null)
    {
        uint currentPid = excludeProcessId ?? (uint)Process.GetCurrentProcess().Id;
        var candidates = new List<WindowCandidate>();
        int zOrder = 0;

        EnumWindowsProc proc = (IntPtr hWnd, IntPtr lParam) =>
        {
            // 1. Must be visible
            if (!IsWindowVisible(hWnd)) return true;

            // 2. Must not be minimised
            if (IsIconic(hWnd)) return true;

            // 3. Exclude own process
            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == currentPid) return true;

            // 4. Exclude cloaked windows (hidden by virtual desktops, Shell, or UWP app lifecycle)
            int hrCloak = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int));
            if (hrCloak == 0 && cloaked != 0) return true;

            // 5. Exclude system class non-windows
            var classSb = new StringBuilder(256);
            GetClassNameW(hWnd, classSb, 256);
            string className = classSb.ToString();
            if (className == "Progman" || className == "WorkerW")
            {
                return true;
            }

            // 6. Get DWM extended frame bounds (without shadow)
            RECT frameRect;
            int hrBounds = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out frameRect, Marshal.SizeOf<RECT>());
            if (hrBounds != 0 || frameRect.Right <= frameRect.Left || frameRect.Bottom <= frameRect.Top)
            {
                if (!GetWindowRect(hWnd, out frameRect) || frameRect.Right <= frameRect.Left || frameRect.Bottom <= frameRect.Top)
                {
                    return true;
                }
            }

            int w = frameRect.Right - frameRect.Left;
            int h = frameRect.Bottom - frameRect.Top;
            if (w <= 0 || h <= 0) return true;

            var titleSb = new StringBuilder(512);
            GetWindowTextW(hWnd, titleSb, 512);
            string title = titleSb.ToString();

            uint windowId = (uint)hWnd.ToInt64();
            var bounds = new Rect(frameRect.Left, frameRect.Top, w, h);

            candidates.Add(new WindowCandidate(
                Handle: hWnd,
                Id: windowId,
                Bounds: bounds,
                Title: title,
                ProcessId: processId,
                ZOrder: zOrder++
            ));

            return true;
        };

        try
        {
            EnumWindows(proc, IntPtr.Zero);
        }
        catch
        {
        }

        return candidates;
    }

    /// <summary>
    /// Enumerates candidate windows as FrozenWindow items for FrozenScreen.
    /// </summary>
    public static IReadOnlyList<FrozenWindow> EnumerateFrozenWindows(uint? excludeProcessId = null)
    {
        var candidates = EnumerateWindows(excludeProcessId);
        var list = new List<FrozenWindow>(candidates.Count);
        foreach (var c in candidates)
        {
            list.Add(new FrozenWindow(c.Id, c.Bounds, null));
        }
        return list;
    }
}
