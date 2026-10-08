// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using Lightshot.Core;

namespace Lightshot.App.Views.Editor;

public sealed record GradientPresetItem(int Index, string Name, LinearGradientBrush Brush);
public sealed record SolidSwatchItem(string Hex, RGBAColor Color, SolidColorBrush Brush);

public sealed class CanvasPanelViewModel : INotifyPropertyChanged
{
    public const string SettingsKey = "editor.canvas";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly IReadOnlyList<SolidSwatchItem> s_solidSwatches =
    [
        CreateSolidSwatch("#FFFFFF", new RGBAColor(1, 1, 1)),
        CreateSolidSwatch("#F2F2F2", new RGBAColor(242 / 255.0, 242 / 255.0, 242 / 255.0)),
        CreateSolidSwatch("#000000", new RGBAColor(0, 0, 0)),
        CreateSolidSwatch("#1A1A2E", new RGBAColor(26 / 255.0, 26 / 255.0, 46 / 255.0)),
        CreateSolidSwatch("#2F80ED", new RGBAColor(47 / 255.0, 128 / 255.0, 237 / 255.0)),
        CreateSolidSwatch("#7F00FF", new RGBAColor(127 / 255.0, 0, 1)),
        CreateSolidSwatch("#FC67FA", new RGBAColor(252 / 255.0, 103 / 255.0, 250 / 255.0)),
        CreateSolidSwatch("#71B280", new RGBAColor(113 / 255.0, 178 / 255.0, 128 / 255.0))
    ];

    private readonly AnnotationDocument _document;
    private readonly ISettingsStore? _settingsStore;
    private readonly EditorViewModel? _editorViewModel;

    private bool _isEnabled;
    private CanvasStyle _style;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CanvasChanged;

    public AnnotationDocument Document => _document;
    public EditorViewModel? EditorViewModel => _editorViewModel;

    public static IReadOnlyList<SolidSwatchItem> SolidSwatches => s_solidSwatches;
    public IReadOnlyList<GradientPresetItem> GradientSwatches { get; }

    public static IReadOnlyList<string> FixedPresetLabels =>
    [
        "1920 x 1080 (FHD)",
        "2560 x 1440 (2K)",
        "3840 x 2160 (4K)",
        "1200 x 630 (Social card)",
        "1080 x 1080 (Square)",
        "1080 x 1920 (Vertical)",
        "Custom"
    ];

    public CanvasPanelViewModel(AnnotationDocument document, ISettingsStore? settingsStore = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _settingsStore = settingsStore;
        GradientSwatches = BuildGradientSwatches();

        _style = LoadSettings();
        _isEnabled = false;
        _document.Canvas = _style with { Enabled = false };
    }

    public CanvasPanelViewModel(EditorViewModel editorViewModel, ISettingsStore? settingsStore = null)
        : this(
            editorViewModel?.Document ?? throw new ArgumentNullException(nameof(editorViewModel)),
            settingsStore ?? GetSettingsStore(editorViewModel))
    {
        _editorViewModel = editorViewModel;
    }

