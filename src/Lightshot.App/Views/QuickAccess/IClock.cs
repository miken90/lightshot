// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Threading;

namespace Lightshot.App.Views.QuickAccess;

/// <summary>
/// Clock and scheduling abstraction enabling unit tests with a fake clock.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
    IDisposable Schedule(TimeSpan delay, Action callback);
}

public class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTime UtcNow => DateTime.UtcNow;

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        Timer? timer = null;
        timer = new Timer(_ =>
        {
            try
            {
                callback();
            }
            finally
            {
                timer?.Dispose();
            }
        }, null, delay, Timeout.InfiniteTimeSpan);

        return new TimerSubscription(timer);
    }

    private sealed class TimerSubscription : IDisposable
    {
        private Timer? _timer;

        public TimerSubscription(Timer timer)
        {
            _timer = timer;
        }

        public void Dispose()
        {
            var t = Interlocked.Exchange(ref _timer, null);
            t?.Dispose();
        }
    }
}
