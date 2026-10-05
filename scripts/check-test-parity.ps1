# scripts/check-test-parity.ps1
# Compares C# test method names against docs/porting/test-manifest.json.
# Targets Windows PowerShell 5.1 (ASCII only).

param()

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")

$exitCode = 0

try {
    Write-Log "Checking test parity against docs/porting/test-manifest.json..."

    $manifestPath = Join-Path $RepoRoot "docs\porting\test-manifest.json"
    if (-not (Test-Path $manifestPath)) {
        throw "Test manifest not found at $manifestPath"
    }

    $rawJson = Get-Content -Path $manifestPath -Raw
    $manifest = ConvertFrom-Json $rawJson

    # 1. Verify status totals add up to 595
    $totalPorted = 0
    $totalDeferred = 0
    $totalPendingC2 = 0
    $totalPendingR = 0
    $totalOther = 0

    $manifestPortedByClass = @{}

    $classes = $manifest.classes.psobject.Properties
    foreach ($cProp in $classes) {
        $className = $cProp.Name
        $classData = $cProp.Value
        $tests = $classData.tests

        if (-not $manifestPortedByClass.ContainsKey($className)) {
            $manifestPortedByClass[$className] = @{}
        }

        foreach ($t in $tests) {
            $status = $t.status
            if ($status -eq "ported") {
                $totalPorted++
                $manifestPortedByClass[$className][$t.csharpMethod] = $t.swiftMethod
            } elseif ($status -eq "deferred") {
                $totalDeferred++
            } elseif ($status -eq "pending: C2") {
                $totalPendingC2++
            } elseif ($status -eq "pending: PackageR") {
                $totalPendingR++
            } else {
                $totalOther++
            }
        }
    }

    $totalManifest = $totalPorted + $totalDeferred + $totalPendingC2 + $totalPendingR + $totalOther
    Write-Log "Manifest totals: Ported=$totalPorted, PendingC2=$totalPendingC2, PendingR=$totalPendingR, Deferred=$totalDeferred, Other=$totalOther (Total: $totalManifest)"

    if ($totalManifest -ne 595) {
        throw "Status totals in manifest do not add up to 595 (found $totalManifest)"
    }

    # Extract allowed new tests
    $allowedNewByClass = @{}
    if ($manifest.new) {
        foreach ($n in $manifest.new) {
            $cName = $n.className
            if (-not $allowedNewByClass.ContainsKey($cName)) {
                $allowedNewByClass[$cName] = @()
            }
            $allowedNewByClass[$cName] += $n.csharpMethod
        }
    }

    # 2. Scan C# test files
    $testDirs = @(
        (Join-Path $RepoRoot "tests\Lightshot.Core.Tests"),
        (Join-Path $RepoRoot "tests\Lightshot.Rendering.Tests")
    )

    $errors = @()
    $warningsNoAssert = @()
    $csharpTestsByClass = @{}

    foreach ($dir in $testDirs) {
        if (-not (Test-Path $dir)) { continue }
        $csFiles = Get-ChildItem -Path $dir -Filter "*.cs" -Recurse | Where-Object { $_.FullName -notmatch "[\\/]obj[\\/]" -and $_.FullName -notmatch "[\\/]bin[\\/]" }

        foreach ($file in $csFiles) {
            $lines = Get-Content -Path $file.FullName
            $currentClass = $null

            for ($i = 0; $i -lt $lines.Count; $i++) {
                $line = $lines[$i].Trim()

                # Check for class declaration
                if ($line -match "public\s+(?:sealed\s+)?class\s+([A-Za-z0-9_]+)") {
                    $currentClass = $matches[1]
                    if (-not $csharpTestsByClass.ContainsKey($currentClass)) {
                        $csharpTestsByClass[$currentClass] = @()
                    }
                }

                # Check for Skip = on any attribute line
                if ($line -match "\[(Fact|Theory).*[,\(]\s*Skip\s*=") {
                    $errors += "Banned 'Skip =' found in $($file.Name) at line $($i + 1): $line"
                }

                # Check for [Fact] or [Theory]
                if ($line -match "^\[(Fact|Theory)") {
                    # Look ahead for method name
                    $methodName = $null
                    for ($j = $i + 1; $j -lt [Math]::Min($lines.Count, $i + 6); $j++) {
                        $subLine = $lines[$j].Trim()
                        if ($subLine -match "public\s+(?:async\s+)?(?:void|Task)\s+([A-Za-z0-9_]+)\s*\(") {
                            $methodName = $matches[1]
                            break
                        }
                    }

                    if ($methodName -and $currentClass) {
                        $csharpTestsByClass[$currentClass] += $methodName

                        # Scan method body for Assert or Throws
                        $hasAssert = $false
                        $braceCount = 0
                        $started = $false
                        for ($k = $j; $k -lt [Math]::Min($lines.Count, $j + 200); $k++) {
                            $bodyLine = $lines[$k]
                            if ($bodyLine.Contains("{")) {
                                $braceCount++
                                $started = $true
                            }
                            if ($bodyLine.Contains("}")) {
                                $braceCount--
                            }

                            if ($bodyLine -match "Assert\." -or $bodyLine -match "Throws" -or $bodyLine -match "Should") {
                                $hasAssert = $true
                            }

                            if ($started -and $braceCount -le 0) {
                                break
                            }
                        }

                        if (-not $hasAssert) {
                            $warningsNoAssert += "$currentClass.$methodName (in $($file.Name))"
                        }
                    }
                }
            }
        }
    }

    # 3. Check every manifest ported method exists in C#
    $missingPorted = @()
    foreach ($cName in $manifestPortedByClass.Keys) {
        $portedMethods = $manifestPortedByClass[$cName]
        if ($portedMethods.Count -eq 0) { continue }

        $csharpList = @()
        if ($csharpTestsByClass.ContainsKey($cName)) {
            $csharpList = $csharpTestsByClass[$cName]
        }

        foreach ($mName in $portedMethods.Keys) {
            if ($csharpList -notcontains $mName) {
                $missingPorted += "$cName.$mName"
            }
        }
    }

    if ($missingPorted.Count -gt 0) {
        $errors += "Manifest ported methods missing in C# ($($missingPorted.Count)): " + ($missingPorted -join ", ")
    }

    # 4. Check every C# test is in manifest ported OR in new list
    $extraCSharp = @()
    foreach ($cName in $csharpTestsByClass.Keys) {
        # Exclude smoke tests from upstream parity checks
        if ($cName -eq "CoreSmokeTests" -or $cName -eq "RenderingSmokeTests") {
            continue
        }

        $csharpMethods = $csharpTestsByClass[$cName]
        $allowedManifest = @()
        if ($manifestPortedByClass.ContainsKey($cName)) {
            $allowedManifest = [array]$manifestPortedByClass[$cName].Keys
        }
        $allowedNew = @()
        if ($allowedNewByClass.ContainsKey($cName)) {
            $allowedNew = [array]$allowedNewByClass[$cName]
        }

        foreach ($m in $csharpMethods) {
            if (($allowedManifest -notcontains $m) -and ($allowedNew -notcontains $m)) {
                $extraCSharp += "$cName.$m"
            }
        }
    }

    if ($extraCSharp.Count -gt 0) {
        $errors += "C# tests neither in manifest ported nor in new list ($($extraCSharp.Count)): " + ($extraCSharp -join ", ")
    }

    # 5. Output Report
    Write-Log "--- Test Parity Check Report ---"
    Write-Log "Ported tests in manifest: $totalPorted"
    Write-Log "Tests with no Assert/Throws detected ($($warningsNoAssert.Count)):"
    foreach ($w in $warningsNoAssert) {
        Write-Log "  [WARN NO-ASSERT] $w" "WARN"
    }

    if ($errors.Count -gt 0) {
        Write-Log "Parity check failed with $($errors.Count) error(s):" "ERROR"
        foreach ($err in $errors) {
            Write-Log "  $err" "ERROR"
        }
        $exitCode = 1
    } else {
        Write-Log "Test parity check completed successfully: all ported and new methods matched exactly!"
    }
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}

exit $exitCode
