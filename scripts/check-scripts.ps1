# scripts/check-scripts.ps1
# Enforce ASCII-only encoding and Windows PowerShell 5.1 syntax on all scripts.
# Targets Windows PowerShell 5.1 (ASCII only).

param()

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")

$exitCode = 0

try {
    Write-Log "Checking all scripts for ASCII encoding and PS 5.1 compatibility..."
    $scripts = Get-ChildItem -Path (Join-Path $RepoRoot "scripts") -Filter "*.ps1" -Recurse

    if ($scripts.Count -eq 0) {
        throw "No scripts found under scripts/ directory."
    }

    foreach ($script in $scripts) {
        $path = $script.FullName
        $fileName = $script.Name

        # 1. ASCII byte check
        $bytes = [System.IO.File]::ReadAllBytes($path)
        for ($i = 0; $i -lt $bytes.Length; $i++) {
            $b = $bytes[$i]
            if ($b -gt 127) {
                throw "Non-ASCII byte 0x$([Convert]::ToString($b, 16).ToUpper()) in $fileName at offset $i"
            }
        }

        # 2. Check for banned PS7 operators: &&, ||
        $content = [System.IO.File]::ReadAllText($path)
        if ($fileName -ne "check-scripts.ps1") {
            $andOp = [string][char]38 + [string][char]38
            $orOp = [string][char]124 + [string][char]124
            if ($content.Contains($andOp)) {
                throw "Banned PS7 operator '&&' detected in $fileName"
            }
            if ($content.Contains($orOp)) {
                throw "Banned PS7 operator '||' detected in $fileName"
            }
        }

        # 3. PS 5.1 parser syntax validation
        $parseErrors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$parseErrors)
        if ($parseErrors -and $parseErrors.Count -gt 0) {
            $msg = ($parseErrors | ForEach-Object { $_.Message }) -join "; "
            throw "PowerShell 5.1 parse errors in $($fileName): $msg"
        }

        Write-Log "  [OK] $fileName (ASCII, PS5.1 syntax valid)"
    }

    Write-Log "All $($scripts.Count) scripts passed ASCII and PS 5.1 validation."
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}

exit $exitCode
