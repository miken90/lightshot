// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lightshot.Core;

namespace Lightshot.App.Views.Editor;

public partial class ColorPopup : UserControl
{
    public event Action<RGBAColor>? ColorSelected;

    public ColorPopup()
    {
        InitializeComponent();
    }

    public void SetCurrentColor(RGBAColor color)
    {
        byte r = (byte)Math.Round(Math.Clamp(color.R, 0.0, 1.0) * 255);
        byte g = (byte)Math.Round(Math.Clamp(color.G, 0.0, 1.0) * 255);
        byte b = (byte)Math.Round(Math.Clamp(color.B, 0.0, 1.0) * 255);
        HexTextBox.Text = $"#{r:X2}{g:X2}{b:X2}";
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            if (TryParseHex(hex, out var color))
            {
                HexTextBox.Text = hex;
                ColorSelected?.Invoke(color);
            }
        }
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHex();
            e.Handled = true;
        }
    }

    private void OnHexLostFocus(object sender, RoutedEventArgs e)
    {
        CommitHex();
    }

    private void CommitHex()
    {
        string text = HexTextBox.Text.Trim();
        if (TryParseHex(text, out var color))
        {
            ColorSelected?.Invoke(color);
        }
    }

    public static bool TryParseHex(string hex, out RGBAColor color)
    {
        color = RGBAColor.RedColor;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        string clean = hex.Trim().TrimStart('#');
        if (clean.Length == 6)
        {
            if (byte.TryParse(clean.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
                byte.TryParse(clean.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
                byte.TryParse(clean.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                color = new RGBAColor(r / 255.0, g / 255.0, b / 255.0, 1.0);
                return true;
            }
        }
        else if (clean.Length == 8)
        {
            if (byte.TryParse(clean.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte a) &&
                byte.TryParse(clean.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
                byte.TryParse(clean.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
                byte.TryParse(clean.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                color = new RGBAColor(r / 255.0, g / 255.0, b / 255.0, a / 255.0);
                return true;
            }
        }
        return false;
    }
}
