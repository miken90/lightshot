// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Lightshot.Platform.Windows.Files;

namespace Lightshot.App;

public sealed partial class AppController
{
    private DispatcherTimer? _autoClearTimer;

    internal void StartAutoClear()
    {
        _ = RunAutoClearAsync();
        _autoClearTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        _autoClearTimer.Tick += (_, _) => _ = RunAutoClearAsync();
        _autoClearTimer.Start();
    }

    // Never sweeps while recording, GIF conversion, archive, or pending take is running.
    private Task RunAutoClearAsync()
    {
        int days = _settingsStore.HistoryMaxAgeDays;
        if (days <= 0 || _coordinator.HasWorkInProgress || _coordinator.PendingRecording != null)
            return Task.CompletedTask;

        var cutoff = DateTime.UtcNow.AddDays(-days);
        try { _historyStore.PruneOlderThan(cutoff); } catch { }
        var keep = new HashSet<string>(
            _historyStore.All()
                .SelectMany(r => new[] { r.FileUrl, r.ThumbnailUrl })
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => Path.GetFullPath(p)),
            StringComparer.OrdinalIgnoreCase);
        return Task.Run(() => { try { TempSweeper.Sweep(AutoClearTargets.Default(), cutoff, keep); } catch { } });
    }
}
