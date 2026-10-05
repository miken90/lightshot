using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using AlphaMode = Vortice.DCommon.AlphaMode;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace CompositorProbe;

// NoBlur/NoShadow exist only to isolate which effect differs between adapters; the product path leaves both false.
public readonly record struct RenderState(bool Zoom, int StripCode, bool NoBlur = false, bool NoShadow = false)
{
    public static RenderState Default => new(true, -1);
}

/// <summary>
/// The one Render(state, t, target) used by the live preview and by the offscreen export. Everything it draws is a
/// pure function of (state, t, source frame): blurred backdrop, shadow, rounded-corner video, zoom, cursor sprite.
/// </summary>
public sealed class ProbeCompositor : IDisposable
{
    private readonly ID3D11Device _d3d;
    private readonly ID2D1Factory1 _factory;
    private readonly ID2D1Device _d2dDevice;
    private readonly ID2D1DeviceContext _ctx;
    private readonly GaussianBlur _blur;
    private readonly Shadow _shadow;
    private readonly ID2D1Bitmap1 _cursor;
    private readonly Dictionary<IntPtr, ID2D1Bitmap1> _bitmaps = new();
    private ID2D1SolidColorBrush? _dim, _white, _black, _ruler;

    public const float CursorSize = 56f;
    public const float CornerRadius = 36f;
    public const float VideoFill = 0.82f;

