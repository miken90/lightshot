// Ported from LightshotKit/Sources/LightshotKit/InputEventSource.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lightshot.Platform.Windows.Input;

[Flags]
public enum HookTypeMask
{
    None = 0,
    Mouse = 1,
    Keyboard = 2,
    Both = Mouse | Keyboard
}

/// <summary>
/// Dedicated thread running its own Win32 message pump for WH_MOUSE_LL and WH_KEYBOARD_LL hooks.
/// Callbacks ONLY enqueue into a bounded lock-free queue and return immediately to avoid OS hook timeouts.
/// </summary>
public sealed class HookThread : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const uint WM_QUIT = 0x0012;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

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
    private static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    private readonly HookTypeMask _mask;
    private readonly HookQueue<RawHookMessage> _queue;
    private readonly ManualResetEventSlim _startedEvent = new(false);
    private readonly object _lock = new();

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private HookProc? _mouseProc;
    private HookProc? _keyboardProc;
    private bool _disposed;

    public HookThread(HookTypeMask mask = HookTypeMask.Both, HookQueue<RawHookMessage>? queue = null)
    {
        _mask = mask;
        _queue = queue ?? new HookQueue<RawHookMessage>();
    }

    public HookQueue<RawHookMessage> Queue => _queue;

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed || _thread != null) return;

            _startedEvent.Reset();
            _thread = new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "dev.lightshot.hookthread"
            };
            _thread.Start();
        }

        _startedEvent.Wait(2000);
    }

    public void Stop()
    {
        Thread? threadToJoin = null;
        lock (_lock)
        {
            if (_thread == null) return;

            if (_threadId != 0)
            {
                PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
            threadToJoin = _thread;
            _thread = null;
        }

        if (threadToJoin != null && threadToJoin.IsAlive)
        {
            threadToJoin.Join(2000);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
        _startedEvent.Dispose();
    }

    private void RunMessageLoop()
    {
        _threadId = GetCurrentThreadId();
        IntPtr hModule = GetModuleHandleW(null);

        if ((_mask & HookTypeMask.Mouse) != 0)
        {
            _mouseProc = MouseHookCallback;
            _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, hModule, 0);
        }

        if ((_mask & HookTypeMask.Keyboard) != 0)
        {
            _keyboardProc = KeyboardHookCallback;
            _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, hModule, 0);
        }

        _startedEvent.Set();

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        _mouseProc = null;
        _keyboardProc = null;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            long qpc = Stopwatch.GetTimestamp();
            try
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                _queue.Enqueue(new RawHookMessage
                {
                    Type = HookType.Mouse,
                    Message = (int)wParam,
                    QpcTimestamp = qpc,
                    X = hookStruct.pt.X,
                    Y = hookStruct.pt.Y,
                    MouseData = hookStruct.mouseData,
                    Flags = hookStruct.flags
                });
            }
            catch
            {
                // Never throw from hook callback
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            long qpc = Stopwatch.GetTimestamp();
            try
            {
                var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                _queue.Enqueue(new RawHookMessage
                {
                    Type = HookType.Keyboard,
                    Message = (int)wParam,
                    QpcTimestamp = qpc,
                    VkCode = hookStruct.vkCode,
                    ScanCode = hookStruct.scanCode,
                    Flags = hookStruct.flags
                });
            }
            catch
            {
                // Never throw from hook callback
            }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }
}
