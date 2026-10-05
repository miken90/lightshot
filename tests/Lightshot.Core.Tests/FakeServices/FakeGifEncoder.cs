// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lightshot.Core.Tests.FakeServices;

public class FakeGifEncoder : IGifEncoding
{
    public bool Fails { get; set; }
    public bool Holds { get; set; }
    public List<(string Video, string Output, GIFSettings Settings)> Encodes { get; } = [];
    public TaskCompletionSource<bool>? Gate { get; private set; }

    public async Task EncodeAsync(
        string videoPath,
        string outputPath,
        GIFSettings settings,
        Action<double> progress,
        CancellationToken cancellationToken = default)
    {
        Encodes.Add((videoPath, outputPath, settings));
        progress(0.5);

        if (Holds)
        {
            Gate = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => Gate.TrySetCanceled()))
            {
                await Gate.Task;
            }
        }

        if (Fails)
        {
            throw new IOException("GIF encoding failed");
        }

        progress(1.0);
    }

    public void Release()
    {
        Gate?.TrySetResult(true);
        Gate = null;
    }
}
