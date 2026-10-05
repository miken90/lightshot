// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Core;
using Lightshot.Platform.Windows.Files;
using Lightshot.Platform.Windows.Settings;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class FileImageSinkTests : IDisposable
{
    private readonly string _tempDir;

    public FileImageSinkTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LightshotTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    [Unit]
    public void WritesAtomicallyWithUniqueName()
    {
        var settings = new JsonSettingsStore(Path.Combine(_tempDir, "settings.json"));
        var codec = new SkiaImageCodec();
        var sink = new FileImageSink(settings, codec);

        // Create a 2x2 test image (BGRA)
        byte[] pixels = new byte[2 * 2 * 4];
        Array.Fill(pixels, (byte)255);
        var rendered = new RenderedImage(2, 2, pixels);

        string targetPath = Path.Combine(_tempDir, "screenshot.png");

        // First write: should write to screenshot.png
        sink.Write(rendered, targetPath, new ImageFormat.Png());

        Assert.True(File.Exists(targetPath), "First file should exist at the target path.");
        long size1 = new FileInfo(targetPath).Length;
        Assert.True(size1 > 0, "File should have non-zero size.");

        // Second write to the exact same path: should automatically collision-suffix to "screenshot 2.png"
        sink.Write(rendered, targetPath, new ImageFormat.Png());

        string secondPath = Path.Combine(_tempDir, "screenshot 2.png");
        Assert.True(File.Exists(secondPath), "Second file should exist with collision suffix ' 2'.");
        long size2 = new FileInfo(secondPath).Length;
        Assert.True(size2 > 0, "Second file should have non-zero size.");

        // Third write to the exact same path: should collision-suffix to "screenshot 3.png"
        sink.Write(rendered, targetPath, new ImageFormat.Png());

        string thirdPath = Path.Combine(_tempDir, "screenshot 3.png");
        Assert.True(File.Exists(thirdPath), "Third file should exist with collision suffix ' 3'.");

        // Verify all 3 files exist simultaneously
        Assert.True(File.Exists(targetPath));
        Assert.True(File.Exists(secondPath));
        Assert.True(File.Exists(thirdPath));
    }
}
