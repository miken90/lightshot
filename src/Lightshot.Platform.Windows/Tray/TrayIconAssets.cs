// Ported from LightshotKit/Sources/LightshotKit/StatusMenuController.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Lightshot.Platform.Windows.Tray;

/// <summary>
/// Provides system tray icon assets, including multi-size ICO loading,
/// theme-based glyph selection (tray-light.ico vs tray-dark.ico), and fallback glyph creation.
/// </summary>
public static class TrayIconAssets
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbm,
        uint start,
        uint cLines,
        [Out] uint[] lpvBits,
        ref BITMAPINFOHEADER lpbmi,
        uint usage);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(
        byte[] pbIconBits,
        uint cbIconBits,
        bool fIcon,
        uint dwVersion,
        int cxDesired,
        int cyDesired,
        uint uFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, IntPtr lpBits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// Chooses the tray icon filename based on system taskbar theme.
    /// SystemUsesLightTheme == 1 (light taskbar) -> tray-light.ico (dark ink for high contrast on light taskbar).
    /// SystemUsesLightTheme == 0 (dark taskbar) -> tray-dark.ico (light ink for high contrast on dark taskbar).
    /// </summary>
    public static string ChooseTrayIconFileName(bool systemUsesLightTheme) =>
        systemUsesLightTheme ? "tray-light.ico" : "tray-dark.ico";

    /// <summary>
    /// Chooses the relative resource path for the tray icon.
    /// </summary>
    public static string ChooseTrayIconResourcePath(bool systemUsesLightTheme) =>
        $"Resources/Icons/Tray/{(systemUsesLightTheme ? "tray-light.ico" : "tray-dark.ico")}";

    /// <summary>
    /// Reads SystemUsesLightTheme from the registry.
    /// </summary>
    public static bool GetSystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("SystemUsesLightTheme") is int val)
            {
                return val != 0;
            }
        }
        catch
        {
        }
        return false;
    }

    /// <summary>
    /// Loads the appropriate tray icon for the given taskbar theme.
    /// </summary>
    public static IntPtr LoadTrayIcon(bool systemUsesLightTheme, int targetSize = 16)
    {
        string fileName = ChooseTrayIconFileName(systemUsesLightTheme);

        // 1. Try file on disk (output directory or project directory)
        byte[]? bytes = TryReadFromDisk(fileName);

        // 2. Try embedded resource
        bytes ??= TryReadFromAssemblyResources(fileName);

        if (bytes != null)
        {
            IntPtr hIcon = CreateIconFromIcoBytes(bytes, targetSize);
            if (hIcon != IntPtr.Zero)
            {
                return hIcon;
            }
        }

        // 3. Fallback to procedurally drawn placeholder icon
        return CreatePlaceholderTrayIcon();
    }

    private static byte[]? TryReadFromDisk(string fileName)
    {
        try
        {
            // BaseDirectory check
            string directPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Icons", "Tray", fileName);
            if (File.Exists(directPath)) return File.ReadAllBytes(directPath);

            string flatPath = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(flatPath)) return File.ReadAllBytes(flatPath);

            // Repository root check (tests)
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            int depth = 0;
            while (dir != null && depth < 6 && !File.Exists(Path.Combine(dir.FullName, "Lightshot.slnx")))
            {
                dir = dir.Parent;
                depth++;
            }
            if (dir != null && File.Exists(Path.Combine(dir.FullName, "Lightshot.slnx")))
            {
                string repoPath = Path.Combine(dir.FullName, "src", "Lightshot.App", "Resources", "Icons", "Tray", fileName);
                if (File.Exists(repoPath)) return File.ReadAllBytes(repoPath);
            }
        }
        catch
        {
        }
        return null;
    }

    private static byte[]? TryReadFromAssemblyResources(string fileName)
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.FullName?.StartsWith("Lightshot") != true) continue;

                // Check manifest resource streams
                string? resName = Array.Find(asm.GetManifestResourceNames(),
                    n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
                if (resName != null)
                {
                    using var stream = asm.GetManifestResourceStream(resName);
                    if (stream != null)
                    {
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }

                // Check .g.resources
                string gResName = $"{asm.GetName().Name}.g.resources";
                using var gStream = asm.GetManifestResourceStream(gResName);
                if (gStream != null)
                {
                    using var reader = new ResourceReader(gStream);
                    foreach (System.Collections.DictionaryEntry entry in reader)
                    {
                        if (entry.Key is string key && key.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (entry.Value is Stream s)
                            {
                                using var ms = new MemoryStream();
                                s.CopyTo(ms);
                                return ms.ToArray();
                            }
                            if (entry.Value is byte[] b)
                            {
                                return b;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static IntPtr CreateIconFromIcoBytes(byte[] data, int targetSize)
    {
        if (data.Length < 6) return IntPtr.Zero;
        if (BitConverter.ToUInt16(data, 0) != 0 || BitConverter.ToUInt16(data, 2) != 1) return IntPtr.Zero;

        ushort count = BitConverter.ToUInt16(data, 4);
        int bestOffset = -1;
        int bestLength = 0;
        int bestDiff = int.MaxValue;

        for (int i = 0; i < count; i++)
        {
            int entry = 6 + (i * 16);
            if (entry + 16 > data.Length) break;

            int w = data[entry] == 0 ? 256 : data[entry];
            int h = data[entry + 1] == 0 ? 256 : data[entry + 1];
            int diff = Math.Abs(w - targetSize);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestLength = BitConverter.ToInt32(data, entry + 8);
                bestOffset = BitConverter.ToInt32(data, entry + 12);
            }
        }

        if (bestOffset >= 0 && bestOffset + bestLength <= data.Length)
        {
            byte[] frame = new byte[bestLength];
            Array.Copy(data, bestOffset, frame, 0, bestLength);
            return CreateIconFromResourceEx(frame, (uint)bestLength, true, 0x00030000, targetSize, targetSize, 0);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Creates a 16x16 32-bit placeholder icon glyph in code (stylized camera/aperture).
    /// </summary>
    public static IntPtr CreatePlaceholderTrayIcon()
    {
        const int width = 16;
        const int height = 16;

        uint[] colorPixels = new uint[width * height];
        byte[] maskBytes = new byte[((width + 15) / 16) * 2 * height];

        uint white = 0xFFFFFFFF;
        uint purple = 0xFF8A2BE2;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                bool isBody = (x >= 2 && x <= 13 && y >= 4 && y <= 13);
                bool isBump = (x >= 5 && x <= 10 && y >= 2 && y <= 3);

                if (isBody || isBump)
                {
                    double dx = x - 7.5;
                    double dy = y - 8.5;
                    double distSq = dx * dx + dy * dy;

                    if (distSq <= 4.0)
                    {
                        colorPixels[idx] = purple;
                    }
                    else
                    {
                        colorPixels[idx] = white;
                    }
                }
                else
                {
                    colorPixels[idx] = 0x00000000;
                }
            }
        }

        return CreateIconFromPixels(colorPixels, width, height);
    }

    private static IntPtr CreateIconFromPixels(uint[] argb, int w, int h)
    {
        byte[] maskBytes = new byte[((w + 15) / 16) * 2 * h];
        IntPtr hbmColor = IntPtr.Zero;
        IntPtr hbmMask = IntPtr.Zero;
        GCHandle colorHandle = GCHandle.Alloc(argb, GCHandleType.Pinned);
        GCHandle maskHandle = GCHandle.Alloc(maskBytes, GCHandleType.Pinned);

        try
        {
            hbmColor = CreateBitmap(w, h, 1, 32, colorHandle.AddrOfPinnedObject());
            hbmMask = CreateBitmap(w, h, 1, 1, maskHandle.AddrOfPinnedObject());

            var iconInfo = new ICONINFO
            {
                fIcon = true,
                xHotspot = 0,
                yHotspot = 0,
                hbmMask = hbmMask,
                hbmColor = hbmColor
            };

            return CreateIconIndirect(ref iconInfo);
        }
        finally
        {
            colorHandle.Free();
            maskHandle.Free();
            if (hbmColor != IntPtr.Zero) DeleteObject(hbmColor);
            if (hbmMask != IntPtr.Zero) DeleteObject(hbmMask);
        }
    }

    public const uint UpdateDotColor = 0xFF0A84FF;

    // Reads a 32-bpp top-down copy of an icon's colour bitmap; null if the icon cannot be read.
    public static uint[]? ReadIconPixels(IntPtr hIcon, int size)
    {
        if (hIcon == IntPtr.Zero || size <= 0) return null;
        if (!GetIconInfo(hIcon, out var info)) return null;

        try
        {
            if (info.hbmColor == IntPtr.Zero) return null;
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return null;

            try
            {
                var bmi = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = size,
                    biHeight = -size,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0
                };

                uint[] pixels = new uint[size * size];
                int lines = GetDIBits(hdc, info.hbmColor, 0, (uint)size, pixels, ref bmi, 0);
                if (lines == 0) return null;

                bool hasAlpha = false;
                for (int i = 0; i < pixels.Length; i++)
                {
                    if ((pixels[i] & 0xFF000000) != 0)
                    {
                        hasAlpha = true;
                        break;
                    }
                }

                if (!hasAlpha)
                {
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] |= 0xFF000000;
                    }
                }

                return pixels;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }
        finally
        {
            if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
            if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
        }
    }

    // Pure: returns a copy of argb with a filled blue dot of radius size*0.22 in the bottom-right corner.
    public static uint[] AddUpdateDot(uint[] argb, int size)
    {
        uint[] copy = (uint[])argb.Clone();
        double r = size * 0.22;
        double rSq = r * r;
        double cx = size - r - 0.5;
        double cy = size - r - 0.5;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - cx;
                double dy = y - cy;
                if (dx * dx + dy * dy <= rSq)
                {
                    copy[y * size + x] = UpdateDotColor;
                }
            }
        }

        return copy;
    }

    public static IntPtr CreateTrayIconWithUpdateDot(bool systemUsesLightTheme, int size = 16)
    {
        IntPtr baseIcon = LoadTrayIcon(systemUsesLightTheme, size);
        try
        {
            uint[] pixels = (baseIcon == IntPtr.Zero ? null : ReadIconPixels(baseIcon, size)) ?? new uint[size * size];
            return CreateIconFromPixels(AddUpdateDot(pixels, size), size, size);
        }
        finally
        {
            if (baseIcon != IntPtr.Zero) DestroyIcon(baseIcon);
        }
    }

    public static uint[] RecordingIconPixels(int size = 16)
    {
        uint[] pixels = new uint[size * size];
        double radius = size * 0.375;
        double radiusSq = radius * radius;
        double center = (size - 1) / 2.0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - center;
                double dy = y - center;
                if (dx * dx + dy * dy <= radiusSq)
                {
                    pixels[y * size + x] = 0xFFFF3B30;
                }
                else
                {
                    pixels[y * size + x] = 0x00000000;
                }
            }
        }

        return pixels;
    }

    public static IntPtr CreateRecordingTrayIcon() => CreateIconFromPixels(RecordingIconPixels(), 16, 16);
}
