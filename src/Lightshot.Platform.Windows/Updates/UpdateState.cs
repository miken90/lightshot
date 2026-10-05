using System;

namespace Lightshot.Platform.Windows.Updates;

public sealed class UpdateState
{
    public bool IsStaged { get; set; }
    public string? StagedVersion { get; set; }
    public long StagedSequence { get; set; }
    public string? StagedPackagePath { get; set; }
    public string? StagedSha256 { get; set; }
    public DateTimeOffset? LastCheckTime { get; set; }
    public string? LastError { get; set; }

    public void ResetStaging()
    {
        IsStaged = false;
        StagedVersion = null;
        StagedSequence = 0;
        StagedPackagePath = null;
        StagedSha256 = null;
    }
}
