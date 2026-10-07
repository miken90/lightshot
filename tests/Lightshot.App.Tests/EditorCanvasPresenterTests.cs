// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lightshot.App.Views.Editor;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.App.Tests;

[Trait("Tier", "Unit")]
public class EditorCanvasPresenterTests
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

    private static CapturedImage CreateTestImage(int width, int height)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Green);
        }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

    [Fact]
    [Unit]
    public void CropToolShowsTheTightSurface()
    {
        RunInSta(() =>
        {
            var doc = new AnnotationDocument(CreateTestImage(200, 150));
            var sink = new LocalTestImageSink();
            var store = new LocalTestSettingsStore();
            var editorVm = new EditorViewModel(doc, sink, new DocumentRenderer(), null, store);
            var canvasVm = new CanvasPanelViewModel(editorVm, store)
            {
                Padding = 40,
                Aspect = AspectPreset.Auto,
                IsEnabled = true
            };

            var stage = new Grid();
            var backdrop = new Image();
            var container = new Canvas();
            var host = new CanvasHost { ViewModel = editorVm };

            var presenter = new EditorCanvasPresenter(editorVm, canvasVm, stage, backdrop, container, host);

            // 1. With canvas enabled and Select tool: framed surface
            Assert.False(presenter.IsTightView);
            Assert.Equal(280, stage.Width);
            Assert.Equal(230, stage.Height);
            Assert.Equal(Visibility.Visible, backdrop.Visibility);
            Assert.NotNull(backdrop.Source);
            Assert.Equal(40, container.Margin.Left);
            Assert.Equal(40, container.Margin.Top);

            // 2. Activate Crop tool: shows tight surface (no backdrop, no offset, no clip)
            editorVm.ActiveTool = EditorTool.Crop;

            Assert.True(presenter.IsTightView);
            Assert.Equal(200, stage.Width);
            Assert.Equal(150, stage.Height);
            Assert.Equal(Visibility.Collapsed, backdrop.Visibility);
            Assert.Null(backdrop.Source);
            Assert.Equal(0, container.Margin.Left);
            Assert.Equal(0, container.Margin.Top);
            Assert.Null(container.Clip);

            // 3. Deactivate Crop tool: restores frame
            editorVm.ActiveTool = EditorTool.Select;

            Assert.False(presenter.IsTightView);
            Assert.Equal(280, stage.Width);
            Assert.Equal(230, stage.Height);
            Assert.Equal(Visibility.Visible, backdrop.Visibility);
            Assert.NotNull(backdrop.Source);
            Assert.Equal(40, container.Margin.Left);
            Assert.Equal(40, container.Margin.Top);
        });
    }

    [Fact]
    [Unit]
    public void StageSizingAndClipFollowsCornerRadius()
    {
        RunInSta(() =>
        {
            var doc = new AnnotationDocument(CreateTestImage(200, 150));
            var sink = new LocalTestImageSink();
            var store = new LocalTestSettingsStore();
            var editorVm = new EditorViewModel(doc, sink, new DocumentRenderer(), null, store);
            var canvasVm = new CanvasPanelViewModel(editorVm, store)
            {
                Padding = 40,
                CornerRadius = 16,
                Aspect = AspectPreset.Auto,
                IsEnabled = true
            };

            var stage = new Grid();
            var backdrop = new Image();
            var container = new Canvas();

            var presenter = new EditorCanvasPresenter(editorVm, canvasVm, stage, backdrop, container);

            Assert.NotNull(container.Clip);
            Assert.IsType<RectangleGeometry>(container.Clip);
            var rectClip = (RectangleGeometry)container.Clip;
            Assert.Equal(16, rectClip.RadiusX);
            Assert.Equal(16, rectClip.RadiusY);
        });
    }

    private sealed class LocalTestImageSink : IImageSink
    {
        public List<RenderedImage> Copied { get; } = [];
        public List<(RenderedImage Image, string Path, ImageFormat Format)> Written { get; } = [];
        public List<string> CopiedText { get; } = [];

        public void CopyToClipboard(RenderedImage image) => Copied.Add(image);
        public void CopyText(string text) => CopiedText.Add(text);
        public void Write(RenderedImage image, string destinationPath, ImageFormat format) =>
            Written.Add((image, destinationPath, format));
    }

    private sealed class LocalTestSettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string?> _settings = new();

        public ImageFormat DefaultFormat { get; set; } = new ImageFormat.Png();
        public string SaveLocation { get; set; } = string.Empty;
        public string FilenamePattern { get; set; } = string.Empty;
        public HotkeyBindings Hotkeys { get; set; } = HotkeyBindings.Defaults;
        public bool OpenInEditor { get; set; } = true;
        public bool IncludeCursor { get; set; } = false;
        public double CaptureDelay { get; set; } = 0.0;
        public int HistoryRetention { get; set; } = 50;
        public int HistoryMaxAgeDays { get; set; } = 7;
        public int MagnifierZoom { get; set; } = 4;
        public bool LaunchAtLogin { get; set; } = false;
        public RecordingDefaults RecordingDefaults { get; set; } = new();
        public bool RememberLastRecordingArea { get; set; } = false;
        public CaptureRegion? LastRecordingRegion { get; set; }
        public AppearancePreference Appearance { get; set; } = AppearancePreference.System;
        public bool OcrKeepsLineBreaks { get; set; } = true;
        public bool HideDesktopIcons { get; set; } = false;
        public bool AdjustAreaBeforeCapture { get; set; } = false;
        public QuickAccessSettings QuickAccess { get; set; } = new();
        public AfterCaptureSettings AfterCapture { get; set; } = new();

        public string? GetSetting(string key) => _settings.TryGetValue(key, out var v) ? v : null;
        public void SetSetting(string key, string? value) => _settings[key] = value;
    }
}
