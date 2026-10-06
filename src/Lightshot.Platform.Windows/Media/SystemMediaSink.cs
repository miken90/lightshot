// Ported from LightshotKit/Sources/LightshotKit/SystemMediaSink.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Lightshot.Core;
using Lightshot.Platform.Windows.Recycle;

namespace Lightshot.Platform.Windows.Media;

/// <summary>
/// System MediaSink (the Windows side of the recording output seam):
/// file references on the clipboard (CF_HDROP), moving or copying finished recordings,
/// trashing to Windows Recycle Bin, and deleting scratch files.
/// </summary>
public sealed class SystemMediaSink : IMediaSink
{
    private const uint CF_HDROP = 15;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint GMEM_ZEROINIT = 0x0040;
    private const uint GHND = GMEM_MOVEABLE | GMEM_ZEROINIT;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    /// <summary>
    /// Puts a file reference on the Windows clipboard using CF_HDROP so it pastes into File Explorer,
    /// Slack, Teams, Mail, etc.
    /// </summary>
    public void CopyFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("File to copy to clipboard was not found.", fullPath);
        }

        // DROPFILES struct: 20 bytes
        // DWORD pFiles = 20; POINT pt = {0,0}; BOOL fNC = FALSE; BOOL fWide = TRUE (Unicode)
        // followed by null-terminated Unicode string and extra null terminator (double null).
        byte[] pathBytes = Encoding.Unicode.GetBytes(fullPath + "\0\0");
        int headerSize = 20;
        byte[] dropFilesData = new byte[headerSize + pathBytes.Length];

        // pFiles = 20
        dropFilesData[0] = 20;
        // fWide = 1 (offset 16)
        dropFilesData[16] = 1;

        Buffer.BlockCopy(pathBytes, 0, dropFilesData, headerSize, pathBytes.Length);

        ExecuteWithClipboardRetry(() =>
        {
            EmptyClipboard();
            SetGlobalMemoryData(CF_HDROP, dropFilesData);
        });
    }

    /// <summary>
    /// Moves a finished recording to its final destination, creating directory if needed
    /// and refusing to overwrite an existing file.
    /// </summary>
    public void Save(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("Source path cannot be null or empty.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("Destination path cannot be null or empty.", nameof(destinationPath));

        string fullSource = Path.GetFullPath(sourcePath);
        string fullDest = Path.GetFullPath(destinationPath);

        if (!File.Exists(fullSource))
        {
            throw new FileNotFoundException("Source file not found.", fullSource);
        }

        if (File.Exists(fullDest))
        {
            throw new IOException($"Destination file already exists: {fullDest}");
        }

        string? dir = Path.GetDirectoryName(fullDest);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.Move(fullSource, fullDest);
    }

    /// <summary>
    /// Moves a recording to the Windows Recycle Bin so deletions are recoverable.
    /// </summary>
    public void Trash(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        string fullPath = Path.GetFullPath(path);
        RecycleBin.Delete(fullPath);
    }

    /// <summary>
    /// Permanently removes a scratch file that was never delivered to the user.
    /// </summary>
    public void Delete(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch
        {
            // Best effort deletion for scratch files
        }
    }

    /// <summary>
    /// Copies a history-owned recording to its destination without overwriting existing files.
    /// </summary>
    public void Copy(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("Source path cannot be null or empty.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("Destination path cannot be null or empty.", nameof(destinationPath));

        string fullSource = Path.GetFullPath(sourcePath);
        string fullDest = Path.GetFullPath(destinationPath);

        if (!File.Exists(fullSource))
        {
            throw new FileNotFoundException("Source file not found.", fullSource);
        }

        if (File.Exists(fullDest))
        {
            throw new IOException($"Destination file already exists: {fullDest}");
        }

        string? dir = Path.GetDirectoryName(fullDest);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.Copy(fullSource, fullDest, overwrite: false);
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
}
