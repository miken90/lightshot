// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lightshot.Core;

namespace Lightshot.App.Views.Settings;

public partial class ClickHighlightPreview : UserControl
{
    public static readonly DependencyProperty SettingsProperty =
        DependencyProperty.Register(
            nameof(Settings),
            typeof(ClickHighlightSettings),
            typeof(ClickHighlightPreview),
            new PropertyMetadata(null, OnSettingsChanged));

    public ClickHighlightSettings? Settings
    {
        get => (ClickHighlightSettings?)GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    public ClickHighlightPreview()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdatePreview();
        SizeChanged += (_, _) => UpdatePreview();
    }

    private static void OnSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ClickHighlightPreview preview)
        {
            preview.UpdatePreview();
        }
    }

    public void UpdatePreview()
    {
        if (PreviewCanvas == null || HighlightShape == null) return;

        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (w <= 0 || h <= 0)
        {
            w = 180;
            h = 90;
        }

        double cx = w / 2.0;
        double cy = h / 2.0;

        var settings = Settings ?? ClickHighlightSettings.Standard;
        double radius = settings.Size.Radius();

        RGBColor rgb = RGBColor.FromCursorHighlightColor(settings.Color) ?? new RGBColor(1.0, 0.85, 0.20);
        var baseColor = Color.FromRgb(
            (byte)Math.Round(rgb.Red * 255),
            (byte)Math.Round(rgb.Green * 255),
            (byte)Math.Round(rgb.Blue * 255));

        HighlightShape.Width = radius * 2.0;
        HighlightShape.Height = radius * 2.0;
        Canvas.SetLeft(HighlightShape, cx - radius);
        Canvas.SetTop(HighlightShape, cy - radius);

        switch (settings.Style)
        {
            case CursorHighlightStyle.Ring:
                HighlightShape.Fill = Brushes.Transparent;
                HighlightShape.Stroke = new SolidColorBrush(baseColor);
                HighlightShape.StrokeThickness = HighlightCircle.CalculateStrokeWidth(radius);
                break;
            case CursorHighlightStyle.Filled:
                var fillColor = baseColor;
                fillColor.A = (byte)Math.Round(ClickHighlightModel.HaloOpacity * 255);
                HighlightShape.Fill = new SolidColorBrush(fillColor);
                HighlightShape.Stroke = Brushes.Transparent;
                HighlightShape.StrokeThickness = 0;
                break;
            case CursorHighlightStyle.Outline:
                var fillOutlineColor = baseColor;
                fillOutlineColor.A = (byte)Math.Round(ClickHighlightModel.HaloOpacity * 255);
                HighlightShape.Fill = new SolidColorBrush(fillOutlineColor);
                HighlightShape.Stroke = new SolidColorBrush(baseColor);
                HighlightShape.StrokeThickness = HighlightCircle.CalculateStrokeWidth(radius);
                break;
        }

        if (CursorArrow != null)
        {
            Canvas.SetLeft(CursorArrow, cx);
            Canvas.SetTop(CursorArrow, cy);
        }
    }
}
