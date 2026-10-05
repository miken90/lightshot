// Ported from LightshotKit/Sources/LightshotKit/QuickAccess.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lightshot.Core;

/// <summary>
/// Which corner the Quick Access cards stack in.
/// </summary>
public enum QuickAccessSide
{
    Left,
    Right
}

/// <summary>
/// Screen anchor position for stacking Quick Access cards in a top-left origin work area.
/// </summary>
public enum ScreenAnchor
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft
}

/// <summary>
/// Auto-close timeout preference for Quick Access cards.
/// </summary>
public enum QuickAccessAutoClose
{
    Never,
    After10s,
    After30s,
    After1min
}

public static class QuickAccessAutoCloseExtensions
{
    public static string Title(this QuickAccessAutoClose close) => close switch
    {
        QuickAccessAutoClose.Never => "Never",
        QuickAccessAutoClose.After10s => "After 10 seconds",
        QuickAccessAutoClose.After30s => "After 30 seconds",
        QuickAccessAutoClose.After1min => "After 1 minute",
        _ => "Never"
    };

    public static double? Seconds(this QuickAccessAutoClose close) => close switch
    {
        QuickAccessAutoClose.Never => null,
        QuickAccessAutoClose.After10s => 10.0,
        QuickAccessAutoClose.After30s => 30.0,
        QuickAccessAutoClose.After1min => 60.0,
        _ => null
    };
}

/// <summary>
/// Settings for Quick Access overlay cards.
/// </summary>
public record QuickAccessSettings(
    QuickAccessSide Side = QuickAccessSide.Left,
    QuickAccessAutoClose AutoClose = QuickAccessAutoClose.Never,
    bool CloseAfterDragging = true);

/// <summary>
/// In-memory stack of Quick Access cards.
/// </summary>
public class QuickAccessStack
{
    public record Card(Guid Id, CapturedImage Image);

    private readonly List<Card> _cards = [];
    public IReadOnlyList<Card> Cards => _cards;

    public Guid Push(CapturedImage image)
    {
        var id = Guid.NewGuid();
        _cards.Add(new Card(id, image));
        return id;
    }

    public void Remove(Guid id)
    {
        _cards.RemoveAll(c => c.Id == id);
    }

    public IReadOnlyList<Guid> Overflow(IReadOnlyList<double> cardHeights, double available, double spacing)
    {
        double used = 0;
        int kept = 0;

        for (int i = cardHeights.Count - 1; i >= 0; i--)
        {
            double height = cardHeights[i];
            double needed = used + (kept == 0 ? 0 : spacing) + height;
            if (kept != 0 && needed > available)
            {
                break;
            }
            used = needed;
            kept++;
        }

        int countToRemove = Math.Max(0, _cards.Count - kept);
        return _cards.Take(countToRemove).Select(c => c.Id).ToList();
    }
}

/// <summary>
/// Pure layout calculation for Quick Access cards.
/// </summary>
public static class QuickAccessLayout
{
    public const double CardWidth = 220.0;
    public const double MinCardHeight = 90.0;
    public const double MaxCardHeight = 220.0;
    public const double Margin = 16.0;
    public const double Spacing = 12.0;

    public static Size CardSize(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return new Size(CardWidth, MaxCardHeight);
        }

        double height = CardWidth * (double)pixelHeight / pixelWidth;
        return new Size(CardWidth, Math.Min(Math.Max(height, MinCardHeight), MaxCardHeight));
    }

    public static double AvailableHeight(Rect visible)
    {
        return visible.Height - 2 * Margin;
    }

    /// <summary>
    /// Upstream AppKit layout: stacks upwards from bottom-left or bottom-right
    /// where visible has a bottom-left origin.
    /// </summary>
    public static IReadOnlyList<Rect> Frames(IReadOnlyList<Size> sizes, Rect visible, QuickAccessSide side)
    {
        double y = visible.MinY + Margin;
        var result = new List<Rect>(sizes.Count);

        for (int i = sizes.Count - 1; i >= 0; i--)
        {
            var size = sizes[i];
            double x = side == QuickAccessSide.Left ? visible.MinX + Margin : visible.MaxX - Margin - size.Width;
            result.Add(new Rect(x, y, size.Width, size.Height));
            y += size.Height + Spacing;
        }

        result.Reverse();
        return result;
    }

    /// <summary>
    /// Windows workArea layout (top-left origin coordinates).
    /// </summary>
    public static IReadOnlyList<Rect> Frames(IReadOnlyList<Size> sizes, Rect workArea, ScreenAnchor anchor)
    {
        var result = new List<Rect>(sizes.Count);
        if (sizes.Count == 0) return result;

        switch (anchor)
        {
            case ScreenAnchor.BottomRight:
            {
                // Newest card sits at the bottom right
                double currentBottom = workArea.MaxY - Margin;
                for (int i = sizes.Count - 1; i >= 0; i--)
                {
                    var size = sizes[i];
                    double x = workArea.MaxX - Margin - size.Width;
                    double y = currentBottom - size.Height;
                    result.Add(new Rect(x, y, size.Width, size.Height));
                    currentBottom = y - Spacing;
                }
                result.Reverse();
                break;
            }

            case ScreenAnchor.BottomLeft:
            {
                double currentBottom = workArea.MaxY - Margin;
                for (int i = sizes.Count - 1; i >= 0; i--)
                {
                    var size = sizes[i];
                    double x = workArea.MinX + Margin;
                    double y = currentBottom - size.Height;
                    result.Add(new Rect(x, y, size.Width, size.Height));
                    currentBottom = y - Spacing;
                }
                result.Reverse();
                break;
            }

            case ScreenAnchor.TopLeft:
            {
                // Newest card sits at top-left, older cards stack downwards
                double currentTop = workArea.MinY + Margin;
                for (int i = sizes.Count - 1; i >= 0; i--)
                {
                    var size = sizes[i];
                    double x = workArea.MinX + Margin;
                    double y = currentTop;
                    result.Add(new Rect(x, y, size.Width, size.Height));
                    currentTop = y + size.Height + Spacing;
                }
                result.Reverse();
                break;
            }

            case ScreenAnchor.TopRight:
            {
                double currentTop = workArea.MinY + Margin;
                for (int i = sizes.Count - 1; i >= 0; i--)
                {
                    var size = sizes[i];
                    double x = workArea.MaxX - Margin - size.Width;
                    double y = currentTop;
                    result.Add(new Rect(x, y, size.Width, size.Height));
                    currentTop = y + size.Height + Spacing;
                }
                result.Reverse();
                break;
            }
        }

        return result;
    }

    public record Arrangement(IReadOnlyDictionary<Guid, Rect> Frames, IReadOnlyList<Guid> Closing);

    public static Arrangement Arrange(QuickAccessStack stack, Rect visible, QuickAccessSide side)
    {
        var sizes = stack.Cards.Select(c => CardSize(c.Image.PixelWidth, c.Image.PixelHeight)).ToList();
        var closing = stack.Overflow(sizes.Select(s => s.Height).ToList(), AvailableHeight(visible), Spacing);
        var keptCards = stack.Cards.Zip(sizes, (c, s) => (c, s)).Where(p => !closing.Contains(p.c.Id)).ToList();
        var placed = Frames(keptCards.Select(p => p.s).ToList(), visible, side);

        var framesDict = new Dictionary<Guid, Rect>();
        for (int i = 0; i < keptCards.Count; i++)
        {
            framesDict[keptCards[i].c.Id] = placed[i];
        }

        return new Arrangement(framesDict, closing);
    }
}
