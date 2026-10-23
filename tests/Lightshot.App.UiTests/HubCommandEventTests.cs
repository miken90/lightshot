using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Lightshot.Core;
using Lightshot.Platform.Windows.Recording;
using Lightshot.Platform.Windows.Windows;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.UiTests;

/// <summary>
/// Each hub command event must run its handler in a tray-less instance, on the UI thread: the handlers
/// open windows and dialogs, which only works through the dispatcher.
/// </summary>
public class HubCommandEventTests : IDisposable
{
    private const string EditorTitle = "Lightshot";
    private const string OverlayTitle = "LightshotOverlay";

    private readonly IsolatedApp _app = new();

    public void Dispose() => _app.Dispose();

    [Theory]
    [Desktop]
    [InlineData(HubCommands.FullscreenBaseName, EditorTitle)]
    [InlineData(HubCommands.WindowBaseName, OverlayTitle)]
    [InlineData(HubCommands.RepeatLastBaseName, EditorTitle)]
    [InlineData(HubCommands.HistoryBaseName, "History")]
    [InlineData(HubCommands.OpenImageBaseName, "Open Image")]
    public void Command_event_runs_its_handler_in_hub_mode(string baseName, string expectedTitle)
    {
        SeedSettings(saveDir: null);
        using var process = StartHubInstance();
        try
        {
            if (baseName == HubCommands.RepeatLastBaseName)
            {
                // Repeat needs a previous capture to repeat.
                SetEvent(HubCommands.FullscreenBaseName);
                Assert.True(WaitForWindow(process.Id, EditorTitle), "The setup fullscreen capture opened no editor.");
                CloseWindows(process.Id, EditorTitle);
                Assert.True(WaitFor(() => !HasWindow(process.Id, EditorTitle), TimeSpan.FromSeconds(5)), "The setup editor did not close.");
                // The setup capture flow finishes after its editor closes; a command set before that is ignored.
                Thread.Sleep(1500);
            }

            SetEvent(baseName);
            Assert.True(WaitForWindow(process.Id, expectedTitle), $"{baseName} opened no '{expectedTitle}' window within 5 s.");
            CloseWindows(process.Id, expectedTitle);
        }
        finally
        {
            Quit(process);
        }
    }

