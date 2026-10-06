// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Platform.Windows.Files;

namespace Lightshot.Platform.Windows.Windows;

/// <summary>
/// Resolves standard Lightshot directories under %AppData% and %LocalAppData%.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// %AppData%\Lightshot (Roaming settings).
    /// </summary>
    public static string RoamingData => Path.Combine(KnownFolders.AppData, "Lightshot");

    /// <summary>
    /// %AppData%\Lightshot\settings.json.
    /// </summary>
    public static string SettingsFile => Path.Combine(RoamingData, "settings.json");

    /// <summary>
    /// %LocalAppData%\Lightshot (Local state, history, caches).
    /// </summary>
    public static string LocalData => Path.Combine(KnownFolders.LocalApplicationData, "Lightshot");

    /// <summary>
    /// %LocalAppData%\Lightshot\History.
    /// </summary>
    public static string History => Path.Combine(LocalData, "History");

    /// <summary>
    /// %LocalAppData%\Lightshot\Temp (e.g. Quick Access drag-out temp files).
    /// </summary>
    public static string Temp => Path.Combine(LocalData, "Temp");

    /// <summary>
    /// %LocalAppData%\Lightshot\Logs.
    /// </summary>
    public static string Logs => Path.Combine(LocalData, "Logs");

    /// <summary>
    /// %LocalAppData%\Lightshot\Updates.
    /// </summary>
    public static string Updates => Path.Combine(LocalData, "Updates");

    /// <summary>
    /// Creates all standard directories if they do not yet exist.
    /// </summary>
    public static void EnsureDirectoriesCreated()
    {
        Directory.CreateDirectory(RoamingData);
        Directory.CreateDirectory(LocalData);
        Directory.CreateDirectory(History);
        Directory.CreateDirectory(Temp);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Updates);
    }
}
