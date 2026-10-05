// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Lightshot.Core;
using Lightshot.Platform.Windows.Files;

namespace Lightshot.Platform.Windows.Clipboard;

/// <summary>
/// Windows implementation of IImageSink for the system clipboard.
/// Sets both PNG and CF_DIBV5 (with alpha) on a single clipboard transaction,
/// with retry backoff against clipboard contention.
/// </summary>
public class ClipboardImageSink : IImageSink
{
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_DIBV5 = 17;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint GMEM_ZEROINIT = 0x0040;
    private const uint GHND = GMEM_MOVEABLE | GMEM_ZEROINIT;

    private readonly IImageCodec? _codec;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string lpszFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    public ClipboardImageSink(IImageCodec? codec = null)
    {
        _codec = codec;
    }

    public void CopyToClipboard(RenderedImage image)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));

        byte[]? pngBytes = null;
        byte[]? dibBytes = null;

        bool isPng = image.Data.Length >= 8 &&
                     image.Data[0] == 0x89 && image.Data[1] == 0x50 &&
                     image.Data[2] == 0x4E && image.Data[3] == 0x47 &&
                     image.Data[4] == 0x0D && image.Data[5] == 0x0A &&
                     image.Data[6] == 0x1A && image.Data[7] == 0x0A;

        if (isPng)
        {
            pngBytes = image.Data;
            // Decode to BGRA for CF_DIBV5
            if (_codec != null)
            {
                var decoded = _codec.Decode(pngBytes);
                if (decoded.HasValue && decoded.Value.Data.Length >= decoded.Value.PixelWidth * decoded.Value.PixelHeight * 4)
                {
                    dibBytes = DibConverter.ConvertToDibV5(decoded.Value.PixelWidth, decoded.Value.PixelHeight, decoded.Value.Data.Span);
                }
            }
        }
        else if (image.PixelWidth > 0 && image.PixelHeight > 0 && image.Data.Length >= image.PixelWidth * image.PixelHeight * 4)
        {
            // Raw BGRA
            dibBytes = DibConverter.ConvertToDibV5(image.PixelWidth, image.PixelHeight, image.Data);
            if (_codec != null)
            {
                pngBytes = _codec.Encode(image, new ImageFormat.Png());
            }
            else
            {
                // Simple PNG wrap if no codec
                pngBytes = CreateFallbackPng(image.PixelWidth, image.PixelHeight, image.Data);
            }
        }

        if (pngBytes == null && isPng) pngBytes = image.Data;
        if (dibBytes == null && image.PixelWidth > 0 && image.PixelHeight > 0 && image.Data.Length >= image.PixelWidth * image.PixelHeight * 4)
        {
            dibBytes = DibConverter.ConvertToDibV5(image.PixelWidth, image.PixelHeight, image.Data);
        }

        ExecuteWithClipboardRetry(() =>
        {
            EmptyClipboard();

            // 1. Set CF_DIBV5
            if (dibBytes != null && dibBytes.Length > 0)
            {
                SetGlobalMemoryData(CF_DIBV5, dibBytes);
            }

            // 2. Set PNG
            if (pngBytes != null && pngBytes.Length > 0)
            {
                uint pngFormat = RegisterClipboardFormatW("PNG");
                if (pngFormat != 0)
                {
                    SetGlobalMemoryData(pngFormat, pngBytes);
                }
            }
        });
    }

    public void CopyText(string text)
    {
        if (text == null) text = string.Empty;
        byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");

        ExecuteWithClipboardRetry(() =>
        {
            EmptyClipboard();
            SetGlobalMemoryData(CF_UNICODETEXT, bytes);
        });
    }

    public void Write(RenderedImage image, string destinationPath, ImageFormat format)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        byte[] bytes;
        if (_codec != null)
        {
            bytes = _codec.Encode(image, format);
        }
        else
        {
            bytes = image.Data;
        }

        AtomicFile.WriteAllBytes(destinationPath, bytes);
    }

    private static void SetGlobalMemoryData(uint format, byte[] data)
    {
        IntPtr hMem = GlobalAlloc(GHND, (nuint)data.Length);
        if (hMem == IntPtr.Zero) return;

        IntPtr pMem = GlobalLock(hMem);
        if (pMem == IntPtr.Zero) return;

        try
        {
            Marshal.Copy(data, 0, pMem, data.Length);
        }
        finally
        {
            GlobalUnlock(hMem);
        }

        SetClipboardData(format, hMem);
    }

    private static void ExecuteWithClipboardRetry(Action action)
    {
        bool opened = false;
        for (int retry = 0; retry < 10; retry++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                opened = true;
                break;
            }
            Thread.Sleep(10 * (retry + 1));
        }

        if (!opened)
        {
            throw new InvalidOperationException("Failed to open Windows clipboard after multiple attempts.");
        }

        try
        {
            action();
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static byte[] CreateFallbackPng(int width, int height, byte[] bgraPixels)
    {
        // Minimal uncompressed PNG writer fallback using Deflate
        using var ms = new MemoryStream();
        // PNG signature
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        // IHDR
        byte[] ihdrData = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdrData.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdrData.AsSpan(4, 4), height);
        ihdrData[8] = 8; // 8-bit depth
        ihdrData[9] = 6; // RGBA color type
        ihdrData[10] = 0; // Deflate
        ihdrData[11] = 0; // Filter
        ihdrData[12] = 0; // Interlace
        WriteChunk(ms, "IHDR", ihdrData);

        // IDAT
        // Convert BGRA to RGBA with filter byte 0 per scanline
        int rowBytes = width * 4;
        byte[] rawScanlines = new byte[height * (rowBytes + 1)];
        for (int y = 0; y < height; y++)
        {
            int dstOffset = y * (rowBytes + 1);
            rawScanlines[dstOffset] = 0; // None filter
            int srcOffset = y * rowBytes;
            for (int x = 0; x < width; x++)
            {
                int sIdx = srcOffset + x * 4;
                int dIdx = dstOffset + 1 + x * 4;
                rawScanlines[dIdx] = bgraPixels[sIdx + 2];     // R
                rawScanlines[dIdx + 1] = bgraPixels[sIdx + 1]; // G
                rawScanlines[dIdx + 2] = bgraPixels[sIdx];     // B
                rawScanlines[dIdx + 3] = bgraPixels[sIdx + 3]; // A
            }
        }

        using (var compressedMs = new MemoryStream())
        {
            // ZLIB header: 0x78, 0x9C
            compressedMs.WriteByte(0x78);
            compressedMs.WriteByte(0x9C);
            using (var deflate = new System.IO.Compression.DeflateStream(compressedMs, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            {
                deflate.Write(rawScanlines, 0, rawScanlines.Length);
            }

            // Adler-32
            uint a1 = 1, a2 = 0;
            foreach (byte b in rawScanlines)
            {
                a1 = (a1 + b) % 65521;
                a2 = (a2 + a1) % 65521;
            }
            uint adler = (a2 << 16) | a1;
            byte[] adlerBytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(adlerBytes, adler);
            compressedMs.Write(adlerBytes, 0, 4);

            WriteChunk(ms, "IDAT", compressedMs.ToArray());
        }

        // IEND
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] lenBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lenBytes, data.Length);
        stream.Write(lenBytes, 0, 4);

        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes, 0, 4);
        if (data.Length > 0)
        {
            stream.Write(data, 0, data.Length);
        }

        // CRC32
        uint crc = 0xFFFFFFFF;
        foreach (byte b in typeBytes) crc = UpdateCrc(crc, b);
        foreach (byte b in data) crc = UpdateCrc(crc, b);
        crc ^= 0xFFFFFFFF;

        byte[] crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes, 0, 4);
    }

    private static uint UpdateCrc(uint crc, byte b)
    {
        crc ^= b;
        for (int i = 0; i < 8; i++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : (crc >> 1);
        }
        return crc;
    }
}
