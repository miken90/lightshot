// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Platform.Windows.Tray;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class TrayIconAssetsTests
{
    [Fact]
    [Unit]
    public void RecordingIconHasRedDotAndTransparentCorners()
    {
        uint[] pixels = TrayIconAssets.RecordingIconPixels(16);
        Assert.Equal(256, pixels.Length);

        // Center pixel at (8, 8) or (7, 7) must be red (0xFFFF3B30)
        Assert.Equal(0xFFFF3B30u, pixels[7 * 16 + 7]);
        Assert.Equal(0xFFFF3B30u, pixels[8 * 16 + 8]);

        // Corner [0] at (0, 0) must have alpha 0
        Assert.Equal(0x00000000u, pixels[0] & 0xFF000000u);

        IntPtr hIcon = TrayIconAssets.CreateRecordingTrayIcon();
        Assert.NotEqual(IntPtr.Zero, hIcon);

        bool destroyed = TrayIconAssets.DestroyIcon(hIcon);
        Assert.True(destroyed);
    }
}
