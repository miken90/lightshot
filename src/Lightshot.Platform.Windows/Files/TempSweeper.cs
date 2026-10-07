// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;

namespace Lightshot.Platform.Windows.Files;

public sealed record SweepTarget(string Directory, string Pattern, bool FoldersOnly);

public static class TempSweeper
{
    public static int Sweep(IEnumerable<SweepTarget> targets, DateTime cutoffUtc, ISet<string> protectedPaths)
    {
        int deleted = 0;
        foreach (var t in targets)
        {
            try
            {
                if (!Directory.Exists(t.Directory)) continue;
                var root = new DirectoryInfo(t.Directory);
                string fullRoot = Path.GetFullPath(root.FullName);
                if (!fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                {
                    fullRoot += Path.DirectorySeparatorChar;
                }

                if (t.FoldersOnly)
                {
                    foreach (var d in root.EnumerateDirectories(t.Pattern))
                    {
                        if (d.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        if (!Path.GetFullPath(d.FullName).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) continue;
                        if (NewestWriteUtc(d) >= cutoffUtc) continue;
                        if (ContainsProtectedOrLocked(d, protectedPaths)) continue;
                        try
                        {
                            d.Delete(recursive: true);
                            deleted++;
                        }
                        catch { }
                    }
                }
                else
                {
                    foreach (var f in root.EnumerateFiles(t.Pattern))
                    {
                        if (f.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        if (!Path.GetFullPath(f.FullName).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) continue;
                        if (f.LastWriteTimeUtc >= cutoffUtc) continue;
                        string fullPath = Path.GetFullPath(f.FullName);
                        if (protectedPaths.Contains(f.FullName) || protectedPaths.Contains(fullPath)) continue;
                        if (IsLocked(f.FullName)) continue;
                        try
                        {
                            f.Delete();
                            deleted++;
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                // Never let a failure on one target halt other targets
            }
        }

        return deleted;
    }

    private static bool IsLocked(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static DateTime NewestWriteUtc(DirectoryInfo dir)
    {
        var newest = dir.LastWriteTimeUtc;
        try
        {
            foreach (var f in EnumerateFilesSafe(dir))
            {
                if (f.LastWriteTimeUtc > newest)
                {
                    newest = f.LastWriteTimeUtc;
                }
            }
        }
        catch { }
        return newest;
    }

    private static bool ContainsProtectedOrLocked(DirectoryInfo dir, ISet<string> protectedPaths)
    {
        try
        {
            string dirFull = Path.GetFullPath(dir.FullName);
            if (protectedPaths.Contains(dir.FullName) || protectedPaths.Contains(dirFull))
            {
                return true;
            }

            foreach (var f in EnumerateFilesSafe(dir))
            {
                string fFull = Path.GetFullPath(f.FullName);
                if (protectedPaths.Contains(f.FullName) || protectedPaths.Contains(fFull))
                {
                    return true;
                }

                if (IsLocked(f.FullName))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static IEnumerable<FileInfo> EnumerateFilesSafe(DirectoryInfo dir)
    {
        var stack = new Stack<DirectoryInfo>();
        stack.Push(dir);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            FileInfo[] files;
            DirectoryInfo[] subDirs;
            try
            {
                files = current.GetFiles();
                subDirs = current.GetDirectories();
            }
            catch
            {
                continue;
            }

            foreach (var f in files)
            {
                if (!f.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    yield return f;
                }
            }

            foreach (var sub in subDirs)
            {
                if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    stack.Push(sub);
                }
            }
        }
    }
}
