// Ported from LightshotKit/Sources/LightshotKit/ImageSink.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// A ready-to-drag payload for the editor's drag-out action.
/// </summary>
public record ImageDragItem(byte[] Data, ImageFormat Format, string SuggestedName)
{
    public virtual bool Equals(ImageDragItem? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Format.Equals(other.Format)
            && SuggestedName == other.SuggestedName
            && Data.AsSpan().SequenceEqual(other.Data.AsSpan());
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Format, SuggestedName, Data.Length);
    }
}
