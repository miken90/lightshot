// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Lightshot.Platform.Windows.Displays;

namespace Lightshot.Platform.Windows.DesktopCover;

public enum DesktopWallpaperPosition
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5
}

/// <summary>
/// Immutable descriptor of a monitor's wallpaper configuration.
/// </summary>
public record WallpaperInfo(
    string MonitorId,
    string? ImagePath,
    DesktopWallpaperPosition Position,
    uint BackgroundColor,
    int X,
    int Y,
    int Width,
    int Height
);

/// <summary>
/// Queries Windows wallpaper settings per monitor via IDesktopWallpaper COM API,
/// falling back to user registry settings when COM queries are unavailable.
/// </summary>
public static class WallpaperProvider
{
    private static readonly Guid DesktopWallpaperClsid = new("c2cf3110-460e-4fc1-b9d0-8a1c0c9cc4bd");

    /// <summary>
    /// Retrieves wallpaper configuration for all connected displays.
    /// </summary>
    public static IReadOnlyList<WallpaperInfo> GetWallpapers()
    {
        var results = new List<WallpaperInfo>();

        try
        {
            Type? wallpaperType = Type.GetTypeFromCLSID(DesktopWallpaperClsid);
            if (wallpaperType != null)
            {
                var comObj = Activator.CreateInstance(wallpaperType);
                if (comObj is IDesktopWallpaper wallpaper)
                {
                    try
                    {
                        uint count = 0;
                        int hr = wallpaper.GetMonitorDevicePathCount(out count);
                        if (hr == 0 && count > 0)
                        {
                            wallpaper.GetPosition(out var position);
                            wallpaper.GetBackgroundColor(out uint bgColor);

                            var displays = DisplayTopology.GetDisplays();

                            for (uint i = 0; i < count; i++)
                            {
                                string monitorId = "";
                                wallpaper.GetMonitorDevicePathAt(i, out monitorId);

                                string path = "";
                                wallpaper.GetWallpaper(monitorId, out path);
                                if (string.IsNullOrEmpty(path))
                                {
                                    wallpaper.GetWallpaper(null, out path);
                                }

                                RECT rect;
                                wallpaper.GetMonitorRECT(monitorId, out rect);

                                int x = rect.Left;
                                int y = rect.Top;
                                int w = rect.Width;
                                int h = rect.Height;

                                if (w <= 0 || h <= 0)
                                {
                                    if (i < displays.Count)
                                    {
                                        x = (int)displays[(int)i].Bounds.X;
                                        y = (int)displays[(int)i].Bounds.Y;
                                        w = (int)displays[(int)i].Bounds.Width;
                                        h = (int)displays[(int)i].Bounds.Height;
                                    }
                                    else
                                    {
                                        w = 1920;
                                        h = 1080;
                                    }
                                }

                                results.Add(new WallpaperInfo(
                                    MonitorId: monitorId,
                                    ImagePath: string.IsNullOrWhiteSpace(path) ? null : path,
                                    Position: position,
                                    BackgroundColor: bgColor,
                                    X: x,
                                    Y: y,
                                    Width: w,
                                    Height: h
                                ));
                            }
                        }
                    }
                    finally
                    {
                        if (Marshal.IsComObject(comObj))
                        {
                            Marshal.ReleaseComObject(comObj);
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall back to registry
        }

        if (results.Count == 0)
        {
            results.AddRange(GetWallpapersFromRegistry());
        }

        return results;
    }

    private static List<WallpaperInfo> GetWallpapersFromRegistry()
    {
        var list = new List<WallpaperInfo>();

        string? wallpaperPath = null;
        var position = DesktopWallpaperPosition.Fill;
        uint bgColor = 0x00000000;

        try
        {
            using var desktopKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", false);
            if (desktopKey != null)
            {
                wallpaperPath = desktopKey.GetValue("Wallpaper") as string;
                string? style = desktopKey.GetValue("WallpaperStyle") as string;
                string? tile = desktopKey.GetValue("TileWallpaper") as string;

                if (tile == "1")
                {
                    position = DesktopWallpaperPosition.Tile;
                }
                else if (style == "0")
                {
                    position = DesktopWallpaperPosition.Center;
                }
                else if (style == "2")
                {
                    position = DesktopWallpaperPosition.Stretch;
                }
                else if (style == "6")
                {
                    position = DesktopWallpaperPosition.Fit;
                }
                else if (style == "10")
                {
                    position = DesktopWallpaperPosition.Fill;
                }
                else if (style == "22")
                {
                    position = DesktopWallpaperPosition.Span;
                }
            }

            using var colorsKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors", false);
            if (colorsKey != null)
            {
                string? bgStr = colorsKey.GetValue("Background") as string;
                if (!string.IsNullOrEmpty(bgStr))
                {
                    var parts = bgStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3 &&
                        byte.TryParse(parts[0], out byte r) &&
                        byte.TryParse(parts[1], out byte g) &&
                        byte.TryParse(parts[2], out byte b))
                    {
                        // COLORREF format 0x00bbggrr
                        bgColor = (uint)(r | (g << 8) | (b << 16));
                    }
                }
            }
        }
        catch
        {
            // Ignore registry read errors
        }

        var displays = DisplayTopology.GetDisplays();
        if (displays.Count == 0)
        {
            list.Add(new WallpaperInfo("Primary", wallpaperPath, position, bgColor, 0, 0, 1920, 1080));
        }
        else
        {
            for (int i = 0; i < displays.Count; i++)
            {
                var d = displays[i];
                list.Add(new WallpaperInfo(
                    MonitorId: d.DeviceName,
                    ImagePath: wallpaperPath,
                    Position: position,
                    BackgroundColor: bgColor,
                    X: (int)d.Bounds.X,
                    Y: (int)d.Bounds.Y,
                    Width: (int)d.Bounds.Width,
                    Height: (int)d.Bounds.Height
                ));
            }
        }

        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [ComImport]
    [Guid("b927045b-6724-4b52-a383-943a571f0d38")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        [PreserveSig]
        int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        [PreserveSig]
        int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);

        [PreserveSig]
        int GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorID);

        [PreserveSig]
        int GetMonitorDevicePathCount(out uint count);

        [PreserveSig]
        int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);

        [PreserveSig]
        int SetBackgroundColor(uint color);

        [PreserveSig]
        int GetBackgroundColor(out uint color);

        [PreserveSig]
        int SetPosition(DesktopWallpaperPosition position);

        [PreserveSig]
        int GetPosition(out DesktopWallpaperPosition position);

        [PreserveSig]
        int SetSlideshow(IntPtr items);

        [PreserveSig]
        int GetSlideshow(out IntPtr items);

        [PreserveSig]
        int AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, int direction);

        [PreserveSig]
        int GetStatus(out int state);

        [PreserveSig]
        int Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }
}
