// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Globalization;
using System.Windows;
using Lightshot.App.Views.Editor;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

[Trait("Tier", "Unit")]
public class ValuesEqualConverterTests
{
    private readonly ValuesEqualConverter _converter = new();

    [Fact]
    [Unit]
    public void MatchingValuesConvertToTrue()
    {
        object result1 = _converter.Convert([10, 10], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(true, result1);

        object result2 = _converter.Convert([AspectPreset.Square, AspectPreset.Square], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(true, result2);

        object result3 = _converter.Convert(["same", "same"], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(true, result3);
    }

    [Fact]
    [Unit]
    public void DifferentValuesConvertToFalse()
    {
        object result1 = _converter.Convert([10, 20], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, result1);

        object result2 = _converter.Convert([AspectPreset.Square, AspectPreset.SixteenNine], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, result2);

        object result3 = _converter.Convert(["one", "two"], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, result3);
    }

    [Fact]
    [Unit]
    public void UnsetOrNullConvertsToFalse()
    {
        object nullFirst = _converter.Convert([null!, 10], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, nullFirst);

        object unsetFirst = _converter.Convert([DependencyProperty.UnsetValue, 10], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, unsetFirst);

        object nullSecond = _converter.Convert([10, null!], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, nullSecond);

        object unsetSecond = _converter.Convert([10, DependencyProperty.UnsetValue], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, unsetSecond);

        object wrongLength = _converter.Convert([10], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, wrongLength);

        object empty = _converter.Convert([], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, empty);

        object nullArray = _converter.Convert(null!, typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, nullArray);
    }

    [Fact]
    [Unit]
    public void NullableColorMatchesSwatchColor()
    {
        var color = new RGBAColor(0.2f, 0.4f, 0.6f, 1f);
        RGBAColor? nullableMatching = color;
        RGBAColor? nullableDifferent = new RGBAColor(1f, 0f, 0f, 1f);
        RGBAColor? nullableNull = null;

        object match = _converter.Convert([color, nullableMatching], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(true, match);

        object mismatch = _converter.Convert([color, nullableDifferent], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, mismatch);

        object nullMismatch = _converter.Convert([color, nullableNull!], typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, nullMismatch);

        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(true, [typeof(object), typeof(object)], null!, CultureInfo.InvariantCulture));
    }
}
