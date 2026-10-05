using System;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Shell;

/// <summary>
/// Windows UI thread dispatcher implementing IUiDispatcher via SynchronizationContext.
/// </summary>
public sealed class UiDispatcher : IUiDispatcher
{
    private readonly SynchronizationContext? _syncContext;

    public UiDispatcher(SynchronizationContext? syncContext = null)
    {
        _syncContext = syncContext ?? SynchronizationContext.Current;
    }

    public void Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_syncContext == null || SynchronizationContext.Current == _syncContext)
        {
            action();
        }
        else
        {
            _syncContext.Send(_ => action(), null);
        }
    }

    public Task DispatchAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_syncContext == null || SynchronizationContext.Current == _syncContext)
        {
            return action();
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _syncContext.Post(async _ =>
        {
            try
            {
                await action().ConfigureAwait(false);
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);

        return tcs.Task;
    }
}
