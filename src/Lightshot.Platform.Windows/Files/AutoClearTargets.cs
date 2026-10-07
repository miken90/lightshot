// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using Lightshot.Platform.Windows.Windows;

namespace Lightshot.Platform.Windows.Files;

public static class AutoClearTargets
{
    public static IReadOnlyList<SweepTarget> Default() =>
    [
        new(Path.GetTempPath(), "Lightshot_*.png", FoldersOnly: false),
        new(Path.Combine(Path.GetTempPath(), "Lightshot Drag"), "*", FoldersOnly: true),
        new(AppPaths.Temp, "*", FoldersOnly: false),
        new(AppPaths.Temp, "*", FoldersOnly: true),
        new(AppPaths.Logs, "*.log", FoldersOnly: false),
        new(Path.Combine(AppPaths.LocalData, "Lightshot Recordings"), "*.partial", FoldersOnly: false),
        new(Path.Combine(KnownFolders.LocalApplicationData, "Lightshot", "Recordings"), "*.partial", FoldersOnly: false),
    ];
}
