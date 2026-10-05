// Velopack 1.2.161 Local Directory Apply Call Chain:
// 1. var source = new Velopack.Sources.SimpleFileSource(new DirectoryInfo(stagingDirectory));
// 2. var updateManager = new Velopack.UpdateManager(source);
// 3. if (!updateManager.IsInstalled) return false;
// 4. var updateInfo = await updateManager.CheckForUpdatesAsync();
// 5. if (updateInfo == null) return false;
// 6. await updateManager.DownloadUpdatesAsync(updateInfo);
// 7. updateManager.WaitExitThenApplyUpdates(updateInfo.TargetFullRelease, silent: true, restart: true);

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Lightshot.Platform.Windows.Updates;

public class VelopackApplier
{
    private readonly Action<string>? _logger;

    public VelopackApplier(Action<string>? logger = null)
    {
        _logger = logger;
    }

    public virtual bool IsInstalled()
    {
        try
        {
            var mgr = new UpdateManager("");
            return mgr.IsInstalled;
        }
        catch
        {
            return false;
        }
    }

    public virtual async Task<bool> ApplyStagedUpdateAsync(
        string stagingDirectory,
        bool restart = true,
        bool silent = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);

        if (!Directory.Exists(stagingDirectory))
        {
            _logger?.Invoke($"[VelopackApplier] Staging directory '{stagingDirectory}' does not exist.");
            return false;
        }

        try
        {
            var dirInfo = new DirectoryInfo(stagingDirectory);
            var source = new SimpleFileSource(dirInfo);
            var mgr = new UpdateManager(source);

            if (!mgr.IsInstalled)
            {
                _logger?.Invoke("[VelopackApplier] Current process is not running an installed Velopack app (portable or dev build). Skipping update apply.");
                return false;
            }

            _logger?.Invoke($"[VelopackApplier] Checking for staged updates in '{stagingDirectory}'...");
            var updateInfo = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updateInfo == null)
            {
                _logger?.Invoke("[VelopackApplier] No compatible update found in staging directory.");
                return false;
            }

            _logger?.Invoke($"[VelopackApplier] Preparing update {updateInfo.TargetFullRelease?.Version}...");
            await mgr.DownloadUpdatesAsync(updateInfo, cancelToken: ct).ConfigureAwait(false);

            _logger?.Invoke($"[VelopackApplier] Triggering Velopack wait-and-apply (silent: {silent}, restart: {restart})...");
            mgr.WaitExitThenApplyUpdates(updateInfo.TargetFullRelease, silent: silent, restart: restart);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"[VelopackApplier] Error applying update: {ex.Message}");
            return false;
        }
    }
}
