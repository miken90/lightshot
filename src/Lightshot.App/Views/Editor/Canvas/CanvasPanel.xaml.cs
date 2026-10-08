// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Windows;
using System.Windows.Controls;
using Lightshot.Core;
using Microsoft.Win32;

namespace Lightshot.App.Views.Editor;

public partial class CanvasPanel : UserControl
{
    public CanvasPanelViewModel? ViewModel
    {
        get => DataContext as CanvasPanelViewModel;
        set => DataContext = value;
    }

    public CanvasPanel()
    {
        InitializeComponent();
        BorderColorPickerControl.ColorSelected += OnBorderColorSelected;
    }

    private void OnAspectButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AspectPreset preset && ViewModel != null)
        {
            ViewModel.Aspect = preset;
        }
    }

    private void OnSolidSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RGBAColor color && ViewModel != null)
        {
            ViewModel.SolidColor = color;
        }
    }

    private void OnGradientSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int idx && ViewModel != null)
        {
            ViewModel.GradientIndex = idx;
        }
    }

    private void OnChooseImageClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var ofd = new OpenFileDialog
        {
            Title = "Choose Background Image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp"
        };

        if (ofd.ShowDialog() == true)
        {
            ViewModel.ImagePath = ofd.FileName;
        }
    }

    private void OnResetOptionsClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.ResetDefaults();
    }

    private void OnBorderColorButtonClick(object sender, RoutedEventArgs e)
    {
        BorderColorPickerControl.SetCurrentColor(ViewModel?.EffectiveBorderColor ?? new RGBAColor(1, 1, 1, 0.5));
        BorderColorPopup.IsOpen = true;
    }

    private void OnBorderColorSelected(RGBAColor color)
    {
        BorderColorPopup.IsOpen = false;
        if (ViewModel != null)
        {
            ViewModel.BorderColor = color;
        }
    }
}
