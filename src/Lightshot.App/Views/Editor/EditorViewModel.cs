// Ported from App/Sources/EditorModel.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Lightshot.Core;
using Lightshot.Rendering;

namespace Lightshot.App.Views.Editor;

public enum EditorTool
{
    Select,
    Arrow,
    Line,
    Rectangle,
    Ellipse,
    Freehand,
    Text,
    Step,
    Highlight,
    Focus,
    Redact,
    Crop
}

public class EditorViewModel : INotifyPropertyChanged
{
    public const string ArrowStyleDefaultsKey = Lightshot.Platform.Windows.Settings.SettingsKeys.EditorLastArrowStyle;
    public const double DefaultWindowWidth = 1180.0;
    public const double DefaultWindowHeight = 800.0;
    public const double WorkAreaWidthCapRatio = 0.90;
    public const double WorkAreaHeightCapRatio = 0.85;

    public AnnotationDocument Document { get; }

    private readonly IImageRenderer _renderer;
    private readonly IImageSink _imageSink;
    private readonly IImageSource? _imageSource;
    private readonly ISettingsStore? _settingsStore;

    private EditorTool _activeTool = EditorTool.Select;
    private Style _activeStyle = Style.Default;
    private ArrowStyle _arrowStyle = ArrowStyle.Standard;
    private RedactionStyle _redactionStyle = RedactionStyle.Pixelate;
    private double _redactionStrength = RedactionStyleDefaults.DefaultStrength;

    private static double s_lastRedactionStrength = RedactionStyleDefaults.DefaultStrength;

    private Rect? _cropDraft;
    private ElementID? _editingTextID;
    private bool _releasesTextWhenDone;
    private bool _reopensText;
    private double _viewScale = 1.0;

    private Draft? _draft;
    private DragSession? _drag;
    private CropSession? _cropSession;
    private bool _gestureActive;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? RequestClose;
    public event Action? CanvasInvalidated;
    public event Action<(string Title, string Message, string? Details)>? ShowError;

    public EditorViewModel(
        AnnotationDocument document,
        IImageSink imageSink,
        IImageRenderer? renderer = null,
        IImageSource? imageSource = null,
        ISettingsStore? settingsStore = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        _imageSink = imageSink ?? throw new ArgumentNullException(nameof(imageSink));
        _renderer = renderer ?? new DocumentRenderer();
        _imageSource = imageSource;
        _settingsStore = settingsStore;

        _arrowStyle = LoadLastArrowStyle();
    }

    public static (double Width, double Height) CalculateWindowSize(double workAreaWidth, double workAreaHeight)
    {
        double width = Math.Min(DefaultWindowWidth, workAreaWidth * WorkAreaWidthCapRatio);
        double height = Math.Min(DefaultWindowHeight, workAreaHeight * WorkAreaHeightCapRatio);
        return (width, height);
    }

