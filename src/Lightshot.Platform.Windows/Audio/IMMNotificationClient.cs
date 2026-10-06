// Lightshot Windows Port
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;

namespace Lightshot.Platform.Windows.Audio;

/// <summary>
/// Windows Core Audio MMDevice notification client COM interface.
/// Notifies when audio devices are added, removed, or change state.
/// </summary>
[ComImport]
[Guid("79912836-3708-4EE8-A80E-6711C5B0D00A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, uint dwNewState);
    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
    void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId);
    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, nint key);
}
