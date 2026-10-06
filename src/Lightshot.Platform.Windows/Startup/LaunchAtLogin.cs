// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Microsoft.Win32;

namespace Lightshot.Platform.Windows.Startup;

public enum StartupStatus
{
    Disabled,
    Enabled,
    DisabledByTaskManager
}

/// <summary>
/// Manages launch-at-login registration via the Windows Registry (HKCU Run key).
/// Reads StartupApproved\Run to detect if startup was disabled by Task Manager.
/// </summary>
public static class LaunchAtLogin
{
    public const string DefaultValueName = "Lightshot";
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string StartupApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>
    /// Builds the expected command line: quoted executable path plus --background.
    /// </summary>
    public static string BuildCommandLine(string? exePath = null)
    {
        exePath ??= Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Lightshot.exe");
        return $"\"{exePath}\" --background";
    }

    /// <summary>
    /// Queries the full startup status of the given value name.
    /// </summary>
    public static StartupStatus GetStatus(string valueName = DefaultValueName)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        if (runKey == null) return StartupStatus.Disabled;

        object? val = runKey.GetValue(valueName);
        if (val == null) return StartupStatus.Disabled;

        // Check StartupApproved\Run
        using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, false);
        if (approvedKey != null)
        {
            object? approvedVal = approvedKey.GetValue(valueName);
            if (approvedVal is byte[] bytes && bytes.Length > 0)
            {
                // Bit 0 set (odd byte, e.g. 0x01 or 0x03) indicates disabled in Task Manager / Windows Settings
                if ((bytes[0] & 1) != 0)
                {
                    return StartupStatus.DisabledByTaskManager;
                }
            }
        }

        return StartupStatus.Enabled;
    }

    /// <summary>
    /// Returns true if registered and not disabled by Task Manager.
    /// </summary>
    public static bool IsEnabled(string valueName = DefaultValueName)
    {
        return GetStatus(valueName) == StartupStatus.Enabled;
    }

    /// <summary>
    /// Returns true if the entry exists in Run but was disabled by Task Manager.
    /// </summary>
    public static bool IsDisabledByTaskManager(string valueName = DefaultValueName)
    {
        return GetStatus(valueName) == StartupStatus.DisabledByTaskManager;
    }

    /// <summary>
    /// Reads the raw registered command line from the Run key, or null if absent.
    /// </summary>
    public static string? GetRegisteredCommandLine(string valueName = DefaultValueName)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return runKey?.GetValue(valueName) as string;
    }

    /// <summary>
    /// Enables or disables launch-at-login for the given value name.
    /// </summary>
    public static void SetEnabled(bool enabled, string? exePath = null, string valueName = DefaultValueName)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (enabled)
        {
            string cmdLine = BuildCommandLine(exePath);
            runKey.SetValue(valueName, cmdLine, RegistryValueKind.String);

            // If StartupApproved\Run exists and marked as disabled, re-enable it
            using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, true);
            if (approvedKey != null)
            {
                object? existing = approvedKey.GetValue(valueName);
                if (existing is byte[] bytes && bytes.Length > 0 && (bytes[0] & 1) != 0)
                {
                    bytes[0] = 0x02; // Enabled
                    approvedKey.SetValue(valueName, bytes, RegistryValueKind.Binary);
                }
            }
        }
        else
        {
            runKey.DeleteValue(valueName, false);
        }
    }
}

/// <summary>
/// Alias for internal helper access.
/// </summary>
internal static class LaunchAtLoginHelper
{
    public static bool IsEnabled() => LaunchAtLogin.IsEnabled();
    public static void SetEnabled(bool enabled) => LaunchAtLogin.SetEnabled(enabled);
}
