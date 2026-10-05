// MIT License, Copyright (c) 2026 Viet Le

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lightshot.Core.Tests.FakeServices;

public class FakeDelaySpy
{
    public List<double> Waits { get; } = [];

    public Task SleepAsync(double seconds)
    {
        Waits.Add(seconds);
        return Task.CompletedTask;
    }
}
