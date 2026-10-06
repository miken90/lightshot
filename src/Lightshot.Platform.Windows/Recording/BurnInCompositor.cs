// Ported from App/Sources/RecordingCompositor.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using Size = Lightshot.Core.Size;

namespace Lightshot.Platform.Windows.Recording;

/// <summary>
/// Direct2D and DirectWrite burn-in compositor for cursor highlight rings and keystroke overlay pills.
/// Composites overlays directly into the BGRA render target before the video processor pipeline.
/// Note: Camera bubble burn-in is deferred post-MVP (package R4).
/// </summary>
public sealed class BurnInCompositor : IDisposable
{
    private readonly RecordingOptions _options;
    private readonly Point _regionOrigin;
    private readonly double _pixelsPerPoint;
    private readonly ClickHighlightModel? _clickModel;
    private readonly KeystrokeOverlayModel? _keystrokeModel;
    private readonly FrameMapping _mapping;

    private readonly ID2D1Factory1 _d2dFactory;
    private readonly IDWriteFactory _dwriteFactory;
    private readonly IDWriteTextFormat? _textFormat;
    private readonly IInputEventSource? _inputEventSource;

    private double _currentTimeSeconds;
    private bool _disposed;

    public ClickHighlightModel? ClickModel => _clickModel;
    public KeystrokeOverlayModel? KeystrokeModel => _keystrokeModel;

    public BurnInCompositor(
        RecordingOptions options,
        Point regionOrigin,
        double pixelsPerPoint = 1.0,
        IInputEventSource? inputEventSource = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _regionOrigin = regionOrigin;
        _pixelsPerPoint = pixelsPerPoint <= 0 ? 1.0 : pixelsPerPoint;
        _inputEventSource = inputEventSource;

        if (_options.HighlightClicks)
        {
            _clickModel = new ClickHighlightModel(_options.ClickHighlight);
        }

        if (_options.ShowKeystrokes)
        {
            _keystrokeModel = new KeystrokeOverlayModel(_options.KeystrokeOverlay);
        }

        _mapping = new FrameMapping(_regionOrigin, _pixelsPerPoint, _pixelsPerPoint);

        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        _dwriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();

        double fontSize = _options.KeystrokeOverlay.Size.FontSize() * _pixelsPerPoint;
        _textFormat = _dwriteFactory.CreateTextFormat("Segoe UI", FontWeight.Bold, FontStyle.Normal, (float)fontSize);

        if (_inputEventSource != null)
        {
            _inputEventSource.Start(OnInputEvent);
        }
    }

    private void OnInputEvent(InputEvent ev)
    {
        switch (ev)
        {
            case InputEvent.Pointer p:
                if (p.Event is PointerEvent.Moved m)
                {
                    _clickModel?.PointerMoved(m.Position);
                }
                else if (p.Event is PointerEvent.Down d)
                {
                    _clickModel?.Clicked(d.Position, _currentTimeSeconds);
                }
                break;

            case InputEvent.Key k:
                _keystrokeModel?.Handle(k.Event, _currentTimeSeconds);
                break;
        }
    }

    public void RecordClick(Point screenPoint, double timeSeconds)
    {
        _currentTimeSeconds = timeSeconds;
        _clickModel?.Clicked(screenPoint, timeSeconds);
    }

    public void RecordPointerMove(Point screenPoint)
    {
        _clickModel?.PointerMoved(screenPoint);
    }

    public void RecordKey(KeyPress press, double timeSeconds)
    {
        _currentTimeSeconds = timeSeconds;
        _keystrokeModel?.KeyDown(press, timeSeconds);
    }

    public void RecordKeyEvent(KeyEvent keyEvent, double timeSeconds)
    {
        _currentTimeSeconds = timeSeconds;
        _keystrokeModel?.Handle(keyEvent, timeSeconds);
    }

    /// <summary>
    /// Composites active cursor highlights and keystroke pills directly into the BGRA target texture.
    /// </summary>
    public void Composite(ID3D11Texture2D bgraTarget, double timeSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bgraTarget);

