// Ported from LightshotKit/Sources/LightshotKit/TextLayout.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;
using SkiaSharp;

namespace Lightshot.Rendering;

/// <summary>
/// Text measurement, greedy word wrapping, and rendering using Skia font metrics and bundled Inter font.
/// </summary>
public static class SkiaTextLayout
{
    public static double Padding(double fontSize) => Math.Max(4.0, Math.Round(fontSize * 0.3));

    public static double MinimumWidth(double fontSize) =>
        fontSize * 1.5 + 2.0 * Padding(fontSize);

    public static List<string> WrapText(string text, SKFont font, double wrapWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        string[] paragraphs = text.Split('\n');
        float spaceWidth = font.MeasureText(" ");

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrEmpty(paragraph))
            {
                lines.Add("");
                continue;
            }

            if (wrapWidth <= 0)
            {
                lines.Add(paragraph);
                continue;
            }

            string[] words = paragraph.Split(' ');
            string currentLine = "";
            float currentLineWidth = 0;

            foreach (var word in words)
            {
                float wordWidth = font.MeasureText(word);

                // Handle long word breaking if the word alone exceeds wrap width
                if (wordWidth > wrapWidth)
                {
                    if (!string.IsNullOrEmpty(currentLine))
                    {
                        lines.Add(currentLine);
                        currentLine = "";
                        currentLineWidth = 0;
                    }

                    // Break long word character by character
                    string broken = "";
                    float brokenWidth = 0;
                    foreach (char ch in word)
                    {
                        string chStr = ch.ToString();
                        float chWidth = font.MeasureText(chStr);
                        if (brokenWidth + chWidth > wrapWidth && broken.Length > 0)
                        {
                            lines.Add(broken);
                            broken = chStr;
                            brokenWidth = chWidth;
                        }
                        else
                        {
                            broken += chStr;
                            brokenWidth += chWidth;
                        }
                    }

                    if (broken.Length > 0)
                    {
                        currentLine = broken;
                        currentLineWidth = brokenWidth;
                    }
                    continue;
                }

                if (string.IsNullOrEmpty(currentLine))
                {
                    currentLine = word;
                    currentLineWidth = wordWidth;
                }
                else if (currentLineWidth + spaceWidth + wordWidth <= wrapWidth)
                {
                    currentLine += " " + word;
                    currentLineWidth += spaceWidth + wordWidth;
                }
                else
                {
                    lines.Add(currentLine);
                    currentLine = word;
                    currentLineWidth = wordWidth;
                }
            }

            if (!string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
            }
        }

        return lines;
    }

    public static void Draw(SKCanvas canvas, string text, double fontSize, SKColor color, SKRect rect)
    {
        if (string.IsNullOrEmpty(text) || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var font = FontProvider.GetFont(fontSize, bold: false);
        using var paint = FontProvider.CreateTextPaint(color);

        font.GetFontMetrics(out var metrics);
        float lineHeight = (float)Math.Ceiling(metrics.Descent - metrics.Ascent);
        var lines = WrapText(text, font, rect.Width);

        float y = rect.Top - metrics.Ascent;

        foreach (var line in lines)
        {
            if (y - metrics.Descent > rect.Bottom + lineHeight)
            {
                break;
            }

            canvas.DrawText(line, rect.Left, y, font, paint);
            y += lineHeight;
        }
    }
}
