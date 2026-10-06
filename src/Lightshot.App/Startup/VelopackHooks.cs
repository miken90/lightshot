using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Velopack;
using Lightshot.Platform.Windows.Startup;
using Lightshot.Platform.Windows.Windows;

namespace Lightshot.App.Startup;

internal static class VelopackHooks
{
    public static void Run() =>
        VelopackApp.Build()
            // Consent: an update applies only on quit or "Restart to Update", never silently at the next start.
            .SetAutoApplyOnStartup(false)
            .OnAfterUpdateFastCallback(_ => RefreshRunKey())
            // The prompt is passed as a delegate so it runs AFTER the Run value is removed (see CleanupOnUninstall).
            .OnBeforeUninstallFastCallback(_ => CleanupOnUninstall(AskRemoveUserData, LaunchAtLogin.DefaultValueName,
                new[] { AppPaths.RoamingData, AppPaths.LocalData }, ScratchRecordingsDirectory))
            .Run();

    // Unfinished takes live here (AppController.cs:129). Keep them on uninstall when the folder holds any file.
    internal static readonly string ScratchRecordingsDirectory = Path.Combine(AppPaths.LocalData, "Lightshot Recordings");

    // The Run value points at the running exe; rewrite it after an update so it names the new one.
    internal static void RefreshRunKey(string valueName = LaunchAtLogin.DefaultValueName)
    {
        if (LaunchAtLogin.IsEnabled(valueName)) LaunchAtLogin.SetEnabled(true, null, valueName);
    }

    // Order matters: Velopack kills this hook after 30 s, so the Run value goes first,
    // before a prompt the user may never answer.
    internal static void CleanupOnUninstall(Func<bool> askRemoveUserData, string valueName,
        IReadOnlyList<string> dataDirectories, string? keepIfNotEmpty = null)
    {
        try { LaunchAtLogin.SetEnabled(false, null, valueName); } catch (Exception ex) { Trace.TraceError($"Run value cleanup failed: {ex}"); }
        if (!askRemoveUserData()) return;
        bool keep = keepIfNotEmpty != null && Directory.Exists(keepIfNotEmpty)
            && Directory.EnumerateFiles(keepIfNotEmpty, "*", SearchOption.AllDirectories).Any();
        foreach (var dir in dataDirectories)
        {
            try { DeleteDataDirectory(dir, keep ? keepIfNotEmpty : null); }
            catch (Exception ex) { Trace.TraceError($"Data cleanup failed for {dir}: {ex}"); }
        }
    }

    // Deletes dir recursively; when keep is a direct child of dir, deletes every other entry and leaves keep alone.
    private static void DeleteDataDirectory(string dir, string? keep)
    {
        if (!Directory.Exists(dir)) return;
        string full = Path.GetFullPath(dir).TrimEnd('\\');
        if (keep == null || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(keep).TrimEnd('\\')), full, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(dir, recursive: true);
            return;
        }
        foreach (var file in Directory.EnumerateFiles(dir)) File.Delete(file);
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            if (!string.Equals(Path.GetFullPath(sub).TrimEnd('\\'), Path.GetFullPath(keep).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(sub, recursive: true);
        }
    }

    // Velopack kills the hook after 30 s; an unanswered prompt therefore keeps the data.
    private static bool AskRemoveUserData()
    {
        if (AppController.IsTestMode()) return false;
        return MessageBox.Show(
            "Also remove your Lightshot settings and capture history?\n\nYour saved screenshots and recordings are not affected. Unfinished recordings are kept.",
            "Uninstall Lightshot", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No, MessageBoxOptions.DefaultDesktopOnly) == MessageBoxResult.Yes;
    }
}
