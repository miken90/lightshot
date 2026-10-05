# scripts/fetch-models.ps1
# Download and verify pinned AI/ML model assets against assets/models.lock.json.
# Targets Windows PowerShell 5.1 (ASCII only).

param(
    [switch]$Force
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")

$exitCode = 0

try {
    Write-Log "Checking model assets against lockfile..."
    $lockFilePath = Join-Path $RepoRoot "assets\models.lock.json"

    if (-not (Test-Path $lockFilePath)) {
        throw "Model lockfile not found at '$lockFilePath'."
    }

    $lockJsonContent = Get-Content $lockFilePath -Raw -Encoding ASCII
    $lockData = ConvertFrom-Json $lockJsonContent

    if (-not $lockData) {
        throw "Failed to parse model lockfile '$lockFilePath'."
    }

    $models = $lockData.models
    if (-not $models -or $models.Count -eq 0) {
        Write-Log "Model lockfile is valid: 0 models registered. No assets to fetch."
        exit 0
    }

    $modelsDir = Join-Path $RepoRoot "assets\models"
    if (-not (Test-Path $modelsDir)) {
        New-Item -Path $modelsDir -ItemType Directory -Force | Out-Null
    }

    foreach ($model in $models) {
        $modelName = $model.name
        $relPath = $model.path
        $targetPath = Join-Path $RepoRoot $relPath
        $expectedSha = $model.sha256
        $url = $model.url

        Write-Log "Processing model: $modelName -> $relPath"

        $needsDownload = $true
        if ((Test-Path $targetPath) -and (-not $Force)) {
            $currentHash = (Get-FileHash -Path $targetPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($currentHash -eq $expectedSha.ToLowerInvariant()) {
                Write-Log "Model '$modelName' matches expected SHA-256. Skipping download."
                $needsDownload = $false
            } else {
                Write-Log "Model '$modelName' hash mismatch (found $currentHash, expected $expectedSha). Redownloading..." "WARN"
            }
        }

        if ($needsDownload) {
            $destDir = Split-Path -Parent $targetPath
            if (-not (Test-Path $destDir)) {
                New-Item -Path $destDir -ItemType Directory -Force | Out-Null
            }

            $tempFile = "$targetPath.tmp"
            Write-Log "Downloading $url to $tempFile..."
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $webClient = New-Object Net.WebClient
            $webClient.DownloadFile($url, $tempFile)

            $downloadedHash = (Get-FileHash -Path $tempFile -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($downloadedHash -ne $expectedSha.ToLowerInvariant()) {
                Remove-Item -Path $tempFile -Force -ErrorAction SilentlyContinue
                throw "SHA-256 validation failed for '$modelName'. Expected $expectedSha, got $downloadedHash"
            }

            Move-Item -Path $tempFile -Destination $targetPath -Force
            Write-Log "Successfully downloaded and verified '$modelName'."
        }
    }

    Write-Log "All models verified successfully."
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}

exit $exitCode
