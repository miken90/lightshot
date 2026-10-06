// Ported for Lightshot Windows Port (Phase 7 R5)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.Platform.Windows.Input;

namespace Lightshot.Platform.Windows.Recording;

/// <summary>
/// Creates and manages the lifecycle of input event sources (mouse, keyboard, or composite)
/// for cursor click highlighting and keystroke overlays.
/// </summary>
internal sealed class RecordingInputSession : IDisposable
{
    private readonly IInputEventSource? _ownedSource;
    private readonly IInputEventSource? _effectiveSource;
    private bool _disposed;

    public IInputEventSource? EffectiveSource => _effectiveSource;

    public RecordingInputSession(RecordingOptions options, IInputEventSource? injectedSource = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (injectedSource != null)
        {
            _effectiveSource = injectedSource;
            _ownedSource = null;
            return;
        }

        if (options.HighlightClicks && options.ShowKeystrokes)
        {
            var mouse = new MouseEventSource();
            var key = new KeyEventSource();
            _ownedSource = new CompositeInputEventSource(mouse, key);
        }
        else if (options.HighlightClicks)
        {
            _ownedSource = new MouseEventSource();
        }
        else if (options.ShowKeystrokes)
        {
            _ownedSource = new KeyEventSource();
        }

        _effectiveSource = _ownedSource;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ownedSource is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Suppress disposal errors during teardown
            }
        }
    }
}
