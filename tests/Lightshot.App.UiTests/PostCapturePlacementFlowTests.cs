// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using Lightshot.App.Views.Editor;
using Lightshot.App.Views.QuickAccess;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Interop;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.App.UiTests;

/// <summary>
/// Opens the real card and editor windows with the pointer at the centre of every attached
/// monitor (always the primary) and checks their on-screen rects against that monitor's work area.
/// </summary>
public class PostCapturePlacementFlowTests
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const double TolerancePx = 1.0;

    private readonly ITestOutputHelper _output;

    public PostCapturePlacementFlowTests(ITestOutputHelper output) => _output = output;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

    private static Rect VisibleFrame(IntPtr hwnd)
    {
        // The extended frame excludes the invisible resize borders that GetWindowRect includes.
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) != 0)
        {
            GetWindowRect(hwnd, out r);
        }
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private static void Pump(int milliseconds)
    {
        // Runs the dispatcher so WPF applies layout and window positions before rects are read.
        var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(15);
        }
    }

    private static CapturedImage CreateTestImage(int width, int height)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.DarkSeaGreen);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

    private static void AssertInside(Rect frame, Rect workArea, string what)
    {
        Assert.True(
            frame.MinX >= workArea.MinX - TolerancePx && frame.MinY >= workArea.MinY - TolerancePx &&
            frame.MaxX <= workArea.MaxX + TolerancePx && frame.MaxY <= workArea.MaxY + TolerancePx,
            $"{what} at {frame} is not inside work area {workArea}.");
    }

    [Fact]
    [Desktop]
    public void CardAndEditorOpenInsideTheWorkAreaOfEveryMonitor()
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                Win32Window.EnsurePerMonitorDpiV2();
                foreach (var display in DisplayTopology.GetDisplays())
                {
                    var center = display.Bounds.Center;
                    _output.WriteLine($"{display.DeviceName} primary={display.IsPrimary} bounds={display.Bounds} work={display.WorkArea} scale={display.ScaleFactor}");

                    var settings = new QuickAccessSettings(QuickAccessSide.Left, QuickAccessAutoClose.Never);
                    using (var host = new QuickAccessHost(settings: () => settings, pointerProvider: () => center))
                    {
                        host.Present(CreateTestImage(1200, 800));
                        Pump(400);
                        var card = (CardWindow)host.ActiveWindows.Values.Single();
                        var cardFrame = VisibleFrame(new WindowInteropHelper(card).Handle);
                        _output.WriteLine($"  card   {cardFrame}");
                        AssertInside(cardFrame, display.WorkArea, $"Card on {display.DeviceName}");
                        host.CloseAll();
                        Pump(100);
                    }

                    var editor = new EditorWindow();
                    try
                    {
                        editor.PlaceOnDisplayAt(center);
                        editor.ShowActivated = false;
                        editor.Show();
                        Pump(400);
                        var editorFrame = VisibleFrame(new WindowInteropHelper(editor).Handle);
                        _output.WriteLine($"  editor {editorFrame}");
                        AssertInside(editorFrame, display.WorkArea, $"Editor on {display.DeviceName}");
                    }
                    finally
                    {
                        editor.Close();
                        Pump(100);
                    }
                }
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
}
