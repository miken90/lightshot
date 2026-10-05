# scripts/release.ps1
# Build, package, sign manifest, and release Lightshot.
# Targets Windows PowerShell 5.1 (ASCII only, no PS7 syntax).

param(
    [string]$Version = "0.1.0",
    [long]$Sequence = 1,
    [switch]$DryRun,
    [switch]$Publish,
    [string]$KeyPath = "$env:USERPROFILE\.lightshot-release\update-signing-key",
    [int]$MaxSetupMB = 113
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")

$exitCode = 0

try {
    Write-Log "Initializing Lightshot release pipeline (Version: $Version, Sequence: $Sequence)..."
    Update-EnvironmentPath
    $dotnetExe = Get-DotnetExecutable

    if (-not $DryRun -and -not $Publish) {
        throw "Must specify either -DryRun or -Publish."
    }
    if ($DryRun -and $Publish) {
        throw "Cannot specify both -DryRun and -Publish simultaneously."
    }

    # 1. Key configuration
    $tempKeyDir = $null
    $activeKeyPath = $null

    if ($DryRun) {
        Write-Log "Operating in -DryRun mode: generating temporary throwaway key in temp..."
        $tempKeyDir = Join-Path $env:TEMP ("lightshot-dryrun-key-" + [guid]::NewGuid().ToString("N"))
        New-Item -Path $tempKeyDir -ItemType Directory -Force | Out-Null
        $activeKeyPath = Join-Path $tempKeyDir "throwaway-key"

        $keygenScript = Join-Path $RepoRoot "scripts\keygen.ps1"
        & $keygenScript -KeyPath $activeKeyPath -Force | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to generate throwaway key for dry run."
        }
    } else {
        Write-Log "Operating in -Publish mode: using official signing key at '$KeyPath'..."
        if (-not (Test-Path $KeyPath)) {
            throw "Official signing key not found at '$KeyPath'. Run keygen.ps1 first."
        }
        $activeKeyPath = $KeyPath
    }

    # 2. Build and package application
    Write-Log "Invoking package.ps1 with MaxSetupMB=$MaxSetupMB..."
    $packageScript = Join-Path $RepoRoot "scripts\package.ps1"
    & $packageScript -Version $Version -MaxSetupMB $MaxSetupMB
    if ($LASTEXITCODE -ne 0) {
        throw "package.ps1 failed with exit code $LASTEXITCODE"
    }

    $releaseDir = Join-Path $RepoRoot "artifacts\release"
    if (-not (Test-Path $releaseDir)) {
        throw "Release directory '$releaseDir' not found."
    }

    # 3. Locate Setup executable and verify size
    $setupFile = Get-ChildItem -Path $releaseDir -Filter "*Setup.exe" | Select-Object -First 1
    if (-not $setupFile) {
        $setupFile = Get-ChildItem -Path $releaseDir -Filter "*.exe" | Select-Object -First 1
    }
    if (-not $setupFile) {
        throw "No Setup executable found in '$releaseDir'."
    }

    $setupSizeMB = [math]::Round($setupFile.Length / 1MB, 2)
    Write-Log "Measured Setup executable size: ${setupSizeMB} MB (Size ceiling: ${MaxSetupMB} MB)"
    if ($setupSizeMB -gt $MaxSetupMB) {
        throw "Setup executable size (${setupSizeMB} MB) exceeds maximum allowed limit (${MaxSetupMB} MB)."
    }

    # 4. Generate portable zip archive if publish payload exists
    $publishDir = Join-Path $RepoRoot "artifacts\publish\app"
    $portableZip = Join-Path $releaseDir "Lightshot-win-Portable.zip"
    if (Test-Path $publishDir) {
        Write-Log "Creating portable zip archive '$portableZip'..."
        if (Test-Path $portableZip) {
            Remove-Item -Path $portableZip -Force
        }
        Compress-Archive -Path "$publishDir\*" -DestinationPath $portableZip -Force
    }

    # 5. Generate and sign manifest via ReleaseTool
    Write-Log "Generating and signing update manifest via Lightshot.ReleaseTool..."
    $manifestFile = Join-Path $releaseDir "releases.json"
    $sigFile = Join-Path $releaseDir "releases.json.sig"
    $releaseToolProj = Join-Path $RepoRoot "tools\Lightshot.ReleaseTool\Lightshot.ReleaseTool.csproj"

    $signArgs = @(
        "run",
        "--project", $releaseToolProj,
        "--",
        "sign-manifest",
        "--version", $Version,
        "--sequence", $Sequence,
        "--package", $setupFile.FullName,
        "--key", $activeKeyPath,
        "--out-manifest", $manifestFile,
        "--out-sig", $sigFile,
        "--notes-url", "https://github.com/miken90/lightshot/releases/tag/v$Version"
    )

    & $dotnetExe @signArgs
    if ($LASTEXITCODE -ne 0) {
        throw "ReleaseTool sign-manifest failed with exit code $LASTEXITCODE"
    }

    # 6. Generate SHA256SUMS.txt
    Write-Log "Generating SHA256SUMS.txt..."
    $sumsFile = Join-Path $releaseDir "SHA256SUMS.txt"
    $filesToHash = Get-ChildItem -Path $releaseDir -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" }

    $sumLines = @()
    foreach ($file in $filesToHash) {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        $stream = [System.IO.File]::OpenRead($file.FullName)
        $hashBytes = $sha.ComputeHash($stream)
        $stream.Close()
        $sha.Dispose()

        $hex = ""
        foreach ($b in $hashBytes) {
            $hex += "{0:x2}" -f $b
        }
        $sumLines += "$hex  $($file.Name)"
    }
    [System.IO.File]::WriteAllLines($sumsFile, $sumLines)

    # 7. Clean up temporary throwaway key
    if ($tempKeyDir -and (Test-Path $tempKeyDir)) {
        Remove-Item -Path $tempKeyDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    # 8. Report release assets
    Write-Host ""
    Write-Host "=================== Release Artifacts ==================="
    Write-Host "  Setup Executable : $($setupFile.Name) (${setupSizeMB} MB)"
    if (Test-Path $portableZip) {
        $zipMB = [math]::Round((Get-Item $portableZip).Length / 1MB, 2)
        Write-Host "  Portable Zip     : Lightshot-win-Portable.zip (${zipMB} MB)"
    }
    Write-Host "  Manifest         : releases.json"
    Write-Host "  Signature        : releases.json.sig"
    Write-Host "  Checksums        : SHA256SUMS.txt"
    Write-Host "========================================================="
    Write-Host ""

    if ($DryRun) {
        Write-Log "Dry-run release verification completed successfully. No publish action taken."
    } elseif ($Publish) {
        Write-Log "Publishing to GitHub release v$Version..."
        $ghExe = Get-Command "gh" -ErrorAction SilentlyContinue
        if (-not $ghExe) {
            throw "GitHub CLI ('gh') is required for -Publish but was not found in PATH."
        }

        $assets = @(
            $setupFile.FullName,
            $portableZip,
            $manifestFile,
            $sigFile,
            $sumsFile
        )
        $nupkg = Get-ChildItem -Path $releaseDir -Filter "*.nupkg" | Select-Object -First 1
        if ($nupkg) {
            $assets += $nupkg.FullName
        }

        $ghArgs = @(
            "release", "create", "v$Version",
            "--repo", "miken90/lightshot",
            "--title", "v$Version",
            "--notes", "Release v$Version"
        )
        foreach ($a in $assets) {
            if (Test-Path $a) {
                $ghArgs += $a
            }
        }

        Write-Log "Executing: gh $($ghArgs -join ' ')..."
        & $ghExe.Source @ghArgs
        if ($LASTEXITCODE -ne 0) {
            throw "gh release create failed with exit code $LASTEXITCODE"
        }
        Write-Log "Release published successfully."
    }
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}

exit $exitCode
