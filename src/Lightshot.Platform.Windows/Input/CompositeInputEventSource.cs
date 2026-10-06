// Ported for Lightshot Windows Port (Phase 7 R5)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Input;

/// <summary>
/// Combines multiple IInputEventSource instances (e.g. mouse and keyboard) into a single composite source.
/// </summary>
public sealed class CompositeInputEventSource : IInputEventSource, IDisposable
{
    private readonly IReadOnlyList<IInputEventSource> _sources;
    private bool _disposed;

    public CompositeInputEventSource(params IInputEventSource[] sources)
    {
        _sources = sources ?? Array.Empty<IInputEventSource>();
    }

    public void Start(Action<InputEvent> onEvent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(onEvent);

        foreach (var source in _sources)
        {
            source.Start(onEvent);
        }
    }

    public void Stop()
    {
        foreach (var source in _sources)
        {
            try
            {
                source.Stop();
            }
            catch
            {
                // Suppress errors during shutdown
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();

        foreach (var source in _sources)
        {
            if (source is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch
                {
                    // Suppress disposal errors
                }
            }
        }
    }
}
