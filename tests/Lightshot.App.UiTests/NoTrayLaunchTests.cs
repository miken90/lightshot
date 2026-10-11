using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.UiTests;

public class NoTrayLaunchTests : IDisposable
{
    private readonly IsolatedApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    [Desktop]
    public void Starts_without_tray_and_quits_on_event()
    {
        string exePath = FindAppExecutable();
        Assert.True(File.Exists(exePath), $"App executable not found at: {exePath}");

        using var process = _app.Start(exePath, "--no-tray --no-onboarding");
        Thread.Sleep(3000);
        Assert.False(process.HasExited, "App exited although only the tray was meant to be off.");
        Assert.False(HasTrayHostWindow(process.Id), "A tray host window exists although --no-tray was passed.");

        Assert.True(_app.SignalQuit(), $"Quit event {_app.QuitEventName} was not registered.");
        Assert.True(process.WaitForExit(5000), "App did not exit within 5 s of the quit event.");
    }

    // Control for the test above: the same probe must see the tray host window on a default launch,
    // otherwise "no tray host" would pass vacuously.
    [Fact]
    [Desktop]
    public void Default_launch_creates_the_tray_host_window()
    {
        string exePath = FindAppExecutable();
        Assert.True(File.Exists(exePath), $"App executable not found at: {exePath}");

        using var process = _app.Start(exePath, "--no-onboarding");
        Thread.Sleep(3000);
        Assert.False(process.HasExited);
        Assert.True(HasTrayHostWindow(process.Id));

        Assert.True(_app.SignalQuit());
        Assert.True(process.WaitForExit(5000));
    }

    // TrayIcon owns a message-only window of class "LightshotTrayHost_<guid>"; the notification-area
    // button itself is not reliably visible to UI Automation (and the user's own Lightshot shows one).
    private static bool HasTrayHostWindow(int processId)
    {
        IntPtr hwnd = IntPtr.Zero;
        var cls = new StringBuilder(256);
        while ((hwnd = FindWindowEx(new IntPtr(-3), hwnd, null, null)) != IntPtr.Zero)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != processId) continue;
            cls.Clear();
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString().StartsWith("LightshotTrayHost_", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static string FindAppExecutable()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "src", "Lightshot.App", "bin", "Debug",
                "net10.0-windows10.0.22621.0", "Lightshot.App.exe");
            if (File.Exists(candidate) || File.Exists(Path.Combine(current, "Lightshot.slnx")))
            {
                return candidate;
            }
            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }
        return string.Empty;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
}
