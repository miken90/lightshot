using System;
using Xunit;

namespace Lightshot.TestSupport;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class UnitAttribute : TraitAttribute
{
    public const string Tier = "Unit";
    public string TierName => Tier;

    public UnitAttribute() : base("Tier", Tier)
    {
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RenderAttribute : TraitAttribute
{
    public const string Tier = "Render";
    public string TierName => Tier;

    public RenderAttribute() : base("Tier", Tier)
    {
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class GpuAttribute : TraitAttribute
{
    public const string Tier = "Gpu";
    public string TierName => Tier;

    public GpuAttribute() : base("Tier", Tier)
    {
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class MediaAttribute : TraitAttribute
{
    public const string Tier = "Media";
    public string TierName => Tier;

    public MediaAttribute() : base("Tier", Tier)
    {
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class DesktopAttribute : TraitAttribute
{
    public const string Tier = "Desktop";
    public string TierName => Tier;

    public DesktopAttribute() : base("Tier", Tier)
    {
    }
}
