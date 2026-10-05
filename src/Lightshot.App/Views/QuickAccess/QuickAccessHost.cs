// Ported from LightshotKit/Sources/LightshotKit/QuickAccess.swift and App/Sources/QuickAccessController.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    private readonly Func<Guid, CapturedImage, CardViewModel, ICardWindow> _windowFactory;
    private readonly IClock _clock;

    private readonly QuickAccessStack _stack = new();
    private readonly Dictionary<Guid, CardEntry> _cards = new();
    private Rect? _activeWorkArea;
    private bool _disposed;

    public IReadOnlyList<QuickAccessStack.Card> StackCards => _stack.Cards;
    public IReadOnlyDictionary<Guid, ICardWindow> ActiveWindows => _cards.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Window);

    public QuickAccessHost(
        QuickAccessActions? actions = null,
        Func<QuickAccessSettings>? settings = null,
        Func<Point>? pointerProvider = null,
        Func<Point, Rect>? workAreaProvider = null,
        Func<Guid, CapturedImage, CardViewModel, ICardWindow>? windowFactory = null,
        IClock? clock = null)
    {
        _actions = actions ?? new QuickAccessActions();
        _settings = settings ?? (() => new QuickAccessSettings());
        _pointerProvider = pointerProvider;
        _workAreaProvider = workAreaProvider;
        _windowFactory = windowFactory ?? ((id, img, vm) => new CardWindow(id, img, vm, _settings));
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
        if (_stack.Cards.Count == 0 || !_activeWorkArea.HasValue)
        {
            _activeWorkArea = ResolveWorkArea();
        }

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

        Rect workArea = _activeWorkArea ?? ResolveWorkArea();
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
                double offset = (targetFrame.Width + QuickAccessLayout.Margin) * (currentSettings.Side == QuickAccessSide.Left ? -1 : 1);
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

    public Rect ResolveWorkArea()
    {
        Point pointer = _pointerProvider?.Invoke() ?? GetCurrentPointerPosition();
        if (_workAreaProvider != null)
        {
            return _workAreaProvider(pointer);
        }

        try
        {
            var displays = DisplayTopology.GetDisplays();
            var targetDisplay = displays.FirstOrDefault(d => d.Bounds.Contains(pointer))
                                ?? displays.FirstOrDefault(d => d.IsPrimary)
                                ?? displays.FirstOrDefault();

            if (targetDisplay != null)
            {
                double scale = targetDisplay.ScaleFactor > 0 ? targetDisplay.ScaleFactor : 1.0;
                return new Rect(
                    targetDisplay.WorkArea.MinX / scale,
                    targetDisplay.WorkArea.MinY / scale,
                    targetDisplay.WorkArea.Width / scale,
                    targetDisplay.WorkArea.Height / scale);
            }
        }
        catch
        {
            // Ignore display topology failures
        }

        var wa = SystemParameters.WorkArea;
        return new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
    }

    private static Point GetCurrentPointerPosition()
    {
        if (GetCursorPos(out var pt))
        {
            return new Point(pt.X, pt.Y);
        }
        return new Point(0, 0);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
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
