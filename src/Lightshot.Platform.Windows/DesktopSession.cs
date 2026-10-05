using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lightshot.Platform.Windows;

public static class DesktopSession
{
    private const int WTS_CURRENT_SESSION = -1;
    private static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;

    public enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        int sessionId,
        int wtsInfoClass,
        out IntPtr ppBuffer,
        out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct REASON_CONTEXT
    {
        public uint Version;
        public uint Flags;
        public string SimpleReasonString;
    }

    private const uint POWER_REQUEST_CONTEXT_VERSION = 0;
    private const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;
    private const int PowerRequestDisplayRequired = 0;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT context);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerSetRequest(IntPtr powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerClearRequest(IntPtr powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static bool IsInteractiveSession()
    {
        int currentSessionId = Process.GetCurrentProcess().SessionId;
        // Session 0 is non-interactive service session
        if (currentSessionId == 0)
        {
            return false;
        }

        uint activeConsoleSession = WTSGetActiveConsoleSessionId();
        if (activeConsoleSession == 0xFFFFFFFF)
        {
            return false;
        }

        // Check connect state: WTSConnectState = 8
        if (WTSQuerySessionInformation(WTS_CURRENT_SERVER_HANDLE, WTS_CURRENT_SESSION, 8, out IntPtr buffer, out int bytesReturned))
        {
            try
            {
                if (bytesReturned >= sizeof(int))
                {
                    int state = Marshal.ReadInt32(buffer);
                    return state == (int)WTS_CONNECTSTATE_CLASS.WTSActive;
                }
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }

        return true;
    }

    public static IDisposable? TakeDisplayRequiredRequest(string reason = "Lightshot Test Run")
    {
        try
        {
            var ctx = new REASON_CONTEXT
            {
                Version = POWER_REQUEST_CONTEXT_VERSION,
                Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING,
                SimpleReasonString = reason
            };

            IntPtr handle = PowerCreateRequest(ref ctx);
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                if (PowerSetRequest(handle, PowerRequestDisplayRequired))
                {
                    return new PowerRequestScope(handle);
                }
                CloseHandle(handle);
            }
        }
        catch
        {
            // Fallback gracefully if power requests not permitted
        }

        return null;
    }

    private sealed class PowerRequestScope : IDisposable
    {
        private IntPtr _handle;

        public PowerRequestScope(IntPtr handle)
        {
            _handle = handle;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero && _handle != new IntPtr(-1))
            {
                PowerClearRequest(_handle, PowerRequestDisplayRequired);
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
