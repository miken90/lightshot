// Ported from LightshotKit/Sources/LightshotKit/KeystrokeOverlay.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lightshot.Core;

public enum KeystrokeDisplayMode
{
    AllKeys,
    CommandOnly
}

public enum KeystrokeOverlayPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

public static class KeystrokeOverlayPositionExtensions
{
    public static bool IsTop(this KeystrokeOverlayPosition position) =>
        position is KeystrokeOverlayPosition.TopLeft or KeystrokeOverlayPosition.TopCenter or KeystrokeOverlayPosition.TopRight;

    public static Rect Rect(this KeystrokeOverlayPosition position, Size size, Size frame, double margin)
    {
        double x = position switch
        {
            KeystrokeOverlayPosition.TopLeft or KeystrokeOverlayPosition.BottomLeft => margin,
            KeystrokeOverlayPosition.TopCenter or KeystrokeOverlayPosition.BottomCenter => (frame.Width - size.Width) / 2.0,
            KeystrokeOverlayPosition.TopRight or KeystrokeOverlayPosition.BottomRight => frame.Width - margin - size.Width,
            _ => margin
        };
        double y = position.IsTop() ? margin : frame.Height - margin - size.Height;
        return new Rect(x, y, size.Width, size.Height);
    }
}

public enum KeystrokeOverlaySize
{
    Small,
    Medium,
    Large
}

public static class KeystrokeOverlaySizeExtensions
{
    public static double FontSize(this KeystrokeOverlaySize size) => size switch
    {
        KeystrokeOverlaySize.Small => 16.0,
        KeystrokeOverlaySize.Medium => 22.0,
        KeystrokeOverlaySize.Large => 30.0,
        _ => 22.0
    };
}

public enum KeystrokeOverlayAppearance
{
    Light,
    Dark,
    System
}

public record KeystrokeOverlaySettings
{
    public KeystrokeDisplayMode Mode { get; set; } = KeystrokeDisplayMode.AllKeys;
    public KeystrokeOverlayPosition Position { get; set; } = KeystrokeOverlayPosition.BottomCenter;
    public KeystrokeOverlaySize Size { get; set; } = KeystrokeOverlaySize.Medium;
    public KeystrokeOverlayAppearance Appearance { get; set; } = KeystrokeOverlayAppearance.System;
    public bool BlurBackground { get; set; } = true;

    public KeystrokeOverlaySettings() { }

    public KeystrokeOverlaySettings(
        KeystrokeDisplayMode mode = KeystrokeDisplayMode.AllKeys,
        KeystrokeOverlayPosition position = KeystrokeOverlayPosition.BottomCenter,
        KeystrokeOverlaySize size = KeystrokeOverlaySize.Medium,
        KeystrokeOverlayAppearance appearance = KeystrokeOverlayAppearance.System,
        bool blurBackground = true)
    {
        Mode = mode;
        Position = position;
        Size = size;
        Appearance = appearance;
        BlurBackground = blurBackground;
    }

    public static readonly KeystrokeOverlaySettings Standard = new();
}

public readonly record struct KeystrokeItem(string Text, double Opacity, double Scale);

public class KeystrokeOverlayModel
{
    public const double HoldDuration = 1.5;
    public const double FadeDuration = 0.35;
    public const double BumpDuration = 0.15;
    public const double BumpScale = 1.12;
    public const int MaxCharacters = 28;

    public KeystrokeOverlaySettings Settings { get; }
    public bool SecureInput { get; private set; }
    public KeyModifiers HeldModifiers { get; private set; } = KeyModifiers.None;

    private sealed class Token
    {
        public string Text { get; set; }
        public bool IsTyping { get; }
        public int Count { get; set; } = 1;

        public Token(string text, bool isTyping)
        {
            Text = text;
            IsTyping = isTyping;
        }

        public string Display => Count > 1 ? $"{Text} ×{Count}" : Text;
    }

    private sealed class Burst
    {
        public List<Token> Tokens { get; }
        public double LastTime { get; set; }
        public double BumpTime { get; set; }

