// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Buffers.Binary;
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

public class EditorFlowTests : IDisposable
{
    private readonly IsolatedApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    [Desktop]
    public void DrawArrowCopyAndClose()
    {
        // 2. Clear clipboard
        ClearClipboard();

        // 3. Track pre-existing screenshot files to assert no file is written
        string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        DateTime startTime = DateTime.UtcNow.AddSeconds(-2);

        // 4. Locate executable and launch with --area
        string exePath = FindAppExecutable();
        Assert.True(File.Exists(exePath), $"App executable not found at: {exePath}");

        var psi = _app.StartInfo(exePath, "--area");
        psi.CreateNoWindow = false;

        using var process = _app.Start(psi);
        Assert.NotNull(process);

        try
        {
            using var automation = new UIA3Automation();
            using var app = Application.Attach(process.Id);

            // 5. Wait for overlay window to initialize and show (robust replacement for fixed Thread.Sleep)
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
                        catch
                        {
                        }
                    }
                    return null;
                },
                TimeSpan.FromSeconds(10));

            Assert.NotNull(overlayWindowResult?.Result);
            Thread.Sleep(300);

            // Drag a 300x200 selection rectangle on primary monitor
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

            // Send Enter to confirm selection in case adjustable mode was enabled
            Keyboard.Press(VirtualKeyShort.RETURN);
            Thread.Sleep(100);
            Keyboard.Release(VirtualKeyShort.RETURN);

            // 6. Wait for EditorWindow titled "Lightshot"
            var editorWindowResult = Retry.WhileNull(
                () => app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "Lightshot"),
                TimeSpan.FromSeconds(10));

            Assert.NotNull(editorWindowResult?.Result);
            var editorWindow = editorWindowResult.Result;
            editorWindow.Focus();
            Thread.Sleep(300);

            // 7. Select Arrow tool (click ArrowButton or press 'A')
            var arrowButton = editorWindow.FindFirstDescendant(cf => cf.ByAutomationId("ArrowButton"))?.AsButton();
            if (arrowButton != null)
            {
                arrowButton.Click();
            }
            else
            {
                Keyboard.Type('A');
            }
            Thread.Sleep(200);

            // 8. Draw an arrow mark across canvas
            var canvas = editorWindow.FindFirstDescendant(cf => cf.ByAutomationId("MainCanvasHost"));
            var canvasBounds = canvas != null ? canvas.BoundingRectangle : editorWindow.BoundingRectangle;

            int arrowStartX = (int)(canvasBounds.Left + canvasBounds.Width / 2 - 40);
            int arrowStartY = (int)(canvasBounds.Top + canvasBounds.Height / 2 - 20);
            int arrowEndX = arrowStartX + 80;
            int arrowEndY = arrowStartY + 40;

            Mouse.MoveTo(new System.Drawing.Point(arrowStartX, arrowStartY));
            Thread.Sleep(100);
            Mouse.Down(MouseButton.Left);
            Thread.Sleep(100);
            Mouse.MoveTo(new System.Drawing.Point(arrowEndX, arrowEndY));
            Thread.Sleep(100);
            Mouse.Up(MouseButton.Left);
            Thread.Sleep(300);

            // 9. Copy and Close (click DoneButton or press Ctrl+S)
            var doneButton = editorWindow.FindFirstDescendant(cf => cf.ByAutomationId("DoneButton"))?.AsButton();
            if (doneButton != null)
            {
                try
                {
                    doneButton.Invoke();
                }
                catch
                {
                    doneButton.Click();
                }
            }
            else
            {
                editorWindow.Focus();
                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
            }

            // Fallback: If still open after 1 second, send Ctrl+S
            Thread.Sleep(1000);
            if (app.GetAllTopLevelWindows(automation).Any(w => w.Title == "Lightshot"))
            {
                try
                {
                    editorWindow.Focus();
                    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
                }
                catch { }
            }

            // 10. Assert editor window closes
            var closeResult = Retry.WhileTrue(
                () => app.GetAllTopLevelWindows(automation).Any(w => w.Title == "Lightshot"),
                TimeSpan.FromSeconds(8));
            bool closed = closeResult?.Success ?? true;
            Assert.True(closed, "EditorWindow failed to close after Done / Ctrl+S.");

            // 11. Assert clipboard holds an image of the selected size (300x200)
            var imgDimensions = GetClipboardImageDimensions();
            Assert.NotNull(imgDimensions);
            Assert.Equal(selW, imgDimensions.Value.Width);
            Assert.Equal(selH, imgDimensions.Value.Height);

            // 12. Assert no new file was written
            var newDesktopFiles = Directory.GetFiles(desktopDir, "Screenshot*")
                .Where(f => File.GetCreationTimeUtc(f) >= startTime)
                .ToList();
            Assert.Empty(newDesktopFiles);

            if (Directory.Exists(picturesDir))
            {
                var newPicFiles = Directory.GetFiles(picturesDir, "Screenshot*", SearchOption.AllDirectories)
                    .Where(f => File.GetCreationTimeUtc(f) >= startTime)
                    .ToList();
                Assert.Empty(newPicFiles);
            }
        }
        finally
        {
            // Clean shutdown of app via quit event
            if (_app.SignalQuit())
            {
                process.WaitForExit(5000);
            }
            _app.KillStarted();
        }
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string lpszFormat);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(IntPtr hMem);

    private static void ClearClipboard()
    {
        for (int i = 0; i < 5; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    return;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(50);
        }
    }

    private static (int Width, int Height)? GetClipboardImageDimensions()
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    // 1. Try PNG format
                    uint pngFormat = RegisterClipboardFormatW("PNG");
                    if (pngFormat != 0)
                    {
                        IntPtr hPng = GetClipboardData(pngFormat);
                        if (hPng != IntPtr.Zero)
                        {
                            IntPtr ptr = GlobalLock(hPng);
                            if (ptr != IntPtr.Zero)
                            {
                                try
                                {
                                    nuint size = GlobalSize(hPng);
                                    if (size >= 24)
                                    {
                                        byte[] header = new byte[24];
                                        Marshal.Copy(ptr, header, 0, 24);
                                        int width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
                                        int height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
                                        if (width > 0 && height > 0)
                                        {
                                            return (width, height);
                                        }
                                    }
                                }
                                finally
                                {
                                    GlobalUnlock(hPng);
                                }
                            }
                        }
                    }

                    // 2. Try CF_DIBV5 (17)
                    IntPtr hDib = GetClipboardData(17);
                    if (hDib != IntPtr.Zero)
                    {
                        IntPtr ptr = GlobalLock(hDib);
                        if (ptr != IntPtr.Zero)
                        {
                            try
                            {
                                nuint size = GlobalSize(hDib);
                                if (size >= 16)
                                {
                                    byte[] header = new byte[16];
                                    Marshal.Copy(ptr, header, 0, 16);
                                    int width = BitConverter.ToInt32(header, 4);
                                    int height = Math.Abs(BitConverter.ToInt32(header, 8));
                                    if (width > 0 && height > 0)
                                    {
                                        return (width, height);
                                    }
                                }
                            }
                            finally
                            {
                                GlobalUnlock(hDib);
                            }
                        }
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(150);
        }
        return null;
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
