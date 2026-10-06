using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.UiTests;

/// <summary>
/// One area capture gesture must open Lightshot's own overlay on the first try and yield exactly one editor.
/// </summary>
public class AreaCaptureFlowTests
{
    private const string OverlayTitle = "LightshotOverlay";
    private const string EditorTitle = "Lightshot";

    [Fact]
    [Desktop]
    public void FirstPrintScreenPressOpensTheLightshotOverlayAndOneEditor()
    {
        // Windows 11 hands PrintScreen to the Snipping Tool by default; the press must reach Lightshot instead.
        using var process = LaunchApp(arguments: "");
        try
        {
            Assert.True(WaitFor(() => HasHotkeyWindow(process.Id), TimeSpan.FromSeconds(10)), "Hotkeys were never registered.");
            // The sink window exists just before Initialize registers the chords and installs the hook.
            Thread.Sleep(1000);

            Keyboard.Press(VirtualKeyShort.SNAPSHOT);
            Keyboard.Release(VirtualKeyShort.SNAPSHOT);

            bool overlayShown = WaitFor(() => OverlayCount(process.Id) > 0, TimeSpan.FromSeconds(5));
            bool snippingShown = VisibleWindowClasses("SnippingTool").Contains("SnipOverlayRootWindow");
            if (!overlayShown && snippingShown)
            {
                Keyboard.Type(VirtualKeyShort.ESCAPE);
            }
            Assert.False(snippingShown, "PrintScreen opened the Snipping Tool overlay.");
            Assert.True(overlayShown, "PrintScreen did not open the Lightshot overlay.");

            SelectAreaAndAssertOneEditor(process);
        }
        finally
        {
            Quit(process);
        }
    }

    [Fact]
    [Desktop]
    public void SecondAreaTriggerWhileTheOverlayIsOpenAddsNoOverlayAndOneEditor()
    {
        using var process = LaunchApp(arguments: "--area");
        try
        {
            Assert.True(WaitFor(() => OverlayCount(process.Id) > 0, TimeSpan.FromSeconds(10)), "The overlay never appeared.");
            Thread.Sleep(300);
            int overlays = OverlayCount(process.Id);

            // A second launch signals this event and asks the running instance for another area capture.
            using (var activate = EventWaitHandle.OpenExisting(Program.ActivateEventName))
            {
                activate.Set();
            }
            Thread.Sleep(1500);
            Assert.Equal(overlays, OverlayCount(process.Id));

            SelectAreaAndAssertOneEditor(process);
        }
        finally
        {
            Quit(process);
        }
    }

