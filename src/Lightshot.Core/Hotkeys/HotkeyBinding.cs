// Ported from LightshotKit/Sources/LightshotKit/HotkeyBinding.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lightshot.Core;

/// <summary>
/// The modifier keys held for a global hotkey.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1 << 0,
    Option = 1 << 1,
    Shift = 1 << 2,
    Command = 1 << 3
}

public static class HotkeyModifiersExtensions
{
    public static string DisplayString(this HotkeyModifiers modifiers)
    {
        string outStr = "";
        if (modifiers.HasFlag(HotkeyModifiers.Control)) outStr += "⌃";
        if (modifiers.HasFlag(HotkeyModifiers.Option)) outStr += "⌥";
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) outStr += "⇧";
        if (modifiers.HasFlag(HotkeyModifiers.Command)) outStr += "⌘";
        return outStr;
    }
}

/// <summary>
/// A single global-hotkey chord: a physical key plus its modifiers.
/// </summary>
public readonly record struct HotkeyBinding(ushort KeyCode, HotkeyModifiers Modifiers, string KeyLabel)
{
    public string DisplayString => Modifiers.DisplayString() + DisplayKey;

    private string DisplayKey
    {
        get
        {
            if (Modifiers.HasFlag(HotkeyModifiers.Shift) && ShiftedDigits.TryGetValue(KeyLabel, out string? digit))
            {
                return digit;
            }
            return KeyLabel.ToUpperInvariant();
        }
    }

    private static readonly Dictionary<string, string> ShiftedDigits = new()
    {
        ["!"] = "1", ["@"] = "2", ["#"] = "3", ["$"] = "4", ["%"] = "5",
        ["^"] = "6", ["&"] = "7", ["*"] = "8", ["("] = "9", [")"] = "0"
    };

    public bool ChordEquals(HotkeyBinding other) =>
        KeyCode == other.KeyCode && Modifiers == other.Modifiers;
}

/// <summary>
/// Two-or-more actions that resolve to the same chord.
/// </summary>
public record HotkeyConflict(HotkeyBinding Binding, IReadOnlyList<CaptureAction> Actions)
{
    public virtual bool Equals(HotkeyConflict? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Binding.ChordEquals(other.Binding) &&
               Actions.SequenceEqual(other.Actions);
    }

    public override int GetHashCode() => HashCode.Combine(Binding.KeyCode, Binding.Modifiers, Actions.Count);
}

/// <summary>
/// The full set of action->chord assignments the app registers and the settings window edits.
/// </summary>
public class HotkeyBindings : IEquatable<HotkeyBindings>
{
    private readonly Dictionary<CaptureAction, HotkeyBinding> _storage;

    public HotkeyBindings(Dictionary<CaptureAction, HotkeyBinding>? storage = null)
    {
        _storage = storage != null ? new Dictionary<CaptureAction, HotkeyBinding>(storage) : [];
    }

    public HotkeyBinding? this[CaptureAction action]
    {
        get => _storage.TryGetValue(action, out HotkeyBinding b) ? b : null;
        set
        {
            if (value is { } b)
            {
                _storage[action] = b;
            }
            else
            {
                _storage.Remove(action);
            }
        }
    }

    public IReadOnlyDictionary<CaptureAction, HotkeyBinding> Assignments => _storage;

    /// <summary>
    /// Windows defaults: PrintScreen for Area, Ctrl+PrintScreen for Fullscreen.
    /// </summary>
    public static HotkeyBindings Defaults => new(new Dictionary<CaptureAction, HotkeyBinding>
    {
        [CaptureAction.Area] = new(0x2C, HotkeyModifiers.None, "PrintScreen"),
        [CaptureAction.Fullscreen] = new(0x2C, HotkeyModifiers.Control, "PrintScreen")
    });

    public IReadOnlyList<HotkeyConflict> Conflicts
    {
        get
        {
            var groups = new Dictionary<(ushort, HotkeyModifiers), (HotkeyBinding Binding, List<CaptureAction> Actions)>();
            // Enumerate in declaration order of CaptureAction
            foreach (CaptureAction action in Enum.GetValues<CaptureAction>())
            {
                if (_storage.TryGetValue(action, out HotkeyBinding binding))
                {
                    var key = (binding.KeyCode, binding.Modifiers);
                    if (!groups.TryGetValue(key, out var entry))
                    {
                        entry = (binding, []);
                        groups[key] = entry;
                    }
                    entry.Actions.Add(action);
                }
            }

            return groups.Values
                .Where(g => g.Actions.Count > 1)
                .Select(g => new HotkeyConflict(g.Binding, g.Actions))
                .ToList();
        }
    }

    public CaptureAction? ConflictingAction(HotkeyBinding chord, CaptureAction? excluding = null)
    {
        foreach (CaptureAction action in Enum.GetValues<CaptureAction>())
        {
            if (excluding.HasValue && action == excluding.Value) continue;
            if (_storage.TryGetValue(action, out HotkeyBinding existing) && existing.ChordEquals(chord))
            {
                return action;
            }
        }
        return null;
    }

    public bool Equals(HotkeyBindings? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_storage.Count != other._storage.Count) return false;
        foreach (var (k, v) in _storage)
        {
            if (!other._storage.TryGetValue(k, out HotkeyBinding otherVal) || !v.Equals(otherVal))
            {
                return false;
            }
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is HotkeyBindings other && Equals(other);

    public override int GetHashCode() => _storage.Count;
}
