// Ported from LightshotKit/Sources/LightshotKit/PixelSurface.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// A raw BGRA32 uncompressed pixel buffer representing display or image pixels.
/// Used by FrozenScreen to eliminate PNG decode overhead during capture flows.
/// </summary>
public sealed class PixelSurface
{
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public byte[] Pixels { get; }

    public PixelSurface(int width, int height, byte[]? pixels = null, int? stride = null)
    {
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        Stride = stride ?? (width * 4);
        Pixels = pixels ?? new byte[Height * Stride];
    }
}
