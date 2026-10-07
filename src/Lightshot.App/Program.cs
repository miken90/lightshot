using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Lightshot.Core;
using Lightshot.Platform.Windows.Files;
using Lightshot.Platform.Windows.Settings;
using Lightshot.Platform.Windows.Windows;
using Velopack;
using Lightshot.App.Startup;

namespace Lightshot.App;

public static class Program
{
    // Scoped to LIGHTSHOT_DATA_ROOT when set, so a test-launched app never meets the user's instance.
    private static readonly string MutexName = AppInstance.ScopedName(@"Local\Lightshot.SingleInstance");
    public const string QuitEventBaseName = @"Local\Lightshot.Quit";
    public const string ActivateEventBaseName = @"Local\Lightshot.Activate";
    public static readonly string QuitEventName = AppInstance.ScopedName(QuitEventBaseName);
    public static readonly string ActivateEventName = AppInstance.ScopedName(ActivateEventBaseName);

    [STAThread]
    public static int Main(string[] args)
    {
        VelopackHooks.Run();

        // Ensure directories exist and settings file migrations are run at startup
        AppPaths.EnsureDirectoriesCreated();
        SettingsMigrations.MigrateFile(AppPaths.SettingsFile);

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
        using var controller = new AppController();
        controller.Initialize();

        // 5. Register wait handle on quit event to cleanly shut down
        RegisteredWaitHandle? waitHandle = null;
        waitHandle = ThreadPool.RegisterWaitForSingleObject(
            quitEvent,
            (state, timedOut) =>
            {
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        controller.Dispose();
                    }
                    finally
                    {
                        waitHandle?.Unregister(null);
                        app.Shutdown(0);
                    }
                }));
            },
            null,
            -1,
            true);

        // 6. Register wait handle on activate event (e.g. from secondary process launch)
        RegisteredWaitHandle? activateWaitHandle = null;
        activateWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            activateEvent,
            (state, timedOut) =>
            {
                controller.TriggerAreaCapture();
            },
            null,
            -1,
            false);

        // Configure Quick Access routing if requested via command-line
        if (args.Contains("--quick-access", StringComparer.OrdinalIgnoreCase))
        {
            controller.Settings.OpenInEditor = false;
        }
        else if (args.Contains("--area", StringComparer.OrdinalIgnoreCase) ||
                 args.Contains("--capture-area", StringComparer.OrdinalIgnoreCase))
        {
            controller.Settings.OpenInEditor = true;
        }

        // Onboarding window on first launch only; suppressed during automated tests
        bool disableOnboarding =
            args.Contains("--no-onboarding", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--area", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--capture-area", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--quick-access", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--record-screen", StringComparer.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("LIGHTSHOT_DISABLE_ONBOARDING"), "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("LIGHTSHOT_TEST_MODE"), "1", StringComparison.OrdinalIgnoreCase);

        bool isOnboarded = controller.Settings.GetSetting(SettingsKeys.AppOnboarded) == "true";
        if (!isOnboarded && !disableOnboarding)
        {
            controller.ShowOnboarding();
        }

        // Trigger area capture if started with --area, --capture-area, or --quick-access
        if (args.Contains("--area", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--capture-area", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--quick-access", StringComparer.OrdinalIgnoreCase))
        {
            app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, new Action(() => controller.TriggerAreaCapture()));
        }

        if (args.Contains("--record-screen", StringComparer.OrdinalIgnoreCase))
        {
            app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, new Action(() => controller.TriggerCaptureAction(CaptureAction.RecordScreen)));
        }

        int exitCode = app.Run();
        activateWaitHandle?.Unregister(null);
        // Apply on quit (spec consent rule); runs after the UI has stopped.
        controller.ApplyPendingUpdateOnExit();
        controller.Dispose();
        return exitCode;
    }
}
