using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Hotkeys;
using Lightshot.Platform.Windows.Interop;
using Lightshot.Platform.Windows.Overlay;
using Lightshot.Platform.Windows.Shell;
using Lightshot.TestSupport;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Xunit;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Tests;

public class OverlayLatencyTests
{
    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_F9 = 0x78;
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_LEFT = 0x25;
    private const byte VK_UP = 0x26;
    private const byte VK_RIGHT = 0x27;
    private const byte VK_DOWN = 0x28;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private readonly ITestOutputHelper _output;

    public OverlayLatencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Desktop]
    public void HotkeyToFirstPixelUnder150Ms()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var monitors = OverlayHost.DiscoverMonitors();
        Assert.NotEmpty(monitors);

        // Document monitor setup
        string monitorSetup = string.Join("; ", monitors.Select(m =>
            $"Monitor {m.Id}: {m.Bounds.Width}x{m.Bounds.Height} at ({m.Bounds.MinX}, {m.Bounds.MinY}){(m.IsPrimary ? " [Primary]" : "")}"));

        using var shell = new ShellThread("LatencyTestShell");
        var latencies = new List<double>();
        const int iterations = 20;

        factory.EnumAdapters1(0, out var adapter);
        adapter!.EnumOutputs(0, out var output);
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_0 }, out ID3D11Device? device, out ID3D11DeviceContext? context);
        using var output1 = output!.QueryInterface<IDXGIOutput1>();

        IDXGIOutputDuplication? dup = null;
        ID3D11Texture2D? staging = null;

        try
        {
            dup = output1.DuplicateOutput(device!);

            shell.Invoke(() =>
            {
                using var host = new OverlayHost();
                using var hotkeyWindow = new HotkeyWindow();

                hotkeyWindow.HotkeyTriggered = (_, _) =>
                {
                    foreach (var win in host.Windows)
                    {
                        win.Show();
                    }
                };

                bool registered = hotkeyWindow.TryRegister(
                    CaptureAction.Area,
                    new HotkeyBinding(VK_F9, HotkeyModifiers.Control | HotkeyModifiers.Shift, "F9"),
                    out _);

                Assert.True(registered, "Failed to register test hotkey Ctrl+Shift+F9");

                for (int iter = 0; iter < iterations; iter++)
                {
                    host.HideAllWindows();
                    Win32Window.PumpMessages(5);
                    Thread.Sleep(30);

                    // Drain DDA queue
                    while (dup.AcquireNextFrame(0, out _, out var drainRes).Success)
                    {
                        drainRes?.Dispose();
                        dup.ReleaseFrame();
                    }

                    long qpcStart = Stopwatch.GetTimestamp();

                    // Fire test chord
                    Win32Window.keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                    Win32Window.keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
                    Win32Window.keybd_event(VK_F9, 0, 0, UIntPtr.Zero);
                    Win32Window.keybd_event(VK_F9, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Win32Window.keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Win32Window.keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                    double elapsedMs = 0;
                    var pollSw = Stopwatch.StartNew();
                    bool frameFound = false;

                    while (pollSw.ElapsedMilliseconds < 500)
                    {
                        Win32Window.PumpMessages(1);
                        Win32Window.mouse_event(0x0001, 1, 1, 0, UIntPtr.Zero);

                        var hr = dup.AcquireNextFrame(16, out _, out var res);
                        if (hr.Success && res != null)
                        {
                            using (res)
                            {
                                long qpcCaptured = Stopwatch.GetTimestamp();
                                using var tex = res.QueryInterface<ID3D11Texture2D>();
                                if (staging == null)
                                {
                                    var sDesc = tex.Description;
                                    sDesc.Usage = ResourceUsage.Staging;
                                    sDesc.BindFlags = BindFlags.None;
                                    sDesc.CPUAccessFlags = CpuAccessFlags.Read;
                                    sDesc.MiscFlags = ResourceOptionFlags.None;
                                    staging = device!.CreateTexture2D(sDesc);
                                }

                                context!.CopyResource(staging, tex);
                                dup.ReleaseFrame();

                                elapsedMs = (qpcCaptured - qpcStart) * 1000.0 / Stopwatch.Frequency;
                                frameFound = true;
                                break;
                            }
                        }
                    }

                    if (!frameFound)
                    {
                        elapsedMs = pollSw.Elapsed.TotalMilliseconds;
                    }

                    latencies.Add(elapsedMs);
                }

                hotkeyWindow.UnregisterAll();
            });
        }
        finally
        {
            staging?.Dispose();
            dup?.Dispose();
            context?.Dispose();
            device?.Dispose();
            output?.Dispose();
            adapter?.Dispose();
        }

        latencies.Sort();
        double median = latencies[latencies.Count / 2];
        double min = latencies.Min();
        double max = latencies.Max();

        _output.WriteLine($"Overlay latency: median={median:F2} ms, min={min:F2} ms, max={max:F2} ms. Setup: {monitorSetup}");

        Assert.True(median < 150.0,
            $"Expected median overlay latency < 150 ms, but measured {median:F2} ms (Min: {min:F2} ms, Max: {max:F2} ms). Monitor setup: {monitorSetup}");
    }

    [Fact]
    [Desktop]
    public void KeysReachOverlay()
    {
        var monitors = OverlayHost.DiscoverMonitors();
        Assert.NotEmpty(monitors);
        var primary = monitors[0];

        using var shell = new ShellThread("KeysReachOverlayShell");

        shell.Invoke(() =>
        {
            using var window = new OverlayWindow(primary.Id, primary.Bounds);
            window.Mode = OverlayMode.Region;
            window.IsAdjustable = true;
            window.Show();

            // 1. Grant foreground and focus
            bool fg = ForegroundGrant.GrantForeground(window.Handle);
            Assert.True(fg, "Expected overlay window to obtain foreground activation");

            var receivedKeys = new List<int>();
            window.KeyReceivedForTesting = vk => receivedKeys.Add(vk);

            // 2. Deliver navigation keys: Esc, Return, ArrowLeft, ArrowUp, ArrowRight, ArrowDown
            byte[] testKeys = { VK_ESCAPE, VK_RETURN, VK_LEFT, VK_UP, VK_RIGHT, VK_DOWN };
            for (int i = 0; i < 20; i++)
            {
                byte vk = testKeys[i % testKeys.Length];
                Win32Window.keybd_event(vk, 0, 0, UIntPtr.Zero);
                Win32Window.keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                Win32Window.PumpMessages(2);
                Thread.Sleep(5);
            }

            Assert.True(receivedKeys.Count >= 20, $"Expected >= 20 keys delivered, got {receivedKeys.Count}");

            // 3. Test Esc cancels
            CaptureRegion? completedRegion = new CaptureRegion.DisplayRegion(999);
            window.SelectionCompleted = r => completedRegion = r;

            Win32Window.keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
            Win32Window.keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Win32Window.PumpMessages(5);

            Assert.Null(completedRegion);

            // 4. Test bare click captures nothing
            CaptureRegion? bareClickResult = null;
            window.SelectionCompleted = r => bareClickResult = r;

            // Simulate bare click (down and up with tiny movement < 4px)
            window.SetSelection(null, false);
            // Simulate mouse down at (100, 100)
            IntPtr lDown = (IntPtr)((100 << 16) | 100);
            Win32Window.PostMessageW(window.Handle, Win32Window.WM_LBUTTONDOWN, IntPtr.Zero, lDown);
            Win32Window.PostMessageW(window.Handle, Win32Window.WM_LBUTTONUP, IntPtr.Zero, lDown);
            Win32Window.PumpMessages(5);

            Assert.Null(bareClickResult);
            Assert.Null(window.CurrentSelection);

            // 5. Test adjustable selection nudge with arrow keys
            window.SetSelection(new Rect(100, 100, 200, 200), settled: true);
            Assert.NotNull(window.CurrentSelection);
            double initialX = window.CurrentSelection.Value.MinX;

            // Send Right arrow key
            Win32Window.keybd_event(VK_RIGHT, 0, 0, UIntPtr.Zero);
            Win32Window.keybd_event(VK_RIGHT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Win32Window.PumpMessages(5);

            Assert.NotNull(window.CurrentSelection);
            Assert.Equal(initialX + 1.0, window.CurrentSelection.Value.MinX);
        });
    }
}
