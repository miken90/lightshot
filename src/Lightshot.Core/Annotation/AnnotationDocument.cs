// Ported from LightshotKit/Sources/LightshotKit/AnnotationDocument.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lightshot.Core;

/// <summary>
/// The domain heart: a pure value type holding a screenshot plus its annotations,
/// mutated only through an explicit command API.
/// </summary>
public class AnnotationDocument : IEquatable<AnnotationDocument>
{
    private sealed class State : IEquatable<State>
    {
        public List<AnnotationElement> Elements { get; set; } = [];
        public Rect? CropRect { get; set; }

        public State Copy() => new()
        {
            Elements = Elements.Select(e => new AnnotationElement(
                e.Id,
                e.ElementKind,
                new Style(e.Style.Color, e.Style.StrokeWidth, e.Style.FontSize, e.Style.Fill)
            )).ToList(),
            CropRect = CropRect
        };

        public bool Equals(State? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (CropRect != other.CropRect) return false;
            if (Elements.Count != other.Elements.Count) return false;
            for (int i = 0; i < Elements.Count; i++)
            {
                if (!Elements[i].Equals(other.Elements[i])) return false;
            }
            return true;
        }

        public override int GetHashCode() => HashCode.Combine(Elements.Count, CropRect);
    }

    public CapturedImage BaseImage { get; }
    public CapturedImage Image => BaseImage;

    private State _state = new();
    private readonly List<State> _undoStack = [];
    private readonly List<State> _redoStack = [];

    private abstract record CoalesceKey
    {
        public sealed record StyleKey(ElementID Id) : CoalesceKey;
        public sealed record TextKey(ElementID Id) : CoalesceKey;
    }

    private CoalesceKey? _coalesceKey;

    public ElementID? SelectedID { get; private set; }

    public const double TextFontSizesMin = 6.0;
    public const double TextFontSizesMax = 200.0;
    private const double MinHitTolerance = 6.0;

    public static readonly IReadOnlyList<Handle> TextHandles = [Handle.Left, Handle.Right, Handle.BottomRight];

    public AnnotationDocument(CapturedImage baseImage)
    {
        BaseImage = baseImage;
    }

    public IReadOnlyList<AnnotationElement> Elements => _state.Elements;

    public Rect? CropRect => _state.CropRect;

    public Rect ImageBounds => new(0, 0, BaseImage.PixelWidth, BaseImage.PixelHeight);

    public Rect VisibleFrame => _state.CropRect ?? ImageBounds;

    /// <summary>Non-undoable presentation canvas style (null = disabled / tight capture).</summary>
    public CanvasStyle? Canvas { get; set; }

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public AnnotationElement? Element(ElementID id) =>
        _state.Elements.FirstOrDefault(e => e.Id == id);

    public ElementID? ElementID(Point point)
    {
        var hits = _state.Elements.AsEnumerable().Reverse().Where(element =>
        {
            double tol = Math.Max(element.Style.StrokeWidth / 2.0, MinHitTolerance);
            return element.ElementKind.HitTest(point, tol);
        }).ToList();

        return (hits.FirstOrDefault(e => e.ElementKind.FocusRect == null) ?? hits.FirstOrDefault())?.Id;
    }

    public ElementID? GrabbableElementID(Point point)
    {
        var hits = _state.Elements.AsEnumerable().Reverse().Where(element =>
        {
            double tol = Math.Max(element.Style.StrokeWidth / 2.0, MinHitTolerance);
            if (!element.ElementKind.HitTest(point, tol)) return false;
            if (element.Style.Fill != null) return true;
            return element.ElementKind.IsNearOutline(point, tol) ?? true;
        }).ToList();

        return (hits.FirstOrDefault(e => e.ElementKind.FocusRect == null) ?? hits.FirstOrDefault())?.Id;
    }

    public int NextStepNumber
    {
        get
        {
            int? highest = _state.Elements.Select(e => e.ElementKind switch
            {
                AnnotationElement.Kind.StepMarker sm => (int?)sm.Number,
                _ => null
            }).Where(n => n.HasValue).Max();

            return (highest ?? 0) + 1;
        }
    }