        public Burst(List<Token> tokens, double lastTime, double bumpTime)
        {
            Tokens = tokens;
            LastTime = lastTime;
            BumpTime = bumpTime;
        }
    }

    private Burst? _burst;

    public KeystrokeOverlayModel(KeystrokeOverlaySettings settings)
    {
        Settings = settings;
    }

    public void Handle(KeyEvent @event, double time)
    {
        switch (@event)
        {
            case KeyEvent.KeyDown kd:
                KeyDown(kd.Press, time);
                break;
            case KeyEvent.ModifiersChanged mc:
                ModifiersChanged(mc.Modifiers);
                break;
            case KeyEvent.SecureInput si:
                SetSecureInput(si.On);
                break;
        }
    }

    public void KeyDown(KeyPress press, double time)
    {
        if (SecureInput) return;
        if (Settings.Mode == KeystrokeDisplayMode.CommandOnly && !press.Modifiers.IsCommandChord()) return;

        if (_burst != null && time - _burst.LastTime >= HoldDuration + FadeDuration)
        {
            _burst = null;
        }

        if (press.IsRepeat && _burst != null)
        {
            _burst.LastTime = time;
            return;
        }

        var current = _burst ?? new Burst([], time, time);
        string? typed = TypedCharacter(press);

        if (typed != null)
        {
            if (current.Tokens.Count > 0 && current.Tokens[^1].IsTyping)
            {
                current.Tokens[^1].Text += typed;
            }
            else
            {
                current.Tokens.Add(new Token(typed, isTyping: true));
            }
        }
        else if (current.Tokens.Count > 0 && !current.Tokens[^1].IsTyping && current.Tokens[^1].Text == press.Text)
        {
            current.Tokens[^1].Count++;
        }
        else
        {
            current.Tokens.Add(new Token(press.Text, isTyping: false));
        }

        current.LastTime = time;
        current.BumpTime = time;
        _burst = current;
    }

    private static string? TypedCharacter(KeyPress press)
    {
        if (press.Modifiers != KeyModifiers.None && press.Modifiers != KeyModifiers.Shift) return null;
        if (press.Label == "Space") return "␣";
        if (press.Label.Length == 1 && !KeyLabel.IsNamed(press.Label)) return press.Label;
        return null;
    }

    public void ModifiersChanged(KeyModifiers modifiers)
    {
        HeldModifiers = SecureInput ? KeyModifiers.None : modifiers;
    }

    public void SetSecureInput(bool on)
    {
        SecureInput = on;
        if (on)
        {
            _burst = null;
            HeldModifiers = KeyModifiers.None;
        }
    }

    public void Prune(double time)
    {
        if (_burst != null && time - _burst.LastTime >= HoldDuration + FadeDuration)
        {
            _burst = null;
        }
    }

    public IReadOnlyList<KeystrokeItem> Items(double time)
    {
        if (SecureInput) return [];

        var result = new List<KeystrokeItem>();
        bool fresh = false;

        if (_burst != null && time >= _burst.LastTime)
        {
            double age = time - _burst.LastTime;
            double opacity = age < HoldDuration ? 1.0 : Math.Max(0.0, 1.0 - (age - HoldDuration) / FadeDuration);
            if (opacity > 0)
            {
                double sinceBump = time - _burst.BumpTime;
                double scale = sinceBump < BumpDuration ? 1.0 + (BumpScale - 1.0) * (1.0 - sinceBump / BumpDuration) : 1.0;
                string joined = string.Join(" ", _burst.Tokens.Select(t => t.Display));
                result.Add(new KeystrokeItem(Clipped(joined), opacity, scale));
            }
            fresh = age < HoldDuration;
        }

        if (HeldModifiers != KeyModifiers.None && !fresh)
        {
            result.Add(new KeystrokeItem(HeldModifiers.Glyphs(), 1.0, 1.0));
        }

        return result;
    }

    private static string Clipped(string text)
    {
        if (text.Length <= MaxCharacters) return text;
        return "…" + text[^ (MaxCharacters - 1)..];
    }
}
