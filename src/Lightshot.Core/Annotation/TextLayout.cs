// Ported from LightshotKit/Sources/LightshotKit/TextLayout.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;

namespace Lightshot.Core;

/// <summary>
/// Pure layout metrics for text annotations in the domain core.
/// </summary>
public static class TextLayout
{
    public const string FontName = "Helvetica";

    public static double Padding(double fontSize) => Math.Max(4.0, Math.Round(fontSize * 0.3));

    public static double MinimumWidth(double fontSize) =>
        fontSize * 1.5 + 2.0 * Padding(fontSize);

    public static Rect Box(string text, double fontSize, Point origin, double? width = null)
    {
        double pad = Padding(fontSize);
        double boxWidth = Math.Max(width ?? NaturalWidth(text, fontSize), MinimumWidth(fontSize));
        Size content = ContentSize(text, fontSize, boxWidth - 2.0 * pad);
        return new Rect(origin.X, origin.Y, boxWidth, content.Height + 2.0 * pad);
    }

    public static bool HasNaturalWidth(Rect box, string text, double fontSize)
    {
        double natural = Math.Max(NaturalWidth(text, fontSize), MinimumWidth(fontSize));
        return Math.Abs(box.Standardized.Width - natural) < 0.5;
    }

    public static double NaturalWidth(string text, double fontSize)
    {
        Size size = ContentSize(text, fontSize, null);
        return Math.Ceiling(size.Width) + 2.0 * Padding(fontSize);
    }

    public static Size ContentSize(string text, double fontSize, double? wrapWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new Size(0, Math.Ceiling(fontSize * 1.2));
        }

        double charWidth = fontSize * 0.55;
        double lineHeight = Math.Ceiling(fontSize * 1.2);

        string[] paragraphs = text.Split('\n');
        double maxLineWidth = 0;
        int totalLines = 0;

        foreach (string paragraph in paragraphs)
        {
            if (string.IsNullOrEmpty(paragraph))
            {
                totalLines++;
                continue;
            }

            if (wrapWidth is null || wrapWidth.Value <= 0)
            {
                double lineWidth = paragraph.Length * charWidth;
                maxLineWidth = Math.Max(maxLineWidth, lineWidth);
                totalLines++;
                continue;
            }

            string[] words = paragraph.Split(' ');
            double currentLineWidth = 0;
            int paragraphLines = 1;

            foreach (string word in words)
            {
                double wordWidth = word.Length * charWidth;
                double spaceWidth = charWidth * 0.5;

                if (currentLineWidth == 0)
                {
                    currentLineWidth = wordWidth;
                }
                else if (currentLineWidth + spaceWidth + wordWidth <= wrapWidth.Value)
                {
                    currentLineWidth += spaceWidth + wordWidth;
                }
                else
                {
                    paragraphLines++;
                    currentLineWidth = wordWidth;
                }
                maxLineWidth = Math.Max(maxLineWidth, currentLineWidth);
            }
            totalLines += paragraphLines;
        }

        return new Size(maxLineWidth, totalLines * lineHeight);
    }
}
