// Ported from LightshotKit/Sources/LightshotKit/QuickAccess.swift and App/Sources/QuickAccessController.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.App.Views.QuickAccess;

public record QuickAccessActions(
    Action<CapturedImage>? Copy = null,
    Func<CapturedImage, bool>? Save = null,
    Func<CapturedImage, bool>? SaveAs = null,
    Action<CapturedImage>? Annotate = null,
    Action<CapturedImage>? Pin = null);

/// <summary>
/// Manages the in-memory stack and on-screen placement of Quick Access card windows.
/// </summary>
public class QuickAccessHost : IDisposable
{
    private static readonly string DragFolder = Path.Combine(Path.GetTempPath(), "Lightshot Drag");

    private readonly QuickAccessActions _actions;
    private readonly Func<QuickAccessSettings> _settings;
    private readonly Func<Point>? _pointerProvider;
    private readonly Func<Point, Rect>? _workAreaProvider;
    private readonly Func<IReadOnlyList<DisplayInfo>>? _displaysProvider;
    private readonly Func<Guid, CapturedImage, CardViewModel, ICardWindow> _windowFactory;
    private readonly IClock _clock;
    private readonly IImageCodec? _codec;

    private readonly QuickAccessStack _stack = new();
    private readonly Dictionary<Guid, CardEntry> _cards = new();
    private Rect? _activeWorkArea;
    // The physical work area the current stack opened on; every card of the stack is moved there.
    private Rect? _activeDisplayWorkArea;

    // Monitors that touch the left and right edges of that work area: a card must not slide in from them,
    // or it first shows on that monitor at its scale and lands on its own monitor at the wrong size.
    private (bool Left, bool Right) _neighbours;
    private bool _disposed;

