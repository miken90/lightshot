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

public class EditorViewModelTests
{
    private static CapturedImage CreateTestImage(int width = 200, int height = 150)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

    private static (EditorViewModel ViewModel, TestImageSink Sink, TestSettingsStore Settings) CreateFixture(
        int width = 200, int height = 150)
    {
        var doc = new AnnotationDocument(CreateTestImage(width, height));
        var sink = new TestImageSink();
        var settings = new TestSettingsStore();
        var vm = new EditorViewModel(doc, sink, new DocumentRenderer(), null, settings);
        return (vm, sink, settings);
    }

    [Fact]
    [Unit]
    public void ToolPersistsAndNewMarkIsNotSelected()
    {
        var (vm, _, _) = CreateFixture();

        // 1. Select Rectangle tool
        vm.ActiveTool = EditorTool.Rectangle;
        Assert.Equal(EditorTool.Rectangle, vm.ActiveTool);

        // 2. Draw a rectangle via gesture
        vm.GestureStarted(new Point(10, 10));
        vm.GestureMoved(new Point(60, 50));
        vm.GestureEnded(new Point(60, 50));

        // Acceptance asserts:
        // Tool persists
        Assert.Equal(EditorTool.Rectangle, vm.ActiveTool);

        // Document has 1 element
        Assert.Single(vm.Document.Elements);
        var created = vm.Document.Elements[0];
        Assert.IsType<AnnotationElement.Kind.Rectangle>(created.ElementKind);

        // New mark is NOT selected
        Assert.Null(vm.Document.SelectedID);
        Assert.False(vm.HasSelection);

        // Any tool grabs an existing mark
        vm.GestureStarted(new Point(10, 10)); // Click on rectangle boundary
        Assert.True(vm.HasSelection);
        Assert.Equal(created.Id, vm.Document.SelectedID);
        vm.GestureEnded(new Point(10, 10));
    }

    [Fact]
    [Unit]
    public void CopyAndCloseWritesNoFile()
    {
        var (vm, sink, _) = CreateFixture();
        bool closeRequested = false;
        vm.RequestClose += () => closeRequested = true;

        vm.CopyAndClose();

        // Clipboard was written to
        Assert.Single(sink.Copied);

        // No file was written
        Assert.Empty(sink.Written);

        // Close event was fired
        Assert.True(closeRequested);
    }

    [Fact]
    [Unit]
    public void RedactionDefaultsToPixelate()
    {
        var (vm, _, _) = CreateFixture();

        // 1. Switch to Redact tool -> defaults to Pixelate
        vm.ActiveTool = EditorTool.Redact;
        Assert.Equal(RedactionStyle.Pixelate, vm.RedactionStyle);

        // 2. Change style to Blur
        vm.RedactionStyle = RedactionStyle.Blur;
        Assert.Equal(RedactionStyle.Blur, vm.RedactionStyle);

        // 3. Switch to another tool
        vm.ActiveTool = EditorTool.Arrow;
        Assert.Equal(EditorTool.Arrow, vm.ActiveTool);

        // 4. Switch back to Redact tool -> must default back to Pixelate on each open
        vm.ActiveTool = EditorTool.Redact;
        Assert.Equal(RedactionStyle.Pixelate, vm.RedactionStyle);

        // 5. Change style to Blackout, switch to Select, switch back to Redact
        vm.RedactionStyle = RedactionStyle.Blackout;
        Assert.Equal(RedactionStyle.Blackout, vm.RedactionStyle);

        vm.ActiveTool = EditorTool.Select;
        vm.ActiveTool = EditorTool.Redact;
        Assert.Equal(RedactionStyle.Pixelate, vm.RedactionStyle);
    }

    [Fact]
    [Unit]
    public void WindowSizeCappedToWorkArea()
    {
        // 1. Standard full HD work area: 1920x1080 -> 1180x800 (capped at defaults)
        // 1920 * 0.90 = 1728 > 1180 => 1180
        // 1080 * 0.85 = 918 > 800 => 800
        var (w1, h1) = EditorViewModel.CalculateWindowSize(1920, 1080);
        Assert.Equal(1180.0, w1);
        Assert.Equal(800.0, h1);

        // 2. Small work area: 1000x700 -> 900x595 (capped by work area ratio)
        // 1000 * 0.90 = 900 < 1180 => 900
        // 700 * 0.85 = 595 < 800 => 595
        var (w2, h2) = EditorViewModel.CalculateWindowSize(1000, 700);
        Assert.Equal(900.0, w2);
        Assert.Equal(595.0, h2);

        // 3. Intermediate work area: 1200x800 -> 1080x680
        // 1200 * 0.90 = 1080 < 1180 => 1080
        // 800 * 0.85 = 680 < 800 => 680
        var (w3, h3) = EditorViewModel.CalculateWindowSize(1200, 800);
        Assert.Equal(1080.0, w3);
        Assert.Equal(680.0, h3);
    }

