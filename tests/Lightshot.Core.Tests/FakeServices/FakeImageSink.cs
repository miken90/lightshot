// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;

namespace Lightshot.Core.Tests.FakeServices;

public class FakeImageSink : IImageSink
{
    public List<RenderedImage> Copied { get; } = [];
    public List<(RenderedImage Image, string Path, ImageFormat Format)> Written { get; } = [];
    public List<string> CopiedText { get; } = [];

    public void CopyToClipboard(RenderedImage image)
    {
        Copied.Add(image);
    }

    public void CopyText(string text)
    {
        CopiedText.Add(text);
    }

    public void Write(RenderedImage image, string path, ImageFormat format)
    {
        Written.Add((image, path, format));
    }
}