    [Fact]
    [Desktop]
    public void Record_event_starts_then_stops_a_recording()
    {
        string saveDir = Path.Combine(Path.GetTempPath(), "Lightshot_UiTest_Save_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(saveDir);
        SeedSettings(saveDir);
        using var process = StartHubInstance(testMode: true);
        try
        {
            using var automation = new UIA3Automation();
            using var app = Application.Attach(process.Id);

            SetEvent(HubCommands.RecordBaseName);
            Assert.True(WaitForWindow(process.Id, OverlayTitle, seconds: 15), "The record event opened no selection overlay.");

            // A second set while the selection is open (a double click in the hub) must not stack another overlay.
            // The overlay builds its windows one after another, so take the count once it stops growing.
            int overlays;
            do
            {
                overlays = WindowCount(process.Id, OverlayTitle);
                Thread.Sleep(700);
            } while (overlays != WindowCount(process.Id, OverlayTitle));
            SetEvent(HubCommands.RecordBaseName);
            Thread.Sleep(1500);
            Assert.Equal(overlays, WindowCount(process.Id, OverlayTitle));

            Thread.Sleep(500);
            Keyboard.Press(VirtualKeyShort.RETURN);

            var recordButton = Retry.WhileNull(() => FindById(app, automation, "RecordVideoButton"), TimeSpan.FromSeconds(10));
            Assert.NotNull(recordButton?.Result);
            Invoke(recordButton.Result);

            var stopButton = Retry.WhileNull(() => FindById(app, automation, "RecordingStopButton"), TimeSpan.FromSeconds(15));
            Assert.NotNull(stopButton?.Result);
            Thread.Sleep(2000);

            // The same event stops the recording; the post-recording overlay then offers the finished file.
            SetEvent(HubCommands.RecordBaseName);
            var save = Retry.WhileNull(() => FindById(app, automation, "PostRecordingSaveButton"), TimeSpan.FromSeconds(15));
            Assert.NotNull(save?.Result);
            Assert.Null(FindById(app, automation, "RecordingStopButton"));
            Thread.Sleep(300);
            Invoke(save.Result);

            var file = Retry.WhileNull(() => Directory.GetFiles(saveDir, "*.mp4").FirstOrDefault(), TimeSpan.FromSeconds(15));
            Assert.NotNull(file?.Result);
            Assert.True(Mp4Remuxer.HasMoovBeforeMdat(file.Result), "The recording was not finalised.");
        }
        finally
        {
            Quit(process);
            try { Directory.Delete(saveDir, true); } catch { }
        }
    }

    // Opens the editor on capture so Fullscreen and Repeat have a window to assert, and keeps the
    // recording test from hiding the real desktop's icons.
    private void SeedSettings(string? saveDir)
    {
        var root = new JsonObject
        {
            ["capture.openInEditor"] = true,
            ["app.hideDesktopIcons"] = false,
            ["recording.defaults"] = JsonSerializer.SerializeToNode(new RecordingDefaults
            {
                AfterRecording = AfterRecordingAction.ShowOverlay,
                ConfirmBeforeDiscard = true,
                CountdownEnabled = false,
                ShowRecordingControls = true,
                HideNotifications = false,
                RecordMicrophone = false,
                RecordComputerAudio = true
            })
        };
        if (saveDir != null) root["save.location"] = saveDir;
        _app.WriteSettings(root);
    }

    private Process StartHubInstance(bool testMode = false)
    {
        string exePath = FindAppExecutable();
        Assert.True(File.Exists(exePath), $"App executable not found at: {exePath}");
        var psi = _app.StartInfo(exePath, "--no-tray --no-onboarding");
        if (testMode)
        {
            psi.CreateNoWindow = false;
            psi.EnvironmentVariables["LIGHTSHOT_TEST_MODE"] = "1";
        }
        return _app.Start(psi);
    }

    // The app creates the events after its UI is up, so the first set waits for the event to exist.
    private void SetEvent(string baseName)
    {
        string name = AppInstance.ScopedName(baseName, _app.DataRoot);
        EventWaitHandle? handle = null;
        Assert.True(WaitFor(() =>
        {
            try { handle = EventWaitHandle.OpenExisting(name); return true; }
            catch (WaitHandleCannotBeOpenedException) { return false; }
        }, TimeSpan.FromSeconds(15)), $"Event {name} was never created.");
        using (handle) handle!.Set();
    }

    private void Quit(Process process)
    {
        if (_app.SignalQuit() && process.WaitForExit(5000)) return;
        _app.KillStarted();
    }

    private static void Invoke(AutomationElement element)
    {
        var button = element.AsButton();
        try { button.Invoke(); } catch { button.Click(); }
    }

    private static AutomationElement? FindById(Application app, UIA3Automation automation, string automationId)
    {
        foreach (var window in app.GetAllTopLevelWindows(automation))
        {
            try
            {
                var element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
                if (element != null) return element;
            }
            catch { }
        }
        return null;
    }

    private static bool WaitForWindow(int processId, string title, int seconds = 5) =>
        WaitFor(() => HasWindow(processId, title), TimeSpan.FromSeconds(seconds));

    private static bool HasWindow(int processId, string title) => WindowCount(processId, title) > 0;

    private static int WindowCount(int processId, string title) => Windows(processId).Count(w => w.Title == title);

    private static void CloseWindows(int processId, string title)
    {
        foreach (var window in Windows(processId).Where(w => w.Title == title))
        {
            PostMessageW(window.Handle, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
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

    private static List<(string Title, IntPtr Handle)> Windows(int processId)
    {
        var found = new List<(string, IntPtr)>();
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId && IsWindowVisible(hWnd))
            {
                var title = new StringBuilder(128);
                GetWindowTextW(hWnd, title, 128);
                found.Add((title.ToString(), hWnd));
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
