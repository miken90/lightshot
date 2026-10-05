# scripts/run.ps1
# Build and run Lightshot detached, or cleanly stop a running instance.
# Targets Windows PowerShell 5.1 (ASCII only).

param(
    [string]$Configuration = "Debug",
    [switch]$Stop
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")

$exitCode = 0

try {
    Update-EnvironmentPath
    $artifactsDir = Join-Path $RepoRoot "artifacts"
    $pidFile = Join-Path $artifactsDir "run.pid"

    if ($Stop) {
        Write-Log "Stopping Lightshot..."
        $targetPid = $null

        if (Test-Path $pidFile) {
            $lines = Get-Content $pidFile -ErrorAction SilentlyContinue
            if ($lines -and $lines.Count -gt 0) {
                $parsedPid = 0
                if ([int]::TryParse($lines[0].Trim(), [ref]$parsedPid)) {
                    $targetPid = $parsedPid
                }
            }
        }

        if (-not $targetPid) {
            $procs = Get-Process -Name "Lightshot.App" -ErrorAction SilentlyContinue
            if ($procs) {
                $targetPid = $procs[0].Id
            }
        }

        # 1. Signal named event Local\Lightshot.Quit
        $signaled = $false
        try {
            $quitEvent = [System.Threading.EventWaitHandle]::OpenExisting("Local\Lightshot.Quit")
            $quitEvent.Set() | Out-Null
            $quitEvent.Dispose()
            $signaled = $true
            Write-Log "Signaled named event 'Local\Lightshot.Quit'."
        }
        catch {
            Write-Log "Named event 'Local\Lightshot.Quit' not found." "DEBUG"
        }

        # 2. Wait up to 5 seconds if PID is known
        if ($targetPid) {
            $proc = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
            if ($proc) {
                Write-Log "Waiting up to 5 seconds for process $targetPid to exit gracefully..."
                $timeoutMs = 5000
                $waitedMs = 0
                while (-not $proc.HasExited -and $waitedMs -lt $timeoutMs) {
                    Start-Sleep -Milliseconds 200
                    $waitedMs += 200
                    $proc.Refresh()
                }

                if (-not $proc.HasExited) {
                    Write-Log "Process $targetPid did not exit within 5 seconds. Stopping forcefully..." "WARN"
                    Stop-Process -Id $targetPid -Force -ErrorAction SilentlyContinue
                    Write-Log "Process $targetPid terminated forcefully."
                } else {
                    Write-Log "Process $targetPid exited cleanly."
                }
            } else {
                Write-Log "Process $targetPid has already exited."
            }
        } else {
            Write-Log "No running Lightshot process detected."
        }

        # 3. Clean up PID file
        if (Test-Path $pidFile) {
            Remove-Item -Path $pidFile -Force -ErrorAction SilentlyContinue
        }

        Write-Log "Stop operation completed."
        exit 0
    }

    # Normal launch flow
    Write-Log "Checking for existing Lightshot instance..."
    $alreadyRunningPid = $null

    # Check named mutex
    $mutexRunning = $false
    try {
        $mutex = [System.Threading.Mutex]::OpenExisting("Local\Lightshot.SingleInstance")
        $mutex.Dispose()
        $mutexRunning = $true
    } catch {
        $mutexRunning = $false
    }

    # Check pid file
    if (Test-Path $pidFile) {
        $lines = Get-Content $pidFile -ErrorAction SilentlyContinue
        if ($lines -and $lines.Count -gt 0) {
            $candidatePid = 0
            if ([int]::TryParse($lines[0].Trim(), [ref]$candidatePid)) {
                $candidateProc = Get-Process -Id $candidatePid -ErrorAction SilentlyContinue
                if ($candidateProc -and -not $candidateProc.HasExited) {
                    $alreadyRunningPid = $candidatePid
                }
            }
        }
    }

    if (-not $alreadyRunningPid -and $mutexRunning) {
        $runningProcs = Get-Process -Name "Lightshot.App" -ErrorAction SilentlyContinue
        if ($runningProcs) {
            $alreadyRunningPid = $runningProcs[0].Id
        }
    }

    if ($alreadyRunningPid) {
        Write-Log "Lightshot is already running (PID: $alreadyRunningPid). Refusing to start a second instance." "WARN"
        exit 1
    }

    # Build solution
    Write-Log "Building solution before launch (Configuration: $Configuration)..."
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ScriptDir "build.ps1") -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "build.ps1 failed with exit code $LASTEXITCODE"
    }

    $exePath = Join-Path $RepoRoot "src\Lightshot.App\bin\$Configuration\net10.0-windows10.0.22621.0\Lightshot.App.exe"
    if (-not (Test-Path $exePath)) {
        throw "Application executable not found at '$exePath'."
    }

    Write-Log "Starting Lightshot detached ($exePath)..."
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exePath
    $psi.WorkingDirectory = (Split-Path -Parent $exePath)
    $psi.UseShellExecute = $false

    $dotnetRoot = $env:DOTNET_ROOT
    if (-not $dotnetRoot) {
        $dotnetRoot = Join-Path $env:LocalAppData "Microsoft\dotnet"
    }
    $psi.EnvironmentVariables["DOTNET_ROOT"] = $dotnetRoot

    $proc = [System.Diagnostics.Process]::Start($psi)
    if (-not $proc) {
        throw "Failed to start process '$exePath'."
    }

    Start-Sleep -Milliseconds 500
    if ($proc.HasExited) {
        throw "Lightshot exited immediately with exit code $($proc.ExitCode)."
    }

    if (-not (Test-Path $artifactsDir)) {
        New-Item -Path $artifactsDir -ItemType Directory -Force | Out-Null
    }

    $pidContent = "$($proc.Id)`r`n$exePath"
    [System.IO.File]::WriteAllText($pidFile, $pidContent)
    Write-Log "Lightshot started successfully (PID: $($proc.Id)). Recorded to $pidFile"
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}

exit $exitCode
