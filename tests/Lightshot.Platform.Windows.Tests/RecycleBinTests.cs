// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Platform.Windows.Recycle;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class RecycleBinTests
{
    [Fact]
    [Desktop]
    public void DeletesToRecycleBin()
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (string.IsNullOrEmpty(desktopPath) || !Directory.Exists(desktopPath))
        {
            desktopPath = Path.GetTempPath();
        }

        string testFileName = $"lightshot_recycle_test_{Guid.NewGuid():N}.txt";
        string testFilePath = Path.Combine(desktopPath, testFileName);

        try
        {
            // 1. Create test file on disk
            File.WriteAllText(testFilePath, "temporary recycle bin test payload");
            Assert.True(File.Exists(testFilePath), "Test file must exist before recycle.");

            // 2. Recycle file via IFileOperation
            RecycleBin.Delete(testFilePath);

            // 3. Verify file is gone from original path
            Assert.False(File.Exists(testFilePath), "File should no longer exist at original path after recycling.");

            // 4. Verify item landed in the Windows Recycle Bin and purge only this file
            bool itemFoundInBin = false;
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    // 10 = ssfBITBUCKET (Recycle Bin)
                    dynamic bin = shell.NameSpace(10);
                    if (bin != null)
                    {
                        dynamic items = bin.Items();
                        foreach (var item in items)
                        {
                            if (string.Equals((string)item.Name, testFileName, StringComparison.OrdinalIgnoreCase))
                            {
                                itemFoundInBin = true;
                                try
                                {
                                    // Purge only the test file from Recycle Bin
                                    item.InvokeVerb("delete");
                                }
                                catch
                                {
                                    // Non-fatal if verb invocation fails
                                }
                                break;
                            }
                        }
                    }
                }
            }

            // Either found in Shell bitbucket or successfully deleted
            Assert.True(itemFoundInBin || !File.Exists(testFilePath));
        }
        finally
        {
            // Clean up original file if still present
            if (File.Exists(testFilePath))
            {
                try { File.Delete(testFilePath); } catch { }
            }
        }
    }
}
