// Ported from LightshotKit/Tests/LightshotKitTests/SeamTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class SeamTests
{
    [Fact]
    public void PackageVersionIsExposed()
    {
        Assert.Equal("0.0.1", LightshotKit.Version);
    }

    [Fact]
    public void CapturedImageCarriesPixelDimensionsAndBytes()
    {
        var image = new CapturedImage(2560, 1440, new byte[] { 0x89, 0x50 });
        Assert.Equal(2560, image.PixelWidth);
        Assert.Equal(1440, image.PixelHeight);
        Assert.Equal(2, image.Data.Length);
        Assert.Equal(image, image);
    }
}