    public ElementID Add(AnnotationElement element)
    {
        AnnotationElement toAdd = element;
        if (toAdd.ElementKind is AnnotationElement.Kind.StepMarker sm)
        {
            toAdd = new AnnotationElement(toAdd.Id, new AnnotationElement.Kind.StepMarker(NextStepNumber, sm.Center, sm.Radius), toAdd.Style);
        }

        ElementID id = toAdd.Id;
        Perform(s => s.Elements.Add(toAdd));
        return id;
    }

    public IReadOnlyList<ElementID> Add(IEnumerable<AnnotationElement> elements)
    {
        int next = NextStepNumber;
        var numbered = new List<AnnotationElement>();
        foreach (AnnotationElement element in elements)
        {
            if (element.ElementKind is AnnotationElement.Kind.StepMarker sm)
            {
                numbered.Add(new AnnotationElement(element.Id, new AnnotationElement.Kind.StepMarker(next++, sm.Center, sm.Radius), element.Style));
            }
            else
            {
                numbered.Add(element);
            }
        }

        Perform(s => s.Elements.AddRange(numbered));
        return numbered.Select(e => e.Id).ToList();
    }

    public void Select(ElementID? id)
    {
        _coalesceKey = null;
        if (id is null)
        {
            SelectedID = null;
            return;
        }
        if (_state.Elements.Any(e => e.Id == id.Value))
        {
            SelectedID = id;
        }
    }

    public void Transform(ElementID id, Transform transform)
    {
        WithElement(id, element =>
        {
            switch (transform)
            {
                case Transform.Move m:
                    element.ElementKind = element.ElementKind.Moved(m.Dx, m.Dy);
                    break;
                case Transform.Resize r:
                    if (element.ElementKind is AnnotationElement.Kind.Text)
                    {
                        ResizeText(element, r.Handle, r.Dx);
                    }
                    else
                    {
                        element.ElementKind = element.ElementKind.Resized(r.Handle, r.Dx, r.Dy);
                    }
                    break;
                case Transform.Reshape resh:
                    element.ElementKind = element.ElementKind.Reshaped(resh.Handle, resh.Dx, resh.Dy);
                    break;
            }
        });
    }

    public void SetStyle(ElementID id, Style style)
    {
        WithElement(id, new CoalesceKey.StyleKey(id), element =>
        {
            double oldSize = element.Style.FontSize;
            element.Style = style;
            if (element.ElementKind is AnnotationElement.Kind.Text t && Math.Abs(oldSize - style.FontSize) > 1e-6)
            {
                bool natural = TextLayout.HasNaturalWidth(t.Box, t.Content, oldSize);
                element.ElementKind = new AnnotationElement.Kind.Text(t.Content, TextLayout.Box(
                    t.Content, style.FontSize, t.Box.Standardized.Origin, natural ? null : t.Box.Standardized.Width
                ));
            }
        });
    }

    public void SetStepRadius(ElementID id, double radius)
    {
        WithElement(id, new CoalesceKey.StyleKey(id), element =>
        {
            if (element.ElementKind is AnnotationElement.Kind.StepMarker sm)
            {
                element.ElementKind = new AnnotationElement.Kind.StepMarker(sm.Number, sm.Center, Math.Max(radius, 1.0));
            }
        });
    }

    public void SetArrowStyle(ElementID id, ArrowStyle style)
    {
        WithElement(id, element =>
        {
            if (element.ElementKind is AnnotationElement.Kind.Arrow a)
            {
                Point? newBend = style.IsBendable()
                    ? (a.Bend ?? ArrowGeometry.DefaultArrowBend(a.From, a.To))
                    : null;
                element.ElementKind = new AnnotationElement.Kind.Arrow(a.From, a.To, newBend, style);
            }
        });
    }

    public void SetRedaction(ElementID id, RedactionStyle style, double strength)
    {
        WithElement(id, new CoalesceKey.StyleKey(id), element =>
        {
            if (element.ElementKind is AnnotationElement.Kind.Redaction r)
            {
                element.ElementKind = new AnnotationElement.Kind.Redaction(
                    r.Rect, style, Math.Min(Math.Max(strength, 0.0), 1.0), r.Seed
                );
            }
        });
    }

    public void UpdateText(ElementID id, string text)
    {
        WithElement(id, new CoalesceKey.TextKey(id), element =>
        {
            if (element.ElementKind is AnnotationElement.Kind.Text t)
            {
                double fontSize = element.Style.FontSize;
                bool natural = TextLayout.HasNaturalWidth(t.Box, t.Content, fontSize);
                element.ElementKind = new AnnotationElement.Kind.Text(text, TextLayout.Box(
                    text, fontSize, t.Box.Standardized.Origin, natural ? null : t.Box.Standardized.Width
                ));
            }
        });
    }

