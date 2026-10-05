using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Interop;

namespace Lightshot.Platform.Windows.Hotkeys;

/// <summary>
/// Hidden message-only Win32 window for receiving WM_HOTKEY notifications.
/// </summary>
public sealed class HotkeyWindow : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static readonly List<Win32Window.WndProc> s_pinnedWndProcs = new();
    private readonly Win32Window.WndProc _wndProc;
    private readonly string _className;
    private readonly Dictionary<int, CaptureAction> _idToAction = new();
    private int _nextId = 1000;
    private bool _disposed;

    public IntPtr Handle { get; private set; }
    public Action<CaptureAction, long>? HotkeyTriggered { get; set; }

    public HotkeyWindow()
    {
        _wndProc = WndProc;
        lock (s_pinnedWndProcs)
        {
            s_pinnedWndProcs.Add(_wndProc);
        }
        _className = $"LightshotHotkeyWindow_{Guid.NewGuid():N}";
        IntPtr hInst = Win32Window.GetModuleHandleW(null);

        var wc = new Win32Window.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32Window.WNDCLASSEXW>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInst,
            lpszClassName = _className
        };
        Win32Window.RegisterClassExW(ref wc);

        Handle = Win32Window.CreateWindowExW(
            0, _className, "HotkeyWindow", 0, 0, 0, 0, 0,
            Win32Window.HWND_MESSAGE, IntPtr.Zero, hInst, IntPtr.Zero);
    }

    public bool TryRegister(CaptureAction action, HotkeyBinding binding, out int assignedId)
    {
        assignedId = _nextId++;
        uint fsModifiers = VirtualKeyNames.ToWin32Modifiers(binding.Modifiers, noRepeat: true);
        bool success = RegisterHotKey(Handle, assignedId, fsModifiers, binding.KeyCode);
        if (success)
        {
            _idToAction[assignedId] = action;
            return true;
        }

        return false;
    }

    public void Unregister(int id)
    {
        if (_idToAction.Remove(id))
        {
            UnregisterHotKey(Handle, id);
        }
    }

    public void UnregisterAll()
    {
        foreach (int id in _idToAction.Keys)
        {
            UnregisterHotKey(Handle, id);
        }
        _idToAction.Clear();
    }

    private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        if (uMsg == Win32Window.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_idToAction.TryGetValue(id, out var action))
            {
                long qpc = Stopwatch.GetTimestamp();
                HotkeyTriggered?.Invoke(action, qpc);
                return IntPtr.Zero;
            }
        }
        return Win32Window.DefWindowProcW(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        if (Handle != IntPtr.Zero)
        {
            Win32Window.DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }
        IntPtr hInst = Win32Window.GetModuleHandleW(null);
        Win32Window.UnregisterClassW(_className, hInst);
    }
}
