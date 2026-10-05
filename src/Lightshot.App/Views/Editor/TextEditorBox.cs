// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lightshot.Core;
using Point = Lightshot.Core.Point;

namespace Lightshot.App.Views.Editor;

/// <summary>
/// Floating text box placed over the canvas to edit text annotations inline.
/// Synchronizes content with the active element and commits on finish.
/// </summary>
public sealed class TextEditorBox : TextBox
{
    private CanvasHost? _canvasHost;
    private EditorViewModel? _viewModel;

    public TextEditorBox()
    {
        Visibility = Visibility.Collapsed;
        AcceptsReturn = true;
        AcceptsTab = false;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 255, 255, 255));
        BorderThickness = new Thickness(1);
        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 122, 255));
        Padding = new Thickness(2);

        TextChanged += OnTextChanged;
        LostFocus += OnLostFocus;
        KeyDown += OnKeyDown;
    }

    public void Attach(CanvasHost canvasHost, EditorViewModel viewModel)
    {
        _canvasHost = canvasHost;
        _viewModel = viewModel;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.IsEditingText) or nameof(EditorViewModel.EditingTextID))
        {
            UpdatePositionAndVisibility();
        }
    }

    public void UpdatePositionAndVisibility()
    {
        if (_viewModel == null || _canvasHost == null || !_viewModel.IsEditingText || !_viewModel.EditingTextID.HasValue)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        var el = _viewModel.Document.Element(_viewModel.EditingTextID.Value);
        if (el?.ElementKind is not AnnotationElement.Kind.Text t)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        var box = t.Box.Standardized;
        var screenOrigin = _canvasHost.ImageToScreen(new Point(box.MinX, box.MinY));

        double scale = _canvasHost.ActualWidth > 0 ? _canvasHost.ActualWidth / _viewModel.Document.VisibleFrame.Width : 1.0;
        double fontSize = Math.Max(10.0, el.Style.FontSize * scale);

        FontSize = fontSize;
        FontFamily = new FontFamily("Segoe UI");

        var c = el.Style.Color;
        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
            (byte)(c.A * 255), (byte)(c.R * 255), (byte)(c.G * 255), (byte)(c.B * 255)));

        Text = t.Content;
        Select(Text.Length, 0);

        System.Windows.Controls.Canvas.SetLeft(this, screenOrigin.X);
        System.Windows.Controls.Canvas.SetTop(this, screenOrigin.Y);

        MinWidth = Math.Max(40.0, box.Width * scale);
        MinHeight = Math.Max(24.0, box.Height * scale);

        Visibility = Visibility.Visible;
        Focus();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_viewModel != null && _viewModel.IsEditingText)
        {
            _viewModel.EditingText = Text;
        }
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        _viewModel?.EndTextEditing();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel?.EndTextEditing();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            // Enter without Shift commits text
            _viewModel?.EndTextEditing();
            e.Handled = true;
        }
    }
}
