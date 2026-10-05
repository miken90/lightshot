// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Core;
using Lightshot.Platform.Windows.Clipboard;

namespace Lightshot.Platform.Windows.Files;

/// <summary>
/// File-system image sink implementing IImageSink.
/// Writes rendered images atomically to disk with filename sanitization, collision suffixing (name 2.ext),
/// and desktop default location.
/// </summary>
public class FileImageSink : IImageSink
{
    private readonly ISettingsStore? _settings;
    private readonly IImageCodec? _codec;
    private readonly ClipboardImageSink _clipboardSink;

    public FileImageSink(ISettingsStore? settings = null, IImageCodec? codec = null)
    {
        _settings = settings;
        _codec = codec;
        _clipboardSink = new ClipboardImageSink(codec);
    }

    public void Write(RenderedImage image, string destinationPath, ImageFormat format)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Destination path cannot be empty", nameof(destinationPath));
        }

        // 1. Sanitize filename component
        string sanitizedPath = SanitizeDestinationPath(destinationPath);

        // 2. Resolve collision suffix if file exists
        string uniquePath = GetUniqueFilePath(sanitizedPath);

        // 3. Encode image
        byte[] bytes;
        if (_codec != null)
        {
            bytes = _codec.Encode(image, format);
        }
        else
        {
            bool isPng = image.Data.Length >= 8 &&
                         image.Data[0] == 0x89 && image.Data[1] == 0x50 &&
                         image.Data[2] == 0x4E && image.Data[3] == 0x47 &&
                         image.Data[4] == 0x0D && image.Data[5] == 0x0A &&
                         image.Data[6] == 0x1A && image.Data[7] == 0x0A;

            if (isPng && format is ImageFormat.Png)
            {
                bytes = image.Data;
            }
            else
            {
                bytes = image.Data;
            }
        }

        // 4. Atomic write
        AtomicFile.WriteAllBytes(uniquePath, bytes);
    }

    public void CopyToClipboard(RenderedImage image)
    {
        _clipboardSink.CopyToClipboard(image);
    }

    public void CopyText(string text)
    {
        _clipboardSink.CopyText(text);
    }

    /// <summary>
    /// Generates a unique collision-free file path for destinationPath if it already exists.
    /// Appends " 2", " 3", etc. before the extension.
    /// </summary>
    public static string GetUniqueFilePath(string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            return destinationPath;
        }

        string? dir = Path.GetDirectoryName(destinationPath);
        string nameWithoutExt = Path.GetFileNameWithoutExtension(destinationPath);
        string ext = Path.GetExtension(destinationPath);

        for (int i = 2; i < 10000; i++)
        {
            string candidate = Path.Combine(dir ?? "", $"{nameWithoutExt} {i}{ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return destinationPath;
    }

    /// <summary>
    /// Sanitizes the file name portion of the path for Windows file systems.
    /// </summary>
    public static string SanitizeDestinationPath(string destinationPath)
    {
        string? dir = Path.GetDirectoryName(destinationPath);
        string fileName = Path.GetFileNameWithoutExtension(destinationPath);
        string ext = Path.GetExtension(destinationPath);

        string safeName = FilenameFormatter.Sanitized(fileName) ?? "Screenshot";
        string cleanExt = ext.TrimStart('.');
        string safeExt = FilenameFormatter.Sanitized(cleanExt) ?? "png";

        return Path.Combine(dir ?? "", $"{safeName}.{safeExt}");
    }
}
