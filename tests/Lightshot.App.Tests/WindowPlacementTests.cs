// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.App.Views;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class WindowPlacementTests
{
    [Fact]
    [Unit]
    public void CappedSizeKeepsTheDesignedSize()
    {
        var sizeFunc = WindowPlacement.CappedSize(720, 600);
        var (width, height) = sizeFunc(1920, 1040);

        Assert.Equal(720, width);
        Assert.Equal(600, height);
    }

    [Fact]
    [Unit]
    public void CappedSizeCapsToNinetyPercent()
    {
        var sizeFunc = WindowPlacement.CappedSize(720, 600);
        var (width, height) = sizeFunc(600, 500);

        Assert.Equal(540, width);
        Assert.Equal(450, height);
    }
}
