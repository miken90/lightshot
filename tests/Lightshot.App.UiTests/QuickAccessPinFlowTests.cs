// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.UiTests;

public class QuickAccessPinFlowTests
{
    [Fact]
    [Desktop]
    public void CaptureArea_ShowsQuickAccessCard_PinsImage()
    {
        // 1. Terminate any previous Lightshot instances
        CleanupPreviousProcesses();

        // 2. Locate executable and launch with --quick-access
        string exePath = FindAppExecutable();
        Assert.True(File.Exists(exePath), $"App executable not found at: {exePath}");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "--quick-access",
            UseShellExecute = false,
            CreateNoWindow = false
        };

        psi.EnvironmentVariables["LIGHTSHOT_DISABLE_ONBOARDING"] = "1";

        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            psi.EnvironmentVariables["DOTNET_ROOT"] = dotnetRoot;
        }
        else
        {
            string defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet");
            if (Directory.Exists(defaultRoot))
            {
                psi.EnvironmentVariables["DOTNET_ROOT"] = defaultRoot;
            }
        }

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        try
        {
            using var automation = new UIA3Automation();
            using var app = Application.Attach(process);

            // 3. Wait for overlay window to appear
            var overlayWindowResult = Retry.WhileNull(
                () =>
                {
                    var w = app.GetAllTopLevelWindows(automation)
                        .FirstOrDefault(win => win.Title == "LightshotOverlay" || (win.ClassName != null && win.ClassName.StartsWith("LightshotOverlayWindow")));
                    if (w != null) return w;

                    IntPtr hwnd = FindOverlayHwnd(process.Id);
                    if (hwnd != IntPtr.Zero)
                    {
                        try
                        {
                            return automation.FromHandle(hwnd)?.AsWindow();
                        }
                        catch { }
                    }
                    return null;
                },
                TimeSpan.FromSeconds(10));

            Assert.NotNull(overlayWindowResult?.Result);
            Thread.Sleep(300);

            // 4. Drag selection rectangle (300x200)
            const int selX = 350;
            const int selY = 250;
            const int selW = 300;
            const int selH = 200;

            Mouse.MoveTo(new System.Drawing.Point(selX, selY));
            Thread.Sleep(100);
            Mouse.Down(MouseButton.Left);
            Thread.Sleep(100);
            Mouse.MoveTo(new System.Drawing.Point(selX + selW, selY + selH));
            Thread.Sleep(100);
            Mouse.Up(MouseButton.Left);
            Thread.Sleep(200);

            Keyboard.Press(VirtualKeyShort.RETURN);
            Thread.Sleep(100);
            Keyboard.Release(VirtualKeyShort.RETURN);

            // 5. Assert QuickAccessCard appears and Editor ("Lightshot") does NOT appear
            var cardWindowResult = Retry.WhileNull(
                () => app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "QuickAccessCard"),
                TimeSpan.FromSeconds(10));

            Assert.NotNull(cardWindowResult?.Result);
            var cardWindow = cardWindowResult.Result;

            var editorWindow = app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "Lightshot");
            Assert.Null(editorWindow);

            // 6. Hover over the card to reveal Pin button
            var cardBounds = cardWindow.BoundingRectangle;
            Mouse.MoveTo(new System.Drawing.Point((int)(cardBounds.Left + cardBounds.Width / 2), (int)(cardBounds.Top + cardBounds.Height / 2)));
            Thread.Sleep(300);

            var pinButton = cardWindow.FindFirstDescendant(cf => cf.ByAutomationId("PinButton"))?.AsButton();
            Assert.NotNull(pinButton);
            pinButton.Click();
            Thread.Sleep(300);

            // 7. Assert "Lightshot Pin" window appears
            var pinWindowResult = Retry.WhileNull(
                () => app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "Lightshot Pin"),
                TimeSpan.FromSeconds(10));

            Assert.NotNull(pinWindowResult?.Result);
            var pinWindow = pinWindowResult.Result;
            Assert.NotNull(pinWindow);

            // 8. Close pin window via Escape key
            pinWindow.Focus();
            Thread.Sleep(100);
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Thread.Sleep(100);
            Keyboard.Release(VirtualKeyShort.ESCAPE);
            Thread.Sleep(200);

            // 9. Signal application exit
            SignalAppQuit();
            bool exited = process.WaitForExit(5000);
            Assert.True(exited, "App failed to exit within 5s after quit signal.");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch { }

            ResetSettingsFile();
        }
    }

    [Fact]
    [Desktop]
    public void Editor_PinAction_OpensPinWindow()
    {
        // 1. Terminate any previous Lightshot instances
        CleanupPreviousProcesses();

        // 2. Locate executable and launch with --area
        string exePath = FindAppExecutable();
        Assert.True(File.Exists(exePath), $"App executable not found at: {exePath}");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "--area",
            UseShellExecute = false,
            CreateNoWindow = false
        };

        psi.EnvironmentVariables["LIGHTSHOT_DISABLE_ONBOARDING"] = "1";

        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            psi.EnvironmentVariables["DOTNET_ROOT"] = dotnetRoot;
        }

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        try
        {
            using var automation = new UIA3Automation();
            using var app = Application.Attach(process);

            // 3. Wait for overlay
            var overlayWindowResult = Retry.WhileNull(
                () =>
                {
                    var w = app.GetAllTopLevelWindows(automation)
                        .FirstOrDefault(win => win.Title == "LightshotOverlay" || (win.ClassName != null && win.ClassName.StartsWith("LightshotOverlayWindow")));
                    if (w != null) return w;

                    IntPtr hwnd = FindOverlayHwnd(process.Id);
                    if (hwnd != IntPtr.Zero)
                    {
                        try { return automation.FromHandle(hwnd)?.AsWindow(); } catch { }
                    }
                    return null;
                },
                TimeSpan.FromSeconds(10));

            Assert.NotNull(overlayWindowResult?.Result);
            Thread.Sleep(300);

            // 4. Select area
            const int selX = 350;
            const int selY = 250;
            Mouse.MoveTo(new System.Drawing.Point(selX, selY));
            Thread.Sleep(100);
            Mouse.Down(MouseButton.Left);
            Thread.Sleep(100);
            Mouse.MoveTo(new System.Drawing.Point(selX + 300, selY + 200));
            Thread.Sleep(100);
            Mouse.Up(MouseButton.Left);
            Thread.Sleep(200);

            Keyboard.Press(VirtualKeyShort.RETURN);
            Thread.Sleep(100);
            Keyboard.Release(VirtualKeyShort.RETURN);

            // 5. Wait for Editor
            var editorWindowResult = Retry.WhileNull(
                () => app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "Lightshot"),
                TimeSpan.FromSeconds(10));

            Assert.NotNull(editorWindowResult?.Result);
            var editorWindow = editorWindowResult.Result;
            editorWindow.Focus();
            Thread.Sleep(200);

            // 6. Click PinButton on Editor
            var pinButton = editorWindow.FindFirstDescendant(cf => cf.ByAutomationId("PinButton"))?.AsButton();
            Assert.NotNull(pinButton);
            try
            {
                pinButton.Invoke();
            }
            catch
            {
                pinButton.Click();
            }
            Thread.Sleep(500);

            // 7. Assert "Lightshot Pin" window appears
            var pinWindowResult = Retry.WhileNull(
                () => app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "Lightshot Pin"),
                TimeSpan.FromSeconds(10));

            Assert.NotNull(pinWindowResult?.Result);
            var pinWindow = pinWindowResult!.Result;
            Assert.NotNull(pinWindow);

            // 8. Close pin window
            pinWindow.Focus();
            Thread.Sleep(100);
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Thread.Sleep(100);
            Keyboard.Release(VirtualKeyShort.ESCAPE);
            Thread.Sleep(200);

            // 9. Signal application exit
            SignalAppQuit();
            bool exited = process.WaitForExit(5000);
            Assert.True(exited, "App failed to exit within 5s after quit signal.");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch { }

            ResetSettingsFile();
        }
    }

    private static void CleanupPreviousProcesses()
    {
        foreach (var p in Process.GetProcessesByName("Lightshot.App"))
        {
            try
            {
                p.Kill();
                p.WaitForExit(1000);
            }
            catch { }
        }
        Thread.Sleep(300);
        ResetSettingsFile();
    }

    private static void ResetSettingsFile()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string settingsPath = Path.Combine(appData, "Lightshot", "settings.json");
            if (File.Exists(settingsPath))
            {
                string json = File.ReadAllText(settingsPath);
                json = json.Replace("\"openInEditor\": false", "\"openInEditor\": true");
                json = json.Replace("\"openInEditor\":false", "\"openInEditor\":true");
                File.WriteAllText(settingsPath, json);
            }
        }
        catch { }
    }

    private static void SignalAppQuit()
    {
        try
        {
            using var quitHandle = EventWaitHandle.OpenExisting(@"Local\Lightshot.Quit");
            quitHandle.Set();
        }
        catch { }
    }

    private static string FindAppExecutable()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(
                current,
                "src",
                "Lightshot.App",
                "bin",
                "Debug",
                "net10.0-windows10.0.22621.0",
                "Lightshot.App.exe");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (File.Exists(Path.Combine(current, "Lightshot.slnx")) || File.Exists(Path.Combine(current, "global.json")))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        return string.Empty;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private static IntPtr FindOverlayHwnd(int processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId && IsWindowVisible(hWnd))
            {
                var sb = new System.Text.StringBuilder(128);
                GetWindowTextW(hWnd, sb, 128);
                if (sb.ToString() == "LightshotOverlay")
                {
                    found = hWnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
