// Ported from LightshotKit/Sources/LightshotKit/CapturedImage.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// The source-agnostic image value that flows through the whole app.
/// It is a pure value type carrying raw bytes and the native pixel dimensions.
/// </summary>
public readonly record struct CapturedImage(int PixelWidth, int PixelHeight, ReadOnlyMemory<byte> Data)
{
    public CapturedImage(int pixelWidth, int pixelHeight, byte[] data)
        : this(pixelWidth, pixelHeight, new ReadOnlyMemory<byte>(data))
    {
    }

    public bool Equals(CapturedImage other)
    {
        return PixelWidth == other.PixelWidth
            && PixelHeight == other.PixelHeight
            && Data.Span.SequenceEqual(other.Data.Span);
    }

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(PixelWidth, PixelHeight);
        if (!Data.IsEmpty)
        {
            hash = HashCode.Combine(hash, Data.Length, Data.Span[0]);
        }
        return hash;
    }
}
