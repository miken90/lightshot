// Ported from LightshotKit/Sources/LightshotKit/OverlayController.swift
// MIT License, Copyright (c) 2026 Viet Le

using System.Threading.Tasks;

namespace Lightshot.Core;

/// <summary>
/// Pre-capture selection overlay seam: resolves region, window, or recording choices.
/// </summary>
public interface IOverlayController
{
    Task<CaptureRegion?> SelectRegionAsync(FrozenScreen? frozen, bool adjustable);
    Task<CaptureRegion?> SelectWindowAsync(FrozenScreen? frozen);
    Task<RecordingChoice?> SelectRecordingAsync(CaptureRegion? initial, RecordingDefaults defaults);
}
