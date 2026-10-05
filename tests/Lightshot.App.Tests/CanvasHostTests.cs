// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using Lightshot.App.Views.Editor;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.App.Tests;

public class CanvasHostTests
{
    private static void RunInSta(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }

        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        captured?.Throw();
    }

    private static CapturedImage CreateTestImage(int width = 200, int height = 120)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);

            using var redPaint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, 0, width / 2f, height, redPaint);

            using var bluePaint = new SKPaint { Color = SKColors.Blue, Style = SKPaintStyle.Fill };
            canvas.DrawRect(width / 2f, 0, width / 2f, height, bluePaint);
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

    [Fact]
    [Render]
    public void BackingBitmapEqualsExport()
    {
        RunInSta(() =>
        {
            // 1. Setup document with base image and diverse annotations (rectangle, redaction)
            var baseImg = CreateTestImage(200, 120);
            var doc = new AnnotationDocument(baseImg);

            // Add rectangle annotation
            doc.Add(new AnnotationElement(
                kind: new AnnotationElement.Kind.Rectangle(new Rect(20, 20, 50, 40)),
                style: new Style(color: new RGBAColor(0, 1, 0), strokeWidth: 4)));

            // Add redaction annotation (blur)
            doc.Add(new AnnotationElement(
                kind: new AnnotationElement.Kind.Redaction(new Rect(80, 30, 60, 50), RedactionStyle.Blur, 0.6, 42)));

            // 2. Instantiate CanvasHost and set Document at 100% zoom
            using var host = new CanvasHost
            {
                Zoom = 1.0,
                Document = doc
            };

            // CanvasHost renders to backing bitmap
            host.RenderCanvas();

            Assert.NotNull(host.BackingBitmap);
            var backing = host.BackingBitmap;

            // 3. Render export bytes via DocumentRenderer
            var renderer = new DocumentRenderer();
            var rendered = renderer.Render(doc);

            Assert.Equal(backing.Width, rendered.PixelWidth);
            Assert.Equal(backing.Height, rendered.PixelHeight);

            // 4. Decode export bytes to SKBitmap
            using var ms = new MemoryStream(rendered.Data);
            using var exportBmp = SKBitmap.Decode(ms);
            Assert.NotNull(exportBmp);

            Assert.Equal(backing.Width, exportBmp.Width);
            Assert.Equal(backing.Height, exportBmp.Height);

            // 5. Compare backing SKBitmap with decoded export bytes pixel-for-pixel
            int width = backing.Width;
            int height = backing.Height;

            for (int y = 0; y < height; y += 2)
            {
                for (int x = 0; x < width; x += 2)
                {
                    var c1 = backing.GetPixel(x, y);
                    var c2 = exportBmp.GetPixel(x, y);

                    int dr = Math.Abs(c1.Red - c2.Red);
                    int dg = Math.Abs(c1.Green - c2.Green);
                    int db = Math.Abs(c1.Blue - c2.Blue);
                    int da = Math.Abs(c1.Alpha - c2.Alpha);

                    // Tolerance <= 2 for any Skia PNG premul compression rounding
                    Assert.True(dr <= 2 && dg <= 2 && db <= 2 && da <= 2,
                        $"BackingBitmap and Export differ at ({x}, {y}): backing=({c1.Red},{c1.Green},{c1.Blue},{c1.Alpha}), export=({c2.Red},{c2.Green},{c2.Blue},{c2.Alpha})");
                }
            }
        });
    }
}
