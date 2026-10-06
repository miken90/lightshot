# scripts/build.ps1
# Build Lightshot solution with pre-flight purity and script checks.
# Targets Windows PowerShell 5.1 (ASCII only).

param(
    [string]$Configuration = "Debug",
    [switch]$NoRestore,
    [switch]$SelfCheck
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")

$exitCode = 0

try {
    Write-Log "Starting Lightshot build pipeline (Configuration: $Configuration)..."
    Update-EnvironmentPath
    $dotnetExe = Get-DotnetExecutable

    # 1. Run Core and Rendering purity litmus
    Write-Log "Step 1/4: Running check-core.ps1..."
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ScriptDir "check-core.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "check-core.ps1 failed with exit code $LASTEXITCODE"
    }

    # 2. Run script ASCII and syntax check
    Write-Log "Step 2/4: Running check-scripts.ps1..."
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ScriptDir "check-scripts.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "check-scripts.ps1 failed with exit code $LASTEXITCODE"
    }

    # 3. Build solution
    Write-Log "Step 3/4: Building Lightshot.slnx..."
    $slnxPath = Join-Path $RepoRoot "Lightshot.slnx"
    $buildArgs = @("build", $slnxPath, "-c", $Configuration)
    if ($NoRestore) {
        $buildArgs += "--no-restore"
    }

    & $dotnetExe @buildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }
    Write-Log "Build succeeded."

    # 4. Build the recording probe helper. AudioSyncTests (Media tier) launches it from its bin folder.
    Write-Log "Step 4/4: Building spikes\recording-probe..."
    $probeProj = Join-Path $RepoRoot "spikes\recording-probe\recording-probe.csproj"
    & $dotnetExe build $probeProj -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "recording-probe build failed with exit code $LASTEXITCODE"
    }

    # 5. Optional SelfCheck
    if ($SelfCheck) {
        Write-Log "Running self-check via check-test-exit.ps1..."
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ScriptDir "check-test-exit.ps1")
        if ($LASTEXITCODE -ne 0) {
            throw "check-test-exit.ps1 failed with exit code $LASTEXITCODE"
        }
        Write-Log "SelfCheck completed successfully."
    }

    Write-Log "build.ps1 finished successfully."
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}

exit $exitCode
