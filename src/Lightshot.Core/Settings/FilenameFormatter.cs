// Ported from LightshotKit/Sources/LightshotKit/SettingsStore.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Text;

namespace Lightshot.Core;

/// <summary>
/// Expands a filename pattern into a concrete name for a save.
/// Pure and deterministic given a date, so it is unit-tested without touching disk.
/// </summary>
public record FilenameFormatter
{
    private static readonly char[] WindowsForbiddenChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    public string Pattern { get; init; }

    public FilenameFormatter(string pattern)
    {
        Pattern = pattern;
    }

    /// <summary>
    /// A user-typed file name made safe for the save folder.
    /// Replaces Windows-forbidden characters and / : with -, strips control characters,
    /// trims whitespace and dots, and avoids reserved DOS device names.
    /// </summary>
    public static string? Sanitized(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (c < 0x20)
            {
                continue; // strip control characters
            }

            if (Array.IndexOf(WindowsForbiddenChars, c) >= 0)
            {
                sb.Append('-');
            }
            else
            {
                sb.Append(c);
            }
        }

        string cleaned = sb.ToString().Trim().Trim('.');
        if (string.IsNullOrEmpty(cleaned))
        {
            return null;
        }

        // Check reserved device names
        string upper = cleaned.ToUpperInvariant();
        foreach (string reserved in ReservedDeviceNames)
        {
            if (upper == reserved || upper.StartsWith(reserved + ".", StringComparison.Ordinal))
            {
                cleaned = "_" + cleaned;
                break;
            }
        }

        return cleaned;
    }

    /// <summary>
    /// The expanded filename, without an extension.
    /// </summary>
    public string Filename(DateTime date)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Pattern.Length; i++)
        {
            char ch = Pattern[i];
            if (ch == '%' && i + 1 < Pattern.Length)
            {
                char token = Pattern[++i];
                switch (token)
                {
                    case 'Y':
                        sb.Append(date.Year.ToString("D4"));
                        break;
                    case 'm':
                        sb.Append(date.Month.ToString("D2"));
                        break;
                    case 'd':
                        sb.Append(date.Day.ToString("D2"));
                        break;
                    case 'H':
                        sb.Append(date.Hour.ToString("D2"));
                        break;
                    case 'M':
                        sb.Append(date.Minute.ToString("D2"));
                        break;
                    case 'S':
                        sb.Append(date.Second.ToString("D2"));
                        break;
                    default:
                        sb.Append('%');
                        sb.Append(token);
                        break;
                }
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// The full destination path: filename(at) under directory, with format's extension.
    /// </summary>
    public string DestinationPath(string directory, ImageFormat format, DateTime date)
    {
        string name = Filename(date) + "." + format.FileExtension;
        return Path.Combine(directory, name).Replace('\\', '/');
    }
}
