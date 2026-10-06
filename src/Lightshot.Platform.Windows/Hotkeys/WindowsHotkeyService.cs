using System;
using System.Collections.Generic;
using System.Linq;
using Lightshot.Core;
using Lightshot.Platform.Windows.Shell;

namespace Lightshot.Platform.Windows.Hotkeys;

/// <summary>
/// Windows implementation of IHotkeyService managing global hotkey chords via RegisterHotKey.
/// </summary>
public sealed class WindowsHotkeyService : IHotkeyService, IDisposable
{
    private readonly ShellThread? _shellThread;
    private readonly bool _ownsShellThread;
    private HotkeyWindow? _window;
    private readonly PrintScreenHook _printScreenHook;
    private Action<CaptureAction>? _currentHandler;
    private bool _disposed;

    public WindowsHotkeyService(ShellThread? shellThread = null)
    {
        // Same thread and handler as WM_HOTKEY, so a claimed PrintScreen press dispatches identically.
        _printScreenHook = new PrintScreenHook(action => _shellThread?.Post(() => _currentHandler?.Invoke(action)));

        if (shellThread != null)
        {
            _shellThread = shellThread;
            _ownsShellThread = false;
            _shellThread.Invoke(() =>
            {
                _window = new HotkeyWindow();
                _window.HotkeyTriggered = OnHotkeyFired;
            });
        }
        else
        {
            _shellThread = new ShellThread("LightshotHotkeyThread");
            _ownsShellThread = true;
            _shellThread.Invoke(() =>
            {
                _window = new HotkeyWindow();
                _window.HotkeyTriggered = OnHotkeyFired;
            });
        }
    }

    public IReadOnlyList<CaptureAction> Register(HotkeyBindings bindings, Action<CaptureAction> handler)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _currentHandler = handler;

        var printScreenChords = new Dictionary<HotkeyModifiers, CaptureAction>();
        var failedActions = _shellThread!.Invoke(() =>
        {
            _window!.UnregisterAll();

            var failed = new List<CaptureAction>();
            var claimedChords = new HashSet<(ushort, HotkeyModifiers)>();

            // Enumerate in deterministic order
            foreach (var (action, binding) in bindings.Assignments.OrderBy(kv => kv.Key))
            {
                var chordKey = (binding.KeyCode, binding.Modifiers);

                // Detect duplicate chord conflict in the requested bindings set
                if (claimedChords.Contains(chordKey))
                {
                    failed.Add(action);
                    continue;
                }

                if (_window.TryRegister(action, binding, out _))
                {
                    claimedChords.Add(chordKey);
                    if (binding.KeyCode == PrintScreenHook.VK_SNAPSHOT)
                    {
                        printScreenChords[binding.Modifiers] = action;
                    }
                }
                else
                {
                    failed.Add(action);
                }
            }

            return (IReadOnlyList<CaptureAction>)failed;
        });

        // Only chords RegisterHotKey granted: one held by another app stays reported as failed.
        _printScreenHook.SetChords(printScreenChords);
        return failedActions;
    }

    public void UnregisterAll()
    {
        if (_disposed || _shellThread == null) return;
        _printScreenHook.SetChords(new Dictionary<HotkeyModifiers, CaptureAction>());
        _shellThread.Invoke(() =>
        {
            _window?.UnregisterAll();
        });
    }

    private void OnHotkeyFired(CaptureAction action, long qpcTimestamp)
    {
        _currentHandler?.Invoke(action);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _printScreenHook.Dispose();

        if (_shellThread != null)
        {
            _shellThread.Invoke(() =>
            {
                _window?.Dispose();
                _window = null;
            });

            if (_ownsShellThread)
            {
                _shellThread.Dispose();
            }
        }
    }
}
