// Ported from App/Sources/HotkeyRecorderView.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lightshot.Core;

namespace Lightshot.App.Views.Settings;

public partial class HotkeyRecorder : UserControl
{
    public static readonly DependencyProperty BindingProperty =
        DependencyProperty.Register(
            nameof(Binding),
            typeof(HotkeyBinding?),
            typeof(HotkeyRecorder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBindingChanged));

    public static readonly DependencyProperty DisplayTextProperty =
        DependencyProperty.Register(
            nameof(DisplayText),
            typeof(string),
            typeof(HotkeyRecorder),
            new PropertyMetadata("None"));

    private bool _isRecording;

    public HotkeyBinding? Binding
    {
        get => (HotkeyBinding?)GetValue(BindingProperty);
        set => SetValue(BindingProperty, value);
    }

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        private set => SetValue(DisplayTextProperty, value);
    }

    public HotkeyRecorder()
    {
        InitializeComponent();
        UpdateDisplayText();
    }

    private static void OnBindingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HotkeyRecorder recorder)
        {
            recorder.UpdateDisplayText();
        }
    }

    private void UpdateDisplayText()
    {
        if (_isRecording)
        {
            DisplayText = "Type shortcut...";
        }
        else if (Binding.HasValue)
        {
            DisplayText = Binding.Value.DisplayString;
        }
        else
        {
            DisplayText = "None";
        }
    }

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        _isRecording = true;
        UpdateDisplayText();
        RecordButton.Focus();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _isRecording = false;
        Binding = null;
        UpdateDisplayText();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isRecording) return;

        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Escape cancels recording
        if (key == Key.Escape)
        {
            _isRecording = false;
            UpdateDisplayText();
            return;
        }

        // Backspace or Delete clears
        if (key == Key.Back || key == Key.Delete)
        {
            _isRecording = false;
            Binding = null;
            UpdateDisplayText();
            return;
        }

        // Ignore standalone modifier keys
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin)
        {
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        HotkeyModifiers modifiers = HotkeyModifiers.None;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Option;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Command;

        string keyLabel = key.ToString();
        if (key == Key.PrintScreen || key == Key.Snapshot || vk == 0x2C)
        {
            keyLabel = "PrintScreen";
            vk = 0x2C;
        }

        Binding = new HotkeyBinding((ushort)vk, modifiers, keyLabel);
        _isRecording = false;
        UpdateDisplayText();
    }
}
