// Ported from LightshotKit/Sources/LightshotKit/InputEventSource.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Lightshot.Core;
using Lightshot.Platform.Windows.Recording;

namespace Lightshot.Platform.Windows.Input;

/// <summary>
/// Windows implementation of IInputEventSource for the keyboard.
/// Consumer thread processes raw keyboard messages from HookQueue, maintains modifier state,
/// translates keys into Core KeyPress models via KeyTranslator, and coordinates with SecureInputProbe.
/// </summary>
public sealed class KeyEventSource : IInputEventSource, IDisposable
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    private readonly HookQueue<RawHookMessage> _queue;
    private readonly HookThread? _ownedHookThread;
    private readonly SecureInputProbe? _secureInputProbe;
    private readonly bool _ownsSecureProbe;
    private readonly object _lock = new();

    private Thread? _consumerThread;
    private Action<InputEvent>? _onEvent;
    private KeyModifiers _currentModifiers = KeyModifiers.None;
    private uint _lastVkCode;
    private bool _running;
    private bool _disposed;
    private long _startQpc;

    public KeyEventSource(
        HookQueue<RawHookMessage>? queue = null,
        HookThread? hookThread = null,
        SecureInputProbe? secureInputProbe = null)
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
            _ownedHookThread = new HookThread(HookTypeMask.Keyboard, _queue);
        }

        if (secureInputProbe != null)
        {
            _secureInputProbe = secureInputProbe;
            _ownsSecureProbe = false;
        }
        else
        {
            _secureInputProbe = new SecureInputProbe();
            _ownsSecureProbe = true;
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
            _currentModifiers = QueryCurrentModifiers();
            _lastVkCode = 0;

            if (_secureInputProbe != null)
            {
                _secureInputProbe.SecureInputChanged += HandleSecureInputChanged;
                _secureInputProbe.Start();
                if (_secureInputProbe.IsSecureInput)
                {
                    onEvent(new InputEvent.Key(new KeyEvent.SecureInput(true)));
                }
            }

            _ownedHookThread?.Start();

            _consumerThread = new Thread(ConsumeQueue)
            {
                IsBackground = true,
                Name = "dev.lightshot.key-consumer"
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

            if (_secureInputProbe != null)
            {
                _secureInputProbe.SecureInputChanged -= HandleSecureInputChanged;
                if (_ownsSecureProbe)
                {
                    _secureInputProbe.Stop();
                }
            }
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
        if (_ownsSecureProbe)
        {
            _secureInputProbe?.Dispose();
        }
    }

    private void HandleSecureInputChanged(bool isSecure)
    {
        _onEvent?.Invoke(new InputEvent.Key(new KeyEvent.SecureInput(isSecure)));
    }

    private static KeyModifiers QueryCurrentModifiers()
    {
        KeyModifiers mods = KeyModifiers.None;
        if ((GetAsyncKeyState(0x11) & 0x8000) != 0) mods |= KeyModifiers.Control; // VK_CONTROL
        if ((GetAsyncKeyState(0x12) & 0x8000) != 0) mods |= KeyModifiers.Option;  // VK_MENU (Alt)
        if ((GetAsyncKeyState(0x10) & 0x8000) != 0) mods |= KeyModifiers.Shift;   // VK_SHIFT
        if (((GetAsyncKeyState(0x5B) | GetAsyncKeyState(0x5C)) & 0x8000) != 0) mods |= KeyModifiers.Command; // VK_LWIN/VK_RWIN
        return mods;
    }

    private void ConsumeQueue()
    {
        while (_running)
        {
            bool hadItem = false;
            while (_queue.TryDequeue(out var msg))
            {
                hadItem = true;
                if (msg.Type != HookType.Keyboard) continue;

                var callback = _onEvent;
                if (callback == null) break;

                bool isDown = msg.Message == WM_KEYDOWN || msg.Message == WM_SYSKEYDOWN;
                bool isUp = msg.Message == WM_KEYUP || msg.Message == WM_SYSKEYUP;

                // Sync modifiers state
                var newMods = QueryCurrentModifiers();
                if (newMods != _currentModifiers)
                {
                    _currentModifiers = newMods;
                    callback(new InputEvent.Key(new KeyEvent.ModifiersChanged(newMods)));
                }

                if (KeyTranslator.IsModifierKey((int)msg.VkCode))
                {
                    _lastVkCode = 0;
                    continue;
                }

                if (isDown)
                {
                    bool isRepeat = msg.VkCode == _lastVkCode;
                    _lastVkCode = msg.VkCode;

                    IntPtr hkl = GetKeyboardLayout(0);
                    var press = KeyTranslator.Translate((int)msg.VkCode, _currentModifiers, msg.ScanCode, hkl, isRepeat);
                    if (press != null)
                    {
                        callback(new InputEvent.Key(new KeyEvent.KeyDown(press.Value)));
                    }
                }
                else if (isUp)
                {
                    if (msg.VkCode == _lastVkCode)
                    {
                        _lastVkCode = 0;
                    }
                }
            }

            if (!hadItem && _running)
            {
                Thread.Sleep(2);
            }
        }
    }
}
