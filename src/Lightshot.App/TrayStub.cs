using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lightshot.App;

public sealed class TrayStub : IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint WM_USER = 0x0400;
    private const uint WM_TRAYICON = WM_USER + 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
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
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private static readonly IntPtr IDI_APPLICATION = new(32512);

    private readonly uint _iconId = 1001;
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isCreated;
    private bool _disposed;

    public bool IsCreated => _isCreated;

    public void Initialize()
    {
        if (_isCreated) return;

        try
        {
            _hwnd = CreateWindowEx(
                0,
                "STATIC",
                "LightshotTrayStub",
                0,
                0, 0, 0, 0,
                HWND_MESSAGE,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = _iconId,
                uFlags = NIF_ICON | NIF_TIP,
                hIcon = LoadIcon(IntPtr.Zero, IDI_APPLICATION),
                szTip = "Lightshot for Windows"
            };

            _isCreated = Shell_NotifyIcon(NIM_ADD, ref data);
        }
        catch
        {
            _isCreated = false;
        }
    }

    public void Remove()
    {
        if (!_isCreated) return;

        try
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = _iconId
            };

            Shell_NotifyIcon(NIM_DELETE, ref data);
            _isCreated = false;
        }
        catch
        {
            // Ignore failure on cleanup
        }
        finally
        {
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Remove();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
