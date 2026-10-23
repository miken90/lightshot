using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Lightshot.Platform.Windows.Settings;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class HubContractTests
{
    private const string Hint =
        "SettingsKeys changed: bump settings.schemaVersion in hub-contract.json, add a history entry, tell the my-utilities hub owner";

    [Fact]
    [Unit]
    public void Contract_matches_launch_flags_and_settings_keys()
    {
        var contract = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "hub-contract.json")))!.AsObject();
        var history = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "HubContractHistory.json")))!.AsObject();

        var hubFlag = contract["hubFlag"]!.GetValue<string>();
        Assert.True(LaunchOptions.Parse([hubFlag]).NoTray, $"LaunchOptions does not recognise hubFlag '{hubFlag}'.");

        var settings = contract["settings"]!.AsObject();
        var keys = settings["keys"]!.AsArray().Select(k => k!.GetValue<string>()).ToList();
        Assert.True(keys.ToHashSet().SetEquals(SettingsKeys.AllKeys), Hint);

        var version = settings["schemaVersion"]!.GetValue<int>().ToString();
        var recorded = history[version]!.AsArray().Select(k => k!.GetValue<string>()).ToList();
        Assert.True(recorded.SequenceEqual(keys), $"HubContractHistory entry {version} differs from hub-contract.json. {Hint}");
    }

    [Fact]
    [Unit]
    public void Contract_signals_are_the_events_the_app_creates()
    {
        var contract = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "hub-contract.json")))!.AsObject();

        var signals = Assert.IsType<JsonArray>(contract["signals"]).Select(s => s!.GetValue<string>()).ToList();
        var created = new[] { Program.QuitEventBaseName, Program.ActivateEventBaseName }.Concat(HubCommands.BaseNames).ToList();

        Assert.True(signals.ToHashSet().SetEquals(created) && signals.Count == created.Count,
            "hub-contract.json signals differ from the events the app creates: update both and tell the my-utilities hub owner");
    }
}
