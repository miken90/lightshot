// Ported from LightshotKit/Sources/LightshotKit/ImageFormat.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// The encoding a RenderedImage is written out as.
/// </summary>
public abstract record ImageFormat
{
    public sealed record Png : ImageFormat
    {
        public static readonly Png Instance = new();
    }

    public sealed record Jpeg(double Quality) : ImageFormat
    {
        public static Jpeg Clamping(double quality) =>
            new(Math.Min(Math.Max(quality, 0.0), 1.0));
    }

    public string FileExtension => this switch
    {
        Png => "png",
        Jpeg => "jpg",
        _ => "png"
    };

    public string UtiIdentifier => this switch
    {
        Png => "public.png",
        Jpeg => "public.jpeg",
        _ => "public.png"
    };
}
