using System;
using Lightshot.App.Updates;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class UpdatePolicyTests
{
    [Theory]
    [Unit]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(100, true)]
    public void CanCheckOrPrompt_RequiresSecondLaunch(int launchCount, bool expectedCanPrompt)
    {
        bool actual = UpdatePolicy.CanCheckOrPrompt(launchCount);
        Assert.Equal(expectedCanPrompt, actual);
    }

    [Fact]
    [Unit]
    public void RejectsWhenNoUpdateIsStaged()
    {
        bool canApply = UpdatePolicy.CanApplyUpdate(launchCount: 2, isStaged: false, hasWorkInProgress: () => false);
        Assert.False(canApply);

        var decision = UpdatePolicy.Evaluate(launchCount: 2, isStaged: false, hasWorkInProgress: () => false);
        Assert.Equal(UpdatePolicyDecision.NoUpdateStaged, decision);
    }

    [Fact]
    [Unit]
    public void DefersOnFirstLaunchEvenIfStaged()
    {
        bool canApply = UpdatePolicy.CanApplyUpdate(launchCount: 1, isStaged: true, hasWorkInProgress: () => false);
        Assert.False(canApply);

        var decision = UpdatePolicy.Evaluate(launchCount: 1, isStaged: true, hasWorkInProgress: () => false);
        Assert.Equal(UpdatePolicyDecision.DeferredFirstLaunch, decision);
    }

    [Fact]
    [Unit]
    public void HoldsUpdateWhenWorkIsInProgress()
    {
        // Second launch and staged, but user is currently taking a screenshot or recording
        bool canApply = UpdatePolicy.CanApplyUpdate(launchCount: 2, isStaged: true, hasWorkInProgress: () => true);
        Assert.False(canApply);

        var decision = UpdatePolicy.Evaluate(launchCount: 2, isStaged: true, hasWorkInProgress: () => true);
        Assert.Equal(UpdatePolicyDecision.HeldWorkInProgress, decision);
    }

    [Fact]
    [Unit]
    public void ReadyToApplyWhenSecondLaunchStagedAndIdle()
    {
        bool canApply = UpdatePolicy.CanApplyUpdate(launchCount: 2, isStaged: true, hasWorkInProgress: () => false);
        Assert.True(canApply);

        var decision = UpdatePolicy.Evaluate(launchCount: 2, isStaged: true, hasWorkInProgress: () => false);
        Assert.Equal(UpdatePolicyDecision.ReadyToApply, decision);
    }

    [Fact]
    [Unit]
    public void NullWorkInProgressHandlerTreatedAsIdle()
    {
        bool canApply = UpdatePolicy.CanApplyUpdate(launchCount: 5, isStaged: true, hasWorkInProgress: null);
        Assert.True(canApply);

        var decision = UpdatePolicy.Evaluate(launchCount: 5, isStaged: true, hasWorkInProgress: null);
        Assert.Equal(UpdatePolicyDecision.ReadyToApply, decision);
    }
}
