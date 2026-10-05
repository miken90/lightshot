# scripts/check-core.ps1
# Core purity litmus and architectural constraints validation.
# Targets Windows PowerShell 5.1 (ASCII only).

param()

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")

$exitCode = 0

try {
    Write-Log "Checking Core and Rendering purity constraints..."

    # 1. Verify NOTICE file exists
    $noticePath = Join-Path $RepoRoot "NOTICE"
    if (-not (Test-Path $noticePath)) {
        throw "NOTICE file is missing at repo root: $noticePath"
    }
    Write-Log "NOTICE file verified."

    # 2. Check Lightshot.Core.csproj
    $coreProjPath = Join-Path $RepoRoot "src\Lightshot.Core\Lightshot.Core.csproj"
    if (-not (Test-Path $coreProjPath)) {
        throw "Lightshot.Core.csproj not found at $coreProjPath"
    }

    [xml]$coreXml = Get-Content $coreProjPath
    $tfm = $coreXml.Project.PropertyGroup.TargetFramework
    if ($tfm -ne "net10.0") {
        throw "Lightshot.Core TargetFramework must be exactly 'net10.0', found '$tfm'"
    }

    $packageRefs = $coreXml.SelectNodes("//PackageReference")
    if ($packageRefs.Count -gt 0) {
        throw "Lightshot.Core must have 0 PackageReference items, found $($packageRefs.Count)"
    }

    $projectRefs = $coreXml.SelectNodes("//ProjectReference")
    if ($projectRefs.Count -gt 0) {
        throw "Lightshot.Core must have 0 ProjectReference items, found $($projectRefs.Count)"
    }

    # 3. Source scan Lightshot.Core
    $coreFiles = Get-ChildItem -Path (Join-Path $RepoRoot "src\Lightshot.Core") -Filter "*.cs" -Recurse
    $forbiddenCorePatterns = @("System\.Windows", "Windows\.", "Microsoft\.Win32", "System\.Drawing", "DllImport", "LibraryImport")

    foreach ($file in $coreFiles) {
        $content = Get-Content -Path $file.FullName
        $lineNum = 0
        foreach ($line in $content) {
            $lineNum++
            foreach ($pattern in $forbiddenCorePatterns) {
                if ($line -match $pattern) {
                    throw "Purity violation in $($file.FullName) line $($lineNum): matches '$pattern'"
                }
            }
        }
    }
    Write-Log "Lightshot.Core passed purity check (0 packages, 0 project refs, pure net10.0)."

    # 4. Check Lightshot.Rendering.csproj
    $rendProjPath = Join-Path $RepoRoot "src\Lightshot.Rendering\Lightshot.Rendering.csproj"
    if (-not (Test-Path $rendProjPath)) {
        throw "Lightshot.Rendering.csproj not found at $rendProjPath"
    }

    [xml]$rendXml = Get-Content $rendProjPath
    $rendTfm = $rendXml.Project.PropertyGroup.TargetFramework
    if ($rendTfm -ne "net10.0") {
        throw "Lightshot.Rendering TargetFramework must be exactly 'net10.0', found '$rendTfm'"
    }

    $rendPackageNodes = $rendXml.SelectNodes("//PackageReference")
    $rendPackages = @()
    foreach ($node in $rendPackageNodes) {
        $inc = $node.GetAttribute("Include")
        if ($inc) {
            $rendPackages += $inc
        }
    }
    $rendPackages = $rendPackages | Sort-Object
    $expectedPackages = @("HarfBuzzSharp", "SkiaSharp")
    $diff = Compare-Object $rendPackages $expectedPackages
    if ($diff) {
        throw "Lightshot.Rendering packages must be exactly HarfBuzzSharp and SkiaSharp, found: $($rendPackages -join ', ')"
    }

    # 5. Source scan Lightshot.Rendering for Windows references
    $rendFiles = Get-ChildItem -Path (Join-Path $RepoRoot "src\Lightshot.Rendering") -Filter "*.cs" -Recurse
    $forbiddenRendPatterns = @("System\.Windows", "Windows\.")

    foreach ($file in $rendFiles) {
        $content = Get-Content -Path $file.FullName
        $lineNum = 0
        foreach ($line in $content) {
            $lineNum++
            foreach ($pattern in $forbiddenRendPatterns) {
                if ($line -match $pattern) {
                    throw "Purity violation in $($file.FullName) line $($lineNum): matches '$pattern'"
                }
            }
        }
    }
    Write-Log "Lightshot.Rendering passed purity check (net10.0, HarfBuzzSharp + SkiaSharp only, no Windows refs)."
    Write-Log "check-core completed successfully."
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}

exit $exitCode
