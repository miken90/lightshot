// Ported for Lightshot Windows Port (Phase 7 R2)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Threading.Tasks;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Recording;

/// <summary>
/// Windows implementation of IRecordingService.
/// Coordinates screen recording sessions through RecordingEngine, MfFragmentedWriter,
/// DDA capture, cadence driver, compositor, audio pump, and finalizer.
/// </summary>
public sealed class WindowsRecordingService : IRecordingService, IDisposable
{
    private readonly RecordingEngine _engine;
    private bool _disposed;

    public bool RequestWaitsForAnswer => false;
    public RecordingEngine Engine => _engine;

    public WindowsRecordingService(RecordingEngine? engine = null)
    {
        _engine = engine ?? new RecordingEngine();
    }

    public Task<CaptureAuthorizationStatus> AuthorizationStatusAsync()
    {
        return Task.FromResult(CaptureAuthorizationStatus.Authorized);
    }

    public Task<CaptureAuthorizationStatus> RequestAuthorizationAsync()
    {
        return Task.FromResult(CaptureAuthorizationStatus.Authorized);
    }

    public Task<RecordingError?> StartAsync(RecordingOptions options, string outputPath, Action<RecordingEvent> onEvent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _engine.StartAsync(options, outputPath, onEvent);
    }

    public Task PauseAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _engine.PauseAsync();
    }

    public Task ResumeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _engine.ResumeAsync();
    }

    public Task<(string? Path, RecordingError? Error)> StopAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _engine.StopAsync();
    }

    public Task CancelAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _engine.CancelAsync();
    }

    public void WriteAudioSample(int streamIndex, byte[] pcmData, long sampleTimeHns, long durationHns)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _engine.WriteAudioSample(streamIndex, pcmData, sampleTimeHns, durationHns);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.Dispose();
    }
}