    private static void ResizeText(AnnotationElement element, Handle handle, double dx)
    {
        if (element.ElementKind is not AnnotationElement.Kind.Text t) return;
        Rect old = t.Box.Standardized;
        double fontSize = element.Style.FontSize;

        switch (handle)
        {
            case Handle.Left or Handle.Right:
                double width = Math.Max(old.Width + (handle == Handle.Left ? -dx : dx), TextLayout.MinimumWidth(fontSize));
                double x = handle == Handle.Left ? old.MaxX - width : old.MinX;
                element.ElementKind = new AnnotationElement.Kind.Text(t.Content, TextLayout.Box(
                    t.Content, fontSize, new Point(x, old.MinY), width
                ));
                break;
            case Handle.BottomRight:
                double scale = Math.Max(old.Width + dx, 1.0) / Math.Max(old.Width, 1.0);
                double size = Math.Min(Math.Max(Math.Round(fontSize * scale), TextFontSizesMin), TextFontSizesMax);
                double ratio = size / fontSize;
                bool natural = TextLayout.HasNaturalWidth(old, t.Content, fontSize);
                element.Style.FontSize = size;
                element.ElementKind = new AnnotationElement.Kind.Text(t.Content, TextLayout.Box(
                    t.Content, size, old.Origin, natural ? null : old.Width * ratio
                ));
                break;
        }
    }

    public void EndCoalescing() => _coalesceKey = null;

    public void Delete(ElementID id)
    {
        Perform(s => s.Elements.RemoveAll(e => e.Id == id));
        if (SelectedID == id) SelectedID = null;
    }

    public void Reorder(ElementID id, int index)
    {
        Perform(s =>
        {
            int current = s.Elements.FindIndex(e => e.Id == id);
            if (current < 0) return;
            AnnotationElement item = s.Elements[current];
            s.Elements.RemoveAt(current);
            int clamped = Math.Min(Math.Max(index, 0), s.Elements.Count);
            s.Elements.Insert(clamped, item);
        });
    }

    public void ApplyCrop(Rect rect)
    {
        Rect bounds = ImageBounds;
        Perform(s =>
        {
            Rect? clamped = bounds.Intersection(rect);
            if (clamped.HasValue)
            {
                s.CropRect = clamped.Value;
            }
        });
    }

    public void Undo()
    {
        _coalesceKey = null;
        if (_undoStack.Count == 0) return;
        State previous = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _redoStack.Add(_state);
        _state = previous;
        SanitizeSelection();
    }

    public void Redo()
    {
        _coalesceKey = null;
        if (_redoStack.Count == 0) return;
        State next = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _undoStack.Add(_state);
        _state = next;
        SanitizeSelection();
    }

    private void WithElement(ElementID id, Action<AnnotationElement> action) =>
        WithElement(id, null, action);

    private void WithElement(ElementID id, CoalesceKey? key, Action<AnnotationElement> action)
    {
        Perform(key, s =>
        {
            int index = s.Elements.FindIndex(e => e.Id == id);
            if (index >= 0)
            {
                action(s.Elements[index]);
            }
        });
    }

    private void Perform(Action<State> change) => Perform(null, change);

    private void Perform(CoalesceKey? key, Action<State> change)
    {
        State before = _state.Copy();
        var next = _state.Copy();
        change(next);
        if (next.Equals(_state)) return;

        if (key == null || key != _coalesceKey)
        {
            _undoStack.Add(before);
        }
        _redoStack.Clear();
        _coalesceKey = key;
        _state = next;
    }

    private void SanitizeSelection()
    {
        if (SelectedID.HasValue && !_state.Elements.Any(e => e.Id == SelectedID.Value))
        {
            SelectedID = null;
        }
    }

    public bool Equals(AnnotationDocument? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return BaseImage.Equals(other.BaseImage) &&
               _state.Equals(other._state) &&
               SelectedID.Equals(other.SelectedID);
    }

    public override bool Equals(object? obj) => obj is AnnotationDocument other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(BaseImage, _state, SelectedID);
}
