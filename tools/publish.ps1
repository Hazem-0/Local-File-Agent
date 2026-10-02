[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "dist\LocalFileAgent"
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDir)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " LocalFileAgent v2 - Packaging Self-Contained Desktop App " -ForegroundColor Cyan
Write-Host " Configuration: $Configuration | Runtime: $Runtime " -ForegroundColor Cyan
Write-Host " Output:        $resolvedOutput " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path $resolvedOutput)) {
    New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
}

Write-Host ""
Write-Host "[1/3] Publishing Out-of-Process Worker (LocalFileAgent.Worker)..." -ForegroundColor Yellow
$workerProj = Join-Path $repoRoot "src\LocalFileAgent.Worker\LocalFileAgent.Worker.csproj"
dotnet publish $workerProj -c $Configuration -r $Runtime --self-contained true -o $resolvedOutput
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish LocalFileAgent.Worker"
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "[2/3] Publishing WPF Desktop Application (LocalFileAgent.App)..." -ForegroundColor Yellow
$appProj = Join-Path $repoRoot "src\LocalFileAgent.App\LocalFileAgent.App.csproj"
dotnet publish $appProj -c $Configuration -r $Runtime --self-contained true -o $resolvedOutput
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish LocalFileAgent.App"
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "[3/3] Generating one-click launcher scripts..." -ForegroundColor Yellow

$batLauncher = Join-Path $resolvedOutput "Start-LocalFileAgent.bat"
$batLines = @(
    "@echo off",
    "title LocalFileAgent v2 (Arabic-First Desktop Search)",
    'cd /d "%~dp0"',
    'start "" "%~dp0LocalFileAgent.App.exe" %*'
)
[System.IO.File]::WriteAllLines($batLauncher, $batLines)

$psLauncher = Join-Path $resolvedOutput "Start-LocalFileAgent.ps1"
$psLines = @(
    '$appPath = Join-Path $PSScriptRoot "LocalFileAgent.App.exe"',
    'if (Test-Path $appPath) {',
    '    Start-Process -FilePath $appPath -WorkingDirectory $PSScriptRoot',
    '} else {',
    '    Write-Error "Executable not found at $appPath"',
    '}'
)
[System.IO.File]::WriteAllLines($psLauncher, $psLines)

$allFiles = Get-ChildItem -Path $resolvedOutput -Recurse -File
$totalBytes = ($allFiles | Measure-Object -Property Length -Sum).Sum
$totalSizeMB = [math]::Round(($totalBytes / 1MB), 2)
$exeCount = ($allFiles | Where-Object { $_.Extension -eq ".exe" }).Count

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host " RELEASE PACKAGING SUCCESSFUL!" -ForegroundColor Green
Write-Host " Package Location : $resolvedOutput" -ForegroundColor Green
Write-Host " Package Size     : $totalSizeMB MB" -ForegroundColor Green
Write-Host " Executables      : $exeCount found" -ForegroundColor Green
Write-Host " Batch Launcher   : $batLauncher" -ForegroundColor Green
Write-Host " PowerShell Run   : $psLauncher" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
