// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;

namespace Lightshot.Core.Tests.FakeServices;

public class FakeImageSource : IImageSource
{
    public Result<CapturedImage, ImageLoadError> Result { get; set; }
    public int OpenCount { get; private set; }
    public List<string> LoadedPaths { get; } = [];

    public FakeImageSource(Result<CapturedImage, ImageLoadError> result)
    {
        Result = result;
    }

    public Result<CapturedImage, ImageLoadError> LoadImage(string path)
    {
        LoadedPaths.Add(path);
        return Result;
    }

    public Result<CapturedImage, ImageLoadError> OpenDocument()
    {
        OpenCount++;
        return Result;
    }
}
