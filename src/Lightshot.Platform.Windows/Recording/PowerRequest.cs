// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;

namespace Lightshot.Platform.Windows.Recording;

/// <summary>
/// Keeps the display and system awake during an active recording take.
/// </summary>
public sealed class PowerRequest : IDisposable
{
    private const uint POWER_REQUEST_CONTEXT_VERSION = 0;
    private const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;
    private const int PowerRequestDisplayRequired = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct REASON_CONTEXT
    {
        public uint Version;
        public uint Flags;
        public string SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT context);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerSetRequest(IntPtr powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerClearRequest(IntPtr powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private IntPtr _handle = IntPtr.Zero;
    private bool _active;

    public bool IsActive => _active;

    public PowerRequest(string reason = "Lightshot Screen Recording")
    {
        try
        {
            var ctx = new REASON_CONTEXT
            {
                Version = POWER_REQUEST_CONTEXT_VERSION,
                Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING,
                SimpleReasonString = reason
            };
            _handle = PowerCreateRequest(ref ctx);
        }
        catch
        {
            _handle = IntPtr.Zero;
        }
    }

    public bool Activate()
    {
        if (_handle == IntPtr.Zero || _handle == new IntPtr(-1)) return false;
        if (_active) return true;

        if (PowerSetRequest(_handle, PowerRequestDisplayRequired))
        {
            _active = true;
            return true;
        }
        return false;
    }

    public void Deactivate()
    {
        if (_handle == IntPtr.Zero || _handle == new IntPtr(-1)) return;
        if (!_active) return;

        PowerClearRequest(_handle, PowerRequestDisplayRequired);
        _active = false;
    }

    public void Dispose()
    {
        Deactivate();
        if (_handle != IntPtr.Zero && _handle != new IntPtr(-1))
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
