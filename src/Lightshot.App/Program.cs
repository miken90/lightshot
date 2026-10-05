using System.Windows;
using Velopack;

namespace Lightshot.App;

public static class Program
{
    private const string MutexName = @"Local\Lightshot.SingleInstance";
    public const string QuitEventName = @"Local\Lightshot.Quit";
    public const string ActivateEventName = @"Local\Lightshot.Activate";

    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build().Run();

        // 1. Startup OS gate check (Build >= 22621)
        if (!OsGate.CheckCurrentOs())
        {
            MessageBox.Show(
                $"Lightshot requires Windows 11 (build {OsGate.MinimumSupportedBuild} or higher). Current OS build is {Environment.OSVersion.Version.Build}.",
                "Lightshot - Unsupported Operating System",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }

        // 2. Handle --quit command line argument
        if (args.Contains("--quit", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var quitHandle = EventWaitHandle.OpenExisting(QuitEventName);
                quitHandle.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // No existing instance to signal
            }
            return 0;
        }

        // 3. Single-instance mutex check
        using var singleInstanceMutex = new Mutex(true, MutexName, out bool isOnlyInstance);
        if (!isOnlyInstance)
        {
            try
            {
                using var activateHandle = EventWaitHandle.OpenExisting(ActivateEventName);
                activateHandle.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // Event not registered
            }
            return 0;
        }

        // 4. Create named events for Quit and Activate
        using var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        using var activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        var app = new App();
        using var tray = new TrayStub();
        tray.Initialize();

        // 5. Register wait handle on quit event to cleanly shut down
        RegisteredWaitHandle? waitHandle = null;
        waitHandle = ThreadPool.RegisterWaitForSingleObject(
            quitEvent,
            (state, timedOut) =>
            {
                tray.Remove();
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    waitHandle?.Unregister(null);
                    app.Shutdown(0);
                }));
            },
            null,
            -1,
            true);

        int exitCode = app.Run();
        tray.Remove();
        return exitCode;
    }
}
