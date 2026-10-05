using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class TakeFinalizerTests : IDisposable
{
    private readonly string _tempDir;

    public TakeFinalizerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lightshot_finalizer_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private class FakeRemuxer : IRemuxer
    {
        private readonly Func<int, string, string, RemuxResult> _onRemux;
        public int CallCount { get; private set; }

        public FakeRemuxer(Func<int, string, string, RemuxResult> onRemux)
        {
            _onRemux = onRemux;
        }

        public RemuxResult Remux(string inputPath, string outputPath)
        {
            CallCount++;
            return _onRemux(CallCount, inputPath, outputPath);
        }
    }

    [Fact]
    [Unit]
    public void RetriesOnceThenKeepsTakeAndReportsError()
    {
        string takePath = Path.Combine(_tempDir, "sample_take.frag.mp4");
        byte[] originalTakeBytes = new byte[] { 1, 2, 3, 4, 5, 42, 99 };
        File.WriteAllBytes(takePath, originalTakeBytes);

        string destPath = Path.Combine(_tempDir, "delivered.mp4");
        string partialPath = destPath + ".partial";

        var recordedDelays = new List<TimeSpan>();
        var fakeRemuxer = new FakeRemuxer((attempt, input, output) =>
        {
            // Simulate a failing remux that leaves partial junk
            File.WriteAllText(output, $"corrupt partial attempt {attempt}");
            return new RemuxResult(false, 0, 0, $"Remux failed attempt {attempt}");
        });

        var finalizer = new TakeFinalizer(
            fakeRemuxer,
            delay: ts =>
            {
                // Assert remux partial was deleted after first failure before retry delay
                Assert.False(File.Exists(partialPath), "Partial file must be deleted after first failure before retry delay.");
                recordedDelays.Add(ts);
            },
            validator: (take, partial) => true
        );

        var result = finalizer.FinalizeTake(takePath, destPath);

        // 1. Failure reported and nothing delivered
        Assert.False(result.Success);
        Assert.Null(result.DeliveredFilePath);
        Assert.Equal(takePath, result.RecoverableTakePath);
        Assert.Contains("could not be finalised", result.ErrorMessage);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, fakeRemuxer.CallCount);

        // 2. Exactly one retry with injected 2 s delay (no test sleep)
        Assert.Single(recordedDelays);
        Assert.Equal(TimeSpan.FromSeconds(2), recordedDelays[0]);

        // 3. Remux partial deleted after failure
        Assert.False(File.Exists(partialPath), "Partial file must be deleted after failure.");
        Assert.False(File.Exists(destPath), "Final destination file must not exist on failure.");

        // 4. Take is preserved and byte-identical
        Assert.True(File.Exists(takePath), "Take file must be kept intact on permanent failure.");
        byte[] finalTakeBytes = File.ReadAllBytes(takePath);
        Assert.True(originalTakeBytes.SequenceEqual(finalTakeBytes), "Take must remain byte-identical.");
    }

    [Fact]
    [Unit]
    public void SecondAttemptSuccessDeliversNormally()
    {
        string takePath = Path.Combine(_tempDir, "retry_take.frag.mp4");
        File.WriteAllText(takePath, "valid take bytes");

        string destPath = Path.Combine(_tempDir, "delivered_success.mp4");
        string partialPath = destPath + ".partial";

        var recordedDelays = new List<TimeSpan>();
        var fakeRemuxer = new FakeRemuxer((attempt, input, output) =>
        {
            if (attempt == 1)
            {
                File.WriteAllText(output, "first attempt partial");
                return new RemuxResult(false, 0, 0, "Transient antivirus lock");
            }

            File.WriteAllText(output, "valid progressive mp4 content");
            return new RemuxResult(true, 3, 4.5);
        });

        var finalizer = new TakeFinalizer(
            fakeRemuxer,
            delay: ts =>
            {
                // Assert remux partial was deleted after first failure before retry delay
                Assert.False(File.Exists(partialPath), "Partial file must be deleted after first failure before retry delay.");
                recordedDelays.Add(ts);
            },
            validator: (take, partial) => File.ReadAllText(partial).Contains("valid progressive mp4 content")
        );

        var result = finalizer.FinalizeTake(takePath, destPath);

        // 1. Success delivered normally on second attempt
        Assert.True(result.Success);
        Assert.Equal(destPath, result.DeliveredFilePath);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, fakeRemuxer.CallCount);

        // 2. Injected delay triggered once between attempts
        Assert.Single(recordedDelays);
        Assert.Equal(TimeSpan.FromSeconds(2), recordedDelays[0]);

        // 3. Partial file moved to final destination, take deleted
        Assert.False(File.Exists(partialPath));
        Assert.True(File.Exists(destPath));
        Assert.Equal("valid progressive mp4 content", File.ReadAllText(destPath));
        Assert.False(File.Exists(takePath), "Take file must be deleted upon successful finalization.");
    }
}
