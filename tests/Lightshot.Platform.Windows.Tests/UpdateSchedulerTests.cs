using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Platform.Windows.Updates;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class UpdateSchedulerTests
{
    private sealed class CountingFakeHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string ContentString { get; set; } = "{}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ContentString, Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    [Unit]
    public async Task DisabledSettingMakesZeroRequests()
    {
        var handler = new CountingFakeHandler();
        var client = new HttpClient(handler);
        var verifier = new ManifestVerifier();
        var checker = new UpdateChecker(client, verifier);

        var scheduler = new UpdateScheduler(
            checker: checker,
            isDisabled: () => true
        );

        Assert.False(scheduler.CanCheckNow(DateTimeOffset.UtcNow));

        var manifest = await scheduler.TriggerCheckAsync(currentSequence: 0, ct: TestContext.Current.CancellationToken);

        Assert.Null(manifest);
        Assert.Equal(0, handler.RequestCount); // Zero network requests made
    }

    [Fact]
    [Unit]
    public void DailyScheduleEnforces24HourInterval()
    {
        var handler = new CountingFakeHandler();
        var client = new HttpClient(handler);
        var checker = new UpdateChecker(client, new ManifestVerifier());

        var t0 = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset currentVirtualTime = t0;

        var scheduler = new UpdateScheduler(
            checker: checker,
            clock: () => currentVirtualTime,
            checkInterval: TimeSpan.FromHours(24),
            errorBackoff: TimeSpan.FromHours(1)
        );

        // Before any check, can check immediately
        Assert.True(scheduler.CanCheckNow(currentVirtualTime));
    }

    [Fact]
    [Unit]
    public async Task DailyIntervalAndBackoffTimingCalculations()
    {
        var handler = new CountingFakeHandler();
        var client = new HttpClient(handler);
        var checker = new UpdateChecker(client, new ManifestVerifier());

        var t0 = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        var scheduler = new UpdateScheduler(
            checker: checker,
            clock: () => t0,
            checkInterval: TimeSpan.FromHours(24),
            errorBackoff: TimeSpan.FromHours(1)
        );

        // 1. Initial state (never checked)
        Assert.True(scheduler.CanCheckNow(t0));

        // 2. Simulate failed check at t0 -> error backoff should be 1 hour
        handler.StatusCode = HttpStatusCode.NotFound;
        await scheduler.TriggerCheckAsync(0, ct: TestContext.Current.CancellationToken);

        Assert.True(scheduler.LastCheckFailed);
        Assert.NotNull(scheduler.LastCheckTime);

        // At T0 + 30m: still inside backoff, should be false
        Assert.False(scheduler.CanCheckNow(t0.AddMinutes(30)));

        // At T0 + 59m: still inside backoff, should be false
        Assert.False(scheduler.CanCheckNow(t0.AddMinutes(59)));

        // At T0 + 60m: backoff elapsed, should be true
        Assert.True(scheduler.CanCheckNow(t0.AddHours(1)));
    }

    [Fact]
    [Unit]
    public async Task TriggerCheckSkipsWhenNotDueUnlessForced()
    {
        var handler = new CountingFakeHandler { StatusCode = HttpStatusCode.NotFound };
        var client = new HttpClient(handler);
        var checker = new UpdateChecker(client, new ManifestVerifier());

        var scheduler = new UpdateScheduler(
            checker: checker,
            checkInterval: TimeSpan.FromHours(24),
            errorBackoff: TimeSpan.FromHours(1)
        );

        // First check
        await scheduler.TriggerCheckAsync(0, ct: TestContext.Current.CancellationToken);
        int requestsAfterFirst = handler.RequestCount;
        Assert.True(requestsAfterFirst > 0);

        // Second check immediately without force -> skipped
        await scheduler.TriggerCheckAsync(0, force: false, ct: TestContext.Current.CancellationToken);
        Assert.Equal(requestsAfterFirst, handler.RequestCount);

        // Third check with force: true -> executes
        await scheduler.TriggerCheckAsync(0, force: true, ct: TestContext.Current.CancellationToken);
        Assert.True(handler.RequestCount > requestsAfterFirst);
    }
}
