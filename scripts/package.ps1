# scripts/package.ps1
# Publish self-contained Lightshot win-x64 app and build unsigned Velopack installer.
# Targets Windows PowerShell 5.1 (ASCII only).

param(
    [string]$Version = "0.1.0",
    [switch]$SkipVpk,
    [int]$MaxSetupMB = 113
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")

$exitCode = 0

try {
    Write-Log "Initializing package pipeline (Version: $Version, MaxSetupMB: $MaxSetupMB)..."
    Update-EnvironmentPath
    $dotnetExe = Get-DotnetExecutable

    $publishDir = Join-Path $RepoRoot "artifacts\publish\app"
    $releaseDir = Join-Path $RepoRoot "artifacts\release"
    $appCsProj = Join-Path $RepoRoot "src\Lightshot.App\Lightshot.App.csproj"

    if (-not (Test-Path $appCsProj)) {
        throw "Lightshot.App project not found at '$appCsProj'."
    }

    # 1. Publish self-contained application
    Write-Log "Publishing self-contained win-x64 app to '$publishDir'..."
    if (Test-Path $publishDir) {
        Remove-Item -Path $publishDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -Path $publishDir -ItemType Directory -Force | Out-Null

    $publishArgs = @(
        "publish",
        $appCsProj,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:PublishReadyToRun=true",
        "-o", $publishDir
    )

    Write-Log "Executing: dotnet $($publishArgs -join ' ')..."
    & $dotnetExe @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    # 2. Package with Velopack unless -SkipVpk is specified
    if (-not $SkipVpk) {
        Write-Log "Packaging release with Velopack (vpk)..."
        if (-not (Test-Path $releaseDir)) {
            New-Item -Path $releaseDir -ItemType Directory -Force | Out-Null
        }

        $mainExeName = "Lightshot.App.exe"
        $vpkArgs = @(
            "vpk", "pack",
            "--packId", "Lightshot",
            "--packVersion", $Version,
            "--packDir", $publishDir,
            "--packAuthors", "Lightshot",
            "--mainExe", $mainExeName,
            "--runtime", "win-x64",
            "-o", $releaseDir
        )

        Write-Log "Executing: dotnet $($vpkArgs -join ' ')..."
        & $dotnetExe @vpkArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet vpk pack failed with exit code $LASTEXITCODE"
        }
    } else {
        Write-Log "Skipping Velopack packaging (-SkipVpk specified)."
    }

    # 3. Report file sizes and enforce MaxSetupMB ceiling
    Write-Host ""
    Write-Host "================ Package Size Report ================"
    if (-not $SkipVpk) {
        $releaseFiles = Get-ChildItem -Path $releaseDir -File
        if ($releaseFiles) {
            foreach ($file in $releaseFiles) {
                $sizeMB = [math]::Round($file.Length / 1MB, 2)
                Write-Host ("  {0,-35} : {1,8} MB" -f $file.Name, $sizeMB)

                if ($file.Name -like "*Setup.exe" -or $file.Name -like "*.exe") {
                    if ($sizeMB -gt $MaxSetupMB) {
                        throw "Package file '$($file.Name)' size (${sizeMB}MB) exceeds maximum allowed limit (${MaxSetupMB}MB)."
                    }
                }
            }
        } else {
            Write-Log "No release files found in '$releaseDir'." "WARN"
        }
    } else {
        $publishedFiles = Get-ChildItem -Path $publishDir -Recurse -File
        $totalBytes = 0
        foreach ($f in $publishedFiles) {
            $totalBytes += $f.Length
        }
        $totalMB = [math]::Round($totalBytes / 1MB, 2)
        Write-Host ("  Published app payload total : {0,8} MB" -f $totalMB)
    }
    Write-Host "====================================================="
    Write-Host ""
    Write-Log "Package pipeline completed successfully."
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}

exit $exitCode
