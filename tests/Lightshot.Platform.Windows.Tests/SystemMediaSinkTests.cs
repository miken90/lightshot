// Ported from LightshotKit/Tests/LightshotKitTests/SystemMediaSinkTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Runtime.InteropServices;
using Lightshot.Platform.Windows.Media;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class SystemMediaSinkTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SystemMediaSink _sink;

    public SystemMediaSinkTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lightshot_mediasink_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sink = new SystemMediaSink();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    [Unit]
    public void Save_MovesFileAndRefusesOverwrite()
    {
        string src = Path.Combine(_tempDir, "source.mp4");
        string dst = Path.Combine(_tempDir, "subdir", "dest.mp4");
        File.WriteAllText(src, "video payload");

        _sink.Save(src, dst);

        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dst));
        Assert.Equal("video payload", File.ReadAllText(dst));

        // Overwrite refusal
        File.WriteAllText(src, "another payload");
        Assert.Throws<IOException>(() => _sink.Save(src, dst));
    }

    [Fact]
    [Unit]
    public void Copy_CopiesFileAndRefusesOverwrite()
    {
        string src = Path.Combine(_tempDir, "source.gif");
        string dst = Path.Combine(_tempDir, "copy.gif");
        File.WriteAllText(src, "gif payload");

        _sink.Copy(src, dst);

        Assert.True(File.Exists(src));
        Assert.True(File.Exists(dst));
        Assert.Equal("gif payload", File.ReadAllText(dst));

        // Overwrite refusal
        Assert.Throws<IOException>(() => _sink.Copy(src, dst));
    }

    [Fact]
    [Unit]
    public void Delete_RemovesFile_AndIgnoresMissing()
    {
        string src = Path.Combine(_tempDir, "scratch.tmp");
        File.WriteAllText(src, "junk");

        _sink.Delete(src);
        Assert.False(File.Exists(src));

        // Idempotent / ignores missing or empty
        _sink.Delete(src);
        _sink.Delete("");
    }

    [Fact]
    [Unit]
    public void Validation_ThrowsExpectedExceptions()
    {
        string missing = Path.Combine(_tempDir, "missing.mp4");
        string dst = Path.Combine(_tempDir, "out.mp4");

        Assert.Throws<ArgumentException>(() => _sink.Save("", dst));
        Assert.Throws<ArgumentException>(() => _sink.Save(missing, ""));
        Assert.Throws<FileNotFoundException>(() => _sink.Save(missing, dst));

        Assert.Throws<ArgumentException>(() => _sink.Copy("", dst));
        Assert.Throws<ArgumentException>(() => _sink.Copy(missing, ""));
        Assert.Throws<FileNotFoundException>(() => _sink.Copy(missing, dst));

        Assert.Throws<ArgumentException>(() => _sink.Trash(""));
        Assert.Throws<ArgumentException>(() => _sink.CopyFile(""));
        Assert.Throws<FileNotFoundException>(() => _sink.CopyFile(missing));
    }

    [Fact]
    [Desktop]
    public void CopyFile_SetsHdropOnClipboard()
    {
        string testFile = Path.Combine(_tempDir, "clip_test.mp4");
        File.WriteAllText(testFile, "clipboard test file");

        _sink.CopyFile(testFile);

        // Verify CF_HDROP format exists on clipboard
        [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr h);
        [DllImport("user32.dll")] static extern bool CloseClipboard();
        [DllImport("user32.dll")] static extern uint EnumClipboardFormats(uint format);

        bool hasHdrop = false;
        if (OpenClipboard(IntPtr.Zero))
        {
            try
            {
                uint f = 0;
                while ((f = EnumClipboardFormats(f)) != 0)
                {
                    if (f == 15) // CF_HDROP
                    {
                        hasHdrop = true;
                        break;
                    }
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        Assert.True(hasHdrop, "Clipboard must contain CF_HDROP format after CopyFile.");
    }

    [Fact]
    [Desktop]
    public void Trash_RecyclesFile()
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (string.IsNullOrEmpty(desktopPath) || !Directory.Exists(desktopPath))
        {
            desktopPath = _tempDir;
        }

        string testFile = Path.Combine(desktopPath, $"lightshot_sink_recycle_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(testFile, "recycle payload");
            Assert.True(File.Exists(testFile));

            _sink.Trash(testFile);
            Assert.False(File.Exists(testFile), "File must be removed from original path after Trash.");
        }
        finally
        {
            if (File.Exists(testFile))
            {
                try { File.Delete(testFile); } catch { }
            }
        }
    }
}
