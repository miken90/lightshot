// Velopack 1.2.161 Local Directory Apply Call Chain:
// 1. Staging directory has package and releases.win.json feed (from UpdateStager.WriteVelopackFeed).
// 2. var source = new Velopack.Sources.SimpleFileSource(new DirectoryInfo(stagingDirectory));
// 3. var updateManager = new Velopack.UpdateManager(source, null, _locator);
// 4. if (!updateManager.IsInstalled) return null;
// 5. var updateInfo = await updateManager.CheckForUpdatesAsync();
// 6. if (updateInfo == null || updateInfo.IsDowngrade) return null;
// 7. await updateManager.DownloadUpdatesAsync(updateInfo);
// 8. updateManager.WaitExitThenApplyUpdates(updateInfo.TargetFullRelease, silent: true, restart: true);

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Lightshot.Platform.Windows.Updates;

public class VelopackApplier
{
    private readonly Action<string>? _logger;
    private readonly IVelopackLocator? _locator;

    public VelopackApplier(Action<string>? logger = null, IVelopackLocator? locator = null)
    {
        _logger = logger;
        _locator = locator;
    }

    // VelopackLocator.Current throws until VelopackApp.Build().Run() has set it (VelopackHooks.Run in Program.Main);
    // in a test host or a dev run it is not set, so this reports "not installed".
    private IVelopackLocator? Locator()
    {
        try { return _locator ?? VelopackLocator.Current; } catch (InvalidOperationException) { return null; }
    }

    public virtual bool IsInstalled() => Locator()?.CurrentlyInstalledVersion != null;

    public virtual string? InstalledVersion() => Locator()?.CurrentlyInstalledVersion?.ToString();

    // Copies the staged package into Velopack's packages folder after Velopack re-checks it against the feed.
    public virtual async Task<VelopackAsset?> PrepareStagedUpdateAsync(string stagingDirectory, CancellationToken ct = default)
    {
        var (_, asset) = await PrepareCoreAsync(stagingDirectory, ct).ConfigureAwait(false);
        return asset;
    }

    public virtual async Task<bool> ApplyStagedUpdateAsync(
        string stagingDirectory,
        bool restart = true,
        bool silent = true,
        CancellationToken ct = default)
    {
        var (mgr, asset) = await PrepareCoreAsync(stagingDirectory, ct).ConfigureAwait(false);
        if (mgr == null || asset == null) return false;
        mgr.WaitExitThenApplyUpdates(asset, silent: silent, restart: restart);
        return true;
    }

    private async Task<(UpdateManager? Manager, VelopackAsset? Asset)> PrepareCoreAsync(string stagingDirectory, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);

        if (!Directory.Exists(stagingDirectory))
        {
            _logger?.Invoke($"[VelopackApplier] Staging directory '{stagingDirectory}' does not exist.");
            return (null, null);
        }

        try
        {
            var dirInfo = new DirectoryInfo(stagingDirectory);
            var source = new SimpleFileSource(dirInfo);
            var mgr = new UpdateManager(source, null, _locator);

            if (!mgr.IsInstalled)
            {
                _logger?.Invoke("[VelopackApplier] Current process is not running an installed Velopack app (portable or dev build). Skipping update apply.");
                return (null, null);
            }

            _logger?.Invoke($"[VelopackApplier] Checking for staged updates in '{stagingDirectory}'...");
            var updateInfo = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updateInfo == null || updateInfo.IsDowngrade)
            {
                _logger?.Invoke("[VelopackApplier] No compatible update found in staging directory.");
                return (null, null);
            }

            _logger?.Invoke($"[VelopackApplier] Preparing update {updateInfo.TargetFullRelease?.Version}...");
            await mgr.DownloadUpdatesAsync(updateInfo, cancelToken: ct).ConfigureAwait(false);
            return (mgr, updateInfo.TargetFullRelease);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"[VelopackApplier] Error preparing update: {ex.Message}");
            return (null, null);
        }
    }
}
