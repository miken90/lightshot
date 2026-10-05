// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Threading.Tasks;

namespace Lightshot.Core.Tests.FakeServices;

public class FakeMediaMetadataSource : IMediaMetadataSource
{
    public VideoMetadata? Result { get; set; } = new(1280, 720, 30.0, new byte[] { 1, 2, 3 });

    public Task<VideoMetadata?> VideoMetadataAsync(string path) => Task.FromResult(Result);
}

public class HeldMediaMetadataSource : IMediaMetadataSource
{
    public TaskCompletionSource<bool>? Gate { get; private set; }

    public async Task<VideoMetadata?> VideoMetadataAsync(string path)
    {
        Gate = new TaskCompletionSource<bool>();
        await Gate.Task;
        return new VideoMetadata(1280, 720, 30.0, new byte[] { 1, 2, 3 });
    }

    public void Release()
    {
        Gate?.TrySetResult(true);
        Gate = null;
    }
}