    public IReadOnlyList<QuickAccessStack.Card> StackCards => _stack.Cards;
    public IReadOnlyDictionary<Guid, ICardWindow> ActiveWindows => _cards.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Window);

    public QuickAccessHost(
        QuickAccessActions? actions = null,
        Func<QuickAccessSettings>? settings = null,
        Func<Point>? pointerProvider = null,
        Func<Point, Rect>? workAreaProvider = null,
        Func<Guid, CapturedImage, CardViewModel, ICardWindow>? windowFactory = null,
        IClock? clock = null,
        Func<IReadOnlyList<DisplayInfo>>? displaysProvider = null,
        IImageCodec? codec = null)
    {
        _actions = actions ?? new QuickAccessActions();
        _settings = settings ?? (() => new QuickAccessSettings());
        _pointerProvider = pointerProvider;
        _workAreaProvider = workAreaProvider;
        _displaysProvider = displaysProvider;
        _codec = codec;
        _windowFactory = windowFactory ?? ((id, img, vm) => new CardWindow(id, img, vm, _settings, _codec));
        _clock = clock ?? SystemClock.Instance;

        try
        {
            if (Directory.Exists(DragFolder))
            {
                Directory.Delete(DragFolder, true);
            }
        }
        catch
        {
            // Best effort temp cleanup
        }
    }

    public Guid Present(CapturedImage image)
    {
        bool startsStack = _stack.Cards.Count == 0 || !_activeWorkArea.HasValue;
        Guid id = _stack.Push(image);
        var viewModel = new CardViewModel(id, image);

        viewModel.OnCopy = () => _actions.Copy?.Invoke(image);
        viewModel.OnSave = () => _actions.Save?.Invoke(image);
        viewModel.OnSaveAs = () =>
        {
            if (_actions.SaveAs?.Invoke(image) == true)
            {
                Remove(id);
            }
        };
        viewModel.OnAnnotate = () =>
        {
            Remove(id);
            _actions.Annotate?.Invoke(image);
        };
        viewModel.OnPin = () =>
        {
            Remove(id);
            _actions.Pin?.Invoke(image);
        };
        viewModel.OnClose = () => Remove(id);
        viewModel.OnCloseAll = CloseAll;

        var window = _windowFactory(id, image, viewModel);
        if (startsStack)
        {
            _activeWorkArea = ResolveWorkArea(window);
        }
        else if (_activeDisplayWorkArea is Rect displayWorkArea)
        {
            window.MoveOntoDisplay(displayWorkArea);
        }
        if (window is CardWindow cardWin)
        {
            cardWin.HoverChanged = (cardId, isInside) => OnCardHover(cardId, isInside);
            cardWin.RequestClose = cardId => Remove(cardId);
        }

        var entry = new CardEntry(id, image, window, viewModel);
        _cards[id] = entry;

        Relayout(enteringId: id);

        var autoCloseSec = _settings().AutoClose.Seconds();
        if (autoCloseSec.HasValue)
        {
            StartAutoClose(id, TimeSpan.FromSeconds(autoCloseSec.Value));
        }

        return id;
    }

    public void OnCardHover(Guid id, bool isInside)
    {
        if (!_cards.TryGetValue(id, out var entry)) return;

        entry.ViewModel.IsHovering = isInside;
        if (entry.ViewModel.IsConfirmed) return;

        if (isInside)
        {
            if (entry.Deadline.HasValue)
            {
                var remaining = entry.Deadline.Value - _clock.UtcNow;
                entry.Remaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
            StopAutoClose(id);
        }
        else
        {
            if (entry.Remaining.HasValue)
            {
                StartAutoClose(id, entry.Remaining.Value);
                entry.Remaining = null;
            }
        }
    }

    public void Remove(Guid id, bool animated = true)
    {
        if (!_cards.TryGetValue(id, out var entry)) return;

        StopAutoClose(id);
        _cards.Remove(id);
        _stack.Remove(id);

        entry.Window.CloseCard(animated);

        if (_stack.Cards.Count == 0)
        {
            _activeWorkArea = null;
            _activeDisplayWorkArea = null;
            _neighbours = default;
        }
        else
        {
            Relayout();
        }
    }

    public void CloseAll()
    {
        var ids = _stack.Cards.Select(c => c.Id).ToList();
        foreach (var id in ids)
        {
            Remove(id, animated: false);
        }
    }

    public void Relayout(Guid? enteringId = null)
    {
        if (_stack.Cards.Count == 0) return;

        Rect workArea = _activeWorkArea ?? ResolveWorkArea(_cards.Values.First().Window);
        var currentSettings = _settings();
        var anchor = currentSettings.Side == QuickAccessSide.Left
            ? ScreenAnchor.BottomLeft
            : ScreenAnchor.BottomRight;

        var sizes = _stack.Cards.Select(c => QuickAccessLayout.CardSize(c.Image.PixelWidth, c.Image.PixelHeight)).ToList();
        var closing = _stack.Overflow(sizes.Select(s => s.Height).ToList(), QuickAccessLayout.AvailableHeight(workArea), QuickAccessLayout.Spacing);

        foreach (var closingId in closing)
        {
            Remove(closingId, animated: false);
        }

        var keptCards = _stack.Cards.Zip(sizes, (c, s) => (c, s)).Where(p => !closing.Contains(p.c.Id)).ToList();
        var placed = QuickAccessLayout.Frames(keptCards.Select(p => p.s).ToList(), workArea, anchor);

        for (int i = 0; i < keptCards.Count; i++)
        {
            var id = keptCards[i].c.Id;
            var targetFrame = placed[i];
            if (!_cards.TryGetValue(id, out var entry)) continue;

            if (id == enteringId)
            {
                bool left = currentSettings.Side == QuickAccessSide.Left;
                if (left ? _neighbours.Left : _neighbours.Right)
                {
                    entry.Window.ShowCard(targetFrame, targetFrame, animate: false);
                    continue;
                }
                double offset = (targetFrame.Width + QuickAccessLayout.Margin) * (left ? -1 : 1);
                var initialFrame = new Rect(targetFrame.MinX + offset, targetFrame.MinY, targetFrame.Width, targetFrame.Height);
                entry.Window.ShowCard(initialFrame, targetFrame, animate: true);
            }
            else
            {
                entry.Window.Frame = targetFrame;
            }
        }
    }

    private void StartAutoClose(Guid id, TimeSpan delay)
    {
        StopAutoClose(id);
        if (!_cards.TryGetValue(id, out var entry)) return;

        entry.Deadline = _clock.UtcNow + delay;
        entry.TimerSubscription = _clock.Schedule(delay, () =>
        {
            Remove(id);
        });
    }

    private void StopAutoClose(Guid id)
    {
        if (_cards.TryGetValue(id, out var entry))
        {
            entry.TimerSubscription?.Dispose();
            entry.TimerSubscription = null;
            entry.Deadline = null;
        }
    }

    /// <summary>
    /// The work area of the monitor under the pointer, in the DIPs of <paramref name="window"/>. The
    /// window is moved onto that monitor first: WPF scales a window by the monitor it is on, so DIPs
    /// computed while it still sits on the primary are wrong on a monitor with another scale.
    /// </summary>
    public Rect ResolveWorkArea(ICardWindow window)
    {
        Point pointer = _pointerProvider?.Invoke() ?? DisplayTopology.GetCursorPosition();
        if (_workAreaProvider != null)
        {
            return _workAreaProvider(pointer);
        }

        try
        {
            var displays = _displaysProvider?.Invoke() ?? DisplayTopology.GetDisplays();
            var targetDisplay = DisplayMath.FindDisplayAt(displays, pointer);
            if (targetDisplay != null)
            {
                _activeDisplayWorkArea = targetDisplay.WorkArea;
                _neighbours = (HasNeighbourAt(displays, targetDisplay, targetDisplay.WorkArea.MinX - 1),
                               HasNeighbourAt(displays, targetDisplay, targetDisplay.WorkArea.MaxX));
                return DisplayMath.PhysicalToDip(targetDisplay.WorkArea, window.MoveOntoDisplay(targetDisplay.WorkArea));
            }
        }
        catch
        {
            // Ignore display topology failures
        }

        var wa = SystemParameters.WorkArea;
        return new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
    }

    private static bool HasNeighbourAt(IReadOnlyList<DisplayInfo> displays, DisplayInfo display, double x)
    {
        var strip = new Rect(x, display.WorkArea.MinY, 1, display.WorkArea.Height);
        return displays.Any(d => d.DisplayId != display.DisplayId && d.Bounds.Intersection(strip) != null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseAll();
    }

    private sealed class CardEntry
    {
        public Guid Id { get; }
        public CapturedImage Image { get; }
        public ICardWindow Window { get; }
        public CardViewModel ViewModel { get; }
        public IDisposable? TimerSubscription { get; set; }
        public DateTime? Deadline { get; set; }
        public TimeSpan? Remaining { get; set; }

        public CardEntry(Guid id, CapturedImage image, ICardWindow window, CardViewModel viewModel)
        {
            Id = id;
            Image = image;
            Window = window;
            ViewModel = viewModel;
        }
    }
}