    private static ISettingsStore? GetSettingsStore(EditorViewModel vm)
    {
        var field = typeof(EditorViewModel).GetField("_settingsStore", BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(vm) as ISettingsStore;
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            _style = _style with { Enabled = value };
            _document.Canvas = _style;
            PersistSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Style));
            NotifyCanvasChanged();
        }
    }

    public CanvasStyle Style
    {
        get => _style;
        set
        {
            _style = value with { Enabled = _isEnabled };
            _document.Canvas = _style;
            PersistSettings();
            OnPropertyChanged();
            NotifyProperties();
            NotifyCanvasChanged();
        }
    }

    public CanvasSizeMode SizeMode
    {
        get => _style.SizeMode;
        set
        {
            if (_style.SizeMode == value) return;
            Style = _style with { SizeMode = value };
            OnPropertyChanged(nameof(IsAspectMode));
            OnPropertyChanged(nameof(IsFixedSizeMode));
        }
    }

    public bool IsAspectMode
    {
        get => SizeMode == CanvasSizeMode.Aspect;
        set { if (value) SizeMode = CanvasSizeMode.Aspect; }
    }

    public bool IsFixedSizeMode
    {
        get => SizeMode == CanvasSizeMode.FixedSize;
        set { if (value) SizeMode = CanvasSizeMode.FixedSize; }
    }

    public AspectPreset Aspect
    {
        get => _style.Aspect;
        set
        {
            if (_style.Aspect == value) return;
            Style = _style with { Aspect = value };
            OnPropertyChanged();
        }
    }

    public int TargetWidth
    {
        get => _style.TargetWidth;
        set
        {
            int clamped = Math.Clamp(value, 16, 8192);
            if (_style.TargetWidth == clamped) return;
            Style = _style with { TargetWidth = clamped };
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedFixedPresetIndex));
            OnPropertyChanged(nameof(IsCustomFixedSize));
        }
    }

    public int TargetHeight
    {
        get => _style.TargetHeight;
        set
        {
            int clamped = Math.Clamp(value, 16, 8192);
            if (_style.TargetHeight == clamped) return;
            Style = _style with { TargetHeight = clamped };
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedFixedPresetIndex));
            OnPropertyChanged(nameof(IsCustomFixedSize));
        }
    }

    public bool DownscaleToFit
    {
        get => _style.DownscaleToFit;
        set
        {
            if (_style.DownscaleToFit == value) return;
            Style = _style with { DownscaleToFit = value };
            OnPropertyChanged();
        }
    }

    public CanvasFillKind FillKind
    {
        get => _style.EffectiveFill.Kind;
        set
        {
            if (_style.EffectiveFill.Kind == value) return;
            var fill = _style.EffectiveFill with { Kind = value };
            Style = _style with { Fill = fill };
            OnPropertyChanged(nameof(IsSolidFill));
            OnPropertyChanged(nameof(IsGradientFill));
            OnPropertyChanged(nameof(IsAutoEdgeFill));
            OnPropertyChanged(nameof(IsImageFill));
        }
    }

    public bool IsSolidFill
    {
        get => FillKind == CanvasFillKind.Solid;
        set { if (value) FillKind = CanvasFillKind.Solid; }
    }

    public bool IsGradientFill
    {
        get => FillKind == CanvasFillKind.Gradient;
        set { if (value) FillKind = CanvasFillKind.Gradient; }
    }

    public bool IsAutoEdgeFill
    {
        get => FillKind == CanvasFillKind.AutoEdge;
        set { if (value) FillKind = CanvasFillKind.AutoEdge; }
    }

    public bool IsImageFill
    {
        get => FillKind == CanvasFillKind.Image;
        set { if (value) FillKind = CanvasFillKind.Image; }
    }

    public RGBAColor? SolidColor
    {
        get => _style.EffectiveFill.Color;
        set
        {
            var fill = _style.EffectiveFill with { Kind = CanvasFillKind.Solid, Color = value };
            Style = _style with { Fill = fill };
            OnPropertyChanged();
        }
    }

    public int GradientIndex
    {
        get => _style.EffectiveFill.GradientIndex;
        set
        {
            int clamped = Math.Clamp(value, 0, GradientPresets.All.Count - 1);
            if (_style.EffectiveFill.GradientIndex == clamped && FillKind == CanvasFillKind.Gradient) return;
            var fill = _style.EffectiveFill with { Kind = CanvasFillKind.Gradient, GradientIndex = clamped };
            Style = _style with { Fill = fill };
            OnPropertyChanged();
        }
    }

    public string? ImagePath
    {
        get => _style.EffectiveFill.ImagePath;
        set
        {
            if (string.Equals(_style.EffectiveFill.ImagePath, value, StringComparison.OrdinalIgnoreCase)) return;
            var fill = _style.EffectiveFill with { Kind = CanvasFillKind.Image, ImagePath = value };
            Style = _style with { Fill = fill };
            OnPropertyChanged();
            OnPropertyChanged(nameof(ImageFileName));
        }
    }

    public string ImageFileName => string.IsNullOrEmpty(ImagePath)
        ? "No image selected"
        : System.IO.Path.GetFileName(ImagePath);

    public double Padding
    {
        get => _style.Padding;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 200.0);
            if (Math.Abs(_style.Padding - clamped) < 1e-4) return;
            Style = _style with { Padding = clamped };
            OnPropertyChanged();
        }
    }

    public double CornerRadius
    {
        get => _style.CornerRadius;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 64.0);
            if (Math.Abs(_style.CornerRadius - clamped) < 1e-4) return;
            Style = _style with { CornerRadius = clamped };
            OnPropertyChanged();
        }
    }

    public double Shadow
    {
        get => _style.Shadow;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 60.0);
            if (Math.Abs(_style.Shadow - clamped) < 1e-4) return;
            Style = _style with { Shadow = clamped };
            OnPropertyChanged();
        }
    }

    public double Inset
    {
        get => _style.Inset;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 64.0);
            if (Math.Abs(_style.Inset - clamped) < 1e-4) return;
            Style = _style with { Inset = clamped };
            OnPropertyChanged();
        }
    }

    public double BorderWidth
    {
        get => _style.BorderWidth;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 12.0);
            if (Math.Abs(_style.BorderWidth - clamped) < 1e-4) return;
            Style = _style with { BorderWidth = clamped };
            OnPropertyChanged();
        }
    }

    public RGBAColor? BorderColor
    {
        get => _style.BorderColor;
        set
        {
            if (_style.BorderColor == value) return;
            Style = _style with { BorderColor = value };
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveBorderColor));
            OnPropertyChanged(nameof(BorderColorBrush));
        }
    }

    public RGBAColor EffectiveBorderColor => _style.EffectiveBorderColor;

    public SolidColorBrush BorderColorBrush
    {
        get
        {
            var c = EffectiveBorderColor;
            byte a = (byte)Math.Clamp(Math.Round(c.A * 255), 0, 255);
            byte r = (byte)Math.Clamp(Math.Round(c.R * 255), 0, 255);
            byte g = (byte)Math.Clamp(Math.Round(c.G * 255), 0, 255);
            byte b = (byte)Math.Clamp(Math.Round(c.B * 255), 0, 255);
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    public int SelectedFixedPresetIndex
    {
        get
        {
            for (int i = 0; i < CanvasStyle.FixedPresets.Count; i++)
            {
                var preset = CanvasStyle.FixedPresets[i];
                if (_style.TargetWidth == preset.Width && _style.TargetHeight == preset.Height)
                {
                    return i;
                }
            }
            return CanvasStyle.FixedPresets.Count; // "Custom"
        }
        set
        {
            if (value >= 0 && value < CanvasStyle.FixedPresets.Count)
            {
                var preset = CanvasStyle.FixedPresets[value];
                TargetWidth = preset.Width;
                TargetHeight = preset.Height;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomFixedSize));
        }
    }

    public bool IsCustomFixedSize => SelectedFixedPresetIndex == CanvasStyle.FixedPresets.Count;

    public void ResetDefaults()
    {
        _style = new CanvasStyle(Enabled: _isEnabled);
        _document.Canvas = _style;
        PersistSettings();
        OnPropertyChanged(nameof(Style));
        NotifyProperties();
        NotifyCanvasChanged();
    }

    private void NotifyProperties()
    {
        OnPropertyChanged(nameof(SizeMode));
        OnPropertyChanged(nameof(IsAspectMode));
        OnPropertyChanged(nameof(IsFixedSizeMode));
        OnPropertyChanged(nameof(Aspect));
        OnPropertyChanged(nameof(TargetWidth));
        OnPropertyChanged(nameof(TargetHeight));
        OnPropertyChanged(nameof(DownscaleToFit));
        OnPropertyChanged(nameof(FillKind));
        OnPropertyChanged(nameof(IsSolidFill));
        OnPropertyChanged(nameof(IsGradientFill));
        OnPropertyChanged(nameof(IsAutoEdgeFill));
        OnPropertyChanged(nameof(IsImageFill));
        OnPropertyChanged(nameof(SolidColor));
        OnPropertyChanged(nameof(GradientIndex));
        OnPropertyChanged(nameof(ImagePath));
        OnPropertyChanged(nameof(ImageFileName));
        OnPropertyChanged(nameof(Padding));
        OnPropertyChanged(nameof(CornerRadius));
        OnPropertyChanged(nameof(Shadow));
        OnPropertyChanged(nameof(Inset));
        OnPropertyChanged(nameof(BorderWidth));
        OnPropertyChanged(nameof(BorderColor));
        OnPropertyChanged(nameof(EffectiveBorderColor));
        OnPropertyChanged(nameof(BorderColorBrush));
        OnPropertyChanged(nameof(SelectedFixedPresetIndex));
        OnPropertyChanged(nameof(IsCustomFixedSize));
    }

    private void NotifyCanvasChanged()
    {
        CanvasChanged?.Invoke();
    }

    private CanvasStyle LoadSettings()
    {
        var raw = _settingsStore?.GetSetting(SettingsKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new CanvasStyle(Enabled: false);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<CanvasStyle>(raw, s_jsonOptions);
            return (parsed ?? new CanvasStyle()) with { Enabled = false };
        }
        catch
        {
            return new CanvasStyle(Enabled: false);
        }
    }

    private void PersistSettings()
    {
        try
        {
            string json = JsonSerializer.Serialize(_style with { Enabled = false }, s_jsonOptions);
            _settingsStore?.SetSetting(SettingsKey, json);
        }
        catch
        {
            // Ignore settings persistence errors
        }
    }

    private static SolidSwatchItem CreateSolidSwatch(string hex, RGBAColor color)
    {
        byte r = (byte)Math.Clamp(Math.Round(color.R * 255), 0, 255);
        byte g = (byte)Math.Clamp(Math.Round(color.G * 255), 0, 255);
        byte b = (byte)Math.Clamp(Math.Round(color.B * 255), 0, 255);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return new SolidSwatchItem(hex, color, brush);
    }

    private static IReadOnlyList<GradientPresetItem> BuildGradientSwatches()
    {
        var list = new List<GradientPresetItem>(GradientPresets.All.Count);
        for (int i = 0; i < GradientPresets.All.Count; i++)
        {
            var p = GradientPresets.All[i];
            var startColor = System.Windows.Media.Color.FromRgb(
                (byte)Math.Clamp(Math.Round(p.From.R * 255), 0, 255),
                (byte)Math.Clamp(Math.Round(p.From.G * 255), 0, 255),
                (byte)Math.Clamp(Math.Round(p.From.B * 255), 0, 255));
            var endColor = System.Windows.Media.Color.FromRgb(
                (byte)Math.Clamp(Math.Round(p.To.R * 255), 0, 255),
                (byte)Math.Clamp(Math.Round(p.To.G * 255), 0, 255),
                (byte)Math.Clamp(Math.Round(p.To.B * 255), 0, 255));

            var brush = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(startColor, 0.0),
                    new GradientStop(endColor, 1.0)
                },
                new System.Windows.Point(0, 0),
                new System.Windows.Point(1, 1));
            brush.Freeze();
            list.Add(new GradientPresetItem(i, p.Name, brush));
        }
        return list;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
