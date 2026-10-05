# scripts/keygen.ps1
# Generate ECDSA P-256 release signing keypair.
# Targets Windows PowerShell 5.1 (ASCII only, no PS7 syntax).

param(
    [string]$KeyPath = "$env:USERPROFILE\.lightshot-release\update-signing-key",
    [switch]$Force
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")

$exitCode = 0

try {
    Write-Log "Initializing Lightshot keygen..."
    Update-EnvironmentPath
    $dotnetExe = Get-DotnetExecutable

    # 1. Refuse if key file already exists unless -Force
    if ((Test-Path $KeyPath) -and (-not $Force)) {
        throw "Signing key already exists at '$KeyPath'. Re-running keygen would orphan existing pinned keys. Refusing to overwrite."
    }

    # 2. Ensure parent directory exists
    $keyDir = Split-Path -Parent $KeyPath
    if (-not (Test-Path $keyDir)) {
        New-Item -Path $keyDir -ItemType Directory -Force | Out-Null
    }

    # 3. Invoke ReleaseTool keygen (does in-memory self-test sign+verify before writing)
    $releaseToolProj = Join-Path $RepoRoot "tools\Lightshot.ReleaseTool\Lightshot.ReleaseTool.csproj"
    $keygenArgs = @(
        "run",
        "--project", $releaseToolProj,
        "--",
        "keygen",
        "--out", $KeyPath
    )
    if ($Force) {
        $keygenArgs += "--force"
    }

    $output = & $dotnetExe @keygenArgs
    if ($LASTEXITCODE -ne 0) {
        throw "ReleaseTool keygen failed with exit code $LASTEXITCODE"
    }

    # 4. Restrict private key permissions to CurrentUser only
    Write-Log "Securing key file permissions with current-user-only ACL..."
    & icacls "$KeyPath" /inheritance:r /grant:r "$($env:USERNAME):(R,W)" | Out-Null

    # 5. Output only public key and fingerprint (never print or inspect private key)
    Write-Host ""
    Write-Host "=================== Lightshot Keygen ==================="
    foreach ($line in $output) {
        if ($line -like "PublicKey:*" -or $line -like "Fingerprint:*" -or $line -like "PrivateKeyPath:*") {
            Write-Host "  $line"
        }
    }
    Write-Host "========================================================"
    Write-Host ""
    Write-Log "Keypair generation completed successfully."
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}

exit $exitCode
