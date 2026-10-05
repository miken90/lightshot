// Ported from LightshotKit/Sources/LightshotKit/HistoryStore.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

public enum HistoryError
{
    UnreadableMedia,
    HistoryOff
}

public class HistoryException : Exception
{
    public HistoryError Error { get; }

    public HistoryException(string message, HistoryError error)
        : base(message)
    {
        Error = error;
    }
}
