using System;
using System.Runtime.InteropServices;
using Lightshot.Platform.Windows.Interop;

namespace Lightshot.Platform.Windows.Tray;

/// <summary>
/// Generates an in-memory placeholder glyph for the system tray icon in code.
/// Follow-up: tray glyph art (separate graphics job).
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
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, IntPtr lpBits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// Creates a 16x16 32-bit placeholder icon glyph in code (stylized camera/aperture).
    /// </summary>
    public static IntPtr CreatePlaceholderTrayIcon()
    {
        const int width = 16;
        const int height = 16;

        // 32-bit ARGB pixel buffer
        uint[] colorPixels = new uint[width * height];
        byte[] maskBytes = new byte[((width + 15) / 16) * 2 * height];

        // Draw a clean 16x16 camera-style box with lens circle in code
        // Border / fill color: 0xFFFFFFFF (opaque white) with slight shadow
        uint white = 0xFFFFFFFF;
        uint purple = 0xFF8A2BE2; // Lightshot purple accent

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;

                // Camera body: rect from x:2..13, y:4..13
                bool isBody = (x >= 2 && x <= 13 && y >= 4 && y <= 13);
                // Camera viewfinder bump: x:5..10, y:2..3
                bool isBump = (x >= 5 && x <= 10 && y >= 2 && y <= 3);

                if (isBody || isBump)
                {
                    // Center lens: circle at center (7.5, 8.5), radius ~ 2.5
                    double dx = x - 7.5;
                    double dy = y - 8.5;
                    double distSq = dx * dx + dy * dy;

                    if (distSq <= 4.0)
                    {
                        colorPixels[idx] = purple;
                    }
                    else if (distSq <= 7.0)
                    {
                        colorPixels[idx] = white;
                    }
                    else
                    {
                        colorPixels[idx] = white;
                    }
                }
                else
                {
                    // Transparent
                    colorPixels[idx] = 0x00000000;
                }
            }
        }

        IntPtr hbmColor = IntPtr.Zero;
        IntPtr hbmMask = IntPtr.Zero;
        GCHandle colorHandle = GCHandle.Alloc(colorPixels, GCHandleType.Pinned);
        GCHandle maskHandle = GCHandle.Alloc(maskBytes, GCHandleType.Pinned);

        try
        {
            hbmColor = CreateBitmap(width, height, 1, 32, colorHandle.AddrOfPinnedObject());
            hbmMask = CreateBitmap(width, height, 1, 1, maskHandle.AddrOfPinnedObject());

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
}