    [Fact]
    [Unit]
    public void ArrowStylePersistsViaSettingsStore()
    {
        var (vm1, sink, settings) = CreateFixture();

        // Default arrow style is Standard
        Assert.Equal(ArrowStyle.Standard, vm1.ArrowStyle);

        // Change arrow style to Curved
        vm1.ArrowStyle = ArrowStyle.Curved;
        Assert.Equal(ArrowStyle.Curved, vm1.ArrowStyle);

        // Check settings store received the setting
        var stored = settings.GetSetting(EditorViewModel.ArrowStyleDefaultsKey);
        Assert.Equal(((int)ArrowStyle.Curved).ToString(), stored);

        // Create new ViewModel with the same settings store
        var doc2 = new AnnotationDocument(CreateTestImage(200, 150));
        var vm2 = new EditorViewModel(doc2, sink, new DocumentRenderer(), null, settings);

        // Remembers last style
        Assert.Equal(ArrowStyle.Curved, vm2.ArrowStyle);
    }

    [Fact]
    [Unit]
    public void UndoRedoAndZOrderWork()
    {
        var (vm, _, _) = CreateFixture();

        vm.ActiveTool = EditorTool.Line;
        vm.GestureStarted(new Point(0, 0));
        vm.GestureEnded(new Point(50, 50));
        Assert.Single(vm.Document.Elements);

        vm.ActiveTool = EditorTool.Ellipse;
        vm.GestureStarted(new Point(60, 60));
        vm.GestureEnded(new Point(100, 100));
        Assert.Equal(2, vm.Document.Elements.Count);

        // Undo
        Assert.True(vm.CanUndo);
        vm.Undo();
        Assert.Single(vm.Document.Elements);

        // Redo
        Assert.True(vm.CanRedo);
        vm.Redo();
        Assert.Equal(2, vm.Document.Elements.Count);

        // Z-order: select first element and bring forward
        var firstId = vm.Document.Elements[0].Id;
        vm.Document.Select(firstId);
        vm.BringForward();
        Assert.Equal(firstId, vm.Document.Elements[1].Id);
    }

    [Fact]
    [Unit]
    public void CopyAndSaveAsOutputActions()
    {
        var (vm, sink, _) = CreateFixture();

        // Copy (stay)
        vm.Copy();
        Assert.Single(sink.Copied);
        Assert.Empty(sink.Written);

        // Save As
        vm.SaveAs("test.png", new ImageFormat.Png());
        Assert.Single(sink.Written);
        Assert.Equal("test.png", sink.Written[0].Path);
    }

    [Fact]
    [Unit]
    public void ArrowStyleRoundTripsAcrossJsonSettingsStoreInstances()
    {
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lightshot_test_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempDir);
        string settingsFile = System.IO.Path.Combine(tempDir, "settings.json");

        try
        {
            // 1. First instance: Set arrow style via EditorViewModel
            var store1 = new Lightshot.Platform.Windows.Settings.JsonSettingsStore(settingsFile);
            var doc1 = new AnnotationDocument(CreateTestImage());
            var vm1 = new EditorViewModel(doc1, new TestImageSink(), new DocumentRenderer(), null, store1);

            // Change arrow style
            vm1.ArrowStyle = ArrowStyle.Curved;

            // Assert store1 recorded the setting
            Assert.Equal(((int)ArrowStyle.Curved).ToString(), store1.GetSetting(EditorViewModel.ArrowStyleDefaultsKey));

            // 2. Second instance: Read setting from fresh JsonSettingsStore
            var store2 = new Lightshot.Platform.Windows.Settings.JsonSettingsStore(settingsFile);
            var doc2 = new AnnotationDocument(CreateTestImage());
            var vm2 = new EditorViewModel(doc2, new TestImageSink(), new DocumentRenderer(), null, store2);

            // Assert EditorViewModel restored the persisted arrow style
            Assert.Equal(ArrowStyle.Curved, vm2.ArrowStyle);
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, true);
            }
        }
    }
}

public class TestImageSink : IImageSink
{
    public List<RenderedImage> Copied { get; } = [];
    public List<(RenderedImage Image, string Path, ImageFormat Format)> Written { get; } = [];
    public List<string> CopiedText { get; } = [];

    public void CopyToClipboard(RenderedImage image) => Copied.Add(image);
    public void CopyText(string text) => CopiedText.Add(text);
    public void Write(RenderedImage image, string destinationPath, ImageFormat format) =>
        Written.Add((image, destinationPath, format));
}

public class TestSettingsStore : ISettingsStore
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
