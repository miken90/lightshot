// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Platform.Windows.Windows;

namespace Lightshot.Platform.Windows.Files;

/// <summary>
/// Resolves standard Windows system and user directory locations.
/// </summary>
public static class KnownFolders
{
    public static string Desktop =>
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    public static string Pictures =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    public static string AppData => AppInstance.DataRoot is { } root
        ? Path.Combine(root, "Roaming")
        : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static string LocalApplicationData => AppInstance.DataRoot is { } root
        ? Path.Combine(root, "Local")
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string UserProfile =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string DefaultSaveLocation
    {
        get
        {
            string desktop = Desktop;
            if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop))
            {
                return desktop;
            }

            string user = UserProfile;
            return !string.IsNullOrEmpty(user) ? user : AppContext.BaseDirectory;
        }
    }
}
