using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Platform.Windows.Interop;

namespace Lightshot.Platform.Windows.Shell;

/// <summary>
/// Dedicated Single-Threaded Apartment (STA) shell thread that owns
/// HotkeyWindow, OverlayWindow, and tray message loops.
/// </summary>
public sealed class ShellThread : IDisposable
{
    private readonly Thread _thread;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private volatile bool _disposed;

    public uint ThreadId { get; private set; }
    public int ManagedThreadId => _thread.ManagedThreadId;

    public ShellThread(string threadName = "LightshotShellThread")
    {
        _thread = new Thread(ThreadProc)
        {
            IsBackground = true,
            Name = threadName
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    private void ThreadProc()
    {
        Win32Window.EnsurePerMonitorDpiV2();
        ThreadId = Win32Window.GetCurrentThreadId();
        _ready.Set();

        while (!_disposed)
        {
            // Pump all pending Win32 messages for windows created on this thread
            while (Win32Window.PeekMessageW(out var msg, IntPtr.Zero, 0, 0, 1))
            {
                if (msg.message == 0x0012 /* WM_QUIT */)
                {
                    _disposed = true;
                    break;
                }
                Win32Window.TranslateMessage(ref msg);
                Win32Window.DispatchMessageW(ref msg);
            }

            // Process queued actions
            while (_queue.TryTake(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"ShellThread action error: {ex}");
                }
            }

            if (_disposed) break;

            // Wait a brief slice if no message or action to avoid busy looping while maintaining sub-millisecond responsiveness
            Thread.Sleep(2);
        }
    }

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId)
        {
            action();
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        tcs.Task.GetAwaiter().GetResult();
    }

    public T Invoke<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId)
        {
            return func();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task.GetAwaiter().GetResult();
    }

    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId)
        {
            return Task.FromResult(func());
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!_disposed)
        {
            _queue.Add(action);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        _ready.Dispose();
        _thread.Join(1500);
    }
}
