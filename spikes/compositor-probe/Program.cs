using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using Vortice.MediaFoundation;

namespace CompositorProbe;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.Error.WriteLine("UNHANDLED: " + e.ExceptionObject);
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ROOT")))
        {
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet");
            if (Directory.Exists(local)) Environment.SetEnvironmentVariable("DOTNET_ROOT", local);
        }

        var opts = RunOptions.Parse(args);
        string artifacts = FindArtifactsDir();
        Directory.CreateDirectory(artifacts);
        string resultPath = Path.Combine(artifacts, "compositor-probe-result.json");
        string clipPath = Path.Combine(artifacts, "compositor-probe-clip.mp4");

        MediaFactory.MFStartup().CheckError();
        int exit = 1;
        try
        {
            var sw = Stopwatch.StartNew();
            TestClipWriter.Write(clipPath, opts.ClipFrames);
            double genSec = sw.Elapsed.TotalSeconds;
            Console.WriteLine($"clip: {clipPath} {opts.ClipFrames} frames, {new FileInfo(clipPath).Length / 1048576.0:F1} MB, generated in {genSec:F1} s");

            var app = new App();
            var host = new D3dHost();
            var window = new MainWindow(host);
            ProbeReport? report = null;
            window.Loaded += (_, _) => Task.Run(async () =>
            {
                try { report = await new Runner(app, window, host, clipPath, opts, genSec).RunAsync(); }
                catch (Exception ex) { report = ProbeReport.Failure(ex); }
                finally { app.Dispatcher.Invoke(() => { try { window.Close(); } catch { } app.Shutdown(); }); }
            });
            app.Run(window);

            report ??= ProbeReport.Failure(new InvalidOperationException("runner produced no report"));
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
            File.WriteAllText(resultPath, json);
            Console.WriteLine(report.Summary());
            Console.WriteLine("result: " + resultPath);
            exit = (!opts.Assert || report.AllPass) ? 0 : 1;
            if (report.ExecutionFailed) exit = 1;
        }
        finally { MediaFactory.MFShutdown(); }
        return exit;
    }

    private static string FindArtifactsDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Lightshot.slnx"))) return Path.Combine(d.FullName, "artifacts");
        return Path.Combine(AppContext.BaseDirectory, "artifacts");
    }
}

public sealed class RunOptions
{
    public bool Assert;
    public double PlaySeconds = 30;
    public int ResizeCycles = 100;
    public int DpiCycles = 10;
    public int SeekCount = 20;
    public int ClipFrames = 2400;
    public int ClipFramesAvailable => ClipFrames;

    public static RunOptions Parse(string[] args)
    {
        var o = new RunOptions { Assert = args.Contains("--assert", StringComparer.OrdinalIgnoreCase) };
        for (int i = 0; i + 1 < args.Length; i++)
        {
            var v = args[i + 1];
            switch (args[i])
            {
                case "--seconds": o.PlaySeconds = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--resize-cycles": o.ResizeCycles = int.Parse(v); break;
                case "--dpi-cycles": o.DpiCycles = int.Parse(v); break;
                case "--seeks": o.SeekCount = int.Parse(v); break;
            }
        }
        return o;
    }
}
