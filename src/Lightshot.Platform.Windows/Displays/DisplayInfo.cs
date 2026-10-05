// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Displays;

/// <summary>
/// Information about an active display monitor in physical pixel coordinates.
/// </summary>
public record DisplayInfo(
    uint DisplayId,
    string DeviceName,
    IntPtr HMonitor,
    Rect Bounds,
    Rect WorkArea,
    double ScaleFactor,
    uint DpiX,
    uint DpiY,
    bool IsPrimary,
    long AdapterLuid
)
{
    public int PhysicalWidth => (int)Math.Round(Bounds.Width);
    public int PhysicalHeight => (int)Math.Round(Bounds.Height);
}
