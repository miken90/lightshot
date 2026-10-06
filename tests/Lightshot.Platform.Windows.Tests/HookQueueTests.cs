using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Platform.Windows.Input;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class HookQueueTests
{
    [Fact]
    [Unit]
    public void CallbackNeverBlocks()
    {
        // Small capacity to force queue saturation quickly
        const int capacity = 100;
        var queue = new HookQueue<RawHookMessage>(capacity);

        // Fake hook feeder callback simulating a high-frequency OS hook
        bool Callback(int messageId)
        {
            var msg = new RawHookMessage
            {
                Type = HookType.Mouse,
                Message = messageId,
                QpcTimestamp = Stopwatch.GetTimestamp(),
                X = messageId,
                Y = messageId
            };
            return queue.Enqueue(msg);
        }

        // Fill queue to capacity
        for (int i = 0; i < capacity; i++)
        {
            Assert.True(Callback(i));
        }

        Assert.Equal(capacity, queue.Count);
        Assert.Equal(0, queue.DroppedCount);

        // Feed an aggressive burst of 10,000 events into the saturated queue across multiple threads
        // The callback MUST NEVER block or hang, even when saturated
        const int burstCount = 10_000;
        var sw = Stopwatch.StartNew();

        Parallel.For(0, burstCount, i =>
        {
            // Must return immediately (false because queue is full)
            bool enqueued = Callback(i + capacity);
            Assert.False(enqueued);
        });

        sw.Stop();

        // 10,000 callback invocations on saturated queue must complete in well under 1 second
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Callbacks blocked or were too slow: {sw.ElapsedMilliseconds} ms");

        // Queue count remains at capacity, dropped count matches the burst
        Assert.Equal(capacity, queue.Count);
        Assert.Equal(burstCount, queue.DroppedCount);

        // Consumer can still cleanly drain the initial items in FIFO order
        for (int i = 0; i < capacity; i++)
        {
            Assert.True(queue.TryDequeue(out var dequeued));
            Assert.Equal(i, dequeued.Message);
        }

        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryDequeue(out _));
    }
}
