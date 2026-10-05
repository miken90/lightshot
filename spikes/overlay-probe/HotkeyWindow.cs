using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OverlayProbe;

public record PrintScreenClaimResult(
    int? SnippingToolSetting,
    bool RegisterHotKeySuccess,
    int LastWin32Error,
    string Details,
    string ExactSettingNeeded
);

public class HotkeyWindow : IDisposable
{
    private const uint WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_F9 = 0x78;
    private const uint VK_SNAPSHOT = 0x2C;

    private const int TEST_HOTKEY_ID = 1001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    public IntPtr Handle { get; private set; }
    public Action<long>? HotkeyTriggered { get; set; }

    private readonly WndProcDelegate _wndProc;
    private bool _registered;

    public HotkeyWindow()
    {
        _wndProc = WndProc;
        string className = $"LightshotHotkeyWindow_{Guid.NewGuid():N}";
        IntPtr hInst = GetModuleHandleW(null);

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInst,
            lpszClassName = className
        };
        RegisterClassExW(ref wc);

        // Message-only window (parent = HWND_MESSAGE = -3)
        Handle = CreateWindowExW(0, className, "HotkeyWindow", 0, 0, 0, 0, 0, (IntPtr)(-3), IntPtr.Zero, hInst, IntPtr.Zero);
    }

    public bool RegisterTestHotkey()
    {
        if (Handle == IntPtr.Zero) return false;
        // Ctrl + Shift + F9 with MOD_NOREPEAT
        _registered = RegisterHotKey(Handle, TEST_HOTKEY_ID, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_F9);
        return _registered;
    }

    public void UnregisterTestHotkey()
    {
        if (_registered && Handle != IntPtr.Zero)
        {
            UnregisterHotKey(Handle, TEST_HOTKEY_ID);
            _registered = false;
        }
    }

    public PrintScreenClaimResult CheckPrintScreenClaim()
    {
        int? snippingEnabled = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
            if (key != null)
            {
                object? val = key.GetValue("PrintScreenKeyForSnippingEnabled");
                if (val is int intVal)
                {
                    snippingEnabled = intVal;
                }
                else if (val is string strVal && int.TryParse(strVal, out int parsed))
                {
                    snippingEnabled = parsed;
                }
            }
        }
        catch { }

        // Attempt RegisterHotKey(VK_SNAPSHOT)
        const int snapshotId = 9998;
        bool registered = RegisterHotKey(Handle, snapshotId, 0, VK_SNAPSHOT);
        int err = Marshal.GetLastWin32Error();

        if (registered)
        {
            UnregisterHotKey(Handle, snapshotId);
        }

        string details;
        string settingNeeded;
        if (registered)
        {
            details = "VK_SNAPSHOT successfully registered without OS interception.";
            settingNeeded = "No registry change required; bare PrintScreen hotkey is available.";
        }
        else
        {
            details = $"VK_SNAPSHOT registration failed with Win32 error {err} (0x{err:X8}). SnippingTool claim active.";
            settingNeeded = "Set HKCU\\Control Panel\\Keyboard\\PrintScreenKeyForSnippingEnabled to 0 (DWORD) to release VK_SNAPSHOT for third-party hotkeys, or use default chords (Ctrl+PrintScreen / Ctrl+Shift+PrintScreen).";
        }

        return new PrintScreenClaimResult(
            SnippingToolSetting: snippingEnabled,
            RegisterHotKeySuccess: registered,
            LastWin32Error: err,
            Details: details,
            ExactSettingNeeded: settingNeeded
        );
    }

    private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        if (uMsg == WM_HOTKEY && wParam.ToInt32() == TEST_HOTKEY_ID)
        {
            long qpc = Stopwatch.GetTimestamp();
            HotkeyTriggered?.Invoke(qpc);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        UnregisterTestHotkey();
        if (Handle != IntPtr.Zero)
        {
            DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }
    }
}
