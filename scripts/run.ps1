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
    $exePath = Join-Path $RepoRoot "src\Lightshot.App\bin\$Configuration\net10.0-windows10.0.22621.0\Lightshot.App.exe"

    # Only the build output of this repo counts as ours: an installed Lightshot shares the
    # process name and the unscoped quit event, and must never be quit or killed from here.
    function Get-DevProcess {
        param([string]$ExePath, [string]$PidFile)
        $candidates = @()
        if (Test-Path $PidFile) {
            $lines = Get-Content $PidFile -ErrorAction SilentlyContinue
            $parsed = 0
            if ($lines -and [int]::TryParse(([string]$lines[0]).Trim(), [ref]$parsed)) {
                $p = Get-Process -Id $parsed -ErrorAction SilentlyContinue
                if ($p) { $candidates += $p }
            }
        }
        $candidates += @(Get-Process -Name "Lightshot.App" -ErrorAction SilentlyContinue)
        foreach ($p in $candidates) {
            $path = $null
            try { $path = $p.Path } catch { $path = $null }
            if ($path -and ([string]::Compare([System.IO.Path]::GetFullPath($path), [System.IO.Path]::GetFullPath($ExePath), $true) -eq 0)) {
                return $p
            }
        }
        return $null
    }

    if ($Stop) {
        Write-Log "Stopping Lightshot..."
        $dev = Get-DevProcess $exePath $pidFile
        if (-not $dev) {
            Write-Log "No dev instance of Lightshot is running."
            if (Test-Path $pidFile) {
                Remove-Item -Path $pidFile -Force -ErrorAction SilentlyContinue
            }
            exit 0
        }

        $targetPid = $dev.Id

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

    # Check named mutex
    $mutexRunning = $false
    try {
        $mutex = [System.Threading.Mutex]::OpenExisting("Local\Lightshot.SingleInstance")
        $mutex.Dispose()
        $mutexRunning = $true
    } catch {
        $mutexRunning = $false
    }

    $dev = Get-DevProcess $exePath $pidFile
    if ($dev) {
        Write-Log "Lightshot dev build is already running (PID: $($dev.Id)). Refusing to start a second instance." "WARN"
        exit 1
    }

    if ($mutexRunning) {
        Write-Log "Another Lightshot (probably the installed app) is running. Quit it from its tray icon first." "WARN"
        exit 1
    }

    # Build solution
    Write-Log "Building solution before launch (Configuration: $Configuration)..."
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ScriptDir "build.ps1") -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "build.ps1 failed with exit code $LASTEXITCODE"
    }

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
