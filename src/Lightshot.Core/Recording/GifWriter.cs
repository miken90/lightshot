// Ported for Lightshot Windows Port (Phase 3 Core domain)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;

namespace Lightshot.Core;

/// <summary>
/// Pure Core GIF89a writer with LZW table-based compression and Netscape loop extension.
/// </summary>
public sealed class GifWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private bool _headerWritten;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }
    public ushort LoopCount { get; }

    public GifWriter(Stream stream, int width, int height, ushort loopCount = 0, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        _stream = stream;
        Width = width;
        Height = height;
        LoopCount = loopCount;
        _leaveOpen = leaveOpen;
    }

    private void EnsureHeader()
    {
        if (_headerWritten) return;
        _headerWritten = true;

        // 1. GIF89a Header
        _stream.WriteByte((byte)'G');
        _stream.WriteByte((byte)'I');
        _stream.WriteByte((byte)'F');
        _stream.WriteByte((byte)'8');
        _stream.WriteByte((byte)'9');
        _stream.WriteByte((byte)'a');

        // 2. Logical Screen Descriptor
        WriteUInt16((ushort)Width);
        WriteUInt16((ushort)Height);
        _stream.WriteByte(0x70); // No global color table, 8 bits color resolution
        _stream.WriteByte(0x00); // Background color index
        _stream.WriteByte(0x00); // Pixel aspect ratio

        // 3. Netscape 2.0 Application Extension (for looping)
        _stream.WriteByte(0x21); // Extension Introducer
        _stream.WriteByte(0xFF); // Application Extension Label
        _stream.WriteByte(0x0B); // Block Size = 11
        byte[] netscape = "NETSCAPE2.0"u8.ToArray();
        _stream.Write(netscape, 0, netscape.Length);
        _stream.WriteByte(0x03); // Sub-block length
        _stream.WriteByte(0x01); // Loop sub-block ID
        WriteUInt16(LoopCount);
        _stream.WriteByte(0x00); // Block Terminator
    }

    /// <summary>
    /// Writes a single frame with a local color table and specified delay in centiseconds (1/100 s).
    /// </summary>
    public void WriteFrame(QuantizedGifFrame frame, ushort delayCentiseconds, bool restoreToBackground = true)
    {
        ArgumentNullException.ThrowIfNull(frame);
        EnsureHeader();

        // 1. Graphic Control Extension
        _stream.WriteByte(0x21); // Extension Introducer
        _stream.WriteByte(0xF9); // Graphic Control Label
        _stream.WriteByte(0x04); // Block Size
        byte disposal = (byte)(restoreToBackground ? (2 << 2) : (1 << 2)); // Disposal method
        _stream.WriteByte(disposal);
        WriteUInt16(delayCentiseconds);
        _stream.WriteByte(0x00); // Transparent color index
        _stream.WriteByte(0x00); // Block Terminator

        // Calculate table size power of 2
        int paletteCount = frame.Palette.Count;
        int colorBits = 1;
        while ((1 << colorBits) < paletteCount)
        {
            colorBits++;
        }
        colorBits = Math.Max(1, Math.Min(8, colorBits));
        int tableSize = 1 << colorBits;

        // 2. Image Descriptor
        _stream.WriteByte(0x2C); // Image Separator
        WriteUInt16(0);          // Left
        WriteUInt16(0);          // Top
        WriteUInt16((ushort)frame.Width);
        WriteUInt16((ushort)frame.Height);
        byte packed = (byte)(0x80 | (colorBits - 1)); // Local color table flag + size
        _stream.WriteByte(packed);

        // 3. Local Color Table
        for (int i = 0; i < tableSize; i++)
        {
            if (i < frame.Palette.Count)
            {
                var c = frame.Palette[i];
                _stream.WriteByte(c.R);
                _stream.WriteByte(c.G);
                _stream.WriteByte(c.B);
            }
            else
            {
                _stream.WriteByte(0);
                _stream.WriteByte(0);
                _stream.WriteByte(0);
            }
        }

        // 4. Image Data: LZW
        int minCodeSize = Math.Max(2, colorBits);
        WriteLzwData(frame.IndexedPixels, minCodeSize);
    }

    private void WriteUInt16(ushort value)
    {
        _stream.WriteByte((byte)(value & 0xFF));
        _stream.WriteByte((byte)((value >> 8) & 0xFF));
    }

    private void WriteLzwData(byte[] pixels, int minCodeSize)
    {
        _stream.WriteByte((byte)minCodeSize);

        int clearCode = 1 << minCodeSize;
        int eoiCode = clearCode + 1;
        int nextCode = clearCode + 2;
        int currentCodeSize = minCodeSize + 1;

        var bitBuffer = new BitBuffer(_stream);
        var dictionary = new Dictionary<int, int>();

        void ResetDictionary()
        {
            dictionary.Clear();
            currentCodeSize = minCodeSize + 1;
            nextCode = clearCode + 2;
        }

        bitBuffer.Write(clearCode, currentCodeSize);

        if (pixels.Length > 0)
        {
            int prefix = pixels[0];

            for (int i = 1; i < pixels.Length; i++)
            {
                int k = pixels[i];
                int key = (prefix << 8) | k;

                if (dictionary.TryGetValue(key, out int code))
                {
                    prefix = code;
                }
                else
                {
                    bitBuffer.Write(prefix, currentCodeSize);

                    if (nextCode < 4096)
                    {
                        dictionary[key] = nextCode++;
                        if (nextCode > (1 << currentCodeSize) && currentCodeSize < 12)
                        {
                            currentCodeSize++;
                        }
                    }
                    else
                    {
                        bitBuffer.Write(clearCode, currentCodeSize);
                        ResetDictionary();
                    }

                    prefix = k;
                }
            }

            bitBuffer.Write(prefix, currentCodeSize);
        }

        bitBuffer.Write(eoiCode, currentCodeSize);
        bitBuffer.Flush();
        _stream.WriteByte(0x00); // Block terminator
    }

    private sealed class BitBuffer
    {
        private readonly Stream _stream;
        private readonly byte[] _subBlock = new byte[255];
        private int _subBlockIndex;
        private int _accum;
        private int _bitsCount;

        public BitBuffer(Stream stream)
        {
            _stream = stream;
        }

        public void Write(int code, int codeSize)
        {
            _accum |= (code << _bitsCount);
            _bitsCount += codeSize;

            while (_bitsCount >= 8)
            {
                AppendByte((byte)(_accum & 0xFF));
                _accum >>= 8;
                _bitsCount -= 8;
            }
        }

        public void Flush()
        {
            if (_bitsCount > 0)
            {
                AppendByte((byte)(_accum & 0xFF));
                _accum = 0;
                _bitsCount = 0;
            }
            FlushSubBlock();
        }

        private void AppendByte(byte b)
        {
            _subBlock[_subBlockIndex++] = b;
            if (_subBlockIndex == 255)
            {
                FlushSubBlock();
            }
        }

        private void FlushSubBlock()
        {
            if (_subBlockIndex > 0)
            {
                _stream.WriteByte((byte)_subBlockIndex);
                _stream.Write(_subBlock, 0, _subBlockIndex);
                _subBlockIndex = 0;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_headerWritten)
        {
            // GIF Trailer
            _stream.WriteByte(0x3B);
        }

        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }
}
