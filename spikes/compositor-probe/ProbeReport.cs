using System.Text;
using System.Text.Json.Serialization;

namespace CompositorProbe;

public sealed class Criterion
{
    [JsonPropertyName("status")] public string Status { get; set; } = "FAIL";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("measurement")] public object? Measurement { get; set; }
    [JsonPropertyName("fallback")] public string? Fallback { get; set; }
}

public sealed class ProbeReport
{
    [JsonPropertyName("probe")] public string Probe => "compositor-probe";
    [JsonPropertyName("host")] public string HostKind { get; set; } = "HwndHost";
    [JsonPropertyName("environment")] public Dictionary<string, object?> Environment { get; set; } = new();
    [JsonPropertyName("criteria")] public Dictionary<string, Criterion> Criteria { get; set; } = new();
    [JsonPropertyName("executionError")] public string? ExecutionError { get; set; }
    [JsonIgnore] public bool ExecutionFailed => ExecutionError != null;
    [JsonIgnore] public bool AllPass => !ExecutionFailed && Criteria.Values.All(c => c.Status == "PASS");

    public static ProbeReport Failure(Exception ex) => new() { ExecutionError = ex.ToString() };

    public string Summary()
    {
        var sb = new StringBuilder();
        if (ExecutionError != null) sb.AppendLine("EXECUTION ERROR: " + ExecutionError);
        foreach (var (k, c) in Criteria) sb.AppendLine($"{c.Status,-9} {k}: {c.Summary}");
        return sb.ToString();
    }
}
