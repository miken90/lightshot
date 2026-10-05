namespace Lightshot.App;

public static class OsGate
{
    public const int MinimumSupportedBuild = 22621;

    public static bool IsBuildSupported(int buildNumber) => buildNumber >= MinimumSupportedBuild;

    public static bool CheckCurrentOs() => IsBuildSupported(Environment.OSVersion.Version.Build);
}
