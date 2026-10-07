// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using Lightshot.App.Views.Editor;
using Lightshot.App.Views.QuickAccess;
using Lightshot.App.Views.Recording;
using Lightshot.App.Views.VideoEditor;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.TestSupport;
using Xunit;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.App.Tests;

/// <summary>
/// The Quick Access card and the editor open inside the work area of the capture's monitor on
/// mixed-DPI desktops. A window is moved onto its target monitor before it is placed, and WPF then
/// maps its DIPs to device pixels with that monitor's scale, so the expected physical frame is the
/// DIP frame times the target monitor's scale.
/// </summary>
public class PostCapturePlacementTests
{
    private static DisplayInfo Display(uint id, Rect bounds, double scale, bool primary, double taskbar = 48)
    {
        var workArea = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height - taskbar);
        uint dpi = (uint)Math.Round(96 * scale);
        return new DisplayInfo(id, $@"\\.\DISPLAY{id}", (IntPtr)id, bounds, workArea, scale, dpi, dpi, primary, 0);
    }

    private static IReadOnlyList<DisplayInfo> Topology(string name) => name switch
    {
        // The reporting desktop: 150% primary, 100% secondaries to the right and at a negative X.
        "150-primary-100-right-and-left" => new[]
        {
            Display(1, new Rect(0, 0, 2560, 1600), 1.5, true, 72),
            Display(5, new Rect(2560, 0, 2532, 1170), 1.0, false),
            Display(2, new Rect(-1920, 0, 1920, 1080), 1.0, false),
        },
        "100-primary-150-right" => new[]
        {
            Display(1, new Rect(0, 0, 1920, 1080), 1.0, true),
            Display(2, new Rect(1920, 0, 3840, 2160), 1.5, false),
        },
        "100-primary-150-above-negative-y" => new[]
        {
            Display(1, new Rect(0, 0, 1920, 1080), 1.0, true),
            Display(2, new Rect(-960, -2160, 3840, 2160), 1.5, false),
        },
        "150-primary-100-above-left-negative-xy" => new[]
        {
            Display(1, new Rect(0, 0, 2880, 1800), 1.5, true),
            Display(2, new Rect(-1280, -1024, 1280, 1024), 1.0, false),
        },
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

    // Takes the scale of the monitor it is moved onto, as a per-monitor aware WPF window does.
    private sealed class ScaledCardWindow : ICardWindow
    {
        private readonly IReadOnlyList<DisplayInfo> _displays;

        public ScaledCardWindow(Guid id, CardViewModel viewModel, IReadOnlyList<DisplayInfo> displays, double creationScale)
        {
            Id = id;
            ViewModel = viewModel;
            _displays = displays;
            DeviceScale = creationScale;
        }

        public Guid Id { get; }
        public CardViewModel ViewModel { get; }
        public Rect Frame { get; set; }
        public double DeviceScale { get; private set; }

        public double MoveOntoDisplay(Rect physicalWorkArea)
        {
            DeviceScale = _displays.Single(d => d.WorkArea == physicalWorkArea).ScaleFactor;
            return DeviceScale;
        }

        public event EventHandler? Closed;
        public Rect InitialFrame { get; private set; }

        public void ShowCard(Rect initialFrame, Rect targetFrame, bool animate)
        {
            InitialFrame = initialFrame;
            Frame = targetFrame;
        }
        public void CloseCard(bool animated = true) => Closed?.Invoke(this, EventArgs.Empty);
    }

    [Theory]
    [Unit]
    [MemberData(nameof(CardCases))]
    public void CardOpensInsideTheWorkAreaOfThePointerMonitor(string topology, QuickAccessSide side)
    {
        var displays = Topology(topology);
        double creationScale = displays.Single(d => d.IsPrimary).ScaleFactor;
        foreach (var display in displays)
        {
            var windows = new List<ScaledCardWindow>();
            var settings = new QuickAccessSettings(side, QuickAccessAutoClose.Never);
            using var host = new QuickAccessHost(
                settings: () => settings,
                pointerProvider: () => display.Bounds.Center,
                windowFactory: (id, img, vm) =>
                {
                    var w = new ScaledCardWindow(id, vm, displays, creationScale);
                    windows.Add(w);
                    return w;
                },
                displaysProvider: () => displays);

            host.Present(new CapturedImage(1200, 800, new byte[16]));
            host.Present(new CapturedImage(400, 900, new byte[16]));

            Assert.Equal(2, windows.Count);
            foreach (var w in windows)
            {
                Assert.Equal(display.ScaleFactor, w.DeviceScale);
                AssertInside(ToPhysical(w.Frame, w.DeviceScale), display.WorkArea, $"Card on {display.DeviceName}");
                // A slide-in that starts on another monitor shows the card there first, at that monitor's scale.
                var start = ToPhysical(w.InitialFrame, w.DeviceScale);
                Assert.DoesNotContain(displays, d => d.DisplayId != display.DisplayId && d.Bounds.Intersection(start) != null);
            }
        }
    }

    [Theory]
    [Unit]
    [MemberData(nameof(TopologyCases))]
    public void EditorOpensCentredInsideTheWorkAreaOfItsMonitor(string topology)
    {
        foreach (var display in Topology(topology))
        {
            var frame = EditorWindow.CalculateFrame(display.WorkArea, display.ScaleFactor);
            var physical = ToPhysical(frame, display.ScaleFactor);

            AssertInside(physical, display.WorkArea, $"Editor on {display.DeviceName}");
            Assert.Equal(display.WorkArea.Center.X, physical.Center.X, 3);
            Assert.Equal(display.WorkArea.Center.Y, physical.Center.Y, 3);
        }
    }

    [Theory]
    [Unit]
    [MemberData(nameof(TopologyCases))]
    public void VideoEditorOpensCentredInsideTheWorkAreaOfItsMonitor(string topology)
    {
        foreach (var display in Topology(topology))
        {
            var physical = ToPhysical(VideoEditorWindow.CalculateFrame(display.WorkArea, display.ScaleFactor), display.ScaleFactor);

            AssertInside(physical, display.WorkArea, $"Video editor on {display.DeviceName}");
            Assert.Equal(display.WorkArea.Center.X, physical.Center.X, 3);
            Assert.Equal(display.WorkArea.Center.Y, physical.Center.Y, 3);
        }
    }

    [Fact]
    [Unit]
    public void PointerOnASecondaryPicksThatMonitorNotThePrimary()
    {
        var displays = Topology("150-primary-100-right-and-left");

        Assert.Equal(5u, DisplayMath.FindDisplayAt(displays, new Point(3000, 600))!.DisplayId);
        Assert.Equal(2u, DisplayMath.FindDisplayAt(displays, new Point(-5, 1079))!.DisplayId);
        Assert.Equal(1u, DisplayMath.FindDisplayAt(displays, new Point(2559, 0))!.DisplayId);
    }

    [Theory]
    [Unit]
    [MemberData(nameof(TopologyCases))]
    public void MediaViewerOpensCentredInsideTheWorkAreaOfTheRecordedMonitor(string topology)
    {
        var displays = Topology(topology);
        var primaryCentre = displays.Single(d => d.IsPrimary).WorkArea.Center;
        foreach (var display in displays)
        {
            // A take on this monitor while the pointer rests on the primary
            var region = new CaptureRegion.RectRegion(new Rect(display.Bounds.X + 10, display.Bounds.Y + 10, 400, 300));
            var target = MediaViewerWindow.ResolveTargetPoint(displays, region, primaryCentre);
            Assert.Equal(display.DisplayId, DisplayMath.FindDisplayAt(displays, target)!.DisplayId);

            var physical = ToPhysical(MediaViewerWindow.CalculateFrame(display.WorkArea, display.ScaleFactor), display.ScaleFactor);
            AssertInside(physical, display.WorkArea, $"Media viewer on {display.DeviceName}");
            Assert.Equal(display.WorkArea.Center.X, physical.Center.X, 3);
            Assert.Equal(display.WorkArea.Center.Y, physical.Center.Y, 3);
        }
    }

    [Fact]
    [Unit]
    public void MediaViewerWithoutARecordedRegionFollowsThePointer()
    {
        var displays = Topology("150-primary-100-right-and-left");
        var pointer = new Point(-100, 500);

        Assert.Equal(pointer, MediaViewerWindow.ResolveTargetPoint(displays, null, pointer));
    }
}
