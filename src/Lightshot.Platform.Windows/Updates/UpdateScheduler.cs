using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lightshot.Platform.Windows.Updates;

public sealed class UpdateScheduler
{
    public static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan DefaultErrorBackoff = TimeSpan.FromHours(1);

    private readonly UpdateChecker _checker;
    private readonly Func<bool> _isDisabled;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _errorBackoff;
    private readonly Action<string>? _logger;

    public DateTimeOffset? LastCheckTime { get; private set; }
    public bool LastCheckFailed { get; private set; }

    public UpdateScheduler(
        UpdateChecker checker,
        Func<bool>? isDisabled = null,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? checkInterval = null,
        TimeSpan? errorBackoff = null,
        Action<string>? logger = null)
    {
        _checker = checker ?? throw new ArgumentNullException(nameof(checker));
        _isDisabled = isDisabled ?? (() => false);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _checkInterval = checkInterval ?? DefaultCheckInterval;
        _errorBackoff = errorBackoff ?? DefaultErrorBackoff;
        _logger = logger;
    }

    public bool CanCheckNow(DateTimeOffset now)
    {
        if (_isDisabled())
        {
            return false;
        }

        if (LastCheckTime == null)
        {
            return true;
        }

        var elapsed = now - LastCheckTime.Value;
        if (elapsed < TimeSpan.Zero)
        {
            return true; // Clock jumped backwards
        }

        var requiredWait = LastCheckFailed ? _errorBackoff : _checkInterval;
        return elapsed >= requiredWait;
    }

    public DateTimeOffset GetNextCheckDueTime(DateTimeOffset now)
    {
        if (LastCheckTime == null)
        {
            return now;
        }

        var requiredWait = LastCheckFailed ? _errorBackoff : _checkInterval;
        return LastCheckTime.Value + requiredWait;
    }

    public async Task<UpdateManifest?> TriggerCheckAsync(
        long currentSequence,
        string? expectedPublicKey = null,
        bool force = false,
        CancellationToken ct = default)
    {
        if (_isDisabled())
        {
            _logger?.Invoke("[UpdateScheduler] Updates disabled; check skipped.");
            return null;
        }

        var now = _clock();
        if (!force && !CanCheckNow(now))
        {
            _logger?.Invoke($"[UpdateScheduler] Check deferred until next interval (due at {GetNextCheckDueTime(now):u}).");
            return null;
        }

        LastCheckTime = now;
        var manifest = await _checker.CheckForUpdateAsync(currentSequence, expectedPublicKey, ct).ConfigureAwait(false);

        if (manifest == null)
        {
            LastCheckFailed = true;
            _logger?.Invoke($"[UpdateScheduler] Update check returned no manifest or failed; backing off for {_errorBackoff.TotalMinutes}m.");
        }
        else
        {
            LastCheckFailed = false;
            _logger?.Invoke($"[UpdateScheduler] Update check succeeded; next regular check in {_checkInterval.TotalHours}h.");
        }

        return manifest;
    }

    public void Reset()
    {
        LastCheckTime = null;
        LastCheckFailed = false;
    }
}
