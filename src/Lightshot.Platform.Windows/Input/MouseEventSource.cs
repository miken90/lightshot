// Ported from LightshotKit/Sources/LightshotKit/InputEventSource.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Lightshot.Core;
using Lightshot.Platform.Windows.Recording;

namespace Lightshot.Platform.Windows.Input;

/// <summary>
/// Windows implementation of IInputEventSource for the mouse.
/// Enqueues low-level mouse hook messages and runs a consumer thread that emits PointerEvent models.
/// Mouse position is seeded at start using GetCursorPos so overlays know the initial pointer coordinate immediately.
/// </summary>
public sealed class MouseEventSource : IInputEventSource, IDisposable
{
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private readonly HookQueue<RawHookMessage> _queue;
    private readonly HookThread? _ownedHookThread;
    private readonly object _lock = new();

    private Thread? _consumerThread;
    private Action<InputEvent>? _onEvent;
    private bool _running;
    private bool _disposed;
    private long _startQpc;

    public MouseEventSource(HookQueue<RawHookMessage>? queue = null, HookThread? hookThread = null)
    {
        if (queue != null)
        {
            _queue = queue;
            _ownedHookThread = hookThread;
        }
        else if (hookThread != null)
        {
            _queue = hookThread.Queue;
            _ownedHookThread = hookThread;
        }
        else
        {
            _queue = new HookQueue<RawHookMessage>();
            _ownedHookThread = new HookThread(HookTypeMask.Mouse, _queue);
        }
    }

    public void Start(Action<InputEvent> onEvent)
    {
        lock (_lock)
        {
            if (_disposed || _running) return;
            _running = true;
            _onEvent = onEvent;
            _startQpc = QpcClock.NowTicks;

            // Seed initial mouse position at start
            if (GetCursorPos(out var pt))
            {
                onEvent(new InputEvent.Pointer(new PointerEvent.Moved(new Point(pt.X, pt.Y))));
            }

            _ownedHookThread?.Start();

            _consumerThread = new Thread(ConsumeQueue)
            {
                IsBackground = true,
                Name = "dev.lightshot.mouse-consumer"
            };
            _consumerThread.Start();
        }
    }

    public void Stop()
    {
        Thread? consumer = null;
        lock (_lock)
        {
            if (!_running) return;
            _running = false;
            consumer = _consumerThread;
            _consumerThread = null;
            _onEvent = null;
        }

        _ownedHookThread?.Stop();

        if (consumer != null && consumer.IsAlive)
        {
            consumer.Join(1000);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
        _ownedHookThread?.Dispose();
    }

    private void ConsumeQueue()
    {
        while (_running)
        {
            bool hadItem = false;
            while (_queue.TryDequeue(out var msg))
            {
                hadItem = true;
                if (msg.Type != HookType.Mouse) continue;

                var callback = _onEvent;
                if (callback == null) break;

                switch (msg.Message)
                {
                    case WM_MOUSEMOVE:
                        callback(new InputEvent.Pointer(new PointerEvent.Moved(new Point(msg.X, msg.Y))));
                        break;
                    case WM_LBUTTONDOWN:
                    case WM_RBUTTONDOWN:
                    case WM_MBUTTONDOWN:
                        callback(new InputEvent.Pointer(new PointerEvent.Down(new Point(msg.X, msg.Y))));
                        break;
                }
            }

            if (!hadItem && _running)
            {
                Thread.Sleep(2);
            }
        }
    }
}
