// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lightshot.App.Views.Editor;

public partial class ToolPalette : UserControl
{
    private static readonly Brush s_activeBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 0, 122, 255));
    private static readonly Brush s_inactiveBrush = Brushes.Transparent;
    private static readonly Brush s_activeForeground = Brushes.White;
    private static readonly Brush s_inactiveForeground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 224, 224, 224));

    private EditorViewModel? _viewModel;
    private readonly Dictionary<EditorTool, Button> _buttons = new();

    static ToolPalette()
    {
        s_activeBrush.Freeze();
        s_inactiveForeground.Freeze();
    }

    public ToolPalette()
    {
        InitializeComponent();

        _buttons[EditorTool.Crop] = CropButton;
        _buttons[EditorTool.Select] = SelectButton;
        _buttons[EditorTool.Arrow] = ArrowButton;
        _buttons[EditorTool.Line] = LineButton;
        _buttons[EditorTool.Rectangle] = RectangleButton;
        _buttons[EditorTool.Ellipse] = EllipseButton;
        _buttons[EditorTool.Freehand] = FreehandButton;
        _buttons[EditorTool.Text] = TextButton;
        _buttons[EditorTool.Step] = StepButton;
        _buttons[EditorTool.Highlight] = HighlightButton;
        _buttons[EditorTool.Focus] = FocusButton;
        _buttons[EditorTool.Redact] = RedactButton;
    }

    public void BindViewModel(EditorViewModel viewModel)
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateActiveButton();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.ActiveTool))
        {
            UpdateActiveButton();
        }
    }

    private void UpdateActiveButton()
    {
        if (_viewModel == null) return;

        foreach (var (tool, btn) in _buttons)
        {
            bool isActive = _viewModel.ActiveTool == tool;
            btn.Background = isActive ? s_activeBrush : s_inactiveBrush;
            btn.Foreground = isActive ? s_activeForeground : s_inactiveForeground;
        }
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && Enum.TryParse<EditorTool>(tagStr, out var tool))
        {
            if (_viewModel != null)
            {
                _viewModel.ActiveTool = tool;
            }
        }
    }
}
