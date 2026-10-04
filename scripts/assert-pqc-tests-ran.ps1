# ==============================================================================
# Symbolon PQC Test Execution Gating Script (PowerShell / Windows) (§10.10)
#
# Asserts that Post-Quantum Cryptography (FIPS 203 ML-KEM, FIPS 204 ML-DSA,
# FIPS 205 SLH-DSA) test cases were actively executed and NOT silently skipped.
# ==============================================================================

[CmdletBinding()]
param(
    [string]$LogDir = "./TestResults"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Symbolon PQC Test Coverage Assertion ===" -ForegroundColor Cyan

if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

$trxFiles = Get-ChildItem -Path $LogDir -Filter "*.trx" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
$trxFile = $null

if ($trxFiles.Count -eq 0) {
    Write-Host "Running Symbolon.Crypto.Tests to verify PQC coverage..." -ForegroundColor Yellow
    dotnet test tests/Symbolon.Crypto.Tests/Symbolon.Crypto.Tests.csproj `
        --configuration Release `
        --logger "trx;LogFileName=pqc-verification.trx" `
        --results-directory $LogDir `
        --verbosity normal
    $trxFile = Join-Path $LogDir "pqc-verification.trx"
} else {
    $trxFile = $trxFiles[0].FullName
}

if (-not (Test-Path $trxFile)) {
    Write-Error "ERROR: Test results file '$trxFile' not found!"
    exit 1
}

Write-Host "Analyzing test results in: $trxFile" -ForegroundColor Gray

[xml]$trxContent = Get-Content -Path $trxFile -Raw
$unitTestResults = $trxContent.TestRun.Results.UnitTestResult

$mlDsaPassed = ($unitTestResults | Where-Object { $_.testName -match "MlDsa" -and $_.outcome -eq "Passed" }).Count
$mlKemPassed = ($unitTestResults | Where-Object { $_.testName -match "MlKem" -and $_.outcome -eq "Passed" }).Count
$slhDsaPassed = ($unitTestResults | Where-Object { $_.testName -match "SlhDsa" -and $_.outcome -eq "Passed" }).Count
$skippedTests = ($unitTestResults | Where-Object { $_.outcome -eq "NotExecuted" }).Count

$totalPqc = $mlDsaPassed + $mlKemPassed + $slhDsaPassed

Write-Host "PQC Test Summary:" -ForegroundColor White
Write-Host " - ML-DSA (FIPS 204) Passed:  $mlDsaPassed" -ForegroundColor Green
Write-Host " - ML-KEM (FIPS 203) Passed:  $mlKemPassed" -ForegroundColor Green
Write-Host " - SLH-DSA (FIPS 205) Passed: $slhDsaPassed" -ForegroundColor Green
Write-Host " - Total PQC Tests Passed:    $totalPqc" -ForegroundColor Cyan
Write-Host " - Skipped Tests:             $skippedTests" -ForegroundColor $(if ($skippedTests -gt 0) { "Red" } else { "Green" })

if ($totalPqc -eq 0) {
    Write-Error "CRITICAL SECURITY ERROR: Zero Post-Quantum Cryptography tests were executed! Environment may be missing PQC primitives."
    exit 1
}

if ($skippedTests -gt 0) {
    Write-Error "WARNING / REJECTION: $skippedTests tests were skipped! PQC tests must NEVER be silently skipped."
    exit 1
}

Write-Host "✔ SUCCESS: PQC test coverage assertion passed ($totalPqc tests executed, 0 skipped)." -ForegroundColor Green
exit 0
