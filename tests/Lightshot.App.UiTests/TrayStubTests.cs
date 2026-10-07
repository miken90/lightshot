using System.Diagnostics;
using System.IO;
using System.Threading;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.UiTests;

public class TrayStubTests
{
    [Fact]
    [Desktop]
    public void AppStartsAndQuitsOnSignal()
    {
        string exePath = FindAppExecutable();
        Assert.True(File.Exists(exePath), $"App executable not found at: {exePath}");

        using var isolated = new IsolatedApp();
        using var process = isolated.Start(exePath);

        try
        {
            // Allow the process to initialize its mutex and quit event
            bool eventOpened = false;
            for (int i = 0; i < 30; i++)
            {
                Thread.Sleep(200);
                if (process.HasExited)
                {
                    Assert.Fail($"App process exited prematurely with exit code {process.ExitCode}.");
                    break;
                }
                if (isolated.SignalQuit())
                {
                    eventOpened = true;
                    break;
                }
            }

            Assert.True(eventOpened, $"Failed to open quit event {isolated.QuitEventName}");
            bool exited = process.WaitForExit(5000);
            Assert.True(exited, "App process failed to exit within 5 seconds of quit signal.");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            isolated.KillStarted();
        }
    }

    [Fact]
    [Desktop]
    public void IsolatedAppLeavesTheRealSettingsFileUntouched()
    {
        string exePath = FindAppExecutable();
        Assert.True(File.Exists(exePath), $"App executable not found at: {exePath}");

        string realSettings = IsolatedApp.RealSettingsPath;
        byte[]? realBefore = File.Exists(realSettings) ? File.ReadAllBytes(realSettings) : null;
        DateTime realStampBefore = File.Exists(realSettings) ? File.GetLastWriteTimeUtc(realSettings) : DateTime.MinValue;

        using var isolated = new IsolatedApp();
        // A legacy key makes startup migrate and rewrite whichever settings file the app resolves.
        isolated.WriteSettings(new System.Text.Json.Nodes.JsonObject { ["HideDesktopIcons"] = true, ["save.location"] = isolated.DataRoot });

        using var process = isolated.Start(exePath);
        try
        {
            bool quitSignalled = false;
            for (int i = 0; i < 50 && !quitSignalled; i++)
            {
                Thread.Sleep(200);
                Assert.False(process.HasExited, $"App exited early with code {(process.HasExited ? process.ExitCode : 0)}: it must not hand off to another instance.");
                quitSignalled = isolated.SignalQuit();
            }
            Assert.True(quitSignalled, $"Failed to open quit event {isolated.QuitEventName}");
            Assert.True(process.WaitForExit(5000), "App process failed to exit within 5 seconds of quit signal.");
        }
        finally
        {
            isolated.KillStarted();
        }

        Assert.True(Directory.Exists(Path.Combine(isolated.LocalData, "History")), "The app did not create its data under the isolated root.");
        Assert.Contains("app.hideDesktopIcons", File.ReadAllText(isolated.SettingsPath));
        Assert.Equal(realBefore != null, File.Exists(realSettings));
        if (realBefore != null)
        {
            Assert.Equal(realBefore, File.ReadAllBytes(realSettings));
            Assert.Equal(realStampBefore, File.GetLastWriteTimeUtc(realSettings));
        }
    }

    private static string FindAppExecutable()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(
                current,
                "src",
                "Lightshot.App",
                "bin",
                "Debug",
                "net10.0-windows10.0.22621.0",
                "Lightshot.App.exe");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (File.Exists(Path.Combine(current, "Lightshot.slnx")) || File.Exists(Path.Combine(current, "global.json")))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        return string.Empty;
    }
}
