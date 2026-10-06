// Ported from LightshotKit/Tests/LightshotKitTests/HistoryStoreTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Linq;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class HistoryStoreTests
{
    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "history-" + Guid.NewGuid());
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
            bytes[i] = 0;     // B
            bytes[i + 1] = 0; // G
            bytes[i + 2] = 255; // R
            bytes[i + 3] = 255; // A
        }
        return new CapturedImage(width, height, bytes);
    }

    private static DateTime Instant(double offset)
    {
        var referenceDate = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return referenceDate.AddSeconds(1_000_000 + offset);
    }

    private static string MovieFixture(TempDir dir, byte[]? bytes = null)
    {
        var path = Path.Combine(dir.Path, "take.mp4");
        File.WriteAllBytes(path, bytes ?? new byte[] { 0, 1, 2, 3 });
        return path;
    }

    [Fact]
    public void AddRecordsCapturesAndListsThemNewestFirst()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);

        var first = store.Add(SolidImage(), CaptureSource.Fullscreen, Instant(0));
        var second = store.Add(SolidImage(), CaptureSource.Area, Instant(10));
        var third = store.Add(SolidImage(), CaptureSource.Window, Instant(20));

        var all = store.All();
        Assert.Equal(new[] { third.Id, second.Id, first.Id }, all.Select(r => r.Id));
        Assert.Equal(new[] { CaptureSource.Window, CaptureSource.Area, CaptureSource.Fullscreen }, all.Select(r => r.Source));
    }

    [Fact]
    public void CapturedImageReopensWithOriginalBytesAndDimensions()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        var image = SolidImage(50, 20);

        var record = store.Add(image, CaptureSource.Fullscreen);
        var reopened = store.CapturedImage(record);

        Assert.Equal(image, reopened);
    }

    [Fact]
    public void RemoveDeletesTheItemAndItsFiles()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        var keep = store.Add(SolidImage(), CaptureSource.Fullscreen, Instant(0));
        var drop = store.Add(SolidImage(), CaptureSource.Area, Instant(10));

        store.Remove(drop);

        Assert.Equal(new[] { keep.Id }, store.All().Select(r => r.Id));
        Assert.False(File.Exists(drop.FileUrl));
        Assert.False(File.Exists(drop.ThumbnailUrl));
        Assert.True(File.Exists(keep.FileUrl));
    }

    [Fact]
    public void ClearEmptiesHistoryAndDeletesEveryFile()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path);
        var a = store.Add(SolidImage(), CaptureSource.Fullscreen, Instant(0));
        var b = store.Add(SolidImage(), CaptureSource.Area, Instant(10));

        store.Clear();

        Assert.Empty(store.All());
        Assert.False(File.Exists(a.FileUrl));
        Assert.False(File.Exists(b.ThumbnailUrl));
    }

    [Fact]
    public void RetentionTrimsOldestBeyondTheLimit()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path, retention: 2);

        var oldest = store.Add(SolidImage(), CaptureSource.Fullscreen, Instant(0));
        var middle = store.Add(SolidImage(), CaptureSource.Area, Instant(10));
        var newest = store.Add(SolidImage(), CaptureSource.Window, Instant(20));

        Assert.Equal(new[] { newest.Id, middle.Id }, store.All().Select(r => r.Id));
        Assert.False(File.Exists(oldest.FileUrl));
        Assert.False(File.Exists(oldest.ThumbnailUrl));
    }

    [Fact]
    public void SetRetentionTrimsImmediatelyWhenLoweredBelowCount()
    {
        using var dir = new TempDir();
        var store = new HistoryStore(dir.Path, retention: 10);
        var a = store.Add(SolidImage(), CaptureSource.Fullscreen, Instant(0));
        var b = store.Add(SolidImage(), CaptureSource.Area, Instant(10));
        var c = store.Add(SolidImage(), CaptureSource.Window, Instant(20));

        store.SetRetention(1);

        Assert.Equal(1, store.Retention);
        Assert.Equal(new[] { c.Id }, store.All().Select(r => r.Id));
        Assert.False(File.Exists(a.FileUrl));
        Assert.False(File.Exists(b.FileUrl));
    }

    [Fact]
    public void RecordsSurviveAReloadAndTheCallerSuppliesRetention()
    {
        using var dir = new TempDir();
        {
            var store = new HistoryStore(dir.Path, retention: 10);
            store.Add(SolidImage(), CaptureSource.Fullscreen, Instant(0));
            store.Add(SolidImage(), CaptureSource.Area, Instant(10));
            store.Add(SolidImage(), CaptureSource.Window, Instant(20));
        }

        var reloaded = new HistoryStore(dir.Path, retention: 2);

        Assert.Equal(2, reloaded.Retention);
        Assert.Equal(new[] { CaptureSource.Window, CaptureSource.Area, CaptureSource.Fullscreen }, reloaded.All().Select(r => r.Source));
        Assert.NotNull(reloaded.CapturedImage(reloaded.All()[0]));

        reloaded.SetRetention(2);
        Assert.Equal(new[] { CaptureSource.Window, CaptureSource.Area }, reloaded.All().Select(r => r.Source));
    }

    [Fact]
    public void ACaptureAddedAfterAReloadIsTheNewestAndSurvivesAFullStore()
    {
        using var dir = new TempDir();
        {
            var store = new HistoryStore(dir.Path, retention: 2);
            store.Add(SolidImage(), CaptureSource.Fullscreen, Instant(0));
            store.Add(SolidImage(), CaptureSource.Area, Instant(10));
        }

        // Reloaded timestamps must compare in the same zone as new ones, or on a host east of UTC the
        // new capture sorts as the oldest and the full store trims it (and its file) straight away.
        var reloaded = new HistoryStore(dir.Path, retention: 2);
        var newest = reloaded.Add(SolidImage(), CaptureSource.Window, Instant(20));

        Assert.Equal(new[] { CaptureSource.Window, CaptureSource.Area }, reloaded.All().Select(r => r.Source));
        Assert.True(File.Exists(newest.FileUrl));
    }

    [Fact]
    public void AddMediaMovesTheFileInKeepsItsExtensionAndRecordsWhatTheCallerKnows()
    {
        using var dir = new TempDir();
        var historyDir = Path.Combine(dir.Path, "history");
        var store = new HistoryStore(historyDir);
        var take = MovieFixture(dir);
        var thumbnail = SolidImage(8, 6).Data;

        var record = store.Add(take, CaptureKind.Video, 1920, 1080, 12.5, thumbnail, CaptureSource.Recording, Instant(1));

        Assert.False(File.Exists(take)); // moved, not copied
        Assert.Equal(".mp4", Path.GetExtension(record.FileUrl));
        Assert.True(File.Exists(record.FileUrl));
        Assert.Equal(CaptureKind.Video, record.Kind);
        Assert.Equal(12.5, record.Duration);
        Assert.Equal(CaptureSource.Recording, record.Source);
        Assert.Equal(1920, record.PixelWidth);
        Assert.Equal(1080, record.PixelHeight);
        Assert.Equal(thumbnail.ToArray(), File.ReadAllBytes(record.ThumbnailUrl));
        Assert.Null(store.CapturedImage(record)); // not an image
        Assert.Equal(new[] { record.Id }, store.All().Select(r => r.Id));
    }

    [Fact]
    public void RemoveClearAndRetentionDeleteRecordingsExactlyLikeScreenshots()
    {
        using var dir = new TempDir();
        var historyDir = Path.Combine(dir.Path, "history");
        var store = new HistoryStore(historyDir, retention: 2);

        var video = store.Add(MovieFixture(dir), CaptureKind.Video, 10, 10, 1.0, SolidImage().Data, CaptureSource.Recording, Instant(1));
        var shot = store.Add(SolidImage(), CaptureSource.Area, Instant(2));
        var video2Path = Path.Combine(dir.Path, "take2.mp4");
        File.WriteAllBytes(video2Path, new byte[] { 1, 2 });
        var video2 = store.Add(video2Path, CaptureKind.Video, 10, 10, 2.0, SolidImage().Data, CaptureSource.Recording, Instant(3));

        Assert.Equal(new[] { video2.Id, shot.Id }, store.All().Select(r => r.Id));
        Assert.False(File.Exists(video.FileUrl));
        Assert.False(File.Exists(video.ThumbnailUrl));

        store.Remove(video2);
        Assert.False(File.Exists(video2.FileUrl));
        Assert.False(File.Exists(video2.ThumbnailUrl));
        Assert.Null(store.Record(video2.Id));
        Assert.NotNull(store.Record(shot.Id));

        store.Clear();
        Assert.Empty(store.All());
        Assert.False(File.Exists(shot.FileUrl));
    }

    [Fact]
    public void APre0006IndexDecodesEveryRecordAsAScreenshot()
    {
        using var dir = new TempDir();
        var history = Path.Combine(dir.Path, "history");
        Directory.CreateDirectory(history);
        var id = Guid.NewGuid();
        var index = $$"""
        {"records":[{"id":"{{id}}","timestamp":700000000,"source":"area","pixelWidth":40,"pixelHeight":30,"imageFile":"a.png","thumbnailFile":"a-thumb.png"}]}
        """;
        File.WriteAllText(Path.Combine(history, "index.json"), index);
        File.WriteAllBytes(Path.Combine(history, "a.png"), SolidImage().Data.ToArray());

        var store = new HistoryStore(history);
        var records = store.All();
        Assert.Single(records);
        Assert.Equal(id, records[0].Id);
        Assert.Equal(CaptureKind.Screenshot, records[0].Kind);
        Assert.Null(records[0].Duration);
        Assert.Equal(40, store.CapturedImage(records[0])?.PixelWidth);

        store.SetRetention(10);
        var reloaded = new HistoryStore(history).All();
        Assert.Equal(CaptureKind.Screenshot, reloaded.First().Kind);
        Assert.Equal("a.png", Path.GetFileName(reloaded.First().FileUrl));
    }

    [Fact]
    public void AddMediaRefusesWhenRetentionIsOffAndRollsBackAFailedIndexWrite()
    {
        using var dir = new TempDir();
        var historyDir = Path.Combine(dir.Path, "history");
        var off = new HistoryStore(historyDir, retention: 0);
        var take = MovieFixture(dir);

        var ex = Assert.Throws<HistoryException>(() =>
            off.Add(take, CaptureKind.Video, 1, 1, 1.0, SolidImage().Data, CaptureSource.Recording));
        Assert.Equal(HistoryError.HistoryOff, ex.Error);
        Assert.True(File.Exists(take)); // left where it was

        // The index cannot be written because its path is a directory: the file comes back, no record.
        var blockedDir = Path.Combine(dir.Path, "blocked");
        Directory.CreateDirectory(Path.Combine(blockedDir, "index.json"));
        var store = new HistoryStore(blockedDir);

        Assert.ThrowsAny<Exception>(() =>
            store.Add(take, CaptureKind.Video, 1, 1, 1.0, SolidImage().Data, CaptureSource.Recording));

        Assert.True(File.Exists(take));
        Assert.Empty(store.All());
        Assert.Empty(Directory.GetFiles(blockedDir, "*-thumb.png"));
    }
}
