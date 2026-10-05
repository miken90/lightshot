# scripts/_common.ps1
# Shared helpers for Lightshot Windows build and test scripts.
# Targets Windows PowerShell 5.1 (ASCII only, no PS7 syntax).

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Update-EnvironmentPath {
    $machinePath = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
    if ($userPath) {
        $env:Path = "$userPath;$machinePath"
    } else {
        $env:Path = $machinePath
    }

    $userDotnetRoot = [System.Environment]::GetEnvironmentVariable("DOTNET_ROOT", "User")
    if ($userDotnetRoot) {
        $env:DOTNET_ROOT = $userDotnetRoot
    } elseif (Test-Path "$env:LocalAppData\Microsoft\dotnet") {
        $env:DOTNET_ROOT = "$env:LocalAppData\Microsoft\dotnet"
    }
}

function Write-Log {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Message,
        [string]$Level = "INFO"
    )
    Write-Host "[$Level] $Message"
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory=$true)]
        [scriptblock]$ScriptBlock,
        [string]$ErrorMessage = "Command execution failed"
    )
    & $ScriptBlock
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        throw "$ErrorMessage (exit code: $code)"
    }
}

function Get-DotnetExecutable {
    Update-EnvironmentPath
    if ($env:DOTNET_ROOT) {
        $candidate = Join-Path $env:DOTNET_ROOT "dotnet.exe"
        if (Test-Path $candidate) {
            return $candidate
        }
    }
    $cmd = Get-Command "dotnet" -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }
    return "dotnet.exe"
}
