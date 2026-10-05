// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lightshot.Core.Tests.FakeServices;

public class FakeRecordingService : IRecordingService
{
    public RecordingError? StartResult { get; set; }
    public (string? Path, RecordingError? Error) StopResult { get; set; } = ("/tmp/scratch/take.mp4", null);
    public CaptureAuthorizationStatus Status { get; set; } = CaptureAuthorizationStatus.Authorized;
    public CaptureAuthorizationStatus RequestResult { get; set; } = CaptureAuthorizationStatus.Authorized;

    public List<(RecordingOptions Options, string OutputPath)> Starts { get; } = [];
    public Action<RecordingEvent>? OnEvent { get; private set; }

    public bool HoldStart { get; set; }
    public TaskCompletionSource<bool>? StartGate { get; set; }

    public bool HoldCancel { get; set; }
    public TaskCompletionSource<bool>? CancelGate { get; set; }

    public bool HoldStop { get; set; }
    public TaskCompletionSource<bool>? StopGate { get; set; }

    public int StopCount { get; private set; }
    public int CancelCount { get; private set; }
    public int PauseCount { get; private set; }
    public int ResumeCount { get; private set; }
    public int RequestAuthorizationCount { get; private set; }

    public FakeRecordingService((string? Path, RecordingError? Error)? stopResult = null)
    {
        if (stopResult.HasValue)
        {
            StopResult = stopResult.Value;
        }
    }

    public Task<CaptureAuthorizationStatus> AuthorizationStatusAsync() => Task.FromResult(Status);

    public Task<CaptureAuthorizationStatus> RequestAuthorizationAsync()
    {
        RequestAuthorizationCount++;
        Status = RequestResult;
        return Task.FromResult(RequestResult);
    }

    public async Task<RecordingError?> StartAsync(RecordingOptions options, string outputPath, Action<RecordingEvent> onEvent)
    {
        Starts.Add((options, outputPath));
        OnEvent = onEvent;
        if (HoldStart)
        {
            StartGate = new TaskCompletionSource<bool>();
            await StartGate.Task;
        }
        return StartResult;
    }

    public Task PauseAsync()
    {
        PauseCount++;
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        ResumeCount++;
        return Task.CompletedTask;
    }

    public async Task<(string? Path, RecordingError? Error)> StopAsync()
    {
        StopCount++;
        if (HoldStop)
        {
            StopGate = new TaskCompletionSource<bool>();
            await StopGate.Task;
        }
        return StopResult;
    }

    public async Task CancelAsync()
    {
        CancelCount++;
        if (HoldCancel)
        {
            CancelGate = new TaskCompletionSource<bool>();
            await CancelGate.Task;
        }
    }
}
