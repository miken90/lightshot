// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lightshot.Core.Tests.FakeServices;

public class FakeOverlayController : IOverlayController
{
    public CaptureRegion? Region { get; set; }
    public CaptureRegion? WindowRegion { get; set; }
    public RecordingChoice? RecordingChoiceResult { get; set; }

    // When set, region selection stays open until the test completes this source.
    public TaskCompletionSource<CaptureRegion?>? PendingRegion { get; set; }

    public int CallCount { get; private set; }
    public int WindowCallCount { get; private set; }
    public int RecordingCallCount { get; private set; }

    public List<FrozenScreen?> Backdrops { get; } = [];
    public List<bool> Adjustables { get; } = [];
    public List<(CaptureRegion? Initial, RecordingDefaults Defaults)> RecordingCalls { get; } = [];

    public FakeOverlayController(CaptureRegion? region = null, CaptureRegion? windowRegion = null, RecordingChoice? recordingChoice = null)
    {
        Region = region;
        WindowRegion = windowRegion;
        RecordingChoiceResult = recordingChoice;
    }

    public Task<CaptureRegion?> SelectRegionAsync(FrozenScreen? frozen = null, bool adjustable = false)
    {
        CallCount++;
        Backdrops.Add(frozen);
        Adjustables.Add(adjustable);
        return PendingRegion?.Task ?? Task.FromResult(Region);
    }

    public Task<CaptureRegion?> SelectWindowAsync(FrozenScreen? frozen = null)
    {
        WindowCallCount++;
        Backdrops.Add(frozen);
        return Task.FromResult(WindowRegion);
    }

    public Task<RecordingChoice?> SelectRecordingAsync(CaptureRegion? initial, RecordingDefaults defaults)
    {
        RecordingCallCount++;
        RecordingCalls.Add((initial, defaults));
        return Task.FromResult(RecordingChoiceResult);
    }
}
