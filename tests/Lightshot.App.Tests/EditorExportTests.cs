// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Controls;
using Lightshot.App.Views.Editor;
using Lightshot.Core;
using Lightshot.Platform.Windows.Settings;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.App.Tests;

[Trait("Tier", "Unit")]
public class EditorExportTests
{
    private static void RunInSta(Action action)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        captured?.Throw();
    }

    private static CapturedImage CreateTestImage(int width = 100, int height = 100)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.CornflowerBlue);
        }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

    [Fact]
    [Unit]
    public void QuickSaveWritesToDefaultDestinationAndCloses()
    {
        var doc = new AnnotationDocument(CreateTestImage());
        var sink = new TestImageSink();
        var tempDir = Path.Combine(Path.GetTempPath(), "LightshotQuickSaveTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var settings = new TestSettingsStore
            {
                SaveLocation = tempDir,
                FilenamePattern = "QuickTest-%Y%m%d",
                DefaultFormat = new ImageFormat.Png()
            };
            var vm = new EditorViewModel(doc, sink, settingsStore: settings);

            bool closed = false;
            vm.RequestClose += () => closed = true;

            vm.QuickSave();

            Assert.True(closed);
            Assert.Single(sink.Written);
            var (image, path, format) = sink.Written[0];
            Assert.NotNull(image);
            Assert.StartsWith(tempDir.Replace('\\', '/'), path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("QuickTest-", path);
            Assert.EndsWith(".png", path, StringComparison.OrdinalIgnoreCase);
            Assert.IsType<ImageFormat.Png>(format);
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
    public void QuickSaveWithNullSettingsDoesNothing()
    {
        var doc = new AnnotationDocument(CreateTestImage());
        var sink = new TestImageSink();
        var vm = new EditorViewModel(doc, sink, settingsStore: null);

        Assert.False(vm.CanQuickSave);

        bool closed = false;
        vm.RequestClose += () => closed = true;

        vm.QuickSave();

        Assert.False(closed);
        Assert.Empty(sink.Written);
    }

    [Fact]
    [Unit]
    public void QuickSaveShowsErrorOnException()
    {
        var doc = new AnnotationDocument(CreateTestImage());
        var throwingSink = new ThrowingImageSink();
        var settings = new TestSettingsStore
        {
            SaveLocation = "C:/fake/path",
            FilenamePattern = "ErrorTest-%Y",
            DefaultFormat = new ImageFormat.Png()
        };
        var vm = new EditorViewModel(doc, throwingSink, settingsStore: settings);

        bool closed = false;
        vm.RequestClose += () => closed = true;

        (string Title, string Message, string? Details)? errorReported = null;
        vm.ShowError += err => errorReported = err;

        vm.QuickSave();

        Assert.False(closed);
        Assert.NotNull(errorReported);
        Assert.Equal("Save Error", errorReported.Value.Title);
        Assert.Equal("Could not save screenshot.", errorReported.Value.Message);
        Assert.Equal("Disk write failed", errorReported.Value.Details);
    }

    [Fact]
    [Unit]
    public void SaveAsSuggestsConfiguredPattern()
    {
        var doc = new AnnotationDocument(CreateTestImage());
        var sink = new TestImageSink();
        var settings = new TestSettingsStore
        {
            FilenamePattern = "CustomPattern-%Y-%m-%d"
        };
        var vm = new EditorViewModel(doc, sink, settingsStore: settings);

        var testDate = new DateTime(2026, 10, 8, 14, 30, 0);
        var suggested = vm.SuggestedFileName(testDate);

        Assert.Equal("CustomPattern-2026-10-08", suggested);

        // When settingsStore is null, falls back to default timestamp format
        var vmNoSettings = new EditorViewModel(doc, sink, settingsStore: null);
        var fallback = vmNoSettings.SuggestedFileName(testDate);
        Assert.Equal("Screenshot 2026-10-08 at 14.30.00", fallback);

        // When pattern is empty string, also falls back to default timestamp format
        var emptyPatternSettings = new TestSettingsStore { FilenamePattern = "" };
        var vmEmpty = new EditorViewModel(doc, sink, settingsStore: emptyPatternSettings);
        Assert.Equal("Screenshot 2026-10-08 at 14.30.00", vmEmpty.SuggestedFileName(testDate));
    }

    [Fact]
    [Unit]
    public void DefaultExportFormatHonorsSettingsStore()
    {
        var doc = new AnnotationDocument(CreateTestImage());
        var sink = new TestImageSink();

        var vmNull = new EditorViewModel(doc, sink, settingsStore: null);
        Assert.IsType<ImageFormat.Png>(vmNull.DefaultExportFormat);

        var jpegSettings = new TestSettingsStore { DefaultFormat = new ImageFormat.Jpeg(0.85) };
        var vmJpeg = new EditorViewModel(doc, sink, settingsStore: jpegSettings);
        Assert.IsType<ImageFormat.Jpeg>(vmJpeg.DefaultExportFormat);
        Assert.Equal(0.85, ((ImageFormat.Jpeg)vmJpeg.DefaultExportFormat).Quality);
    }

    [Fact]
    [Unit]
    public void QuickSaveCommandDisabledWhenSettingsNull()
    {
        RunInSta(() =>
        {
            var doc = new AnnotationDocument(CreateTestImage());
            var sink = new TestImageSink();
            var vmNoSettings = new EditorViewModel(doc, sink, settingsStore: null);
            var windowNoSettings = new EditorWindow(vmNoSettings);

            Assert.False(windowNoSettings.QuickSaveCommand.CanExecute(null));

            var settings = new TestSettingsStore();
            var vmWithSettings = new EditorViewModel(doc, sink, settingsStore: settings);
            var windowWithSettings = new EditorWindow(vmWithSettings);

            Assert.True(windowWithSettings.QuickSaveCommand.CanExecute(null));
        });
    }

    [Fact]
    [Unit]
    public void QuickSaveButtonHasAutomationId()
    {
        RunInSta(() =>
        {
            var doc = new AnnotationDocument(CreateTestImage());
            var sink = new TestImageSink();
            var vm = new EditorViewModel(doc, sink);
            var window = new EditorWindow(vm);

            var button = window.FindName("QuickSaveButton") as Button;
            Assert.NotNull(button);
            Assert.Equal("QuickSaveButton", AutomationProperties.GetAutomationId(button));
            Assert.Equal("Quick Save", AutomationProperties.GetName(button));
        });
    }

    private sealed class ThrowingImageSink : IImageSink
    {
        public void CopyToClipboard(RenderedImage image) { }
        public void CopyText(string text) { }
        public void Write(RenderedImage image, string destinationPath, ImageFormat format) =>
            throw new IOException("Disk write failed");
    }
}
