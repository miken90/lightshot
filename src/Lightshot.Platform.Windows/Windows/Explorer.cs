// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;

namespace Lightshot.Platform.Windows.Windows;

/// <summary>
/// Helper for revealing files in Windows Explorer (/select,<path>) and opening Windows Settings URIs.
/// </summary>
public static class Explorer
{
    public const string EaseOfAccessKeyboardUri = "ms-settings:easeofaccess-keyboard";
    public const string StartupAppsUri = "ms-settings:startupapps";
    public const string PrivacyMicrophoneUri = "ms-settings:privacy-microphone";
    public const string PrivacyWebcamUri = "ms-settings:privacy-webcam";

    /// <summary>
    /// Builds the argument string for explorer.exe to select and highlight a file.
    /// Format: /select,"<path>"
    /// </summary>
    public static string BuildRevealArguments(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        }
        return $"/select,\"{filePath}\"";
    }

    /// <summary>
    /// Builds the full ProcessStartInfo for revealing a file in Explorer without starting it.
    /// </summary>
    public static ProcessStartInfo BuildRevealStartInfo(string filePath)
    {
        return new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = BuildRevealArguments(filePath),
            UseShellExecute = false
        };
    }

    /// <summary>
    /// Builds the ProcessStartInfo for opening an ms-settings URI via shell execute.
    /// </summary>
    public static ProcessStartInfo BuildSettingsUriStartInfo(string uriString)
    {
        if (string.IsNullOrWhiteSpace(uriString))
        {
            throw new ArgumentException("URI string cannot be null or empty.", nameof(uriString));
        }
        return new ProcessStartInfo
        {
            FileName = uriString,
            UseShellExecute = true
        };
    }

    /// <summary>
    /// Reveals a file in Windows Explorer, highlighting it in its parent directory.
    /// </summary>
    public static void Reveal(string filePath)
    {
        var psi = BuildRevealStartInfo(filePath);
        Process.Start(psi);
    }

    /// <summary>
    /// Launches a Windows Settings page by URI (e.g. ms-settings:startupapps).
    /// </summary>
    public static void OpenSettingsUri(string uriString)
    {
        var psi = BuildSettingsUriStartInfo(uriString);
        Process.Start(psi);
    }
}
