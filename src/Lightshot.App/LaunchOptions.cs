namespace Lightshot.App;

/// <summary>Launch flags that change how the app starts; the other flags are still read inline in Program.</summary>
internal sealed record LaunchOptions(bool NoTray)
{
    public static LaunchOptions Parse(IReadOnlyList<string> args) =>
        new(args.Contains("--no-tray", StringComparer.OrdinalIgnoreCase));
}
