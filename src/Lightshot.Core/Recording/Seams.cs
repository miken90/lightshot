// Ported from LightshotKit/Sources/LightshotKit/RecordingService.swift & AudioInputService.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lightshot.Core;

public readonly record struct AudioInputDevice(string Id, string Name, bool IsDefault);

public interface IAudioInputService
{
    Task<IReadOnlyList<AudioInputDevice>> AvailableInputsAsync();
}

public interface ICameraService
{
    Task<IReadOnlyList<CameraDevice>> AvailableCamerasAsync();
}

public abstract record RecordingEvent
{
    public sealed record Failed(RecordingError Error) : RecordingEvent;
    public sealed record AudioInputLost : RecordingEvent;
}

public interface IRecordingService : IPermissionAuthorizing
{
    Task<RecordingError?> StartAsync(RecordingOptions options, string outputPath, Action<RecordingEvent> onEvent);
    Task PauseAsync();
    Task ResumeAsync();
    Task<(string? Path, RecordingError? Error)> StopAsync();
    Task CancelAsync();
}

public interface IMediaSink
{
    void CopyFile(string path);
    void Save(string sourcePath, string destinationPath);
    void Trash(string path);
    void Delete(string path);
    void Copy(string sourcePath, string destinationPath);
}

public readonly record struct VideoMetadata(int PixelWidth, int PixelHeight, double Duration, ReadOnlyMemory<byte> ThumbnailPng);

public interface IMediaMetadataSource
{
    Task<VideoMetadata?> VideoMetadataAsync(string path);
}
