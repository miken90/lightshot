using System;

namespace Lightshot.App.Updates;

public enum UpdatePolicyDecision
{
    ReadyToApply,
    NoUpdateStaged,
    DeferredFirstLaunch,
    HeldWorkInProgress
}

public sealed class UpdatePolicy
{
    public const int MinimumLaunchCountForUpdate = 2;

    public static bool CanCheckOrPrompt(int launchCount)
    {
        return launchCount >= MinimumLaunchCountForUpdate;
    }

    public static bool CanApplyUpdate(int launchCount, bool isStaged, Func<bool>? hasWorkInProgress)
    {
        return Evaluate(launchCount, isStaged, hasWorkInProgress) == UpdatePolicyDecision.ReadyToApply;
    }

    public static UpdatePolicyDecision Evaluate(int launchCount, bool isStaged, Func<bool>? hasWorkInProgress)
    {
        if (!isStaged)
        {
            return UpdatePolicyDecision.NoUpdateStaged;
        }

        if (launchCount < MinimumLaunchCountForUpdate)
        {
            return UpdatePolicyDecision.DeferredFirstLaunch;
        }

        if (hasWorkInProgress != null && hasWorkInProgress())
        {
            return UpdatePolicyDecision.HeldWorkInProgress;
        }

        return UpdatePolicyDecision.ReadyToApply;
    }
}
