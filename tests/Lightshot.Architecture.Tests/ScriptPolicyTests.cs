using System.Diagnostics;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Architecture.Tests;

public class ScriptPolicyTests
{
    [Fact]
    [Unit]
    public void AllScriptsAreAsciiAndParse()
    {
        string scriptsDir = FindRepoRelativePath("scripts");
        Assert.True(Directory.Exists(scriptsDir), $"Scripts directory not found: {scriptsDir}");

        var scriptFiles = Directory.GetFiles(scriptsDir, "*.ps1", SearchOption.AllDirectories);
        Assert.NotEmpty(scriptFiles);

        foreach (string scriptFile in scriptFiles)
        {
            // 1. Enforce strict ASCII (bytes 0 - 127)
            byte[] bytes = File.ReadAllBytes(scriptFile);
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                Assert.True(
                    b <= 127,
                    $"Non-ASCII byte 0x{b:X2} found in {Path.GetFileName(scriptFile)} at byte offset {i}");
            }

            // 2. Enforce Windows PowerShell 5.1 parse check
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"$err = $null; $null = [System.Management.Automation.Language.Parser]::ParseFile('{scriptFile.Replace("'", "''")}', [ref]$null, [ref]$err); if ($err.Count -gt 0) {{ foreach ($e in $err) {{ Write-Error $e.Message }}; exit 1 }}\"",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            Assert.NotNull(process);
            process.StandardInput.Close();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);
            Assert.True(
                process.ExitCode == 0,
                $"PowerShell 5.1 failed to parse {Path.GetFileName(scriptFile)}: {stderr}");
        }
    }

    private static string FindRepoRelativePath(string relativePath)
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                return candidate;
            }
            if (File.Exists(Path.Combine(current, "Lightshot.slnx")) || File.Exists(Path.Combine(current, "global.json")))
            {
                return Path.Combine(current, relativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }
        return Path.GetFullPath(relativePath);
    }
}
