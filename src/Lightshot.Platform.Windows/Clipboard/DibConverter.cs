// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Lightshot.Platform.Windows.Clipboard;

/// <summary>
/// Converts between raw top-down 32-bit BGRA pixel buffers and Windows CF_DIBV5 clipboard format.
/// Preserves premultiplied or straight alpha across the 124-byte BITMAPV5HEADER structure.
/// </summary>
public static class DibConverter
{
    public const int HeaderSize = 124;
    private const uint BI_BITFIELDS = 3;
    private const uint LCS_sRGB = 0x73524742; // 'sRGB'
    private const uint LCS_GM_IMAGES = 4;

    /// <summary>
    /// Converts a top-down BGRA byte buffer into a standard CF_DIBV5 byte array (124-byte header + bottom-up pixel rows).
    /// </summary>
    public static byte[] ConvertToDibV5(int width, int height, ReadOnlySpan<byte> bgraPixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Width and height must be positive", nameof(width));
        }

        int rowBytes = width * 4;
        int imageSize = rowBytes * height;
        byte[] dib = new byte[HeaderSize + imageSize];
        var span = dib.AsSpan();

        // 1. Write 124-byte BITMAPV5HEADER
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8, 4), height); // Positive = bottom-up
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(12, 2), 1); // Planes
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(14, 2), 32); // BitCount
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16, 4), BI_BITFIELDS);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(20, 4), (uint)imageSize);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(24, 4), 3780); // ~96 DPI pels/m
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(28, 4), 3780);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(32, 4), 0); // ClrUsed
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(36, 4), 0); // ClrImportant

        // Channel masks
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(40, 4), 0x00FF0000); // Red
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(44, 4), 0x0000FF00); // Green
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(48, 4), 0x000000FF); // Blue
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(52, 4), 0xFF000000); // Alpha

        // Color space
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(56, 4), LCS_sRGB);
        // Endpoints CIEXYZTRIPLE (36 bytes zeroed at offset 60..96)
        // Gamma (12 bytes zeroed at offset 96..108)
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(108, 4), LCS_GM_IMAGES);

        // 2. Copy pixel rows from top-down source to bottom-up DIB
        int dstOffset = HeaderSize;
        for (int y = height - 1; y >= 0; y--)
        {
            int srcOffset = y * rowBytes;
            if (srcOffset + rowBytes <= bgraPixels.Length)
            {
                bgraPixels.Slice(srcOffset, rowBytes).CopyTo(span.Slice(dstOffset, rowBytes));
            }
            dstOffset += rowBytes;
        }

        return dib;
    }

    /// <summary>
    /// Parses a CF_DIBV5 or CF_DIB byte buffer and extracts top-down 32-bit BGRA pixels.
    /// </summary>
    public static byte[]? ConvertFromDibV5(ReadOnlySpan<byte> dibBytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (dibBytes.Length < 40) return null;

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(dibBytes.Slice(0, 4));
        if (headerSize < 40 || headerSize > dibBytes.Length) return null;

        width = BinaryPrimitives.ReadInt32LittleEndian(dibBytes.Slice(4, 4));
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(dibBytes.Slice(8, 4));
        height = Math.Abs(rawHeight);
        ushort bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dibBytes.Slice(14, 2));

        if (width <= 0 || height <= 0 || bitCount != 32) return null;

        int rowBytes = width * 4;
        int expectedPixels = rowBytes * height;
        int pixelDataOffset = (int)headerSize;

        if (headerSize == 40) // BITMAPINFOHEADER with BI_BITFIELDS
        {
            uint comp = BinaryPrimitives.ReadUInt32LittleEndian(dibBytes.Slice(16, 4));
            if (comp == BI_BITFIELDS) pixelDataOffset += 12;
        }

        if (dibBytes.Length < pixelDataOffset + expectedPixels) return null;

        byte[] bgra = new byte[expectedPixels];
        bool isBottomUp = rawHeight > 0;

        for (int y = 0; y < height; y++)
        {
            int srcRowIndex = isBottomUp ? (height - 1 - y) : y;
            int srcOffset = pixelDataOffset + srcRowIndex * rowBytes;
            int dstOffset = y * rowBytes;
            dibBytes.Slice(srcOffset, rowBytes).CopyTo(bgra.AsSpan(dstOffset, rowBytes));
        }

        return bgra;
    }
}
