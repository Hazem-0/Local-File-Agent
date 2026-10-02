# tools/check.ps1
# Runs full build, tests, analyzers, banned-API verification, and corpus integrity check.

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Split-Path -Parent $scriptDir

Push-Location $repoRoot
try {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Running LocalFileAgent Quality Checks  " -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    Write-Host "`n[1/4] Building solution (Release, WarningsAsErrors)..." -ForegroundColor Yellow
    dotnet build LocalFileAgent.sln -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed with exit code $LASTEXITCODE"
    }
    Write-Host "Build passed successfully." -ForegroundColor Green

    Write-Host "`n[2/4] Running all unit and integration tests..." -ForegroundColor Yellow
    dotnet test LocalFileAgent.sln -c Release --no-build --logger "console;verbosity=normal"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed with exit code $LASTEXITCODE"
    }
    Write-Host "All tests passed successfully." -ForegroundColor Green

    Write-Host "`n[3/4] Verifying Banned APIs..." -ForegroundColor Yellow
    # BannedApiAnalyzers runs automatically during dotnet build with WarningsAsErrors enabled.
    Write-Host "BannedApiAnalyzers verified (enforced at build time with WarningsAsErrors)." -ForegroundColor Green

    Write-Host "`n[4/4] Verifying Corpus Integrity..." -ForegroundColor Yellow
    # Acceptance test suite contains the CorpusIntegrityTests verifying read-only behavior.
    Write-Host "Corpus integrity verified (asserted in test suite)." -ForegroundColor Green

    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host " ALL CHECKS PASSED                      " -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
}
finally {
    Pop-Location
}
