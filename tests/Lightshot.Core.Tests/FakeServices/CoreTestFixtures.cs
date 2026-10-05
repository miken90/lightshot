// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;

namespace Lightshot.Core.Tests.FakeServices;

public static class CoreTestFixtures
{
    public static CapturedImage SampleImage() =>
        new(2560, 1440, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

    public static CapturedImage SolidImage(int width, int height)
    {
        byte[] data = new byte[width * height * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = 51;     // B
            data[i + 1] = 102; // G
            data[i + 2] = 153; // R
            data[i + 3] = 255; // A
        }
        return new CapturedImage(width, height, data);
    }

    public static CapturedImage SampleWindowImage() => SolidImage(1600, 1200);

    public static FrozenScreen SampleFrozenScreen()
    {
        var display = new FrozenDisplay(
            1,
            new Rect(0, 0, 1000, 800),
            SolidImage(2000, 1600)
        );
        var window = new FrozenWindow(
            4242,
            new Rect(200, 140, 800, 600),
            null
        );
        return new FrozenScreen([display], [window]);
    }
}
