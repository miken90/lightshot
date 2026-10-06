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

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.EnvironmentVariables["LIGHTSHOT_DISABLE_ONBOARDING"] = "1";

        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            psi.EnvironmentVariables["DOTNET_ROOT"] = dotnetRoot;
        }
        else
        {
            string defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet");
            if (Directory.Exists(defaultRoot))
            {
                psi.EnvironmentVariables["DOTNET_ROOT"] = defaultRoot;
            }
        }

        // Ensure no previous instances are running and mutex is released
        foreach (var p in Process.GetProcessesByName("Lightshot.App"))
        {
            try
            {
                p.Kill();
                p.WaitForExit(1000);
            }
            catch { }
        }
        Thread.Sleep(500);

        using var process = Process.Start(psi);
        Assert.NotNull(process);

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
                try
                {
                    using var handle = EventWaitHandle.OpenExisting(@"Local\Lightshot.Quit");
                    eventOpened = true;
                    handle.Set();
                    break;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // Retry until available
                }
            }

            Assert.True(eventOpened, "Failed to open quit event Local\\Lightshot.Quit");
            bool exited = process.WaitForExit(5000);
            Assert.True(exited, "App process failed to exit within 5 seconds of quit signal.");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
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