    public ProbeCompositor(ID3D11Device d3d)
    {
        _d3d = d3d;
        _factory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        using var dxgi = d3d.QueryInterface<IDXGIDevice>();
        _d2dDevice = _factory.CreateDevice(dxgi);
        _ctx = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        _ctx.AntialiasMode = AntialiasMode.PerPrimitive;
        _blur = new GaussianBlur(_ctx) { StandardDeviation = 26f, BorderMode = BorderMode.Hard, Optimization = GaussianBlurOptimization.Quality };
        _shadow = new Shadow(_ctx) { BlurStandardDeviation = 28f, Color = new Color4(0f, 0f, 0f, 0.65f) };
        _dim = _ctx.CreateSolidColorBrush(new Color4(0f, 0f, 0f, 0.35f));
        _white = _ctx.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));
        _black = _ctx.CreateSolidColorBrush(new Color4(0f, 0f, 0f, 1f));
        _ruler = _ctx.CreateSolidColorBrush(new Color4(0f, 1f, 1f, 1f));
        _cursor = BuildCursorSprite();
    }

    /// <summary>Cursor position in source pixels as a function of media time (stands in for the recorded input track).</summary>
    public static Vector2 CursorAt(double t, int srcW, int srcH) =>
        new((float)((0.62 + 0.24 * Math.Sin(t * 0.7)) * srcW), (float)((0.60 + 0.25 * Math.Cos(t * 1.1)) * srcH));

    public static float ZoomAt(double t) => (float)(1.0 + 0.5 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * t / 8.0)));

    /// <summary>Source pixel -> canvas pixel for this state and time (layout times zoom). Used to read stamps back.</summary>
    public static Matrix3x2 SourceToCanvas(in RenderState s, double t, int srcW, int srcH, int canvasW, int canvasH)
    {
        float scale = VideoFill * Math.Min(canvasW / (float)srcW, canvasH / (float)srcH);
        var layout = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation((canvasW - srcW * scale) / 2f, (canvasH - srcH * scale) / 2f);
        if (!s.Zoom) return layout;
        var focus = Vector2.Transform(CursorAt(t, srcW, srcH), layout);
        float z = ZoomAt(t);
        return layout * Matrix3x2.CreateTranslation(-focus) * Matrix3x2.CreateScale(z) * Matrix3x2.CreateTranslation(focus);
    }

    private ID2D1Bitmap1 BuildCursorSprite()
    {
        int sz = 128;
        var props = new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 96, 96, BitmapOptions.Target);
        var sprite = _ctx.CreateBitmap(new Vortice.Mathematics.SizeI(sz, sz), IntPtr.Zero, 0, props);
        var prev = _ctx.Target;
        _ctx.Target = sprite;
        _ctx.BeginDraw();
        _ctx.Clear(new Color4(0f, 0f, 0f, 0f));
        using var path = _factory.CreatePathGeometry();
        using (var sink = path.Open())
        {
            sink.BeginFigure(new Vector2(14, 8), FigureBegin.Filled);
            sink.AddLine(new Vector2(14, 104)); sink.AddLine(new Vector2(38, 82)); sink.AddLine(new Vector2(54, 120));
            sink.AddLine(new Vector2(72, 112)); sink.AddLine(new Vector2(56, 76)); sink.AddLine(new Vector2(90, 76));
            sink.EndFigure(FigureEnd.Closed);
            sink.Close();
        }
        _ctx.FillGeometry(path, _white!);
        _ctx.DrawGeometry(path, _black!, 6f);
        _ctx.EndDraw();
        _ctx.Target = prev;
        return sprite;
    }

    private ID2D1Bitmap1 BitmapFor(ID3D11Texture2D tex, bool isTarget)
    {
        if (_bitmaps.TryGetValue(tex.NativePointer, out var existing)) return existing;
        using var surface = tex.QueryInterface<IDXGISurface>();
        var props = new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, isTarget ? AlphaMode.Premultiplied : AlphaMode.Ignore),
            96, 96, isTarget ? BitmapOptions.Target | BitmapOptions.CannotDraw : BitmapOptions.None);
        var bmp = _ctx.CreateBitmapFromDxgiSurface(surface, props);
        _bitmaps[tex.NativePointer] = bmp;
        return bmp;
    }

    /// <summary>Drop every D2D bitmap that wraps a swapchain buffer; required before ResizeBuffers.</summary>
    public void ReleaseTargets()
    {
        _ctx.Target = null;
        foreach (var b in _bitmaps.Values) b.Dispose();
        _bitmaps.Clear();
    }

    public void Render(in RenderState state, double t, ID3D11Texture2D sourceBgra, ID3D11Texture2D target)
    {
        var sd = sourceBgra.Description;
        var td = target.Description;
        int srcW = (int)sd.Width, srcH = (int)sd.Height, W = (int)td.Width, H = (int)td.Height;
        var src = BitmapFor(sourceBgra, false);
        var dst = BitmapFor(target, true);

        var m = SourceToCanvas(state, t, srcW, srcH, W, H);
        float scale = Math.Abs(m.M11);
        float r = CornerRadius / scale;

        // A command list is recorded in its own BeginDraw/EndDraw pair, which cannot nest inside the main draw.
        using var cl = _ctx.CreateCommandList();
        _ctx.Target = cl;
        _ctx.BeginDraw();
        _ctx.Transform = Matrix3x2.Identity;
        _ctx.Clear(new Color4(0f, 0f, 0f, 0f));
        _ctx.FillRoundedRectangle(new RoundedRectangle(new System.Drawing.RectangleF(0, 0, srcW, srcH), r, r), _black!);
        _ctx.EndDraw();
        cl.Close();

        _ctx.Target = dst;
        _ctx.BeginDraw();
        _ctx.Transform = Matrix3x2.Identity;
        _ctx.Clear(new Color4(0.05f, 0.05f, 0.07f, 1f));

        // 1. Backdrop: the frame, scaled to cover, blurred, dimmed.
        float cover = Math.Max(W / (float)srcW, H / (float)srcH);
        _ctx.Transform = Matrix3x2.CreateScale(cover) * Matrix3x2.CreateTranslation((W - srcW * cover) / 2f, (H - srcH * cover) / 2f);
        _blur.SetInput(0, src, true);
        if (!state.NoBlur) _ctx.DrawImage(_blur, InterpolationMode.Linear, CompositeMode.SourceOver);
        _ctx.Transform = Matrix3x2.Identity;
        _ctx.FillRectangle(new Rect(0, 0, W, H), _dim!);

        // 2. Video layer in source space under layout * zoom.
        _ctx.Transform = m;

        // Shadow of the rounded video rectangle, offset downwards (command list recorded before the main draw).
        _shadow.SetInput(0, cl, true);
        if (!state.NoShadow) _ctx.DrawImage(_shadow, new Vector2(0, 30f / scale), InterpolationMode.Linear, CompositeMode.SourceOver);

        // Rounded-corner video: the frame as a bitmap brush clipped to the rounded rectangle.
        using (var brush = _ctx.CreateBitmapBrush(src, new BitmapBrushProperties1(ExtendMode.Clamp, ExtendMode.Clamp, InterpolationMode.Linear)))
        {
            _ctx.FillRoundedRectangle(new RoundedRectangle(new System.Drawing.RectangleF(0, 0, srcW, srcH), r, r), brush);
        }

        // 3. Cursor sprite at the tracked position, in source space (scales with zoom like Studio's cursor).
        var c = CursorAt(t, srcW, srcH);
        float cs = CursorSize / (VideoFill * Math.Min(W / (float)srcW, H / (float)srcH));
        _ctx.DrawBitmap(_cursor, new Vortice.RawRectF(c.X - 14f * cs / 128f, c.Y - 8f * cs / 128f, c.X - 14f * cs / 128f + cs, c.Y - 8f * cs / 128f + cs), 1f, BitmapInterpolationMode.Linear, null);

        // 4. Present-sequence strips (canvas space): tear detector, top and bottom rows.
        _ctx.Transform = Matrix3x2.Identity;
        if (state.StripCode >= 0)
        {
            for (int row = 0; row < 2; row++)
            {
                float y0 = row == 0 ? 0 : H - FrameStamp.StripH;
                _ctx.FillRectangle(new Rect(0, y0, FrameStamp.StripCells * FrameStamp.StripCellW, FrameStamp.StripH), _black!);
                _ctx.FillRectangle(new Rect(FrameStamp.StripBits * FrameStamp.StripCellW, y0, FrameStamp.StripCellW, FrameStamp.StripH), _white!);
                for (int b = 0; b < FrameStamp.StripBits; b++)
                    if (((state.StripCode >> b) & 1) != 0)
                        _ctx.FillRectangle(new Rect(b * FrameStamp.StripCellW, y0, FrameStamp.StripCellW, FrameStamp.StripH), _white!);
            }
        }
        if (state.StripCode >= 0)
            _ctx.FillRectangle(new Rect(0, FrameStamp.StripH, FrameStamp.StripRulerW, H - 2 * FrameStamp.StripH), _ruler!);
        _ctx.EndDraw();
    }

    public void Dispose()
    {
        ReleaseTargets();
        _cursor.Dispose(); _blur.Dispose(); _shadow.Dispose();
        _dim?.Dispose(); _white?.Dispose(); _black?.Dispose(); _ruler?.Dispose();
        _ctx.Dispose(); _d2dDevice.Dispose(); _factory.Dispose();
    }
}
