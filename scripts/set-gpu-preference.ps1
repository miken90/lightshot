# scripts/set-gpu-preference.ps1
# Sets HKCU\Software\Microsoft\DirectX\UserGpuPreferences for an executable to iGPU, dGPU or SystemDefault.
# Targets Windows PowerShell 5.1 (ASCII only).

param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [ValidateSet("iGPU", "dGPU", "SystemDefault", "Reset")]
    [string]$Preference = "SystemDefault",
    [switch]$Reset
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")

$exitCode = 0

try {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $regKey = "HKCU:\Software\Microsoft\DirectX\UserGpuPreferences"

    if (-not (Test-Path $regKey)) {
        New-Item -Path $regKey -Force | Out-Null
    }

    if ($Reset -or $Preference -eq "Reset") {
        Write-Log "Resetting GPU preference for $fullPath..."
        Remove-ItemProperty -Path $regKey -Name $fullPath -ErrorAction SilentlyContinue
        Write-Log "GPU preference reset (removed) for $fullPath"
    } elseif ($Preference -eq "iGPU") {
        $val = "GpuPreference=1;"
        Set-ItemProperty -Path $regKey -Name $fullPath -Value $val -Type String
        Write-Log "GPU preference set to iGPU ($val) for $fullPath"
    } elseif ($Preference -eq "dGPU") {
        $val = "GpuPreference=2;"
        Set-ItemProperty -Path $regKey -Name $fullPath -Value $val -Type String
        Write-Log "GPU preference set to dGPU ($val) for $fullPath"
    } else {
        $val = "GpuPreference=0;"
        Set-ItemProperty -Path $regKey -Name $fullPath -Value $val -Type String
        Write-Log "GPU preference set to SystemDefault ($val) for $fullPath"
    }
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}

exit $exitCode
