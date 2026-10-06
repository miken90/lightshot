// Ported from LightshotKit/Sources/LightshotKit/InputEventSource.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Accessibility;

namespace Lightshot.Platform.Windows.Input;

/// <summary>
/// Subscribes to UI Automation focus-change events to detect when the user focuses a password field.
/// Heuristic matches macOS IsSecureEventInputEnabled behaviour to redact keystrokes on password inputs.
/// Note: Keystrokes typed into elevated (administrator) windows are invisible to standard user hooks (documented limitation).
/// </summary>
public sealed class SecureInputProbe : IDisposable
{
    public event Action<bool>? SecureInputChanged;

    private readonly object _lock = new();
    private IUIAutomation? _automation;
    private FocusChangedHandler? _handler;
    private bool _isSecureInput;
    private bool _started;

    public bool IsSecureInput
    {
        get
        {
            lock (_lock)
            {
                return _isSecureInput;
            }
        }
        private set
        {
            bool changed = false;
            lock (_lock)
            {
                if (_isSecureInput != value)
                {
                    _isSecureInput = value;
                    changed = true;
                }
            }
            if (changed)
            {
                SecureInputChanged?.Invoke(value);
            }
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_started) return;
            _started = true;

            try
            {
                var uiaType = Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"))
                    ?? throw new InvalidOperationException("CUIAutomation CLSID not found");
                _automation = (IUIAutomation)Activator.CreateInstance(uiaType)!;
                _handler = new FocusChangedHandler(this);
                _automation.AddFocusChangedEventHandler(null, _handler);

                // Initial probe on focused element
                try
                {
                    var focused = _automation.GetFocusedElement();
                    if (focused != null)
                    {
                        IsSecureInput = IsElementPassword(focused);
                    }
                }
                catch
                {
                    // Focus element may be transient
                }
            }
            catch
            {
                // Degrade gracefully if UIA initialization fails
            }
        }
    }

    public void Stop()
    {
        IUIAutomation? auto;
        FocusChangedHandler? handler;

        lock (_lock)
        {
            if (!_started) return;
            _started = false;
            auto = _automation;
            handler = _handler;
            _handler = null;
            _automation = null;
            IsSecureInput = false;
        }

        if (auto != null && handler != null)
        {
            try
            {
                var unhookTask = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        auto.RemoveFocusChangedEventHandler(handler);
                    }
                    catch
                    {
                        // Ignore COM unhook errors on shutdown
                    }
                });
                unhookTask.Wait(1000);
            }
            catch
            {
                // Best-effort unhook
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }

    internal void OnFocusChanged(IUIAutomationElement? element)
    {
        bool isPassword = false;
        if (element != null)
        {
            try
            {
                isPassword = IsElementPassword(element);
            }
            catch
            {
                isPassword = false;
            }
        }
        IsSecureInput = isPassword;
    }

    private static bool IsElementPassword(IUIAutomationElement element)
    {
        try
        {
            // CurrentIsPassword is BOOL in Win32 UIA
            return element.CurrentIsPassword.Value != 0;
        }
        catch
        {
            return false;
        }
    }

    [ClassInterface(ClassInterfaceType.None)]
    private sealed class FocusChangedHandler : IUIAutomationFocusChangedEventHandler
    {
        private readonly SecureInputProbe _owner;

        public FocusChangedHandler(SecureInputProbe owner)
        {
            _owner = owner;
        }

        public void HandleFocusChangedEvent(IUIAutomationElement sender)
        {
            _owner.OnFocusChanged(sender);
        }
    }
}
