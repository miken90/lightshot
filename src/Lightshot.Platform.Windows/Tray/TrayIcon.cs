using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Interop;

namespace Lightshot.Platform.Windows.Tray;

/// <summary>
/// System tray notification icon managed via Shell_NotifyIconW (version 4).
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NOTIFYICON_VERSION_4 = 4;

    private const uint WM_TRAYCALLBACK = Win32Window.WM_APP + 0x100;
    private const uint NIN_SELECT = Win32Window.WM_USER + 0;
    private const uint NIN_KEYSELECT = Win32Window.WM_USER + 1;
    private const uint WM_CONTEXTMENU = 0x007B;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private const uint WM_SETTINGCHANGE = 0x001A;

    private static readonly List<Win32Window.WndProc> s_pinnedWndProcs = new();
    private readonly Win32Window.WndProc _wndProc;
    private readonly string _className;
    private IntPtr _hIcon;
    private IntPtr _hWnd;
    private bool _added;
    private bool _disposed;
    private bool _currentSystemUsesLightTheme;

    public bool IsCreated => _added;
    public bool IsAdded => _added;

    public HotkeyBindings? Bindings { get; set; }
    public IReadOnlyList<DisplayMenuItem>? Displays { get; set; }

    public Action<CaptureAction>? OnCaptureAction { get; set; }
    public Action<uint>? OnFullscreenDisplayCapture { get; set; }
    public Action? OnOpenFile { get; set; }
    public Action? OnHistory { get; set; }
    public Action? OnSettings { get; set; }
    public Action? OnQuit { get; set; }

    public TrayIcon(string tooltip = "Lightshot")
    {
        _wndProc = WndProc;
        lock (s_pinnedWndProcs)
        {
            s_pinnedWndProcs.Add(_wndProc);
        }
        _className = $"LightshotTrayHost_{Guid.NewGuid():N}";
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

        _hWnd = Win32Window.CreateWindowExW(
            0, _className, "TrayHost", 0, 0, 0, 0, 0,
            Win32Window.HWND_MESSAGE, IntPtr.Zero, hInst, IntPtr.Zero);

        _currentSystemUsesLightTheme = TrayIconAssets.GetSystemUsesLightTheme();
        _hIcon = TrayIconAssets.LoadTrayIcon(_currentSystemUsesLightTheme);

        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYCALLBACK,
            hIcon = _hIcon,
            szTip = tooltip,
            uTimeoutOrVersion = NOTIFYICON_VERSION_4
        };

        if (Shell_NotifyIconW(NIM_ADD, ref nid))
        {
            _added = true;
            Shell_NotifyIconW(NIM_SETVERSION, ref nid);
        }
    }

    public void UpdateTooltip(string tooltip)
    {
        if (!_added || _disposed) return;
        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NIF_TIP,
            szTip = tooltip
        };
        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        if (uMsg == WM_SETTINGCHANGE)
        {
            bool newSystemLight = TrayIconAssets.GetSystemUsesLightTheme();
            if (newSystemLight != _currentSystemUsesLightTheme)
            {
                UpdateTheme(newSystemLight);
            }
            return IntPtr.Zero;
        }

        if (uMsg == WM_TRAYCALLBACK)
        {
            uint eventId = unchecked((uint)lParam.ToInt64() & 0xFFFF);
            switch (eventId)
            {
                case Win32Window.WM_LBUTTONUP:
                case NIN_SELECT:
                    OnCaptureAction?.Invoke(CaptureAction.Area);
                    return IntPtr.Zero;

                case Win32Window.WM_RBUTTONUP:
                case WM_CONTEXTMENU:
                case NIN_KEYSELECT:
                    ShowContextMenu();
                    return IntPtr.Zero;
            }
        }

        return Win32Window.DefWindowProcW(hWnd, uMsg, wParam, lParam);
    }

    public void UpdateTheme(bool systemUsesLightTheme)
    {
        if (_disposed) return;
        _currentSystemUsesLightTheme = systemUsesLightTheme;
        IntPtr newIcon = TrayIconAssets.LoadTrayIcon(systemUsesLightTheme);
        if (newIcon != IntPtr.Zero)
        {
            UpdateIcon(newIcon);
        }
    }

    public void UpdateIcon(IntPtr newIcon)
    {
        if (!_added || _disposed || newIcon == IntPtr.Zero) return;
        IntPtr oldIcon = _hIcon;
        _hIcon = newIcon;

        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NIF_ICON,
            hIcon = _hIcon
        };
        Shell_NotifyIconW(NIM_MODIFY, ref nid);

        if (oldIcon != IntPtr.Zero && oldIcon != newIcon)
        {
            TrayIconAssets.DestroyIcon(oldIcon);
        }
    }

    private void ShowContextMenu()
    {
        GetCursorPos(out var pt);
        uint cmd = TrayMenu.Show(_hWnd, pt.x, pt.y, Bindings, Displays);
        switch (cmd)
        {
            case TrayMenu.CMD_AREA:
                OnCaptureAction?.Invoke(CaptureAction.Area);
                break;
            case TrayMenu.CMD_FULLSCREEN:
                OnCaptureAction?.Invoke(CaptureAction.Fullscreen);
                break;
            case TrayMenu.CMD_WINDOW:
                OnCaptureAction?.Invoke(CaptureAction.Window);
                break;
            case TrayMenu.CMD_REPEAT_LAST:
                OnCaptureAction?.Invoke(CaptureAction.RepeatLast);
                break;
            case TrayMenu.CMD_OPEN_FILE:
                OnOpenFile?.Invoke();
                break;
            case TrayMenu.CMD_HISTORY:
                OnHistory?.Invoke();
                break;
            case TrayMenu.CMD_SETTINGS:
                OnSettings?.Invoke();
                break;
            case TrayMenu.CMD_QUIT:
                OnQuit?.Invoke();
                break;
            default:
                if (cmd >= TrayMenu.CMD_DISPLAY_BASE && Displays != null)
                {
                    int idx = (int)(cmd - TrayMenu.CMD_DISPLAY_BASE);
                    if (idx >= 0 && idx < Displays.Count)
                    {
                        OnFullscreenDisplayCapture?.Invoke(Displays[idx].DisplayId);
                    }
                }
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_added)
        {
            var nid = new NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _hWnd,
                uID = 1
            };
            Shell_NotifyIconW(NIM_DELETE, ref nid);
            _added = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            TrayIconAssets.DestroyIcon(_hIcon);
        }

        if (_hWnd != IntPtr.Zero)
        {
            Win32Window.DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }

        IntPtr hInst = Win32Window.GetModuleHandleW(null);
        Win32Window.UnregisterClassW(_className, hInst);
    }
}
