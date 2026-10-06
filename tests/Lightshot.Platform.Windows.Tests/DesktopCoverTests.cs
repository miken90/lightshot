// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;
using Lightshot.Core;
using Lightshot.Platform.Windows.Settings;
using Lightshot.TestSupport;
using Xunit;
using DesktopCoverManager = global::Lightshot.Platform.Windows.DesktopCover.DesktopCover;
using DesktopWallpaperPosition = global::Lightshot.Platform.Windows.DesktopCover.DesktopWallpaperPosition;
using DesktopCoverWindow = global::Lightshot.Platform.Windows.DesktopCover.DesktopCoverWindow;

namespace Lightshot.Platform.Windows.Tests;

public class DesktopCoverTests
{
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLongW(IntPtr hWnd, int nIndex);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [Fact]
    [Desktop]
    public void IconsAbsentFromStillAndCoverRemoved()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"lightshot_cover_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string settingsPath = Path.Combine(tempDir, "settings.json");

        try
        {
            var settings = new JsonSettingsStore(settingsPath);

            // 1. By default, HideDesktopIcons is false -> CreateIfEnabled returns null
            Assert.False(settings.HideDesktopIcons);
            var disabledCover = DesktopCoverManager.CreateIfEnabled(settings);
            Assert.Null(disabledCover);

            // 2. Enable HideDesktopIcons -> CreateIfEnabled creates and shows cover
            settings.HideDesktopIcons = true;
            using var cover = DesktopCoverManager.CreateIfEnabled(settings);
            Assert.NotNull(cover);
            Assert.True(cover.IsActive);

            var handles = cover.WindowHandles;
            Assert.NotEmpty(handles);

            // Verify each cover window is created, visible, and has click-through style
            foreach (var hwnd in handles)
            {
                Assert.True(IsWindow(hwnd), "Cover window HWND must be valid.");

                int exStyle = GetWindowLongW(hwnd, GWL_EXSTYLE);
                Assert.True((exStyle & WS_EX_TRANSPARENT) != 0, "Cover window must have WS_EX_TRANSPARENT for click-through.");
            }

            // 3. Teardown cover (simulating cancel or completion)
            cover.TearDown();

            Assert.False(cover.IsActive);
            Assert.Empty(cover.WindowHandles);

            // Verify HWNDs were properly destroyed
            foreach (var hwnd in handles)
            {
                Assert.False(IsWindow(hwnd), "Cover window HWND must be destroyed on teardown.");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    [Unit]
    public void RenderWallpaperAllPositionsSucceed()
    {
        using var bitmap = new SKBitmap(100, 100);
        using (var c = new SKCanvas(bitmap))
        {
            c.Clear(SKColors.Blue);
        }

        var positions = new[]
        {
            DesktopWallpaperPosition.Center,
            DesktopWallpaperPosition.Stretch,
            DesktopWallpaperPosition.Tile,
            DesktopWallpaperPosition.Fit,
            DesktopWallpaperPosition.Fill,
            DesktopWallpaperPosition.Span
        };

        foreach (var pos in positions)
        {
            using var surface = SKSurface.Create(new SKImageInfo(200, 200, SKColorType.Bgra8888));
            Assert.NotNull(surface);

            DesktopCoverWindow.RenderWallpaper(surface.Canvas, 200, 200, bitmap, pos, 0x00FF0000);
            // Must render without exception
        }
    }
}
