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
        "-p:Version=$Version",
        "-o", $publishDir
    )

    Write-Log "Executing: dotnet $($publishArgs -join ' ')..."
    & $dotnetExe @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    # Ship the hub contract next to the exe: the my-utilities hub reads it to decide whether it can
    # run this build in hub mode. Version and exe hash are only known here.
    $contractSrc = Join-Path $RepoRoot "hub-contract.json"
    $exePublished = Join-Path $publishDir "Lightshot.App.exe"
    if (-not (Test-Path $contractSrc)) {
        throw "hub-contract.json not found at '$contractSrc'."
    }
    if (-not (Test-Path $exePublished)) {
        throw "Published exe not found at '$exePublished'."
    }
    $contract = Get-Content -Path $contractSrc -Raw | ConvertFrom-Json
    $exeSha256 = (Get-FileHash -Path $exePublished -Algorithm SHA256).Hash.ToLower()
    $contract | Add-Member -NotePropertyName "version" -NotePropertyValue $Version -Force
    $contract | Add-Member -NotePropertyName "exeSha256" -NotePropertyValue $exeSha256 -Force
    $contractJson = $contract | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText((Join-Path $publishDir "hub-contract.json"), $contractJson, (New-Object Text.UTF8Encoding $false))
    Write-Log "Wrote hub-contract.json (version $Version, exe sha256 $exeSha256)."

    # 2. Package with Velopack unless -SkipVpk is specified
    if (-not $SkipVpk) {
        Write-Log "Packaging release with Velopack (vpk)..."
        # vpk refuses to pack a version already present in the output folder, so a rerun
        # (release dry run, repeated package) starts from an empty generated directory.
        if (Test-Path $releaseDir) {
            Remove-Item -Path $releaseDir -Recurse -Force
        }
        if (-not (Test-Path $releaseDir)) {
            New-Item -Path $releaseDir -ItemType Directory -Force | Out-Null
        }

        $mainExeName = "Lightshot.App.exe"
        $iconPath = Join-Path $RepoRoot "src\Lightshot.App\Resources\Icons\app.ico"
        # The packId names the install root %LocalAppData%\<packId>, which Velopack deletes on uninstall,
        # so it must differ from AppPaths.LocalData (%LocalAppData%\Lightshot), and it must equal
        # UpdateStager.VelopackPackageId (InstallerSmokeTests.DryRunSucceeds checks it against vpk's own releases.win.json).
        $vpkArgs = @(
            "vpk", "pack",
            "--packId", "LightshotApp",
            "--packTitle", "Lightshot",
            "--packVersion", $Version,
            "--packDir", $publishDir,
            "--packAuthors", "Lightshot",
            "--mainExe", $mainExeName,
            "--icon", $iconPath,
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
