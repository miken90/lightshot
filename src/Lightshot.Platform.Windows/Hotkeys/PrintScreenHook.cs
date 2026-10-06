using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Hotkeys;

/// <summary>
/// Claims the bound PrintScreen chords with a WH_KEYBOARD_LL hook. Windows 11 routes PrintScreen to the
/// Snipping Tool by default (PrintScreenKeyForSnippingEnabled is on when the value is absent) while
/// RegisterHotKey(VK_SNAPSHOT) still succeeds, so WM_HOTKEY never arrives and the Snipping Tool overlay
/// opens instead. A low-level hook sees the key before that routing; swallowing it there gives the press
/// to Lightshot only. Unbound PrintScreen chords pass through untouched.
/// </summary>
public sealed class PrintScreenHook : IDisposable
{
    public const uint VK_SNAPSHOT = 0x2C;

    private const int WH_KEYBOARD_LL = 13;
    private const uint WM_QUIT = 0x0012;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint PM_NOREMOVE = 0x0000;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    // Unassigned virtual key, the same mask key AutoHotkey uses.
    private const ushort VK_MASK = 0xE8;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x;
        public int y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        // Pads the union to MOUSEINPUT, its largest member.
        public ulong padding;
    }

    [DllImport("user32.dll")]
    private static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    // Runs on the hook thread: it must only hand the action off, or every keystroke on the desktop waits.
    private readonly Action<CaptureAction> _onChord;
    private readonly object _lock = new();
    private volatile IReadOnlyDictionary<HotkeyModifiers, CaptureAction> _chords = new Dictionary<HotkeyModifiers, CaptureAction>();
    private bool _held;
    private Thread? _thread;
    private uint _threadId;
    private HookProc? _proc;
    private IntPtr _hook;
    private bool _disposed;

    public PrintScreenHook(Action<CaptureAction> onChord)
    {
        _onChord = onChord ?? throw new ArgumentNullException(nameof(onChord));
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>
    /// Replaces the claimed chords (modifiers held with PrintScreen). The hook is installed on first use.
    /// </summary>
    public void SetChords(IReadOnlyDictionary<HotkeyModifiers, CaptureAction> chords)
    {
        _chords = new Dictionary<HotkeyModifiers, CaptureAction>(chords);
        _held = false;
        if (chords.Count > 0)
        {
            Start();
        }
    }

    /// <summary>
    /// Decides one PrintScreen key event: returns true to swallow it. A bound chord fires once per press
    /// (auto-repeat is ignored, like MOD_NOREPEAT) and its key-up is swallowed too.
    /// </summary>
    public bool HandleKey(uint vkCode, bool isDown, HotkeyModifiers modifiers)
    {
        if (vkCode != VK_SNAPSHOT) return false;

        if (!isDown)
        {
            bool wasHeld = _held;
            _held = false;
            return wasHeld;
        }

        if (!_chords.TryGetValue(modifiers, out var action))
        {
            _held = false;
            return false;
        }
        if (_held) return true;

        // Held only once dispatched: a throwing handler lets the key through and the next press retries.
        _onChord(action);
        _held = true;
        return true;
    }

    private void Start()
    {
        // Not disposed: after a timed-out wait the hook thread still signals it.
        var started = new ManualResetEventSlim(false);
        lock (_lock)
        {
            if (_disposed || _thread != null) return;
            _thread = new Thread(() => RunMessageLoop(started))
            {
                IsBackground = true,
                Name = "LightshotPrintScreenHook"
            };
            _thread.Start();
        }
        started.Wait(2000);
    }

    private void RunMessageLoop(ManualResetEventSlim started)
    {
        _threadId = GetCurrentThreadId();
        _proc = HookCallback;
        _hook = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, GetModuleHandleW(null), 0);
        // Creates the message queue before Start returns, so Dispose's WM_QUIT cannot be lost.
        PeekMessageW(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
        started.Set();
        if (_hook == IntPtr.Zero)
        {
            // PrintScreen stays with the OS; RegisterHotKey still serves the chords where Windows delivers them.
            _proc = null;
            return;
        }

        while (GetMessageW(out _, IntPtr.Zero, 0, 0) > 0)
        {
            // Low-level hook callbacks are delivered while this thread waits in GetMessage.
        }

        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _proc = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                int msg = wParam.ToInt32();
                uint vk = (uint)Marshal.ReadInt32(lParam);
                bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                if (vk == VK_SNAPSHOT && (isDown || isUp))
                {
                    var modifiers = CurrentModifiers();
                    if (HandleKey(vk, isDown, modifiers))
                    {
                        if (isDown && (modifiers & (HotkeyModifiers.Option | HotkeyModifiers.Command)) != 0)
                        {
                            SendMaskKey();
                        }
                        return (IntPtr)1;
                    }
                }
            }
            catch
            {
                // Never throw from a hook callback
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    // With PrintScreen swallowed, a lone Alt or Win release would open the menu bar or Start menu.
    private static void SendMaskKey()
    {
        var inputs = new[]
        {
            new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_MASK } },
            new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_MASK, dwFlags = KEYEVENTF_KEYUP } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        var modifiers = HotkeyModifiers.None;
        if (Down(0x11 /* VK_CONTROL */)) modifiers |= HotkeyModifiers.Control;
        if (Down(0x12 /* VK_MENU */)) modifiers |= HotkeyModifiers.Option;
        if (Down(0x10 /* VK_SHIFT */)) modifiers |= HotkeyModifiers.Shift;
        if (Down(0x5B /* VK_LWIN */) || Down(0x5C /* VK_RWIN */)) modifiers |= HotkeyModifiers.Command;
        return modifiers;
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            thread = _thread;
            _thread = null;
        }

        if (thread != null)
        {
            if (_threadId != 0)
            {
                PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
            thread.Join(2000);
        }
    }
}
