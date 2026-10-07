// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Linq;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class HistoryPruneTests
{
    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "history-prune-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch { }
        }
    }

    private static CapturedImage SolidImage(int width = 40, int height = 30)
    {
        byte[] bytes = new byte[width * height * 4];
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i] = 0;
            bytes[i + 1] = 0;
            bytes[i + 2] = 255;
            bytes[i + 3] = 255;
        }
        return new CapturedImage(width, height, bytes);
    }

    [Fact]
    public void PruneRemovesOnlyEntriesOlderThanTheCutoff()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);

        var old1 = store.Add(SolidImage(), CaptureSource.Fullscreen, DateTime.UtcNow.AddDays(-10));
        var old2 = store.Add(SolidImage(), CaptureSource.Area, DateTime.UtcNow.AddDays(-8));
        var newer = store.Add(SolidImage(), CaptureSource.Window, DateTime.UtcNow.AddDays(-2));

        Assert.True(File.Exists(old1.FileUrl));
        Assert.True(File.Exists(old1.ThumbnailUrl));
        Assert.True(File.Exists(old2.FileUrl));
        Assert.True(File.Exists(old2.ThumbnailUrl));
        Assert.True(File.Exists(newer.FileUrl));
        Assert.True(File.Exists(newer.ThumbnailUrl));

        var cutoff = DateTime.UtcNow.AddDays(-5);
        int removed = store.PruneOlderThan(cutoff);

        Assert.Equal(2, removed);
        var remaining = store.All();
        Assert.Single(remaining);
        Assert.Equal(newer.Id, remaining[0].Id);

        Assert.False(File.Exists(old1.FileUrl));
        Assert.False(File.Exists(old1.ThumbnailUrl));
        Assert.False(File.Exists(old2.FileUrl));
        Assert.False(File.Exists(old2.ThumbnailUrl));
        Assert.True(File.Exists(newer.FileUrl));
        Assert.True(File.Exists(newer.ThumbnailUrl));
    }

    [Fact]
    public void PruneWithNothingOldDoesNotRewriteTheIndex()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);

        var recent = store.Add(SolidImage(), CaptureSource.Fullscreen, DateTime.UtcNow.AddDays(-1));
        var indexPath = Path.Combine(dir.Path, "index.json");
        Assert.True(File.Exists(indexPath));

        var originalWriteTime = DateTime.UtcNow.AddHours(-2);
        File.SetLastWriteTimeUtc(indexPath, originalWriteTime);

        var cutoff = DateTime.UtcNow.AddDays(-5);
        int removed = store.PruneOlderThan(cutoff);

        Assert.Equal(0, removed);
        Assert.Single(store.All());
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(indexPath));
    }
}
