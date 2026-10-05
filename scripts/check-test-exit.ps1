# scripts/check-test-exit.ps1
# Canary exit-code verification script.
# Asserts ExitCodeCanaryTests passes with 0 when unset, and fails with non-zero when LIGHTSHOT_CANARY_FAIL=1.
# Targets Windows PowerShell 5.1 (ASCII only).

param()

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptDir "_common.ps1")

$exitCode = 0

try {
    Write-Log "Running check-test-exit self-check..."
    $dotnetExe = Get-DotnetExecutable
    $testProj = Join-Path $RepoRoot "tests\Lightshot.Architecture.Tests\Lightshot.Architecture.Tests.csproj"

    # 1. Unset canary -> must exit 0
    Write-Log "1. Verifying canary test passes with exit code 0 when LIGHTSHOT_CANARY_FAIL is unset..."
    $env:LIGHTSHOT_CANARY_FAIL = $null
    & $dotnetExe test --project $testProj --filter "FailsWhenCanaryEnabled"
    $passCode = $LASTEXITCODE
    if ($passCode -ne 0) {
        throw "Expected exit code 0 when canary is unset, but got $passCode"
    }
    Write-Log "Pass case verified (exit code: 0)."

    # 2. Set canary=1 -> must exit non-zero
    Write-Log "2. Verifying canary test fails with non-zero exit code when LIGHTSHOT_CANARY_FAIL=1..."
    $env:LIGHTSHOT_CANARY_FAIL = "1"
    & $dotnetExe test --project $testProj --filter "FailsWhenCanaryEnabled"
    $failCode = $LASTEXITCODE
    $env:LIGHTSHOT_CANARY_FAIL = $null

    if ($failCode -eq 0) {
        throw "Expected non-zero exit code when LIGHTSHOT_CANARY_FAIL=1, but got 0"
    }
    Write-Log "Failure case verified (exit code: $failCode)."

    Write-Log "check-test-exit self-check passed: exit code reporting is proven reliable."
}
catch {
    Write-Log $_.Exception.Message "ERROR"
    $exitCode = 1
}
finally {
    $env:LIGHTSHOT_CANARY_FAIL = $null
}

exit $exitCode
