// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

public readonly record struct CanvasFrame(int Width, int Height, Rect ImageRect, double Scale);

public static class CanvasLayout
{
    public static CanvasFrame Compute(int imageWidth, int imageHeight, CanvasStyle style)
    {
        if (!style.Enabled)
        {
            return new CanvasFrame(imageWidth, imageHeight, new Rect(0, 0, imageWidth, imageHeight), 1.0);
        }

        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return new CanvasFrame(0, 0, Rect.Zero, 1.0);
        }

        double padding = Math.Max(0.0, style.Padding);

        int frameWidth;
        int frameHeight;
        double scale;

        if (style.SizeMode == CanvasSizeMode.FixedSize)
        {
            int targetWidth = Math.Max(1, style.TargetWidth);
            int targetHeight = Math.Max(1, style.TargetHeight);

            bool fits = (imageWidth + 2 * padding <= targetWidth) &&
                        (imageHeight + 2 * padding <= targetHeight);

            if (fits)
            {
                frameWidth = targetWidth;
                frameHeight = targetHeight;
                scale = 1.0;
            }
            else if (style.DownscaleToFit)
            {
                frameWidth = targetWidth;
                frameHeight = targetHeight;
                double availW = Math.Max(0.0, targetWidth - 2 * padding);
                double availH = Math.Max(0.0, targetHeight - 2 * padding);
                scale = Math.Min(availW / imageWidth, availH / imageHeight);
            }
            else
            {
                // Larger and not downscaled: treat as Aspect with ratio TargetWidth / TargetHeight
                double ratio = targetWidth / (double)targetHeight;
                double wMin = imageWidth + 2 * padding;
                double hMin = imageHeight + 2 * padding;
                double minRatio = wMin / hMin;

                if (ratio > minRatio)
                {
                    frameWidth = (int)Math.Round(hMin * ratio, MidpointRounding.AwayFromZero);
                    frameHeight = (int)Math.Round(hMin, MidpointRounding.AwayFromZero);
                }
                else
                {
                    frameWidth = (int)Math.Round(wMin, MidpointRounding.AwayFromZero);
                    frameHeight = (int)Math.Round(wMin / ratio, MidpointRounding.AwayFromZero);
                }
                scale = 1.0;
            }
        }
        else // CanvasSizeMode.Aspect
        {
            double wMin = imageWidth + 2 * padding;
            double hMin = imageHeight + 2 * padding;
            scale = 1.0;

            double? ratio = AspectPresets.Ratio(style.Aspect);
            if (ratio == null) // Auto gives Wmin x Hmin
            {
                frameWidth = (int)Math.Round(wMin, MidpointRounding.AwayFromZero);
                frameHeight = (int)Math.Round(hMin, MidpointRounding.AwayFromZero);
            }
            else
            {
                double targetRatio = ratio.Value;
                double minRatio = wMin / hMin;

                if (targetRatio > minRatio)
                {
                    frameWidth = (int)Math.Round(hMin * targetRatio, MidpointRounding.AwayFromZero);
                    frameHeight = (int)Math.Round(hMin, MidpointRounding.AwayFromZero);
                }
                else
                {
                    frameWidth = (int)Math.Round(wMin, MidpointRounding.AwayFromZero);
                    frameHeight = (int)Math.Round(wMin / targetRatio, MidpointRounding.AwayFromZero);
                }
            }
        }

        double imgW = imageWidth * scale;
        double imgH = imageHeight * scale;
        double x = (frameWidth - imgW) / 2.0;
        double y = (frameHeight - imgH) / 2.0;

        return new CanvasFrame(frameWidth, frameHeight, new Rect(x, y, imgW, imgH), scale);
    }
}
