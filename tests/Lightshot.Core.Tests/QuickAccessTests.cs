// Ported from LightshotKit/Tests/LightshotKitTests/QuickAccessTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class QuickAccessTests
{
    private static CapturedImage Image(int width = 200, int height = 100) =>
        new(width, height, new byte[] { (byte)(width % 256), (byte)(height % 256) });

    [Fact]
    public void CardsStackOldestFirstWithDistinctIDs()
    {
        var stack = new QuickAccessStack();
        var first = stack.Push(Image(10, 10));
        var second = stack.Push(Image(20, 20));

        Assert.Equal(new[] { first, second }, stack.Cards.Select(c => c.Id));
        Assert.Equal(new[] { Image(10, 10), Image(20, 20) }, stack.Cards.Select(c => c.Image));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void RemovingACardKeepsTheOthersInOrder()
    {
        var stack = new QuickAccessStack();
        var a = stack.Push(Image());
        var b = stack.Push(Image());
        var c = stack.Push(Image());

        stack.Remove(b);
        Assert.Equal(new[] { a, c }, stack.Cards.Select(card => card.Id));
        stack.Remove(Guid.NewGuid());
        Assert.Equal(new[] { a, c }, stack.Cards.Select(card => card.Id));
    }

    [Fact]
    public void TheOldestCardsCloseFirstWhenTheStackDoesNotFit()
    {
        var stack = new QuickAccessStack();
        var ids = Enumerable.Range(0, 4).Select(_ => stack.Push(Image())).ToList();

        var closing = stack.Overflow(new[] { 100.0, 100.0, 100.0, 100.0 }, 300, 12);
        Assert.Equal(new[] { ids[0], ids[1] }, closing);
        Assert.Empty(stack.Overflow(new[] { 100.0, 100.0, 100.0, 100.0 }, 436, 12));
    }

    [Fact]
    public void TheNewestCardIsKeptEvenWhenItAloneIsTooTall()
    {
        var stack = new QuickAccessStack();
        var old = stack.Push(Image());
        _ = stack.Push(Image());

        Assert.Equal(new[] { old }, stack.Overflow(new[] { 100.0, 500.0 }, 300, 12));
        Assert.Empty(new QuickAccessStack().Overflow(Array.Empty<double>(), 300, 12));
    }

    [Fact]
    public void CardsAre220WideWithTheImagesAspectClampedTo90Through220()
    {
        Assert.Equal(new Size(220, 123.75), QuickAccessLayout.CardSize(2560, 1440));
        Assert.Equal(new Size(220, 220), QuickAccessLayout.CardSize(800, 800));
        Assert.Equal(new Size(220, 220), QuickAccessLayout.CardSize(400, 1600));
        Assert.Equal(new Size(220, 90), QuickAccessLayout.CardSize(4000, 100));
        Assert.Equal(new Size(220, 220), QuickAccessLayout.CardSize(0, 0));
    }

    [Fact]
    public void CardsStackUpFromTheBottomLeftNewestAtTheBottom()
    {
        var visible = new Rect(0, 0, 1440, 900);
        var frames = QuickAccessLayout.Frames(
            new[] { new Size(220, 100), new Size(220, 150) },
            visible,
            QuickAccessSide.Left);

        Assert.Equal(
            new[]
            {
                new Rect(16, 16 + 150 + 12, 220, 100),
                new Rect(16, 16, 220, 150),
            },
            frames);
    }

    [Fact]
    public void OnTheRightCardsHugTheRightEdge()
    {
        var visible = new Rect(0, 0, 1440, 900);
        var frames = QuickAccessLayout.Frames(
            new[] { new Size(220, 100) },
            visible,
            QuickAccessSide.Right);

        Assert.Equal(new[] { new Rect(1440 - 16 - 220, 16, 220, 100) }, frames);
    }

    [Fact]
    public void FramesFollowAnOffsetVisibleFrame()
    {
        var visible = new Rect(1440, 70, 1920, 1010);
        Assert.Equal(
            new[] { new Rect(1440 + 16, 70 + 16, 220, 100) },
            QuickAccessLayout.Frames(new[] { new Size(220, 100) }, visible, QuickAccessSide.Left));

        Assert.Equal(
            new[] { new Rect(1440 + 1920 - 16 - 220, 70 + 16, 220, 100) },
            QuickAccessLayout.Frames(new[] { new Size(220, 100) }, visible, QuickAccessSide.Right));

        Assert.Empty(QuickAccessLayout.Frames(Array.Empty<Size>(), visible, QuickAccessSide.Left));
    }

    [Fact]
    public void TheHeightTheStackMayFillLeavesAMarginTopAndBottom()
    {
        Assert.Equal(800 - 32, QuickAccessLayout.AvailableHeight(new Rect(0, 70, 1440, 800)));
    }

    [Fact]
    public void QuickAccessDefaultsToBottomLeftNeverAutoClosingAndClosingAfterADrag()
    {
        var settings = new QuickAccessSettings();
        Assert.Equal(QuickAccessSide.Left, settings.Side);
        Assert.Equal(QuickAccessAutoClose.Never, settings.AutoClose);
        Assert.Null(settings.AutoClose.Seconds());
        Assert.True(settings.CloseAfterDragging);
        Assert.Equal(30.0, QuickAccessAutoClose.After30s.Seconds());
    }

    [Fact]
    public void QuickAccessSettingsRoundTripAndFillMissingFields()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        var custom = new QuickAccessSettings(QuickAccessSide.Right, QuickAccessAutoClose.After1min, false);
        string json = JsonSerializer.Serialize(custom, options);
        var roundTripped = JsonSerializer.Deserialize<QuickAccessSettings>(json, options);
        Assert.Equal(custom, roundTripped);

        string partial = """{"side":"right"}""";
        var fromPartial = JsonSerializer.Deserialize<QuickAccessSettings>(partial, options);
        Assert.Equal(new QuickAccessSettings(QuickAccessSide.Right, QuickAccessAutoClose.Never, true), fromPartial);
    }

    [Fact]
    public void ArrangingPlacesEveryCardThatFitsAndClosesTheRest()
    {
        var stack = new QuickAccessStack();
        var old = stack.Push(Image(1600, 1000));
        var mid = stack.Push(Image(1000, 1000));
        var @new = stack.Push(Image(1600, 1000));
        var visible = new Rect(0, 0, 1440, 32 + 137.5 + 12 + 220 + 50);

        var arrangement = QuickAccessLayout.Arrange(stack, visible, QuickAccessSide.Left);
        Assert.Equal(new[] { old }, arrangement.Closing);
        Assert.Equal(new Rect(16, 16, 220, 137.5), arrangement.Frames[@new]);
        Assert.Equal(new Rect(16, 16 + 137.5 + 12, 220, 220), arrangement.Frames[mid]);
    }

    [Fact]
    public void AutoCloseChoicesHaveTheirSettingsLabels()
    {
        var cases = new[]
        {
            QuickAccessAutoClose.Never,
            QuickAccessAutoClose.After10s,
            QuickAccessAutoClose.After30s,
            QuickAccessAutoClose.After1min
        };
        var titles = cases.Select(c => c.Title()).ToList();
        Assert.Equal(new[] { "Never", "After 10 seconds", "After 30 seconds", "After 1 minute" }, titles);
    }

    [Fact]
    public void StacksFromBottomRightOfWorkArea()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var frames = QuickAccessLayout.Frames(
            new[] { new Size(220, 100), new Size(220, 150) },
            workArea,
            ScreenAnchor.BottomRight);

        Assert.Equal(
            new[]
            {
                new Rect(1684, 802, 220, 100),
                new Rect(1684, 914, 220, 150),
            },
            frames);
    }

    [Fact]
    public void StacksFromTopLeftOfWorkArea()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var frames = QuickAccessLayout.Frames(
            new[] { new Size(220, 100), new Size(220, 150) },
            workArea,
            ScreenAnchor.TopLeft);

        Assert.Equal(
            new[]
            {
                new Rect(16, 178, 220, 100),
                new Rect(16, 16, 220, 150),
            },
            frames);
    }
}
