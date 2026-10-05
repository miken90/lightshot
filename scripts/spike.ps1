# scripts/spike.ps1
# Builds and runs a Lightshot spike probe, collecting its JSON result.
# Targets Windows PowerShell 5.1 (ASCII only).

param(
    [Parameter(Mandatory = $true)]
    [string]$Name,
    [string]$Configuration = "Debug",
    [switch]$Assert,
    [switch]$NoBuild,
    [string[]]$ExtraArgs = @()
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")

$exitCode = 0

try {
    Update-EnvironmentPath
    $dotnetExe = Get-DotnetExecutable

    # Normalize probe name
    $probeName = $Name.ToLowerInvariant().Trim()
    if ($probeName -eq "a" -or $probeName -eq "capture") {
        $probeName = "capture-probe"
    } elseif ($probeName -eq "d" -or $probeName -eq "overlay") {
        $probeName = "overlay-probe"
    } elseif ($probeName -eq "b" -or $probeName -eq "recording") {
        $probeName = "recording-probe"
    } elseif ($probeName -eq "c" -or $probeName -eq "compositor") {
        $probeName = "compositor-probe"
    } elseif ($probeName -eq "e" -or $probeName -eq "ml") {
        $probeName = "ml-probe"
    }

    $probeDir = Join-Path $RepoRoot "spikes\$probeName"
    $projPath = Join-Path $probeDir "$probeName.csproj"

    if (-not (Test-Path $projPath)) {
        throw "Probe project not found at $projPath"
    }

    # 1. Build probe
    if (-not $NoBuild) {
        Write-Log "Building $probeName ($Configuration)..."
        & $dotnetExe build $projPath -c $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to build $probeName (exit code $LASTEXITCODE)"
        }
        Write-Log "Build succeeded."
    }

    # 2. Locate built executable
    $binDir = Join-Path $probeDir "bin\$Configuration\net10.0-windows10.0.22621.0"
    $exePath = Join-Path $binDir "$probeName.exe"
    if (-not (Test-Path $exePath)) {
        $binDir = Join-Path $probeDir "bin\$Configuration\net10.0"
        $exePath = Join-Path $binDir "$probeName.exe"
    }
    if (-not (Test-Path $exePath)) {
        throw "Probe executable not found at $exePath"
    }

    # 3. Assemble arguments
    $argsList = @()
    if ($Assert) {
        $argsList += "--assert"
    }
    if ($ExtraArgs) {
        $argsList += $ExtraArgs
    }

    Write-Log "Executing probe: $exePath $($argsList -join ' ')..."

    # 4. Run probe directly to avoid stream redirection deadlocks
    $dllPath = [System.IO.Path]::ChangeExtension($exePath, ".dll")
    if (Test-Path $dllPath) {
        & $dotnetExe exec $dllPath $argsList
    } else {
        & $exePath $argsList
    }
    $procExit = $LASTEXITCODE

    if ($procExit -ne 0) {
        throw "Probe $probeName exited with non-zero exit code: $procExit"
    }

    Write-Log "$probeName executed successfully."
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}

exit $exitCode
