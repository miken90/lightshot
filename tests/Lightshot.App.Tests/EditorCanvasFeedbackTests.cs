// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lightshot.App.Views.Editor;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;
using Point = Lightshot.Core.Point;
using Size = System.Windows.Size;

namespace Lightshot.App.Tests;

/// <summary>
/// What the editor canvas shows while a region is dragged out, and where the crop
/// frame lands after the crop is reset or reverted.
/// </summary>
public class EditorCanvasFeedbackTests
{
    private const int ImageWidth = 200;
    private const int ImageHeight = 150;

    private static void RunInSta(Action action)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        captured?.Throw();
    }

    private static EditorViewModel CreateViewModel(SKColor fill)
    {
        using var bmp = new SKBitmap(new SKImageInfo(ImageWidth, ImageHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(fill);
        }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        var doc = new AnnotationDocument(new CapturedImage(ImageWidth, ImageHeight, data.ToArray()));
        return new EditorViewModel(doc, new TestImageSink(), new DocumentRenderer(), null, new TestSettingsStore());
    }

    private static Color PixelAt(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static bool IsAccent(Color c) => c.A > 100 && c.B > 150 && c.B > c.R + 80;

    [Theory]
    [Render]
    [InlineData(EditorTool.Focus, 96.0)]
    [InlineData(EditorTool.Focus, 144.0)]
    [InlineData(EditorTool.Redact, 96.0)]
    [InlineData(EditorTool.Redact, 144.0)]
    public void DraggingFocusOrRedactDrawsAnOutline(EditorTool tool, double dpi)
    {
        RunInSta(() =>
        {
            // A flat dark capture: a blur or pixelate patch over it is invisible, like the reported chat window.
            var vm = CreateViewModel(new SKColor(24, 26, 30));
            using var host = new CanvasHost { ViewModel = vm };
            host.Measure(new Size(ImageWidth, ImageHeight));
            host.Arrange(new System.Windows.Rect(0, 0, ImageWidth, ImageHeight));

            vm.ActiveTool = tool;
            vm.GestureStarted(new Point(20, 20));
            vm.GestureMoved(new Point(120, 100));

            var adorner = new SelectionAdorner(host, vm);
            adorner.Measure(new Size(ImageWidth, ImageHeight));
            adorner.Arrange(new System.Windows.Rect(0, 0, ImageWidth, ImageHeight));

            double scale = dpi / 96.0;
            var target = new RenderTargetBitmap(
                (int)Math.Ceiling(ImageWidth * scale), (int)Math.Ceiling(ImageHeight * scale), dpi, dpi, PixelFormats.Pbgra32);
            target.Render(adorner);

            bool OutlineNear(double x, double y)
            {
                int px = (int)Math.Round(x * scale);
                int py = (int)Math.Round(y * scale);
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (IsAccent(PixelAt(target, px + dx, py + dy))) return true;
                    }
                }
                return false;
            }

            // All four edges of the dragged rect (20,20)-(120,100) carry the outline.
            Assert.True(OutlineNear(20, 60), "left edge has no outline");
            Assert.True(OutlineNear(120, 60), "right edge has no outline");
            Assert.True(OutlineNear(70, 20), "top edge has no outline");
            Assert.True(OutlineNear(70, 100), "bottom edge has no outline");
            // It is an outline, not a fill.
            Assert.Equal(0, PixelAt(target, (int)(70 * scale), (int)(60 * scale)).A);

            vm.GestureEnded(new Point(120, 100));
        });
    }

    [Fact]
    [Render]
    public void DraggingFocusDimsAroundTheDraft()
    {
        RunInSta(() =>
        {
            var vm = CreateViewModel(SKColors.White);
            using var host = new CanvasHost { ViewModel = vm };

            vm.ActiveTool = EditorTool.Focus;
            vm.GestureStarted(new Point(20, 20));
            vm.GestureMoved(new Point(120, 100));

            var backing = host.BackingBitmap;
            Assert.NotNull(backing);
            Assert.True(backing.GetPixel(5, 5).Red < 200, "outside the focus draft is not dimmed");
            Assert.Equal(255, backing.GetPixel(70, 60).Red);

            vm.GestureEnded(new Point(120, 100));
        });
    }

    private static void Layout(EditorWindow window)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1600, 1200));
        root.Arrange(new System.Windows.Rect(0, 0, 1600, 1200));
        root.UpdateLayout();
    }

    private static void AssertFrameOnImageBounds(EditorWindow window, EditorViewModel vm, double zoom)
    {
        Layout(window);
        var host = window.MainCanvasHost;
        var container = window.CanvasContainer;

        Assert.Equal(vm.Document.ImageBounds, vm.CropFrame);
        Assert.Equal(ImageWidth * zoom, host.ActualWidth, 3);
        Assert.Equal(ImageHeight * zoom, host.ActualHeight, 3);
        // The centred container must be exactly the canvas, or the canvas spills past its right edge.
        Assert.Equal(host.ActualWidth, container.ActualWidth, 3);
        Assert.Equal(host.ActualHeight, container.ActualHeight, 3);

        var frame = vm.CropFrame!.Value;
        var topLeft = host.ImageToScreen(new Point(frame.MinX, frame.MinY));
        var bottomRight = host.ImageToScreen(new Point(frame.MaxX, frame.MaxY));
        Assert.Equal(0, topLeft.X, 3);
        Assert.Equal(0, topLeft.Y, 3);
        Assert.Equal(host.ActualWidth, bottomRight.X, 3);
        Assert.Equal(host.ActualHeight, bottomRight.Y, 3);
    }

    private static string Describe(EditorWindow window, EditorViewModel vm) =>
        $"visible={vm.Document.VisibleFrame} crop={vm.CropFrame} canvas={window.MainCanvasHost.Width}/{window.MainCanvasHost.ActualWidth} " +
        $"container={window.CanvasContainer.Width}/{window.CanvasContainer.ActualWidth}";

    private static (EditorWindow Window, EditorViewModel ViewModel) CroppedEditor(double zoom)
    {
        var vm = CreateViewModel(SKColors.White);
        var window = new EditorWindow();
        window.MainCanvasHost.Zoom = zoom;
        window.InitializeViewModel(vm);
        Layout(window);

        // Drag the crop frame's top-left handle in, leaving the right half of the image.
        vm.ActiveTool = EditorTool.Crop;
        vm.GestureStarted(new Point(0, 0));
        vm.GestureMoved(new Point(100, 75));
        vm.GestureEnded(new Point(100, 75));
        Layout(window);

        Assert.True(Math.Abs(100 * zoom - window.MainCanvasHost.ActualWidth) < 0.001, Describe(window, vm));
        Assert.True(Math.Abs(window.MainCanvasHost.ActualWidth - window.CanvasContainer.ActualWidth) < 0.001, Describe(window, vm));
        return (window, vm);
    }

    [Theory]
    [Unit]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void CropResetPutsTheFrameOnTheImageBounds(double zoom)
    {
        RunInSta(() =>
        {
            var (window, vm) = CroppedEditor(zoom);
            try
            {
                vm.ResetCrop();
                AssertFrameOnImageBounds(window, vm, zoom);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [Unit]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void CropRevertPutsTheFrameOnTheImageBounds(double zoom)
    {
        RunInSta(() =>
        {
            var (window, vm) = CroppedEditor(zoom);
            try
            {
                vm.Undo();
                AssertFrameOnImageBounds(window, vm, zoom);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
