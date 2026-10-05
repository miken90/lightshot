// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lightshot.App.Views.History;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class HistoryViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly HistoryStore _store;
    private readonly FakeRecycleBin _recycleBin;
    private readonly FakeExplorerService _explorerService;

    public HistoryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Lightshot_HistoryTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new HistoryStore(_tempDir, retention: 50, thumbnailer: new SkiaThumbnailer());
        _recycleBin = new FakeRecycleBin();
        _explorerService = new FakeExplorerService();
    }

    [Fact]
    [Unit]
    public void ReopenRevealDelete()
    {
        CapturedImage? reopenedImage = null;
        CapturedImage? copiedImage = null;

        var vm = new HistoryViewModel(
            _store,
            _recycleBin,
            _explorerService,
            onReopen: img => reopenedImage = img,
            onCopy: img => copiedImage = img);

        // 1. Populate store with 3 screenshots
        var dummyData = CreateDummyPng();
        var img1 = new CapturedImage(100, 80, dummyData);
        var img2 = new CapturedImage(200, 160, dummyData);
        var img3 = new CapturedImage(300, 240, dummyData);

        var rec1 = _store.Add(img1, CaptureSource.Area, DateTime.UtcNow.AddMinutes(-10));
        var rec2 = _store.Add(img2, CaptureSource.Fullscreen, DateTime.UtcNow.AddMinutes(-5));
        var rec3 = _store.Add(img3, CaptureSource.Window, DateTime.UtcNow);

        vm.Refresh();
        Assert.Equal(3, vm.Records.Count);

        // Records ordered newest first: rec3, rec2, rec1
        Assert.Equal(rec3.Id, vm.Records[0].Id);
        Assert.Equal(rec2.Id, vm.Records[1].Id);
        Assert.Equal(rec1.Id, vm.Records[2].Id);

        // 2. Test Reopen
        vm.Reopen(rec1);
        Assert.NotNull(reopenedImage);
        Assert.Equal(100, reopenedImage.Value.PixelWidth);
        Assert.Equal(80, reopenedImage.Value.PixelHeight);

        // 3. Test Copy
        vm.Copy(rec2);
        Assert.NotNull(copiedImage);
        Assert.Equal(200, copiedImage.Value.PixelWidth);
        Assert.Equal(160, copiedImage.Value.PixelHeight);

        // 4. Test Reveal
        vm.Reveal(rec2);
        Assert.Contains(rec2.FileUrl, _explorerService.RevealedPaths);

        // 5. Test Delete to Recycle Bin
        vm.Delete(rec3);
        Assert.Contains(rec3.FileUrl, _recycleBin.DeletedPaths);
        Assert.Contains(rec3.ThumbnailUrl, _recycleBin.DeletedPaths);

        // rec3 should now be gone from model and store
        Assert.Equal(2, vm.Records.Count);
        Assert.Null(_store.Record(rec3.Id));
        Assert.DoesNotContain(vm.Records, r => r.Id == rec3.Id);

        // 6. Test Retention change
        vm.SetRetention(1);
        Assert.Single(vm.Records);
        Assert.Equal(rec2.Id, vm.Records[0].Id); // Newest kept

        // 7. Test Clear All
        vm.ClearAll();
        Assert.Empty(vm.Records);
        Assert.True(vm.IsEmpty);
    }

    private static byte[] CreateDummyPng()
    {
        // 1x1 transparent PNG bytes
        return
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private sealed class FakeRecycleBin : IRecycleBin
    {
        public List<string> DeletedPaths { get; } = [];

        public void DeleteToRecycleBin(string filePath)
        {
            DeletedPaths.Add(filePath);
        }
    }

    private sealed class FakeExplorerService : IExplorerService
    {
        public List<string> RevealedPaths { get; } = [];

        public void RevealInExplorer(string filePath)
        {
            RevealedPaths.Add(filePath);
        }
    }
}
