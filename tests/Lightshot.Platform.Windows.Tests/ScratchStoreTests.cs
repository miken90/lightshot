using System;
using System.IO;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class ScratchStoreTests : IDisposable
{
    private readonly string _tempDir;

    public ScratchStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lightshot_scratch_test_" + Guid.NewGuid().ToString("N"));
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
    public void CreatesDirectoryAndUniqueTakePaths()
    {
        var store = new ScratchStore(_tempDir);
        Assert.True(Directory.Exists(_tempDir));

        string take1 = store.CreateTakePath("screen");
        string take2 = store.CreateTakePath("screen");

        Assert.NotEqual(take1, take2);
        Assert.EndsWith(".frag.mp4", take1);
        Assert.EndsWith(".frag.mp4", take2);

        string partial = store.GetRemuxPartialPath(take1);
        Assert.EndsWith(".mp4.partial", partial);

        string finalPath = store.GetFinalMp4Path(take1);
        Assert.EndsWith(".mp4", finalPath);
        Assert.False(finalPath.EndsWith(".frag.mp4"));
    }

    [Fact]
    [Unit]
    public void CleansStalePartialsAndPreservesTakes()
    {
        var store = new ScratchStore(_tempDir);

        string takeFile = store.CreateTakePath();
        File.WriteAllText(takeFile, "dummy take data");

        string stalePartial1 = Path.Combine(_tempDir, "take1.mp4.partial");
        string stalePartial2 = Path.Combine(_tempDir, "animation.gif.partial");
        File.WriteAllText(stalePartial1, "corrupted remux partial");
        File.WriteAllText(stalePartial2, "corrupted gif partial");

        int deleted = store.CleanStalePartials();
        Assert.Equal(2, deleted);
        Assert.False(File.Exists(stalePartial1));
        Assert.False(File.Exists(stalePartial2));
        Assert.True(File.Exists(takeFile));

        var takes = store.EnumerateTakes();
        Assert.Single(takes);
        Assert.Equal(takeFile, takes[0]);

        bool deleteResult = store.DeleteTake(takeFile);
        Assert.True(deleteResult);
        Assert.False(File.Exists(takeFile));
    }
}
