// Ported from LightshotKit/Sources/LightshotKit/ThreadDispatcher.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Threading.Tasks;

namespace Lightshot.Core;

/// <summary>
/// Replaces Swift @MainActor: provides execution on the UI thread or synchronously for tests.
/// </summary>
public interface IUiDispatcher
{
    void Dispatch(Action action);
    Task DispatchAsync(Func<Task> action);
}

/// <summary>
/// Synchronous dispatcher for tests and non-UI environments.
/// </summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public static readonly ImmediateDispatcher Instance = new();

    public void Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }

    public Task DispatchAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action();
    }
}
