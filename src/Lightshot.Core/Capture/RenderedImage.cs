// Ported from LightshotKit/Sources/LightshotKit/Render.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// A flattened image rendered from an AnnotationDocument.
/// </summary>
public record RenderedImage(int PixelWidth, int PixelHeight, byte[] Data)
{
    public virtual bool Equals(RenderedImage? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return PixelWidth == other.PixelWidth
            && PixelHeight == other.PixelHeight
            && Data.AsSpan().SequenceEqual(other.Data.AsSpan());
    }

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(PixelWidth, PixelHeight);
        if (Data.Length > 0)
        {
            hash = HashCode.Combine(hash, Data.Length, Data[0]);
        }
        return hash;
    }
}
