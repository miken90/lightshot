// Ported from LightshotKit/Sources/LightshotKit/ClickHighlight.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;

namespace Lightshot.Core;

public enum CursorHighlightStyle
{
    Ring,
    Filled,
    Outline
}

public enum CursorHighlightSize
{
    Small,
    Medium,
    Large
}

public static class CursorHighlightSizeExtensions
{
    public static double Radius(this CursorHighlightSize size) => size switch
    {
        CursorHighlightSize.Small => 14.0,
        CursorHighlightSize.Medium => 22.0,
        CursorHighlightSize.Large => 32.0,
        _ => 22.0
    };
}

public enum CursorHighlightColor
{
    Blue,
    Red,
    Green,
    Yellow,
    Orange,
    Purple,
    Pink,
    Gray,
    Accent
}

public readonly record struct RGBColor(double Red, double Green, double Blue)
{
    public static readonly RGBColor BlueColor = new(0.20, 0.50, 1.00);
    public static readonly RGBColor RedColor = new(1.00, 0.25, 0.25);
    public static readonly RGBColor GreenColor = new(0.25, 0.80, 0.40);
    public static readonly RGBColor YellowColor = new(1.00, 0.85, 0.20);
    public static readonly RGBColor OrangeColor = new(1.00, 0.55, 0.15);
    public static readonly RGBColor PurpleColor = new(0.65, 0.40, 1.00);
    public static readonly RGBColor PinkColor = new(1.00, 0.45, 0.75);
    public static readonly RGBColor GrayColor = new(0.60, 0.60, 0.60);

    public static RGBColor? FromCursorHighlightColor(CursorHighlightColor color) => color switch
    {
        CursorHighlightColor.Blue => BlueColor,
        CursorHighlightColor.Red => RedColor,
        CursorHighlightColor.Green => GreenColor,
        CursorHighlightColor.Yellow => YellowColor,
        CursorHighlightColor.Orange => OrangeColor,
        CursorHighlightColor.Purple => PurpleColor,
        CursorHighlightColor.Pink => PinkColor,
        CursorHighlightColor.Gray => GrayColor,
        CursorHighlightColor.Accent => null,
        _ => YellowColor
    };
}

public record ClickHighlightSettings
{
    public CursorHighlightStyle Style { get; set; } = CursorHighlightStyle.Ring;
    public CursorHighlightSize Size { get; set; } = CursorHighlightSize.Medium;
    public CursorHighlightColor Color { get; set; } = CursorHighlightColor.Yellow;
    public bool AnimateClicks { get; set; } = true;

    public ClickHighlightSettings() { }

    public ClickHighlightSettings(
        CursorHighlightStyle style = CursorHighlightStyle.Ring,
        CursorHighlightSize size = CursorHighlightSize.Medium,
        CursorHighlightColor color = CursorHighlightColor.Yellow,
        bool animateClicks = true)
    {
        Style = style;
        Size = size;
        Color = color;
        AnimateClicks = animateClicks;
    }

    public static readonly ClickHighlightSettings Standard = new();
}

public readonly record struct HighlightCircle(
    Point Center,
    double Radius,
    double Opacity,
    bool Filled,
    double StrokeWidth)
{
    public static double CalculateStrokeWidth(double radius) => Math.Max(2.0, radius * 0.18);

    public Rect Bounds => new(Center.X - Radius, Center.Y - Radius, Radius * 2.0, Radius * 2.0);
}

public class ClickHighlightModel
{
    public const double RingDuration = 0.4;
    public const double RingGrowth = 2.4;
    public const double HaloOpacity = 0.45;

    public ClickHighlightSettings Settings { get; }
    public Point? Pointer { get; private set; }

    private readonly List<Click> _clicks = [];

    private readonly record struct Click(Point Position, double Time);

    public ClickHighlightModel(ClickHighlightSettings settings)
    {
        Settings = settings;
    }

    public void PointerMoved(Point point)
    {
        Pointer = point;
    }

    public void Clicked(Point point, double time)
    {
        Pointer = point;
        if (!Settings.AnimateClicks) return;
        _clicks.Add(new Click(point, time));
    }

    public void Prune(double time)
    {
        _clicks.RemoveAll(c => time - c.Time >= RingDuration);
    }

    public IReadOnlyList<HighlightCircle> Circles(double time)
    {
        var result = new List<HighlightCircle>();
        double radius = Settings.Size.Radius();

        if (Pointer.HasValue)
        {
            result.Add(new HighlightCircle(
                Center: Pointer.Value,
                Radius: radius,
                Opacity: HaloOpacity,
                Filled: Settings.Style != CursorHighlightStyle.Ring,
                StrokeWidth: Settings.Style == CursorHighlightStyle.Filled ? 0 : HighlightCircle.CalculateStrokeWidth(radius)
            ));
        }

        foreach (Click click in _clicks)
        {
            double progress = (time - click.Time) / RingDuration;
            if (progress is < 0 or >= 1.0) continue;

            double ringRadius = radius * (1.0 + (RingGrowth - 1.0) * progress);
            result.Add(new HighlightCircle(
                Center: click.Position,
                Radius: ringRadius,
                Opacity: 1.0 - progress,
                Filled: false,
                StrokeWidth: HighlightCircle.CalculateStrokeWidth(ringRadius)
            ));
        }

        return result;
    }
}

public readonly record struct FrameMapping(Point RegionOrigin, double PixelsPerPointX, double PixelsPerPointY)
{
    public Point PixelPoint(Point screenPoint) =>
        new((screenPoint.X - RegionOrigin.X) * PixelsPerPointX, (screenPoint.Y - RegionOrigin.Y) * PixelsPerPointY);

    public HighlightCircle PixelCircle(HighlightCircle circle) =>
        new(PixelPoint(circle.Center), circle.Radius * PixelsPerPointX, circle.Opacity, circle.Filled, circle.StrokeWidth * PixelsPerPointX);
}
