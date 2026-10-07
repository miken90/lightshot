// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Interop;
using Vortice.DXGI;

namespace Lightshot.Platform.Windows.Displays;

/// <summary>
/// Enumerate and tracks physical monitors, DPI scalings, and DXGI adapter LUIDs.
/// </summary>
public class DisplayTopology
{
    private readonly List<DisplayInfo> _displays = [];
    private readonly object _lock = new();

    public event Action? TopologyChanged;

    public static IReadOnlyList<DisplayInfo> GetDisplays() => new DisplayTopology().Displays;

    /// <summary>
    /// The pointer position in physical pixels (the process is PerMonitorV2-aware).
    /// </summary>
    public static Point GetCursorPosition()
    {
        return GetCursorPos(out var pt) ? new Point(pt.X, pt.Y) : new Point(0, 0);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CURSORPOINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORPOINT
    {
        public int X;
        public int Y;
    }

    public IReadOnlyList<DisplayInfo> Displays
    {
        get
        {
            lock (_lock)
            {
                return _displays.ToList();
            }
        }
    }

    public DisplayInfo? PrimaryDisplay
    {
        get
        {
            lock (_lock)
            {
                return _displays.FirstOrDefault(d => d.IsPrimary) ?? _displays.FirstOrDefault();
            }
        }
    }

    public DisplayTopology(IEnumerable<DisplayInfo>? seededDisplays = null)
    {
        if (seededDisplays != null)
        {
            _displays.AddRange(seededDisplays);
        }
        else
        {
            Refresh();
        }
    }

    public DisplayInfo? GetDisplay(uint displayId)
    {
        lock (_lock)
        {
            return _displays.FirstOrDefault(d => d.DisplayId == displayId);
        }
    }

    public DisplayInfo? GetDisplayForPoint(Point physicalPoint)
    {
        lock (_lock)
        {
            return _displays.FirstOrDefault(d => d.Bounds.Contains(physicalPoint));
        }
    }

    public DisplayInfo? GetDisplayForRect(Rect physicalRect)
    {
        lock (_lock)
        {
            return DisplayMath.FindLargestOverlap(_displays, physicalRect);
        }
    }

    /// <summary>
    /// Re-enumerates all active monitors and queries DXGI adapter LUIDs.
    /// </summary>
    public void Refresh()
    {
        Dpi.EnsurePerMonitorV2();
        var dxgiMap = QueryDxgiOutputMap();
        var detected = EnumerateMonitorsWin32(dxgiMap);

        bool changed = false;
        lock (_lock)
        {
            if (_displays.Count != detected.Count || !_displays.SequenceEqual(detected))
            {
                _displays.Clear();
                _displays.AddRange(detected);
                changed = true;
            }
        }

        if (changed)
        {
            TopologyChanged?.Invoke();
        }
    }

    private static Dictionary<string, long> QueryDxgiOutputMap()
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            if (factory == null) return map;

            for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                if (adapter == null) continue;
                long luid = (long)adapter.Description1.Luid;

                for (uint j = 0; adapter.EnumOutputs(j, out var output).Success; j++)
                {
                    if (output == null) continue;
                    try
                    {
                        string devName = output.Description.DeviceName;
                        if (!string.IsNullOrEmpty(devName) && !map.ContainsKey(devName))
                        {
                            map[devName] = luid;
                        }
                    }
                    finally
                    {
                        output.Dispose();
                    }
                }
                adapter.Dispose();
            }
        }
        catch
        {
            // DXGI unavailable (headless/restricted environment)
        }
        return map;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEXW
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

    private const uint MONITORINFOF_PRIMARY = 0x00000001;

    private static List<DisplayInfo> EnumerateMonitorsWin32(Dictionary<string, long> dxgiMap)
    {
        var list = new List<DisplayInfo>();
        uint displayIndex = 1;

        MonitorEnumProc callback = (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFOEXW();
            mi.cbSize = Marshal.SizeOf<MONITORINFOEXW>();
            if (GetMonitorInfoW(hMon, ref mi))
            {
                string deviceName = mi.szDevice ?? $"\\\\.\\DISPLAY{displayIndex}";
                bool isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;

                int width = mi.rcMonitor.Right - mi.rcMonitor.Left;
                int height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                int workW = mi.rcWork.Right - mi.rcWork.Left;
                int workH = mi.rcWork.Bottom - mi.rcWork.Top;

                var (dpiX, dpiY) = Dpi.GetDpiForMonitor(hMon);
                double scaleFactor = Dpi.ScaleFactorFromDpi(dpiX);

                dxgiMap.TryGetValue(deviceName, out long adapterLuid);

                var bounds = new Rect(mi.rcMonitor.Left, mi.rcMonitor.Top, width, height);
                var workArea = new Rect(mi.rcWork.Left, mi.rcWork.Top, workW, workH);

                list.Add(new DisplayInfo(
                    DisplayId: displayIndex,
                    DeviceName: deviceName,
                    HMonitor: hMon,
                    Bounds: bounds,
                    WorkArea: workArea,
                    ScaleFactor: scaleFactor,
                    DpiX: dpiX,
                    DpiY: dpiY,
                    IsPrimary: isPrimary,
                    AdapterLuid: adapterLuid
                ));

                displayIndex++;
            }
            return true;
        };

        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        }
        catch
        {
        }

        // Fallback for headless environments with 0 displays
        if (list.Count == 0)
        {
            list.Add(new DisplayInfo(
                DisplayId: 1,
                DeviceName: "\\\\.\\DISPLAY1",
                HMonitor: IntPtr.Zero,
                Bounds: new Rect(0, 0, 1920, 1080),
                WorkArea: new Rect(0, 0, 1920, 1040),
                ScaleFactor: 1.0,
                DpiX: 96,
                DpiY: 96,
                IsPrimary: true,
                AdapterLuid: 0
            ));
        }

        return list;
    }
}
