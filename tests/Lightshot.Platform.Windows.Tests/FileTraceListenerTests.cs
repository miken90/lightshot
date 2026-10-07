// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Lightshot.Platform.Windows.Logging;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class FileTraceListenerTests
{
    [Fact]
    [Unit]
    public void WritesTraceMessagesToFileWithTimestamp()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ls_log_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var fixedDate = new DateTime(2026, 10, 7, 14, 30, 0, DateTimeKind.Utc);
            using var listener = new FileTraceListener(dir, clock: () => fixedDate);

            Trace.Listeners.Add(listener);
            try
            {
                Trace.WriteLine("Diagnostic message from Lightshot test");
                Trace.Flush();
            }
            finally
            {
                Trace.Listeners.Remove(listener);
            }

            string expectedFile = Path.Combine(dir, "lightshot-2026-10-07.log");
            Assert.True(File.Exists(expectedFile));

            string content = ReadLogFile(expectedFile);
            Assert.Contains("[2026-10-07 14:30:00.000] Diagnostic message from Lightshot test", content);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    [Unit]
    public void DailyRotationSwitchesToFileWithNewDate()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ls_log_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var simulatedClock = new DateTime(2026, 10, 7, 23, 50, 0, DateTimeKind.Utc);
            using var listener = new FileTraceListener(dir, clock: () => simulatedClock);

            listener.WriteLine("Message before midnight");
            listener.Flush();

            string day1File = Path.Combine(dir, "lightshot-2026-10-07.log");
            Assert.True(File.Exists(day1File));
            string day1Content = ReadLogFile(day1File);
            Assert.Contains("Message before midnight", day1Content);

            // Advance clock past midnight (e.g. 25 minutes later)
            simulatedClock = new DateTime(2026, 10, 8, 0, 15, 0, DateTimeKind.Utc);

            listener.WriteLine("Message after midnight");
            listener.Flush();

            string day2File = Path.Combine(dir, "lightshot-2026-10-08.log");
            Assert.True(File.Exists(day2File));
            string day2Content = ReadLogFile(day2File);
            Assert.Contains("Message after midnight", day2Content);

            // Day 1 file remains unchanged and does not contain the new day's message
            Assert.DoesNotContain("Message after midnight", ReadLogFile(day1File));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    [Unit]
    public void SizeCapPreventsUnboundedFileGrowth()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ls_log_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var fixedDate = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
            // Limit each file to 100 bytes
            using var listener = new FileTraceListener(dir, maxFileSizeBytes: 100, clock: () => fixedDate);

            // Write 100 characters (+ timestamp overhead ~ 28 bytes = ~128 bytes, exceeding 100)
            listener.WriteLine(new string('A', 100));
            listener.Flush();

            string baseFile = Path.Combine(dir, "lightshot-2026-10-07.log");
            Assert.True(File.Exists(baseFile));
            long initialSize = new FileInfo(baseFile).Length;
            Assert.True(initialSize >= 100);

            // Second write triggers rollover to .1.log
            listener.WriteLine(new string('B', 100));
            listener.Flush();

            string part1File = Path.Combine(dir, "lightshot-2026-10-07.1.log");
            Assert.True(File.Exists(part1File));
            string part1Content = ReadLogFile(part1File);
            Assert.Contains(new string('B', 100), part1Content);

            // Base file did not grow
            long finalBaseSize = new FileInfo(baseFile).Length;
            Assert.Equal(initialSize, finalBaseSize);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    [Unit]
    public void FlushForcesPendingBufferToDisk()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ls_log_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var fixedDate = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
            using var listener = new FileTraceListener(dir, clock: () => fixedDate);

            listener.WriteLine("Pending unflushed log message");

            string logFile = Path.Combine(dir, "lightshot-2026-10-07.log");
            Assert.True(File.Exists(logFile));
            // Before Flush(), StreamWriter buffer has not been flushed to disk
            Assert.Equal(0, new FileInfo(logFile).Length);

            listener.Flush();

            // After Flush(), bytes are committed to disk
            Assert.True(new FileInfo(logFile).Length > 0);
            string content = ReadLogFile(logFile);
            Assert.Contains("Pending unflushed log message", content);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static string ReadLogFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