    [Fact]
    [Desktop]
    public void ClosingTheOverlayFromOutsideCancelsSoTheNextCaptureStillOpens()
    {
        using var process = LaunchApp(arguments: "--area");
        try
        {
            Assert.True(WaitFor(() => OverlayCount(process.Id) > 0, TimeSpan.FromSeconds(10)), "The overlay never appeared.");
            Thread.Sleep(300);

            // Alt+F4 or another app closing the window arrives as WM_CLOSE.
            PostMessageW(FirstOverlayHandle(process.Id), 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            Assert.True(WaitFor(() => OverlayCount(process.Id) == 0, TimeSpan.FromSeconds(5)), "WM_CLOSE did not cancel the overlay.");

            using (var activate = EventWaitHandle.OpenExisting(Program.ActivateEventName))
            {
                activate.Set();
            }
            Assert.True(WaitFor(() => OverlayCount(process.Id) > 0, TimeSpan.FromSeconds(10)),
                "A capture after the closed overlay was ignored.");

            SelectAreaAndAssertOneEditor(process);
        }
        finally
        {
            Quit(process);
        }
    }

    private static void SelectAreaAndAssertOneEditor(Process process)
    {
        using var automation = new UIA3Automation();
        using var app = FlaUI.Core.Application.Attach(process);
        Thread.Sleep(300);

        Mouse.MoveTo(new System.Drawing.Point(350, 250));
        Thread.Sleep(100);
        Mouse.Down(MouseButton.Left);
        Thread.Sleep(100);
        Mouse.MoveTo(new System.Drawing.Point(650, 450));
        Thread.Sleep(100);
        Mouse.Up(MouseButton.Left);
        Thread.Sleep(200);
        // Confirms the selection when the user enabled "adjust area before capture".
        Keyboard.Type(VirtualKeyShort.RETURN);

        int EditorCount() => app.GetAllTopLevelWindows(automation).Count(w => w.Title == EditorTitle);

        var editor = Retry.WhileFalse(() => EditorCount() > 0, TimeSpan.FromSeconds(10));
        Assert.True(editor.Result, "No editor opened after the selection.");

        // A stacked second overlay or a duplicate result would surface as a second editor or a leftover overlay.
        Thread.Sleep(1500);
        Assert.Equal(1, EditorCount());
        Assert.Equal(0, OverlayCount(process.Id));
    }

    private static Process LaunchApp(string arguments)
    {
        // The previous test's instance may still be shutting down; a user-run instance never goes away.
        Assert.True(WaitFor(() => Process.GetProcessesByName("Lightshot.App").Length == 0, TimeSpan.FromSeconds(10)),
            "Another Lightshot.App is running; it would own the hotkeys and single-instance mutex.");

        string exePath = FindAppExecutable();
        Assert.True(File.Exists(exePath), $"App executable not found at: {exePath}");
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false
        };
        psi.EnvironmentVariables["LIGHTSHOT_DISABLE_ONBOARDING"] = "1";

        var process = Process.Start(psi);
        Assert.NotNull(process);
        return process;
    }

    private static void Quit(Process process)
    {
        try
        {
            using var quit = EventWaitHandle.OpenExisting(Program.QuitEventName);
            quit.Set();
            if (process.WaitForExit(5000)) return;
        }
        catch
        {
            // Fall through to Kill: the instance this test started must not outlive it.
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(5000);
            }
        }
        catch
        {
            // Already exited
        }
    }

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition()) return true;
            Thread.Sleep(100);
        }
        return condition();
    }

    private static string FindAppExecutable()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Lightshot.slnx")))
            {
                return Path.Combine(current, "src", "Lightshot.App", "bin", "Debug",
                    "net10.0-windows10.0.22621.0", "Lightshot.App.exe");
            }
            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }
        return string.Empty;
    }

    private static int OverlayCount(int processId) =>
        VisibleWindows(new[] { processId }).Count(w => w.Title == OverlayTitle);

    private static IntPtr FirstOverlayHandle(int processId) =>
        VisibleWindows(new[] { processId }).First(w => w.Title == OverlayTitle).Handle;

    private static List<string> VisibleWindowClasses(string processName) =>
        VisibleWindows(Process.GetProcessesByName(processName).Select(p => p.Id).ToArray())
            .Select(w => w.ClassName).ToList();

    private static List<(string Title, string ClassName, IntPtr Handle)> VisibleWindows(int[] processIds)
    {
        var found = new List<(string, string, IntPtr)>();
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (processIds.Contains((int)pid) && IsWindowVisible(hWnd))
            {
                var title = new StringBuilder(128);
                var cls = new StringBuilder(128);
                GetWindowTextW(hWnd, title, 128);
                GetClassNameW(hWnd, cls, 128);
                found.Add((title.ToString(), cls.ToString(), hWnd));
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // The hotkey sink is a message-only window, so it is found under HWND_MESSAGE rather than by EnumWindows.
    private static bool HasHotkeyWindow(int processId)
    {
        IntPtr child = IntPtr.Zero;
        while ((child = FindWindowExW(new IntPtr(-3) /* HWND_MESSAGE */, child, null, "HotkeyWindow")) != IntPtr.Zero)
        {
            GetWindowThreadProcessId(child, out uint pid);
            if (pid == (uint)processId) return true;
        }
        return false;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string lpszWindow);
}
