// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using Lightshot.App.Views.QuickAccess;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.App.Tests;

public class QuickAccessHostTests
{
    private class FakeClock : IClock
    {
        private DateTime _now = new(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
        private readonly List<ScheduledItem> _scheduled = [];

        public DateTime UtcNow => _now;

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var item = new ScheduledItem(_now + delay, callback);
            _scheduled.Add(item);
            return new Unsubscriber(() => _scheduled.Remove(item));
        }

        public void Advance(TimeSpan delta)
        {
            _now += delta;
            var due = _scheduled.Where(s => s.Due <= _now).ToList();
            foreach (var item in due)
            {
                _scheduled.Remove(item);
                item.Callback();
            }
        }

        private sealed class ScheduledItem
        {
            public DateTime Due { get; }
            public Action Callback { get; }

            public ScheduledItem(DateTime due, Action callback)
            {
                Due = due;
                Callback = callback;
            }
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly Action _action;
            public Unsubscriber(Action action) => _action = action;
            public void Dispose() => _action();
        }
    }

    private class FakeCardWindow : ICardWindow
    {
        public Guid Id { get; }
        public CardViewModel ViewModel { get; }
        public Rect Frame { get; set; }
        public double MoveOntoDisplay(Rect physicalWorkArea) => 1.0;
        public bool IsShown { get; private set; }
        public bool IsClosed { get; private set; }

        public event EventHandler? Closed;

        public FakeCardWindow(Guid id, CardViewModel viewModel)
        {
            Id = id;
            ViewModel = viewModel;
        }

        public void ShowCard(Rect initialFrame, Rect targetFrame, bool animate)
        {
            IsShown = true;
            Frame = targetFrame;
        }

        public void CloseCard(bool animated = true)
        {
            IsClosed = true;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    [Fact]
    [Unit]
    public void PositionsCardsInPointerMonitorWorkArea()
    {
        // 1. Setup multi-monitor environment in workAreaProvider:
        // Monitor 1: (0, 0, 1920, 1040)
        // Monitor 2: (1920, 0, 1920, 1040)
        Point pointer = new Point(500, 400); // On Monitor 1
        var monitor1Wa = new Rect(0, 0, 1920, 1040);
        var monitor2Wa = new Rect(1920, 0, 1920, 1040);

        var fakeWindows = new Dictionary<Guid, FakeCardWindow>();
        Func<Guid, CapturedImage, CardViewModel, ICardWindow> factory = (id, img, vm) =>
        {
            var win = new FakeCardWindow(id, vm);
            fakeWindows[id] = win;
            return win;
        };

        var settings = new QuickAccessSettings(QuickAccessSide.Left, QuickAccessAutoClose.Never);
        using var host = new QuickAccessHost(
            settings: () => settings,
            pointerProvider: () => pointer,
            workAreaProvider: pt => pt.X >= 1920 ? monitor2Wa : monitor1Wa,
            windowFactory: factory);

        // 2. Present card on Monitor 1
        var sampleImg = new CapturedImage(440, 200, new byte[16]);
        var id1 = host.Present(sampleImg);

        Assert.Single(host.StackCards);
        var win1 = fakeWindows[id1];
        Assert.True(win1.IsShown);

        // Card size: 220 x 100
        // Bottom-left anchor: X = 0 + 16 = 16, Y = 1040 - 16 - 100 = 924
        Assert.Equal(16.0, win1.Frame.MinX);
        Assert.Equal(924.0, win1.Frame.MinY);
        Assert.Equal(220.0, win1.Frame.Width);
        Assert.Equal(100.0, win1.Frame.Height);

        // 3. Present second card on same monitor
        var id2 = host.Present(sampleImg);
        Assert.Equal(2, host.StackCards.Count);
        var win2 = fakeWindows[id2];

        // Newest card sits at the bottom (924), older card stacks above it (924 - 12 - 100 = 812)
        Assert.Equal(924.0, win2.Frame.MinY);
        Assert.Equal(812.0, win1.Frame.MinY);

        // 4. Test secondary monitor placement when pointer is on monitor 2
        pointer = new Point(2500, 300);
        host.CloseAll();
        Assert.Empty(host.StackCards);

        var id3 = host.Present(sampleImg);
        var win3 = fakeWindows[id3];
        // On monitor 2: X = 1920 + 16 = 1936, Y = 1040 - 16 - 100 = 924
        Assert.Equal(1936.0, win3.Frame.MinX);
        Assert.Equal(924.0, win3.Frame.MinY);
    }

    [Fact]
    [Unit]
    public void AutoCloseHonoursSettingAndHoverPause()
    {
        var clock = new FakeClock();
        var fakeWindows = new Dictionary<Guid, FakeCardWindow>();
        Func<Guid, CapturedImage, CardViewModel, ICardWindow> factory = (id, img, vm) =>
        {
            var win = new FakeCardWindow(id, vm);
            fakeWindows[id] = win;
            return win;
        };

        var settings = new QuickAccessSettings(QuickAccessSide.Left, QuickAccessAutoClose.After10s);
        using var host = new QuickAccessHost(
            settings: () => settings,
            pointerProvider: () => new Point(100, 100),
            workAreaProvider: _ => new Rect(0, 0, 1920, 1080),
            windowFactory: factory,
            clock: clock);

        var sampleImg = new CapturedImage(440, 200, new byte[16]);
        var id = host.Present(sampleImg);
        var win = fakeWindows[id];

        Assert.Single(host.StackCards);
        Assert.False(win.IsClosed);

        // Advance 5 seconds -> Card should still be open
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Single(host.StackCards);
        Assert.False(win.IsClosed);

        // Pointer hovers card -> timer pauses (with ~5 seconds remaining)
        host.OnCardHover(id, true);

        // Advance 15 seconds while hovering -> Card remains open
        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.Single(host.StackCards);
        Assert.False(win.IsClosed);

        // Pointer leaves card -> countdown resumes with remaining ~5 seconds
        host.OnCardHover(id, false);

        // Advance 3 seconds -> still open
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Single(host.StackCards);
        Assert.False(win.IsClosed);

        // Advance 3 more seconds (total 6 > remaining 5) -> auto-close triggers!
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Empty(host.StackCards);
        Assert.True(win.IsClosed);

        // Verify Never setting does not auto-close
        settings = new QuickAccessSettings(QuickAccessSide.Left, QuickAccessAutoClose.Never);
        var idNever = host.Present(sampleImg);
        var winNever = fakeWindows[idNever];

        clock.Advance(TimeSpan.FromHours(10));
        Assert.Single(host.StackCards);
        Assert.False(winNever.IsClosed);
    }
}
