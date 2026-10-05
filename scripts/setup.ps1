# scripts/setup.ps1
# Setup prerequisites, per-user .NET 10 SDK, and restore local tools.
# Bootstrap invocation (one-time if execution policy blocks scripts):
# powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'D:\WORKSPACES\PERSONAL\lightshot\scripts\setup.ps1'
# Manual machine-wide alternative:
# winget install --id Microsoft.DotNet.SDK.10 -e --silent --accept-package-agreements --accept-source-agreements

param()

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")

$exitCode = 0

try {
    Write-Log "Checking execution policy for CurrentUser..."
    $currentPolicy = Get-ExecutionPolicy -Scope CurrentUser
    if ($currentPolicy -eq "Restricted" -or $currentPolicy -eq "Undefined") {
        Write-Log "ExecutionPolicy (CurrentUser) is '$currentPolicy'. Setting to 'RemoteSigned' (per-user, no elevation)..."
        Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force -ErrorAction SilentlyContinue
        Write-Log "ExecutionPolicy (CurrentUser) updated to RemoteSigned."
    } else {
        Write-Log "ExecutionPolicy (CurrentUser) is '$currentPolicy' (OK)."
    }

    Update-EnvironmentPath
    $dotnetExe = Get-DotnetExecutable
    $hasSdk10 = $false

    if (Test-Path $dotnetExe) {
        $sdkList = & $dotnetExe --list-sdks 2>&1
        foreach ($line in $sdkList) {
            if ($line -match "^10\.") {
                $hasSdk10 = $true
                break
            }
        }
    }

    if (-not $hasSdk10) {
        $installDir = Join-Path $env:LocalAppData "Microsoft\dotnet"
        Write-Log ".NET 10 SDK not detected. Installing per-user into '$installDir'..."
        if (-not (Test-Path $installDir)) {
            New-Item -Path $installDir -ItemType Directory -Force | Out-Null
        }

        $installerScript = Join-Path $env:TEMP "dotnet-install.ps1"
        Write-Log "Downloading dotnet-install.ps1 from dot.net..."
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $webClient = New-Object Net.WebClient
        $webClient.DownloadFile("https://dot.net/v1/dotnet-install.ps1", $installerScript)

        Write-Log "Executing dotnet-install.ps1 -Channel 10.0 -Architecture x64..."
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerScript -Channel 10.0 -InstallDir $installDir -Architecture x64
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet-install.ps1 failed with exit code $LASTEXITCODE"
        }

        Write-Log "Configuring user environment variables DOTNET_ROOT and PATH..."
        [System.Environment]::SetEnvironmentVariable("DOTNET_ROOT", $installDir, "User")
        $env:DOTNET_ROOT = $installDir

        $currentUserPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
        if (-not $currentUserPath) {
            $currentUserPath = ""
        }
        if ($currentUserPath -notlike "*$installDir*") {
            $newUserPath = "$installDir;$currentUserPath".TrimEnd(';')
            [System.Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
        }

        Update-EnvironmentPath
        $dotnetExe = Get-DotnetExecutable
    }

    Write-Log "Verifying .NET SDKs with $dotnetExe..."
    $sdks = & $dotnetExe --list-sdks 2>&1
    $foundSdk10 = $false
    Write-Host "Installed SDKs:"
    foreach ($sdk in $sdks) {
        Write-Host "  $sdk"
        if ($sdk -match "^10\.") {
            $foundSdk10 = $true
        }
    }

    if (-not $foundSdk10) {
        throw "Missing .NET 10 SDK after installation step."
    }

    $toolsManifest = Join-Path $RepoRoot ".config\dotnet-tools.json"
    if (Test-Path $toolsManifest) {
        Write-Log "Restoring local dotnet tools..."
        Push-Location $RepoRoot
        try {
            & $dotnetExe tool restore
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet tool restore failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            Pop-Location
        }
    }

    $fetchModelsScript = Join-Path $ScriptDir "fetch-models.ps1"
    if (Test-Path $fetchModelsScript) {
        Write-Log "Fetching model assets..."
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $fetchModelsScript
        if ($LASTEXITCODE -ne 0) {
            throw "fetch-models.ps1 failed with exit code $LASTEXITCODE"
        }
    }

    Write-Host ""
    Write-Host "================ Host and Tool Versions ================"
    Write-Host "OS Build:        $([Environment]::OSVersion.Version.ToString())"
    $gitVer = & git --version 2>&1
    Write-Host "Git:             $gitVer"
    $dotnetVer = & $dotnetExe --version 2>&1
    Write-Host "Dotnet SDK:      $dotnetVer"
    $wingetCmd = Get-Command "winget" -ErrorAction SilentlyContinue
    if ($wingetCmd) {
        $wingetVer = & winget --version 2>&1
        Write-Host "Winget:          $wingetVer"
    }
    Write-Host "========================================================"
    Write-Log "Setup completed successfully."
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}

exit $exitCode
