// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Lightshot.App.UiTests;

/// <summary>
/// An AppCompat HighDpiAware layer on dotnet.exe reaches the test host through __COMPAT_LAYER and forces
/// Per-Monitor v1 without WM_DPICHANGED, so WPF windows keep the primary's scale on every monitor. The
/// app itself starts as Per-Monitor v2; tests that check per-monitor scaling re-run in this test exe
/// without the layer so they see what the app sees.
/// </summary>
internal static class CleanDpiHost
{
    private const string CompatLayer = "__COMPAT_LAYER";
    private const string ChildMarker = "LIGHTSHOT_UITESTS_CLEAN_DPI_HOST";

    public static bool IsShimmed =>
        Environment.GetEnvironmentVariable(ChildMarker) != "1" &&
        (Environment.GetEnvironmentVariable(CompatLayer) ?? string.Empty)
            .Contains("HighDpiAware", StringComparison.OrdinalIgnoreCase);

    public static void Run(ITestOutputHelper output, Type testClass, string testMethod)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Lightshot.App.UiTests.exe");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        psi.ArgumentList.Add("-method");
        psi.ArgumentList.Add($"{testClass.FullName}.{testMethod}");
        psi.ArgumentList.Add("-showLiveOutput");
        psi.Environment.Remove(CompatLayer);
        psi.Environment[ChildMarker] = "1";

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{testMethod} timed out in a clean DPI host.");
        }

        output.WriteLine(stdout.Result);
        output.WriteLine(stderr.Result);
        Assert.True(process.ExitCode == 0 && stdout.Result.Contains("Failed: 0", StringComparison.Ordinal),
            $"{testMethod} failed in a clean DPI host (exit {process.ExitCode}).");
    }
}
