// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lightshot.Platform.Windows.Capture;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class WindowEnumeratorTests
{
    private const int DWMWA_CLOAKED = 14;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [Fact]
    [Desktop]
    public void ExcludesOwnAndCloakedWindows()
    {
        uint currentPid = (uint)Process.GetCurrentProcess().Id;
        var windows = WindowEnumerator.EnumerateWindows();

        // 1. Must exclude own process windows
        foreach (var win in windows)
        {
            Assert.NotEqual(currentPid, win.ProcessId);
        }

        // 2. Must exclude cloaked windows
        foreach (var win in windows)
        {
            int hr = DwmGetWindowAttribute(win.Handle, DWMWA_CLOAKED, out int cloaked, sizeof(int));
            if (hr == 0)
            {
                Assert.Equal(0, cloaked);
            }
        }

        // 3. Must exclude specified PID
        if (windows.Count > 0)
        {
            uint targetPid = windows[0].ProcessId;
            var filtered = WindowEnumerator.EnumerateWindows(excludeProcessId: targetPid);
            foreach (var win in filtered)
            {
                Assert.NotEqual(targetPid, win.ProcessId);
            }
        }
    }
}
