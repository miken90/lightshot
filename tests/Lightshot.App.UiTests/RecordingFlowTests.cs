// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Lightshot.Core;
using Lightshot.Platform.Windows.Media;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.UiTests;

public class RecordingFlowTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IsolatedApp _app = new();

    public RecordingFlowTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose() => _app.Dispose();

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private void CleanupPreviousProcesses() => _app.KillStarted();

    private void SignalAppQuit() => _app.SignalQuit();

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

    private static bool DoesDesktopCoverExist(int processId)
    {
        bool found = false;
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId && IsWindowVisible(hWnd))
            {
                var sb = new System.Text.StringBuilder(128);
                GetClassNameW(hWnd, sb, 128);
                if (sb.ToString() == "LightshotDesktopCoverWindow")
                {
                    found = true;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static AutomationElement? FindDescendantInApp(Application app, UIA3Automation automation, string automationId)
    {
        var windows = app.GetAllTopLevelWindows(automation);
        foreach (var win in windows)
        {
            try
            {
                var el = win.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
                if (el != null) return el;
            }
            catch { }
        }

        AutomationElement? found = null;
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)app.ProcessId && IsWindowVisible(hWnd))
            {
                try
                {
                    var win = automation.FromHandle(hWnd);
                    var el = win?.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
                    if (el != null)
                    {
                        found = el;
                        return false;
                    }
                }
                catch { }
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static IntPtr FindTopLevelWindowByClass(int processId, string className)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId && IsWindowVisible(hWnd))
            {
                var sb = new System.Text.StringBuilder(128);
                GetClassNameW(hWnd, sb, 128);
                if (sb.ToString() == className)
                {
                    found = hWnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // Failure context for the trx: which app windows exist, which one has the foreground, and a screenshot.
    private string DescribeApp(Process process, string tag)
    {
        var sb = new System.Text.StringBuilder();
        bool exited = process.HasExited;
        sb.Append($"[{tag}] app exited={exited}");
        if (exited) sb.Append($" exitCode={process.ExitCode}");
        IntPtr fg = GetForegroundWindow();
        GetWindowThreadProcessId(fg, out uint fgPid);
        sb.Append($"; foreground pid={fgPid}");
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)process.Id && IsWindowVisible(hWnd))
            {
                var cls = new System.Text.StringBuilder(128);
                var title = new System.Text.StringBuilder(128);
                GetClassNameW(hWnd, cls, 128);
                GetWindowTextW(hWnd, title, 128);
                sb.Append($"; window {cls} '{title}'{(hWnd == fg ? " (foreground)" : string.Empty)}");
            }
            return true;
        }, IntPtr.Zero);

        try
        {
            string shot = Path.Combine(Path.GetTempPath(), $"Lightshot_UiDiag_{tag}_{DateTime.Now:HHmmss}.png");
            Capture.Screen().ToFile(shot);
            sb.Append($"; screenshot {shot}");
        }
        catch (Exception ex)
        {
            sb.Append($"; screenshot failed: {ex.Message}");
        }

        string text = sb.ToString();
        _output.WriteLine(text);
        return text;
    }

    private static string ListFiles(string dir) =>
        Directory.Exists(dir) ? $"[{string.Join(", ", Directory.GetFiles(dir).Select(Path.GetFileName))}]" : "(missing)";

    private static void TriggerRecordButton(Application app, UIA3Automation automation, AutomationElement recordButtonElement)
    {
        var toolbarWin = app.GetAllTopLevelWindows(automation)
            .FirstOrDefault(w => w.Title == "Recording Toolbar");

        if (toolbarWin == null)
        {
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == (uint)app.ProcessId && IsWindowVisible(hWnd))
                {
                    var sb = new System.Text.StringBuilder(128);
                    GetWindowTextW(hWnd, sb, 128);
                    if (sb.ToString() == "Recording Toolbar")
                    {
                        try { toolbarWin = automation.FromHandle(hWnd)?.AsWindow(); } catch { }
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
        }

        toolbarWin?.Focus();
        Thread.Sleep(200);

        var btn = recordButtonElement.AsButton();
        try { btn.Invoke(); } catch { btn.Click(); }

        // The toolbar closes once it hands the choice to the coordinator.
        var closed = Retry.WhileTrue(
            () => app.GetAllTopLevelWindows(automation).Any(w => w.Title == "Recording Toolbar"),
            TimeSpan.FromSeconds(5));
        Assert.True(closed?.Success ?? false, "Recording toolbar must close after Record is invoked");
    }

    // Screen readers and remote-control tools find the pill buttons by name, not by glyph.
    private static void AssertPillButtonNames(Application app, UIA3Automation automation)
    {
        foreach (var (id, name) in new[]
        {
            ("RecordingPauseResumeButton", "Pause Recording"),
            ("RecordingStopButton", "Stop Recording"),
            ("RecordingRestartButton", "Restart Recording"),
            ("RecordingDiscardButton", "Discard Recording")
        })
        {
            var button = FindDescendantInApp(app, automation, id);
            Assert.True(button != null, $"{id} must be on the controls pill");
            Assert.Equal(name, button!.Name);
        }
    }

    private static string[] VisibleWindows(int processId)
    {
        var windows = new System.Collections.Generic.List<string>();
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId && IsWindowVisible(hWnd))
            {
                var cls = new System.Text.StringBuilder(128);
                var title = new System.Text.StringBuilder(128);
                GetClassNameW(hWnd, cls, 128);
                GetWindowTextW(hWnd, title, 128);
                windows.Add($"{cls} '{title}'");
            }
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }

    private ProcessStartInfo CreateStartInfo(string exePath, string args)
    {
        var psi = _app.StartInfo(exePath, args);
        psi.CreateNoWindow = false;
        psi.EnvironmentVariables["LIGHTSHOT_TEST_MODE"] = "1";
        return psi;
    }

    [Fact]
    [Desktop]
    public async Task StartPauseStopProducesPlayableFile()
    {
        CleanupPreviousProcesses();

        string appData = Path.Combine(_app.DataRoot, "Roaming");
        string settingsDir = Path.Combine(appData, "Lightshot");
        string settingsPath = Path.Combine(settingsDir, "settings.json");
        byte[]? originalSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;

        string tempSaveDir = Path.Combine(Path.GetTempPath(), "Lightshot_UiTest_Save_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSaveDir);

        try
        {
            Directory.CreateDirectory(settingsDir);
            var root = new JsonObject();
            if (originalSettings != null)
            {
                try { root = JsonNode.Parse(originalSettings)?.AsObject() ?? new JsonObject(); } catch { }
            }
            root["save.location"] = tempSaveDir;
            root["app.hideDesktopIcons"] = false;
            root["recording.defaults"] = JsonSerializer.SerializeToNode(new RecordingDefaults
            {
                AfterRecording = AfterRecordingAction.ShowOverlay,
                ConfirmBeforeDiscard = true,
                CountdownEnabled = true,
                CountdownSeconds = 3,
                ShowRecordingControls = true,
                HideNotifications = false,
                RecordMicrophone = false,
                RecordComputerAudio = true
            });
            File.WriteAllText(settingsPath, root.ToJsonString());

            string exePath = FindAppExecutable();
            Assert.True(File.Exists(exePath), $"App executable not found at: {exePath}");

            var psi = CreateStartInfo(exePath, "--record-screen");
            using var process = _app.Start(psi);
            Assert.NotNull(process);

            try
            {
                using var automation = new UIA3Automation();
                using var app = Application.Attach(process.Id);

                // 1. Wait for overlay window to appear
                var overlayResult = Retry.WhileNull(
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
                    TimeSpan.FromSeconds(15));

                Assert.NotNull(overlayResult?.Result);
                Thread.Sleep(500);

                // 2. Press Enter to confirm region
                Keyboard.Press(VirtualKeyShort.RETURN);

                // 3. Wait for toolbar and click RecordVideoButton
                var recordButtonResult = Retry.WhileNull(
                    () => FindDescendantInApp(app, automation, "RecordVideoButton"),
                    TimeSpan.FromSeconds(10));

                Assert.NotNull(recordButtonResult?.Result);
                TriggerRecordButton(app, automation, recordButtonResult.Result);

                var wallStart = Stopwatch.StartNew();

                // 4. Wait for countdown (3s) + active recording (1s) and locate pause button
                // 5. Pause recording (~2 s)
                var pauseButtonResult = Retry.WhileNull(
                    () => FindDescendantInApp(app, automation, "RecordingPauseResumeButton"),
                    TimeSpan.FromSeconds(15));

                Assert.NotNull(pauseButtonResult?.Result);
                var pauseBtn = pauseButtonResult.Result.AsButton();
                AssertPillButtonNames(app, automation);
                Thread.Sleep(2000);
                try { pauseBtn.Invoke(); } catch { pauseBtn.Click(); }

                var pauseWatch = Stopwatch.StartNew();
                Thread.Sleep(2000);
                pauseWatch.Stop();
                double measuredPause = pauseWatch.Elapsed.TotalSeconds;

                // 6. Resume recording (~2 s)
                try { pauseBtn.Invoke(); } catch { pauseBtn.Click(); }
                Thread.Sleep(2000);

                // 7. Stop recording
                var stopButtonResult = Retry.WhileNull(
                    () => FindDescendantInApp(app, automation, "RecordingStopButton"),
                    TimeSpan.FromSeconds(10));

                Assert.NotNull(stopButtonResult?.Result);
                var stopBtn = stopButtonResult.Result.AsButton();
                try { stopBtn.Invoke(); } catch { stopBtn.Click(); }
                wallStart.Stop();
                double measuredWall = wallStart.Elapsed.TotalSeconds - 3.0; // exclude countdown

                // 8. Click Save on post-recording overlay
                var saveButtonResult = Retry.WhileNull(
                    () => FindDescendantInApp(app, automation, "PostRecordingSaveButton"),
                    TimeSpan.FromSeconds(15));

                Assert.NotNull(saveButtonResult?.Result);
                var saveBtn = saveButtonResult.Result.AsButton();
                Thread.Sleep(300);
                try { saveBtn.Invoke(); } catch { saveBtn.Click(); }

                // 9. Verify .mp4 in save.location
                var fileResult = Retry.WhileNull(
                    () => Directory.GetFiles(tempSaveDir, "*.mp4").FirstOrDefault(),
                    TimeSpan.FromSeconds(15));

                Assert.NotNull(fileResult?.Result);
                string deliveredFile = fileResult.Result;
                Assert.True(File.Exists(deliveredFile));
                Assert.True(Mp4Remuxer.HasMoovBeforeMdat(deliveredFile));

                var metaNullable = await new MfMediaMetadata().VideoMetadataAsync(deliveredFile);
                Assert.NotNull(metaNullable);
                var meta = metaNullable.Value;
                Assert.True(meta.PixelWidth > 0 && meta.PixelWidth % 2 == 0);
                Assert.True(meta.PixelHeight > 0 && meta.PixelHeight % 2 == 0);

                double maxExpectedDuration = (measuredWall - measuredPause) + 1.5;
                Assert.InRange(meta.Duration, 2.5, maxExpectedDuration);
            }
            finally
            {
                SignalAppQuit();
                try
                {
                    if (!process.WaitForExit(10000))
                    {
                        try { process.Kill(); } catch { }
                    }
                }
                catch { }
            }
        }
        finally
        {
            CleanupPreviousProcesses();
            try
            {
                if (originalSettings != null) File.WriteAllBytes(settingsPath, originalSettings);
                else if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
            catch { }

            try { Directory.Delete(tempSaveDir, true); } catch { }
        }
    }

    [Fact]
    [Desktop]
    public async Task CoverRemovedOnCancelAndFailure()
    {
        CleanupPreviousProcesses();

        string appData = Path.Combine(_app.DataRoot, "Roaming");
        string settingsDir = Path.Combine(appData, "Lightshot");
        string settingsPath = Path.Combine(settingsDir, "settings.json");
        byte[]? originalSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;

        string tempSaveDir = Path.Combine(Path.GetTempPath(), "Lightshot_CoverTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSaveDir);

        try
        {
            Directory.CreateDirectory(settingsDir);
            var root = new JsonObject();
            if (originalSettings != null)
            {
                try { root = JsonNode.Parse(originalSettings)?.AsObject() ?? new JsonObject(); } catch { }
            }
            root["save.location"] = tempSaveDir;
            root["app.hideDesktopIcons"] = true;
            root["recording.defaults"] = JsonSerializer.SerializeToNode(new RecordingDefaults
            {
                AfterRecording = AfterRecordingAction.ShowOverlay,
                ConfirmBeforeDiscard = true,
                CountdownEnabled = true,
                CountdownSeconds = 3,
                ShowRecordingControls = true,
                HideNotifications = false,
                RecordMicrophone = false,
                RecordComputerAudio = true
            });
            File.WriteAllText(settingsPath, root.ToJsonString());

            string exePath = FindAppExecutable();
            Assert.True(File.Exists(exePath), $"App executable not found at: {exePath}");

            var psi = CreateStartInfo(exePath, "--record-screen");
            using var process = _app.Start(psi);
            Assert.NotNull(process);

            try
            {
                using var automation = new UIA3Automation();
                using var app = Application.Attach(process.Id);

                // 1. Wait for overlay window to appear
                var overlayResult = Retry.WhileNull(
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
                    TimeSpan.FromSeconds(15));

                Assert.NotNull(overlayResult?.Result);
                Thread.Sleep(500);

                // 2. Press Enter to confirm region
                Keyboard.Press(VirtualKeyShort.RETURN);

                // 3. Wait for toolbar and click RecordVideoButton
                var recordButtonResult = Retry.WhileNull(
                    () => FindDescendantInApp(app, automation, "RecordVideoButton"),
                    TimeSpan.FromSeconds(10));

                Assert.NotNull(recordButtonResult?.Result);
                TriggerRecordButton(app, automation, recordButtonResult.Result);

                // 4. Wait for countdown (3s) and verify DesktopCover appears while recording (up to 12s)
                var coverAppeared = Retry.WhileFalse(
                    () => DoesDesktopCoverExist(process.Id),
                    TimeSpan.FromSeconds(12));
                Assert.True(coverAppeared?.Success ?? false, "LightshotDesktopCoverWindow must exist during recording with hideDesktopIcons=true");

                // 5. Click Discard button on ControlsPill
                var discardButtonResult = Retry.WhileNull(
                    () => FindDescendantInApp(app, automation, "RecordingDiscardButton"),
                    TimeSpan.FromSeconds(10));

                Assert.NotNull(discardButtonResult?.Result);
                var discardBtn = discardButtonResult.Result.AsButton();
                try { discardBtn.Invoke(); } catch { discardBtn.Click(); }

                // 6. Confirm the discard message box (ConfirmBeforeDiscard=true) with OK.
                // A key press goes to the foreground window, and a process that only received UIA calls
                // may not own the foreground, so press the dialog's own OK button (IDOK = "1").
                var dialogResult = Retry.WhileNull(
                    () =>
                    {
                        IntPtr hwnd = FindTopLevelWindowByClass(process.Id, "#32770");
                        return hwnd == IntPtr.Zero ? null : (IntPtr?)hwnd;
                    },
                    TimeSpan.FromSeconds(5));
                if (dialogResult?.Result == null) Assert.Fail("Discard confirmation must appear: " + DescribeApp(process, "discard-dialog"));
                IntPtr dialogHwnd = dialogResult!.Result!.Value;
                _output.WriteLine($"Discard dialog foreground={GetForegroundWindow() == dialogHwnd}");
                var okButton = automation.FromHandle(dialogHwnd).FindFirstDescendant(cf => cf.ByAutomationId("1"));
                Assert.NotNull(okButton);
                okButton.AsButton().Invoke();

                // 7. Verify cover window is removed within 5s
                var coverClosedResult = Retry.WhileTrue(
                    () => DoesDesktopCoverExist(process.Id),
                    TimeSpan.FromSeconds(5));

                if (!(coverClosedResult?.Success ?? true)) Assert.Fail("Desktop cover must be removed within 5s after discard: " + DescribeApp(process, "cover"));
            }
            finally
            {
                SignalAppQuit();
                try
                {
                    if (!process.WaitForExit(10000))
                    {
                        try { process.Kill(); } catch { }
                    }
                }
                catch { }
            }
        }
        finally
        {
            CleanupPreviousProcesses();
            try
            {
                if (originalSettings != null) File.WriteAllBytes(settingsPath, originalSettings);
                else if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
            catch { }

            try { Directory.Delete(tempSaveDir, true); } catch { }
        }

        // Part (b): In-process failure path
        string tmp = Path.Combine(Path.GetTempPath(), "Lightshot_FailTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var store = new ScratchStore(tmp);
        Directory.Delete(tmp, true);

        using (var svc = new WindowsRecordingService(new RecordingEngine(scratchStore: store)))
        {
            var options = new RecordingOptions(
                new CaptureRegion.RectRegion(new Lightshot.Core.Rect(0, 0, 800, 600)),
                new RecordingOutput.Video(VideoSettings.Standard),
                hideDesktopIcons: true);

            var err = await svc.StartAsync(options, Path.Combine(tmp, "out.mp4"), _ => { });
            Assert.NotNull(err);
            Assert.IsType<RecordingError.SystemFailure>(err);

            int currentPid = Process.GetCurrentProcess().Id;
            Assert.False(DoesDesktopCoverExist(currentPid), "Desktop cover must not exist after engine startup failure");
        }
    }

    [Fact]
    [Desktop]
    public void SelectionToolbarCloseButtonCancelsTheFlowLikeEsc()
    {
        string tempSaveDir = Path.Combine(Path.GetTempPath(), "Lightshot_CloseTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSaveDir);

        try
        {
            var root = _app.ReadSettings();
            root["save.location"] = tempSaveDir;
            // Covers on, so the test also proves the cancel leaves no desktop cover behind.
            root["app.hideDesktopIcons"] = true;
            root["recording.defaults"] = JsonSerializer.SerializeToNode(new RecordingDefaults
            {
                AfterRecording = AfterRecordingAction.ShowOverlay,
                CountdownEnabled = true,
                CountdownSeconds = 3,
                ShowRecordingControls = true,
                RecordMicrophone = false,
                RecordComputerAudio = false
            });
            _app.WriteSettings(root);

            string exePath = FindAppExecutable();
            Assert.True(File.Exists(exePath), $"App executable not found at: {exePath}");

            using var process = _app.Start(CreateStartInfo(exePath, "--record-screen"));
            using var automation = new UIA3Automation();
            using var app = Application.Attach(process.Id);

            var overlay = Retry.WhileTrue(() => FindOverlayHwnd(process.Id) == IntPtr.Zero, TimeSpan.FromSeconds(15));
            if (!(overlay?.Success ?? false)) Assert.Fail("Recording overlay must appear: " + DescribeApp(process, "close-overlay"));
            Thread.Sleep(500);
            Keyboard.Press(VirtualKeyShort.RETURN);

            var closeResult = Retry.WhileNull(
                () => FindDescendantInApp(app, automation, "RecordingToolbarCloseButton"),
                TimeSpan.FromSeconds(10));
            if (closeResult?.Result == null) Assert.Fail("Recording toolbar must show a close button: " + DescribeApp(process, "close-toolbar"));
            Assert.Equal("Cancel Recording", closeResult!.Result!.Name);

            var closeBtn = closeResult.Result.AsButton();
            try { closeBtn.Invoke(); } catch { closeBtn.Click(); }

            // Esc's end state: toolbar, frame, overlay and covers all gone, while the app stays running.
            var gone = Retry.WhileTrue(() => VisibleWindows(process.Id).Length > 0, TimeSpan.FromSeconds(5));
            if (!(gone?.Success ?? false)) Assert.Fail("Every recording window must close after the close button: " + DescribeApp(process, "close-windows"));

            // Outlast the 3 s countdown: a take that started anyway would show the pill or a cover by now.
            Thread.Sleep(5000);
            Assert.False(process.HasExited, "The app must return to idle, not exit");
            Assert.Empty(VisibleWindows(process.Id));
            Assert.False(DoesDesktopCoverExist(process.Id), "No desktop cover may remain after cancel");
            Assert.Null(FindDescendantInApp(app, automation, "RecordingStopButton"));

            string scratchDir = Path.Combine(_app.LocalData, "Recordings");
            Assert.True(Directory.GetFiles(tempSaveDir).Length == 0, "Cancel must not save a file: " + ListFiles(tempSaveDir));
            Assert.True(!Directory.Exists(scratchDir) || Directory.GetFiles(scratchDir).Length == 0, "Cancel must not leave a take: " + ListFiles(scratchDir));
        }
        finally
        {
            if (_app.SignalQuit()) Thread.Sleep(500);
            _app.KillStarted();
            try { Directory.Delete(tempSaveDir, true); } catch { }
        }
    }

    [Fact]
    [Desktop]
    public async Task LeftoverScratchRecordingIsDeliveredAtStartup()
    {
        CleanupPreviousProcesses();

        string localData = Path.Combine(_app.DataRoot, "Local");
        string appScratchDir = Path.Combine(localData, "Lightshot", "Lightshot Recordings");
        string backupDir = Path.Combine(Path.GetTempPath(), "Lightshot_Scratch_Backup_" + Guid.NewGuid().ToString("N"));

        string appData = Path.Combine(_app.DataRoot, "Roaming");
        string settingsDir = Path.Combine(appData, "Lightshot");
        string settingsPath = Path.Combine(settingsDir, "settings.json");
        byte[]? originalSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;

        string tempSaveDir = Path.Combine(Path.GetTempPath(), "Lightshot_StartupDeliver_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSaveDir);
        Directory.CreateDirectory(backupDir);

        string scratchFile = string.Empty;

        try
        {
            // 1. Move any existing scratch files to backup
            if (Directory.Exists(appScratchDir))
            {
                foreach (var file in Directory.GetFiles(appScratchDir))
                {
                    string dest = Path.Combine(backupDir, Path.GetFileName(file));
                    File.Move(file, dest);
                }
            }
            else
            {
                Directory.CreateDirectory(appScratchDir);
            }

            // 2. Make a real 2 s MP4 in-process
            string testTemp = Path.Combine(Path.GetTempPath(), "Lightshot_RecoverGen_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testTemp);
            string genOutput = Path.Combine(testTemp, "temp_take.mp4");

            using (var svc = new WindowsRecordingService(new RecordingEngine(scratchStore: new ScratchStore(testTemp))))
            {
                var options = new RecordingOptions(
                    new CaptureRegion.RectRegion(new Lightshot.Core.Rect(0, 0, 640, 480)),
                    new RecordingOutput.Video(VideoSettings.Standard),
                    computerAudio: false,
                    microphone: InputDeviceSelection.Off.Instance);

                var startErr = await svc.StartAsync(options, genOutput, _ => { });
                Assert.Null(startErr);
                Thread.Sleep(2000);
                var (_, stopErr) = await svc.StopAsync();
                Assert.Null(stopErr);
            }

            // Move the generated file to scratch
            scratchFile = Path.Combine(appScratchDir, $"Recording-{Guid.NewGuid():N}.mp4");
            File.Move(genOutput, scratchFile);

            // 3. Configure settings to point to tempSaveDir
            Directory.CreateDirectory(settingsDir);
            var root = new JsonObject();
            if (originalSettings != null)
            {
                try { root = JsonNode.Parse(originalSettings)?.AsObject() ?? new JsonObject(); } catch { }
            }
            root["save.location"] = tempSaveDir;
            File.WriteAllText(settingsPath, root.ToJsonString());

            // 4. Launch app with no arguments
            string exePath = FindAppExecutable();
            Assert.True(File.Exists(exePath), $"App executable not found at: {exePath}");

            var psi = CreateStartInfo(exePath, string.Empty);
            using var process = _app.Start(psi);
            Assert.NotNull(process);

            try
            {
                // 5. Within 20s, a new .mp4 appears in save.location and scratch file is gone
                var deliveredResult = Retry.WhileNull(
                    () => Directory.GetFiles(tempSaveDir, "*.mp4").FirstOrDefault(),
                    TimeSpan.FromSeconds(20));

                if (deliveredResult?.Result == null)
                {
                    Assert.Fail($"Leftover recording must be delivered within 20s: scratch={ListFiles(appScratchDir)} save={ListFiles(tempSaveDir)} " +
                        DescribeApp(process, "leftover"));
                }
                Assert.True(File.Exists(deliveredResult!.Result));
                Assert.False(File.Exists(scratchFile), "Recovered file must be moved/removed from scratch folder");
            }
            finally
            {
                SignalAppQuit();
                try
                {
                    if (!process.WaitForExit(10000))
                    {
                        try { process.Kill(); } catch { }
                    }
                }
                catch { }
            }
        }
        finally
        {
            CleanupPreviousProcesses();

            try
            {
                if (originalSettings != null) File.WriteAllBytes(settingsPath, originalSettings);
                else if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
            catch { }

            // Restore scratch files from backup
            if (Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(appScratchDir);
                foreach (var file in Directory.GetFiles(backupDir))
                {
                    try
                    {
                        string dest = Path.Combine(appScratchDir, Path.GetFileName(file));
                        if (!File.Exists(dest)) File.Move(file, dest);
                    }
                    catch { }
                }
                try { Directory.Delete(backupDir, true); } catch { }
            }

            try { Directory.Delete(tempSaveDir, true); } catch { }
        }
    }
}