        _currentTimeSeconds = timeSeconds;
        _clickModel?.Prune(timeSeconds);
        _keystrokeModel?.Prune(timeSeconds);

        if (_clickModel == null && _keystrokeModel == null)
        {
            return;
        }

        using var dxgiSurface = bgraTarget.QueryInterface<IDXGISurface>();
        var rtProps = new RenderTargetProperties(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied)
        );

        using var rt = _d2dFactory.CreateDxgiSurfaceRenderTarget(dxgiSurface, rtProps);
        rt.BeginDraw();

        try
        {
            // 1. Draw click highlight circles / rings
            if (_clickModel != null)
            {
                var circles = _clickModel.Circles(timeSeconds);
                var rgb = RGBColor.FromCursorHighlightColor(_options.ClickHighlight.Color) ?? RGBColor.YellowColor;

                foreach (var circle in circles)
                {
                    var pc = _mapping.PixelCircle(circle);
                    if (pc.Opacity <= 0 || pc.Radius <= 0) continue;

                    var ellipse = new Ellipse(
                        new System.Numerics.Vector2((float)pc.Center.X, (float)pc.Center.Y),
                        (float)pc.Radius,
                        (float)pc.Radius
                    );

                    var color = new Color4((float)rgb.Red, (float)rgb.Green, (float)rgb.Blue, (float)pc.Opacity);
                    using var brush = rt.CreateSolidColorBrush(color);

                    if (pc.Filled)
                    {
                        rt.FillEllipse(ellipse, brush);
                    }

                    if (pc.StrokeWidth > 0)
                    {
                        rt.DrawEllipse(ellipse, brush, (float)pc.StrokeWidth);
                    }
                }
            }

            // 2. Draw keystroke overlay pills
            if (_keystrokeModel != null)
            {
                var items = _keystrokeModel.Items(timeSeconds);
                if (items.Count > 0)
                {
                    var desc = bgraTarget.Description;
                    var frameSize = new Size(desc.Width, desc.Height);
                    double margin = 24 * _pixelsPerPoint;
                    double fontSize = _options.KeystrokeOverlay.Size.FontSize() * _pixelsPerPoint;
                    double padX = fontSize * 0.75;
                    double padY = fontSize * 0.5;
                    double cornerRadius = fontSize * 0.55;

                    foreach (var item in items)
                    {
                        if (item.Opacity <= 0) continue;

                        using var textLayout = _dwriteFactory.CreateTextLayout(item.Text, _textFormat!, 1000f, 100f);
                        var metrics = textLayout.Metrics;
                        double pillW = metrics.Width + padX * 2;
                        double pillH = metrics.Height + padY * 2;
                        var pillSize = new Size(pillW, pillH);
                        var rect = _options.KeystrokeOverlay.Position.Rect(pillSize, frameSize, margin);

                        var rawRect = new System.Drawing.RectangleF((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
                        var roundedRect = new RoundedRectangle(rawRect, (float)cornerRadius, (float)cornerRadius);

                        // Backdrop tint wash
                        float bgAlpha = (float)(item.Opacity * (_options.KeystrokeOverlay.BlurBackground ? 0.65 : 0.88));
                        var bgColor = new Color4(0.08f, 0.08f, 0.08f, bgAlpha);
                        using var bgBrush = rt.CreateSolidColorBrush(bgColor);
                        rt.FillRoundedRectangle(roundedRect, bgBrush);

                        // Text glyphs
                        var textColor = new Color4(0.98f, 0.98f, 0.98f, (float)item.Opacity);
                        using var textBrush = rt.CreateSolidColorBrush(textColor);
                        var textOrigin = new System.Numerics.Vector2((float)(rect.X + padX), (float)(rect.Y + padY));
                        rt.DrawTextLayout(textOrigin, textLayout, textBrush);
                    }
                }
            }

            // 3. Camera bubble is deferred post-MVP per spec (Package R4). UNCOVERED: deferred.
        }
        finally
        {
            rt.EndDraw();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _inputEventSource?.Stop();
        _textFormat?.Dispose();
        _dwriteFactory.Dispose();
        _d2dFactory.Dispose();
    }
}
