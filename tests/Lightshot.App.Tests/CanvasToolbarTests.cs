// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Lightshot.App.Views.Editor;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.App.Tests;

[Trait("Tier", "Unit")]
public class CanvasToolbarTests
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
            canvas.Clear(SKColors.Red);
        }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

    [Fact]
    [Unit]
    public void CanvasButtonsHaveAutomationIds()
    {
        RunInSta(() =>
        {
            var doc = new AnnotationDocument(CreateTestImage(100, 100));
            var vm = new EditorViewModel(doc, new LocalTestImageSink());
            var window = new EditorWindow(vm);

            var toggle = window.FindName("CanvasToggleButton") as ToggleButton;
            var options = window.FindName("CanvasOptionsButton") as Button;

            Assert.NotNull(toggle);
            Assert.NotNull(options);

            Assert.Equal("CanvasToggle", AutomationProperties.GetAutomationId(toggle));
            Assert.Equal("CanvasOptionsButton", AutomationProperties.GetAutomationId(options));

            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(toggle)));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(options)));
        });
    }

    [Fact]
    [Unit]
    public void OpeningCanvasOptionsTurnsTheCanvasOn()
    {
        RunInSta(() =>
        {
            var doc = new AnnotationDocument(CreateTestImage(100, 100));
            var vm = new EditorViewModel(doc, new LocalTestImageSink());
            var window = new EditorWindow(vm);

            var toggle = window.FindName("CanvasToggleButton") as ToggleButton;
            var options = window.FindName("CanvasOptionsButton") as Button;

            Assert.NotNull(toggle);
            Assert.NotNull(options);
            Assert.False(toggle.IsChecked);

            options.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.True(toggle.IsChecked);
        });
    }

    private sealed class LocalTestImageSink : IImageSink
    {
        public void CopyToClipboard(RenderedImage image) { }
        public void CopyText(string text) { }
        public void Write(RenderedImage image, string destinationPath, ImageFormat format) { }
    }
}
