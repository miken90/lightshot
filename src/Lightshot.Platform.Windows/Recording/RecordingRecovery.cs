// Ported from LightshotKit/Sources/LightshotKit/RecordingRecovery.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vortice.MediaFoundation;

namespace Lightshot.Platform.Windows.Recording;

public record RecoveryResult(
    IReadOnlyList<string> RecoveredFiles,
    IReadOnlyList<string> UnfinalizedTakes
);

/// <summary>
/// Crash recovery at application launch:
/// - Deletes stale *.partial remux and GIF artifacts.
/// - Offers finished undelivered *.mp4 and *.gif files in scratch.
/// - Deletes fragmented takes shorter than 0.5 s or unreadable stubs.
/// - Strips trailing mfra boxes on takes >= 0.5 s and remuxes through TakeFinalizer.
/// - Preserves intact any take whose remux fails, listing it as unfinalized without auto-deletion.
/// </summary>
public static class RecordingRecovery
{
    public const double MinimumTakeDurationSeconds = 0.5;

    public static Task<RecoveryResult> RecoverAsync(
        ScratchStore scratchStore,
        TakeFinalizer? finalizer = null,
        Func<string, double>? durationProvider = null)
    {
        ArgumentNullException.ThrowIfNull(scratchStore);

        // 1. Delete all *.partial files left from crashed remuxes or GIF encodes
        scratchStore.CleanStalePartials();

        var recoveredFiles = new List<string>();
        var unfinalizedTakes = new List<string>();

        if (!Directory.Exists(scratchStore.RootDirectory))
        {
            return Task.FromResult(new RecoveryResult(recoveredFiles, unfinalizedTakes));
        }

        // 2. Discover finished .mp4 and .gif files that were never delivered
        var allFiles = Directory.GetFiles(scratchStore.RootDirectory);
        var finishedFiles = allFiles
            .Where(f => !f.EndsWith(ScratchStore.FragmentedExtension, StringComparison.OrdinalIgnoreCase) &&
                        !f.EndsWith(ScratchStore.PartialExtension, StringComparison.OrdinalIgnoreCase) &&
                        (f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        recoveredFiles.AddRange(finishedFiles);

        // 3. Process fragmented takes (*.frag.mp4)
        var takes = scratchStore.EnumerateTakes().OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        var remuxFinalizer = finalizer ?? new TakeFinalizer(new Mp4Remuxer());

        foreach (var takePath in takes)
        {
            double duration = durationProvider != null ? durationProvider(takePath) : GetTakeDuration(takePath);

            // Fragmented takes shorter than 0.5 s are deleted
            if (duration < MinimumTakeDurationSeconds)
            {
                scratchStore.DeleteTake(takePath);
                continue;
            }

            // Strips trailing mfra index from fragmented take
            MfFragmentedWriter.StripRandomAccessIndex(takePath);

            // Remux through TakeFinalizer
            string destinationMp4Path = scratchStore.GetFinalMp4Path(takePath);
            var result = remuxFinalizer.FinalizeTake(takePath, destinationMp4Path);

            if (result.Success && !string.IsNullOrEmpty(result.DeliveredFilePath))
            {
                recoveredFiles.Add(result.DeliveredFilePath);
            }
            else
            {
                // Remux failed: take is kept intact as recoverable, never deleted automatically
                unfinalizedTakes.Add(takePath);
            }
        }

        return Task.FromResult(new RecoveryResult(recoveredFiles, unfinalizedTakes));
    }

    private static double GetTakeDuration(string path)
    {
        try
        {
            MediaFactory.MFStartup().CheckError();
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
            var prop = reader.GetPresentationAttribute(SourceReaderIndex.MediaSource, PresentationDescriptionAttributeKeys.Duration);
            if (prop.Value is ulong u64) return u64 / 10_000_000.0;
            if (prop.Value is long s64) return s64 / 10_000_000.0;
            if (prop.Value != null) return Convert.ToDouble(prop.Value) / 10_000_000.0;
        }
        catch { }
        return 0.0;
    }
}
