// Ported from LightshotKit/Tests/LightshotKitTests/RecordingCoordinatorTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class BurnInCompositorTests
{
    [Fact]
    [Gpu]
    public void DrawsRingsPillsAndBubble()
    {
        // 1. Initialize WARP D3D11 device
        var hrWarp = D3D11.D3D11CreateDevice(
            null,
            DriverType.Warp,
            DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
            out ID3D11Device? device,
            out ID3D11DeviceContext? context
        );

        Assert.True(hrWarp.Success);
        Assert.NotNull(device);
        Assert.NotNull(context);

        using (device)
        using (context)
        {
            int width = 800;
            int height = 600;

            // 2. Create BGRA render target texture
            var texDesc = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None
            };

            using var renderTexture = device.CreateTexture2D(texDesc);

            // 3. Configure RecordingOptions with click rings and keystroke overlay enabled
            var defaults = new RecordingDefaults
            {
                HighlightClicks = true,
                ClickHighlight = new ClickHighlightSettings(
                    CursorHighlightStyle.Ring,
                    CursorHighlightSize.Medium,
                    CursorHighlightColor.Yellow,
                    animateClicks: true),
                ShowKeystrokes = true,
                KeystrokeOverlay = new KeystrokeOverlaySettings(
                    KeystrokeDisplayMode.AllKeys,
                    KeystrokeOverlayPosition.BottomCenter,
                    KeystrokeOverlaySize.Medium,
                    KeystrokeOverlayAppearance.Dark)
            };
            var options = RecordingOptions.Resolve(
                new CaptureRegion.RectRegion(new Rect(0, 0, width, height)),
                RecordingOutputKind.Video,
                defaults);

            using var compositor = new BurnInCompositor(options, Point.Zero, pixelsPerPoint: 1.0);

            // Simulate click at center (400, 300)
            compositor.RecordClick(new Point(400, 300), 0.1);

            // Simulate keystroke "Ctrl K" at t = 0.1s
            compositor.RecordKey(new KeyPress("K", KeyModifiers.Control), 0.1);

            // Composite overlays onto BGRA texture at t = 0.1s
            compositor.Composite(renderTexture, 0.1);

            // 4. Read back pixels via staging texture
            var stagingDesc = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                MiscFlags = ResourceOptionFlags.None
            };

            using var stagingTex = device.CreateTexture2D(stagingDesc);
            context.CopyResource(stagingTex, renderTexture);

            var mapped = context.Map(stagingTex, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                int rowPitch = (int)mapped.RowPitch;
                byte[] pixels = new byte[height * rowPitch];
                Marshal.Copy(mapped.DataPointer, pixels, 0, pixels.Length);

                // Check click highlight area: near (400, 300)
                // A ring or halo must have painted non-zero pixels
                bool clickPixelFound = false;
                for (int y = 280; y <= 320; y++)
                {
                    for (int x = 380; x <= 420; x++)
                    {
                        int idx = y * rowPitch + x * 4;
                        byte b = pixels[idx];
                        byte g = pixels[idx + 1];
                        byte r = pixels[idx + 2];
                        byte a = pixels[idx + 3];

                        if (a > 0 && (r > 0 || g > 0))
                        {
                            clickPixelFound = true;
                            break;
                        }
                    }
                    if (clickPixelFound) break;
                }
                Assert.True(clickPixelFound, "Click highlight circle should paint non-zero pixels around click point.");

                // Check keystroke pill area: bottom center of 800x600 screen (y ~ 500..580, x ~ 300..500)
                bool pillPixelFound = false;
                for (int y = 500; y < 580; y++)
                {
                    for (int x = 300; x < 500; x++)
                    {
                        int idx = y * rowPitch + x * 4;
                        byte a = pixels[idx + 3];
                        if (a > 0)
                        {
                            pillPixelFound = true;
                            break;
                        }
                    }
                    if (pillPixelFound) break;
                }
                Assert.True(pillPixelFound, "Keystroke pill backdrop and text should paint non-zero pixels in bottom center.");

                // Note: Camera bubble burn-in is deferred post-MVP (package R4). UNCOVERED: deferred.
            }
            finally
            {
                context.Unmap(stagingTex, 0);
            }
        }
    }
}
