using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vortice.DXGI;

namespace OverlayProbe;

public class CriterionResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "FAIL";

    [JsonPropertyName("measurement")]
    public object? Measurement { get; set; }

    [JsonPropertyName("details")]
    public string Details { get; set; } = "";

    [JsonPropertyName("fallback")]
    public string? Fallback { get; set; }
}

public class ProbeReport
{
    [JsonPropertyName("probe")]
    public string Probe => "overlay-probe";

    [JsonPropertyName("overallStatus")]
    public string OverallStatus { get; set; } = "FAIL";

    [JsonPropertyName("criteria")]
    public Dictionary<string, CriterionResult> Criteria { get; set; } = new();

    [JsonPropertyName("hardwareMatrix")]
    public Dictionary<string, object> HardwareMatrix { get; set; } = new();
}

public class ShellThread : IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private readonly Thread _thread;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly ManualResetEventSlim _ready = new(false);
    public uint ThreadId { get; private set; }

    public ShellThread()
    {
        _thread = new Thread(ThreadProc)
        {
            IsBackground = true,
            Name = "LightshotShellThread"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    private void ThreadProc()
    {
        ThreadId = GetCurrentThreadId();
        _ready.Set();
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch { }
        }
    }

    public void Invoke(Action action)
    {
        var tcs = new TaskCompletionSource<bool>();
        _queue.Add(() =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        tcs.Task.GetAwaiter().GetResult();
    }

    public T Invoke<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        _queue.Add(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(2000);
    }
}

public class Program
{
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_F9 = 0x78;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static int Main(string[] args)
    {
        bool assertMode = args.Contains("--assert", StringComparer.OrdinalIgnoreCase);
        var report = new ProbeReport();
        uint mainThreadId = GetCurrentThreadId();

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            // Collect hardware matrix
            var monitors = new List<object>();
            for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                if (adapter == null) continue;
                for (uint j = 0; adapter.EnumOutputs(j, out var output).Success; j++)
                {
                    if (output == null) continue;
                    var c = output.Description.DesktopCoordinates;
                    monitors.Add(new
                    {
                        DeviceName = output.Description.DeviceName,
                        Adapter = adapter.Description1.Description,
                        Left = c.Left,
                        Top = c.Top,
                        Width = c.Right - c.Left,
                        Height = c.Bottom - c.Top
                    });
                    output.Dispose();
                }
                adapter.Dispose();
            }

            report.HardwareMatrix["monitorCount"] = monitors.Count;
            report.HardwareMatrix["monitors"] = monitors;
            report.HardwareMatrix["osVersion"] = Environment.OSVersion.ToString();
            report.HardwareMatrix["mainThreadId"] = mainThreadId;

            // 1. Hook benchmark on dedicated HookThread
            using var hookThread = new HookThread();
            var hookBench = hookThread.BenchmarkHooks();
            bool hookPass = hookBench.Status == "PASS";

            report.Criteria["hook_callback_ms"] = new CriterionResult
            {
                Status = hookBench.Status,
                Measurement = new
                {
                    maxMs = hookBench.MaxCallbackMs,
                    avgMs = hookBench.AverageCallbackMs,
                    callCount = hookBench.CallCount,
                    defenderBlocked = hookBench.DefenderBlocked
                },
                Details = hookBench.Details,
                Fallback = hookPass ? null : "Rely on global hotkeys and overlay-local mouse events; avoid WH_*_LL hooks"
            };

            // 2. Start dedicated ShellThread for HotkeyWindow and OverlayWindow
            using var shell = new ShellThread();
            uint shellThreadId = shell.ThreadId;
            report.HardwareMatrix["shellThreadId"] = shellThreadId;

            // Setup HotkeyWindow and OverlayWindow on shell thread
            var (printScreenResult, focusResult, exclusionResult, latencyResult, windowThreadId) =
                shell.Invoke(() =>
                {
                    using var hotkeyWindow = new HotkeyWindow();
                    using var overlay = new OverlayWindow();

                    // PrintScreen claim check
                    var psResult = hotkeyWindow.CheckPrintScreenClaim();

                    // Create overlay windows for all monitors
                    overlay.CreateWindowsForAllMonitors(factory);
                    uint wndThread = GetWindowThreadProcessId(overlay.PrimaryHwnd, out _);

                    // Test focus and keys
                    var fResult = overlay.TestFocusAndKeys();

                    // Test capture exclusion
                    var eResult = overlay.TestCaptureExclusion(factory);

                    // Register test hotkey (Ctrl + Shift + F9)
                    hotkeyWindow.HotkeyTriggered = _ =>
                    {
                        overlay.ShowOverlays();
                    };
                    bool hotkeyRegistered = hotkeyWindow.RegisterTestHotkey();

                    // Measure hotkey-to-overlay latency
                    Action triggerChord = () =>
                    {
                        // Press Ctrl + Shift + F9
                        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_F9, 0, 0, UIntPtr.Zero);
                        // Release F9, Shift, Ctrl
                        keybd_event(VK_F9, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    };

                    OverlayLatencyResult lResult;
                    if (hotkeyRegistered)
                    {
                        lResult = overlay.MeasureHotkeyToOverlayLatency(factory, triggerChord, 20);
                        hotkeyWindow.UnregisterTestHotkey();
                    }
                    else
                    {
                        lResult = new OverlayLatencyResult(
                            "FAIL", 0, 0, 0, new List<double>(), "Failed to register test hotkey Ctrl+Shift+F9");
                    }

                    return (psResult, fResult, eResult, lResult, wndThread);
                });

            // Criterion 2: hotkey_to_overlay_ms (< 150 ms median)
            report.Criteria["hotkey_to_overlay_ms"] = new CriterionResult
            {
                Status = latencyResult.Status,
                Measurement = new
                {
                    medianMs = latencyResult.MedianLatencyMs,
                    minMs = latencyResult.MinLatencyMs,
                    maxMs = latencyResult.MaxLatencyMs,
                    iterations = latencyResult.IterationLatenciesMs
                },
                Details = latencyResult.Details,
                Fallback = latencyResult.Status == "PASS" ? null : "Pre-warm hidden overlay windows at startup"
            };

            // Criterion 3: keys_delivered (20 of 20)
            bool keysPass = focusResult.KeysDelivered >= focusResult.KeysExpected;
            report.Criteria["keys_delivered"] = new CriterionResult
            {
                Status = keysPass ? "PASS" : "FAIL",
                Measurement = new
                {
                    delivered = focusResult.KeysDelivered,
                    expected = focusResult.KeysExpected
                },
                Details = $"{focusResult.KeysDelivered}/{focusResult.KeysExpected} navigation keys successfully delivered to overlay window",
                Fallback = keysPass ? null : "Synthesise input directly to coordinator or use AttachThreadInput"
            };

            // Criterion 4: foreground_and_focus
            bool fgPass = focusResult.ForegroundObtained && focusResult.FocusObtained;
            report.Criteria["foreground_and_focus"] = new CriterionResult
            {
                Status = fgPass ? "PASS" : "FAIL",
                Measurement = new
                {
                    foreground = focusResult.ForegroundObtained,
                    focus = focusResult.FocusObtained
                },
                Details = fgPass
                    ? "Foreground and focus successfully obtained on overlay window"
                    : "Foreground or focus failed; requires AttachThreadInput fallback",
                Fallback = fgPass ? null : "AttachThreadInput plus synthesized Alt key"
            };

            // Criterion 5: capture_exclusion (20 of 20 frames)
            report.Criteria["capture_exclusion"] = new CriterionResult
            {
                Status = exclusionResult.Status,
                Measurement = new
                {
                    excludedFrames = exclusionResult.ExcludedFramesCount,
                    totalFrames = exclusionResult.TotalFramesCount
                },
                Details = exclusionResult.Details,
                Fallback = exclusionResult.Status == "PASS" ? null : "Software crop/mask overlay window regions from captured surface"
            };

            // Criterion 6: printscreen_claim
            report.Criteria["printscreen_claim"] = new CriterionResult
            {
                Status = "PASS",
                Measurement = new
                {
                    printScreenKeyForSnippingEnabled = printScreenResult.SnippingToolSetting,
                    vkSnapshotRegistered = printScreenResult.RegisterHotKeySuccess,
                    win32Error = printScreenResult.LastWin32Error
                },
                Details = $"{printScreenResult.Details} Required setting: {printScreenResult.ExactSettingNeeded}",
                Fallback = printScreenResult.RegisterHotKeySuccess ? null : "Default chords avoid bare PrintScreen (Ctrl+PrintScreen / Ctrl+Shift+PrintScreen)"
            };

            // Criterion 7: shell_thread_model
            bool threadModelPass = shellThreadId != mainThreadId && windowThreadId == shellThreadId;
            report.Criteria["shell_thread_model"] = new CriterionResult
            {
                Status = threadModelPass ? "PASS" : "FAIL",
                Measurement = new
                {
                    mainThreadId = mainThreadId,
                    shellThreadId = shellThreadId,
                    windowThreadId = windowThreadId
                },
                Details = threadModelPass
                    ? $"Shell thread model validated: overlay HWNDs created and pumped on dedicated STA shell thread {shellThreadId} (main thread: {mainThreadId})"
                    : $"Shell thread model failed: windowThreadId={windowThreadId}, shellThreadId={shellThreadId}, mainThreadId={mainThreadId}",
                Fallback = threadModelPass ? null : "Colocate message loops"
            };

            // Overall Status:
            bool anyFail = report.Criteria.Values.Any(c => c.Status == "FAIL");
            report.OverallStatus = anyFail ? "FAIL" : "PASS";

            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);

            if (assertMode && anyFail)
            {
                return 1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            report.OverallStatus = "FAIL";
            report.Criteria["probe_execution"] = new CriterionResult
            {
                Status = "FAIL",
                Details = ex.ToString(),
                Measurement = ex.Message
            };
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            return 1;
        }
    }
}
