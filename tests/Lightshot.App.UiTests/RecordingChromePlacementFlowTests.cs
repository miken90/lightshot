// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using Lightshot.App.Views.Recording;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Interop;
using Lightshot.TestSupport;
using Lightshot.Platform.Windows.Settings;
using Xunit;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.App.UiTests;

/// <summary>
/// Opens the real recording toolbar, controls pill and post-recording overlay for a region on every attached
/// monitor and checks their on-screen rects against that monitor's work area, then moves to the next monitor
/// the way a later take would.
/// </summary>
public class RecordingChromePlacementFlowTests
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const double TolerancePx = 1.0;

    private readonly ITestOutputHelper _output;

    public RecordingChromePlacementFlowTests(ITestOutputHelper output) => _output = output;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

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

    private sealed class NoopMediaSink : IMediaSink
    {
        public void CopyFile(string path) { }
        public void Save(string sourcePath, string destinationPath) { }
        public void Trash(string path) { }
        public void Delete(string path) { }
        public void Copy(string sourcePath, string destinationPath) { }
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
    public void RecordingChromeOpensInsideTheWorkAreaOfTheRecordedMonitor()
    {
        ExceptionDispatchInfo? captured = null;
        var settingsPath = Path.Combine(Path.GetTempPath(), "Lightshot_ChromeTest_" + Guid.NewGuid().ToString("N") + ".json");
        var thread = new Thread(() =>
        {
            try
            {
                Win32Window.EnsurePerMonitorDpiV2();
                var settings = new JsonSettingsStore(settingsPath);
                var displays = DisplayTopology.GetDisplays();
                // Visit every monitor, then the first again: a later take must move the chrome back.
                var order = displays.Concat(displays.Take(1)).ToList();
                foreach (var display in order)
                {
                    var c = display.Bounds.Center;
                    var region = new CaptureRegion.RectRegion(new Rect(c.X - 200, c.Y - 150, 400, 300));
                    _output.WriteLine($"{display.DeviceName} primary={display.IsPrimary} bounds={display.Bounds} work={display.WorkArea} scale={display.ScaleFactor}");

                    var (toolbar, _) = RecordingToolbarWindow.Open(region, new RecordingDefaults(), Array.Empty<AudioInputDevice>(), false, settings);
                    try
                    {
                        Pump(400);
                        var toolbarFrame = VisibleFrame(new WindowInteropHelper(toolbar).Handle);
                        _output.WriteLine($"  toolbar {toolbarFrame}");
                        AssertInside(toolbarFrame, display.WorkArea, $"Toolbar on {display.DeviceName}");
                    }
                    finally
                    {
                        toolbar.Close();
                        Pump(100);
                    }

                    foreach (var top in new[] { true, false })
                    {
                        var pill = new ControlsPill(new ControlsPillViewModel(hasMicrophone: false));
                        try
                        {
                            pill.ShowActivated = false;
                            pill.Show();
                            pill.PositionPill(top, region);
                            Pump(400);
                            var pillFrame = VisibleFrame(new WindowInteropHelper(pill).Handle);
                            _output.WriteLine($"  pill(top={top}) {pillFrame}");
                            AssertInside(pillFrame, display.WorkArea, $"Pill on {display.DeviceName}");
                        }
                        finally
                        {
                            pill.Close();
                            Pump(100);
                        }
                    }

                    var overlay = new PostRecordingOverlay(new PostRecordingOverlayViewModel(
                        new PendingRecording("missing.mp4", RecordingOutputKind.Video, 1.0),
                        new NoopMediaSink()))  { RecordedRegion = region };
                    try
                    {
                        overlay.ShowActivated = false;
                        overlay.Show();
                        Pump(400);
                        var overlayFrame = VisibleFrame(new WindowInteropHelper(overlay).Handle);
                        _output.WriteLine($"  overlay {overlayFrame}");
                        AssertInside(overlayFrame, display.WorkArea, $"Post-recording overlay on {display.DeviceName}");
                    }
                    finally
                    {
                        overlay.Close();
                        Pump(100);
                    }
                }
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                try { File.Delete(settingsPath); } catch { }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        captured?.Throw();
    }
}
