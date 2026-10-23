using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Lightshot.Core;
using Lightshot.Platform.Windows.Windows;

namespace Lightshot.App;

/// <summary>
/// Named events the my-utilities hub sets to run a command in this (possibly tray-less) instance.
/// Area capture stays on <see cref="Program.ActivateEventBaseName"/>. The base names are the contract:
/// hub-contract.json lists them in "signals" and the hub manifest refers to them.
/// </summary>
public static class HubCommands
{
    public const string FullscreenBaseName = @"Local\Lightshot.Cmd.Fullscreen";
    public const string WindowBaseName = @"Local\Lightshot.Cmd.Window";
    public const string RepeatLastBaseName = @"Local\Lightshot.Cmd.RepeatLast";
    public const string RecordBaseName = @"Local\Lightshot.Cmd.Record";
    public const string OpenImageBaseName = @"Local\Lightshot.Cmd.OpenImage";
    public const string HistoryBaseName = @"Local\Lightshot.Cmd.History";

    private static readonly (string BaseName, Action<AppController> Handler)[] Table =
    [
        (FullscreenBaseName, c => c.TriggerCaptureAction(CaptureAction.Fullscreen)),
        (WindowBaseName, c => c.TriggerCaptureAction(CaptureAction.Window)),
        (RepeatLastBaseName, c => c.TriggerCaptureAction(CaptureAction.RepeatLast)),
        (RecordBaseName, c => c.TriggerCaptureAction(CaptureAction.RecordScreen)),
        (OpenImageBaseName, c => c.Coordinator.OpenFile()),
        (HistoryBaseName, c => c.ShowHistoryWindow()),
    ];

    public static IReadOnlyList<string> BaseNames { get; } = Table.Select(t => t.BaseName).ToArray();

    /// <summary>
    /// Creates one auto-reset event per command, scoped like Quit and Activate, and runs its handler on
    /// the UI dispatcher (Open image shows a dialog; capture and recording build WPF windows).
    /// Dispose unregisters the waits and closes the events.
    /// </summary>
    public static IDisposable Listen(AppController controller, Dispatcher dispatcher)
    {
        var listener = new Listener();
        foreach (var (baseName, handler) in Table)
        {
            var handle = new EventWaitHandle(false, EventResetMode.AutoReset, AppInstance.ScopedName(baseName));
            listener.Add(handle, ThreadPool.RegisterWaitForSingleObject(
                handle,
                (_, _) => dispatcher.BeginInvoke(new Action(() => handler(controller))),
                null,
                -1,
                false));
        }
        return listener;
    }

    private sealed class Listener : IDisposable
    {
        private readonly List<(EventWaitHandle Handle, RegisteredWaitHandle Wait)> _entries = [];

        public void Add(EventWaitHandle handle, RegisteredWaitHandle wait) => _entries.Add((handle, wait));

        public void Dispose()
        {
            foreach (var (handle, wait) in _entries)
            {
                wait.Unregister(null);
                handle.Dispose();
            }
            _entries.Clear();
        }
    }
}
