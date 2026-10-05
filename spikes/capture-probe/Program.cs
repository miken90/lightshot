using System.Text.Json;
using System.Text.Json.Serialization;
using Vortice.DXGI;

namespace CaptureProbe;

public class CriterionResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "FAIL";

    [JsonPropertyName("measurement")]
    public object? Measurement { get; set; }

    [JsonPropertyName("details")]
    public string Details { get; set; } = "";

    [JsonPropertyName("fallback")]
    public string? Fallback { get; set; }
}

public class ProbeReport
{
    [JsonPropertyName("probe")]
    public string Probe => "capture-probe";

    [JsonPropertyName("gpuPreference")]
    public string GpuPreference { get; set; } = "SystemDefault";

    [JsonPropertyName("overallStatus")]
    public string OverallStatus { get; set; } = "FAIL";

    [JsonPropertyName("criteria")]
    public Dictionary<string, CriterionResult> Criteria { get; set; } = new();

    [JsonPropertyName("hardwareMatrix")]
    public Dictionary<string, object> HardwareMatrix { get; set; } = new();
}

public class Program
{
    public static int Main(string[] args)
    {
        bool assertMode = args.Contains("--assert", StringComparer.OrdinalIgnoreCase);
        string gpuPref = "SystemDefault";
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--gpu-pref", StringComparison.OrdinalIgnoreCase))
            {
                gpuPref = args[i + 1];
            }
        }

        var report = new ProbeReport { GpuPreference = gpuPref };

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            // 1. DDA Capture on all monitors
            var (ddaResults, ddaUnsupported) = DdaCapture.CaptureAllMonitors(factory);

            // Record hardware matrix info
            report.HardwareMatrix["monitorCount"] = ddaResults.Count;
            report.HardwareMatrix["monitors"] = ddaResults.Select(r => new
            {
                r.DeviceName,
                r.Width,
                r.Height,
                r.Left,
                r.Top,
                r.AdapterName
            }).ToList();

            // Criterion 1: still_latency_ms (< 100 ms warm median per monitor)
            bool latencyPass = ddaResults.Count > 0 && ddaResults.All(r => r.Success && r.WarmMedianLatencyMs < 100.0);
            report.Criteria["still_latency_ms"] = new CriterionResult
            {
                Status = latencyPass ? "PASS" : "FAIL",
                Measurement = new
                {
                    perMonitor = ddaResults.Select(r => new
                    {
                        monitor = r.DeviceName,
                        coldMs = Math.Round(r.ColdLatencyMs, 2),
                        warmMedianMs = Math.Round(r.WarmMedianLatencyMs, 2),
                        success = r.Success
                    }).ToList()
                },
                Details = latencyPass
                    ? "All monitors achieved warm median capture latency < 100 ms via DDA"
                    : "One or more monitors failed or exceeded 100 ms latency limit",
                Fallback = latencyPass ? null : "WGC for displays or pre-warmed desktop duplication"
            };

            // Criterion 2: dda_gpu_preferences (DDA does not fail with DXGI_ERROR_UNSUPPORTED)
            bool ddaPass = !ddaUnsupported && ddaResults.All(r => r.Success);
            report.Criteria["dda_gpu_preferences"] = new CriterionResult
            {
                Status = ddaPass ? "PASS" : "FAIL",
                Measurement = new
                {
                    unsupportedErrorCodeEncountered = ddaUnsupported,
                    testedGpuPreference = gpuPref,
                    adaptersCount = ddaResults.Select(r => r.AdapterName).Distinct().Count()
                },
                Details = ddaPass
                    ? $"DDA succeeded on all {ddaResults.Count} outputs without DXGI_ERROR_UNSUPPORTED (GPU pref: {gpuPref})"
                    : "DDA encountered DXGI_ERROR_UNSUPPORTED (0x887A0004) or failed output duplication",
                Fallback = ddaPass ? null : "Create D3D device on output adapter or use WGC for displays"
            };

            // Criterion 3: wgc_border (no yellow border on WGC for unpackaged exe)
            var wgcRes = WgcCapture.ProbeWgcAndPrintWindow();
            report.Criteria["wgc_border"] = new CriterionResult
            {
                Status = wgcRes.BorderStatus,
                Measurement = new
                {
                    isBorderRequiredSupported = wgcRes.IsBorderRequiredSupported,
                    wgcWindowCaptureSuccess = wgcRes.WgcWindowCaptureSuccess,
                    printWindowSuccess = wgcRes.PrintWindowSuccess
                },
                Details = $"{wgcRes.BorderReason}. {wgcRes.Details}",
                Fallback = wgcRes.BorderStatus == "PASS" ? null : "DDA for display captures; PrintWindow(PW_RENDERFULLCONTENT) for window stills; accept border for window recording as DEGRADE"
            };

            // Criterion 4: hdr_sdr_tone_mapping
            var hdrRes = HdrProbe.ProbeHdr(factory);
            report.HardwareMatrix["hdrDisplays"] = hdrRes.Displays;
            report.Criteria["hdr_sdr_tone_mapping"] = new CriterionResult
            {
                Status = hdrRes.Status,
                Measurement = new
                {
                    hdrDisplayCount = hdrRes.HdrDisplayCount,
                    toneMappedSdrWhite203Nit = hdrRes.ToneMappedSdrWhite203Nit
                },
                Details = hdrRes.Reason,
                Fallback = hdrRes.Status == "FAIL" ? "Use standard Reinhard SDR tone map" : null
            };

            // Criterion 5: mixed_dpi_coords
            var mixedDpiRes = DdaCapture.TestMixedDpiCoordinates(factory);
            report.Criteria["mixed_dpi_coords"] = new CriterionResult
            {
                Status = mixedDpiRes.Status,
                Measurement = new
                {
                    target = new { x = mixedDpiRes.MarkerTargetX, y = mixedDpiRes.MarkerTargetY },
                    found = new { x = mixedDpiRes.MarkerFoundX, y = mixedDpiRes.MarkerFoundY },
                    delta = new { x = mixedDpiRes.DeltaX, y = mixedDpiRes.DeltaY }
                },
                Details = mixedDpiRes.Reason,
                Fallback = mixedDpiRes.Status == "FAIL" ? "Per-monitor DPI coordinate conversion via GetDpiForMonitor" : null
            };

            // Compute overall status:
            // Any FAIL -> FAIL.
            // If all PASS or UNCOVERED -> PASS.
            bool anyFail = report.Criteria.Values.Any(c => c.Status == "FAIL");
            report.OverallStatus = anyFail ? "FAIL" : "PASS";

            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);

            if (assertMode && anyFail)
            {
                return 1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            report.OverallStatus = "FAIL";
            report.Criteria["probe_execution"] = new CriterionResult
            {
                Status = "FAIL",
                Details = ex.ToString(),
                Measurement = ex.Message
            };
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            return 1;
        }
    }
}
