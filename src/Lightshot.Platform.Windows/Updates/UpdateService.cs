using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Core.Settings;
using Lightshot.Platform.Windows.Settings;

namespace Lightshot.Platform.Windows.Updates;

public enum UpdateCheckOutcome
{
    NotInstalled,
    Disabled,
    NoUpdate,
    Staged,
    StageFailed
}

/// <summary>
/// Composes the verify-then-stage pipeline for the app. NoUpdate also covers a failed check;
/// UpdateChecker logs the reason, and the scheduler applies its backoff.
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string ReleaseDownloadBase = "https://github.com/miken90/lightshot/releases/download/";

    private readonly ISettingsStore _settings;
    private readonly VelopackApplier _applier;
    private readonly string _stagingDirectory;
    private readonly string? _publicKey;
    private readonly HttpClient _http;
    private readonly UpdateScheduler _scheduler;
    private readonly UpdateStager _stager;
    private int _launchCount = -1;

    public UpdateService(
        ISettingsStore settings,
        VelopackApplier applier,
        string stagingDirectory,
        HttpMessageHandler? handler = null,
        string? publicKey = null,
        string manifestUrl = UpdateChecker.DefaultManifestUrl,
        Action<string>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));
        _stagingDirectory = stagingDirectory ?? throw new ArgumentNullException(nameof(stagingDirectory));
        _publicKey = publicKey;

        IsInstalled = applier.IsInstalled();
        RunningVersion = IsInstalled ? applier.InstalledVersion() : null;

        _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        var verifier = new ManifestVerifier(publicKey, logger, RunningVersion);
        var checker = new UpdateChecker(_http, verifier, () => !IsEnabled, null, logger, manifestUrl);
        _scheduler = new UpdateScheduler(checker, () => !IsEnabled, logger: logger);
        _stager = new UpdateStager(_http, logger);
    }

    public bool IsInstalled { get; }
    public string? RunningVersion { get; }
    public UpdateState State { get; } = new();

    public bool IsEnabled
    {
        get => !string.Equals(_settings.GetSetting(SettingsKeys.UpdateEnabled), "false", StringComparison.OrdinalIgnoreCase);
        set => _settings.SetSetting(SettingsKeys.UpdateEnabled, value ? "true" : "false");
    }

    public long SequenceFloor =>
        long.TryParse(_settings.GetSetting(SettingsKeys.UpdateSequence), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : 0;

    // Once per process; returns this launch's number (1 = first launch).
    public int RecordLaunch()
    {
        if (_launchCount > 0) return _launchCount;

        int current = int.TryParse(_settings.GetSetting(SettingsKeys.AppLaunchCount), NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c : 0;
        _launchCount = current + 1;
        _settings.SetSetting(SettingsKeys.AppLaunchCount, _launchCount.ToString(CultureInfo.InvariantCulture));
        return _launchCount;
    }

    // After a successful apply the new version runs: adopt the applied sequence as the floor. Pending keys are always cleared;
    // a failed apply simply leaves the floor where it was.
    public void ReconcileAfterUpdate()
    {
        string? pendingVersion = _settings.GetSetting(SettingsKeys.UpdatePendingVersion);
        if (pendingVersion == null) return;
        if (RunningVersion != null && string.Equals(pendingVersion, RunningVersion, StringComparison.OrdinalIgnoreCase))
        {
            string? pendingSeq = _settings.GetSetting(SettingsKeys.UpdatePendingSequence);
            if (pendingSeq != null)
            {
                _settings.SetSetting(SettingsKeys.UpdateSequence, pendingSeq);
            }
        }
        _settings.SetSetting(SettingsKeys.UpdatePendingVersion, null);
        _settings.SetSetting(SettingsKeys.UpdatePendingSequence, null);
    }

    public async Task<UpdateCheckOutcome> CheckAndStageAsync(bool force, CancellationToken ct = default)
    {
        if (!IsInstalled) return UpdateCheckOutcome.NotInstalled;
        if (!IsEnabled) return UpdateCheckOutcome.Disabled;
        var manifest = await _scheduler.TriggerCheckAsync(SequenceFloor, _publicKey, force, ct).ConfigureAwait(false);
        if (manifest == null) return State.IsStaged ? UpdateCheckOutcome.Staged : UpdateCheckOutcome.NoUpdate;
        if (State.IsStaged && State.StagedSequence >= manifest.Sequence) return UpdateCheckOutcome.Staged;
        bool ok = await _stager.StagePackageAsync(manifest, _stagingDirectory, State,
            ReleaseDownloadBase + "v" + manifest.Version + "/", ct).ConfigureAwait(false);
        return ok ? UpdateCheckOutcome.Staged : UpdateCheckOutcome.StageFailed;
    }

    public async Task<bool> ApplyAsync(bool restart, CancellationToken ct = default)
    {
        if (!IsInstalled || !State.IsStaged) return false;
        _settings.SetSetting(SettingsKeys.UpdatePendingVersion, State.StagedVersion);
        _settings.SetSetting(SettingsKeys.UpdatePendingSequence, State.StagedSequence.ToString(CultureInfo.InvariantCulture));
        return await _applier.ApplyStagedUpdateAsync(_stagingDirectory, restart, silent: true, ct).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();
}
