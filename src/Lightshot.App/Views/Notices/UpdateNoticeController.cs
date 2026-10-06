using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;
using Lightshot.App.Updates;
using Lightshot.Platform.Windows.Updates;

namespace Lightshot.App.Views.Notices;

public sealed class UpdateNoticeController : IDisposable
{
    private readonly UpdateService _service;
    private readonly Func<bool> _hasWorkInProgress;
    private readonly Action<string?> _setTrayUpdate;
    private readonly Action<string, string> _showNotice;
    private readonly Action _shutdown;
    private readonly bool _enabled;
    private readonly Func<bool, Task<bool>> _apply;

    private DispatcherTimer? _timer;
    private bool _applyStarted;

    public event Action<string>? Staged;
    public string? StagedVersion => _service.State.IsStaged ? _service.State.StagedVersion : null;
    public int LaunchCount { get; private set; }

    public UpdateNoticeController(
        UpdateService service,
        Func<bool> hasWorkInProgress,
        Action<string?> setTrayUpdate,
        Action<string, string> showNotice,
        Action shutdown,
        bool testMode,
        Func<bool, Task<bool>>? apply = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _hasWorkInProgress = hasWorkInProgress ?? throw new ArgumentNullException(nameof(hasWorkInProgress));
        _setTrayUpdate = setTrayUpdate ?? throw new ArgumentNullException(nameof(setTrayUpdate));
        _showNotice = showNotice ?? throw new ArgumentNullException(nameof(showNotice));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
        _enabled = service.IsInstalled && !testMode;
        _apply = apply ?? (restart => service.ApplyAsync(restart));
    }

    public async Task StartAsync()
    {
        if (!_enabled) return;
        LaunchCount = _service.RecordLaunch();
        _service.ReconcileAfterUpdate();
        if (!UpdatePolicy.CanCheckOrPrompt(LaunchCount)) return; // first launch: no check, no prompt
        _timer = new DispatcherTimer { Interval = UpdateScheduler.DefaultCheckInterval };
        _timer.Tick += async (_, _) => await CheckSafelyAsync(force: false);
        _timer.Start();
        await CheckSafelyAsync(force: false);
    }

    public Task<UpdateCheckOutcome> CheckNowAsync() =>
        _enabled ? CheckSafelyAsync(force: true) : Task.FromResult(UpdateCheckOutcome.NotInstalled);

    private async Task<UpdateCheckOutcome> CheckSafelyAsync(bool force)
    {
        try
        {
            string? before = StagedVersion;
            var outcome = await _service.CheckAndStageAsync(force);
            if (outcome == UpdateCheckOutcome.Staged && StagedVersion is { } v && v != before) OnStaged(v);
            return outcome;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Update check failed: {ex}");
            return UpdateCheckOutcome.StageFailed;
        }
    }

    private void OnStaged(string version)
    {
        _setTrayUpdate($"Restart to Update ({version})");
        _showNotice("Lightshot update ready",
            $"Version {version} installs when you quit Lightshot, or choose Restart to Update from the tray menu.");
        Staged?.Invoke(version);
    }

    public async Task<UpdatePolicyDecision> RestartToUpdateAsync()
    {
        var decision = UpdatePolicy.Evaluate(LaunchCount, _service.State.IsStaged, _hasWorkInProgress);
        if (decision == UpdatePolicyDecision.HeldWorkInProgress)
        {
            _showNotice("Update waiting", "Finish your recording, export or edits, then choose Restart to Update again.");
            return decision;
        }
        if (decision != UpdatePolicyDecision.ReadyToApply) return decision;
        _applyStarted = true;
        if (await _apply(true))
        {
            _shutdown();
        }
        else
        {
            _applyStarted = false;
            _showNotice("Update failed", "The update could not be started. Lightshot will try again later.");
        }
        return decision;
    }

    // Runs on the main thread after the dispatcher stopped: no WPF or tray calls here.
    public bool ApplyOnExit()
    {
        if (!_enabled || _applyStarted || !UpdatePolicy.CanApplyUpdate(LaunchCount, _service.State.IsStaged, null)) return false;
        _applyStarted = true;
        var task = Task.Run(() => _apply(false));
        return task.Wait(TimeSpan.FromMinutes(2)) && task.Result;
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
    }
}
