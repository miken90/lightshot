// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Lightshot.Platform.Windows.Windows;

/// <summary>
/// Optional per-process data root. Automated tests set LIGHTSHOT_DATA_ROOT so the app they launch
/// keeps its settings, history and scratch under a throwaway folder and runs beside the user's own
/// instance: the single-instance mutex and named events are scoped to that root, so a test can
/// never reach, quit or replace the instance the user is running.
/// </summary>
public static class AppInstance
{
    public const string DataRootVariable = "LIGHTSHOT_DATA_ROOT";

    /// <summary>
    /// The redirected data root, or null for a normal run against the user's profile folders.
    /// </summary>
    public static string? DataRoot => Normalize(Environment.GetEnvironmentVariable(DataRootVariable));

    /// <summary>
    /// The kernel object name for <paramref name="baseName"/> in the current process's scope.
    /// </summary>
    public static string ScopedName(string baseName) => ScopedName(baseName, DataRoot);

    /// <summary>
    /// The kernel object name an app started with <paramref name="dataRoot"/> uses for <paramref name="baseName"/>.
    /// </summary>
    public static string ScopedName(string baseName, string? dataRoot)
    {
        string? root = Normalize(dataRoot);
        if (root == null) return baseName;

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant()));
        return baseName + "." + Convert.ToHexString(hash, 0, 8);
    }

    private static string? Normalize(string? root) =>
        string.IsNullOrWhiteSpace(root) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
}
