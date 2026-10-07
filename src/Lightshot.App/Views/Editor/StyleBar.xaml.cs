// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Lightshot.Core;

namespace Lightshot.App.Views.Editor;

public partial class StyleBar : UserControl
{
    private EditorViewModel? _viewModel;
    private bool _updatingUi;

    public StyleBar()
    {
        InitializeComponent();
        ColorPickerControl.ColorSelected += OnColorSelected;
        FillPickerControl.ColorSelected += OnFillSelected;
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
            UpdateAll();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(EditorViewModel.VisibleStyleFields):
            case nameof(EditorViewModel.ActiveTool):
            case nameof(EditorViewModel.IsCropping):
            case nameof(EditorViewModel.CanResetCrop):
                UpdateVisibility();
                break;
            case nameof(EditorViewModel.ActiveColor):
                UpdateColor();
                break;
            case nameof(EditorViewModel.ActiveFill):
                UpdateFill();
                break;
            case nameof(EditorViewModel.ActiveCornerRadius):
                UpdateCornerRadius();
                break;
            case nameof(EditorViewModel.CropAspect):
                UpdateCropAspect();
                break;
            case nameof(EditorViewModel.StrokeWidth):
                UpdateStrokeWidth();
                break;
            case nameof(EditorViewModel.ArrowStyle):
                UpdateArrowStyle();
                break;
            case nameof(EditorViewModel.FontSize):
                UpdateFontSize();
                break;
            case nameof(EditorViewModel.RedactionStyle):
            case nameof(EditorViewModel.RedactionStrength):
                UpdateRedaction();
                break;
        }
    }

    private void UpdateAll()
    {
        UpdateVisibility();
        UpdateColor();
        UpdateFill();
        UpdateCornerRadius();
        UpdateCropAspect();
        UpdateStrokeWidth();
        UpdateArrowStyle();
        UpdateFontSize();
        UpdateRedaction();
    }

    private void UpdateVisibility()
    {
        if (_viewModel == null) return;
        var fields = _viewModel.VisibleStyleFields;

        ColorContainer.Visibility = (fields & StyleFields.Color) != 0 ? Visibility.Visible : Visibility.Collapsed;
        StrokeContainer.Visibility = (fields & StyleFields.StrokeWidth) != 0 ? Visibility.Visible : Visibility.Collapsed;
        FillContainer.Visibility = (fields & StyleFields.Fill) != 0 ? Visibility.Visible : Visibility.Collapsed;
        CornerContainer.Visibility = (fields & StyleFields.CornerRadius) != 0 ? Visibility.Visible : Visibility.Collapsed;
        ArrowStyleContainer.Visibility = (fields & StyleFields.ArrowStyle) != 0 ? Visibility.Visible : Visibility.Collapsed;
        FontSizeContainer.Visibility = (fields & StyleFields.FontSize) != 0 ? Visibility.Visible : Visibility.Collapsed;
        RedactionContainer.Visibility = (fields & StyleFields.Redaction) != 0 ? Visibility.Visible : Visibility.Collapsed;

        CropRatioComboBox.Visibility = _viewModel.ActiveTool == EditorTool.Crop ? Visibility.Visible : Visibility.Collapsed;
        ResetCropButton.Visibility = _viewModel.CanResetCrop ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateColor()
    {
        if (_viewModel == null) return;
        var c = _viewModel.ActiveColor;
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
            (byte)Math.Round(Math.Clamp(c.A, 0.0, 1.0) * 255),
            (byte)Math.Round(Math.Clamp(c.R, 0.0, 1.0) * 255),
            (byte)Math.Round(Math.Clamp(c.G, 0.0, 1.0) * 255),
            (byte)Math.Round(Math.Clamp(c.B, 0.0, 1.0) * 255)));

        var border = ColorButton.Template.FindName("ColorSwatch", ColorButton) as Ellipse;
        if (border != null)
        {
            border.Fill = brush;
        }
        ColorPickerControl.SetCurrentColor(c);
    }

    private void UpdateStrokeWidth()
    {
        if (_viewModel == null) return;
        _updatingUi = true;
        try
        {
            double width = _viewModel.StrokeWidth;
            foreach (ComboBoxItem item in StrokeWidthComboBox.Items)
            {
                if (item.Tag is string tagStr && double.TryParse(tagStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                {
                    if (Math.Abs(val - width) < 0.1)
                    {
                        StrokeWidthComboBox.SelectedItem = item;
                        return;
                    }
                }
            }
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void UpdateFill()
    {
        if (_viewModel == null) return;
        var fill = _viewModel.ActiveFill;
        var swatch = FillButton.Template.FindName("FillSwatch", FillButton) as Rectangle;
        if (swatch != null)
        {
            if (fill.HasValue)
            {
                var c = fill.Value;
                swatch.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                    (byte)Math.Round(Math.Clamp(c.A, 0.0, 1.0) * 255),
                    (byte)Math.Round(Math.Clamp(c.R, 0.0, 1.0) * 255),
                    (byte)Math.Round(Math.Clamp(c.G, 0.0, 1.0) * 255),
                    (byte)Math.Round(Math.Clamp(c.B, 0.0, 1.0) * 255)));
            }
            else
            {
                swatch.Fill = Brushes.Transparent;
            }
        }
        if (fill.HasValue)
        {
            FillPickerControl.SetCurrentColor(fill.Value);
        }
    }

    private void UpdateCornerRadius()
    {
        if (_viewModel == null) return;
        _updatingUi = true;
        try
        {
            double radius = _viewModel.ActiveCornerRadius;
            foreach (ComboBoxItem item in CornerRadiusComboBox.Items)
            {
                if (item.Tag is string tagStr && double.TryParse(tagStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                {
                    if (Math.Abs(val - radius) < 0.1)
                    {
                        CornerRadiusComboBox.SelectedItem = item;
                        return;
                    }
                }
            }
            if (CornerRadiusComboBox.SelectedItem == null && CornerRadiusComboBox.Items.Count > 0)
            {
                CornerRadiusComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void UpdateCropAspect()
    {
        if (_viewModel == null) return;
        _updatingUi = true;
        try
        {
            var aspect = _viewModel.CropAspect;
            foreach (ComboBoxItem item in CropRatioComboBox.Items)
            {
                if (item.Tag is string tagStr && Enum.TryParse<AspectPreset>(tagStr, out var preset))
                {
                    if (preset == aspect)
                    {
                        CropRatioComboBox.SelectedItem = item;
                        return;
                    }
                }
            }
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void UpdateArrowStyle()
    {
        if (_viewModel == null) return;
        _updatingUi = true;
        try
        {
            ArrowStandardRadio.IsChecked = _viewModel.ArrowStyle == ArrowStyle.Standard;
            ArrowFancyRadio.IsChecked = _viewModel.ArrowStyle == ArrowStyle.Fancy;
            ArrowCurvedRadio.IsChecked = _viewModel.ArrowStyle == ArrowStyle.Curved;
            ArrowDoubleRadio.IsChecked = _viewModel.ArrowStyle == ArrowStyle.Double;
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void UpdateFontSize()
    {
        if (_viewModel == null) return;
        _updatingUi = true;
        try
        {
            FontSizeTextBox.Text = Math.Round(_viewModel.FontSize).ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void UpdateRedaction()
    {
        if (_viewModel == null) return;
        _updatingUi = true;
        try
        {
            RedactPixelateRadio.IsChecked = _viewModel.RedactionStyle == RedactionStyle.Pixelate;
            RedactBlurRadio.IsChecked = _viewModel.RedactionStyle == RedactionStyle.Blur;
            RedactBlackoutRadio.IsChecked = _viewModel.RedactionStyle == RedactionStyle.Blackout;

            IntensityContainer.Visibility = _viewModel.RedactionStyle == RedactionStyle.Blackout ? Visibility.Collapsed : Visibility.Visible;
            IntensitySlider.Value = _viewModel.RedactionStrength;

            if (_viewModel.RedactionStyle == RedactionStyle.Blackout)
            {
                SecurityLabel.Text = "Secure";
                SecurityLabel.Foreground = Brushes.LightGreen;
                SecurityLabel.ToolTip = "Blackout is the secure redaction: covered pixels are replaced with solid fill.";
            }
            else
            {
                SecurityLabel.Text = "Not secure - visual only";
                SecurityLabel.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 149, 0));
                SecurityLabel.ToolTip = "Visual only - can be reversed or inferred. Use Blackout to hide secrets.";
            }
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void OnColorButtonClick(object sender, RoutedEventArgs e)
    {
        ColorPickerPopup.IsOpen = true;
    }

    private void OnColorSelected(RGBAColor color)
    {
        ColorPickerPopup.IsOpen = false;
        if (_viewModel != null)
        {
            _viewModel.ActiveColor = color;
            _viewModel.CommitStyleEdit();
        }
    }

    private void OnFillButtonClick(object sender, RoutedEventArgs e)
    {
        FillPickerPopup.IsOpen = true;
    }

    private void OnFillSelected(RGBAColor color)
    {
        FillPickerPopup.IsOpen = false;
        if (_viewModel != null)
        {
            _viewModel.ActiveFill = color;
            _viewModel.CommitStyleEdit();
        }
    }

    private void OnNoFillClick(object sender, RoutedEventArgs e)
    {
        FillPickerPopup.IsOpen = false;
        if (_viewModel != null)
        {
            _viewModel.ActiveFill = null;
            _viewModel.CommitStyleEdit();
        }
    }

    private void OnCornerRadiusChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || _viewModel == null) return;
        if (CornerRadiusComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tagStr &&
            double.TryParse(tagStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
        {
            _viewModel.ActiveCornerRadius = val;
            _viewModel.CommitStyleEdit();
        }
    }

    private void OnCropAspectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || _viewModel == null) return;
        if (CropRatioComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tagStr &&
            Enum.TryParse<AspectPreset>(tagStr, out var preset))
        {
            _viewModel.CropAspect = preset;
        }
    }

    private void OnStrokeWidthChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || _viewModel == null) return;
        if (StrokeWidthComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tagStr &&
            double.TryParse(tagStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
        {
            _viewModel.StrokeWidth = val;
            _viewModel.CommitStyleEdit();
        }
    }

    private void OnArrowStyleClick(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || _viewModel == null) return;
        if (sender is RadioButton rb && rb.Tag is string tagStr && Enum.TryParse<ArrowStyle>(tagStr, out var style))
        {
            _viewModel.ArrowStyle = style;
        }
    }

    private void OnFontSizeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitFontSize();
            e.Handled = true;
        }
    }

    private void OnFontSizeLostFocus(object sender, RoutedEventArgs e)
    {
        CommitFontSize();
    }

    private void CommitFontSize()
    {
        if (_viewModel == null) return;
        if (double.TryParse(FontSizeTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double size))
        {
            _viewModel.FontSize = Math.Clamp(size, AnnotationDocument.TextFontSizesMin, AnnotationDocument.TextFontSizesMax);
            _viewModel.CommitStyleEdit();
        }
    }

    private void OnRedactStyleClick(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || _viewModel == null) return;
        if (sender is RadioButton rb && rb.Tag is string tagStr && Enum.TryParse<RedactionStyle>(tagStr, out var style))
        {
            _viewModel.RedactionStyle = style;
        }
    }

    private void OnIntensitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi || _viewModel == null) return;
        _viewModel.RedactionStrength = e.NewValue;
    }

    private void OnResetCropClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.ResetCrop();
    }
}
