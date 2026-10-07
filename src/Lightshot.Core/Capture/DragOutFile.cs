// MIT License, Copyright (c) 2026 Viet Le
using System;
using System.IO;
using Lightshot.Core;

namespace Lightshot.Core;

// Drag targets (Explorer, chat apps) need a real image file, never the raw capture pixels.
public static class DragOutFile
{
    public static string WritePng(string directory, Guid id, CapturedImage image, IImageCodec codec)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{id}.png");
        File.WriteAllBytes(path, codec.Encode(new RenderedImage(image.PixelWidth, image.PixelHeight, image.Data.ToArray()), ImageFormat.Png.Instance));
        return path;
    }
}