    public EditorTool ActiveTool
    {
        get => _activeTool;
        set
        {
            if (_activeTool == value) return;
            var old = _activeTool;
            _activeTool = value;

            if (_activeTool != EditorTool.Select)
            {
                EndTextEditing();
            }

            if (_activeTool == EditorTool.Redact)
            {
                // Opening the redact tool always starts a new redaction at the default style (Pixelate)
                Document.Select(null);
                RedactionStyle = RedactionStyle.Pixelate;
            }

            if (_activeTool == EditorTool.Crop)
            {
                Document.Select(null);
                _cropDraft = DocumentCropFrame;
            }
            else if (old == EditorTool.Crop)
            {
                _cropDraft = null;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCropping));
            OnPropertyChanged(nameof(CropFrame));
            OnPropertyChanged(nameof(VisibleStyleFields));
            NotifyCanvasChanged();
        }
    }

    public Style ActiveStyle
    {
        get => _activeStyle;
        set
        {
            _activeStyle = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveColor));
            OnPropertyChanged(nameof(StrokeWidth));
            OnPropertyChanged(nameof(FontSize));
        }
    }

    public RGBAColor ActiveColor
    {
        get => _activeStyle.Color;
        set
        {
            if (_activeStyle.Color == value) return;
            _activeStyle.Color = value;
            ApplyStyleToSelection();
            OnPropertyChanged();
            NotifyCanvasChanged();
        }
    }

    public double StrokeWidth
    {
        get => _activeStyle.StrokeWidth;
        set
        {
            if (Math.Abs(_activeStyle.StrokeWidth - value) < 1e-6) return;
            _activeStyle.StrokeWidth = value;
            ApplyStyleToSelection();
            OnPropertyChanged();
            NotifyCanvasChanged();
        }
    }

    public double FontSize
    {
        get => _activeStyle.FontSize;
        set
        {
            if (Math.Abs(_activeStyle.FontSize - value) < 1e-6) return;
            _activeStyle.FontSize = value;
            if (Document.SelectedID.HasValue)
            {
                var id = Document.SelectedID.Value;
                Document.SetStyle(id, _activeStyle);
                if (Document.Element(id)?.ElementKind is AnnotationElement.Kind.StepMarker)
                {
                    Document.SetStepRadius(id, value);
                }
            }
            OnPropertyChanged();
            NotifyCanvasChanged();
        }
    }

    public ArrowStyle ArrowStyle
    {
        get => _arrowStyle;
        set
        {
            if (_arrowStyle == value) return;
            _arrowStyle = value;
            SaveLastArrowStyle(value);
            if (Document.SelectedID.HasValue)
            {
                Document.SetArrowStyle(Document.SelectedID.Value, value);
            }
            OnPropertyChanged();
            NotifyCanvasChanged();
        }
    }

    public RedactionStyle RedactionStyle
    {
        get => _redactionStyle;
        set
        {
            if (_redactionStyle == value) return;
            _redactionStyle = value;
            ApplyRedactionToSelection();
            OnPropertyChanged();
            NotifyCanvasChanged();
        }
    }

    public double RedactionStrength
    {
        get => _redactionStrength;
        set
        {
            if (Math.Abs(_redactionStrength - value) < 1e-6) return;
            _redactionStrength = Math.Clamp(value, 0.0, 1.0);
            s_lastRedactionStrength = _redactionStrength;
            ApplyRedactionToSelection();
            OnPropertyChanged();
            NotifyCanvasChanged();
        }
    }

    private void ApplyStyleToSelection()
    {
        if (Document.SelectedID.HasValue)
        {
            Document.SetStyle(Document.SelectedID.Value, _activeStyle);
        }
    }

    private void ApplyRedactionToSelection()
    {
        s_lastRedactionStrength = _redactionStrength;
        if (Document.SelectedID.HasValue)
        {
            Document.SetRedaction(Document.SelectedID.Value, _redactionStyle, _redactionStrength);
        }
    }

    public void CommitStyleEdit()
    {
        Document.EndCoalescing();
    }

    public StyleFields VisibleStyleFields
    {
        get
        {
            if (ActiveTool != EditorTool.Crop && Document.SelectedID.HasValue)
            {
                var selected = Document.Element(Document.SelectedID.Value);
                if (selected != null)
                {
                    return StyleFieldsExtensions.Fields(selected.ElementKind);
                }
            }

            return ActiveTool switch
            {
                EditorTool.Arrow => StyleFields.Color | StyleFields.StrokeWidth | StyleFields.ArrowStyle,
                EditorTool.Line or EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Freehand =>
                    StyleFields.Color | StyleFields.StrokeWidth,
                EditorTool.Text or EditorTool.Step =>
                    StyleFields.Color | StyleFields.FontSize,
                EditorTool.Highlight => StyleFields.Color,
                EditorTool.Redact => StyleFields.Redaction,
                EditorTool.Focus or EditorTool.Crop => StyleFields.None,
                EditorTool.Select => Document.SelectedID.HasValue && Document.Element(Document.SelectedID.Value) != null
                    ? StyleFieldsExtensions.Fields(Document.Element(Document.SelectedID.Value)!.ElementKind)
                    : StyleFields.None,
                _ => StyleFields.None
            };
        }
    }

    public bool HasSelection => Document.SelectedID.HasValue;
    public ElementID? SelectedID => Document.SelectedID;
    public bool CanUndo => Document.CanUndo;
    public bool CanRedo => Document.CanRedo;

    public bool IsCropping => ActiveTool == EditorTool.Crop;

    public Rect? CropFrame
    {
        get
        {
            if (IsCropping) return _cropDraft;
            return HasEffectiveCrop ? Document.CropRect : null;
        }
    }

    public bool CanResetCrop => HasEffectiveCrop;

    public bool HasEffectiveCrop =>
        Document.CropRect.HasValue && Document.CropRect.Value.Standardized != Document.ImageBounds.Standardized;

    public Rect DocumentCropFrame => Document.VisibleFrame.Standardized;

    public void ResetCrop()
    {
        Document.ApplyCrop(Document.ImageBounds);
        SyncCropDraft();
        NotifyCanvasChanged();
    }

    private void SyncCropDraft()
    {
        if (IsCropping)
        {
            _cropDraft = DocumentCropFrame;
        }
        OnPropertyChanged(nameof(CropFrame));
        OnPropertyChanged(nameof(CanResetCrop));
        OnPropertyChanged(nameof(HasEffectiveCrop));
    }

    public ElementID? EditingTextID
    {
        get => _editingTextID;
        private set
        {
            if (_editingTextID == value) return;
            _editingTextID = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditingText));
            OnPropertyChanged(nameof(EditingText));
        }
    }

    public bool IsEditingText => _editingTextID.HasValue;

    public string EditingText
    {
        get
        {
            if (_editingTextID.HasValue && Document.Element(_editingTextID.Value)?.ElementKind is AnnotationElement.Kind.Text t)
            {
                return t.Content;
            }
            return string.Empty;
        }
        set
        {
            if (!_editingTextID.HasValue) return;
            Document.UpdateText(_editingTextID.Value, value ?? string.Empty);
            OnPropertyChanged();
            NotifyCanvasChanged();
        }
    }

    public void BeginEditingSelectedText()
    {
        if (Document.SelectedID.HasValue && Document.Element(Document.SelectedID.Value)?.ElementKind is AnnotationElement.Kind.Text)
        {
            EditingTextID = Document.SelectedID.Value;
        }
    }

    public void EndTextEditing()
    {
        if (!_editingTextID.HasValue) return;
        var id = _editingTextID.Value;
        EditingTextID = null;

        if (_releasesTextWhenDone && Document.SelectedID == id)
        {
            Document.Select(null);
        }
        _releasesTextWhenDone = false;
        Document.EndCoalescing();

        if (Document.Element(id)?.ElementKind is AnnotationElement.Kind.Text t &&
            string.IsNullOrWhiteSpace(t.Content))
        {
            Document.Delete(id);
        }

        NotifyCanvasChanged();
    }

    public double ViewScale
    {
        get => _viewScale;
        set
        {
            if (Math.Abs(_viewScale - value) < 1e-6) return;
            _viewScale = Math.Max(value, 0.0001);
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<AnnotationElement> DisplayElements
    {
        get
        {
            if (_drag == null) return Document.Elements;
            var previewElements = new List<AnnotationElement>(Document.Elements.Count);
            foreach (var el in Document.Elements)
            {
                if (el.Id == _drag.Id)
                {
                    var copy = new AnnotationElement(el.Id, el.ElementKind, el.Style);
                    switch (_drag.CurrentTransform)
                    {
                        case Transform.Move m:
                            copy.ElementKind = copy.ElementKind.Moved(m.Dx, m.Dy);
                            break;
                        case Transform.Resize r:
                            copy.ElementKind = copy.ElementKind.Resized(r.Handle, r.Dx, r.Dy);
                            break;
                        case Transform.Reshape resh:
                            copy.ElementKind = copy.ElementKind.Reshaped(resh.Handle, resh.Dx, resh.Dy);
                            break;
                    }
                    previewElements.Add(copy);
                }
                else
                {
                    previewElements.Add(el);
                }
            }
            return previewElements;
        }
    }

    public AnnotationElement? DraftElement
    {
        get
        {
            if (_draft == null || _draft.Kind == null) return null;
            return new AnnotationElement(null, _draft.Kind, _activeStyle);
        }
    }

    // MARK: - Gestures

    public void GestureStarted(Point point)
    {
        _gestureActive = true;
        EndTextEditing();

        switch (ActiveTool)
        {
            case EditorTool.Select:
                BeginSelectGesture(point);
                break;
            case EditorTool.Crop:
                BeginCropGesture(point);
                break;
            case EditorTool.Text:
                if (BeginHandleDrag(point)) return;
                var textHit = Document.ElementID(point);
                if (textHit.HasValue && IsText(textHit.Value))
                {
                    Document.Select(textHit.Value);
                    SyncStyleToSelection();
                    EditingTextID = textHit.Value;
                    _releasesTextWhenDone = true;
                    return;
                }
                StartDraft(point);
                break;
            case EditorTool.Step:
                if (BeginHandleDrag(point)) return;
                var existingStep = Document.GrabbableElementID(point);
                if (existingStep.HasValue)
                {
                    Document.Select(existingStep.Value);
                    SyncStyleToSelection();
                    _drag = new DragSession(existingStep.Value, new DragSession.Mode.MoveMode(), point, point);
                    return;
                }
                // Step placement
                StartDraft(point);
                break;
            default: // Shape tools
                if (BeginHandleDrag(point)) return;
                var hit = Document.GrabbableElementID(point);
                if (hit.HasValue)
                {
                    Document.Select(hit.Value);
                    SyncStyleToSelection();
                    _drag = new DragSession(hit.Value, new DragSession.Mode.MoveMode(), point, point);
                    return;
                }
                StartDraft(point);
                break;
        }

        NotifyCanvasChanged();
    }

    public void GestureMoved(Point point)
    {
        if (!_gestureActive) return;

        if (_cropSession != null)
        {
            _cropDraft = _cropSession.GetRect(point, Document.ImageBounds);
            OnPropertyChanged(nameof(CropFrame));
            NotifyCanvasChanged();
        }
        else if (_drag != null)
        {
            _drag.Current = point;
            NotifyCanvasChanged();
        }
        else if (_draft != null)
        {
            _draft.Current = point;
            _draft.Points.Add(point);
            NotifyCanvasChanged();
        }
    }

    public void GestureEnded(Point point)
    {
        if (!_gestureActive) return;
        _gestureActive = false;

        try
        {
            if (_cropSession != null)
            {
                var rect = _cropSession.GetRect(point, Document.ImageBounds);
                if (rect.Width >= 8.0 && rect.Height >= 8.0)
                {
                    _cropDraft = rect;
                    Document.ApplyCrop(rect);
                }
                return;
            }

            if (_drag != null)
            {
                bool reopen = _reopensText;
                _reopensText = false;
                if (reopen && _drag.Start.Distance(point) < 2.0)
                {
                    EditingTextID = _drag.Id;
                    return;
                }

                Document.Transform(_drag.Id, _drag.CurrentTransform);
                return;
            }

            if (_draft == null) return;
            _draft.Current = point;
            if (_draft.Points.Count == 0 || _draft.Points[^1] != point)
            {
                _draft.Points.Add(point);
            }

            switch (_draft.Tool)
            {
                case EditorTool.Text:
                    PlaceText(_draft.Start);
                    break;
                case EditorTool.Step:
                    PlaceStepMarker(_draft.Start);
                    break;
                default:
                    if (_draft.Kind != null)
                    {
                        Document.Add(new AnnotationElement(null, _draft.Kind, _activeStyle));
                    }
                    else
                    {
                        Document.Select(null);
                    }
                    break;
            }
        }
        finally
        {
            _draft = null;
            _drag = null;
            _cropSession = null;
            SyncCropDraft();
            NotifyCanvasChanged();
        }
    }

    public bool DoubleClick(Point point)
    {
        if (ActiveTool == EditorTool.Crop) return false;
        var hit = Document.ElementID(point);
        if (!hit.HasValue) return false;

        Document.Select(hit.Value);
        SyncStyleToSelection();
        BeginEditingSelectedText();
        NotifyCanvasChanged();
        return EditingTextID.HasValue;
    }

    private void StartDraft(Point point)
    {
        _draft = new Draft(
            ActiveTool,
            point,
            point,
            new List<Point> { point },
            ArrowStyle,
            RedactionStyle,
            RedactionStrength,
            (ulong)Random.Shared.NextInt64());
    }

    private void BeginSelectGesture(Point point)
    {
        if (BeginHandleDrag(point)) return;
        var hit = Document.ElementID(point);
        if (hit.HasValue)
        {
            _reopensText = Document.SelectedID == hit && IsText(hit.Value);
            Document.Select(hit.Value);
            SyncStyleToSelection();
            _drag = new DragSession(hit.Value, new DragSession.Mode.MoveMode(), point, point);
        }
        else
        {
            Document.Select(null);
        }
    }

    private bool BeginHandleDrag(Point point)
    {
        if (!Document.SelectedID.HasValue) return false;
        var id = Document.SelectedID.Value;
        var element = Document.Element(id);
        if (element == null) return false;

        var kind = element.ElementKind;
        if (kind.EndpointHandles != null)
        {
            var hit = kind.EndpointHandles.LastOrDefault(h => IsWithinGrabRadius(point, h.Point));
            if (hit != default)
            {
                _drag = new DragSession(id, new DragSession.Mode.ReshapeMode(hit.Handle), point, point);
                return true;
            }
        }
        else
        {
            var offered = kind is AnnotationElement.Kind.Text
                ? AnnotationDocument.TextHandles
                : (Handle[])Enum.GetValues(typeof(Handle));

            foreach (var h in offered)
            {
                var hp = GeometryUtils.HandlePoint(h, kind.BoundingBox);
                if (IsWithinGrabRadius(point, hp))
                {
                    _drag = new DragSession(id, new DragSession.Mode.ResizeMode(h), point, point);
                    return true;
                }
            }
        }

        return false;
    }

    private void BeginCropGesture(Point point)
    {
        var rect = _cropDraft ?? Document.ImageBounds;
        var handle = CropHandle(point, rect);
        if (handle.HasValue)
        {
            _cropSession = new CropSession(new CropSession.Mode.Resize(handle.Value), point, rect);
        }
        else if (rect.Contains(point))
        {
            _cropSession = new CropSession(new CropSession.Mode.Move(), point, rect);
        }
        else
        {
            _cropSession = new CropSession(new CropSession.Mode.Draw(), point, rect);
        }
    }

    private Handle? CropHandle(Point point, Rect rect)
    {
        Handle[] corners = [Handle.TopLeft, Handle.TopRight, Handle.BottomLeft, Handle.BottomRight];
        foreach (var c in corners)
        {
            if (IsWithinGrabRadius(point, GeometryUtils.HandlePoint(c, rect)))
            {
                return c;
            }
        }

        var box = rect.Standardized;
        double tol = Math.Max(6.0 / Math.Max(ViewScale, 0.0001), 4.0);
        bool withinX = point.X >= box.MinX - tol && point.X <= box.MaxX + tol;
        bool withinY = point.Y >= box.MinY - tol && point.Y <= box.MaxY + tol;

        if (withinY && Math.Abs(point.X - box.MinX) <= tol) return Handle.Left;
        if (withinY && Math.Abs(point.X - box.MaxX) <= tol) return Handle.Right;
        if (withinX && Math.Abs(point.Y - box.MinY) <= tol) return Handle.Top;
        if (withinX && Math.Abs(point.Y - box.MinY) <= tol) return Handle.Bottom;

        return null;
    }

    private bool IsWithinGrabRadius(Point point, Point target)
    {
        double tol = Math.Max(6.0 / Math.Max(ViewScale, 0.0001), 4.0);
        return Math.Abs(target.X - point.X) <= tol && Math.Abs(target.Y - point.Y) <= tol;
    }

    private bool IsText(ElementID id) =>
        Document.Element(id)?.ElementKind is AnnotationElement.Kind.Text;

    private void PlaceText(Point point)
    {
        var box = TextLayout.Box("", _activeStyle.FontSize, point);
        var id = Document.Add(new AnnotationElement(null, new AnnotationElement.Kind.Text("", box), _activeStyle));
        Document.Select(id);
        EditingTextID = id;
        _releasesTextWhenDone = true;
    }

    private void PlaceStepMarker(Point point)
    {
        double radius = Math.Max(_activeStyle.FontSize, 14.0);
        Document.Add(new AnnotationElement(null, new AnnotationElement.Kind.StepMarker(0, point, radius), _activeStyle));
        Document.Select(null);
    }

    public void SyncStyleToSelection()
    {
        if (!Document.SelectedID.HasValue) return;
        var el = Document.Element(Document.SelectedID.Value);
        if (el == null) return;

        var s = el.Style;
        if (el.ElementKind is AnnotationElement.Kind.StepMarker sm)
        {
            s = new Style(s.Color, s.StrokeWidth, sm.Radius, s.Fill);
        }
        _activeStyle = s;

        if (el.ElementKind is AnnotationElement.Kind.Arrow a)
        {
            _arrowStyle = a.Style;
        }
        else if (el.ElementKind is AnnotationElement.Kind.Redaction r)
        {
            _redactionStyle = r.Style;
            _redactionStrength = r.Strength;
        }

        OnPropertyChanged(nameof(ActiveStyle));
        OnPropertyChanged(nameof(ActiveColor));
        OnPropertyChanged(nameof(StrokeWidth));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(ArrowStyle));
        OnPropertyChanged(nameof(RedactionStyle));
        OnPropertyChanged(nameof(RedactionStrength));
        OnPropertyChanged(nameof(VisibleStyleFields));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedID));
    }

    // MARK: - Actions

    public void Undo()
    {
        EndTextEditing();
        Document.Undo();
        SyncCropDraft();
        NotifyCanvasChanged();
    }

    public void Redo()
    {
        EndTextEditing();
        Document.Redo();
        SyncCropDraft();
        NotifyCanvasChanged();
    }

    public void DeleteSelection()
    {
        if (!Document.SelectedID.HasValue) return;
        EndTextEditing();
        Document.Delete(Document.SelectedID.Value);
        NotifyCanvasChanged();
    }

    public void BringForward()
    {
        if (!Document.SelectedID.HasValue) return;
        int idx = Document.Elements.ToList().FindIndex(e => e.Id == Document.SelectedID.Value);
        if (idx >= 0)
        {
            Document.Reorder(Document.SelectedID.Value, idx + 1);
            NotifyCanvasChanged();
        }
    }

    public void SendBackward()
    {
        if (!Document.SelectedID.HasValue) return;
        int idx = Document.Elements.ToList().FindIndex(e => e.Id == Document.SelectedID.Value);
        if (idx >= 0)
        {
            Document.Reorder(Document.SelectedID.Value, idx - 1);
            NotifyCanvasChanged();
        }
    }

    public void CopyAndClose()
    {
        EndTextEditing();
        var rendered = _renderer.Render(Document);
        _imageSink.CopyToClipboard(rendered);
        RequestClose?.Invoke();
    }

    public void Copy()
    {
        EndTextEditing();
        var rendered = _renderer.Render(Document);
        _imageSink.CopyToClipboard(rendered);
    }

    public void SaveAs(string destinationPath, ImageFormat? format = null)
    {
        EndTextEditing();
        var rendered = _renderer.Render(Document);
        _imageSink.Write(rendered, destinationPath, format ?? new ImageFormat.Png());
    }

    public void OpenExistingImage(string? path = null)
    {
        if (_imageSource == null) return;
        EndTextEditing();

        var result = path != null ? _imageSource.LoadImage(path) : _imageSource.OpenDocument();
        if (result.Error != null)
        {
            if (result.Error is ImageLoadError.UserCancelled)
            {
                return;
            }

            string msg = result.Error switch
            {
                ImageLoadError.Unreadable => "The file exists but could not be read.",
                ImageLoadError.UnsupportedFormat => "The file is not a supported image format.",
                _ => "Could not load image."
            };

            ShowError?.Invoke(("Image Load Error", msg, null));
        }
    }

    private ArrowStyle LoadLastArrowStyle()
    {
        var raw = _settingsStore?.GetSetting(ArrowStyleDefaultsKey);
        if (raw != null && int.TryParse(raw, out int idx) && Enum.IsDefined(typeof(ArrowStyle), idx))
        {
            return (ArrowStyle)idx;
        }
        return ArrowStyle.Standard;
    }

    private void SaveLastArrowStyle(ArrowStyle style)
    {
        _settingsStore?.SetSetting(ArrowStyleDefaultsKey, ((int)style).ToString());
    }

    private void NotifyCanvasChanged()
    {
        OnPropertyChanged(nameof(DisplayElements));
        OnPropertyChanged(nameof(DraftElement));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedID));
        CanvasInvalidated?.Invoke();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // MARK: - Internal Helper Records

    private sealed class Draft
    {
        public EditorTool Tool { get; }
        public Point Start { get; }
        public Point Current { get; set; }
        public List<Point> Points { get; }
        public ArrowStyle ArrowStyle { get; }
        public RedactionStyle RedactionStyle { get; }
        public double RedactionStrength { get; }
        public ulong RedactionSeed { get; }

        public Draft(
            EditorTool tool,
            Point start,
            Point current,
            List<Point> points,
            ArrowStyle arrowStyle,
            RedactionStyle redactionStyle,
            double redactionStrength,
            ulong redactionSeed)
        {
            Tool = tool;
            Start = start;
            Current = current;
            Points = points;
            ArrowStyle = arrowStyle;
            RedactionStyle = redactionStyle;
            RedactionStrength = redactionStrength;
            RedactionSeed = redactionSeed;
        }

        public AnnotationElement.Kind? Kind
        {
            get
            {
                switch (Tool)
                {
                    case EditorTool.Arrow:
                        if (Start.Distance(Current) < 3.0) return null;
                        Point? bend = ArrowStyle.IsBendable() ? ArrowGeometry.DefaultArrowBend(Start, Current) : null;
                        return new AnnotationElement.Kind.Arrow(Start, Current, bend, ArrowStyle);
                    case EditorTool.Line:
                        if (Start.Distance(Current) < 3.0) return null;
                        return new AnnotationElement.Kind.Line(Start, Current);
                    case EditorTool.Rectangle:
                        if (Math.Abs(Current.X - Start.X) < 3.0 || Math.Abs(Current.Y - Start.Y) < 3.0) return null;
                        return new AnnotationElement.Kind.Rectangle(GeometryUtils.RectBetween(Start, Current));
                    case EditorTool.Ellipse:
                        if (Math.Abs(Current.X - Start.X) < 3.0 || Math.Abs(Current.Y - Start.Y) < 3.0) return null;
                        return new AnnotationElement.Kind.Ellipse(GeometryUtils.RectBetween(Start, Current));
                    case EditorTool.Freehand:
                        if (Points.Count <= 1) return null;
                        return new AnnotationElement.Kind.Freehand(Points);
                    case EditorTool.Highlight:
                        if (Math.Abs(Current.X - Start.X) < 3.0 || Math.Abs(Current.Y - Start.Y) < 3.0) return null;
                        return new AnnotationElement.Kind.Highlight(GeometryUtils.RectBetween(Start, Current));
                    case EditorTool.Focus:
                        if (Math.Abs(Current.X - Start.X) < 3.0 || Math.Abs(Current.Y - Start.Y) < 3.0) return null;
                        return new AnnotationElement.Kind.Focus(GeometryUtils.RectBetween(Start, Current));
                    case EditorTool.Redact:
                        if (Math.Abs(Current.X - Start.X) < 3.0 || Math.Abs(Current.Y - Start.Y) < 3.0) return null;
                        return new AnnotationElement.Kind.Redaction(GeometryUtils.RectBetween(Start, Current), RedactionStyle, RedactionStrength, RedactionSeed);
                    default:
                        return null;
                }
            }
        }
    }

    private sealed class DragSession
    {
        public abstract record Mode
        {
            public sealed record MoveMode : Mode;
            public sealed record ResizeMode(Handle Handle) : Mode;
            public sealed record ReshapeMode(EndpointHandle Handle) : Mode;
        }

        public ElementID Id { get; }
        public Mode CurrentMode { get; }
        public Point Start { get; }
        public Point Current { get; set; }

        public DragSession(ElementID id, Mode mode, Point start, Point current)
        {
            Id = id;
            CurrentMode = mode;
            Start = start;
            Current = current;
        }

        public Transform CurrentTransform
        {
            get
            {
                double dx = Current.X - Start.X;
                double dy = Current.Y - Start.Y;
                return CurrentMode switch
                {
                    Mode.MoveMode => new Transform.Move(dx, dy),
                    Mode.ResizeMode r => new Transform.Resize(r.Handle, dx, dy),
                    Mode.ReshapeMode resh => new Transform.Reshape(resh.Handle, dx, dy),
                    _ => new Transform.Move(0, 0)
                };
            }
        }
    }

    private sealed class CropSession
    {
        public abstract record Mode
        {
            public sealed record Draw : Mode;
            public sealed record Move : Mode;
            public sealed record Resize(Handle Handle) : Mode;
        }

        public Mode CurrentMode { get; }
        public Point Start { get; }
        public Rect Origin { get; }

        public CropSession(Mode mode, Point start, Rect origin)
        {
            CurrentMode = mode;
            Start = start;
            Origin = origin;
        }

        public Rect GetRect(Point point, Rect bounds)
        {
            switch (CurrentMode)
            {
                case Mode.Draw:
                    var between = GeometryUtils.RectBetween(Start, point);
                    return bounds.Intersection(between) ?? new Rect(point.X, point.Y, 0, 0);
                case Mode.Move:
                    double dx = point.X - Start.X;
                    double dy = point.Y - Start.Y;
                    double x = Math.Min(Math.Max(Origin.MinX + dx, bounds.MinX), bounds.MaxX - Origin.Width);
                    double y = Math.Min(Math.Max(Origin.MinY + dy, bounds.MinY), bounds.MaxY - Origin.Height);
                    return new Rect(x, y, Origin.Width, Origin.Height);
                case Mode.Resize r:
                    var resized = Origin.Resized(r.Handle, point.X - Start.X, point.Y - Start.Y);
                    return bounds.Intersection(resized) ?? Origin;
                default:
                    return Origin;
            }
        }
    }
}
