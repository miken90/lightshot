// Ported from LightshotKit/Sources/LightshotKit/CaptureRegion.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// What an area/window capture targets.
/// </summary>
public abstract record CaptureRegion
{
    public sealed record RectRegion(Rect Rect) : CaptureRegion;
    public sealed record WindowRegion(uint Id, Rect Frame) : CaptureRegion;
    public sealed record DisplayRegion(uint Id) : CaptureRegion;

    public RecordedArea RecordedArea => this switch
    {
        DisplayRegion d => new RecordedArea.DisplayArea(d.Id),
        RectRegion r => new RecordedArea.SubArea(r.Rect.Standardized),
        WindowRegion w => new RecordedArea.SubArea(w.Frame.Standardized),
        _ => throw new InvalidOperationException()
    };
}

/// <summary>
/// What a recording of a CaptureRegion streams: whole display or sub-area.
/// </summary>
public abstract record RecordedArea
{
    public sealed record DisplayArea(uint Id) : RecordedArea;
    public sealed record SubArea(Rect Rect) : RecordedArea;
}
