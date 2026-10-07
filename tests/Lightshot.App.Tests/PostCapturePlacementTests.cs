// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using Lightshot.App.Views.Editor;
using Lightshot.App.Views.QuickAccess;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.TestSupport;
using Xunit;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.App.Tests;

/// <summary>
/// The Quick Access card and the editor open inside the work area of the capture's monitor on
/// mixed-DPI desktops. WPF maps every window's DIPs to device pixels with the window's own scale
/// (the primary's at creation), so the expected physical frame is the DIP frame times that scale.
/// </summary>
public class PostCapturePlacementTests
{
    private static DisplayInfo Display(uint id, Rect bounds, double scale, bool primary, double taskbar = 48)
    {
        var workArea = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height - taskbar);
        uint dpi = (uint)Math.Round(96 * scale);
        return new DisplayInfo(id, $@"\\.\DISPLAY{id}", (IntPtr)id, bounds, workArea, scale, dpi, dpi, primary, 0);
    }

    // (displays, the device scale WPF gives new windows: the primary's)
    private static (IReadOnlyList<DisplayInfo> Displays, double WindowScale) Topology(string name) => name switch
    {
        // The reporting desktop: 150% primary, 100% secondaries to the right and at a negative X.
        "150-primary-100-right-and-left" => (new[]
        {
            Display(1, new Rect(0, 0, 2560, 1600), 1.5, true, 72),
            Display(5, new Rect(2560, 0, 2532, 1170), 1.0, false),
            Display(2, new Rect(-1920, 0, 1920, 1080), 1.0, false),
        }, 1.5),
        "100-primary-150-right" => (new[]
        {
            Display(1, new Rect(0, 0, 1920, 1080), 1.0, true),
            Display(2, new Rect(1920, 0, 3840, 2160), 1.5, false),
        }, 1.0),
        "100-primary-150-above-negative-y" => (new[]
        {
            Display(1, new Rect(0, 0, 1920, 1080), 1.0, true),
            Display(2, new Rect(-960, -2160, 3840, 2160), 1.5, false),
        }, 1.0),
        "150-primary-100-above-left-negative-xy" => (new[]
        {
            Display(1, new Rect(0, 0, 2880, 1800), 1.5, true),
            Display(2, new Rect(-1280, -1024, 1280, 1024), 1.0, false),
        }, 1.5),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    public static TheoryData<string, QuickAccessSide> CardCases()
    {
        var data = new TheoryData<string, QuickAccessSide>();
        foreach (var name in TopologyNames)
        {
            data.Add(name, QuickAccessSide.Left);
            data.Add(name, QuickAccessSide.Right);
        }
        return data;
    }

    public static TheoryData<string> TopologyCases()
    {
        var data = new TheoryData<string>();
        foreach (var name in TopologyNames) data.Add(name);
        return data;
    }

    private static readonly string[] TopologyNames =
    [
        "150-primary-100-right-and-left",
        "100-primary-150-right",
        "100-primary-150-above-negative-y",
        "150-primary-100-above-left-negative-xy",
    ];

    private static Rect ToPhysical(Rect dip, double scale) => DisplayMath.DipToPhysical(dip, scale);

    private static void AssertInside(Rect physical, Rect workArea, string what)
    {
        const double tolerance = 0.5;
        Assert.True(
            physical.MinX >= workArea.MinX - tolerance && physical.MinY >= workArea.MinY - tolerance &&
            physical.MaxX <= workArea.MaxX + tolerance && physical.MaxY <= workArea.MaxY + tolerance,
            $"{what} at {physical} is not inside work area {workArea}.");
    }

    private sealed class ScaledCardWindow : ICardWindow
    {
        public ScaledCardWindow(Guid id, CardViewModel viewModel, double scale)
        {
            Id = id;
            ViewModel = viewModel;
            DeviceScale = scale;
        }

        public Guid Id { get; }
        public CardViewModel ViewModel { get; }
        public Rect Frame { get; set; }
        public double DeviceScale { get; }
        public event EventHandler? Closed;
        public void ShowCard(Rect initialFrame, Rect targetFrame, bool animate) => Frame = targetFrame;
        public void CloseCard(bool animated = true) => Closed?.Invoke(this, EventArgs.Empty);
    }

    [Theory]
    [Unit]
    [MemberData(nameof(CardCases))]
    public void CardOpensInsideTheWorkAreaOfThePointerMonitor(string topology, QuickAccessSide side)
    {
        var (displays, windowScale) = Topology(topology);
        foreach (var display in displays)
        {
            var windows = new List<ScaledCardWindow>();
            var settings = new QuickAccessSettings(side, QuickAccessAutoClose.Never);
            using var host = new QuickAccessHost(
                settings: () => settings,
                pointerProvider: () => display.Bounds.Center,
                windowFactory: (id, img, vm) =>
                {
                    var w = new ScaledCardWindow(id, vm, windowScale);
                    windows.Add(w);
                    return w;
                },
                displaysProvider: () => displays);

            host.Present(new CapturedImage(1200, 800, new byte[16]));
            host.Present(new CapturedImage(400, 900, new byte[16]));

            foreach (var w in windows)
            {
                AssertInside(ToPhysical(w.Frame, windowScale), display.WorkArea, $"Card on {display.DeviceName}");
            }
        }
    }

    [Theory]
    [Unit]
    [MemberData(nameof(TopologyCases))]
    public void EditorOpensCentredInsideTheWorkAreaOfItsMonitor(string topology)
    {
        var (displays, windowScale) = Topology(topology);
        foreach (var display in displays)
        {
            var frame = EditorWindow.CalculateFrame(display.WorkArea, windowScale);
            var physical = ToPhysical(frame, windowScale);

            AssertInside(physical, display.WorkArea, $"Editor on {display.DeviceName}");
            Assert.Equal(display.WorkArea.Center.X, physical.Center.X, 3);
            Assert.Equal(display.WorkArea.Center.Y, physical.Center.Y, 3);
        }
    }

    [Fact]
    [Unit]
    public void PointerOnASecondaryPicksThatMonitorNotThePrimary()
    {
        var (displays, _) = Topology("150-primary-100-right-and-left");

        Assert.Equal(5u, DisplayMath.FindDisplayAt(displays, new Point(3000, 600))!.DisplayId);
        Assert.Equal(2u, DisplayMath.FindDisplayAt(displays, new Point(-5, 1079))!.DisplayId);
        Assert.Equal(1u, DisplayMath.FindDisplayAt(displays, new Point(2559, 0))!.DisplayId);
    }
}
