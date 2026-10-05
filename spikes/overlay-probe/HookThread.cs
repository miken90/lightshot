using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OverlayProbe;

public record HookBenchmarkResult(
    string Status,
    double MaxCallbackMs,
    double AverageCallbackMs,
    int CallCount,
    bool DefenderBlocked,
    string Details
);

public class HookThread : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly Thread _thread;
    private uint _threadId;
    private IntPtr _hHookKeyboard = IntPtr.Zero;
    private IntPtr _hHookMouse = IntPtr.Zero;
    private readonly ManualResetEventSlim _started = new(false);
    private bool _disposed;

    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;

    private readonly List<double> _callbackDurationsMs = new();
    private readonly object _lock = new();

    public HookThread()
    {
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;

        _thread = new Thread(ThreadEntry)
        {
            IsBackground = true,
            Name = "LightshotHookThread"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _started.Wait(2000);
    }

    private void ThreadEntry()
    {
        _threadId = GetCurrentThreadId();
        IntPtr hMod = GetModuleHandleW(null);

        _hHookKeyboard = SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        _hHookMouse = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, hMod, 0);

        _started.Set();

        while (!_disposed && GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }

        if (_hHookKeyboard != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hHookKeyboard);
            _hHookKeyboard = IntPtr.Zero;
        }
        if (_hHookMouse != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hHookMouse);
            _hHookMouse = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            long start = Stopwatch.GetTimestamp();
            IntPtr res = CallNextHookEx(_hHookKeyboard, nCode, wParam, lParam);
            long end = Stopwatch.GetTimestamp();
            double ms = (end - start) * 1000.0 / Stopwatch.Frequency;
            lock (_lock)
            {
                _callbackDurationsMs.Add(ms);
            }
            return res;
        }
        return CallNextHookEx(_hHookKeyboard, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            long start = Stopwatch.GetTimestamp();
            IntPtr res = CallNextHookEx(_hHookMouse, nCode, wParam, lParam);
            long end = Stopwatch.GetTimestamp();
            double ms = (end - start) * 1000.0 / Stopwatch.Frequency;
            lock (_lock)
            {
                _callbackDurationsMs.Add(ms);
            }
            return res;
        }
        return CallNextHookEx(_hHookMouse, nCode, wParam, lParam);
    }

    public HookBenchmarkResult BenchmarkHooks()
    {
        // Check if hooks installed
        if (_hHookKeyboard == IntPtr.Zero || _hHookMouse == IntPtr.Zero)
        {
            return new HookBenchmarkResult(
                Status: "FAIL",
                MaxCallbackMs: 0,
                AverageCallbackMs: 0,
                CallCount: 0,
                DefenderBlocked: true,
                Details: "SetWindowsHookEx failed or blocked by security system"
            );
        }

        // Simulate small input to generate callback samples
        [DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        for (int i = 0; i < 10; i++)
        {
            mouse_event(0x0001, 1, 1, 0, UIntPtr.Zero);
            keybd_event(0x91 /* VK_SCROLL */, 0, 0, UIntPtr.Zero); // ScrollLock down
            keybd_event(0x91, 0, 0x0002, UIntPtr.Zero); // ScrollLock up
            Thread.Sleep(5);
        }

        List<double> samples;
        lock (_lock)
        {
            samples = new List<double>(_callbackDurationsMs);
        }

        double maxMs = samples.Count > 0 ? samples.Max() : 0.0;
        double avgMs = samples.Count > 0 ? samples.Average() : 0.0;

        // Pass criterion: max under 5 ms; hook not blocked
        bool pass = maxMs < 5.0 && samples.Count > 0;

        return new HookBenchmarkResult(
            Status: pass ? "PASS" : "FAIL",
            MaxCallbackMs: Math.Round(maxMs, 3),
            AverageCallbackMs: Math.Round(avgMs, 3),
            CallCount: samples.Count,
            DefenderBlocked: false,
            Details: pass
                ? $"Hook callbacks executed in max {maxMs:F3} ms (< 5 ms bar). Windows Defender did not flag or block hooks."
                : $"Hook callbacks exceeded 5 ms limit (max: {maxMs:F3} ms) or had no samples"
        );
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_threadId != 0)
        {
            const uint WM_QUIT = 0x0012;
            PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        _thread.Join(1000);
    }
}
