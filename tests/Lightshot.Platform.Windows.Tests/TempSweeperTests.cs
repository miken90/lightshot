// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lightshot.Platform.Windows.Files;
using Lightshot.Platform.Windows.Windows;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class TempSweeperTests
{
    [Fact]
    [Unit]
    public void DeletesOnlyFilesOlderThanTheCutoff()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ls_sweep_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string oldFile = Path.Combine(dir, "old.png");
            string newFile = Path.Combine(dir, "recent.png");
            File.WriteAllText(oldFile, "old content");
            File.WriteAllText(newFile, "recent content");

            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-10));
            File.SetLastWriteTimeUtc(newFile, DateTime.UtcNow.AddDays(-1));

            var target = new SweepTarget(dir, "*.png", FoldersOnly: false);
            var cutoff = DateTime.UtcNow.AddDays(-5);

            int deleted = TempSweeper.Sweep([target], cutoff, new HashSet<string>());

            Assert.Equal(1, deleted);
            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(newFile));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    [Unit]
    public void SkipsAFileThatIsOpen()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ls_sweep_lock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string lockedFile = Path.Combine(dir, "locked.png");
            File.WriteAllText(lockedFile, "locked content");
            File.SetLastWriteTimeUtc(lockedFile, DateTime.UtcNow.AddDays(-10));

            var target = new SweepTarget(dir, "*.png", FoldersOnly: false);
            var cutoff = DateTime.UtcNow.AddDays(-5);

            int deleted;
            using (var stream = File.Open(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                deleted = TempSweeper.Sweep([target], cutoff, new HashSet<string>());
            }

            Assert.Equal(0, deleted);
            Assert.True(File.Exists(lockedFile));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    [Unit]
    public void SkipsPathsReferencedByHistory()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ls_sweep_ref_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string refFile = Path.Combine(dir, "referenced.png");
            string unrefFile = Path.Combine(dir, "unreferenced.png");
            File.WriteAllText(refFile, "referenced");
            File.WriteAllText(unrefFile, "unreferenced");

            File.SetLastWriteTimeUtc(refFile, DateTime.UtcNow.AddDays(-10));
            File.SetLastWriteTimeUtc(unrefFile, DateTime.UtcNow.AddDays(-10));

            var target = new SweepTarget(dir, "*.png", FoldersOnly: false);
            var cutoff = DateTime.UtcNow.AddDays(-5);
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(refFile) };

            int deleted = TempSweeper.Sweep([target], cutoff, keep);

            Assert.Equal(1, deleted);
            Assert.True(File.Exists(refFile));
            Assert.False(File.Exists(unrefFile));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    [Unit]
    public void DeletesDragFolderOnlyWhenAllContentIsOld()
    {
        string rootDir = Path.Combine(Path.GetTempPath(), $"ls_sweep_drag_{Guid.NewGuid():N}");
        string folderA = Path.Combine(rootDir, "all_old");
        string folderB = Path.Combine(rootDir, "has_recent");
        Directory.CreateDirectory(folderA);
        Directory.CreateDirectory(folderB);

        try
        {
            string old1 = Path.Combine(folderA, "f1.png");
            string old2 = Path.Combine(folderA, "f2.png");
            File.WriteAllText(old1, "old 1");
            File.WriteAllText(old2, "old 2");
            File.SetLastWriteTimeUtc(old1, DateTime.UtcNow.AddDays(-10));
            File.SetLastWriteTimeUtc(old2, DateTime.UtcNow.AddDays(-8));
            Directory.SetLastWriteTimeUtc(folderA, DateTime.UtcNow.AddDays(-8));

            string oldInB = Path.Combine(folderB, "old.png");
            string recentInB = Path.Combine(folderB, "recent.png");
            File.WriteAllText(oldInB, "old");
            File.WriteAllText(recentInB, "recent");
            File.SetLastWriteTimeUtc(oldInB, DateTime.UtcNow.AddDays(-10));
            File.SetLastWriteTimeUtc(recentInB, DateTime.UtcNow.AddDays(-1));
            Directory.SetLastWriteTimeUtc(folderB, DateTime.UtcNow.AddDays(-1));

            var target = new SweepTarget(rootDir, "*", FoldersOnly: true);
            var cutoff = DateTime.UtcNow.AddDays(-5);

            int deleted = TempSweeper.Sweep([target], cutoff, new HashSet<string>());

            Assert.Equal(1, deleted);
            Assert.False(Directory.Exists(folderA));
            Assert.True(Directory.Exists(folderB));
        }
        finally
        {
            try { Directory.Delete(rootDir, true); } catch { }
        }
    }

    [Fact]
    [Unit]
    public void MissingDirectoryIsIgnored()
    {
        string missingDir = Path.Combine(Path.GetTempPath(), $"ls_nonexistent_{Guid.NewGuid():N}");
        var target = new SweepTarget(missingDir, "*", FoldersOnly: false);
        var cutoff = DateTime.UtcNow.AddDays(-5);

        int deleted = TempSweeper.Sweep([target], cutoff, new HashSet<string>());

        Assert.Equal(0, deleted);
    }

    [Fact]
    [Unit]
    public void DefaultTargetsCoverBothScratchRoots()
    {
        var targets = AutoClearTargets.Default();

        string scratchRoot1 = Path.Combine(AppPaths.LocalData, "Lightshot Recordings");
        string scratchRoot2 = Path.Combine(KnownFolders.LocalApplicationData, "Lightshot", "Recordings");

        var target1 = targets.FirstOrDefault(t => string.Equals(Path.GetFullPath(t.Directory), Path.GetFullPath(scratchRoot1), StringComparison.OrdinalIgnoreCase));
        var target2 = targets.FirstOrDefault(t => string.Equals(Path.GetFullPath(t.Directory), Path.GetFullPath(scratchRoot2), StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(target1);
        Assert.NotNull(target2);

        // Asserts *.partial pattern only for both scratch roots
        Assert.Equal("*.partial", target1.Pattern);
        Assert.False(target1.FoldersOnly);

        Assert.Equal("*.partial", target2.Pattern);
        Assert.False(target2.FoldersOnly);

        // Asserts temp locations are covered
        Assert.Contains(targets, t => string.Equals(Path.GetFullPath(t.Directory), Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && t.Pattern == "Lightshot_*.png");
        Assert.Contains(targets, t => string.Equals(Path.GetFullPath(t.Directory), Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Lightshot Drag")), StringComparison.OrdinalIgnoreCase) && t.FoldersOnly);
        Assert.Contains(targets, t => string.Equals(Path.GetFullPath(t.Directory), Path.GetFullPath(AppPaths.Logs), StringComparison.OrdinalIgnoreCase) && t.Pattern == "*.log");
        Assert.Contains(targets, t => string.Equals(Path.GetFullPath(t.Directory), Path.GetFullPath(AppPaths.Temp), StringComparison.OrdinalIgnoreCase));
    }
}
