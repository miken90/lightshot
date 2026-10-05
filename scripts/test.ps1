# scripts/test.ps1
# Test runner for Lightshot test suites categorized by tier.
# Targets Windows PowerShell 5.1 (ASCII only).

param(
    [string[]]$Tier = @("Unit", "Render"),
    [string]$Filter,
    [switch]$NoBuild,
    [string]$Configuration = "Debug",
    [switch]$UpdateGoldens
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")
Set-Location $RepoRoot

$exitCode = 0
$powerRequestHandle = [IntPtr]::Zero

try {
    Write-Log "Initializing Lightshot test runner..."
    Update-EnvironmentPath
    $dotnetExe = Get-DotnetExecutable

    # Resolve and normalize requested tiers
    $resolvedTiers = @()
    foreach ($entry in $Tier) {
        if ($entry -and $entry.Contains(",")) {
            $parts = $entry.Split(",")
            foreach ($p in $parts) {
                $trimmed = $p.Trim()
                if ($trimmed) {
                    $resolvedTiers += $trimmed
                }
            }
        } elseif ($entry) {
            $trimmed = $entry.Trim()
            if ($trimmed) {
                $resolvedTiers += $trimmed
            }
        }
    }

    if ($resolvedTiers.Count -eq 0) {
        $resolvedTiers = @("Unit", "Render")
    }

    $uniqueTiers = @()
    foreach ($t in $resolvedTiers) {
        if ($uniqueTiers -notcontains $t) {
            $uniqueTiers += $t
        }
    }

    # 1. Validate -UpdateGoldens
    if ($UpdateGoldens) {
        $isOnlyRender = ($uniqueTiers.Count -eq 1 -and $uniqueTiers[0] -eq "Render")
        if (-not $isOnlyRender) {
            throw "-UpdateGoldens is only valid when -Tier is exactly 'Render'."
        }
        $env:LIGHTSHOT_UPDATE_GOLDENS = "1"
        Write-Log "LIGHTSHOT_UPDATE_GOLDENS=1 set. REMINDER: Review git diffs of golden images before committing!" "WARN"
    }

    # 2. Check if Media or Desktop tier is requested
    $hasDesktopOrMedia = $false
    foreach ($t in $uniqueTiers) {
        if ($t -eq "Desktop" -or $t -eq "Media" -or $t -eq "All") {
            $hasDesktopOrMedia = $true
            break
        }
    }

    if ($hasDesktopOrMedia) {
        Write-Log "Media/Desktop tier selected. Validating interactive Windows session..."

        # Verify not in Session 0
        $currentSessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
        if ($currentSessionId -eq 0) {
            throw "Media and Desktop test tiers cannot run in non-interactive Session 0."
        }

        # Initialize P/Invoke helpers for session and power management
        $typeDef = @"
using System;
using System.Runtime.InteropServices;
public static class LightshotSessionHelper {
    [DllImport("kernel32.dll")]
    public static extern uint WTSGetActiveConsoleSessionId();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct REASON_CONTEXT {
        public uint Version;
        public uint Flags;
        public string SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT context);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PowerSetRequest(IntPtr handle, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PowerClearRequest(IntPtr handle, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);
}
"@
        if (-not ([System.Management.Automation.PSTypeName]"LightshotSessionHelper").Type) {
            Add-Type -TypeDefinition $typeDef
        }

        $activeConsole = [LightshotSessionHelper]::WTSGetActiveConsoleSessionId()
        if ($activeConsole -eq 0xFFFFFFFF) {
            throw "No active console session found. Desktop tests require an active logon session."
        }

        Write-Log "Active console session confirmed (Session ID: $activeConsole)."

        # Acquire display-required power request
        Write-Log "Acquiring display-required power request..."
        $ctx = New-Object LightshotSessionHelper+REASON_CONTEXT
        $ctx.Version = 0
        $ctx.Flags = 1
        $ctx.SimpleReasonString = "Lightshot Test Run"
        $powerRequestHandle = [LightshotSessionHelper]::PowerCreateRequest([ref]$ctx)
        if ($powerRequestHandle -ne [IntPtr]::Zero -and $powerRequestHandle -ne (New-Object IntPtr(-1))) {
            $setRes = [LightshotSessionHelper]::PowerSetRequest($powerRequestHandle, 0)
            if ($setRes) {
                Write-Log "Display-required power request active."
            }
        }
    }

    # 3. Build unless -NoBuild specified
    if (-not $NoBuild) {
        Write-Log "Building solution before running tests (Configuration: $Configuration)..."
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ScriptDir "build.ps1") -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "build.ps1 failed with exit code $LASTEXITCODE"
        }
    }

    # 4. Prepare test results directory
    $resultsDir = Join-Path $RepoRoot "artifacts\test-results"
    if (-not (Test-Path $resultsDir)) {
        New-Item -Path $resultsDir -ItemType Directory -Force | Out-Null
    }

    # 5. Execute test tiers
    $isAll = ($uniqueTiers -contains "All")
    $tiersToExecute = if ($isAll) { @("All") } else { $uniqueTiers }

    $slnxPath = Join-Path $RepoRoot "Lightshot.slnx"
    $overallTotal = 0
    $overallPassed = 0
    $overallFailed = 0

    foreach ($t in $tiersToExecute) {
        Write-Log "Running test tier '$t'..."

        $tierResultsDir = Join-Path $resultsDir $t
        if (Test-Path $tierResultsDir) {
            Remove-Item -Path $tierResultsDir -Recurse -Force
        }
        New-Item -Path $tierResultsDir -ItemType Directory -Force | Out-Null

        $testArgs = @("test", "--solution", $slnxPath, "-c", $Configuration, "--no-build")
        $testArgs += @("--results-directory", $tierResultsDir, "--report-xunit-trx")
        $testArgs += @("--ignore-exit-code", "8")

        if ($Filter) {
            if ($isAll) {
                $testArgs += @("--filter", $Filter)
            } else {
                $testArgs += @("--filter", "($Filter) & (Tier=$t)")
            }
        } else {
            if (-not $isAll) {
                $testArgs += @("--filter-trait", "Tier=$t")
            }
        }

        if ($t -eq "Desktop" -or $t -eq "Media") {
            $testArgs += @("--max-parallel-test-modules", "1", "--parallel", "none")
        }

        Write-Log "Executing: dotnet $($testArgs -join ' ')..."
        & $dotnetExe @testArgs
        $runExitCode = $LASTEXITCODE

        if ($runExitCode -ne 0) {
            throw "Test execution for tier '$t' failed with exit code $runExitCode"
        }

        $trxFiles = Get-ChildItem -Path $tierResultsDir -Filter "*.trx"
        $tierTotal = 0
        $tierPassed = 0
        $tierFailed = 0

        foreach ($file in $trxFiles) {
            $xml = [xml](Get-Content -Path $file.FullName)
            if ($xml.TestRun -and $xml.TestRun.ResultSummary -and $xml.TestRun.ResultSummary.Counters) {
                $counters = $xml.TestRun.ResultSummary.Counters
                $tierTotal += [int]$counters.total
                $tierPassed += [int]$counters.passed
                $tierFailed += [int]$counters.failed
            }
        }

        if ($tierTotal -eq 0) {
            throw "Tier '$t' matched zero tests. At least one test must run per requested tier."
        }

        if ($tierFailed -gt 0) {
            throw "Tier '$t' had $tierFailed failed test(s)."
        }

        Write-Log "Tier '$t' summary: Total=$tierTotal, Passed=$tierPassed, Failed=$tierFailed"
        $overallTotal += $tierTotal
        $overallPassed += $tierPassed
        $overallFailed += $tierFailed
    }

    Write-Log "All requested tiers passed ($overallTotal tests across $($tiersToExecute.Count) tier(s))."
    Write-Log "test.ps1 completed successfully. Results saved to artifacts/test-results/"
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}
finally {
    if ($powerRequestHandle -ne [IntPtr]::Zero -and $powerRequestHandle -ne (New-Object IntPtr(-1))) {
        Write-Log "Releasing display power request..."
        [LightshotSessionHelper]::PowerClearRequest($powerRequestHandle, 0) | Out-Null
        [LightshotSessionHelper]::CloseHandle($powerRequestHandle) | Out-Null
        $powerRequestHandle = [IntPtr]::Zero
    }

    if ($UpdateGoldens) {
        $env:LIGHTSHOT_UPDATE_GOLDENS = $null
    }
}

exit $exitCode
