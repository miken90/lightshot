// Ported from LightshotKit/Sources/LightshotKit/InputEventSource.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Lightshot.Platform.Windows.Input;

public enum HookType
{
    Mouse,
    Keyboard
}

/// <summary>
/// Lightweight raw hook message enqueued directly by low-level hook callbacks.
/// Contains pre-captured QPC timestamp and raw Windows message parameters.
/// </summary>
public readonly record struct RawHookMessage
{
    public HookType Type { get; init; }
    public int Message { get; init; }
    public long QpcTimestamp { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public uint VkCode { get; init; }
    public uint ScanCode { get; init; }
    public uint Flags { get; init; }
    public uint MouseData { get; init; }
}

/// <summary>
/// Bounded, lock-free queue for low-level hook callbacks.
/// Callbacks MUST NEVER block under any circumstances: if the queue reaches capacity,
/// items are dropped and Enqueue returns false immediately without waiting or throwing.
/// </summary>
public sealed class HookQueue<T>
{
    private readonly ConcurrentQueue<T> _queue = new();
    private readonly int _capacity;
    private int _count;
    private long _droppedCount;

    public HookQueue(int capacity = 4096)
    {
        _capacity = capacity > 0 ? capacity : 4096;
    }

    public int Capacity => _capacity;

    public int Count => Volatile.Read(ref _count);

    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <summary>
    /// Enqueues an item into the queue. Never blocks. If full, drops the item immediately and returns false.
    /// </summary>
    public bool Enqueue(T item)
    {
        while (true)
        {
            int current = Volatile.Read(ref _count);
            if (current >= _capacity)
            {
                Interlocked.Increment(ref _droppedCount);
                return false;
            }

            if (Interlocked.CompareExchange(ref _count, current + 1, current) == current)
            {
                _queue.Enqueue(item);
                return true;
            }
        }
    }

    public bool TryDequeue(out T item)
    {
        if (_queue.TryDequeue(out item!))
        {
            Interlocked.Decrement(ref _count);
            return true;
        }

        item = default!;
        return false;
    }

    public void Clear()
    {
        while (TryDequeue(out _)) { }
    }
}
