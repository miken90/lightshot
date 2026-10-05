// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Files;

/// <summary>
/// Loads images from disk or via a file open dialog seam, wrapping results in Core Result types.
/// </summary>
public class FileImageSource : IImageSource
{
    private readonly IImageCodec? _codec;
    private readonly Func<string?>? _filePicker;

    public FileImageSource(IImageCodec? codec = null, Func<string?>? filePicker = null)
    {
        _codec = codec;
        _filePicker = filePicker;
    }

    public Result<CapturedImage, ImageLoadError> LoadImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Result<CapturedImage, ImageLoadError>.Failure(new ImageLoadError.Unreadable());
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch
        {
            return Result<CapturedImage, ImageLoadError>.Failure(new ImageLoadError.Unreadable());
        }

        if (bytes.Length == 0)
        {
            return Result<CapturedImage, ImageLoadError>.Failure(new ImageLoadError.UnsupportedFormat());
        }

        if (_codec != null)
        {
            var decoded = _codec.Decode(bytes);
            if (decoded is { } img && img.PixelWidth > 0 && img.PixelHeight > 0)
            {
                return Result<CapturedImage, ImageLoadError>.Success(img);
            }
            return Result<CapturedImage, ImageLoadError>.Failure(new ImageLoadError.UnsupportedFormat());
        }

        return Result<CapturedImage, ImageLoadError>.Success(new CapturedImage(0, 0, bytes));
    }

    public Result<CapturedImage, ImageLoadError> OpenDocument()
    {
        var picked = _filePicker?.Invoke();
        if (string.IsNullOrEmpty(picked))
        {
            return Result<CapturedImage, ImageLoadError>.Failure(new ImageLoadError.UserCancelled());
        }

        return LoadImage(picked);
    }
}
