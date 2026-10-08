// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.App.Views.Editor;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.App.Tests;

[Trait("Tier", "Unit")]
public class CanvasPanelViewModelTests
{
    private static CapturedImage CreateTestImage(int width, int height)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Blue);
        }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

    [Fact]
    [Unit]
    public void NewSessionStartsWithCanvasOff()
    {
        var doc = new AnnotationDocument(CreateTestImage(200, 150));
        var store = new LocalTestSettingsStore();
        var vm = new CanvasPanelViewModel(doc, store);

        Assert.False(vm.IsEnabled);
        Assert.True(doc.Canvas == null || !doc.Canvas.Enabled);

        var renderer = new DocumentRenderer();
        var rendered = renderer.Render(doc);
        Assert.Equal(200, rendered.PixelWidth);
        Assert.Equal(150, rendered.PixelHeight);
    }

    [Fact]
    [Unit]
    public void EnablingRendersAtTheCanvasSize()
    {
        var doc = new AnnotationDocument(CreateTestImage(200, 150));
        var store = new LocalTestSettingsStore();
        var vm = new CanvasPanelViewModel(doc, store)
        {
            Padding = 40,
            Aspect = AspectPreset.Auto,
            IsEnabled = true
        };

        Assert.True(doc.Canvas != null && doc.Canvas.Enabled);

        var renderer = new DocumentRenderer();
        var rendered = renderer.Render(doc);

        var expectedFrame = CanvasLayout.Compute(200, 150, vm.Style);
        Assert.Equal(expectedFrame.Width, rendered.PixelWidth);
        Assert.Equal(expectedFrame.Height, rendered.PixelHeight);
        Assert.Equal(280, rendered.PixelWidth);
        Assert.Equal(230, rendered.PixelHeight);
    }

    [Fact]
    [Unit]
    public void OptionsPersistButToggleDoesNot()
    {
        var store = new LocalTestSettingsStore();

        // Session 1: configure options and enable
        var doc1 = new AnnotationDocument(CreateTestImage(200, 150));
        var vm1 = new CanvasPanelViewModel(doc1, store)
        {
            Aspect = AspectPreset.SixteenNine,
            Padding = 60,
            CornerRadius = 24,
            Shadow = 35,
            IsEnabled = true
        };

        Assert.True(vm1.IsEnabled);

        // Session 2: reopen with same store
        var doc2 = new AnnotationDocument(CreateTestImage(200, 150));
        var vm2 = new CanvasPanelViewModel(doc2, store);

        // User decision: toggle starts strictly OFF at reopen
        Assert.False(vm2.IsEnabled);
        Assert.True(doc2.Canvas == null || !doc2.Canvas.Enabled);

        // Options persisted
        Assert.Equal(AspectPreset.SixteenNine, vm2.Aspect);
        Assert.Equal(60, vm2.Padding);
        Assert.Equal(24, vm2.CornerRadius);
        Assert.Equal(35, vm2.Shadow);
    }

    [Fact]
    [Unit]
    public void CorruptStoredCanvasFallsBackToDefaults()
    {
        var store = new LocalTestSettingsStore();
        store.SetSetting(CanvasPanelViewModel.SettingsKey, "{ invalid json missing braces ");

        var doc = new AnnotationDocument(CreateTestImage(200, 150));
        var vm = new CanvasPanelViewModel(doc, store);

        Assert.False(vm.IsEnabled);
        Assert.Equal(AspectPreset.Auto, vm.Aspect);
        Assert.Equal(40, vm.Padding);
        Assert.Equal(12, vm.CornerRadius);
        Assert.Equal(20, vm.Shadow);
        Assert.Equal(1920, vm.TargetWidth);
        Assert.Equal(1080, vm.TargetHeight);
    }

    [Fact]
    [Unit]
    public void ChangingAnOptionUpdatesTheDocument()
    {
        var doc = new AnnotationDocument(CreateTestImage(200, 150));
        var store = new LocalTestSettingsStore();
        var vm = new CanvasPanelViewModel(doc, store);

        int changedEvents = 0;
        vm.CanvasChanged += () => changedEvents++;

        vm.Padding = 55;
        Assert.True(changedEvents > 0);
        Assert.NotNull(vm.Document.Canvas);
        Assert.Equal(55, vm.Document.Canvas.Padding);

        vm.Aspect = AspectPreset.Square;
        Assert.Equal(AspectPreset.Square, vm.Document.Canvas.Aspect);

        vm.CornerRadius = 18;
        Assert.Equal(18, vm.Document.Canvas.CornerRadius);

        string? saved = store.GetSetting(CanvasPanelViewModel.SettingsKey);
        Assert.NotNull(saved);
        Assert.Contains("55", saved);
    }

    [Fact]
    [Unit]
    public void CopyWithCanvasCopiesTheCanvasSize()
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

        editorVm.Copy();

        Assert.Single(sink.Copied);
        Assert.Equal(280, sink.Copied[0].PixelWidth);
        Assert.Equal(230, sink.Copied[0].PixelHeight);
    }

    [Fact]
    [Unit]
    public void ResetDefaultsRestoresOptionsAndKeepsEnabledState()
    {
        var doc = new AnnotationDocument(CreateTestImage(200, 150));
        var store = new LocalTestSettingsStore();
        var vm = new CanvasPanelViewModel(doc, store)
        {
            IsEnabled = true,
            Padding = 90,
            CornerRadius = 40
        };

        vm.ResetDefaults();

        Assert.True(vm.IsEnabled);
        Assert.Equal(40, vm.Padding);
        Assert.Equal(12, vm.CornerRadius);
        Assert.Equal(20, vm.Shadow);
        Assert.Equal(AspectPreset.Auto, vm.Aspect);
    }

    [Fact]
    [Unit]
    public void ChangingAnOptionTurnsTheCanvasOn()
    {
        var doc = new AnnotationDocument(CreateTestImage(200, 150));
        var store = new LocalTestSettingsStore();
        var vm = new CanvasPanelViewModel(doc, store);

        Assert.False(vm.IsEnabled);

        vm.Padding = 50;

        Assert.True(vm.IsEnabled);
        Assert.NotNull(doc.Canvas);
        Assert.True(doc.Canvas.Enabled);
    }

    [Fact]
    [Unit]
    public void ResetDefaultsDoesNotTurnTheCanvasOn()
    {
        var doc = new AnnotationDocument(CreateTestImage(200, 150));
        var store = new LocalTestSettingsStore();
        var vm = new CanvasPanelViewModel(doc, store);

        Assert.False(vm.IsEnabled);

        vm.ResetDefaults();

        Assert.False(vm.IsEnabled);
        Assert.True(doc.Canvas == null || !doc.Canvas.Enabled);
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
