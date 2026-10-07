// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using Lightshot.Platform.Windows.Windows;

namespace Lightshot.App.UiTests;

/// <summary>
/// Launches Lightshot.App against a throwaway data root. The app then keeps its settings, history and
/// scratch there and scopes its single-instance mutex and named events to it, so a desktop test never
/// rewrites the user's %AppData%\Lightshot\settings.json, never signals the user's running instance,
/// and only ever kills the processes it started itself.
/// </summary>
internal sealed class IsolatedApp : IDisposable
{
    // Ids and start times, not Process objects: FlaUI's Application.Attach(Process) disposes the object it is
    // given, and a reused id must never lead to killing someone else's process.
    private readonly List<(int Id, DateTime StartTime)> _started = [];

    public IsolatedApp()
    {
        DataRoot = Path.Combine(Path.GetTempPath(), "LightshotUiTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
    }

    public string DataRoot { get; }

    public string SettingsPath => Path.Combine(DataRoot, "Roaming", "Lightshot", "settings.json");

    public string LocalData => Path.Combine(DataRoot, "Local", "Lightshot");

    public string QuitEventName => AppInstance.ScopedName(Program.QuitEventBaseName, DataRoot);

    public string ActivateEventName => AppInstance.ScopedName(Program.ActivateEventBaseName, DataRoot);

    /// <summary>The user's real settings file, which no desktop test may touch.</summary>
    public static string RealSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lightshot", "settings.json");

    public ProcessStartInfo StartInfo(string exePath, string arguments = "")
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.EnvironmentVariables[AppInstance.DataRootVariable] = DataRoot;
        psi.EnvironmentVariables["LIGHTSHOT_DISABLE_ONBOARDING"] = "1";

        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (string.IsNullOrEmpty(dotnetRoot))
        {
            string defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet");
            dotnetRoot = Directory.Exists(defaultRoot) ? defaultRoot : null;
        }
        if (dotnetRoot != null)
        {
            psi.EnvironmentVariables["DOTNET_ROOT"] = dotnetRoot;
        }
        return psi;
    }

    public Process Start(ProcessStartInfo psi)
    {
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Lightshot.App did not start.");
        _started.Add((process.Id, process.StartTime));
        return process;
    }

    public Process Start(string exePath, string arguments = "") => Start(StartInfo(exePath, arguments));

    public JsonObject ReadSettings()
    {
        if (!File.Exists(SettingsPath)) return new JsonObject();
        try { return JsonNode.Parse(File.ReadAllText(SettingsPath))?.AsObject() ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    public void WriteSettings(JsonObject root) => File.WriteAllText(SettingsPath, root.ToJsonString());

    /// <summary>Asks this test's own instance to quit; returns false when it has no quit event yet.</summary>
    public bool SignalQuit()
    {
        try
        {
            using var quit = EventWaitHandle.OpenExisting(QuitEventName);
            quit.Set();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    /// <summary>Kills the processes this instance started, and nothing else.</summary>
    public void KillStarted()
    {
        foreach (var (id, startTime) in _started)
        {
            try
            {
                using var p = Process.GetProcessById(id);
                if (p.StartTime != startTime || p.HasExited) continue;
                p.Kill(entireProcessTree: true);
                p.WaitForExit(2000);
            }
            catch (ArgumentException)
            {
                // Already exited.
            }
            catch (InvalidOperationException)
            {
                // Exited between the lookup and the kill.
            }
        }
    }

    public void Dispose()
    {
        KillStarted();
        _started.Clear();
        try { Directory.Delete(DataRoot, true); } catch { }
    }
}
