// Ported from LightshotKit/Sources/LightshotKit/PinBoardController.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.App.Views.Pin;

/// <summary>
/// Manages active floating pinned windows, cascade placement and lifetime.
/// </summary>
public class PinBoard : IDisposable
{
    private readonly List<PinWindow> _windows = [];
    private readonly Func<Point, (Rect WorkAreaDip, double ScaleFactor)>? _displayResolver;
    private bool _disposed;

    public IReadOnlyList<PinWindow> Windows => _windows;

    public PinBoard(Func<Point, (Rect WorkAreaDip, double ScaleFactor)>? displayResolver = null)
    {
        _displayResolver = displayResolver;
    }

    public PinWindow Pin(
        CapturedImage image,
        Action? copyAction = null,
        Func<bool>? saveAction = null,
        Func<bool>? saveAsAction = null)
    {
        var (workAreaDip, scaleFactor) = ResolveDisplayInfo();
        var size = PinWindowMath.CalculateInitialSize(image.PixelWidth, image.PixelHeight, scaleFactor, workAreaDip);

        var window = new PinWindow(image, size);
        window.OnCopy = copyAction;
        window.OnSave = saveAction;
        window.OnSaveAs = saveAsAction;

        int cascadeIndex = _windows.Count;
        window.OnClose = () =>
        {
            _windows.Remove(window);
        };

        // Center in work area, then offset down-right by cascadeIndex * 24
        double centerX = workAreaDip.MinX + (workAreaDip.Width - size.Width) / 2.0;
        double centerY = workAreaDip.MinY + (workAreaDip.Height - size.Height) / 2.0;
        double offset = cascadeIndex * 24.0;

        window.Left = centerX + offset;
        window.Top = centerY + offset;

        _windows.Add(window);
        window.Show();

        return window;
    }

    public void CloseAll()
    {
        var copy = _windows.ToList();
        foreach (var win in copy)
        {
            try
            {
                win.Close();
            }
            catch
            {
                // Window may already be closing
            }
        }
        _windows.Clear();
    }

    private (Rect WorkAreaDip, double ScaleFactor) ResolveDisplayInfo()
    {
        if (_displayResolver != null)
        {
            return _displayResolver(new Point(0, 0));
        }

        try
        {
            var primary = DisplayTopology.GetDisplays().FirstOrDefault(d => d.IsPrimary)
                          ?? DisplayTopology.GetDisplays().FirstOrDefault();

            if (primary != null)
            {
                double scale = primary.ScaleFactor > 0 ? primary.ScaleFactor : 1.0;
                var wa = new Rect(
                    primary.WorkArea.MinX / scale,
                    primary.WorkArea.MinY / scale,
                    primary.WorkArea.Width / scale,
                    primary.WorkArea.Height / scale);
                return (wa, scale);
            }
        }
        catch
        {
            // Ignore display topology failures
        }

        var pwa = SystemParameters.WorkArea;
        return (new Rect(pwa.Left, pwa.Top, pwa.Width, pwa.Height), 1.0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseAll();
    }
}
