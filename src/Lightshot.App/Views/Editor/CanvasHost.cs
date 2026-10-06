// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lightshot.Core;
using Lightshot.Rendering;
using SkiaSharp;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.App.Views.Editor;

/// <summary>
/// WPF host element for rendering SkiaSharp canvas and AnnotationDocument.
/// Manages cached base and redaction patches, maintains a backing SKBitmap,
/// and blits to WPF DrawingContext.
/// </summary>
public sealed class CanvasHost : FrameworkElement, IDisposable
{
    private readonly RedactionPreviewCache _redactionCache = new();
    private AnnotationDocument? _document;
    private EditorViewModel? _viewModel;

    public AnnotationDocument? Document
    {
        get => _document;
        set
        {
            if (_document == value) return;
            _document = value;
            RenderCanvas();
        }
    }

    public EditorViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel != null)
            {
                _viewModel.CanvasInvalidated -= OnViewModelCanvasInvalidated;
            }
            _viewModel = value;
            if (_viewModel != null)
            {
                _viewModel.CanvasInvalidated += OnViewModelCanvasInvalidated;
                if (_document == null && _viewModel.Document != null)
                {
                    _document = _viewModel.Document;
                }
            }
            RenderCanvas();
        }
    }

    public SKBitmap? BackingBitmap { get; private set; }
    public BitmapSource? BackingBitmapSource { get; private set; }

    public double Zoom { get; set; } = 1.0;

    public CanvasHost()
    {
        ClipToBounds = true;
    }

    private void OnViewModelCanvasInvalidated()
    {
        if (_viewModel?.Document != null)
        {
            _document = _viewModel.Document;
        }
        RenderCanvas();
    }

    /// <summary>
    /// Flattens the active document and elements into the backing SKBitmap
    /// and generates a WPF BitmapSource for rendering.
    /// </summary>
    public void RenderCanvas()
    {
        var doc = _document ?? _viewModel?.Document;
        if (doc == null)
        {
            BackingBitmap?.Dispose();
            BackingBitmap = null;
            BackingBitmapSource = null;
            InvalidateVisual();
            return;
        }

        var elements = _viewModel != null ? _viewModel.DisplayElements : doc.Elements;
        // A focus area still being dragged out dims like a placed one; the draft itself is
        // painted separately below, and ElementPainter draws nothing for focus.
        var draft = _viewModel?.DraftElement;
        var focusElements = draft != null ? elements.Append(draft) : elements;
        var newBitmap = DocumentRenderer.Flatten(doc, elements, focusElements);

        if (newBitmap != null && _viewModel?.DraftElement != null)
        {
            // Draw in-progress draft shape on top
            using var canvas = new SKCanvas(newBitmap);
            var frame = doc.VisibleFrame;
            Func<SKBitmap?> snapshotProvider = () =>
            {
                var snap = new SKBitmap(newBitmap.Info);
                newBitmap.CopyTo(snap);
                return snap;
            };

            ElementPainter.Draw(canvas, _viewModel.DraftElement, frame, snapshotProvider);
            canvas.Flush();
        }

        BackingBitmap?.Dispose();
        BackingBitmap = newBitmap;

        if (BackingBitmap != null && BackingBitmap.Width > 0 && BackingBitmap.Height > 0)
        {
            var surface = PixelSurfaceConverter.FromBitmap(BackingBitmap);
            var bs = BitmapSource.Create(
                surface.Width,
                surface.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                surface.Pixels,
                surface.Stride);
            bs.Freeze();
            BackingBitmapSource = bs;

            Width = surface.Width * Zoom;
            Height = surface.Height * Zoom;
        }
        else
        {
            BackingBitmapSource = null;
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (BackingBitmapSource != null)
        {
            dc.DrawImage(BackingBitmapSource, new System.Windows.Rect(0, 0, ActualWidth, ActualHeight));
        }
    }

    /// <summary>
    /// Maps screen/WPF point to document image pixel coordinates.
    /// </summary>
    public Point ScreenToImage(System.Windows.Point screenPoint)
    {
        var doc = _document ?? _viewModel?.Document;
        if (doc == null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return new Point(screenPoint.X, screenPoint.Y);
        }

        var frame = doc.VisibleFrame;
        double scaleX = frame.Width / ActualWidth;
        double scaleY = frame.Height / ActualHeight;

        double imageX = frame.MinX + screenPoint.X * scaleX;
        double imageY = frame.MinY + screenPoint.Y * scaleY;
        return new Point(imageX, imageY);
    }

    /// <summary>
    /// Maps document image pixel point to screen/WPF coordinates.
    /// </summary>
    public System.Windows.Point ImageToScreen(Point imagePoint)
    {
        var doc = _document ?? _viewModel?.Document;
        if (doc == null)
        {
            return new System.Windows.Point(imagePoint.X, imagePoint.Y);
        }

        var frame = doc.VisibleFrame;
        double scaleX = ActualWidth > 0 ? ActualWidth / frame.Width : Zoom;
        double scaleY = ActualHeight > 0 ? ActualHeight / frame.Height : Zoom;

        double screenX = (imagePoint.X - frame.MinX) * scaleX;
        double screenY = (imagePoint.Y - frame.MinY) * scaleY;
        return new System.Windows.Point(screenX, screenY);
    }

    public void Dispose()
    {
        if (_viewModel != null)
        {
            _viewModel.CanvasInvalidated -= OnViewModelCanvasInvalidated;
        }
        _redactionCache.Clear();
        BackingBitmap?.Dispose();
        BackingBitmap = null;
        BackingBitmapSource = null;
    }

    private sealed class RedactionPreviewCache
    {
        private sealed class Entry
        {
            public List<AnnotationElement> Below { get; }
            public Rect Frame { get; }
            public RedactionBackdrop Backdrop { get; }
            public AnnotationElement.Kind? Kind { get; set; }
            public RedactionPatch? Patch { get; set; }

            public Entry(List<AnnotationElement> below, Rect frame, RedactionBackdrop backdrop)
            {
                Below = below;
                Frame = frame;
                Backdrop = backdrop;
            }
        }

        private readonly Dictionary<int, Entry> _entries = new();

        public RedactionPatch? GetPatch(AnnotationElement element, int index, AnnotationDocument document)
        {
            var keysToRemove = _entries.Keys.Where(k => k > document.Elements.Count).ToList();
            foreach (var k in keysToRemove)
            {
                if (_entries.TryGetValue(k, out var oldEntry))
                {
                    oldEntry.Patch?.Dispose();
                }
                _entries.Remove(k);
            }

            if (element.ElementKind is not AnnotationElement.Kind.Redaction r || r.Style == RedactionStyle.Blackout)
            {
                if (_entries.TryGetValue(index, out var existing))
                {
                    existing.Patch?.Dispose();
                }
                _entries.Remove(index);
                return null;
            }

            var below = document.Elements.Take(index).ToList();
            Entry entry;
            if (_entries.TryGetValue(index, out var cached) &&
                ElementsEqual(cached.Below, below) &&
                cached.Frame == document.VisibleFrame)
            {
                entry = cached;
            }
            else
            {
                var backdrop = DocumentRenderer.CreateRedactionBackdrop(document, index);
                if (backdrop == null) return null;
                entry = new Entry(below, document.VisibleFrame, backdrop);
                _entries[index] = entry;
            }

            if (entry.Kind != element.ElementKind)
            {
                entry.Kind = element.ElementKind;
                entry.Patch?.Dispose();
                entry.Patch = entry.Backdrop.Patch(r.Rect, r.Style, r.Strength, r.Seed);
            }

            return entry.Patch;
        }

        private static bool ElementsEqual(IReadOnlyList<AnnotationElement> a, IReadOnlyList<AnnotationElement> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i])) return false;
            }
            return true;
        }

        public void Clear()
        {
            foreach (var e in _entries.Values)
            {
                e.Patch?.Dispose();
            }
            _entries.Clear();
        }
    }
}
