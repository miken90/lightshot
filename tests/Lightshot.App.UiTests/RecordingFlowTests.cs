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

public class RecordingFlowTests
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

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

    private static ProcessStartInfo CreateStartInfo(string exePath, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        psi.EnvironmentVariables["LIGHTSHOT_DISABLE_ONBOARDING"] = "1";
        psi.EnvironmentVariables["LIGHTSHOT_TEST_MODE"] = "1";

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

        return psi;
    }

    [Fact]
    [Desktop]
    public async Task StartPauseStopProducesPlayableFile()
    {
        CleanupPreviousProcesses();

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
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
            using var process = Process.Start(psi);
            Assert.NotNull(process);

            try
            {
                using var automation = new UIA3Automation();
                using var app = Application.Attach(process);

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
                    () =>
                    {
                        var windows = app.GetAllTopLevelWindows(automation);
                        foreach (var win in windows)
                        {
                            var btn = win.FindFirstDescendant(cf => cf.ByAutomationId("RecordVideoButton"));
                            if (btn != null) return btn;
                        }
                        return null;
                    },
                    TimeSpan.FromSeconds(10));

                Assert.NotNull(recordButtonResult?.Result);
                Thread.Sleep(300);
                recordButtonResult.Result.AsButton().Invoke();

                var wallStart = Stopwatch.StartNew();

                // 4. Wait for 3 s countdown + record ~2 s
                Thread.Sleep(5200);

                // 5. Pause recording (~2 s)
                var pauseButtonResult = Retry.WhileNull(
                    () =>
                    {
                        var windows = app.GetAllTopLevelWindows(automation);
                        foreach (var win in windows)
                        {
                            var btn = win.FindFirstDescendant(cf => cf.ByAutomationId("RecordingPauseResumeButton"));
                            if (btn != null) return btn;
                        }
                        return null;
                    },
                    TimeSpan.FromSeconds(10));

                Assert.NotNull(pauseButtonResult?.Result);
                pauseButtonResult.Result.AsButton().Invoke();

                var pauseWatch = Stopwatch.StartNew();
                Thread.Sleep(2000);
                pauseWatch.Stop();
                double measuredPause = pauseWatch.Elapsed.TotalSeconds;

                // 6. Resume recording (~2 s)
                pauseButtonResult.Result.AsButton().Invoke();
                Thread.Sleep(2000);

                // 7. Stop recording
                var stopButtonResult = Retry.WhileNull(
                    () =>
                    {
                        var windows = app.GetAllTopLevelWindows(automation);
                        foreach (var win in windows)
                        {
                            var btn = win.FindFirstDescendant(cf => cf.ByAutomationId("RecordingStopButton"));
                            if (btn != null) return btn;
                        }
                        return null;
                    },
                    TimeSpan.FromSeconds(5));

                Assert.NotNull(stopButtonResult?.Result);
                stopButtonResult.Result.AsButton().Invoke();
                wallStart.Stop();
                double measuredWall = wallStart.Elapsed.TotalSeconds - 3.0; // exclude countdown

                // 8. Click Save on post-recording overlay
                var saveButtonResult = Retry.WhileNull(
                    () =>
                    {
                        var windows = app.GetAllTopLevelWindows(automation);
                        foreach (var win in windows)
                        {
                            var btn = win.FindFirstDescendant(cf => cf.ByAutomationId("PostRecordingSaveButton"));
                            if (btn != null) return btn;
                        }
                        return null;
                    },
                    TimeSpan.FromSeconds(15));

                Assert.NotNull(saveButtonResult?.Result);
                Thread.Sleep(300);
                saveButtonResult.Result.AsButton().Invoke();

                // 9. Verify .mp4 in save.location
                var fileResult = Retry.WhileNull(
                    () => Directory.GetFiles(tempSaveDir, "*.mp4").FirstOrDefault(),
                    TimeSpan.FromSeconds(15));

                Assert.NotNull(fileResult?.Result);
                string deliveredFile = fileResult.Result;
                Assert.True(File.Exists(deliveredFile));
                Assert.True(Mp4Remuxer.HasMoovBeforeMdat(deliveredFile));

                var meta = await new MfMediaMetadata().VideoMetadataAsync(deliveredFile);
                Assert.NotNull(meta);
                Assert.True(meta.PixelWidth > 0 && meta.PixelWidth % 2 == 0);
                Assert.True(meta.PixelHeight > 0 && meta.PixelHeight % 2 == 0);

                double maxExpectedDuration = (measuredWall - measuredPause) + 1.5;
                Assert.InRange(meta.Duration, 2.5, maxExpectedDuration);
            }
            finally
            {
                SignalAppQuit();
                if (!process.WaitForExit(10000))
                {
                    try { process.Kill(); } catch { }
                }
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

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
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
            using var process = Process.Start(psi);
            Assert.NotNull(process);

            try
            {
                using var automation = new UIA3Automation();
                using var app = Application.Attach(process);

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
                    () =>
                    {
                        var windows = app.GetAllTopLevelWindows(automation);
                        foreach (var win in windows)
                        {
                            var btn = win.FindFirstDescendant(cf => cf.ByAutomationId("RecordVideoButton"));
                            if (btn != null) return btn;
                        }
                        return null;
                    },
                    TimeSpan.FromSeconds(10));

                Assert.NotNull(recordButtonResult?.Result);
                Thread.Sleep(300);
                recordButtonResult.Result.AsButton().Invoke();

                // 4. Wait for countdown (3s) + active recording (1s)
                Thread.Sleep(4500);

                // Assert DesktopCover exists while recording
                Assert.True(DoesDesktopCoverExist(process.Id), "LightshotDesktopCoverWindow must exist during recording with hideDesktopIcons=true");

                // 5. Click Discard button on ControlsPill
                var discardButtonResult = Retry.WhileNull(
                    () =>
                    {
                        var windows = app.GetAllTopLevelWindows(automation);
                        foreach (var win in windows)
                        {
                            var btn = win.FindFirstDescendant(cf => cf.ByAutomationId("RecordingDiscardButton"));
                            if (btn != null) return btn;
                        }
                        return null;
                    },
                    TimeSpan.FromSeconds(5));

                Assert.NotNull(discardButtonResult?.Result);
                discardButtonResult.Result.AsButton().Invoke();

                // 6. Confirm discard message box by pressing Enter
                Thread.Sleep(500);
                Keyboard.Press(VirtualKeyShort.RETURN);

                // 7. Verify cover window is removed within 5s
                var coverClosedResult = Retry.WhileTrue(
                    () => DoesDesktopCoverExist(process.Id),
                    TimeSpan.FromSeconds(5));

                Assert.True(coverClosedResult?.Success ?? true, "Desktop cover must be removed within 5s after discard");
            }
            finally
            {
                SignalAppQuit();
                if (!process.WaitForExit(10000))
                {
                    try { process.Kill(); } catch { }
                }
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
            var options = new RecordingOptions
            {
                Region = new CaptureRegion.RectRegion(new Lightshot.Core.Rect(0, 0, 800, 600)),
                Output = RecordingOutputKind.Video,
                HideDesktopIcons = true
            };

            var err = await svc.StartAsync(options, Path.Combine(tmp, "out.mp4"), _ => { });
            Assert.NotNull(err);
            Assert.IsType<RecordingError.SystemFailure>(err);

            int currentPid = Process.GetCurrentProcess().Id;
            Assert.False(DoesDesktopCoverExist(currentPid), "Desktop cover must not exist after engine startup failure");
        }
    }

    [Fact]
    [Desktop]
    public async Task LeftoverScratchRecordingIsDeliveredAtStartup()
    {
        CleanupPreviousProcesses();

        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appScratchDir = Path.Combine(localData, "Lightshot", "Lightshot Recordings");
        string backupDir = Path.Combine(Path.GetTempPath(), "Lightshot_Scratch_Backup_" + Guid.NewGuid().ToString("N"));

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
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
                var options = new RecordingOptions
                {
                    Region = new CaptureRegion.RectRegion(new Lightshot.Core.Rect(0, 0, 640, 480)),
                    Output = RecordingOutputKind.Video,
                    RecordComputerAudio = false,
                    RecordMicrophone = false
                };

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
            using var process = Process.Start(psi);
            Assert.NotNull(process);

            try
            {
                // 5. Within 20s, a new .mp4 appears in save.location and scratch file is gone
                var deliveredResult = Retry.WhileNull(
                    () => Directory.GetFiles(tempSaveDir, "*.mp4").FirstOrDefault(),
                    TimeSpan.FromSeconds(20));

                Assert.NotNull(deliveredResult?.Result);
                Assert.True(File.Exists(deliveredResult.Result));
                Assert.False(File.Exists(scratchFile), "Recovered file must be moved/removed from scratch folder");
            }
            finally
            {
                SignalAppQuit();
                if (!process.WaitForExit(10000))
                {
                    try { process.Kill(); } catch { }
                }
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
